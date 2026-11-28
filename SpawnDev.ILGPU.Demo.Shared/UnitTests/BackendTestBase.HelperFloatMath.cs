using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Float division, sqrt, sin and log inside a [MethodImpl(NoInlining)] helper. WebGL emits such a helper as its own GLSL
    // function ABOVE the kernel header, where the correctly rounded _f32_div_rn / _f32_sqrt_rn and the accurate trig / log
    // are defined, so the shader failed to compile ("'_f32_div_rn' : no matching overloaded function found"; Tuvok
    // 2026-11-28, SpawnDev.ILGPU.ML Trellis2.KernelMath.Activate: SiLU v / (1 + exp(-v)), tanh (e - 1) / (e + 1)).
    // Same shapes, every backend, checked against .NET.
    public abstract partial class BackendTestBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float HelperSilu(float v) => v / (1f + MathF.Exp(-v));

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float HelperTanhRatio(float v)
        {
            float e = MathF.Exp(2f * v);
            return (e - 1f) / (e + 1f);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float HelperSqrtSinLog(float v) => MathF.Sqrt(MathF.Abs(v) + 1f) + MathF.Sin(v) + MathF.Log(MathF.Abs(v) + 2f);

        // a helper calling a helper: the inner one is emitted first, the outer one between it and the kernel
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float HelperOuter(float v) => HelperSilu(v) / (1f + 0.3275911f * MathF.Abs(v));

        static void HelperFloatMathKernel(Index1D i, ArrayView<float> x, ArrayView<float> outp)
        {
            float v = x[i];
            outp[i * 4] = HelperSilu(v);
            outp[i * 4 + 1] = HelperTanhRatio(v);
            outp[i * 4 + 2] = HelperSqrtSinLog(v);
            outp[i * 4 + 3] = HelperOuter(v);
        }

        [TestMethod]
        public async Task HelperFunction_FloatDivisionSqrtSinLog_CompileAndMatchHost() => await RunTest(async accelerator =>
        {
            const int n = 256;
            var input = new float[n];
            for (int k = 0; k < n; k++) input[k] = (k - 128) * 0.0625f;
            using var x = accelerator.Allocate1D(input);
            using var outp = accelerator.Allocate1D<float>(n * 4);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(HelperFloatMathKernel)(n, x.View, outp.View);
            await accelerator.SynchronizeAsync();
            var got = await outp.CopyToHostAsync<float>();
            var errors = new System.Collections.Generic.List<string>();
            string[] names = { "silu", "tanh ratio", "sqrt+sin+log", "helper calling helper" };
            for (int k = 0; k < n && errors.Count < 4; k++)
            {
                float v = input[k];
                float[] want = { HelperSilu(v), HelperTanhRatio(v), HelperSqrtSinLog(v), HelperOuter(v) };
                for (int c = 0; c < 4; c++)
                {
                    // exp / sin / log are not correctly rounded on every backend: a few ulp of f32
                    float w = want[c], g = got[k * 4 + c];
                    if (!(Math.Abs(g - w) <= 1e-5f * Math.Max(1f, Math.Abs(w))))
                        errors.Add($"{names[c]}({v:R}) = {g:R}, expected {w:R}");
                }
            }
            if (errors.Count > 0) throw new Exception(string.Join("; ", errors));
        });
    }
}
