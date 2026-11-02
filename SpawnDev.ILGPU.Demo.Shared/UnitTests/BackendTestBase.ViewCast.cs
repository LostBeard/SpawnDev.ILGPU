using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // ArrayView.Cast<T2> on the host (5.4.15-local.4). Reported by Tuvok 2026-11-02: the new start index was Index * ElementSize /
    // NewElementSize, integer division, so a float view starting at an odd element cast to long silently began one float EARLY and
    // every read was shifted by 4 bytes. And for a packed 4-bit view (ElementSize 1, 4 bits per element) the same formula gave N
    // nibbles a view of N bytes: twice the data, running past the buffer. Cast now works in bits and throws on a start that is not on
    // a whole target element.
    public abstract partial class BackendTestBase
    {
        static void ViewCastCopyLongKernel(Index1D i, ArrayView<long> src, ArrayView<long> dst) => dst[i] = src[i];

        static void ViewCastCopyByteToIntKernel(Index1D i, ArrayView<byte> src, ArrayView<int> dst) => dst[i] = src[i];

        /// <summary>A float view at an EVEN element casts to long at the right element (the data read through it proves it).</summary>
        [TestMethod]
        public async Task ViewCast_FloatSubViewToLong_StartsAtTheRightElement() => await RunTest(async accelerator =>
        {
            var ints = new int[8];
            for (int k = 0; k < ints.Length; k++) ints[k] = 0x10101010 * (k + 1);
            using var buf = accelerator.Allocate1D(ints);
            using var dst = accelerator.Allocate1D<long>(2);
            // ints 2..5 = longs 1..2 of the buffer
            var longs = buf.View.BaseView.SubView(2, 4).Cast<long>();
            if (longs.Length != 2) throw new Exception($"SubView(2, 4).Cast<long>() has Length {longs.Length}, expected 2");
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<long>>(ViewCastCopyLongKernel);
            k1((Index1D)2, longs, dst.View);
            var got = await dst.CopyToHostAsync<long>();
            for (int j = 0; j < 2; j++)
            {
                long want = (long)(uint)ints[2 + 2 * j] | ((long)ints[3 + 2 * j] << 32);
                if (got[j] != want)
                    throw new Exception($"long[{j}] = 0x{got[j]:X16}, expected 0x{want:X16} (ints {2 + 2 * j}, {3 + 2 * j}): the cast view starts at the wrong element");
            }
        });

        /// <summary>
        /// A long sub-view at an ODD element reads its own elements. WebGL emulates a 64-bit element as two texels and added the view's
        /// element offset to the texel index unscaled, so element k of a sub-view read from texel k (element k/2): found by the test
        /// above on WebGL (2026-11-02), where the cast view starts at long 1.
        /// </summary>
        [TestMethod]
        public async Task ViewCast_LongSubViewAtOddElement_ReadsItsOwnElements() => await RunTest(async accelerator =>
        {
            var longs = new long[] { 0x1111111100000001L, 0x2222222200000002L, 0x3333333300000003L, 0x4444444400000004L };
            using var buf = accelerator.Allocate1D(longs);
            using var dst = accelerator.Allocate1D<long>(2);
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<long>>(ViewCastCopyLongKernel);
            k1((Index1D)2, buf.View.BaseView.SubView(1, 2), dst.View);
            var got = await dst.CopyToHostAsync<long>();
            for (int j = 0; j < 2; j++)
                if (got[j] != longs[1 + j])
                    throw new Exception($"SubView(1, 2)[{j}] = 0x{got[j]:X16}, expected 0x{longs[1 + j]:X16}");
        });

        /// <summary>A float view at an ODD element has no exact long view: Cast throws instead of rounding the start down.</summary>
        [TestMethod]
        public async Task ViewCast_MisalignedStart_Throws() => await RunTest(async accelerator =>
        {
            using var buf = accelerator.Allocate1D<float>(8);
            try
            {
                var v = buf.View.BaseView.SubView(1, 4).Cast<long>();
                throw new Exception($"SubView(1, 4).Cast<long>() returned a view of Length {v.Length} instead of throwing: it starts 4 bytes before element 1");
            }
            catch (ArgumentException) { }
            await Task.CompletedTask;
        });

        /// <summary>A packed 4-bit view casts to bytes by BITS: 8 nibbles are 4 bytes, and a sub-view at nibble 2 starts at byte 1.</summary>
        [TestMethod]
        public async Task ViewCast_Packed4BitToByte_CountsBits() => await RunTest(async accelerator =>
        {
            var packed = new byte[] { 0x21, 0x43, 0x65, 0x87 };   // 8 nibbles
            using var buf = accelerator.Allocate1D<QUInt4>(8);
            ((IContiguousArrayView)buf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            var whole = buf.View.BaseView.Cast<byte>();
            if (whole.Length != 4) throw new Exception($"an 8-nibble view .Cast<byte>() has Length {whole.Length}, expected 4");
            var tail = buf.View.BaseView.SubView(2, 6).Cast<byte>();
            if (tail.Length != 3) throw new Exception($"nibbles 2..7 .Cast<byte>() has Length {tail.Length}, expected 3");
            using var dst = accelerator.Allocate1D<int>(3);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<int>>(ViewCastCopyByteToIntKernel);
            k((Index1D)3, tail, dst.View);
            var got = await dst.CopyToHostAsync<int>();
            for (int j = 0; j < 3; j++)
                if (got[j] != packed[1 + j])
                    throw new Exception($"byte[{j}] = 0x{got[j]:X2}, expected 0x{packed[1 + j]:X2}: nibble 2 is byte 1");
        });
    }
}
