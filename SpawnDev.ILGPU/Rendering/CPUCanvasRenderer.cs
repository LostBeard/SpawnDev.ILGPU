using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.Rendering
{
    /// <summary>
    /// CPU/Wasm canvas renderer. Uses CopyToHostUint8ArrayAsync for browser-backed buffers (WebGPU,
    /// WebGL, Wasm) and falls back to synchronous CopyToCPU for pure CPU accelerators.
    /// Reuses a pre-allocated ImageData object to minimise GC pressure.
    /// </summary>
    public sealed class CPUCanvasRenderer : ICanvasRenderer
    {
        private readonly Accelerator _accelerator;

        private CanvasRenderingContext2D? _ctx;
        private ImageData? _imageData;
        private Uint8ClampedArray? _destPixels;
        private int _cachedWidth;
        private int _cachedHeight;
        private bool _disposed;

        /// <summary>Creates a renderer that reads pixel buffers from <paramref name="accelerator"/>.</summary>
        /// <param name="accelerator">The accelerator that owns the buffers passed to <c>PresentAsync</c>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="accelerator"/> is null.</exception>
        public CPUCanvasRenderer(Accelerator accelerator)
        {
            _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
        }

        /// <summary>
        /// Attaches the renderer to <paramref name="canvas"/> through its "2d" context, releasing the
        /// previous context and cached <c>ImageData</c>.
        /// </summary>
        /// <param name="canvas">The canvas to draw to.</param>
        public void AttachCanvas(HTMLCanvasElement canvas)
        {
            DisposeRenderingResources();
            _ctx = canvas.GetContext<CanvasRenderingContext2D>("2d");
        }

        /// <summary>
        /// Copies a packed RGBA8 buffer (one <c>uint</c> per pixel, Extent.X = width, Extent.Y = height)
        /// into the canvas with <c>putImageData</c>. Browser-backed buffers are read with
        /// <see cref="IBrowserMemoryBuffer.CopyToHostUint8ArrayAsync(long, long?)"/>; other buffers are read
        /// with a synchronous <c>Synchronize</c> + <c>CopyToCPU</c>. Does nothing if no canvas is attached.
        /// </summary>
        /// <param name="buffer">The pixel buffer to present.</param>
        public async Task PresentAsync(MemoryBuffer2D<uint, Stride2D.DenseX> buffer)
        {
            if (_ctx == null) return;
            int width = (int)buffer.Extent.X, height = (int)buffer.Extent.Y;
            EnsureImageData(width, height);

            var internalBuffer = ((IArrayView)buffer).Buffer;
            if (internalBuffer is IBrowserMemoryBuffer browserBuf)
            {
                using var src = await browserBuf.CopyToHostUint8ArrayAsync(0, (long)width * height * 4);
                _destPixels!.Set(src);
            }
            else
            {
                _accelerator.Synchronize();
                var tmp = new uint[width * height];
                buffer.View.BaseView.CopyToCPU(tmp);
                _destPixels!.Write(tmp);
            }

            _ctx.PutImageData(_imageData!, 0, 0);
        }

        /// <summary>
        /// Copies a packed RGBA8 buffer (one <c>int</c> per pixel, Extent.X = width, Extent.Y = height)
        /// into the canvas with <c>putImageData</c>. Browser-backed buffers are read with
        /// <see cref="IBrowserMemoryBuffer.CopyToHostUint8ArrayAsync(long, long?)"/>; other buffers are read
        /// with a synchronous <c>Synchronize</c> + <c>CopyToCPU</c>. Does nothing if no canvas is attached.
        /// </summary>
        /// <param name="buffer">The pixel buffer to present.</param>
        public async Task PresentAsync(MemoryBuffer2D<int, Stride2D.DenseX> buffer)
        {
            if (_ctx == null) return;
            int width = (int)buffer.Extent.X, height = (int)buffer.Extent.Y;
            EnsureImageData(width, height);

            var internalBuffer = ((IArrayView)buffer).Buffer;
            if (internalBuffer is IBrowserMemoryBuffer browserBuf)
            {
                using var src = await browserBuf.CopyToHostUint8ArrayAsync(0, (long)width * height * 4);
                _destPixels!.Set(src);
            }
            else
            {
                _accelerator.Synchronize();
                var tmp = new int[width * height];
                buffer.View.BaseView.CopyToCPU(tmp);
                _destPixels!.Write(tmp);
            }

            _ctx.PutImageData(_imageData!, 0, 0);
        }

        private void EnsureImageData(int width, int height)
        {
            if (_imageData != null && _cachedWidth == width && _cachedHeight == height) return;
            _destPixels?.Dispose();
            _imageData?.Dispose();
            _imageData = _ctx!.CreateImageData(width, height);
            _destPixels = _imageData.Data;
            _cachedWidth = width;
            _cachedHeight = height;
        }

        private void DisposeRenderingResources()
        {
            _destPixels?.Dispose(); _destPixels = null;
            _imageData?.Dispose(); _imageData = null;
            _ctx?.Dispose(); _ctx = null;
            _cachedWidth = 0; _cachedHeight = 0;
        }

        /// <summary>Releases the canvas context and cached <c>ImageData</c>. Does not dispose the accelerator.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeRenderingResources();
        }
    }
}
