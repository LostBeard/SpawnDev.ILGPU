using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using QUInt4 = ILGPU.QUInt4;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Packed 4-bit QUInt4 (the UNSIGNED companion of QInt4): 2 nibbles/byte, ArrayView<QUInt4> of N
    // allocates ceil(N/2) bytes. LOAD decodes the (idx&1) nibble of byte (idx>>1) and ZERO-extends
    // (0..15), where QInt4 sign-extends (-8..7). This is the correctness fork that makes the unsigned
    // type meaningful: code 0xF must read back as 15, not -1. Mirrors BackendTestBase.PackedQInt4.
    public abstract partial class BackendTestBase
    {
        static void QUInt4LoadKernel(Index1D i, ArrayView<QUInt4> x, ArrayView<int> y) => y[i] = x[i];
        // A QUInt4 CONSTANT (13, a high code) converted to int + float in-kernel - exercises the IR
        // const-fold/convert path (Convert.cs). Zero-extend gives 13/13.0; a sign-extend bug gives -3.
        static void QUInt4ConstKernel(Index1D i, ArrayView<int> outI, ArrayView<float> outF)
        {
            QUInt4 c = (QUInt4)13;
            outI[i.X] = c;
            outF[i.X] = c;
        }
        static void QUInt4StoreKernel(Index1D i, ArrayView<int> src, ArrayView<QUInt4> dst) => dst[i] = (QUInt4)src[i];
        // The dequant shape: read an unsigned 4-bit code and widen to float (QUInt4 -> Float32 convert).
        static void QUInt4ToFloatKernel(Index1D i, ArrayView<QUInt4> x, ArrayView<float> y) => y[i] = x[i];

        /// <summary>
        /// Uploads pre-packed nibble bytes into an ArrayView&lt;QUInt4&gt; and verifies the kernel reads
        /// each element back as the correct ZERO-extended int (0..15) at both even/odd nibble positions.
        /// The high codes (8..15) are the discriminating case: a sign-extend bug would return them negative.
        /// </summary>
        [TestMethod]
        public async Task PackedQUInt4_Load_ZeroExtendedNibbles() => await RunTest(async accelerator =>
        {
            int n = 256; // spans multiple groups; every nibble value 0..15 at both byte positions
            var expected = new int[n];
            for (int i = 0; i < n; i++) expected[i] = i % 16; // 0..15, unsigned
            var packed = new byte[(n + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)((expected[2 * k] & 0xF) | ((expected[2 * k + 1] & 0xF) << 4));

            using var xBuf = accelerator.Allocate1D<QUInt4>(n);
            using var yBuf = accelerator.Allocate1D<int>(n);
            var raw = ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView();
            if (raw.Length != packed.Length)
                throw new Exception($"raw packed view length {raw.Length} != expected {packed.Length}");
            raw.CopyFromCPU(packed);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QUInt4>, ArrayView<int>>(QUInt4LoadKernel);
            kernel(n, xBuf.View, yBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await yBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"QUInt4 load mismatch at [{i}]: got {got[i]} expected {expected[i]} " +
                        $"(high codes negative => sign-extend bug; should ZERO-extend).");
        });

        /// <summary>
        /// Reads packed QUInt4 codes and widens to float (the dequant path: QUInt4 -&gt; Float32). The
        /// high codes (8..15) must widen to 8.0..15.0, not negatives - exercises the unsigned widening
        /// convert on top of the zero-extending load.
        /// </summary>
        [TestMethod]
        public async Task PackedQUInt4_ToFloat_ZeroExtendedNibbles() => await RunTest(async accelerator =>
        {
            int n = 256;
            var expected = new float[n];
            for (int i = 0; i < n; i++) expected[i] = i % 16; // 0.0 .. 15.0
            var packed = new byte[(n + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)(((int)expected[2 * k] & 0xF) | (((int)expected[2 * k + 1] & 0xF) << 4));

            using var xBuf = accelerator.Allocate1D<QUInt4>(n);
            using var yBuf = accelerator.Allocate1D<float>(n);
            var raw = ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView();
            raw.CopyFromCPU(packed);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QUInt4>, ArrayView<float>>(QUInt4ToFloatKernel);
            kernel(n, xBuf.View, yBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await yBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"QUInt4 -> float mismatch at [{i}]: got {got[i]} expected {expected[i]} " +
                        $"(should ZERO-extend then widen).");
        });

        /// <summary>
        /// A QUInt4 constant (13) converted to int + float in-kernel must zero-extend (13 / 13.0), not
        /// sign-extend (-3). Covers the IR const-fold/convert path on every backend.
        /// </summary>
        [TestMethod]
        public async Task PackedQUInt4_ConstConvert_ZeroExtends() => await RunTest(async accelerator =>
        {
            const int n = 4;
            using var iBuf = accelerator.Allocate1D<int>(n);
            using var fBuf = accelerator.Allocate1D<float>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>>(QUInt4ConstKernel)(
                n, iBuf.View, fBuf.View);
            await accelerator.SynchronizeAsync();
            var gotI = await iBuf.CopyToHostAsync<int>();
            var gotF = await fBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                if (gotI[i] != 13)
                    throw new Exception($"QUInt4 const (int) wrong at [{i}]: got {gotI[i]} expected 13 (sign-extend bug gives -3).");
                if (gotF[i] != 13f)
                    throw new Exception($"QUInt4 const (float) wrong at [{i}]: got {gotF[i]} expected 13.");
            }
        });

        /// <summary>
        /// Stores unsigned int values 0..15 into a packed QUInt4 buffer (atomic nibble RMW), reads
        /// them back (zero-extend), and verifies the round-trip. Same store path as QInt4 (writes the
        /// low nibble), so CPU + WebGL stores are fail-loud (no atomics / managed ref can't write a nibble).
        /// </summary>
        [TestMethod]
        public async Task PackedQUInt4_StoreRoundTrip() => await RunTest(async accelerator =>
        {
            var type = accelerator.AcceleratorType;
            if (type == AcceleratorType.CPU)
                throw new UnsupportedTestException("Packed QUInt4 in-kernel store is fail-loud on the CPU backend (managed ref indexer can't write a nibble).");
            if (type == AcceleratorType.WebGL)
                throw new UnsupportedTestException("Packed QUInt4 store needs atomic word RMW; WebGL has no atomics.");

            int n = 256; // adjacent threads write the two nibbles of one byte concurrently
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = i % 16; // 0..15

            using var sBuf = accelerator.Allocate1D(input);
            using var dBuf = accelerator.Allocate1D<QUInt4>(n);
            using var oBuf = accelerator.Allocate1D<int>(n);
            var kStore = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<QUInt4>>(QUInt4StoreKernel);
            var kLoad = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QUInt4>, ArrayView<int>>(QUInt4LoadKernel);
            kStore(n, sBuf.View, dBuf.View);
            kLoad(n, dBuf.View, oBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await oBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != input[i])
                    throw new Exception($"QUInt4 store round-trip mismatch at [{i}]: got {got[i]} expected {input[i]}");
        });
    }
}
