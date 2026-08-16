using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part 1: Core tests (basic, float, 2D, 3D, vector, struct, math, control flow, atomics, intrinsics, shared memory)
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task KernelTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(MyKernel);
            kernel((Index1D)len, buf.View, 42);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (result[i] != i + 42) throw new Exception($"Kernel failed at {i}. Expected {i + 42}, got {result[i]}");
        });

        [TestMethod]
        public async Task KernelFloatTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, float>(FloatKernel);
            kernel((Index1D)len, buf.View, 0.5f);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < len; i++)
            {
                float expected = i * 2.0f + 0.5f;
                if (MathF.Abs(result[i] - expected) > 0.001f)
                    throw new Exception($"Float kernel failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task FloatInfinityLiteralTest() => await RunTest(async accelerator =>
        {
            // Regression test for WGSL/GLSL FormatFloat substituting +Inf/-Inf
            // literals with float.MaxValue / -float.MaxValue. The IsInf-style
            // kernel below previously returned 0 for actually-infinite inputs
            // because the literal compare was rewritten to MaxValue compare.
            // Diagnosed by Data 2026-04-29 against AllOps_IsInf in
            // SpawnDev.ILGPU.ML; fix in WGSLCodeGenerator + GLSLCodeGenerator
            // emits bitcast<f32>(0x7F800000u) / uintBitsToFloat(0x7F800000u)
            // for the infinity bit patterns.
            using var inBuf = accelerator.Allocate1D(new float[] { 0f, float.PositiveInfinity, float.NegativeInfinity });
            using var outBuf = accelerator.Allocate1D<float>(3);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                IsInfLiteralKernel);
            kernel((Index1D)3, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<float>();
            float[] expected = { 0f, 1f, 1f };
            for (int i = 0; i < 3; i++)
                if (result[i] != expected[i])
                    throw new Exception($"FloatInfinityLiteralTest failed at index {i}. Expected {expected[i]}, got {result[i]}.");
        });

        private static void IsInfLiteralKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst)
        {
            float x = src[i];
            dst[i] = (x == float.PositiveInfinity || x == float.NegativeInfinity) ? 1f : 0f;
        }

        [TestMethod]
        public async Task DoubleInfinityLiteralTest() => await RunTest(async accelerator =>
        {
            // Mirror of FloatInfinityLiteralTest but for double precision. Exercises
            // FormatFloat/FormatDouble + the f64 emulation literal path on backends
            // that emulate doubles (WebGPU Dekker / Ozaki, WebGL, Wasm). Tint's
            // shader-creation reject of non-finite const-expressions applies to f32
            // bitcast; emulated f64 routes through `f64_from_ieee754_bits(lo, hi)`
            // where `hi=0x7FF00000u` triggers the Inf branch. This test verifies
            // that the literal-emit path correctly produces an emulated f64 +Inf
            // value that compares equal to a kernel-side `double.PositiveInfinity`.
            using var inBuf = accelerator.Allocate1D(new double[] { 0.0, double.PositiveInfinity, double.NegativeInfinity });
            using var outBuf = accelerator.Allocate1D<double>(3);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>>(
                IsInfDoubleLiteralKernel);
            kernel((Index1D)3, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<double>();
            double[] expected = { 0.0, 1.0, 1.0 };
            for (int i = 0; i < 3; i++)
                if (result[i] != expected[i])
                    throw new Exception($"DoubleInfinityLiteralTest failed at index {i}. Expected {expected[i]}, got {result[i]}.");
        });

        private static void IsInfDoubleLiteralKernel(Index1D i, ArrayView<double> src, ArrayView<double> dst)
        {
            double x = src[i];
            dst[i] = (x == double.PositiveInfinity || x == double.NegativeInfinity) ? 1.0 : 0.0;
        }

        [TestMethod]
        public async Task DoubleIsInfinityIntrinsicTest() => await RunTest(async accelerator =>
        {
            // Exercise the `double.IsInfinity` intrinsic codegen against the
            // emulated f64 representation. On WebGPU the intrinsic lowers to a
            // bit-pattern check on the high word of the emu_f64 pair (Dekker
            // vec2<f32> or Ozaki vec4<f32>); on Wasm it lowers to a native f64
            // intrinsic. The result must NOT depend on which f64 emulation
            // mode the backend is running.
            var input = new double[] { 0.0, double.PositiveInfinity, double.NegativeInfinity, 1.5, double.NaN, double.MaxValue, double.MinValue };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<int>(input.Length);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<int>>(
                DoubleIsInfinityKernel);
            kernel((Index1D)input.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            int[] expected = { 0, 1, 1, 0, 0, 0, 0 };
            for (int i = 0; i < input.Length; i++)
                if (result[i] != expected[i])
                    throw new Exception($"DoubleIsInfinityIntrinsicTest failed at index {i} (input={input[i]}). Expected {expected[i]}, got {result[i]}.");
        });

        private static void DoubleIsInfinityKernel(Index1D i, ArrayView<double> src, ArrayView<int> dst)
        {
            dst[i] = double.IsInfinity(src[i]) ? 1 : 0;
        }

        [TestMethod]
        public async Task DoubleIsNaNIntrinsicTest() => await RunTest(async accelerator =>
        {
            // double.IsNaN intrinsic. NaN cannot be detected with `x == double.NaN`
            // (IEEE: NaN comparisons are always false) - the intrinsic lowers to a
            // bit-pattern check via `x != x` which the codegen must NOT optimise
            // away under any f64 emulation mode (Dekker or Ozaki).
            var input = new double[] { 0.0, double.NaN, double.PositiveInfinity, 1.5, double.NaN, double.MaxValue };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<int>(input.Length);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<int>>(
                DoubleIsNaNKernel);
            kernel((Index1D)input.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            int[] expected = { 0, 1, 0, 0, 1, 0 };
            for (int i = 0; i < input.Length; i++)
                if (result[i] != expected[i])
                    throw new Exception($"DoubleIsNaNIntrinsicTest failed at index {i} (input={input[i]}). Expected {expected[i]}, got {result[i]}.");
        });

        private static void DoubleIsNaNKernel(Index1D i, ArrayView<double> src, ArrayView<int> dst)
        {
            dst[i] = double.IsNaN(src[i]) ? 1 : 0;
        }

        [TestMethod]
        public async Task DoubleNaNComparisonTest() => await RunTest(async accelerator =>
        {
            // IEEE-754 NaN comparison contract: NaN != x for ALL x including NaN
            // itself. NaN == NaN is false; NaN != NaN is true. Both Dekker and
            // Ozaki must respect this through their f64_eq / f64_ne emulation.
            // Encodes the IEEE rules as bit flags for compact verification:
            //   bit 0: NaN == NaN  (must be 0 / false)
            //   bit 1: NaN != NaN  (must be 1 / true)
            //   bit 2: NaN <  1.0  (must be 0 / false)
            //   bit 3: NaN >  1.0  (must be 0 / false)
            //   bit 4: x  != NaN   for x = 0.0  (must be 1 / true)
            //   bit 5: 1.0 == 1.0  (sanity, must be 1 / true)
            //
            // NaN and the comparison-against constants are loaded from a buffer
            // to defeat IR-level constant-folding of the comparison expression
            // (otherwise the IL/IR optimiser would fold NaN==NaN to false at
            // compile time and skip exercising the f64_eq runtime path). The
            // kernel uses TWO distinct slots for NaN so x == x on the same
            // SSA value is also avoided - some IR passes fold that pattern
            // to `true` regardless of whether x is NaN.
            using var inBuf = accelerator.Allocate1D(new double[] { double.NaN, 1.0, 0.0, double.NaN });
            using var outBuf = accelerator.Allocate1D<int>(1);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<int>>(DoubleNaNComparisonKernel);
            kernel((Index1D)1, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            // Expected flags: bit 1 set, bit 4 set, bit 5 set => 0b110010 = 50
            const int expected = 0b110010;
            if (result[0] != expected)
                throw new Exception($"DoubleNaNComparisonTest failed. Expected flags 0b{Convert.ToString(expected, 2).PadLeft(6, '0')} (={expected}), got 0b{Convert.ToString(result[0], 2).PadLeft(6, '0')} (={result[0]}).");
        });

        private static void DoubleNaNComparisonKernel(Index1D _, ArrayView<double> src, ArrayView<int> dst)
        {
            // Distinct buffer slots for NaN to defeat IR x == x folding, plus
            // explicit bool locals to defeat any kernel-shape-specific
            // optimisation that misfires on `if (cmp) flags |= bit;` chains.
            double nan_a = src[0];   // double.NaN
            double nan_b = src[3];   // double.NaN (separate buffer slot, distinct SSA)
            double one = src[1];     // 1.0
            double zero = src[2];    // 0.0
            bool b_nan_eq = nan_a == nan_b;  // IEEE FALSE
            bool b_nan_ne = nan_a != nan_b;  // IEEE TRUE
            bool b_nan_lt = nan_a <  one;    // IEEE FALSE
            bool b_nan_gt = nan_a >  one;    // IEEE FALSE
            bool b_zero_ne = zero != nan_a;  // IEEE TRUE
            bool b_one_eq = one == one;      // sanity TRUE
            int flags = 0;
            if (b_nan_eq)  flags |= 1 << 0;
            if (b_nan_ne)  flags |= 1 << 1;
            if (b_nan_lt)  flags |= 1 << 2;
            if (b_nan_gt)  flags |= 1 << 3;
            if (b_zero_ne) flags |= 1 << 4;
            if (b_one_eq)  flags |= 1 << 5;
            dst[0] = flags;
        }

        [TestMethod]
        public async Task DoubleNaNLessThanIsolatedTest() => await RunTest(async accelerator =>
        {
            // Minimal isolation of the `NaN < x` IEEE-FALSE behaviour so we can
            // tell whether the bug is in the kernel logic of
            // DoubleNaNComparisonTest or in the actual `<` codegen / runtime.
            using var inBuf = accelerator.Allocate1D(new double[] { double.NaN, 1.0 });
            using var outBuf = accelerator.Allocate1D<int>(1);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<int>>(NaNLessThanKernel);
            kernel((Index1D)1, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            // IEEE: NaN < 1.0 is FALSE, so the kernel must store 0.
            if (result[0] != 0)
                throw new Exception($"DoubleNaNLessThanIsolatedTest failed. NaN < 1.0 returned {result[0]} (expected 0).");
        });

        private static void NaNLessThanKernel(Index1D _, ArrayView<double> src, ArrayView<int> dst)
        {
            double nan = src[0];
            double one = src[1];
            bool isLess = nan < one;
            dst[0] = isLess ? 1 : 0;
        }

        [TestMethod]
        public async Task DoubleInfinityArithmeticTest() => await RunTest(async accelerator =>
        {
            // Verify that arithmetic-derived Inf (1.0 / 0.0) compares equal to a
            // kernel-side `double.PositiveInfinity` literal across both Dekker
            // and Ozaki. This is the harder case for emulated f64 because the
            // value comes from runtime math (not a literal substitute) - the
            // emu_f64 representation produced by `f64_div(1.0, 0.0)` must be
            // canonical (+Inf, 0[, 0, 0]) so f64_eq matches the literal.
            //
            // Some emulation libraries rely on f32 hardware Inf propagation
            // (1.0f / 0.0f produces +Inf in f32); WGSL spec says division by
            // zero yields an indeterminate value, but in practice all major
            // WebGPU implementations match IEEE behaviour. If this test
            // fails on a particular backend it indicates the f64 division
            // codegen does NOT canonicalise to (+Inf, 0).
            using var outBuf = accelerator.Allocate1D<int>(2);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(DoubleInfinityArithmeticKernel);
            kernel((Index1D)1, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            // Expected: result[0] = 1 (1.0/0.0 == +Inf), result[1] = 1 (-1.0/0.0 == -Inf)
            if (result[0] != 1 || result[1] != 1)
                throw new Exception($"DoubleInfinityArithmeticTest failed. (1.0/0.0 == +Inf)={result[0]}, (-1.0/0.0 == -Inf)={result[1]}; expected both 1.");
        });

        private static void DoubleInfinityArithmeticKernel(Index1D _, ArrayView<int> dst)
        {
            // Use kernel-local doubles to avoid C# / IL fold-to-Inf.
            double zero = 0.0;
            double one = 1.0;
            double posInf = one / zero;
            double negInf = -one / zero;
            dst[0] = (posInf == double.PositiveInfinity) ? 1 : 0;
            dst[1] = (negInf == double.NegativeInfinity) ? 1 : 0;
        }

        [TestMethod]
        public async Task FloatBitIdentityRoundTripTest() => await RunTest(async accelerator =>
        {
            // Verifies that IEEE 754 f32 bit patterns survive end-to-end:
            // C# float[] -> BlazorJS marshalling -> backend buffer -> kernel
            // copy (load + store) -> backend buffer -> BlazorJS marshalling
            // -> C# float[]. Compares bits exactly (BitConverter.SingleToInt32Bits)
            // except for NaN which we accept as "any NaN" because Wasm spec
            // permits arithmetic canonicalisation (storage-only round trip
            // should still preserve bits, but a copy kernel goes through the
            // f32 register so the GPU may canonicalise).
            //
            // Diagnostic for the C#-vs-JS bit-interpretation hypothesis on
            // the Wasm spinwait race: if marshalling drifts bits anywhere,
            // we'll see it here. If this test passes on all backends, the
            // BlazorJS TypedArray bit-identity assumption is sound.
            var values = new float[]
            {
                0.0f,
                BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),  // -0.0
                1.0f,
                -1.0f,
                float.MinValue,
                float.MaxValue,
                float.Epsilon,
                float.PositiveInfinity,
                float.NegativeInfinity,
                float.NaN,                                                    // qNaN 0x7FC00000
                BitConverter.Int32BitsToSingle(unchecked((int)0x7FC00001)),  // qNaN with payload
                BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00000)),  // qNaN negative sign
                BitConverter.Int32BitsToSingle(unchecked((int)0x7F800001)),  // sNaN
            };
            using var srcBuf = accelerator.Allocate1D(values);
            using var dstBuf = accelerator.Allocate1D<float>(values.Length);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(BitIdentityCopyKernel);
            kernel((Index1D)values.Length, srcBuf.View, dstBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await dstBuf.CopyToHostAsync<float>();
            for (int i = 0; i < values.Length; i++)
            {
                int expectedBits = BitConverter.SingleToInt32Bits(values[i]);
                int actualBits = BitConverter.SingleToInt32Bits(result[i]);
                if (float.IsNaN(values[i]))
                {
                    if (!float.IsNaN(result[i]))
                        throw new Exception($"Slot {i}: input was NaN (bits 0x{expectedBits:X8}) but output is not NaN (bits 0x{actualBits:X8}, value {result[i]})");
                }
                else if (expectedBits != actualBits)
                {
                    throw new Exception($"Slot {i}: bit drift. Expected 0x{expectedBits:X8} ({values[i]}), got 0x{actualBits:X8} ({result[i]})");
                }
            }
        });

        private static void BitIdentityCopyKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst)
        {
            dst[i] = src[i];
        }

        [TestMethod]
        public async Task FloatNoKernelBitIdentityRoundTripTest() => await RunTest(async accelerator =>
        {
            // Pure marshalling round trip: write f32 bit patterns into a
            // backend buffer, do NOT dispatch any kernel, read back. Tests
            // ONLY the BlazorJS TypedArray marshalling layer (no GPU
            // load/store, no Wasm worker dispatch, no compute path). If
            // this test passes on every backend, the bit-identity
            // assumption SpawnDev.SpawnJS makes is sound for f32.
            var values = new float[]
            {
                0.0f,
                BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
                float.PositiveInfinity,
                float.NegativeInfinity,
                float.NaN,
                BitConverter.Int32BitsToSingle(unchecked((int)0x7FC00001)),
                BitConverter.Int32BitsToSingle(unchecked((int)0xFFC00000)),
                BitConverter.Int32BitsToSingle(unchecked((int)0x7F800001)),
            };
            using var buf = accelerator.Allocate1D(values);
            // No kernel dispatch - just read back.
            var result = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < values.Length; i++)
            {
                int expectedBits = BitConverter.SingleToInt32Bits(values[i]);
                int actualBits = BitConverter.SingleToInt32Bits(result[i]);
                if (expectedBits != actualBits)
                    throw new Exception($"Slot {i}: marshalling bit drift. Expected 0x{expectedBits:X8} ({values[i]}), got 0x{actualBits:X8} ({result[i]})");
            }
        });

        [TestMethod]
        public async Task SignedIntDivByPow2TruncateTowardZeroTest() => await RunTest(async accelerator =>
        {
            // Regression test for ILGPU IR's Div-by-pow2 -> Shr rewrite.
            // C# / IL `div` / C99 signed integer division truncates toward
            // zero: (-1) / 2 == 0, (-3) / 2 == -1, (-5) / 2 == -2, ...
            // Arithmetic shift right floors toward -infinity:
            // -1 >> 1 == -1, -3 >> 1 == -2, -5 >> 1 == -3, ...
            // Off-by-one for every odd-negative dividend. Diagnosed by
            // Tuvok 2026-04-28 against `Vp9ForwardDct8x8Kernel` after the
            // post-pass `output[i] /= 2` produced wrong results on CUDA +
            // OpenCL (CPU passed because the IL backend bypasses the
            // rewrite). Fix gates the rewrite on unsigned dividends only;
            // signed Div is left alone for the backend to lower correctly.
            int[] inputs =
            {
                -1, -3, -5, -7, -32639, -2147483647,
                1, 3, 5, 7, 32639,
                -2, -4, 0, 2, 4,
                int.MinValue + 1, int.MaxValue,
            };
            using var inBuf = accelerator.Allocate1D(inputs);
            using var outBuf = accelerator.Allocate1D<int>(inputs.Length);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(SignedIntDivByTwoKernel);
            kernel((Index1D)inputs.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < inputs.Length; i++)
            {
                int expected = inputs[i] / 2;  // .NET CLR truncate-toward-zero
                if (result[i] != expected)
                    throw new Exception($"Slot {i}: input={inputs[i]} expected (truncate)={expected}, got={result[i]} (diff={result[i] - expected})");
            }
        });

        private static void SignedIntDivByTwoKernel(Index1D i, ArrayView<int> input, ArrayView<int> output)
        {
            output[i] = input[i] / 2;
        }

        [TestMethod]
        public async Task SignedIntDivByPow2_LargerDivisorsTest() => await RunTest(async accelerator =>
        {
            // Same regression but with several pow2 divisors (4, 8, 16, 32)
            // and a wider input range. The div-by-pow2 rewrite applies to
            // every pow2, so the fix needs to cover all of them.
            int[] inputs = { -7, -15, -31, -63, -100, -1000, 7, 15, 31, 63, 100, 1000, 0 };
            using var inBuf = accelerator.Allocate1D(inputs);
            using var outBuf = accelerator.Allocate1D<int>(inputs.Length * 4);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(SignedIntDivByPow2Kernel);
            kernel((Index1D)inputs.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < inputs.Length; i++)
            {
                int x = inputs[i];
                int[] expected = { x / 4, x / 8, x / 16, x / 32 };
                for (int j = 0; j < 4; j++)
                {
                    int got = result[i * 4 + j];
                    if (got != expected[j])
                        throw new Exception($"x={x} /{1 << (j + 2)}: expected={expected[j]}, got={got}");
                }
            }
        });

        private static void SignedIntDivByPow2Kernel(Index1D i, ArrayView<int> input, ArrayView<int> output)
        {
            int x = input[i];
            output[i * 4 + 0] = x / 4;
            output[i * 4 + 1] = x / 8;
            output[i * 4 + 2] = x / 16;
            output[i * 4 + 3] = x / 32;
        }

        [TestMethod]
        public async Task FloatNaNComparisonTest() => await RunTest(async accelerator =>
        {
            // IEEE-754 multi-comparison NaN regression for f32. Mirrors
            // DoubleNaNComparisonTest but for native f32 (no f64 emulation
            // path involved). Catches the same ILGPU IR inversion bug
            // (clt + brfalse rewritten as cge + brtrue [Unordered]) on the
            // f32 codegen of every backend.
            using var inBuf = accelerator.Allocate1D(new float[] { float.NaN, 1.0f, 0.0f, float.NaN });
            using var outBuf = accelerator.Allocate1D<int>(1);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<int>>(FloatNaNComparisonKernel);
            kernel((Index1D)1, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            const int expected = 0b110010;
            if (result[0] != expected)
                throw new Exception($"FloatNaNComparisonTest failed. Expected flags 0b{Convert.ToString(expected, 2).PadLeft(6, '0')} (={expected}), got 0b{Convert.ToString(result[0], 2).PadLeft(6, '0')} (={result[0]}).");
        });

        private static void FloatNaNComparisonKernel(Index1D _, ArrayView<float> src, ArrayView<int> dst)
        {
            float nan_a = src[0];
            float nan_b = src[3];
            float one = src[1];
            float zero = src[2];
            bool b_nan_eq = nan_a == nan_b;
            bool b_nan_ne = nan_a != nan_b;
            bool b_nan_lt = nan_a <  one;
            bool b_nan_gt = nan_a >  one;
            bool b_zero_ne = zero != nan_a;
            bool b_one_eq = one == one;
            int flags = 0;
            if (b_nan_eq)  flags |= 1 << 0;
            if (b_nan_ne)  flags |= 1 << 1;
            if (b_nan_lt)  flags |= 1 << 2;
            if (b_nan_gt)  flags |= 1 << 3;
            if (b_zero_ne) flags |= 1 << 4;
            if (b_one_eq)  flags |= 1 << 5;
            dst[0] = flags;
        }

        [TestMethod]
        public async Task FloatInfinityArithmeticTest() => await RunTest(async accelerator =>
        {
            // 1.0f / 0.0f produces +Inf, -1.0f / 0.0f produces -Inf in IEEE
            // 754 even though some shader specs say "indeterminate". This
            // test verifies that arithmetic-derived f32 Inf compares equal
            // to the literal float.PositiveInfinity / float.NegativeInfinity.
            using var outBuf = accelerator.Allocate1D<int>(2);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(FloatInfinityArithmeticKernel);
            kernel((Index1D)1, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            if (result[0] != 1 || result[1] != 1)
                throw new Exception($"FloatInfinityArithmeticTest failed. (1.0f/0.0f == +Inf)={result[0]}, (-1.0f/0.0f == -Inf)={result[1]}; expected both 1.");
        });

        private static void FloatInfinityArithmeticKernel(Index1D _, ArrayView<int> dst)
        {
            float zero = 0.0f;
            float one = 1.0f;
            float posInf = one / zero;
            float negInf = -one / zero;
            dst[0] = (posInf == float.PositiveInfinity) ? 1 : 0;
            dst[1] = (negInf == float.NegativeInfinity) ? 1 : 0;
        }

        [TestMethod]
        public async Task FloatDivisionByZeroTest() => await RunTest(async accelerator =>
        {
            // IEEE 754 division-by-zero contract for f32:
            //  +x / 0  = +Inf  (x > 0)
            //  -x / 0  = -Inf  (x > 0)
            //   0 / 0  = NaN
            //   Inf / Inf = NaN
            //   x * 0  for x = +Inf is NaN
            // Verify all five invariants in one kernel.
            using var outBuf = accelerator.Allocate1D<int>(5);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(FloatDivisionByZeroKernel);
            kernel((Index1D)1, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < 5; i++)
                if (result[i] != 1)
                    throw new Exception($"FloatDivisionByZeroTest failed at invariant {i}. Got {result[i]}; expected 1. Full results: [{string.Join(",", result)}].");
        });

        private static void FloatDivisionByZeroKernel(Index1D _, ArrayView<int> dst)
        {
            float zero = 0.0f;
            float one = 1.0f;
            float posInf = one / zero;
            float negInf = -one / zero;
            float nanZeroOverZero = zero / zero;
            float nanInfOverInf = posInf / posInf;
            float nanInfTimesZero = posInf * zero;
            // Direct comparison patterns - avoid float.IsNaN/IsPositiveInfinity
            // method calls because constant-propagation through `zero = 0.0f;
            // posInf = one / zero` makes the operand a compile-time constant,
            // which triggers the IsNaNF intrinsic's CPU eager-fold path and
            // tries to constant-fold a Compare(NotEqual, Int1, Int1) that
            // CompareFoldConstants doesn't support. Direct f32 == comparisons
            // work because they fold to a primitive bool result.
            // NaN check via x != x (TRUE only for NaN per IEEE 754).
            dst[0] = (posInf == float.PositiveInfinity) ? 1 : 0;
            dst[1] = (negInf == float.NegativeInfinity) ? 1 : 0;
            dst[2] = (nanZeroOverZero != nanZeroOverZero) ? 1 : 0;
            dst[3] = (nanInfOverInf != nanInfOverInf) ? 1 : 0;
            dst[4] = (nanInfTimesZero != nanInfTimesZero) ? 1 : 0;
        }

        [TestMethod]
        public async Task DoubleDivisionByZeroTest() => await RunTest(async accelerator =>
        {
            // Same IEEE 754 division-by-zero invariants as FloatDivisionByZero
            // but for f64 (which on WebGPU/WebGL is emulated through Dekker
            // or Ozaki — the f64_div helper must canonicalise to (+Inf,0) /
            // (-Inf,0) / NaN to match the literal comparisons below).
            using var outBuf = accelerator.Allocate1D<int>(5);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(DoubleDivisionByZeroKernel);
            kernel((Index1D)1, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < 5; i++)
                if (result[i] != 1)
                    throw new Exception($"DoubleDivisionByZeroTest failed at invariant {i}. Got {result[i]}; expected 1. Full results: [{string.Join(",", result)}].");
        });

        private static void DoubleDivisionByZeroKernel(Index1D _, ArrayView<int> dst)
        {
            double zero = 0.0;
            double one = 1.0;
            double posInf = one / zero;
            double negInf = -one / zero;
            double nanZeroOverZero = zero / zero;
            double nanInfOverInf = posInf / posInf;
            double nanInfTimesZero = posInf * zero;
            // See FloatDivisionByZeroKernel for why we use direct comparisons
            // (`x != x` for NaN) instead of double.IsNaN.
            dst[0] = (posInf == double.PositiveInfinity) ? 1 : 0;
            dst[1] = (negInf == double.NegativeInfinity) ? 1 : 0;
            dst[2] = (nanZeroOverZero != nanZeroOverZero) ? 1 : 0;
            dst[3] = (nanInfOverInf != nanInfOverInf) ? 1 : 0;
            dst[4] = (nanInfTimesZero != nanInfTimesZero) ? 1 : 0;
        }

        [TestMethod]
        public async Task HalfNaNComparisonTest() => await RunTest(async accelerator =>
        {
            // Multi-compare NaN regression for ILGPU.Half. Half is f32 in
            // arithmetic on every backend (f16 storage, f32 compute), so the
            // compare codegen path is the f32 path with implicit Half→f32
            // promotion. NaN bit pattern survives the promotion (f16 NaN
            // encodes to f32 NaN per IEEE 754 lossless conversion).
            global::ILGPU.Half nan = (global::ILGPU.Half)float.NaN;
            global::ILGPU.Half oneH = (global::ILGPU.Half)1.0f;
            global::ILGPU.Half zeroH = (global::ILGPU.Half)0.0f;
            using var inBuf = accelerator.Allocate1D(new global::ILGPU.Half[] { nan, oneH, zeroH, nan });
            using var outBuf = accelerator.Allocate1D<int>(1);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<int>>(HalfNaNComparisonKernel);
            kernel((Index1D)1, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            const int expected = 0b110010;
            if (result[0] != expected)
                throw new Exception($"HalfNaNComparisonTest failed. Expected flags 0b{Convert.ToString(expected, 2).PadLeft(6, '0')} (={expected}), got 0b{Convert.ToString(result[0], 2).PadLeft(6, '0')} (={result[0]}).");
        });

        private static void HalfNaNComparisonKernel(Index1D _, ArrayView<global::ILGPU.Half> src, ArrayView<int> dst)
        {
            global::ILGPU.Half nan_a = src[0];
            global::ILGPU.Half nan_b = src[3];
            global::ILGPU.Half one = src[1];
            global::ILGPU.Half zero = src[2];
            bool b_nan_eq = nan_a == nan_b;
            bool b_nan_ne = nan_a != nan_b;
            bool b_nan_lt = nan_a <  one;
            bool b_nan_gt = nan_a >  one;
            bool b_zero_ne = zero != nan_a;
            bool b_one_eq = one == one;
            int flags = 0;
            if (b_nan_eq)  flags |= 1 << 0;
            if (b_nan_ne)  flags |= 1 << 1;
            if (b_nan_lt)  flags |= 1 << 2;
            if (b_nan_gt)  flags |= 1 << 3;
            if (b_zero_ne) flags |= 1 << 4;
            if (b_one_eq)  flags |= 1 << 5;
            dst[0] = flags;
        }

        [TestMethod]
        public async Task HalfInfinityArithmeticTest() => await RunTest(async accelerator =>
        {
            // Half +Inf / -Inf round-trip through buffer storage. Half-encoded
            // Inf bit patterns must survive _f16→f32 promotion and compare
            // equal to the float +Inf literal. We do NOT do GPU-side div-by-
            // zero arithmetic here because WGSL/GLSL spec says div-by-zero is
            // implementation-defined (some backends do not follow IEEE 754
            // for runtime f32 division). Inf values are constructed host-side
            // via `(Half)float.PositiveInfinity` cast (lossless).
            using var inBuf = accelerator.Allocate1D(new global::ILGPU.Half[]
            {
                (global::ILGPU.Half)float.PositiveInfinity,
                (global::ILGPU.Half)float.NegativeInfinity,
            });
            using var outBuf = accelerator.Allocate1D<int>(2);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<int>>(HalfInfinityArithmeticKernel);
            kernel((Index1D)1, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            if (result[0] != 1 || result[1] != 1)
                throw new Exception($"HalfInfinityArithmeticTest failed. (Half +Inf == float +Inf)={result[0]}, (Half -Inf == float -Inf)={result[1]}; expected both 1.");
        });

        private static void HalfInfinityArithmeticKernel(Index1D _, ArrayView<global::ILGPU.Half> src, ArrayView<int> dst)
        {
            global::ILGPU.Half halfPosInf = src[0];
            global::ILGPU.Half halfNegInf = src[1];
            dst[0] = ((float)halfPosInf == float.PositiveInfinity) ? 1 : 0;
            dst[1] = ((float)halfNegInf == float.NegativeInfinity) ? 1 : 0;
        }

        [TestMethod]
        public async Task HalfDivisionByZeroTest() => await RunTest(async accelerator =>
        {
            // Verifies that Half +Inf / -Inf / NaN values constructed host-
            // side and stored in a buffer survive the round-trip into the
            // kernel and out: the kernel reads them as Half, promotes to
            // f32, and identifies them via direct comparison / x!=x.
            // Tests the storage + conversion path (_f16→f32 must preserve
            // Inf and NaN bit patterns). Does NOT test GPU runtime division
            // because WGSL/GLSL spec says div-by-zero is implementation-
            // defined and Tint in practice declines to follow IEEE for
            // runtime f32 div-by-zero.
            using var inBuf = accelerator.Allocate1D(new global::ILGPU.Half[]
            {
                (global::ILGPU.Half)float.PositiveInfinity,
                (global::ILGPU.Half)float.NegativeInfinity,
                (global::ILGPU.Half)float.NaN,
            });
            using var outBuf = accelerator.Allocate1D<int>(3);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<int>>(HalfDivisionByZeroKernel);
            kernel((Index1D)1, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < 3; i++)
                if (result[i] != 1)
                    throw new Exception($"HalfDivisionByZeroTest failed at invariant {i}. Got {result[i]}; expected 1. Full results: [{string.Join(",", result)}].");
        });

        private static void HalfDivisionByZeroKernel(Index1D _, ArrayView<global::ILGPU.Half> src, ArrayView<int> dst)
        {
            global::ILGPU.Half halfPosInf = src[0];
            global::ILGPU.Half halfNegInf = src[1];
            global::ILGPU.Half halfNaN = src[2];
            float fPos = (float)halfPosInf;
            float fNeg = (float)halfNegInf;
            float fNaN = (float)halfNaN;
            dst[0] = (fPos == float.PositiveInfinity) ? 1 : 0;
            dst[1] = (fNeg == float.NegativeInfinity) ? 1 : 0;
            dst[2] = (fNaN != fNaN) ? 1 : 0;
        }

        [TestMethod]
        public async Task MultiScalarKernelTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int, int>(MultiScalarKernel);
            kernel((Index1D)len, buf.View, 10, 20);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (result[i] != i + 30) throw new Exception($"Multi-scalar failed at {i}. Expected {i + 30}, got {result[i]}");
        });

        [TestMethod]
        public async Task Kernel2DTest() => await RunTest(async accelerator =>
        {
            int width = 8, height = 8;
            using var buf = accelerator.Allocate2DDenseX<float>(new Index2D(width, height));
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index2D, ArrayView2D<float, Stride2D.DenseX>>(Kernel2D);
            kernel(buf.IntExtent, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float expected = x + y * 100.0f;
                    int idx = y * width + x;
                    if (MathF.Abs(result[idx] - expected) > 0.001f)
                        throw new Exception($"2D kernel failed at ({x},{y}). Expected {expected}, got {result[idx]}");
                }
        });

        [TestMethod]
        public async Task Kernel3DTest() => await RunTest(async accelerator =>
        {
            int w = 4, h = 4, d = 4;
            using var buf = accelerator.Allocate3DDenseXY<float>(new Index3D(w, h, d));
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index3D, ArrayView3D<float, Stride3D.DenseXY>>(Kernel3D);
            kernel(buf.IntExtent, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();
            for (int z = 0; z < d; z++)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float expected = x + y * 100.0f + z * 1000.0f;
                        int idx = z * (w * h) + y * w + x;
                        if (MathF.Abs(result[idx] - expected) > 0.001f)
                            throw new Exception($"3D kernel failed at ({x},{y},{z}). Expected {expected}, got {result[idx]}");
                    }
        });

        [TestMethod]
        public async Task VectorAddTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var aData = new float[len]; var bData = new float[len];
            for (int i = 0; i < len; i++) { aData[i] = i; bData[i] = i * 0.5f; }
            using var bufA = accelerator.Allocate1D(aData);
            using var bufB = accelerator.Allocate1D(bData);
            using var bufC = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(VectorAddKernel);
            kernel((Index1D)len, bufA.View, bufB.View, bufC.View);
            await accelerator.SynchronizeAsync();
            var result = await bufC.CopyToHostAsync<float>();
            for (int i = 0; i < len; i++)
            {
                float expected = i + i * 0.5f;
                if (MathF.Abs(result[i] - expected) > 0.001f)
                    throw new Exception($"Vector add failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task StructTest() => await RunTest(async accelerator =>
        {
            int len = 32;
            var points = new MyPoint[len];
            for (int i = 0; i < len; i++) points[i] = new MyPoint { X = i, Y = i * 2.0f };
            using var buf = accelerator.Allocate1D(points);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<MyPoint>>(StructKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<MyPoint>();
            for (int i = 0; i < len; i++)
            {
                if (MathF.Abs(result[i].X - (i + 1.0f)) > 0.001f)
                    throw new Exception($"Struct X failed at {i}. Expected {i + 1.0f}, got {result[i].X}");
                if (MathF.Abs(result[i].Y - (i * 4.0f)) > 0.001f)
                    throw new Exception($"Struct Y failed at {i}. Expected {i * 4.0f}, got {result[i].Y}");
            }
        });

        [TestMethod]
        public async Task MathTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            var input = new float[] { 0.0f, 1.0f, 2.0f, 3.14f, -1.0f, 0.5f, 10.0f, 100.0f };
            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(MathKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<float>();
            for (int i = 0; i < len; i++)
            {
                float expected = MathF.Sin(input[i]) + MathF.Cos(input[i]) + MathF.Sqrt(MathF.Abs(input[i]));
                if (MathF.Abs(result[i] - expected) > 0.01f)
                    throw new Exception($"Math failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task ControlFlowTest() => await RunTest(async accelerator =>
        {
            int len = 16;
            var data = new int[len];
            for (int i = 0; i < len; i++) data[i] = i;
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(ControlFlowKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
            {
                int expected = (data[i] % 2 == 0) ? 10 : -1;
                if (result[i] != expected) throw new Exception($"Control flow failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task AtomicTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            using var buf = accelerator.Allocate1D<int>(len);
            using var atomicBuf = accelerator.Allocate1D<Index1D>(1);
            atomicBuf.MemSetToZero();
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<Index1D>>(AtomicKernel);
            kernel((Index1D)len, buf.View, atomicBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            var atomicResult = await atomicBuf.CopyToHostAsync<Index1D>();
            int expectedSum = (len * (len + 1)) / 2;
            if ((int)atomicResult[0] != expectedSum)
                throw new Exception($"Atomic sum failed. Expected {expectedSum}, got {atomicResult[0]}");
            for (int i = 0; i < len; i++)
                if (result[i] != i + 1) throw new Exception($"Data failed at {i}");
        });

        [TestMethod]
        public async Task IntrinsicMathTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(IntrinsicMathKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();
            if (MathF.Abs(result[0] - MathF.Atan2(1.0f, 1.0f)) > 0.01f)
                throw new Exception($"Atan2 failed. Expected {MathF.Atan2(1.0f, 1.0f)}, got {result[0]}");
            if (MathF.Abs(result[1] - 10.0f) > 0.01f)
                throw new Exception($"FMA failed. Expected 10, got {result[1]}");
            if (MathF.Abs(result[5] - 5.0f) > 0.01f)
                throw new Exception($"Clamp failed. Expected 5, got {result[5]}");
        });

        [TestMethod]
        public async Task ConversionTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            var data = new float[] { 1.9f, 2.1f, -3.7f, 0.0f, 100.5f, -50.5f, 0.1f, 0.9f };
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(ConversionKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();
            float[] expected = { 1.0f, 2.0f, -3.0f, 0.0f, 100.0f, -50.0f, 0.0f, 0.0f };
            for (int i = 0; i < len; i++)
                if (MathF.Abs(result[i] - expected[i]) > 0.001f)
                    throw new Exception($"Conversion failed at {i}. Expected {expected[i]}, got {result[i]}");
        });

        [TestMethod]
        public async Task SharedMemoryTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var data = new int[len];
            for (int i = 0; i < len; i++) data[i] = i * 10;
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>>(SharedMemoryKernel);
            kernel(new KernelConfig(1, len), (Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
            {
                int expected = data[(i + 1) % 64];
                if (result[i] != expected)
                    throw new Exception($"Shared memory failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task NestedControlFlowTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(NestedControlFlowKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            int expected = 19; // 3 iterations of inner(0+1+2=3)*3=9, plus 10 for j==1 = 19
            for (int i = 0; i < len; i++)
                if (result[i] != expected)
                    throw new Exception($"Nested control flow failed at {i}. Expected {expected}, got {result[i]}");
        });

        [TestMethod]
        public async Task FunctionCallTest() => await RunTest(async accelerator =>
        {
            int len = 16;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(FunctionCallKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (result[i] != i + 100)
                    throw new Exception($"Function call failed at {i}. Expected {i + 100}, got {result[i]}");
        });

        [TestMethod]
        public async Task CSharpSharedMemoryTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var data = new int[len];
            for (int i = 0; i < len; i++) data[i] = i * 3;
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>>(CSharpSharedMemoryKernel);
            kernel(new KernelConfig(1, len), (Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
            {
                int expected = data[63 - i];
                if (result[i] != expected)
                    throw new Exception($"C# shared memory failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task ComplexStructTest() => await RunTest(async accelerator =>
        {
            int len = 16;
            var data = new OuterStruct[len];
            for (int i = 0; i < len; i++) data[i] = new OuterStruct { Inner = new InnerStruct { Val = i * 0.5f }, ID = i };
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<OuterStruct>>(ComplexStructKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<OuterStruct>();
            for (int i = 0; i < len; i++)
            {
                if (MathF.Abs(result[i].Inner.Val - (i * 0.5f + 1.0f)) > 0.01f)
                    throw new Exception($"Complex struct Val failed at {i}");
                if (result[i].ID != i * 2)
                    throw new Exception($"Complex struct ID failed at {i}");
            }
        });

        [TestMethod]
        public async Task AtomicCASTest() => await RunTest(async accelerator =>
        {
            int len = 16;
            var data = new int[len];
            for (int i = 0; i < len; i++) data[i] = i;
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(AtomicCASKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (result[i] != i + 100) throw new Exception($"CAS failed at {i}. Expected {i + 100}, got {result[i]}");
        });

        [TestMethod]
        public async Task FMATest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(FMAKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < len; i++)
            {
                float expected = MathF.FusedMultiplyAdd(i, 2.0f, 0.5f);
                if (MathF.Abs(result[i] - expected) > 0.01f)
                    throw new Exception($"FMA failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task StructScalarArgTest() => await RunTest(async accelerator =>
        {
            int len = 32;
            using var buf = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ScalarStruct>(StructScalarArgKernel);
            var s = new ScalarStruct { X = 3.0f, Y = 7.0f };
            kernel((Index1D)len, buf.View, s);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < len; i++)
            {
                float expected = 10.0f; // 3.0 + 7.0
                if (MathF.Abs(result[i] - expected) > 0.001f)
                    throw new Exception($"Struct scalar arg failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task NestedStructScalarArgTest() => await RunTest(async accelerator =>
        {
            int len = 32;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, NestedOuterStruct>(NestedStructScalarArgKernel);
            var s = new NestedOuterStruct { Inner = new NestedInnerStruct { A = 3, B = 5 }, Value = 2.0f };
            kernel((Index1D)len, buf.View, s);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
            {
                int expected = 10; // 3 + 5 + 2
                if (result[i] != expected)
                    throw new Exception($"Nested struct scalar arg failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task CopyToHostPartialReadback() => await RunTest(async accelerator =>
        {
            int len = 64;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(MyKernel);
            kernel((Index1D)len, buf.View, 0); // buf[i] = i + 0 = i
            await accelerator.SynchronizeAsync();

            // Full readback
            var full = await buf.CopyToHostAsync<int>();
            if (full.Length != 64) throw new Exception($"Full readback should be 64, got {full.Length}");

            // Partial readback: offset=10, count=5
            var partial = await buf.CopyToHostAsync<int>(10, 5);
            if (partial.Length != 5) throw new Exception($"Partial should be 5, got {partial.Length}");
            for (int i = 0; i < 5; i++)
            {
                if (partial[i] != i + 10)
                    throw new Exception($"Partial[{i}] expected {i + 10}, got {partial[i]}");
            }

            // Partial readback: first 3 elements
            var first3 = await buf.CopyToHostAsync<int>(0, 3);
            if (first3.Length != 3) throw new Exception($"First3 should be 3, got {first3.Length}");
            if (first3[0] != 0 || first3[1] != 1 || first3[2] != 2)
                throw new Exception($"First3 values wrong: {first3[0]},{first3[1]},{first3[2]}");

            // Partial readback: last element
            var last = await buf.CopyToHostAsync<int>(63, 1);
            if (last.Length != 1) throw new Exception($"Last should be 1, got {last.Length}");
            if (last[0] != 63) throw new Exception($"Last[0] expected 63, got {last[0]}");

            // Full range (offset=0, count=len) should equal full readback
            var fullRange = await buf.CopyToHostAsync<int>(0, 64);
            if (fullRange.Length != 64) throw new Exception($"FullRange should be 64, got {fullRange.Length}");
            for (int i = 0; i < 64; i++)
            {
                if (fullRange[i] != full[i])
                    throw new Exception($"FullRange[{i}] mismatch: {fullRange[i]} != {full[i]}");
            }
        });
    }
}
