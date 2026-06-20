using System;
using System.Runtime.CompilerServices;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using QInt4 = ILGPU.QInt4;
using QUInt4 = ILGPU.QUInt4;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Companion to BackendTestBase.PackedFloat4Helper: the packed 4-bit INTEGER types (QInt4 signed,
    // QUInt4 unsigned) loaded through a [NoInlining] helper must also decode correctly (8 nibbles/word,
    // sign-extend for QInt4 / zero-extend for QUInt4) via the per-backend helper-function generators.
    public abstract partial class BackendTestBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int QInt4HelperLoad(ArrayView<QInt4> v, int i) => v[i];
        static void QInt4ViaHelperKernel(Index1D i, ArrayView<QInt4> packed, ArrayView<int> outI)
            => outI[i] = QInt4HelperLoad(packed, i.X);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int QUInt4HelperLoad(ArrayView<QUInt4> v, int i) => v[i];
        static void QUInt4ViaHelperKernel(Index1D i, ArrayView<QUInt4> packed, ArrayView<int> outI)
            => outI[i] = QUInt4HelperLoad(packed, i.X);

        [TestMethod]
        public async Task PackedQInt4_LoadViaNoInliningHelper_SignExtended() => await RunTest(async accelerator =>
        {
            const int n = 64;
            var expected = new int[n];
            for (int i = 0; i < n; i++) expected[i] = (i % 16) - 8; // -8..7 at both nibble positions
            var packed = new byte[(n + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)((expected[2 * k] & 0xF) | ((expected[2 * k + 1] & 0xF) << 4));

            using var xBuf = accelerator.Allocate1D<QInt4>(n);
            using var outBuf = accelerator.Allocate1D<int>(n);
            ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QInt4>, ArrayView<int>>(QInt4ViaHelperKernel)(n, xBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"QInt4 via NoInlining helper at [{i}] ({BackendName}): got {got[i]}, expected {expected[i]} (sign-extended).");
        });

        [TestMethod]
        public async Task PackedQUInt4_LoadViaNoInliningHelper_ZeroExtended() => await RunTest(async accelerator =>
        {
            const int n = 64;
            var expected = new int[n];
            for (int i = 0; i < n; i++) expected[i] = i % 16; // 0..15 at both nibble positions
            var packed = new byte[(n + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)((expected[2 * k] & 0xF) | ((expected[2 * k + 1] & 0xF) << 4));

            using var xBuf = accelerator.Allocate1D<QUInt4>(n);
            using var outBuf = accelerator.Allocate1D<int>(n);
            ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QUInt4>, ArrayView<int>>(QUInt4ViaHelperKernel)(n, xBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"QUInt4 via NoInlining helper at [{i}] ({BackendName}): got {got[i]}, expected {expected[i]} (zero-extended).");
        });
    }
}
