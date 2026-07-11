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

        public WebGPUMemoryBuffer(WebGPUAccelerator accelerator, long length, int elementSize, int bitsPerElement = 0)
            : base(accelerator, length, elementSize, bitsPerElement)
        {
            _buffer = accelerator.NativeAccelerator.Allocate<byte>(LengthInBytes);
        }

        /// <summary>
        /// Protected constructor for subclasses that provide their own buffer (e.g. ExternalWebGPUMemoryBuffer).
        /// Does NOT allocate a new GPU buffer — the subclass is responsible for providing NativeBuffer.
        /// </summary>
        protected WebGPUMemoryBuffer(WebGPUAccelerator accelerator, long length, int elementSize, bool skipAllocation, int bitsPerElement = 0)
            : base(accelerator, length, elementSize, bitsPerElement)
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

                // Fail-loud guard (opt-in, default OFF; shared across all browser backends): bulk data
                // must stream JS-side (CopyFromStreamAsync/CopyFromJS over an IJSReadStream), never
                // through this synchronous .NET CopyFromCPU path. See BrowserBufferPolicy.
                BrowserBufferPolicy.CheckHostCopy(length, "WebGPU");

                // Use IContiguousArrayView to access internal members
                var sourceContiguous = (IContiguousArrayView)source;
                var sourceBuffer = sourceContiguous.Buffer;
                var srcPtr = sourceBuffer.NativePtr + (int)sourceContiguous.Index;

                // Flush pending dispatches before writing (queue-timeline ordering: a pending dispatch
                // that reads this buffer must be submitted BEFORE the writeBuffer overwrites it).
                var accelerator = (WebGPUAccelerator)Accelerator;
                accelerator.FlushPendingCommands();

                var destContiguous = (IContiguousArrayView)destination;

                // ZERO-COPY host->GPU upload. In Blazor WASM the CPU source already lives in WASM linear
                // memory, so `srcPtr` is a byte offset into the JS heap ArrayBuffer (Module.HEAPU8.buffer) -
                // the exact mechanism HeapView uses. Wrap those bytes in a transient Uint8Array VIEW (no
                // copy) and let queue.writeBuffer consume it synchronously (writeBuffer copies host->GPU
                // immediately, within this sync call, so a heap resize cannot invalidate the view mid-flight).
                // This replaces the old `new byte[]` + Marshal.Copy + `(ArrayBuffer)byteArray` whole-buffer
                // JS marshal - two redundant copies + a fresh JS allocation per call, the CopyFromCPU wrapper
                // cost Seven measured at ~14ms vs 0.02ms for a raw writeBuffer.
                //
                // writeBuffer REQUIRES a 4-byte-multiple byte count; fp32 (and even-count Half) uploads always
                // are, so they take the zero-copy fast path. A non-4-aligned length (odd-count sub-word) falls
                // back to a padded copy - rare, and the destination is 4-byte-padded at allocation.
                if ((length & 3) == 0)
                {
                    // heap view as Uint8Array
                    using var heapView = new HeapViewPtr(srcPtr, length);
                    using var srcView = heapView.As<Uint8Array>();
                    accelerator.NativeAccelerator.Queue!.WriteBuffer(_buffer!.NativeBuffer!, (long)destContiguous.Index, srcView);
                }
                else
                {
                    // create properly sized source
                    using var typedArray = new Uint8Array(WebGPUAlignment.AlignTo4(length));
                    // heap view as Uint8Array
                    using var heapView = new HeapViewPtr(srcPtr, length);
                    using var srcView = heapView.As<Uint8Array>();
                    // copy heap view to into the properly sized Uint8Array
                    typedArray.Set(srcView);
                    accelerator.NativeAccelerator.Queue!.WriteBuffer(_buffer!.NativeBuffer!, (long)destContiguous.Index, typedArray);
                }
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
                // Dispatch-plan capture: device copies during a forward (Concat assembly, cache writes)
                // move data recomputed by earlier replayed dispatches - a replay must re-run them.
                accelerator.ActiveDispatchPlan?.RecordCopy(
                    srcGpuBuffer, (ulong)srcContiguous.Index,
                    _buffer!.NativeBuffer!, (ulong)destContiguous.Index,
                    (ulong)paddedBytes);
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

        /// <inheritdoc/>
        /// <remarks>
        /// Zero-copy save to an <see cref="IJSWriteStream"/> via chunked
        /// <see cref="CopyToHostUint8ArrayAsync"/> -&gt; <c>WriteUint8ArrayAsync</c>: the WebGPU readback
        /// (<c>copyBufferToBuffer</c> + <c>mapAsync</c>) keeps the bytes JS-side, never entering the
        /// .NET/WASM managed heap, and handles arbitrary (non-4-aligned) sizes internally. A plain .NET
        /// <see cref="System.IO.Stream"/> target takes the managed base path (async readback -&gt; WriteAsync).
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
