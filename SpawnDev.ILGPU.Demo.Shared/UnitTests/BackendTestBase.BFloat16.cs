using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using BFloat16 = ILGPU.BFloat16;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Phase 0 of the BFloat16 (bfloat16 / "brain float") rollout: the kernel-native ILGPU.BFloat16
    // type + its IR primitive (BasicValueType.BFloat16). These tests verify the type end-to-end on the
    // CPU accelerator, which executes the managed BFloat16 struct directly (DefaultILBackend invokes the
    // original kernel method), so the struct IS the production code path here. GPU/transpiling backend
    // codegen (WebGPU, WebGL, Wasm, CUDA, OpenCL) is Phase 1+ per Plans/bfloat16-support-plan-2026-06-15.md;
    // these tests skip cleanly on those backends until their phase lands.
    //
    // bfloat16's headline property: it is the top 16 bits of an fp32 (1 sign / 8 exponent / 7 mantissa),
    // so it shares fp32's full dynamic range. The RangeAndSpecials test exercises exactly that — values
    // fp16 cannot hold (~1e30, ~1e-30) plus the ±Inf/NaN/zero/round-to-nearest-even edges.
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// bfloat16 codegen is implemented on all 6 backends: CPU (managed struct), WebGPU (WGSL),
        /// WebGL (GLSL), Wasm (WebAssembly bytecode) and OpenCL - all emulated via the same exact bit
        /// conversion - plus CUDA, which holds bf16 in an f32 register and converts at the load/store
        /// boundary via PTX cvt.f32.bf16 / cvt.rn.bf16.f32 (Ampere+ native). No backend is skipped.
        /// </summary>
        private static void RequireBFloat16SupportedBackend(Accelerator accelerator)
        {
            // BFloat16 is supported on every backend; nothing to gate.
        }

        /// <summary>
        /// Independent round-to-nearest-even fp32 -> bfloat16 conversion, computed from the raw float
        /// bits via the BCL (BitConverter), returning the widened fp32 of the resulting bfloat16. Used as
        /// a reference that does NOT call ILGPU.BFloat16 — so a typo/wrong-constant in the struct is caught.
        /// </summary>
        private static float RefRoundToBFloat16AsFloat(float f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(f);
            uint result;
            if ((bits & 0x7FFFFFFFu) > 0x7F800000u)         // NaN -> keep it NaN (force a mantissa bit)
                result = (bits >> 16) | 0x0040u;
            else
            {
                uint lsb = (bits >> 16) & 1u;
                result = (bits + 0x7FFFu + lsb) >> 16;        // RNE rounding bias, then truncate
            }
            return BitConverter.UInt32BitsToSingle(result << 16);
        }

        // ----- Kernels (static so they bind as ILGPU entry points) -----

        // All-constant bf16 arithmetic -> the IR constant-folds it at compile (both operands are
        // PrimitiveValues). Before the bf16 fold cases existed this THREW at IR construction.
        static void BFloat16ConstFoldKernel(Index1D i, ArrayView<BFloat16> result)
        {
            BFloat16 a = (BFloat16)6.0f;
            BFloat16 b = (BFloat16)2.0f;
            BFloat16 sum = a + b;    // 8
            BFloat16 dif = a - b;    // 4
            BFloat16 prod = a * b;   // 12
            BFloat16 quot = a / b;   // 3
            BFloat16 neg = -a;       // -6
            result[i.X] = sum + dif + prod + quot + neg; // 8+4+12+3-6 = 21
        }

        /// <summary>
        /// Verifies the IR constant-folds bf16 literal arithmetic (Neg/Add/Sub/Mul/Div). The kernel
        /// is entirely constant, so every op folds during IR construction - which THREW
        /// (NotSupportedException) before the BFloat16 fold cases were added. Compiling + running it
        /// to the correct result proves the fold path works rather than throwing.
        /// </summary>
        // Round-trips a float array through an ArrayView<BFloat16> and back: one AutoGrouped kernel
        // stores (BFloat16)src[i] into a bf16 buffer, another reads (float)bf16[i] into a FLOAT buffer.
        // The second kernel is the exact shape that mis-compiled on CUDA: the `(float)bf16` widening
        // Convert is a no-op alias, so a bf16-typed value reached the float-buffer store and the codegen
        // (keying bf16 packing off the value-register type, not the target buffer) narrowed it back to
        // bf16 + st.b16 (2 bytes) into the 4-byte float slot -> every float read back ~0. N=256 spans
        // multiple CUDA blocks. Reference is an independent BCL RNE round (no ILGPU.BFloat16), so a
        // codegen OR a struct bug is caught. Runs on every backend.
        static void BF16RoundTripStoreKernel(Index1D i, ArrayView<float> src, ArrayView<BFloat16> dst) => dst[i] = (BFloat16)src[i];
        static void BF16RoundTripLoadKernel(Index1D i, ArrayView<BFloat16> src, ArrayView<float> dst) => dst[i] = (float)src[i];

        [TestMethod]
        public async Task BFloat16_ArrayViewRoundTrip_MatchesReference() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);
            int n = 256;
            var input = new float[n];
            for (int i = 0; i < n; i++) input[i] = MathF.Sin(i) * 0.9f;
            using var sBuf = accelerator.Allocate1D(input);
            using var bBuf = accelerator.Allocate1D<BFloat16>(n);
            using var oBuf = accelerator.Allocate1D<float>(n);
            var kStore = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<BFloat16>>(BF16RoundTripStoreKernel);
            var kLoad = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<BFloat16>, ArrayView<float>>(BF16RoundTripLoadKernel);
            kStore(n, sBuf.View, bBuf.View);
            kLoad(n, bBuf.View, oBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await oBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                float expected = RefRoundToBFloat16AsFloat(input[i]);
                if (MathF.Abs(got[i] - expected) > 1e-3f)
                    throw new Exception(
                        $"bf16 ArrayView round-trip mismatch at [{i}]: in={input[i]} expected={expected} got={got[i]} " +
                        "(bf16 store/load codegen bug - value stored as bf16 into a float buffer?)");
            }
        });

        [TestMethod]
        public async Task BFloat16_ConstFold_Arithmetic() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);
            int n = 4;
            using var resultBuf = accelerator.Allocate1D<BFloat16>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<BFloat16>>(BFloat16ConstFoldKernel);
            kernel(n, resultBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await resultBuf.CopyToHostAsync<BFloat16>();
            for (int i = 0; i < n; i++)
                if (MathF.Abs((float)result[i] - 21f) > 0.01f)
                    throw new Exception($"bf16 const-fold arithmetic wrong at [{i}]: got {(float)result[i]} expected 21");
        });

        // bf16 less-than: writes 1 if a[i] < b[i] (as a bf16/float comparison), else 0.
        static void BFloat16LessThanKernel(
            Index1D i, ArrayView<BFloat16> a, ArrayView<BFloat16> b, ArrayView<int> lt)
            => lt[i.X] = (a[i.X] < b[i.X]) ? 1 : 0;

        /// <summary>
        /// Verifies the bf16 `&lt;` operator on the device orders by FLOAT magnitude, not raw int16
        /// bits. The key case is -1 vs -4: as a float, -1 &gt; -4 so (-1 &lt; -4) is FALSE; but if bf16
        /// were compared as its raw 16-bit pattern (-1 = 0xBF80 = -16512 &lt; -4 = 0xC080 = -16256),
        /// the answer flips to TRUE. Cross-checks every sign-spanning pair against the CPU float oracle.
        /// </summary>
        [TestMethod]
        public async Task BFloat16_LessThan_OrdersByFloatNotBits() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);
            float[] xs = { -1f, -4f, -1f, 1f, 3f, 2f, -1f, 0f, -4f, 4f };
            float[] ys = { -4f, -1f, 1f, -1f, 2f, 3f, -1f, -1f, -4f, -4f };
            int n = xs.Length;
            var a = new BFloat16[n];
            var b = new BFloat16[n];
            for (int i = 0; i < n; i++) { a[i] = (BFloat16)xs[i]; b[i] = (BFloat16)ys[i]; }

            using var aBuf = accelerator.Allocate1D(a);
            using var bBuf = accelerator.Allocate1D(b);
            using var ltBuf = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<BFloat16>, ArrayView<BFloat16>, ArrayView<int>>(BFloat16LessThanKernel);
            kernel(n, aBuf.View, bBuf.View, ltBuf.View);
            await accelerator.SynchronizeAsync();
            var gpu = await ltBuf.CopyToHostAsync<int>();

            for (int i = 0; i < n; i++)
            {
                int expected = (xs[i] < ys[i]) ? 1 : 0; // float-correct oracle
                if (gpu[i] != expected)
                    throw new Exception(
                        $"bf16 '<' wrong at ({xs[i]} < {ys[i]}): GPU={gpu[i]} expected={expected} " +
                        $"(GPU={(gpu[i] == 1 ? "true" : "false")}; if this sorts by raw int16 bits the sign flips)");
            }
        });

        static void BFloat16CopyKernel(Index1D i, ArrayView<BFloat16> input, ArrayView<BFloat16> output)
            => output[i] = input[i];

        static void BFloat16ArithmeticKernel(
            Index1D i,
            ArrayView<BFloat16> a,
            ArrayView<BFloat16> b,
            ArrayView<BFloat16> add,
            ArrayView<BFloat16> sub,
            ArrayView<BFloat16> mul,
            ArrayView<BFloat16> div)
        {
            BFloat16 x = a[i], y = b[i];
            add[i] = x + y;
            sub[i] = x - y;
            mul[i] = x * y;
            div[i] = x / y;
        }

        static void BFloat16MinMaxKernel(
            Index1D i,
            ArrayView<BFloat16> a,
            ArrayView<BFloat16> b,
            ArrayView<BFloat16> mins,
            ArrayView<BFloat16> maxs)
        {
            BFloat16 x = a[i], y = b[i];
            mins[i] = BFloat16.Min(x, y);
            maxs[i] = BFloat16.Max(x, y);
        }

        /// <summary>
        /// ArrayView&lt;BFloat16&gt; load/store must preserve the exact bit pattern through a kernel copy
        /// (the 2-byte sub-word storage path). Includes large/small/special values.
        /// </summary>
        [TestMethod]
        public async Task BFloat16_BufferRoundTrip() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);

            var floats = new[]
            {
                0f, 1f, -1f, 0.5f, -0.25f, 3.5f, 100f, -100f,
                1e30f, -1e30f, 1e-30f, 65504f * 4f /* > fp16 max */,
                float.PositiveInfinity, float.NegativeInfinity,
            };
            int n = floats.Length;
            var src = new BFloat16[n];
            for (int j = 0; j < n; j++) src[j] = (BFloat16)floats[j];

            using var input = accelerator.Allocate1D(src);
            using var output = accelerator.Allocate1D<BFloat16>(n);

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<BFloat16>, ArrayView<BFloat16>>(
                BFloat16CopyKernel);
            k((Index1D)n, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var got = await output.CopyToHostAsync<BFloat16>();
            for (int j = 0; j < n; j++)
            {
                // Round-trip is bit-exact: the widened fp32 must match exactly (storage must not corrupt).
                float expected = (float)src[j];
                float actual = (float)got[j];
                if (expected != actual)
                    throw new Exception(
                        $"BFloat16 round-trip corrupted at {j}: stored {expected} got {actual}.");
            }
        });

        /// <summary>
        /// bfloat16 arithmetic (+, -, *, /) computed through the FP32 path. Reference is the true f64
        /// result rounded to bfloat16 (independent RNE), compared bit-exactly against the kernel's widened
        /// output — a genuine cross-check of the production path, not an identity.
        /// </summary>
        [TestMethod]
        public async Task BFloat16_Arithmetic() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);

            // Conditioned inputs: no catastrophic cancellation, no divide-by-near-zero.
            var af = new[] { 1.5f, 8f, -3.25f, 100f, 0.5f, -7f, 40f, 2.5f };
            var bf = new[] { 2.5f, 3f, 4f, -8f, 0.25f, 2f, -5f, 6f };
            int n = af.Length;
            var a = new BFloat16[n];
            var b = new BFloat16[n];
            for (int j = 0; j < n; j++) { a[j] = (BFloat16)af[j]; b[j] = (BFloat16)bf[j]; }

            using var da = accelerator.Allocate1D(a);
            using var db = accelerator.Allocate1D(b);
            using var dAdd = accelerator.Allocate1D<BFloat16>(n);
            using var dSub = accelerator.Allocate1D<BFloat16>(n);
            using var dMul = accelerator.Allocate1D<BFloat16>(n);
            using var dDiv = accelerator.Allocate1D<BFloat16>(n);

            var k = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<BFloat16>, ArrayView<BFloat16>,
                ArrayView<BFloat16>, ArrayView<BFloat16>, ArrayView<BFloat16>, ArrayView<BFloat16>>(
                BFloat16ArithmeticKernel);
            k((Index1D)n, da.View, db.View, dAdd.View, dSub.View, dMul.View, dDiv.View);
            await accelerator.SynchronizeAsync();

            var add = await dAdd.CopyToHostAsync<BFloat16>();
            var sub = await dSub.CopyToHostAsync<BFloat16>();
            var mul = await dMul.CopyToHostAsync<BFloat16>();
            var div = await dDiv.CopyToHostAsync<BFloat16>();

            for (int j = 0; j < n; j++)
            {
                // Operands as stored (already bfloat16-rounded), then the exact op, then RNE to bfloat16.
                float x = (float)a[j], y = (float)b[j];
                Check("add", j, x + y, (float)add[j]);
                Check("sub", j, x - y, (float)sub[j]);
                Check("mul", j, x * y, (float)mul[j]);
                Check("div", j, x / y, (float)div[j]);
            }

            static void Check(string op, int j, float exactResult, float actual)
            {
                float expected = RefRoundToBFloat16AsFloat(exactResult);
                if (expected != actual)
                    throw new Exception(
                        $"BFloat16 {op} wrong at {j}: exact {exactResult}, expected (RNE) {expected}, got {actual}.");
            }
        });

        /// <summary>
        /// BFloat16.Min/Max return one of the inputs (no rounding), so the comparison is exact against
        /// the fp32 MathF reference.
        /// </summary>
        [TestMethod]
        public async Task BFloat16_MinMax() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);

            var af = new[] { 1.5f, -8f, 3.25f, 100f, -0.5f, 7f, -40f, 0f };
            var bf = new[] { 2.5f, 3f, -4f, -8f, 0.25f, 7f, 5f, -1f };
            int n = af.Length;
            var a = new BFloat16[n];
            var b = new BFloat16[n];
            for (int j = 0; j < n; j++) { a[j] = (BFloat16)af[j]; b[j] = (BFloat16)bf[j]; }

            using var da = accelerator.Allocate1D(a);
            using var db = accelerator.Allocate1D(b);
            using var dMin = accelerator.Allocate1D<BFloat16>(n);
            using var dMax = accelerator.Allocate1D<BFloat16>(n);

            var k = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<BFloat16>, ArrayView<BFloat16>, ArrayView<BFloat16>, ArrayView<BFloat16>>(
                BFloat16MinMaxKernel);
            k((Index1D)n, da.View, db.View, dMin.View, dMax.View);
            await accelerator.SynchronizeAsync();

            var mins = await dMin.CopyToHostAsync<BFloat16>();
            var maxs = await dMax.CopyToHostAsync<BFloat16>();

            for (int j = 0; j < n; j++)
            {
                float x = (float)a[j], y = (float)b[j];
                float expMin = MathF.Min(x, y), expMax = MathF.Max(x, y);
                if ((float)mins[j] != expMin)
                    throw new Exception($"BFloat16 Min wrong at {j}: expected {expMin} got {(float)mins[j]}.");
                if ((float)maxs[j] != expMax)
                    throw new Exception($"BFloat16 Max wrong at {j}: expected {expMax} got {(float)maxs[j]}.");
            }
        });

        /// <summary>
        /// bfloat16's defining property: fp32's dynamic range. Verifies large/small magnitudes fp16 cannot
        /// hold survive (do NOT collapse to Inf/0), the ±Inf/NaN/zero specials convert correctly, NaN is
        /// preserved (not turned into Inf), and round-to-nearest-even ties round to even. Also round-trips
        /// the values through a CPU kernel to tie conversion correctness to the buffer storage path.
        /// </summary>
        [TestMethod]
        public async Task BFloat16_RangeAndSpecials() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);

            // --- Range: values fp16 (max ~65504, min normal ~6.1e-5) cannot represent ---
            float big = 1e30f, tiny = 1e-30f;
            var bBig = (BFloat16)big;
            var bTiny = (BFloat16)tiny;
            if (!float.IsFinite((float)bBig))
                throw new Exception($"bfloat16 should hold 1e30 (fp16 overflows); got {(float)bBig}.");
            if (Math.Abs((float)bBig - big) > big / 128f)   // within one bfloat16 ulp
                throw new Exception($"bfloat16 1e30 out of range tolerance: got {(float)bBig}.");
            if ((float)bTiny == 0f || !float.IsFinite((float)bTiny))
                throw new Exception($"bfloat16 should hold 1e-30 (fp16 underflows); got {(float)bTiny}.");
            if (Math.Abs((float)bTiny - tiny) > tiny / 64f)
                throw new Exception($"bfloat16 1e-30 out of range tolerance: got {(float)bTiny}.");

            // --- Specials ---
            if (!BFloat16.IsPositiveInfinity((BFloat16)float.PositiveInfinity))
                throw new Exception("+Inf did not convert to bfloat16 +Inf.");
            if (!BFloat16.IsNegativeInfinity((BFloat16)float.NegativeInfinity))
                throw new Exception("-Inf did not convert to bfloat16 -Inf.");
            if (!BFloat16.IsZero((BFloat16)0f))
                throw new Exception("0 did not convert to bfloat16 zero.");
            // NaN preservation: a naive truncate would collapse some NaNs to Inf — must stay NaN.
            if (!BFloat16.IsNaN((BFloat16)float.NaN))
                throw new Exception("NaN did not survive fp32->bfloat16 conversion (collapsed to Inf?).");
            if (BFloat16.IsNaN((BFloat16)float.PositiveInfinity))
                throw new Exception("+Inf was wrongly classified as NaN.");

            // --- Round-to-nearest-even tie cases (cross-checked vs the independent reference) ---
            // A value exactly halfway between two bfloat16 values must round to the even neighbor.
            float[] rneProbes =
            {
                1.0f + (1f / 256f),   // halfway between 1.0 (even) and 1.0+1/128
                1.0f + (3f / 256f),   // halfway between 1.0+1/128 and 1.0+2/128 (even)
                255.5f, 256.5f, 0.1f, 1.0f / 3.0f, MathF.PI, -MathF.E,
            };
            foreach (var f in rneProbes)
            {
                float got = (float)(BFloat16)f;
                float reference = RefRoundToBFloat16AsFloat(f);
                if (got != reference)
                    throw new Exception(
                        $"bfloat16 RNE mismatch for {f}: struct gave {got}, reference {reference}.");
            }

            // --- Tie the conversions to the storage path: round-trip through a CPU kernel ---
            var probes = new[] { big, tiny, -big, 12345f, 0.015625f };
            int n = probes.Length;
            var src = new BFloat16[n];
            for (int j = 0; j < n; j++) src[j] = (BFloat16)probes[j];

            using var input = accelerator.Allocate1D(src);
            using var output = accelerator.Allocate1D<BFloat16>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<BFloat16>, ArrayView<BFloat16>>(
                BFloat16CopyKernel);
            k((Index1D)n, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var got2 = await output.CopyToHostAsync<BFloat16>();
            for (int j = 0; j < n; j++)
            {
                if ((float)got2[j] != (float)src[j])
                    throw new Exception(
                        $"bfloat16 storage round-trip changed value at {j}: {(float)src[j]} -> {(float)got2[j]}.");
            }
        });
    }
}
