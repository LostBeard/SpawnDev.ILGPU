using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.ILGPU.Rendering;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.SpawnJS.JSObjects;
// SpawnJS declares this WebGPU typedef as a global using, which is visible only inside SpawnJS.
using GPUCopyExternalImageSource = SpawnDev.SpawnJS.Union<SpawnDev.SpawnJS.JSObjects.ImageBitmap, SpawnDev.SpawnJS.JSObjects.ImageData, SpawnDev.SpawnJS.JSObjects.HTMLImageElement, SpawnDev.SpawnJS.JSObjects.HTMLVideoElement, SpawnDev.SpawnJS.JSObjects.VideoFrame, SpawnDev.SpawnJS.JSObjects.HTMLCanvasElement, SpawnDev.SpawnJS.JSObjects.OffscreenCanvas>;

namespace SpawnDev.ILGPU.WebGPU.Rendering
{
    /// <summary>
    /// GPU-only <see cref="IExternalImageCopier"/>: <c>queue.copyExternalImageToTexture</c> puts the source's pixels in
    /// an <c>rgba8unorm</c> texture, <c>copyTextureToBuffer</c> moves them into the ILGPU buffer. A video frame reaches
    /// a kernel without a canvas <c>getImageData</c> readback, a JS pixel array, or the .NET heap.
    /// </summary>
    /// <remarks>
    /// <c>copyTextureToBuffer</c> requires <c>bytesPerRow</c> to be a multiple of 256. When <c>width * 4</c> already is,
    /// the texture is copied straight into the destination; otherwise it lands in a padded staging buffer and one
    /// ILGPU kernel compacts the rows into the destination (GPU -> GPU).
    /// <para>
    /// ORDERING: <c>copyExternalImageToTexture</c> runs on the queue timeline the moment it is called, while ILGPU
    /// kernels wait in an unsubmitted command encoder. <see cref="CopyToBuffer"/> therefore FLUSHES pending ILGPU work
    /// first, so a kernel launched before the copy (e.g. one still reading last frame's pixels from the destination)
    /// executes before the destination is overwritten. Kernels launched after the call read the new pixels.
    /// </para>
    /// </remarks>
    public sealed class WebGPUExternalImageCopier : IExternalImageCopier
    {
        private const int BytesPerRowAlignment = 256;
        private static readonly GPUCommandBuffer[] _submitArray = new GPUCommandBuffer[1];

        private readonly WebGPUAccelerator _accelerator;
        private GPUTexture? _texture;
        private int _textureWidth;
        private int _textureHeight;
        private MemoryBuffer1D<int, Stride1D.Dense>? _staging;
        private Action<Index2D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int, int>? _compactRows;
        private bool _disposed;

        public WebGPUExternalImageCopier(WebGPUAccelerator accelerator)
        {
            _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
        }

        private GPUDevice Device => _accelerator.NativeAccelerator.NativeDevice
            ?? throw new InvalidOperationException("WebGPU device is not available (lost or disposed).");
        private GPUQueue Queue => _accelerator.NativeAccelerator.Queue
            ?? throw new InvalidOperationException("WebGPU queue is not available (lost or disposed).");

