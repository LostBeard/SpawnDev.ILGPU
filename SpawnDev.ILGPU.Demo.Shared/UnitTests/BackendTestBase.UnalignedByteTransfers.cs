using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-23 (SpawnDev.Codecs, found by Geordi): on WebGPU, `byteView.SubView(off, n).CopyToHostAsync()` with off % 4 != 0
    // recorded copyBufferToBuffer at that offset; Dawn rejects offsets and sizes that are not multiples of 4 ("Source offset (38)
    // ... is not a multiple of 4"), the command buffer becomes invalid and the copy is dropped, so the host got whatever the
    // staging buffer held before. The same 4-byte rule applies to writeBuffer, copyBufferToBuffer and clearBuffer, which the
    // upload, device-copy and MemSet paths used with the view's own offset and a length padded UP to 4 bytes.
    // Each test runs every byte offset 0..7 and length 1..9 inside a buffer whose other bytes hold a known pattern, and checks
    // the whole buffer afterwards: the requested range must be right AND no byte outside it may change.
    public abstract partial class BackendTestBase
    {
        private const int UnalignedBufferBytes = 64;

        private static byte[] UnalignedPattern(int seed)
        {
            var b = new byte[UnalignedBufferBytes];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)(i * 37 + seed * 11 + 5);
            return b;
        }

        private static void AssertBytes(string what, byte[] want, byte[] got)
        {
            if (got.Length != want.Length)
                throw new Exception($"{what}: length {got.Length}, expected {want.Length}.");
            for (int i = 0; i < want.Length; i++)
                if (got[i] != want[i])
                    throw new Exception($"{what}: byte {i} is 0x{got[i]:X2}, expected 0x{want[i]:X2} (got {string.Join(",", got)}).");
        }

        [TestMethod]
        public async Task UnalignedByteTransfer_ReadbackOfSubView_ReturnsTheRequestedBytes() => await RunTest(async accelerator =>
        {
            var pattern = UnalignedPattern(1);
            using var buf = accelerator.Allocate1D(pattern);
            await accelerator.SynchronizeAsync();
            for (int off = 0; off < 8; off++)
                for (int len = 1; len <= 9; len++)
                {
                    var got = await buf.View.SubView(off, len).CopyToHostAsync();
                    var want = new byte[len];
                    Array.Copy(pattern, off, want, 0, len);
                    AssertBytes($"readback SubView({off}, {len})", want, got);
                }

            // ushort elements at odd element offsets (byte offset 2 mod 4).
            var shorts = new ushort[UnalignedBufferBytes / 2];
            for (int i = 0; i < shorts.Length; i++) shorts[i] = (ushort)(i * 4099 + 17);
            using var sbuf = accelerator.Allocate1D(shorts);
            await accelerator.SynchronizeAsync();
            for (int off = 0; off < 4; off++)
                for (int len = 1; len <= 5; len++)
                {
                    var got = await sbuf.View.SubView(off, len).CopyToHostAsync();
                    for (int k = 0; k < len; k++)
                        if (got[k] != shorts[off + k])
                            throw new Exception($"readback ushort SubView({off}, {len}): element {k} is {got[k]}, expected {shorts[off + k]}.");
                }
        });

        [TestMethod]
        public async Task UnalignedByteTransfer_UploadIntoSubView_WritesOnlyTheRange() => await RunTest(async accelerator =>
        {
            var pattern = UnalignedPattern(2);
            using var buf = accelerator.Allocate1D<byte>(UnalignedBufferBytes);
            for (int off = 0; off < 8; off++)
                for (int len = 1; len <= 9; len++)
                {
                    buf.View.CopyFromCPU(pattern);
                    var data = new byte[len];
                    for (int k = 0; k < len; k++) data[k] = (byte)(0xA0 + k * 3 + off);
                    buf.View.SubView(off, len).CopyFromCPU(data);
                    await accelerator.SynchronizeAsync();
                    var got = await buf.View.CopyToHostAsync();
                    var want = (byte[])pattern.Clone();
                    Array.Copy(data, 0, want, off, len);
                    AssertBytes($"upload into SubView({off}, {len})", want, got);
                }
        });

        [TestMethod]
        public async Task UnalignedByteTransfer_DeviceCopyBetweenSubViews_WritesOnlyTheRange() => await RunTest(async accelerator =>
        {
            var srcPattern = UnalignedPattern(3);
            var dstPattern = UnalignedPattern(4);
            using var src = accelerator.Allocate1D(srcPattern);
            using var dst = accelerator.Allocate1D<byte>(UnalignedBufferBytes);
            for (int srcOff = 0; srcOff < 4; srcOff++)
                for (int dstOff = 0; dstOff < 8; dstOff++)
                    for (int len = 1; len <= 9; len++)
                    {
                        dst.View.CopyFromCPU(dstPattern);
                        dst.View.SubView(dstOff, len).CopyFrom(src.View.SubView(srcOff, len));
                        await accelerator.SynchronizeAsync();
                        var got = await dst.View.CopyToHostAsync();
                        var want = (byte[])dstPattern.Clone();
                        Array.Copy(srcPattern, srcOff, want, dstOff, len);
                        AssertBytes($"device copy src SubView({srcOff}, {len}) -> dst SubView({dstOff}, {len})", want, got);
                    }
        });

        [TestMethod]
        public async Task UnalignedByteTransfer_MemSetOfSubView_WritesOnlyTheRange() => await RunTest(async accelerator =>
        {
            var pattern = UnalignedPattern(5);
            using var buf = accelerator.Allocate1D<byte>(UnalignedBufferBytes);
            foreach (byte value in new byte[] { 0, 0x5A })
                for (int off = 0; off < 8; off++)
                    for (int len = 1; len <= 9; len++)
                    {
                        buf.View.CopyFromCPU(pattern);
                        if (value == 0) buf.View.SubView(off, len).MemSetToZero();
                        else buf.View.SubView(off, len).MemSet(value);
                        await accelerator.SynchronizeAsync();
                        var got = await buf.View.CopyToHostAsync();
                        var want = (byte[])pattern.Clone();
                        for (int k = 0; k < len; k++) want[off + k] = value;
                        AssertBytes($"MemSet(0x{value:X2}) of SubView({off}, {len})", want, got);
                    }
        });
    }
}
