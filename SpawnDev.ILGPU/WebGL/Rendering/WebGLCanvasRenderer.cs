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

        /// <summary>Creates a renderer that presents buffers of <paramref name="accelerator"/>.</summary>
        /// <param name="accelerator">The WebGL accelerator that owns the buffers.</param>
        /// <exception cref="ArgumentNullException"><paramref name="accelerator"/> is null.</exception>
        public WebGLCanvasRenderer(WebGLAccelerator accelerator)
        {
            _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
        }

        /// <inheritdoc/>
        /// <remarks>Uses the canvas's 2D context; disposes any previously attached context.</remarks>
        public void AttachCanvas(HTMLCanvasElement canvas)
        {
            _displayCtx?.Dispose();
            _displayCtx = canvas.GetContext<CanvasRenderingContext2D>("2d");
        }

        /// <inheritdoc/>
        /// <remarks>Does nothing if no canvas is attached or the buffer is not a WebGL buffer.</remarks>
        public async Task PresentAsync(MemoryBuffer2D<uint, Stride2D.DenseX> buffer)
        {
            if (_displayCtx == null) return;
            int width = (int)buffer.Extent.X, height = (int)buffer.Extent.Y;
            var memBuf = _accelerator.GetWebGLMemoryBuffer(buffer);
            if (memBuf == null) return;
            var ctx = _displayCtx;
            await _accelerator.BlitAndDrawAsync(memBuf, width, height, bitmap => ctx.DrawImage(bitmap));
        }

        /// <inheritdoc/>
        /// <remarks>Does nothing if no canvas is attached or the buffer is not a WebGL buffer.</remarks>
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

        /// <summary>Releases the attached canvas context. Safe to call more than once.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _displayCtx?.Dispose();
            _displayCtx = null;
        }
    }
}