        /// <inheritdoc/>
        public void CopyToBuffer(GPUCopyExternalImageSource source, int width, int height, ArrayView1D<int, Stride1D.Dense> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ExternalImageCopier.Validate(width, height, destination);
            // A queue-timeline copy is not a dispatch and cannot be replayed by a recorded dispatch plan: a frame copied
            // inside a capture window would be baked in once and never refreshed. Fail loud instead of freezing frames.
            if (WebGPUDispatchPlan.Recording != null)
                throw new InvalidOperationException($"{nameof(WebGPUExternalImageCopier)}.{nameof(CopyToBuffer)} cannot run while a WebGPU dispatch plan is recording. Copy the frame before the captured work.");

            var baseView = (IContiguousArrayView)destination.BaseView;
            var destMemory = baseView.Buffer as WebGPUMemoryBuffer
                ?? throw new InvalidOperationException("destination is not backed by a WebGPUMemoryBuffer of this accelerator.");
            var destGpuBuffer = destMemory.NativeBuffer.NativeBuffer
                ?? throw new ObjectDisposedException("destination", "The destination GPUBuffer has been disposed.");

            // Submit every pending ILGPU dispatch BEFORE the queue-timeline copy (see class remarks). This also makes
            // it safe to replace the staging buffer below: nothing unsubmitted can still reference the old one.
            _accelerator.FlushPendingCommands();

            EnsureTexture(width, height);
            int rowBytes = width * sizeof(int);
            int paddedRowBytes = (rowBytes + BytesPerRowAlignment - 1) / BytesPerRowAlignment * BytesPerRowAlignment;
            bool direct = paddedRowBytes == rowBytes;

            var extent = new GPUExtent3DDict { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 };
            Queue.CopyExternalImageToTexture(
                new GPUCopyExternalImageSourceInfo { Source = source },
                new GPUCopyExternalImageDestInfo { Texture = _texture },
                extent);

            GPUBuffer copyTarget;
            ulong copyOffset;
            if (direct)
            {
                copyTarget = destGpuBuffer;
                copyOffset = (ulong)(baseView.Index * sizeof(int));
            }
            else
            {
                int stagingInts = paddedRowBytes / sizeof(int) * height;
                if (_staging == null || _staging.Length < stagingInts)
                {
                    _staging?.Dispose();
                    _staging = _accelerator.Allocate1D<int>(stagingInts);
                }
                copyTarget = ((WebGPUMemoryBuffer)((IArrayView)_staging).Buffer).NativeBuffer.NativeBuffer!;
                copyOffset = 0;
            }

            using (var encoder = Device.CreateCommandEncoder())
            {
                encoder.CopyTextureToBuffer(
                    new GPUTexelCopyTextureInfo { Texture = _texture },
                    new GPUTexelCopyBufferInfo { Buffer = copyTarget, Offset = copyOffset, BytesPerRow = (uint)paddedRowBytes, RowsPerImage = (uint)height },
                    extent);
                using var commandBuffer = encoder.Finish();
                _submitArray[0] = commandBuffer;
                Queue.Submit(_submitArray);
                _submitArray[0] = null!;
            }

            if (!direct)
            {
                // Queued after the submit above, so it reads the copied rows.
                _compactRows ??= _accelerator.LoadAutoGroupedStreamKernel<Index2D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int, int>(CompactRowsKernel);
                _compactRows(new Index2D(width, height), _staging!.View, destination, paddedRowBytes / sizeof(int), width);
            }
        }

        /// <summary>dst[y * width + x] = src[y * srcStride + x] - drops the 256-byte row padding.</summary>
        private static void CompactRowsKernel(Index2D index, ArrayView1D<int, Stride1D.Dense> src, ArrayView1D<int, Stride1D.Dense> dst, int srcStride, int width)
        {
            dst[index.Y * width + index.X] = src[index.Y * srcStride + index.X];
        }

        private void EnsureTexture(int width, int height)
        {
            if (_texture != null && _textureWidth == width && _textureHeight == height) return;
            // Destroying after submit is valid WebGPU: work already submitted against the old texture completes.
            _texture?.Destroy();
            _texture?.Dispose();
            _texture = Device.CreateTexture(new GPUTextureDescriptor
            {
                Label = $"ExternalImageCopier:{width}x{height}",
                Size = new[] { width, height },
                Format = "rgba8unorm",
                // copyExternalImageToTexture requires COPY_DST + RENDER_ATTACHMENT on the destination texture.
                Usage = GPUTextureUsage.CopyDst | GPUTextureUsage.CopySrc | GPUTextureUsage.RenderAttachment,
            });
            _textureWidth = width;
            _textureHeight = height;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            // The compaction kernel may still be queued against the staging buffer: submit it before freeing.
            if (!_accelerator.IsDisposed) _accelerator.FlushPendingCommands();
            _staging?.Dispose(); _staging = null;
            _texture?.Destroy(); _texture?.Dispose(); _texture = null;
        }
    }
}
