using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.ILGPU.Rendering;
using SpawnDev.ILGPU.WebGL.Backend;

namespace SpawnDev.ILGPU.WebGL.Rendering
{
    /// <summary>
    /// WebGL canvas renderer. Mirrors the WebGPU pattern: the ImageBitmap is drawn
    /// synchronously inside the worker message handler callback (HandleBlitResponse),
    /// so no JS event loop turn can clear the canvas between the blit and the draw.
    /// </summary>
    public sealed class WebGLCanvasRenderer : ICanvasRenderer
    {
        private readonly WebGLAccelerator _accelerator;
        private CanvasRenderingContext2D? _displayCtx;
        private bool _disposed;

        public WebGLCanvasRenderer(WebGLAccelerator accelerator)
        {
            _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
        }

        public void AttachCanvas(HTMLCanvasElement canvas)
        {
            _displayCtx?.Dispose();
            _displayCtx = canvas.GetContext<CanvasRenderingContext2D>("2d");
        }

        public async Task PresentAsync(MemoryBuffer2D<uint, Stride2D.DenseX> buffer)
        {
            if (_displayCtx == null) return;
            int width = (int)buffer.Extent.X, height = (int)buffer.Extent.Y;
            var memBuf = _accelerator.GetWebGLMemoryBuffer(buffer);
            if (memBuf == null) return;
            var ctx = _displayCtx;
            await _accelerator.BlitAndDrawAsync(memBuf, width, height, bitmap => ctx.DrawImage(bitmap));
        }

        public async Task PresentAsync(MemoryBuffer2D<int, Stride2D.DenseX> buffer)
        {
            if (_displayCtx == null) return;
            int width = (int)buffer.Extent.X, height = (int)buffer.Extent.Y;
            var memBuf = _accelerator.GetWebGLMemoryBuffer(buffer);
            if (memBuf == null) return;
            var ctx = _displayCtx;
            await _accelerator.BlitAndDrawAsync(memBuf, width, height,
                bitmap => ctx.DrawImage(bitmap));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _displayCtx?.Dispose();
            _displayCtx = null;
        }
    }
}
