using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Rendering;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // ICanvasRenderer sizes the attached canvas to the frame. No renderer set the display canvas's drawing-buffer size, so a frame
    // presented to a canvas the caller had not sized landed on the default 300x150: the top-left 300x150 of a 960x889 frame, stretched
    // by CSS (Riker, AssistZ, 2026-11-02).
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// A 96x37 frame, then a 40x200 frame, presented to an unsized canvas: the canvas takes each frame's size and its bottom-right
        /// pixel is the frame's colour.
        /// </summary>
        [TestMethod]
        public async Task CanvasRenderer_SizesTheCanvasToTheFrame() => await RunTest(async acc =>
        {
            if (acc.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("presenting to an HTML canvas needs a browser backend");
            using var canvas = new HTMLCanvasElement();
            using var renderer = CanvasRendererFactory.Create(acc);
            renderer.AttachCanvas(canvas);
            foreach (var (w, h, rgba) in new[] { (96, 37, 0xFF2040C0u), (40, 200, 0xFF10E060u) })
            {
                using var frame = acc.Allocate2DDenseX<uint>(new LongIndex2D(w, h));
                var pixels = new uint[w * h];
                System.Array.Fill(pixels, rgba);
                frame.View.BaseView.CopyFromCPU(pixels);
                await acc.SynchronizeAsync();
                await renderer.PresentAsync(frame);
                if (canvas.Width != w || canvas.Height != h)
                    throw new Exception($"after presenting a {w}x{h} frame the canvas is {canvas.Width}x{canvas.Height} ({BackendName})");
                using var ctx = canvas.GetContext<CanvasRenderingContext2D>("2d");
                using var corner = ctx.GetImageData(w - 1, h - 1, 1, 1);
                using var data = corner.Data;
                var px = data.ToArray();
                uint got = (uint)(px[0] | (px[1] << 8) | (px[2] << 16) | (px[3] << 24));
                if (got != rgba)
                    throw new Exception($"{w}x{h} frame: bottom-right pixel 0x{got:X8}, expected 0x{rgba:X8} ({BackendName})");
            }
        });
    }
}
