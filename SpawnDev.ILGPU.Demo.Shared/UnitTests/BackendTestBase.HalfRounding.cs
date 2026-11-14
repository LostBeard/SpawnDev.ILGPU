using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // float -> Half -> float in a kernel must round to fp16 (round to nearest even) as .NET does. On OpenCL without native
    // half the conversion was `(float)(float)x`, an identity: ML's exact hi + lo fp16 split of fp32 VAE weights got lo = 0
    // for all 1,179,648 weights (SpawnDev.ILGPU.ML SdTensorStreamer.FromF32LoImpl, Tuvok 2026-11-14; CUDA rounded).
    // Bit-exact against the host's ILGPU.Half conversion (IEEE round to nearest even), through PrecisionConvert and
    // through the Half casts, over normals, fp16 subnormals, values past 65504 and exact ties.
    public abstract partial class BackendTestBase
    {
        public static void HalfRoundTripKernel(Index1D i, ArrayView<float> src, ArrayView<float> viaPrecision, ArrayView<float> viaCast, ArrayView<float> lo)
        {
            float v = src[i];
            float a = PrecisionConvert.ConvertToSingle(PrecisionConvert.ConvertFromSingle<global::ILGPU.Half>(v));
            viaPrecision[i] = a;
            viaCast[i] = (float)(global::ILGPU.Half)v;
            lo[i] = v - a;
        }

        [TestMethod]
        public async Task HalfRoundTrip_FloatToHalfToFloat_RoundsAsDotNet() => await RunTest(async accelerator =>
        {
            var rng = new Random(20261114);
            var input = new System.Collections.Generic.List<float>();
            for (int k = 0; k < 8192; k++)
                input.Add((float)((rng.NextDouble() * 2 - 1) * Math.Pow(2, rng.Next(-30, 18))));
            // exact ties between two fp16 values (mantissa bit 12 set, lower bits clear): ties to even
            for (int k = 0; k < 512; k++)
            {
                int e = rng.Next(103, 142);
                int m = (rng.Next(0, 1024) << 13) | 0x1000;
                input.Add(BitConverter.Int32BitsToSingle((e << 23) | m | (rng.Next(2) << 31)));
            }
            // fp16 subnormal range and around 65504
            foreach (float f in new[] { 5.96e-8f, 2.98e-8f, 1e-6f, 6.0e-5f, 6.1e-5f, 65504f, 65519f, 65520f, 70000f, -65520f, 0f, -0f })
                input.Add(f);
            int n = input.Count;
            var src = input.ToArray();
            using var dSrc = accelerator.Allocate1D(src);
            using var dP = accelerator.Allocate1D<float>(n);
            using var dC = accelerator.Allocate1D<float>(n);
            using var dL = accelerator.Allocate1D<float>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(HalfRoundTripKernel);
            kernel(n, dSrc.View, dP.View, dC.View, dL.View);
            var p = await dP.CopyToHostAsync<float>();
            var c = await dC.CopyToHostAsync<float>();
            var l = await dL.CopyToHostAsync<float>();
            int badP = 0, badC = 0, badL = 0, zeroLo = 0, nonzeroLoExpected = 0;
            string firstP = "", firstC = "";
            for (int k = 0; k < n; k++)
            {
                float e = (float)(global::ILGPU.Half)src[k];
                float el = src[k] - e;
                if (BitConverter.SingleToInt32Bits(p[k]) != BitConverter.SingleToInt32Bits(e))
                { if (badP++ == 0) firstP = $"{src[k]:R} -> {p[k]:R}, expected {e:R}"; }
                if (BitConverter.SingleToInt32Bits(c[k]) != BitConverter.SingleToInt32Bits(e))
                { if (badC++ == 0) firstC = $"{src[k]:R} -> {c[k]:R}, expected {e:R}"; }
                if (!float.IsNaN(el) && BitConverter.SingleToInt32Bits(l[k]) != BitConverter.SingleToInt32Bits(el)) badL++;
                if (el != 0 && !float.IsNaN(el)) { nonzeroLoExpected++; if (l[k] == 0) zeroLo++; }
            }
            if (badP + badC + badL > 0)
                throw new Exception($"{accelerator.AcceleratorType}: of {n}: PrecisionConvert round trip {badP} differ from .NET (first: {firstP}); Half cast {badC} (first: {firstC}); low parts {badL} ({zeroLo} of {nonzeroLoExpected} non-zero low parts came out 0)");
        });
    }
}
