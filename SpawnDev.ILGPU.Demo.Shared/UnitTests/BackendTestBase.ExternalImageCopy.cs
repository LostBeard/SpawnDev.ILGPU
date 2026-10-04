using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Rendering;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // IExternalImageCopier: browser image source (video / img / canvas / ImageBitmap / VideoFrame) -> packed RGBA8
    // ILGPU buffer. WebGPU copies source -> texture -> buffer on the GPU; WebGL / Wasm go through a 2D canvas.
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// Width 64 = 256-byte rows: the texture is copied STRAIGHT into the destination.
        /// </summary>
        [TestMethod]
        public async Task ExternalImageCopy_AlignedRows_ExactPixelsTest() => await RunTest(acc => ExternalImageCopyCase(acc, 64, 37));

        /// <summary>
        /// Width 67 = 268-byte rows, not a multiple of 256: on WebGPU the copy lands in a padded staging buffer and
        /// a kernel compacts the rows. A copier that skipped the compaction would shift every row after the first.
        /// </summary>
        [TestMethod]
        public async Task ExternalImageCopy_PaddedRows_ExactPixelsTest() => await RunTest(acc => ExternalImageCopyCase(acc, 67, 41));

        /// <summary>
        /// The same copier instance reused across a size change (a video changing resolution): the texture and
        /// staging buffer are rebuilt and the second frame is exact.
        /// </summary>
        [TestMethod]
        public async Task ExternalImageCopy_ReuseAcrossSizes_ExactPixelsTest() => await RunTest(async acc =>
        {
            RequireBrowserImageBackend(acc);
            using var copier = ExternalImageCopier.Create(acc);
            await ExternalImageCopyCase(acc, 67, 41, copier, seed: 1);
            await ExternalImageCopyCase(acc, 64, 20, copier, seed: 2);
            await ExternalImageCopyCase(acc, 130, 9, copier, seed: 3);
        });

        /// <summary>
        /// A VideoFrame source (what a video frame callback hands out), copied DIRECTLY and through the canvas fallback
        /// that a browser refusing video / VideoFrame sources takes on its own (Firefox: "could not be converted to any
        /// of: ImageBitmap, HTMLImageElement, HTMLCanvasElement, OffscreenCanvas"). Both must be exact.
        /// </summary>
        [TestMethod]
        public async Task ExternalImageCopy_VideoFrameSource_DirectAndCanvasFallback_ExactPixelsTest() => await RunTest(async acc =>
        {
            RequireBrowserImageBackend(acc);
            using (var copier = ExternalImageCopier.Create(acc))
                await ExternalImageCopyCase(acc, 67, 41, copier, seed: 4, asVideoFrame: true);
            var before = SpawnDev.ILGPU.WebGPU.Rendering.WebGPUExternalImageCopier.ForceFrameSourcesThroughCanvas;
            SpawnDev.ILGPU.WebGPU.Rendering.WebGPUExternalImageCopier.ForceFrameSourcesThroughCanvas = true;
            try
            {
                using var copier = ExternalImageCopier.Create(acc);
                await ExternalImageCopyCase(acc, 67, 41, copier, seed: 5, asVideoFrame: true);
                await ExternalImageCopyCase(acc, 64, 20, copier, seed: 6, asVideoFrame: true);   // the fallback canvas resizes
            }
            finally
            {
                SpawnDev.ILGPU.WebGPU.Rendering.WebGPUExternalImageCopier.ForceFrameSourcesThroughCanvas = before;
            }
        });

        static void RequireBrowserImageBackend(Accelerator acc)
        {
            if (acc.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("External image copy needs a browser image source (browser backends only)");
        }

        static Task ExternalImageCopyCase(Accelerator acc, int width, int height) => ExternalImageCopyCaseOwned(acc, width, height);

        static async Task ExternalImageCopyCaseOwned(Accelerator acc, int width, int height)
        {
            RequireBrowserImageBackend(acc);
            using var copier = ExternalImageCopier.Create(acc);
            await ExternalImageCopyCase(acc, width, height, copier, seed: 0);
        }

        static async Task ExternalImageCopyCase(Accelerator acc, int width, int height, IExternalImageCopier copier, int seed, bool asVideoFrame = false)
        {
            // Opaque pixels (alpha 255): premultiplication cannot change the colour bytes, so the copy must be EXACT.
            var expected = new int[width * height];
            var rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    byte r = (byte)(x * 3 + seed), g = (byte)(y * 5 + seed * 7), b = (byte)((x ^ y) + seed * 13);
                    rgba[i * 4 + 0] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 255;
                    expected[i] = r | (g << 8) | (b << 16) | unchecked((int)0xFF000000);
                }
            }
            using var canvas = new OffscreenCanvas(width, height);
            using (var ctx = canvas.Get2DContext())
            using (var imageData = ImageData.FromBytes(rgba, width, height))
            {
                ctx.PutImageData(imageData, 0, 0);
            }

            // Destination is a SUB-VIEW at a non-zero offset, fenced by sentinels: the copy must honour the view's
            // byte offset and write nothing outside it.
            const int lead = 5, tail = 3;
            const int sentinel = 0x5A5A5A5A;
            int total = lead + width * height + tail;
            using var buffer = acc.Allocate1D<int>(total);
            var init = new int[total];
            System.Array.Fill(init, sentinel);
            buffer.CopyFromCPU(init);

            if (asVideoFrame)
            {
                using var frame = new VideoFrame(canvas, new VideoFrameOptions { Timestamp = 0 });
                try { copier.CopyToBuffer(frame, width, height, buffer.View.SubView(lead, width * height)); }
                finally { frame.Close(); }
            }
            else copier.CopyToBuffer(canvas, width, height, buffer.View.SubView(lead, width * height));

            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < lead; i++)
                if (result[i] != sentinel) throw new Exception($"{width}x{height}: wrote before the view at [{i}] = 0x{result[i]:X8}");
            for (int i = 0; i < tail; i++)
                if (result[lead + width * height + i] != sentinel) throw new Exception($"{width}x{height}: wrote past the view at +{i}");
            for (int i = 0; i < width * height; i++)
            {
                int got = result[lead + i];
                if (got != expected[i])
                    throw new Exception($"{width}x{height} seed {seed}: pixel ({i % width},{i / width}) expected 0x{expected[i]:X8} got 0x{got:X8}");
            }
        }
    }
}
