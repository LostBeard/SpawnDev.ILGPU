using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // One thread per output byte of a planar 4:2:0 picture (SpawnDev.Codecs Vp9OutputKernels.PackPlanesKernel, Geordi
    // 2026-11-08): the Y threads take an early return, the U/V threads compute a 64-bit index with / and %. On CUDA
    // (5.4.19) the U threads of the one warp that ALSO holds Y threads (4352..4383 for 66x66: 4352..4355 are Y) read
    // wrong source bytes - 28 wrong; warps whose threads all take one branch were right; int index math was right; CPU and
    // OpenCL were right. Expected values: the same formula on the host.
    public abstract partial class BackendTestBase
    {
        static void DivergentPackEarlyReturnLong(Index1D i, ArrayView<byte> src, ArrayView<byte> dst, int w, int h, int uvW, int uvH,
            int yStride, int uvStride, long yOffset, long uOffset, long vOffset)
        {
            long ySize = (long)w * h, uvSize = (long)uvW * uvH;
            long at = i;
            if (at < ySize)
            {
                dst[at] = src[yOffset + (at / w) * yStride + at % w];
                return;
            }
            at -= ySize;
            long plane = at < uvSize ? uOffset : vOffset;
            if (at >= uvSize) at -= uvSize;
            dst[i] = src[plane + (at / uvW) * uvStride + at % uvW];
        }

        static void DivergentPackIfElseLong(Index1D i, ArrayView<byte> src, ArrayView<byte> dst, int w, int h, int uvW, int uvH,
            int yStride, int uvStride, long yOffset, long uOffset, long vOffset)
        {
            long ySize = (long)w * h, uvSize = (long)uvW * uvH;
            long at = i;
            if (at < ySize)
            {
                dst[at] = src[yOffset + (at / w) * yStride + at % w];
            }
            else
            {
                at -= ySize;
                long plane = at < uvSize ? uOffset : vOffset;
                if (at >= uvSize) at -= uvSize;
                dst[i] = src[plane + (at / uvW) * uvStride + at % uvW];
            }
        }

        static void DivergentPackEarlyReturnInt(Index1D i, ArrayView<byte> src, ArrayView<byte> dst, int w, int h, int uvW, int uvH,
            int yStride, int uvStride, long yOffset, long uOffset, long vOffset)
        {
            int ySize = w * h, uvSize = uvW * uvH;
            int at = i;
            if (at < ySize)
            {
                dst[at] = src[yOffset + (at / w) * yStride + at % w];
                return;
            }
            at -= ySize;
            long plane = at < uvSize ? uOffset : vOffset;
            if (at >= uvSize) at -= uvSize;
            dst[i] = src[plane + (at / uvW) * uvStride + at % uvW];
        }

        static byte DivergentPackReference(long i, byte[] src, int w, int h, int uvW, int uvH, int yStride, int uvStride,
            long yOffset, long uOffset, long vOffset)
        {
            long ySize = (long)w * h, uvSize = (long)uvW * uvH;
            long at = i;
            if (at < ySize) return src[yOffset + (at / w) * yStride + at % w];
            at -= ySize;
            long plane = at < uvSize ? uOffset : vOffset;
            if (at >= uvSize) at -= uvSize;
            return src[plane + (at / uvW) * uvStride + at % uvW];
        }

        [TestMethod]
        public async Task DivergentWarp_Int64DivRemIndex_MatchesHost() => await RunTest(async accelerator =>
        {
            int w = 66, h = 66, uvW = 33, uvH = 33, yStride = 128, uvStride = 64;
            long yOff = 0, uOff = 128L * 128, vOff = uOff + 64L * 64;
            var src = new byte[vOff + 64L * 64];
            for (int k = 0; k < src.Length; k++) src[k] = (byte)(1 + k % 251);
            long total = (long)w * h + 2L * uvW * uvH;
            var want = new byte[total];
            for (long k = 0; k < total; k++) want[k] = DivergentPackReference(k, src, w, h, uvW, uvH, yStride, uvStride, yOff, uOff, vOff);

            using var dSrc = accelerator.Allocate1D(src);
            var fails = new List<string>();
            var variants = new (string Name, Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int, int, int, int, int, long, long, long> Kernel)[]
            {
                ("early return, long index", DivergentPackEarlyReturnLong),
                ("if / else, long index", DivergentPackIfElseLong),
                ("early return, int index", DivergentPackEarlyReturnInt),
            };
            foreach (var (name, method) in variants)
            {
                using var dDst = accelerator.Allocate1D<byte>(total);
                var kernel = accelerator.LoadAutoGroupedStreamKernel(method);
                kernel((int)total, dSrc.View, dDst.View, w, h, uvW, uvH, yStride, uvStride, yOff, uOff, vOff);
                var got = await dDst.CopyToHostAsync<byte>();
                var bad = new List<long>();
                for (long k = 0; k < total; k++) if (got[k] != want[k]) bad.Add(k);
                if (bad.Count > 0)
                    fails.Add($"{name}: {bad.Count}/{total} bytes wrong, first {bad[0]} last {bad[^1]} (got {got[bad[0]]}, want {want[bad[0]]})");
            }
            if (fails.Count > 0) throw new Exception(string.Join("\n", fails));
        });

        /// <summary>The early-return long-index kernel, for offline PTX inspection (DemoConsole ptx-dump divpack).</summary>
        public static Delegate DivergentPackEarlyReturnLongDelegate =>
            (Action<Index1D, ArrayView<byte>, ArrayView<byte>, int, int, int, int, int, int, long, long, long>)DivergentPackEarlyReturnLong;
    }
}
