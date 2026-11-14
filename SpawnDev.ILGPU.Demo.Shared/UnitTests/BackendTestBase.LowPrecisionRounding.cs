using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // float -> T -> float in registers must round to T's grid, as the managed conversion does, for every low-precision type
    // the GPU backends emulate in an f32 register (BFloat16, Float8E4M3, Float8E5M2, Float4E2M1). The backends rounded only
    // at a store to a T buffer, so a conversion was the identity: SpawnDev.ILGPU.ML's exact bf16 hi + lo split of FLUX.1
    // Krea's fp16 biases got lo = 0 on CUDA / OpenCL / WebGPU / WebGL / Wasm (Tuvok, 2026-11-14; the Half twin was fixed in
    // 5.4.32). Bit-exact against the host's ILGPU conversions (NaN equals NaN), through PrecisionConvert and the casts.
    public abstract partial class BackendTestBase
    {
        public static void LowPrecisionRoundTripKernel(Index1D i, ArrayView<float> src, ArrayView<float> bf16, ArrayView<float> e4m3, ArrayView<float> e5m2, ArrayView<float> e2m1, ArrayView<float> bf16Cast)
        {
            float v = src[i];
            bf16[i] = PrecisionConvert.ConvertToSingle(PrecisionConvert.ConvertFromSingle<BFloat16>(v));
            e4m3[i] = PrecisionConvert.ConvertToSingle(PrecisionConvert.ConvertFromSingle<Float8E4M3>(v));
            e5m2[i] = PrecisionConvert.ConvertToSingle(PrecisionConvert.ConvertFromSingle<Float8E5M2>(v));
            e2m1[i] = PrecisionConvert.ConvertToSingle(PrecisionConvert.ConvertFromSingle<Float4E2M1>(v));
            bf16Cast[i] = (float)(BFloat16)v;
        }

        static bool SameFloat(float a, float b) =>
            float.IsNaN(a) ? float.IsNaN(b) : BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        [TestMethod]
        public async Task LowPrecisionRoundTrip_FloatToTypeToFloat_RoundsAsHost() => await RunTest(async accelerator =>
        {
            var rng = new Random(20261114);
            var input = new float[8192];
            for (int k = 0; k < input.Length; k++)
                input[k] = (float)((rng.NextDouble() * 2 - 1) * Math.Pow(2, rng.Next(-12, 10)));
            int n = input.Length;
            using var dSrc = accelerator.Allocate1D(input);
            using var dB = accelerator.Allocate1D<float>(n);
            using var d4 = accelerator.Allocate1D<float>(n);
            using var d5 = accelerator.Allocate1D<float>(n);
            using var d2 = accelerator.Allocate1D<float>(n);
            using var dBc = accelerator.Allocate1D<float>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(LowPrecisionRoundTripKernel);
            kernel(n, dSrc.View, dB.View, d4.View, d5.View, d2.View, dBc.View);
            var b = await dB.CopyToHostAsync<float>();
            var e4 = await d4.CopyToHostAsync<float>();
            var e5 = await d5.CopyToHostAsync<float>();
            var e2 = await d2.CopyToHostAsync<float>();
            var bc = await dBc.CopyToHostAsync<float>();
            var failures = new System.Collections.Generic.List<string>();
            void Check(string name, float[] got, Func<float, float> host)
            {
                int bad = 0; string first = "";
                for (int k = 0; k < n; k++)
                {
                    float e = host(input[k]);
                    if (!SameFloat(got[k], e)) { if (bad++ == 0) first = $"{input[k]:R} -> {got[k]:R}, expected {e:R}"; }
                }
                if (bad > 0) failures.Add($"{name}: {bad} of {n} (first: {first})");
            }
            Check("BFloat16", b, v => (float)(BFloat16)v);
            Check("Float8E4M3", e4, v => (float)(Float8E4M3)v);
            Check("Float8E5M2", e5, v => (float)(Float8E5M2)v);
            Check("Float4E2M1", e2, v => (float)(Float4E2M1)v);
            Check("BFloat16 cast", bc, v => (float)(BFloat16)v);
            if (failures.Count > 0)
                throw new Exception($"{accelerator.AcceleratorType}: " + string.Join("; ", failures));
        });
    }
}
