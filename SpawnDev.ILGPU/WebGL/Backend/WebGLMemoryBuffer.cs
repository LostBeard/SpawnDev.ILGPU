using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.BlazorJS.JSObjects;
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

        public WebGLMemoryBuffer(Accelerator accelerator, long length, int elementSize)
            : base(accelerator, length, elementSize)
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
