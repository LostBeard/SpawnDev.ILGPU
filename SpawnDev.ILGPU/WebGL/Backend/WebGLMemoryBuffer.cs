using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.BlazorJS.Toolbox;
using System.Runtime.InteropServices;

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// ILGPU MemoryBuffer implementation backed by a JavaScript ArrayBuffer.
    /// Data is uploaded to the GL worker on first use or when CPU-modified,
    /// and stays GPU-resident until explicitly read back via CopyToHostAsync.
    /// </summary>
    public class WebGLMemoryBuffer : MemoryBuffer, IBrowserMemoryBuffer
    {
        private Uint8Array? _backingArray;
        private bool _disposed;

        /// <summary>
        /// Unique buffer ID used by the GL worker to reference this buffer's GPU-resident texture.
        /// Assigned during construction, sent to worker via 'allocBuffer' message.
        /// </summary>
        internal int WorkerBufferId { get; }

        /// <summary>
        /// True when CPU-side data has been modified and needs upload to the GL worker.
        /// Set by CopyFrom/MemSet, cleared after upload.
        /// </summary>
        internal bool NeedsUpload { get; set; }

        /// <summary>
        /// True when the GL worker has been notified to allocate this buffer.
        /// </summary>
        internal bool IsAllocatedInWorker { get; set; }

        /// <summary>
        /// The GLSL type for this buffer's texture format in the GL worker.
        /// Default is "float" (R32F). Set by the accelerator based on kernel param bindings.
        /// </summary>
        internal string GlslType { get; set; } = "float";

        public WebGLMemoryBuffer(Accelerator accelerator, long length, int elementSize, int bitsPerElement = 0)
            : base(accelerator, length, elementSize, bitsPerElement)
        {
            if (LengthInBytes > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(length),
                    $"Buffer size {LengthInBytes} bytes exceeds maximum WebGL buffer capacity (2GB)");
            _backingArray = new Uint8Array((int)LengthInBytes);
            WorkerBufferId = ((WebGLAccelerator)accelerator).AllocateWorkerBufferId();
        }

        /// <summary>
        /// Gets the backing Uint8Array that holds the CPU-side data.
        /// </summary>
        public Uint8Array? BackingArray => _backingArray;

        /// <summary>
        /// Gets the underlying ArrayBuffer backing this memory buffer.
        /// </summary>
        public ArrayBuffer? UnderlyingBuffer => _backingArray?.Buffer;

        /// <summary>
        /// Replaces the underlying ArrayBuffer with new data (used after readback from worker).
        /// </summary>
        internal void ReplaceArrayBuffer(ArrayBuffer newBuffer)
        {
            _backingArray?.Dispose();
            _backingArray = new Uint8Array(newBuffer);
        }

        public Task<Uint8Array> CopyToHostUint8ArrayAsync(long sourceByteOffset = 0, long? copyBytes = null)
        {
            // Request readback from the GL worker first
            var accel = (WebGLAccelerator)Accelerator;
            return accel.ReadbackAndGetUint8ArrayAsync(this, sourceByteOffset, copyBytes);
        }

        /// <inheritdoc/>
        public void CopyFromJS(TypedArray source, long targetByteOffset = 0)
        {
            if (_backingArray == null)
                throw new ObjectDisposedException(nameof(WebGLMemoryBuffer));
            using var srcBytes = new Uint8Array(source.Buffer, (int)source.ByteOffset, (int)source.ByteLength);
            _backingArray.Set(srcBytes, targetByteOffset);
            NeedsUpload = true;
        }

        /// <inheritdoc/>
        public void CopyFromJS(ArrayBuffer source, long targetByteOffset = 0)
        {
            if (_backingArray == null)
                throw new ObjectDisposedException(nameof(WebGLMemoryBuffer));
            using var srcBytes = new Uint8Array(source);
            _backingArray.Set(srcBytes, targetByteOffset);
            NeedsUpload = true;
        }

        protected override void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> source,
            in ArrayView<byte> destination)
        {
            if (source.GetAcceleratorType() == AcceleratorType.CPU)
            {
                var length = (int)source.Length;
                var sourceContiguous = (IContiguousArrayView)source;
                var sourceBuffer = sourceContiguous.Buffer;
                var srcPtr = sourceBuffer.NativePtr + (int)sourceContiguous.Index;

                var byteArray = new byte[length];
                Marshal.Copy(srcPtr, byteArray, 0, length);

                var destContiguous = (IContiguousArrayView)destination;
                _backingArray!.Write(byteArray, (int)destContiguous.Index);

                // Mark CPU-dirty — needs upload to worker before next dispatch
                NeedsUpload = true;
            }
            else if (source.GetAcceleratorType() == AcceleratorType.WebGL)
            {
                // GPU→GPU copy. Route through the worker — the worker's entry.data is the
                // canonical post-kernel state. Reading from the CPU-side _backingArray
                // gives stale zeros after a kernel Transform Feedback write (the TF readback
                // updates worker entry.data only). See WebGLAccelerator.WorkerCopyBuffer.
                var sourceContiguous = (IContiguousArrayView)source;
                var sourceMemBuf = (WebGLMemoryBuffer)sourceContiguous.Buffer;
                var destContiguous = (IContiguousArrayView)destination;
                var length = (int)source.Length;
                var accel = (WebGLAccelerator)Accelerator;
                accel.WorkerCopyBuffer(
                    sourceMemBuf, (int)sourceContiguous.Index,
                    this, (int)destContiguous.Index,
                    length);
                // Worker now has correct data in our entry.data; CPU _backingArray is stale
                // (refreshed by next CopyToHostAsync). No upload pending — the worker is
                // already in sync.
                NeedsUpload = false;
            }
            else
            {
                throw new NotSupportedException($"Copy from {source.GetAcceleratorType()} to WebGL not supported.");
            }
        }

        /// <summary>
        /// Real async GPU-&gt;CPU readback through the GL worker (Transform Feedback
        /// output is only host-visible via worker readback). Overrides
        /// <see cref="MemoryBuffer.CopyToRawAsync"/>; the synchronous <see cref="CopyTo"/>
        /// below reads the CPU-side backing array, which is stale when a kernel TF write
        /// was the most recent producer. <c>ReadbackAndGetUint8ArrayAsync</c> drains
        /// pending dispatches first.
        /// </summary>
        protected override async Task<byte[]> CopyToRawAsync(
            AcceleratorStream stream,
            long sourceOffsetInBytes,
            long lengthInBytes)
        {
            if (lengthInBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(lengthInBytes));
            if (lengthInBytes == 0)
                return global::System.Array.Empty<byte>();
            var accel = (WebGLAccelerator)Accelerator;
            using var u8 = await accel.ReadbackAndGetUint8ArrayAsync(
                this, sourceOffsetInBytes, lengthInBytes);
            return u8.ReadBytes();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// WebGL <see cref="CopyFromJS(TypedArray, long)"/> writes into the CPU-side backing array
        /// (uploaded to the GL texture on next dispatch) - no 4-byte-alignment rule - so an
        /// <see cref="IJSReadStream"/> source uploads without entering the .NET managed heap. A plain
        /// .NET <see cref="System.IO.Stream"/> falls back to the managed base implementation.
        /// </remarks>
        protected override System.Threading.Tasks.Task CopyFromStreamRawAsync(
            AcceleratorStream stream,
            System.IO.Stream source,
            long targetOffsetInBytes,
            long lengthInBytes,
            int chunkSizeInBytes,
            System.Threading.CancellationToken cancellationToken)
        {
            if (source is IJSReadStream js)
                return BrowserStreamUpload.CopyFromJSReadStreamAsync(
                    this, js, targetOffsetInBytes, lengthInBytes, LengthInBytes,
                    chunkSizeInBytes, cancellationToken);
            return base.CopyFromStreamRawAsync(
                stream, source, targetOffsetInBytes, lengthInBytes,
                chunkSizeInBytes, cancellationToken);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Zero-copy save to an <see cref="IJSWriteStream"/> via chunked
        /// <see cref="CopyToHostUint8ArrayAsync"/> -&gt; <c>WriteUint8ArrayAsync</c> so the bytes never enter
        /// the .NET/WASM managed heap. A plain .NET <see cref="System.IO.Stream"/> target falls back to the
        /// managed base implementation (async readback -&gt; WriteAsync).
        /// </remarks>
        protected override System.Threading.Tasks.Task CopyToStreamRawAsync(
            AcceleratorStream stream,
            System.IO.Stream target,
            long sourceOffsetInBytes,
            long lengthInBytes,
            int chunkSizeInBytes,
            System.Threading.CancellationToken cancellationToken)
        {
            if (target is IJSWriteStream js)
                return BrowserStreamDownload.CopyToJSWriteStreamAsync(
                    this, js, sourceOffsetInBytes, lengthInBytes, LengthInBytes,
                    chunkSizeInBytes, cancellationToken);
            return base.CopyToStreamRawAsync(
                stream, target, sourceOffsetInBytes, lengthInBytes,
                chunkSizeInBytes, cancellationToken);
        }

        protected override void CopyTo(
            AcceleratorStream stream,
            in ArrayView<byte> source,
            in ArrayView<byte> destination)
        {
            if (destination.GetAcceleratorType() == AcceleratorType.CPU)
            {
                var sourceContiguous = (IContiguousArrayView)source;
                var destContiguous = (IContiguousArrayView)destination;
                var destBuffer = destContiguous.Buffer;
                var destPtr = destBuffer.NativePtr + (int)destContiguous.Index;
                var length = (int)source.Length;

                var byteArray = _backingArray!.Read<byte>((int)sourceContiguous.Index, length);
                Marshal.Copy(byteArray, 0, destPtr, length);
            }
            else if (destination.GetAcceleratorType() == AcceleratorType.WebGL)
            {
                // GPU→GPU copy. Route through the worker — see the matching branch in
                // CopyFrom above and WebGLAccelerator.WorkerCopyBuffer for rationale.
                // Reading from this buffer's CPU-side _backingArray gives stale zeros
                // if a kernel TF write was the most recent producer of our bytes.
                var sourceContiguous = (IContiguousArrayView)source;
                var destContiguous = (IContiguousArrayView)destination;
                var destMemBuf = (WebGLMemoryBuffer)destContiguous.Buffer;
                var length = (int)source.Length;
                var accel = (WebGLAccelerator)Accelerator;
                accel.WorkerCopyBuffer(
                    this, (int)sourceContiguous.Index,
                    destMemBuf, (int)destContiguous.Index,
                    length);
                // dest's worker entry.data is now correct; dest CPU _backingArray is stale.
                destMemBuf.NeedsUpload = false;
            }
            else
            {
                throw new NotSupportedException($"Copy from WebGL to {destination.GetAcceleratorType()} not supported.");
            }
        }

        protected override void MemSet(
            AcceleratorStream stream,
            byte value,
            in ArrayView<byte> view)
        {
            var viewContiguous = (IContiguousArrayView)view;
            var data = new byte[view.Length];
            if (value != 0) global::System.Array.Fill(data, value);
            _backingArray!.Write(data, (int)viewContiguous.Index);

            // Mark CPU-dirty
            NeedsUpload = true;
        }

        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
            {
                // Tell worker to free the GPU-resident buffer
                try
                {
                    var accel = Accelerator as WebGLAccelerator;
                    accel?.FreeWorkerBuffer(WorkerBufferId);
                }
                catch { }

                _backingArray?.Dispose();
                _backingArray = null;
            }
        }
    }
}
