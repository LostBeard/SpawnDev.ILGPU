using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Rendering;
using SpawnDev.SpawnJS.JSObjects;
// SpawnJS declares this WebGPU typedef as a global using, which is visible only inside SpawnJS.
using GPUCopyExternalImageSource = SpawnDev.SpawnJS.Union<SpawnDev.SpawnJS.JSObjects.ImageBitmap, SpawnDev.SpawnJS.JSObjects.ImageData, SpawnDev.SpawnJS.JSObjects.HTMLImageElement, SpawnDev.SpawnJS.JSObjects.HTMLVideoElement, SpawnDev.SpawnJS.JSObjects.VideoFrame, SpawnDev.SpawnJS.JSObjects.HTMLCanvasElement, SpawnDev.SpawnJS.JSObjects.OffscreenCanvas>;

namespace SpawnDev.ILGPU.Rendering
{
    /// <summary>
    /// Creates the optimal <see cref="IExternalImageCopier"/> for an accelerator - the input-side twin of
    /// <see cref="CanvasRendererFactory"/>.
    /// </summary>
    public static class ExternalImageCopier
    {
        /// <summary>
        /// WebGPU: <see cref="WebGPUExternalImageCopier"/> (GPU-only: source -> texture -> buffer).
        /// Other browser backends (WebGL, Wasm): <see cref="Canvas2DExternalImageCopier"/>, which draws the source
        /// to a 2D canvas and uploads its pixels JS-side with <see cref="IBrowserMemoryBuffer.CopyFromJS(TypedArray, long)"/>.
        /// On Wasm that IS the zero-extra-copy path (device memory is the JS heap); on WebGL it is the upload.
        /// </summary>
        public static IExternalImageCopier Create(Accelerator accelerator) => accelerator switch
        {
            WebGPUAccelerator wgpu => new WebGPUExternalImageCopier(wgpu),
            _ => new Canvas2DExternalImageCopier(accelerator),
        };

        /// <summary>Shared argument validation for every implementation.</summary>
        internal static void Validate(int width, int height, ArrayView1D<int, Stride1D.Dense> destination)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "width must be > 0");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "height must be > 0");
            long needed = (long)width * height;
            if (destination.Length < needed)
                throw new ArgumentException($"destination holds {destination.Length} pixels, {width}x{height} needs {needed}", nameof(destination));
        }
    }

    /// <summary>
    /// <see cref="IExternalImageCopier"/> for browser backends without a GPU image-copy path (WebGL, Wasm): the source
    /// is drawn to a reused <see cref="OffscreenCanvas"/> and its pixels are written to the buffer from JS.
    /// The pixels never enter the .NET heap.
    /// </summary>
    public sealed class Canvas2DExternalImageCopier : IExternalImageCopier
    {
        private readonly Accelerator _accelerator;
        private OffscreenCanvas? _canvas;
        private CanvasRenderingContext2D? _ctx;
        private bool _disposed;

        public Canvas2DExternalImageCopier(Accelerator accelerator)
        {
            _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
        }

        /// <inheritdoc/>
        public void CopyToBuffer(GPUCopyExternalImageSource source, int width, int height, ArrayView1D<int, Stride1D.Dense> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ExternalImageCopier.Validate(width, height, destination);
            var baseView = (IContiguousArrayView)destination.BaseView;
            if (baseView.Buffer is not IBrowserMemoryBuffer browserBuffer)
                throw new NotSupportedException($"{nameof(Canvas2DExternalImageCopier)} needs a browser-backed buffer (WebGL / Wasm); got {_accelerator.AcceleratorType}.");
            long targetByteOffset = baseView.Index * sizeof(int);
            using var imageData = ReadPixels(source, width, height);
            using var pixels = imageData.Data;
            browserBuffer.CopyFromJS(pixels, targetByteOffset);
        }

        private ImageData ReadPixels(GPUCopyExternalImageSource source, int width, int height)
        {
            if (_canvas == null)
            {
                _canvas = new OffscreenCanvas(width, height);
                _ctx = _canvas.Get2DContext(new CanvasRenderingContext2DSettings { WillReadFrequently = true });
            }
            else if (_canvas.Width != width || _canvas.Height != height)
            {
                _canvas.Width = width;
                _canvas.Height = height;
            }
            var ctx = _ctx!;
            switch (source.Value)
            {
                case ImageData imageData:
                    ctx.PutImageData(imageData, 0, 0);
                    break;
                case HTMLImageElement v: ctx.DrawImage(v, 0, 0); break;
                case HTMLVideoElement v: ctx.DrawImage(v, 0, 0); break;
                case HTMLCanvasElement v: ctx.DrawImage(v, 0, 0); break;
                case OffscreenCanvas v: ctx.DrawImage(v, 0, 0); break;
                case ImageBitmap v: ctx.DrawImage(v, 0, 0); break;
                case VideoFrame v: ctx.DrawImage(v, 0, 0); break;
                default:
                    throw new NotSupportedException($"Unsupported image source: {source.Value?.GetType().Name ?? "null"}");
            }
            return ctx.GetImageData(0, 0, width, height);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ctx?.Dispose(); _ctx = null;
            _canvas?.Dispose(); _canvas = null;
        }
    }
}
