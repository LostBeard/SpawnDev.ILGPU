using System;
using System.Numerics;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Half = ILGPU.Half;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Verifies generic-math (C# 11 System.Numerics) over the kernel-native ILGPU.Half — the path that
    // failed with "BitCast" when forced onto System.Half. Half now implements the operator interfaces,
    // so a generic-math helper binds to Half's transpilable [MathIntrinsic] operators. This test compiles
    // the helper to a kernel and runs it on every backend, asserting the FP32 reference.
    public abstract partial class BackendTestBase
    {
        // Standard generic-math constraint (what a consumer writes). Exercises the operators, the
        // identities (T.One / T.Zero), Abs, and Clamp — all of which must lower to Half's transpilable
        // FP32 path on every backend.
        static T GenericMathCompute<T>(T x) where T : INumber<T>
            => T.Abs(x * x - T.One) + T.Clamp(x, T.Zero, T.One); // abs(x*x - 1) + clamp(x, 0, 1)

        static void GenericMathHalfKernel(Index1D i, ArrayView<Half> a, ArrayView<Half> outp)
            => outp[i] = GenericMathCompute(a[i]);

        [TestMethod]
        public async Task GenericMathHalf_Transpiles() => await RunTest(async accelerator =>
        {
            const int n = 16;
            var src = new Half[n];
            for (int j = 0; j < n; j++) src[j] = (Half)(j * 0.25f);

            using var input = accelerator.Allocate1D(src);
            using var output = accelerator.Allocate1D<Half>(n);

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Half>, ArrayView<Half>>(
                GenericMathHalfKernel);
            k((Index1D)n, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var got = await output.CopyToHostAsync<Half>();
            for (int j = 0; j < n; j++)
            {
                float xf = (float)src[j];
                float expected = MathF.Abs(xf * xf - 1f) + Math.Clamp(xf, 0f, 1f);
                float actual = (float)got[j];
                if (MathF.Abs(actual - expected) > 0.02f * MathF.Abs(expected) + 0.02f)
                    throw new Exception(
                        $"GenericMathHalf wrong at {j}: x={xf} expected {expected} got {actual} (generic-math dispatch mislowered?).");
            }
        });
    }
}
