using System;
using System.Runtime.CompilerServices;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Float4E2M1 = ILGPU.Float4E2M1;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A [NoInlining] helper that LOADS a packed FP4 view forces the load through the per-backend HELPER-
    // function generator (e.g. WGSL's WGSLFunctionGenerator), NOT the kernel generator. Regression that the
    // helper-gen FP4 path uses the SAME packed 8-nibbles/word addressing as the kernel-gen - it was stale at
    // 4-per-word / 1-byte (the pre-packing layout), so an FP4 view read inside a NoInlining helper decoded
    // the wrong nibble. Fp4Oracle lives in BackendTestBase.FromRawBits (same partial class).
    public abstract partial class BackendTestBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static float Fp4HelperLoad(ArrayView<Float4E2M1> v, int i) => v[i];

        static void Fp4ViaHelperKernel(Index1D i, ArrayView<Float4E2M1> packed, ArrayView<float> outF)
            => outF[i] = Fp4HelperLoad(packed, i.X);

        [TestMethod]
        public async Task PackedFloat4_LoadViaNoInliningHelper_8NibblesPerWord() => await RunTest(async accelerator =>
        {
            const int n = 64; // spans all 16 codes at both nibble positions, across multiple words
            var codes = new byte[n];
            for (int i = 0; i < n; i++) codes[i] = (byte)(i % 16);
            var packed = new byte[(n + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)((codes[2 * k] & 0xF) | ((codes[2 * k + 1] & 0xF) << 4));

            using var xBuf = accelerator.Allocate1D<Float4E2M1>(n);
            using var outBuf = accelerator.Allocate1D<float>(n);
            ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Float4E2M1>, ArrayView<float>>(Fp4ViaHelperKernel)(n, xBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                float expected = Fp4Oracle(codes[i]);
                if (got[i] != expected && !(float.IsNaN(got[i]) && float.IsNaN(expected)))
                    throw new Exception($"FP4 via NoInlining helper at [{i}] ({BackendName}): got {got[i]}, expected {expected} - helper-gen FP4 must use 8-nibbles/word packed addressing (was stale at 4-per-word).");
            }
        });
    }
}
