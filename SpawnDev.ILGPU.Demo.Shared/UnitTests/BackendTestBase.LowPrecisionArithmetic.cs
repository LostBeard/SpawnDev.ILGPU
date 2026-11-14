using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Half / BFloat16 arithmetic must round each result to the type, as the managed operators do ((T)((float)a op
    // (float)b)). Backends that hold the type in an f32 register compute the operation in f32; this checks whether the
    // result is rounded back to the type before it is used (written here as a float, so an unrounded result shows).
    public abstract partial class BackendTestBase
    {
        public static void LowPrecisionArithmeticKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> half4, ArrayView<float> bf4)
        {
            var ha = (global::ILGPU.Half)a[i];
            var hb = (global::ILGPU.Half)b[i];
            half4[4 * i + 0] = (float)(ha + hb);
            half4[4 * i + 1] = (float)(ha - hb);
            half4[4 * i + 2] = (float)(ha * hb);
            half4[4 * i + 3] = (float)(ha / hb);
            var ba = (BFloat16)a[i];
            var bb = (BFloat16)b[i];
            bf4[4 * i + 0] = (float)(ba + bb);
            bf4[4 * i + 1] = (float)(ba - bb);
            bf4[4 * i + 2] = (float)(ba * bb);
            bf4[4 * i + 3] = (float)(ba / bb);
        }

        [TestMethod]
        public async Task LowPrecisionArithmetic_HalfAndBFloat16_RoundEachResult() => await RunTest(LowPrecisionArithmeticCheck);

        /// <summary>
        /// The same on WebGPU's EMULATED f16 path (a browser without <c>shader-f16</c>), where Half lives in an f32. Not
        /// RunTest: the base caches one accelerator per class, so the switch must be set before an accelerator of our own
        /// is created (see Half_HalfToFloat_ExactAllPatterns_WebGPUEmulatedF16).
        /// </summary>
        [TestMethod]
        public async Task LowPrecisionArithmetic_HalfAndBFloat16_RoundEachResult_WebGPUEmulatedF16()
        {
            var before = SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.ForceEmulatedF16;
            SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.ForceEmulatedF16 = true;
            try
            {
                var (context, accelerator) = await CreateAcceleratorAsync();
                try
                {
                    if (accelerator is not SpawnDev.ILGPU.WebGPU.WebGPUAccelerator webGpu)
                        throw new UnsupportedTestException("WebGPU's emulated f16 path only");
                    if (webGpu.Backend.HasShaderF16)
                        throw new Exception("ForceEmulatedF16 did not take: the backend still reports native shader-f16");
                    await LowPrecisionArithmeticCheck(accelerator);
                }
                finally
                {
                    accelerator.Dispose();
                    context.Dispose();
                }
            }
            finally
            {
                SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.ForceEmulatedF16 = before;
            }
        }

        private async Task LowPrecisionArithmeticCheck(Accelerator accelerator)
        {
            // four stores per thread at 4 * i + slot: the positional multi-store layout WebGL's Transform Feedback supports
            var rng = new Random(20261114);
            int n = 4096;
            var a = new float[n];
            var b = new float[n];
            for (int k = 0; k < n; k++)
            {
                a[k] = (float)((rng.NextDouble() * 2 - 1) * Math.Pow(2, rng.Next(-6, 8)));
                b[k] = (float)((rng.NextDouble() * 2 - 1) * Math.Pow(2, rng.Next(-6, 8))) + 0.001f;
            }
            using var dA = accelerator.Allocate1D(a);
            using var dB = accelerator.Allocate1D(b);
            using var dH = accelerator.Allocate1D<float>(4 * n);
            using var dF = accelerator.Allocate1D<float>(4 * n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(LowPrecisionArithmeticKernel);
            kernel(n, dA.View, dB.View, dH.View, dF.View);
            var h = await dH.CopyToHostAsync<float>();
            var f = await dF.CopyToHostAsync<float>();
            var names = new[] { "+", "-", "*", "/" };
            var badH = new int[4];
            var badF = new int[4];
            string first = "";
            for (int k = 0; k < n; k++)
            {
                var ha = (global::ILGPU.Half)a[k]; var hb = (global::ILGPU.Half)b[k];
                var ba = (BFloat16)a[k]; var bb = (BFloat16)b[k];
                float[] eh = { (float)(ha + hb), (float)(ha - hb), (float)(ha * hb), (float)(ha / hb) };
                float[] ef = { (float)(ba + bb), (float)(ba - bb), (float)(ba * bb), (float)(ba / bb) };
                for (int o = 0; o < 4; o++)
                {
                    if (!SameFloat(h[4 * k + o], eh[o])) { if (badH[o]++ == 0 && first == "") first = $"Half {a[k]:R} {names[o]} {b[k]:R} = {h[4 * k + o]:R}, expected {eh[o]:R}"; }
                    if (!SameFloat(f[4 * k + o], ef[o])) { if (badF[o]++ == 0 && first == "") first = $"BFloat16 {a[k]:R} {names[o]} {b[k]:R} = {f[4 * k + o]:R}, expected {ef[o]:R}"; }
                }
            }
            int total = 0;
            for (int o = 0; o < 4; o++) total += badH[o] + badF[o];
            if (total > 0)
                throw new Exception($"{accelerator.AcceleratorType}: of {n}: Half + - * / wrong {badH[0]} / {badH[1]} / {badH[2]} / {badH[3]}; BFloat16 {badF[0]} / {badF[1]} / {badF[2]} / {badF[3]} (first: {first})");
        }
    }
}
