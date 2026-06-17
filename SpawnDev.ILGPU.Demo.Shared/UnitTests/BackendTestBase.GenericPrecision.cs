using System;
using System.Numerics;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Generic INumber<T> mixed-precision kernels (Geordi 2026-06-16, from Tuvok's
    // tuvok-to-geordi-generic-INumber-kernel-codegen-gaps). ONE generic kernel
    // y[i] = relu(x[i]*scale + bias) compiled for float (control) / ILGPU.Half / ILGPU.BFloat16
    // must transpile AND run correctly on every backend. This exercises the codegen gaps the
    // concrete-typed bf16/Half work didn't cover: the generic-specialization compile path AND
    // by-value SUB-WORD SCALAR params (scale/bias) - distinct from sub-word BUFFER elements.
    // The low-precision SCALAR params are the point: ArrayView<Half> already worked; passing a
    // Half/bf16 BY VALUE is what broke (PTX KeyNotFoundException, OpenCL CL_INVALID_ARG_SIZE,
    // WebGPU/WebGL scalar==0, Wasm raw-bits output).
    public abstract partial class BackendTestBase
    {
        private static void FusedReluGeneric<T>(Index1D i,
            ArrayView1D<T, Stride1D.Dense> x, ArrayView1D<T, Stride1D.Dense> y, T scale, T bias)
            where T : unmanaged, INumber<T>
        {
            T v = x[i] * scale + bias;
            y[i] = v > T.Zero ? v : T.Zero;
        }

        [TestMethod]
        public async Task GenericPrecision_Float_RunsAndMatchesCpu() =>
            await RunGenericPrecision<float>(v => v, v => v, 8e-6f);

        [TestMethod]
        public async Task GenericPrecision_Half_RunsAndMatchesCpu() =>
            await RunGenericPrecision<global::ILGPU.Half>(
                v => (global::ILGPU.Half)v, v => (float)v, 8e-3f);

        [TestMethod]
        public async Task GenericPrecision_BFloat16_RunsAndMatchesCpu() =>
            await RunGenericPrecision<global::ILGPU.BFloat16>(
                v => (global::ILGPU.BFloat16)v, v => (float)v, 3e-2f);

        // PrecisionConvert: transpilable generic float<->T conversion inside a generic kernel
        // (Geordi 2026-06-16, from Tuvok's generic-in-kernel-float-T-conversion-gap). The point is
        // that float.CreateChecked(t)/T.CreateChecked(f) touch System.Type and the transpiler rejects
        // them on every GPU backend; PrecisionConvert.ConvertToSingle/ConvertFromSingle lower to the
        // native convert instead. A pure round-trip ConvertFromSingle(ConvertToSingle(x)) must be
        // BIT-EXACT vs the concrete (T)(float)x cast on all 6 backends (no accumulation, no tolerance).
        private static void PrecisionRoundTripGeneric<T>(Index1D i,
            ArrayView1D<T, Stride1D.Dense> x, ArrayView1D<T, Stride1D.Dense> y)
            where T : unmanaged, INumber<T> =>
            y[i] = PrecisionConvert.ConvertFromSingle<T>(PrecisionConvert.ConvertToSingle(x[i]));

        [TestMethod]
        public async Task PrecisionConvert_Float_RoundTripBitExact() =>
            await RunPrecisionRoundTrip<float>(v => v, v => v);

        [TestMethod]
        public async Task PrecisionConvert_Half_RoundTripBitExact() =>
            await RunPrecisionRoundTrip<global::ILGPU.Half>(
                v => (global::ILGPU.Half)v, v => (float)v);

        [TestMethod]
        public async Task PrecisionConvert_BFloat16_RoundTripBitExact() =>
            await RunPrecisionRoundTrip<global::ILGPU.BFloat16>(
                v => (global::ILGPU.BFloat16)v, v => (float)v);

        // FP8 round-trip on the backends where FP8 GPU codegen is wired (CPU, OpenCL, WebGPU as of
        // local.9+). CUDA + WebGL + Wasm FP8 codegen is in progress - skip there until wired (a
        // capability flag will replace this explicit gate). Bit-exact vs the concrete (T)(float)x cast.
        [TestMethod]
        public async Task PrecisionConvert_Float8E4M3_RoundTripBitExact() =>
            await RunFP8RoundTrip<global::ILGPU.Float8E4M3>(
                v => (global::ILGPU.Float8E4M3)v, v => (float)v);

        [TestMethod]
        public async Task PrecisionConvert_Float8E5M2_RoundTripBitExact() =>
            await RunFP8RoundTrip<global::ILGPU.Float8E5M2>(
                v => (global::ILGPU.Float8E5M2)v, v => (float)v);

        private async Task RunFP8RoundTrip<T>(Func<float, T> toT, Func<T, float> toF)
            where T : unmanaged, INumber<T>
            // FP8 (Float8E4M3 + Float8E5M2) codegen is wired on ALL 6 backends
            // (CPU, OpenCL, WebGPU, WebGL, Wasm, CUDA) - no skip needed.
            => await RunTest(async accelerator =>
                await RunPrecisionRoundTripCore<T>(accelerator, toT, toF));

        private async Task RunPrecisionRoundTrip<T>(Func<float, T> toT, Func<T, float> toF)
            where T : unmanaged, INumber<T>
            => await RunTest(async accelerator =>
                await RunPrecisionRoundTripCore<T>(accelerator, toT, toF));

        private async Task RunPrecisionRoundTripCore<T>(
            Accelerator accelerator, Func<float, T> toT, Func<T, float> toF)
            where T : unmanaged, INumber<T>
        {
            const int n = 251;
            var rng = new Random(13);
            var x = new T[n];
            for (int i = 0; i < n; i++)
                x[i] = toT((float)(rng.NextDouble() * 4 - 2));

            using var inBuf = accelerator.Allocate1D(x);
            using var outBuf = accelerator.Allocate1D<T>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<T, Stride1D.Dense>, ArrayView1D<T, Stride1D.Dense>>(
                PrecisionRoundTripGeneric<T>);
            k(n, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<T>();

            for (int i = 0; i < n; i++)
            {
                // Reference = concrete (T)(float)x cast - exactly what the intrinsic lowers to.
                float g = toF(got[i]);
                float e = toF(toT(toF(x[i])));
                bool bothNaN = float.IsNaN(g) && float.IsNaN(e);
                if (!bothNaN && g != e)
                    throw new Exception(
                        $"PrecisionConvert {typeof(T).Name} round-trip @{i} ({BackendName}): got {g}, " +
                        $"want {e} - generic float<->T convert must lower to the native cast (bit-exact).");
            }
        }

        private async Task RunGenericPrecision<T>(Func<float, T> toT, Func<T, float> toF, float relTol)
            where T : unmanaged, INumber<T>
            => await RunTest(async accelerator =>
        {
            const int n = 257; const float scale = 1.5f, bias = -0.25f;
            var rng = new Random(7);
            var x = new T[n];
            var expected = new float[n];
            for (int i = 0; i < n; i++)
            {
                float xf = (float)(rng.NextDouble() * 4 - 2);
                x[i] = toT(xf);
                float v = xf * scale + bias;
                expected[i] = v > 0f ? v : 0f;
            }

            using var inBuf = accelerator.Allocate1D(x);
            using var outBuf = accelerator.Allocate1D<T>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<T, Stride1D.Dense>, ArrayView1D<T, Stride1D.Dense>, T, T>(
                FusedReluGeneric<T>);
            k(n, inBuf.View, outBuf.View, toT(scale), toT(bias));
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<T>();

            for (int i = 0; i < n; i++)
            {
                float g = toF(got[i]);
                float tol = MathF.Max(relTol, MathF.Abs(expected[i]) * relTol);
                if (MathF.Abs(g - expected[i]) > tol)
                    throw new Exception(
                        $"Generic {typeof(T).Name} kernel @{i} ({BackendName}): got {g}, want " +
                        $"{expected[i]} (tol {tol}) - by-value sub-word scalar params (scale/bias) " +
                        "must transpile + marshal correctly.");
            }
        });

        // Float8E4M3 overflow convention (Geordi 2026-06-17, FP8 ML-oracle validation). The DEFAULT
        // = fn (float8_e4m3fn): the bare cast operator AND FromSingleFn map finite overflow (>464)
        // AND +-Inf to NaN; 449..464 round DOWN to 448 (NOT NaN). The IR-level convert itself is fn
        // on every backend (the emitters' E4M3 f32->fp8 overflow branch -> 0x7F), so this verifies the
        // changed codegen. FromSingleSaturating is the OPT-IN clamp-to-+-448 path (composed of the fn
        // cast + a >464 redirect). All three are checked in-kernel vs the managed (oracle-proven) result.
        private static void Float8OverflowKernel(Index1D i,
            ArrayView1D<float, Stride1D.Dense> x,
            ArrayView1D<global::ILGPU.Float8E4M3, Stride1D.Dense> cast,
            ArrayView1D<global::ILGPU.Float8E4M3, Stride1D.Dense> sat)
        {
            cast[i] = (global::ILGPU.Float8E4M3)x[i];                       // operator = fn (the emitter)
            sat[i] = global::ILGPU.Float8E4M3.FromSingleSaturating(x[i]);   // opt-in saturating
        }

        // FromSingleSaturating (the opt-in NVIDIA-TE/OCP saturating cast) for Half / bf16 / E5M2
        // (Geordi 2026-06-17, data-type 100% parity sweep - E4M3 already had it). Finite overflow
        // clamps to max-finite instead of producing Inf; +-Inf stays Inf (these IEEE types have it);
        // NaN stays NaN. Each method is intrinsic-composed (default cast + bit-level finite check +
        // max-finite-constant cast), so it must transpile + match the managed result on every backend.
        private static void HalfSatKernel(Index1D i,
            ArrayView1D<float, Stride1D.Dense> x, ArrayView1D<global::ILGPU.Half, Stride1D.Dense> y) =>
            y[i] = global::ILGPU.Half.FromSingleSaturating(x[i]);
        private static void BF16SatKernel(Index1D i,
            ArrayView1D<float, Stride1D.Dense> x, ArrayView1D<global::ILGPU.BFloat16, Stride1D.Dense> y) =>
            y[i] = global::ILGPU.BFloat16.FromSingleSaturating(x[i]);
        private static void E5M2SatKernel(Index1D i,
            ArrayView1D<float, Stride1D.Dense> x, ArrayView1D<global::ILGPU.Float8E5M2, Stride1D.Dense> y) =>
            y[i] = global::ILGPU.Float8E5M2.FromSingleSaturating(x[i]);

        [TestMethod]
        public async Task LowPrecision_FromSingleSaturating_ClampsOverflow() => await RunTest(async acc =>
        {
            // (a) pin the managed clamp values (host): finite overflow -> max-finite; +-Inf -> Inf.
            static ushort RawH(global::ILGPU.Half h) => System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Half, ushort>(ref h);
            static ushort RawB(global::ILGPU.BFloat16 b) => System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.BFloat16, ushort>(ref b);
            static byte RawE(global::ILGPU.Float8E5M2 e) => System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Float8E5M2, byte>(ref e);
            void Eq(int got, int want, string w) { if (got != want) throw new Exception($"{w} ({BackendName}): got 0x{got:X}, want 0x{want:X}"); }
            Eq(RawH(global::ILGPU.Half.FromSingleSaturating(70000f)), 0x7BFF, "Half sat 70000->65504");
            Eq(RawH(global::ILGPU.Half.FromSingleSaturating(-70000f)), 0xFBFF, "Half sat -70000->-65504");
            Eq(RawH(global::ILGPU.Half.FromSingleSaturating(float.PositiveInfinity)), 0x7C00, "Half sat +Inf->Inf");
            Eq(RawB(global::ILGPU.BFloat16.FromSingleSaturating(float.MaxValue)), 0x7F7F, "bf16 sat MaxValue->0x7F7F");
            Eq(RawB(global::ILGPU.BFloat16.FromSingleSaturating(float.PositiveInfinity)), 0x7F80, "bf16 sat +Inf->Inf");
            Eq(RawE(global::ILGPU.Float8E5M2.FromSingleSaturating(70000f)), 0x7B, "E5M2 sat 70000->57344");
            Eq(RawE(global::ILGPU.Float8E5M2.FromSingleSaturating(float.PositiveInfinity)), 0x7C, "E5M2 sat +Inf->Inf");

            // (b) GPU-vs-managed across overflow / in-range / Inf / NaN, on every backend.
            float[] inputs = { 70000f, -70000f, float.MaxValue, -float.MaxValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN, 1.5f, -0.5f, 0f };
            int n = inputs.Length;
            ushort[] hExp = new ushort[n]; ushort[] bExp = new ushort[n]; byte[] eExp = new byte[n];
            for (int i = 0; i < n; i++)
            {
                hExp[i] = RawH(global::ILGPU.Half.FromSingleSaturating(inputs[i]));
                bExp[i] = RawB(global::ILGPU.BFloat16.FromSingleSaturating(inputs[i]));
                eExp[i] = RawE(global::ILGPU.Float8E5M2.FromSingleSaturating(inputs[i]));
            }
            using var inBuf = acc.Allocate1D(inputs);
            using (var oH = acc.Allocate1D<global::ILGPU.Half>(n))
            {
                acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<global::ILGPU.Half, Stride1D.Dense>>(HalfSatKernel)(n, inBuf.View, oH.View);
                await acc.SynchronizeAsync(); var g = await oH.CopyToHostAsync<global::ILGPU.Half>();
                for (int i = 0; i < n; i++) { ushort gb = RawH(g[i]); bool nan = (gb & 0x7C00) == 0x7C00 && (gb & 0x3FF) != 0 && (hExp[i] & 0x7C00) == 0x7C00 && (hExp[i] & 0x3FF) != 0; if (!nan && gb != hExp[i]) throw new Exception($"Half sat kernel @{i} ({BackendName}): in {inputs[i]} -> 0x{gb:X4}, want 0x{hExp[i]:X4}"); }
            }
            using (var oB = acc.Allocate1D<global::ILGPU.BFloat16>(n))
            {
                acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<global::ILGPU.BFloat16, Stride1D.Dense>>(BF16SatKernel)(n, inBuf.View, oB.View);
                await acc.SynchronizeAsync(); var g = await oB.CopyToHostAsync<global::ILGPU.BFloat16>();
                for (int i = 0; i < n; i++) { ushort gb = RawB(g[i]); bool nan = (gb & 0x7F80) == 0x7F80 && (gb & 0x7F) != 0 && (bExp[i] & 0x7F80) == 0x7F80 && (bExp[i] & 0x7F) != 0; if (!nan && gb != bExp[i]) throw new Exception($"bf16 sat kernel @{i} ({BackendName}): in {inputs[i]} -> 0x{gb:X4}, want 0x{bExp[i]:X4}"); }
            }
            using (var oE = acc.Allocate1D<global::ILGPU.Float8E5M2>(n))
            {
                acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<global::ILGPU.Float8E5M2, Stride1D.Dense>>(E5M2SatKernel)(n, inBuf.View, oE.View);
                await acc.SynchronizeAsync(); var g = await oE.CopyToHostAsync<global::ILGPU.Float8E5M2>();
                for (int i = 0; i < n; i++) { byte gb = RawE(g[i]); bool nan = (gb & 0x7C) == 0x7C && (gb & 0x03) != 0 && (eExp[i] & 0x7C) == 0x7C && (eExp[i] & 0x03) != 0; if (!nan && gb != eExp[i]) throw new Exception($"E5M2 sat kernel @{i} ({BackendName}): in {inputs[i]} -> 0x{gb:X2}, want 0x{eExp[i]:X2}"); }
            }
        });

        // Pin the on-device float->lowp conversions to HARDCODED values from the external authoritative
        // references (numpy.float16 / ml_dtypes.bfloat16 / float8_e4m3fn / float8_e5m2), via
        // DemoConsole -- bf16-f16-oracle / fp8-oracle (Geordi 2026-06-17). The other conversion tests
        // compare GPU-vs-managed (so they'd miss an IDENTICAL regression in both); this pins every
        // backend's convert to the reference itself, locking in the Half-RNE + FP8-fn fixes in CI.
        private static void RefConvertKernel<T>(Index1D i,
            ArrayView1D<float, Stride1D.Dense> x, ArrayView1D<T, Stride1D.Dense> y)
            where T : unmanaged, INumber<T> =>
            y[i] = PrecisionConvert.ConvertFromSingle<T>(x[i]);

        private async Task<T[]> ConvertOnDevice<T>(Accelerator acc, uint[] inBits)
            where T : unmanaged, INumber<T>
        {
            var inputs = new float[inBits.Length];
            for (int i = 0; i < inBits.Length; i++) inputs[i] = BitConverter.UInt32BitsToSingle(inBits[i]);
            using var inBuf = acc.Allocate1D(inputs);
            using var outBuf = acc.Allocate1D<T>(inBits.Length);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<T, Stride1D.Dense>>(RefConvertKernel<T>);
            k(inBits.Length, inBuf.View, outBuf.View);
            await acc.SynchronizeAsync();
            return await outBuf.CopyToHostAsync<T>();
        }

        [TestMethod]
        public async Task LowPrecision_ConversionPinnedToExternalReference() => await RunTest(async acc =>
        {
            // (f32 input bits, expected raw lowp bits) from numpy.float16. NaN row compared tolerant.
            uint[] hIn = { 855638016u, 3003129856u, 864026624u, 1199562752u, 1199566848u, 1200142336u, 1065357312u, 2139095040u, 2143289344u };
            ushort[] hExp = { 0x0000, 0x8001, 0x0001, 0x7BFF, 0x7C00, 0x7C00, 0x3C00, 0x7C00, 0x7E00 };
            var hGot = await ConvertOnDevice<global::ILGPU.Half>(acc, hIn);
            for (int i = 0; i < hIn.Length; i++)
            {
                ushort g = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Half, ushort>(ref hGot[i]);
                bool nan = (g & 0x7C00) == 0x7C00 && (g & 0x3FF) != 0 && (hExp[i] & 0x7C00) == 0x7C00 && (hExp[i] & 0x3FF) != 0;
                if (!nan && g != hExp[i]) throw new Exception($"Half convert @{i} ({BackendName}): in 0x{hIn[i]:X8} -> 0x{g:X4}, want numpy.float16 0x{hExp[i]:X4}.");
            }

            // ml_dtypes.bfloat16
            uint[] bIn = { 1065353216u, 1078530000u, 2139095040u, 2143289344u, 3223322624u, 1900671690u };
            ushort[] bExp = { 0x3F80, 0x4049, 0x7F80, 0x7FC0, 0xC020, 0x714A };
            var bGot = await ConvertOnDevice<global::ILGPU.BFloat16>(acc, bIn);
            for (int i = 0; i < bIn.Length; i++)
            {
                ushort g = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.BFloat16, ushort>(ref bGot[i]);
                bool nan = (g & 0x7F80) == 0x7F80 && (g & 0x7F) != 0 && (bExp[i] & 0x7F80) == 0x7F80 && (bExp[i] & 0x7F) != 0;
                if (!nan && g != bExp[i]) throw new Exception($"BFloat16 convert @{i} ({BackendName}): in 0x{bIn[i]:X8} -> 0x{g:X4}, want ml_dtypes.bfloat16 0x{bExp[i]:X4}.");
            }

            // float8_e4m3fn (fn: overflow -> NaN)
            uint[] e4In = { 1138753536u, 1138786304u, 1139277824u, 1139802112u, 1140850688u, 2139095040u, 4286578688u, 2143289344u, 1065353216u };
            byte[] e4Exp = { 0x7E, 0x7E, 0x7E, 0x7F, 0x7F, 0x7F, 0xFF, 0x7F, 0x38 };
            var e4Got = await ConvertOnDevice<global::ILGPU.Float8E4M3>(acc, e4In);
            for (int i = 0; i < e4In.Length; i++)
            {
                byte g = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Float8E4M3, byte>(ref e4Got[i]);
                bool nan = (g & 0x7F) == 0x7F && (e4Exp[i] & 0x7F) == 0x7F;
                if (!nan && g != e4Exp[i]) throw new Exception($"Float8E4M3 convert @{i} ({BackendName}): in 0x{e4In[i]:X8} -> 0x{g:X2}, want float8_e4m3fn 0x{e4Exp[i]:X2}.");
            }

            // float8_e5m2 (overflow -> Inf; NaN byte tolerant - ILGPU 0x7F vs ml_dtypes 0x7E, both valid)
            uint[] e5In = { 1197473792u, 1198522368u, 1200142336u, 2139095040u, 2143289344u, 1065353216u };
            byte[] e5Exp = { 0x7B, 0x7C, 0x7C, 0x7C, 0x7E, 0x3C };
            var e5Got = await ConvertOnDevice<global::ILGPU.Float8E5M2>(acc, e5In);
            for (int i = 0; i < e5In.Length; i++)
            {
                byte g = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Float8E5M2, byte>(ref e5Got[i]);
                bool nan = (g & 0x7C) == 0x7C && (g & 0x03) != 0 && (e5Exp[i] & 0x7C) == 0x7C && (e5Exp[i] & 0x03) != 0;
                if (!nan && g != e5Exp[i]) throw new Exception($"Float8E5M2 convert @{i} ({BackendName}): in 0x{e5In[i]:X8} -> 0x{g:X2}, want float8_e5m2 0x{e5Exp[i]:X2}.");
            }
        });

        // ILGPU.Half float->half must be round-to-nearest-even on EVERY backend (Geordi 2026-06-17,
        // bf16/Half oracle validation). The managed conversion is bit-exact to numpy.float16 (verified
        // DemoConsole -- bf16-f16-oracle, all 65536 patterns); this proves the WebGPU/WebGL/Wasm
        // emitters match it - they previously TRUNCATED + flushed all subnormals to zero (diverging
        // from numpy AND from CUDA/OpenCL which use native round-to-nearest). Subnormals + overflow +
        // RNE midpoints are the point.
        private static void HalfConvertKernel(Index1D i,
            ArrayView1D<float, Stride1D.Dense> x, ArrayView1D<global::ILGPU.Half, Stride1D.Dense> y) =>
            y[i] = (global::ILGPU.Half)x[i];

        [TestMethod]
        public async Task Half_FloatToHalf_RoundToNearestEven() => await RunTest(async accelerator =>
        {
            float[] inputs =
            {
                1f, 1.5f, -2.5f, 100.3f, 0.333f, 1024.7f,                 // normals
                1.00048828125f, 1.0014648438f, 0.99975586f,              // normal RNE midpoints near 1.0
                65504f, 65519f, 65520f, 65535f, 70000f, 1e30f,           // overflow region -> 65504 / Inf
                float.PositiveInfinity, float.NegativeInfinity, -65504f, -70000f,
                (float)Math.Pow(2, -24), (float)Math.Pow(2, -23), (float)Math.Pow(2, -15), // exact subnormals/boundary
                (float)Math.Pow(2, -25),                                  // tie -> +0 (even)
                (float)(Math.Pow(2, -25) * 1.5),                          // -> smallest subnormal
                (float)Math.Pow(2, -26),                                  // -> +0
                -2.9831426E-08f,                                          // the original failing case -> -smallest subnormal
                0f, -0f, float.NaN, 5.96e-8f, 1e-7f,
            };
            int n = inputs.Length;
            var expected = new ushort[n];
            for (int i = 0; i < n; i++)
            {
                var v = (global::ILGPU.Half)inputs[i];   // managed = bit-exact numpy.float16 (oracle-proven)
                expected[i] = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Half, ushort>(ref v);
            }

            using var inBuf = accelerator.Allocate1D(inputs);
            using var outBuf = accelerator.Allocate1D<global::ILGPU.Half>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<global::ILGPU.Half, Stride1D.Dense>>(HalfConvertKernel);
            k(n, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<global::ILGPU.Half>();

            for (int i = 0; i < n; i++)
            {
                ushort g = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Half, ushort>(ref got[i]);
                bool bothNaN = (g & 0x7C00) == 0x7C00 && (g & 0x03FF) != 0
                    && (expected[i] & 0x7C00) == 0x7C00 && (expected[i] & 0x03FF) != 0;
                if (!bothNaN && g != expected[i])
                    throw new Exception(
                        $"float->Half kernel @{i} ({BackendName}): input {inputs[i]} -> got 0x{g:X4}, " +
                        $"want 0x{expected[i]:X4} (must be round-to-nearest-even incl subnormals, matching numpy/managed).");
            }
        });

        [TestMethod]
        public async Task Float8E4M3_FromSingleFn_OverflowToNaN() => await RunTest(async accelerator =>
        {
            float[] inputs =
            {
                465f, 480f, 512f, 1000f, 1e30f, float.PositiveInfinity,  // >464 finite + Inf -> NaN
                -465f, -480f, -512f, -1e30f, float.NegativeInfinity,     // negatives
                448f, 449f, 463f, 464f, -448f, -449f, -464f,             // round-to-448 region (finite, NOT NaN)
                1f, 1.25f, 256f, -2.5f, 0.5f, 0.001953125f, 0f, -0f, float.NaN,
            };
            int n = inputs.Length;
            var expCast = new byte[n];   // fn (operator)
            var expSat = new byte[n];    // saturating
            for (int i = 0; i < n; i++)
            {
                var c = (global::ILGPU.Float8E4M3)inputs[i];                       // managed fn = proven reference
                var s = global::ILGPU.Float8E4M3.FromSingleSaturating(inputs[i]);
                expCast[i] = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Float8E4M3, byte>(ref c);
                expSat[i] = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Float8E4M3, byte>(ref s);
            }

            using var inBuf = accelerator.Allocate1D(inputs);
            using var castBuf = accelerator.Allocate1D<global::ILGPU.Float8E4M3>(n);
            using var satBuf = accelerator.Allocate1D<global::ILGPU.Float8E4M3>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<global::ILGPU.Float8E4M3, Stride1D.Dense>,
                ArrayView1D<global::ILGPU.Float8E4M3, Stride1D.Dense>>(Float8OverflowKernel);
            k(n, inBuf.View, castBuf.View, satBuf.View);
            await accelerator.SynchronizeAsync();
            var gotCast = await castBuf.CopyToHostAsync<global::ILGPU.Float8E4M3>();
            var gotSat = await satBuf.CopyToHostAsync<global::ILGPU.Float8E4M3>();

            for (int i = 0; i < n; i++)
            {
                byte gc = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Float8E4M3, byte>(ref gotCast[i]);
                byte gs = System.Runtime.CompilerServices.Unsafe.As<global::ILGPU.Float8E4M3, byte>(ref gotSat[i]);
                bool castOk = gc == expCast[i] || ((gc & 0x7F) == 0x7F && (expCast[i] & 0x7F) == 0x7F);
                bool satOk = gs == expSat[i] || ((gs & 0x7F) == 0x7F && (expSat[i] & 0x7F) == 0x7F);
                if (!castOk)
                    throw new Exception(
                        $"fn cast operator kernel @{i} ({BackendName}): input {inputs[i]} -> got 0x{gc:X2}, " +
                        $"want 0x{expCast[i]:X2} (fn: >464/+-Inf must be NaN, 449..464 must round to +-448).");
                if (!satOk)
                    throw new Exception(
                        $"FromSingleSaturating kernel @{i} ({BackendName}): input {inputs[i]} -> got 0x{gs:X2}, " +
                        $"want 0x{expSat[i]:X2} (saturating: finite overflow -> +-448, +-Inf -> NaN).");
            }
        });
    }
}
