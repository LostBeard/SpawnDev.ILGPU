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
    }
}
