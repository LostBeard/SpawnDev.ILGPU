using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using QInt4 = ILGPU.QInt4;
using Float4E2M1 = ILGPU.Float4E2M1;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Regression for the packed-4-bit DIRECT-param view.Length over-count. In-kernel view.Length for a
    // packed-4-bit buffer must be the TRUE element count, not arrayLength()*8 (which ROUNDS UP to the
    // 32-bit-word boundary). For N not a multiple of 8 - e.g. N=180 -> ceil(180/2)=90 bytes -> padded 92
    // -> 23 u32 words -> arrayLength*8 = 184 - the over-count makes a grid-stride loop bounded by
    // view.Length process phantom padding nibbles. (The body-struct radix path was fixed via ViewCountSlot
    // at 4.14.0-local.12; this is the direct-param GetViewLength sibling.) LOAD-only (reads .Length), so
    // it runs on all 6 backends - no packed store involved.
    public abstract partial class BackendTestBase
    {
        // Each thread writes the view's element count into its OWN output slot (WebGL-safe: one store /
        // thread at its own index). Reading packed.Length emits the GetViewLength IR node under test.
        static void QInt4DirectViewLengthKernel(Index1D i, ArrayView<QInt4> packed, ArrayView<int> outLen)
            => outLen[i] = (int)packed.Length;
        static void Float4DirectViewLengthKernel(Index1D i, ArrayView<Float4E2M1> packed, ArrayView<int> outLen)
            => outLen[i] = (int)packed.Length;

        [TestMethod]
        public async Task PackedQInt4_DirectParamViewLength_ExactAtNonMultipleOf8() => await RunTest(async accelerator =>
        {
            const int n = 180; // NOT a multiple of 8 (arrayLength*8 would report 184)
            using var xBuf = accelerator.Allocate1D<QInt4>(n);
            using var outBuf = accelerator.Allocate1D<int>(n);
            // contents irrelevant - the test only reads .Length - but seed the packed bytes so the buffer is initialized
            ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView().CopyFromCPU(new byte[(n + 1) / 2]);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QInt4>, ArrayView<int>>(QInt4DirectViewLengthKernel)(n, xBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != n)
                    throw new Exception($"direct-param ArrayView<QInt4>.Length at N={n} ({BackendName}) thread {i}: got {got[i]}, expected {n} - arrayLength*8 rounds UP to the word boundary (phantom padding elements).");
        });

        [TestMethod]
        public async Task PackedFloat4_DirectParamViewLength_ExactAtNonMultipleOf8() => await RunTest(async accelerator =>
        {
            const int n = 180;
            using var xBuf = accelerator.Allocate1D<Float4E2M1>(n);
            using var outBuf = accelerator.Allocate1D<int>(n);
            ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView().CopyFromCPU(new byte[(n + 1) / 2]);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Float4E2M1>, ArrayView<int>>(Float4DirectViewLengthKernel)(n, xBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != n)
                    throw new Exception($"direct-param ArrayView<Float4E2M1>.Length at N={n} ({BackendName}) thread {i}: got {got[i]}, expected {n}.");
        });
    }
}
