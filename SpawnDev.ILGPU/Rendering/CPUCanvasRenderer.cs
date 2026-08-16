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

        public CPUCanvasRenderer(Accelerator accelerator)
        {
            _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
        }

        public void AttachCanvas(HTMLCanvasElement canvas)
        {
            DisposeRenderingResources();
            _ctx = canvas.GetContext<CanvasRenderingContext2D>("2d");
        }

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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeRenderingResources();
        }
    }
}
