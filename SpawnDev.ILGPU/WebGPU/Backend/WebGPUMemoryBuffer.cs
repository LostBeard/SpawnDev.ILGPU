using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.BlazorJS.Toolbox;
using System.Runtime.InteropServices;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    public class WebGPUMemoryBuffer : MemoryBuffer, IBrowserMemoryBuffer
    {
        private static readonly GPUCommandBuffer[] _submitArray = new GPUCommandBuffer[1];
        private readonly WebGPUBuffer<byte>? _buffer;

        public WebGPUMemoryBuffer(WebGPUAccelerator accelerator, long length, int elementSize)
            : base(accelerator, length, elementSize)
        {
            _buffer = accelerator.NativeAccelerator.Allocate<byte>(LengthInBytes);
        }

        /// <summary>
        /// Protected constructor for subclasses that provide their own buffer (e.g. ExternalWebGPUMemoryBuffer).
        /// Does NOT allocate a new GPU buffer — the subclass is responsible for providing NativeBuffer.
        /// </summary>
        protected WebGPUMemoryBuffer(WebGPUAccelerator accelerator, long length, int elementSize, bool skipAllocation)
            : base(accelerator, length, elementSize)
        {
            // _buffer intentionally left null — subclass overrides NativeBuffer
        }

        public Task<Uint8Array> CopyToHostUint8ArrayAsync(long sourceByteOffset = 0, long? copyBytes = null) => NativeBuffer.CopyToHostUint8ArrayAsync(sourceByteOffset, copyBytes);

        /// <inheritdoc/>
        public void CopyFromJS(TypedArray source, long targetByteOffset = 0) => NativeBuffer.CopyFromJS(source, targetByteOffset);

        /// <inheritdoc/>
        public void CopyFromJS(ArrayBuffer source, long targetByteOffset = 0) => NativeBuffer.CopyFromJS(source, targetByteOffset);

        /// <summary>
        /// Returns the underlying WebGPU byte buffer. Virtual so subclasses can provide an external buffer.
        /// </summary>
        public virtual WebGPUBuffer<byte> NativeBuffer => _buffer!;

        // Implementation of abstract members
        protected override void CopyFrom(AcceleratorStream stream, in ArrayView<byte> source, in ArrayView<byte> destination)
        {
            if (source.GetAcceleratorType() == AcceleratorType.CPU)
            {
                var length = (int)source.Length;

                // Use IContiguousArrayView to access internal members
                var sourceContiguous = (IContiguousArrayView)source;
                var sourceBuffer = sourceContiguous.Buffer;
                var srcPtr = sourceBuffer.NativePtr + (int)sourceContiguous.Index;

                // Read from CPU buffer to managed byte array (still required for NativePtr access)
                var byteArray = new byte[length];
                Marshal.Copy(srcPtr, byteArray, 0, length);

                // Flush pending dispatches before writing to the buffer
                var accelerator = (WebGPUAccelerator)Accelerator;
                accelerator.FlushPendingCommands();

                var destContiguous = (IContiguousArrayView)destination;
                // WebGPU writeBuffer requires the number of bytes to write to be a multiple of 4
                var paddedLength = (int)WebGPUAlignment.AlignTo4(length);
                if (paddedLength > length)
                    System.Array.Resize(ref byteArray, paddedLength);
                using var typedArray = new Uint8Array(byteArray);
                accelerator.NativeAccelerator.Queue!.WriteBuffer(_buffer.NativeBuffer!, (long)destContiguous.Index, typedArray);
            }
            else
            {
                // GPU-to-GPU copy using CopyBufferToBuffer
                var accelerator = (WebGPUAccelerator)Accelerator;
                accelerator.FlushPendingCommands();

                var srcContiguous = (IContiguousArrayView)source;
                var srcMemBuffer = srcContiguous.Buffer as WebGPUMemoryBuffer
                    ?? throw new InvalidOperationException("Source buffer is not a WebGPU memory buffer");
                var srcGpuBuffer = srcMemBuffer.NativeBuffer.NativeBuffer
                    ?? throw new InvalidOperationException("Source GPU buffer is null");

                var destContiguous = (IContiguousArrayView)destination;

                var device = accelerator.NativeAccelerator.NativeDevice
                    ?? throw new InvalidOperationException("GPU device not initialized");

                var copyBytes = source.Length;
                var paddedBytes = WebGPUAlignment.AlignTo4(copyBytes);
                using var encoder = device.CreateCommandEncoder();
                encoder.CopyBufferToBuffer(
                    srcGpuBuffer, (ulong)srcContiguous.Index,
                    _buffer!.NativeBuffer!, (ulong)destContiguous.Index,
                    (ulong)paddedBytes);
                using var commandBuffer = encoder.Finish();
                _submitArray[0] = commandBuffer;
                accelerator.NativeAccelerator.Queue?.Submit(_submitArray);
            }
        }

        protected override void CopyTo(AcceleratorStream stream, in ArrayView<byte> source, in ArrayView<byte> destination)
        {
            // GPU to CPU - This is inherently async in WebGPU.
            // For now, we throw as ILGPU expects sync behavior here.
            // Users should use CopyToHostAsync in WebGPUBuffer for now.
            throw new NotSupportedException("Synchronous GPU to CPU copies are not supported in WebGPU backend. Use CopyToHostAsync.");
        }

        /// <summary>
        /// Real async GPU-&gt;CPU readback via <c>CopyBufferToBuffer</c> + <c>mapAsync</c>.
        /// Overrides <see cref="MemoryBuffer.CopyToRawAsync"/> because the synchronous
        /// <see cref="CopyTo"/> above is impossible on WebGPU (readback is inherently
        /// async). This makes <c>ArrayView&lt;T&gt;.CopyToCPUAsync</c> and algorithm-layer
        /// async readback work on WebGPU instead of throwing.
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
            using var u8 = await NativeBuffer.CopyToHostUint8ArrayAsync(
                sourceOffsetInBytes, lengthInBytes);
            return u8.ReadBytes();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Zero-copy upload from an <see cref="IJSReadStream"/> via
        /// <see cref="CopyFromJS(TypedArray, long)"/> (queue.writeBuffer) when the transfer is
        /// 4-byte aligned - which fp32 and even-count <c>ArrayView&lt;Half&gt;</c> uploads always are,
        /// and the default 16MiB chunk preserves. WebGPU's writeBuffer REQUIRES the destination
        /// offset and the byte count to be 4-byte multiples, so a non-4-aligned upload (e.g. an
        /// odd-count <c>ArrayView&lt;Half&gt;</c> = 2 mod 4 bytes) falls back to the managed base path,
        /// which pads to the buffer's 4-byte-padded allocation. (Mirrors ML's
        /// even-count -&gt; CopyFromJS / odd -&gt; byte[] gate.) A plain .NET
        /// <see cref="System.IO.Stream"/> also takes the base path.
        /// </remarks>
        protected override System.Threading.Tasks.Task CopyFromStreamRawAsync(
            AcceleratorStream stream,
            System.IO.Stream source,
            long targetOffsetInBytes,
            long lengthInBytes,
            int chunkSizeInBytes,
            System.Threading.CancellationToken cancellationToken)
        {
            if (source is IJSReadStream js &&
                (targetOffsetInBytes & 3L) == 0L &&
                (lengthInBytes & 3L) == 0L &&
                (chunkSizeInBytes & 3) == 0)
                return BrowserStreamUpload.CopyFromJSReadStreamAsync(
                    this, js, targetOffsetInBytes, lengthInBytes, LengthInBytes,
                    chunkSizeInBytes, cancellationToken);
            return base.CopyFromStreamRawAsync(
                stream, source, targetOffsetInBytes, lengthInBytes,
                chunkSizeInBytes, cancellationToken);
        }

        protected override void MemSet(AcceleratorStream stream, byte value, in ArrayView<byte> view)
        {
            var length = (int)view.Length;
            var paddedLength = (int)WebGPUAlignment.AlignTo4(length);
            var accelerator = (WebGPUAccelerator)Accelerator;
            var viewContiguous = (IContiguousArrayView)view;

            if (value == 0)
            {
                // Use encoder.ClearBuffer — records the zero-fill into the command
                // encoder pipeline alongside compute passes, with proper implicit
                // barriers.  This avoids Queue.WriteBuffer which is a separate
                // queue-timeline operation and may have subtle ordering issues
                // with subsequent dispatches in some browser implementations.
                accelerator.RecordClearBuffer(
                    stream,
                    _buffer!.NativeBuffer!,
                    (ulong)viewContiguous.Index,
                    (ulong)paddedLength);
            }
            else
            {
                // Non-zero fill: must use WriteBuffer (no encoder-level fill API)
                accelerator.FlushPendingCommands();
                var data = new byte[paddedLength];
                global::System.Array.Fill(data, value);
                using var typedArray = new Uint8Array(data);
                accelerator.NativeAccelerator.Queue!.WriteBuffer(
                    _buffer!.NativeBuffer!,
                    (long)viewContiguous.Index,
                    typedArray);
            }
        }

        // DisposeAcceleratorObject is protected (not protected internal) in base AcceleratorObject
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (disposing) _buffer?.Dispose();
        }



    }
}
