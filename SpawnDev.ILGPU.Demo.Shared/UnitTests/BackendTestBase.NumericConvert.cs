using System;
using System.Numerics;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Half = ILGPU.Half;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Verifies NumericConvert.ToFloat32<T> transpiles + monomorphizes per concrete T — Tuvok's f16
    // generic-kernel collapse: ONE generic kernel over the weight type TW (= float | Half), widening
    // TW->float for fp32 accumulation (ORT-parity). The C# generic-math convert float.CreateTruncating<TW>
    // fails to lower (it inspects typeof(TW)); NumericConvert.ToFloat32 is the transpilable replacement.
    // Runs the SAME generic kernel source with TW = Half (Half->float convert) AND TW = float (identity).
    public abstract partial class BackendTestBase
    {
        // c[i] = a[i] * (float)b[i] + 1  — generic over the weight type TW, fp32 accumulate
        static void GenericConvertKernel<TW>(Index1D i, ArrayView<float> a, ArrayView<TW> b, ArrayView<float> c)
            where TW : unmanaged, INumber<TW>
            => c[i] = a[i] * NumericConvert.ToFloat32(b[i]) + 1.0f;

        [TestMethod]
        public async Task NumericConvert_GenericWeightKernel_Transpiles() => await RunTest(async accelerator =>
        {
            const int n = 16;
            var a = new float[n];
            for (int j = 0; j < n; j++) a[j] = j * 0.5f;

            // TW = Half (the f16 weight case — the convert is Half->float)
            var bHalf = new Half[n];
            for (int j = 0; j < n; j++) bHalf[j] = (Half)(j * 0.25f);
            using (var aBuf = accelerator.Allocate1D(a))
            using (var bBuf = accelerator.Allocate1D(bHalf))
            using (var cBuf = accelerator.Allocate1D<float>(n))
            {
                var k = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<float>, ArrayView<Half>, ArrayView<float>>(GenericConvertKernel<Half>);
                k((Index1D)n, aBuf.View, bBuf.View, cBuf.View);
                await accelerator.SynchronizeAsync();
                var got = await cBuf.CopyToHostAsync<float>();
                for (int j = 0; j < n; j++)
                {
                    float expected = a[j] * (float)bHalf[j] + 1.0f;
                    if (MathF.Abs(got[j] - expected) > 0.02f * MathF.Abs(expected) + 0.02f)
                        throw new Exception(
                            $"Half-weight wrong at {j}: a={a[j]} b={(float)bHalf[j]} expected {expected} got {got[j]} (ToFloat32<Half> mislowered?).");
                }
            }

            // TW = float (the convert must be identity)
            var bFloat = new float[n];
            for (int j = 0; j < n; j++) bFloat[j] = j * 0.25f;
            using (var aBuf = accelerator.Allocate1D(a))
            using (var bBuf = accelerator.Allocate1D(bFloat))
            using (var cBuf = accelerator.Allocate1D<float>(n))
            {
                var k = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(GenericConvertKernel<float>);
                k((Index1D)n, aBuf.View, bBuf.View, cBuf.View);
                await accelerator.SynchronizeAsync();
                var got = await cBuf.CopyToHostAsync<float>();
                for (int j = 0; j < n; j++)
                {
                    float expected = a[j] * bFloat[j] + 1.0f;
                    if (MathF.Abs(got[j] - expected) > 0.001f * MathF.Abs(expected) + 0.001f)
                        throw new Exception(
                            $"float-weight wrong at {j}: expected {expected} got {got[j]} (ToFloat32<float> not identity?).");
                }
            }
        });
    }
}
