using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Rendering;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // ICanvasRenderer (CanvasRendererFactory): packed RGBA8 ILGPU buffer -> HTML canvas. The display canvas is read
    // back with getImageData and compared byte for byte. Added 2026-10-08 for PR #8: on an Intel HD 620 (2017 driver,
    // Chrome 154) the WebGPU renderer's render-pipeline blit drew NOTHING while compute and the fps counter ran, and
    // nothing in the suite looked at the presented pixels.
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// Opaque pixels at sizes that are not multiples of the WebGPU blit's 8x8 workgroup, one renderer reused across
        /// size changes, then production scale (1920x1080). Every channel value of every pixel must arrive exactly.
        /// </summary>
        [TestMethod]
        public async Task CanvasRenderer_Present_ExactPixelsTest() => await RunTest(async acc =>
        {
            if (acc.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("Canvas presentation needs an HTML canvas (browser backends only)");
            using var renderer = CanvasRendererFactory.Create(acc);
            using var canvas = new HTMLCanvasElement();
            renderer.AttachCanvas(canvas);
            int seed = 0;
            foreach (var (w, h) in new[] { (67, 41), (64, 20), (130, 9), (1920, 1080) })
            {
                await CanvasPresentCase(acc, renderer, canvas, w, h, seed++, opaque: true, asUInt: false);
                await CanvasPresentCase(acc, renderer, canvas, w, h, seed++, opaque: true, asUInt: true);
            }
        });

        /// <summary>
        /// WebGPU presents OPAQUE: the alpha byte of the pixel buffer is ignored and the colour bytes arrive unchanged.
        /// Master did that with alphaMode "opaque"; PR #8 uses "premultiplied" (drawImage from an "opaque" WebGPU
        /// canvas returned transparent pixels on the HD 620) and forces alpha = 1 in the blit. Without that force,
        /// premultiplied compositing would scale or drop every translucent pixel here.
        /// </summary>
        [TestMethod]
        public async Task CanvasRenderer_WebGPU_IgnoresAlpha_ExactColorTest() => await RunTest(async acc =>
        {
            if (acc.AcceleratorType != AcceleratorType.WebGPU)
                throw new UnsupportedTestException("WebGPU renderer contract (opaque present)");
            using var renderer = CanvasRendererFactory.Create(acc);
            using var canvas = new HTMLCanvasElement();
            renderer.AttachCanvas(canvas);
            await CanvasPresentCase(acc, renderer, canvas, 67, 41, seed: 3, opaque: false, asUInt: false);
            await CanvasPresentCase(acc, renderer, canvas, 1920, 1080, seed: 4, opaque: false, asUInt: true);
        });

        static async Task CanvasPresentCase(Accelerator acc, ICanvasRenderer renderer, HTMLCanvasElement canvas,
            int width, int height, int seed, bool opaque, bool asUInt)
        {
            // Hostile values: every channel sweeps 0..255 across the image, and the channels differ per pixel so a
            // swapped R/B (bgra vs rgba), a row-stride error or a transposed index shows up as a mismatch.
            var pixels = new int[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int r = (x * 7 + y * 13 + seed) & 255;
                    int g = ((x * 31) ^ (y * 3) ^ (seed * 17)) & 255;
                    int b = (x + y * 5 + seed * 29) & 255;
                    int a = opaque ? 255 : (x * 11 + y * 19 + seed) & 255;
                    pixels[y * width + x] = r | (g << 8) | (b << 16) | (a << 24);
                }
            }
            canvas.Width = width;
            canvas.Height = height;

            if (asUInt)
            {
                using var buffer = acc.Allocate2DDenseX<uint>(new Index2D(width, height));
                var upixels = new uint[pixels.Length];
                for (int i = 0; i < pixels.Length; i++) upixels[i] = unchecked((uint)pixels[i]);
                buffer.View.BaseView.CopyFromCPU(upixels);
                await renderer.PresentAsync(buffer);
            }
            else
            {
                using var buffer = acc.Allocate2DDenseX<int>(new Index2D(width, height));
                buffer.View.BaseView.CopyFromCPU(pixels);
                await renderer.PresentAsync(buffer);
            }

            // Test oracle only: the presented pixels come back to .NET to be compared.
            using var ctx = canvas.GetContext<CanvasRenderingContext2D>("2d")
                ?? throw new Exception("display canvas has no 2d context");
            var got = ctx.GetImageBytes(0, 0, width, height)
                ?? throw new Exception("getImageData returned null");
            if (got.Length != width * height * 4)
                throw new Exception($"{width}x{height}: getImageData returned {got.Length} bytes");
            for (int i = 0; i < pixels.Length; i++)
            {
                int p = pixels[i];
                // Alpha always reads back 255: opaque input, or (WebGPU, translucent input) the alpha byte ignored.
                int er = p & 255, eg = (p >> 8) & 255, eb = (p >> 16) & 255, ea = 255;
                int gr = got[i * 4], gg = got[i * 4 + 1], gb = got[i * 4 + 2], ga = got[i * 4 + 3];
                if (gr != er || gg != eg || gb != eb || ga != ea)
                    throw new Exception($"{acc.AcceleratorType} {width}x{height} seed {seed} {(asUInt ? "uint" : "int")}: " +
                        $"pixel ({i % width},{i / width}) expected RGBA({er},{eg},{eb},{ea}) got RGBA({gr},{gg},{gb},{ga})");
            }
        }
    }
}
