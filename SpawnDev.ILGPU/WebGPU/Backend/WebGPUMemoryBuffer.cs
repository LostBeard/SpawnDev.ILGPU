using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.Toolbox;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// ILGPU <see cref="MemoryBuffer"/> backed by a WebGPU <c>GPUBuffer</c> (sizes padded to 4 bytes).
    /// GPU-&gt;CPU readback is async-only (<c>CopyToHostAsync</c>); uploads and GPU-&gt;GPU copies are synchronous and
    /// ordered with pending dispatches.
    /// </summary>
    public class WebGPUMemoryBuffer : MemoryBuffer, IBrowserMemoryBuffer
    {
        private readonly WebGPUBuffer<byte>? _buffer;

        /// <summary>Allocates the GPU buffer through the accelerator.</summary>
        /// <param name="accelerator">The owning accelerator.</param>
        /// <param name="length">Number of elements.</param>
        /// <param name="elementSize">Element size in bytes.</param>
        /// <param name="bitsPerElement">Bits per element for packed sub-byte types, or 0.</param>
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

        /// <summary>
        /// Reads the buffer back from the GPU and returns the bytes as a JS <see cref="Uint8Array"/>, without copying
        /// them into the .NET heap.
        /// </summary>
        /// <param name="sourceByteOffset">Byte offset to start reading at.</param>
        /// <param name="copyBytes">Number of bytes to read, or null for the rest of the buffer.</param>
        /// <returns>A Uint8Array the caller should dispose.</returns>
        public Task<Uint8Array> CopyToHostUint8ArrayAsync(long sourceByteOffset = 0, long? copyBytes = null) => NativeBuffer.CopyToHostUint8ArrayAsync(sourceByteOffset, copyBytes);

        /// <inheritdoc/>
        /// <remarks>Any byte offset and length: an aligned range is one <c>writeBuffer</c>; an unaligned one goes
        /// through an aligned scratch buffer and a byte kernel (see <see cref="UploadJSBytes"/>).</remarks>
        public void CopyFromJS(TypedArray source, long targetByteOffset = 0)
        {
            if ((targetByteOffset & 3) == 0 && (source.ByteLength & 3) == 0 && targetByteOffset + source.ByteLength <= LengthInBytes)
            {
                NativeBuffer.CopyFromJS(source, targetByteOffset);
                return;
            }
            using var bytes = new Uint8Array(source.Buffer, (int)source.ByteOffset, (int)source.ByteLength);
            UploadJSBytes(bytes, targetByteOffset);
        }

        /// <inheritdoc/>
        /// <remarks>Any byte offset and length, as <see cref="CopyFromJS(TypedArray, long)"/>.</remarks>
        public void CopyFromJS(ArrayBuffer source, long targetByteOffset = 0)
        {
            if ((targetByteOffset & 3) == 0 && (source.ByteLength & 3) == 0 && targetByteOffset + source.ByteLength <= LengthInBytes)
            {
                NativeBuffer.CopyFromJS(source, targetByteOffset);
                return;
            }
            using var bytes = new Uint8Array(source);
            UploadJSBytes(bytes, targetByteOffset);
        }

        /// <summary>
        /// Returns the underlying WebGPU byte buffer. Virtual so subclasses can provide an external buffer.
        /// </summary>
        public virtual WebGPUBuffer<byte> NativeBuffer => _buffer!;

        // Implementation of abstract members
        /// <summary>
        /// Copies into this buffer. A CPU source is uploaded zero-copy from WASM linear memory: small uploads are
        /// recorded in the pending batch, larger ones flush pending work and use <c>queue.writeBuffer</c>. A WebGPU
        /// source is recorded as an ordered <c>copyBufferToBuffer</c>.
        /// </summary>
        /// <param name="stream">The accelerator stream.</param>
        /// <param name="source">The source byte range.</param>
        /// <param name="destination">The byte range of this buffer to write.</param>
        /// <exception cref="InvalidOperationException">A non-CPU source is not a WebGPU buffer.</exception>
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

                var accelerator = (WebGPUAccelerator)Accelerator;
                var destContiguous = (IContiguousArrayView)destination;

                // writeBuffer takes only a 4-byte-aligned offset and size: an odd offset was rejected (write dropped)
                // and a padded size overwrote the neighbouring bytes. Such ranges go through a byte kernel.
                if (!IsNativeAddressable(destContiguous.Index, length, LengthInBytes))
                {
                    UploadThroughScratch(accelerator, stream, srcPtr, length, destination);
                    return;
                }

                // A small upload joins the dispatch batch as an ORDERED record (see WebGPUStream.BatchUpload):
                // same ordering as flush-then-write, without splitting the batch or crossing to JS here.
                if (accelerator.TryBatchUpload(stream, _buffer!.NativeBuffer!, destContiguous.Index, srcPtr, length))
                    return;

                // Flush pending dispatches before writing (queue-timeline ordering: a pending dispatch
                // that reads this buffer must be submitted BEFORE the writeBuffer overwrites it).
                accelerator.FlushPendingCommands();


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
                // are, so they take the zero-copy fast path. A non-4-aligned length reaches here only when the
                // range ends at the buffer's end (IsNativeAddressable), so the padded copy writes into the
                // allocation's padding, never into another element.
                if ((length & 3) == 0)
                {
                    // heap view as Uint8Array
                    using var heapView = new HeapView<byte, Uint8Array>(srcPtr, length);
                    accelerator.NativeAccelerator.Queue!.WriteBuffer(_buffer!.NativeBuffer!, (long)destContiguous.Index, heapView.View);
                }
                else
                {
                    // create properly sized source
                    using var typedArray = new Uint8Array(WebGPUAlignment.AlignTo4(length));
                    // heap view as Uint8Array
                    using var heapView = new HeapView<byte, Uint8Array>(srcPtr, length);
                    // copy heap view to into the properly sized Uint8Array
                    typedArray.Set(heapView.View);
                    accelerator.NativeAccelerator.Queue!.WriteBuffer(_buffer!.NativeBuffer!, (long)destContiguous.Index, typedArray);
                }
            }
            else
            {
                // GPU-to-GPU copy using CopyBufferToBuffer, recorded on the stream (see RecordCopyBuffer)
                var accelerator = (WebGPUAccelerator)Accelerator;

                var srcContiguous = (IContiguousArrayView)source;
                var srcMemBuffer = srcContiguous.Buffer as WebGPUMemoryBuffer
                    ?? throw new InvalidOperationException("Source buffer is not a WebGPU memory buffer");
                var srcGpuBuffer = srcMemBuffer.NativeBuffer.NativeBuffer
                    ?? throw new InvalidOperationException("Source GPU buffer is null");

                var destContiguous = (IContiguousArrayView)destination;

                CopyDeviceRange(accelerator, stream,
                    source, srcGpuBuffer, srcContiguous.Index,
                    destination, NativeBuffer.NativeBuffer!, destContiguous.Index, LengthInBytes);
            }
        }

        /// <summary>
        /// Copies from this buffer to another WebGPU buffer as an ordered <c>copyBufferToBuffer</c>. GPU-&gt;CPU copies
        /// are async-only on WebGPU.
        /// </summary>
        /// <param name="stream">The accelerator stream.</param>
        /// <param name="source">The byte range of this buffer to read.</param>
        /// <param name="destination">The destination byte range.</param>
        /// <exception cref="NotSupportedException">The destination is not a WebGPU buffer (use <c>CopyToHostAsync</c>).</exception>
        protected override void CopyTo(AcceleratorStream stream, in ArrayView<byte> source, in ArrayView<byte> destination)
        {
            // GPU to GPU: ILGPU calls CopyTo on the SOURCE buffer (view.CopyTo(target)), and the target can be another
            // WebGPU buffer - record CopyBufferToBuffer exactly as CopyFrom's device branch does. This used to throw
            // for every target, so `gpuView.CopyTo(gpuView)` failed on WebGPU while `target.CopyFrom(gpuView)` worked
            // (found 2026-10-07 copying a MemoryBuffer2D's view into a 1D buffer, SpawnScene super-resolution).
            var destContiguous = (IContiguousArrayView)destination;
            if (destContiguous.Buffer is WebGPUMemoryBuffer destMemBuffer)
            {
                var accelerator = (WebGPUAccelerator)Accelerator;
                var srcContiguous = (IContiguousArrayView)source;
                var srcMemBuffer = srcContiguous.Buffer as WebGPUMemoryBuffer
                    ?? throw new InvalidOperationException("Source buffer is not a WebGPU memory buffer");
                var srcGpuBuffer = srcMemBuffer.NativeBuffer.NativeBuffer
                    ?? throw new InvalidOperationException("Source GPU buffer is null");
                var dstGpuBuffer = destMemBuffer.NativeBuffer.NativeBuffer
                    ?? throw new InvalidOperationException("Destination GPU buffer is null");
                CopyDeviceRange(accelerator, stream,
                    source, srcGpuBuffer, srcContiguous.Index,
                    destination, dstGpuBuffer, destContiguous.Index, destMemBuffer.LengthInBytes);
                return;
            }

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

        /// <summary>
        /// Fills <paramref name="view"/> with <paramref name="value"/>. Zero is recorded as an ordered
        /// <c>clearBuffer</c> in the pending batch; other values flush pending work and use <c>queue.writeBuffer</c>.
        /// The length is padded to a multiple of 4 bytes.
        /// </summary>
        /// <param name="stream">The accelerator stream.</param>
        /// <param name="value">The byte value to write.</param>
        /// <param name="view">The byte range of this buffer to fill.</param>
        protected override void MemSet(AcceleratorStream stream, byte value, in ArrayView<byte> view)
        {
            var accelerator = (WebGPUAccelerator)Accelerator;
            var viewContiguous = (IContiguousArrayView)view;

            // clearBuffer / writeBuffer address only 4-byte-aligned ranges: the unaligned head and tail bytes are
            // filled by a byte kernel, the aligned body natively.
            var (head, body, tail) = SplitRange(viewContiguous.Index, view.Length, LengthInBytes);
            if (head > 0) FillBytes(accelerator, stream, view.SubView(0, head), value);
            if (tail > 0) FillBytes(accelerator, stream, view.SubView(head + body, tail), value);
            if (body == 0) return;
            var bodyOffset = viewContiguous.Index + head;
            var paddedLength = (int)WebGPUAlignment.AlignTo4(body);

            if (value == 0)
            {
                // Use encoder.ClearBuffer — records the zero-fill into the command
                // encoder pipeline alongside compute passes, with proper implicit
                // barriers.  This avoids Queue.WriteBuffer which is a separate
                // queue-timeline operation and may have subtle ordering issues
                // with subsequent dispatches in some browser implementations.
                accelerator.RecordClearBuffer(
                    stream,
                    NativeBuffer.NativeBuffer!,
                    (ulong)bodyOffset,
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
                    NativeBuffer.NativeBuffer!,
                    bodyOffset,
                    typedArray);
            }
        }

        // ── Byte ranges WebGPU's copy commands cannot address ──────────────────────────────────────────────────
        // writeBuffer, copyBufferToBuffer and clearBuffer take only offsets and sizes that are multiples of 4. A
        // rejected command does not throw: it invalidates the command buffer and the operation is silently dropped,
        // and a size padded up to 4 writes up to 3 bytes past the range. Both hit byte and short views at arbitrary
        // offsets (SpawnDev.Codecs, 2026-10-23: header bytes at odd file offsets, poison-tail checks). Unaligned
        // ranges are moved by a byte kernel instead: WGSL byte stores are word read-modify-writes with atomics, so a
        // kernel can write any byte range without touching its neighbours, and a launch is ordered with the stream.

        private static void CopyBytesKernel(Index1D i, ArrayView<byte> source, ArrayView<byte> target) => target[i] = source[i];

        private static void FillBytesKernel(Index1D i, ArrayView<byte> target, int value) => target[i] = (byte)value;

        private sealed class ByteKernels
        {
            public Action<AcceleratorStream, Index1D, ArrayView<byte>, ArrayView<byte>>? Copy;
            public Action<AcceleratorStream, Index1D, ArrayView<byte>, int>? Fill;
        }

        private static readonly ConditionalWeakTable<WebGPUAccelerator, ByteKernels> s_byteKernels = new();

        private static void CopyBytes(WebGPUAccelerator accelerator, AcceleratorStream stream, ArrayView<byte> source, ArrayView<byte> target)
        {
            if (target.Length == 0) return;
            var kernels = s_byteKernels.GetValue(accelerator, _ => new ByteKernels());
            kernels.Copy ??= accelerator.LoadAutoGroupedKernel<Index1D, ArrayView<byte>, ArrayView<byte>>(CopyBytesKernel);
            kernels.Copy(stream, (int)target.Length, source, target);
        }

        private static void FillBytes(WebGPUAccelerator accelerator, AcceleratorStream stream, ArrayView<byte> target, byte value)
        {
            if (target.Length == 0) return;
            var kernels = s_byteKernels.GetValue(accelerator, _ => new ByteKernels());
            kernels.Fill ??= accelerator.LoadAutoGroupedKernel<Index1D, ArrayView<byte>, int>(FillBytesKernel);
            kernels.Fill(stream, (int)target.Length, target, value);
        }

        /// <summary>
        /// True when a native command can write [<paramref name="start"/>, +<paramref name="length"/>) of a buffer of
        /// <paramref name="bufferBytes"/> bytes: the offset is a multiple of 4 and the size is too, or the range ends at
        /// the buffer's end so the padded size lands in the allocation's padding.
        /// </summary>
        private static bool IsNativeAddressable(long start, long length, long bufferBytes) =>
            (start & 3) == 0 && ((length & 3) == 0 || start + length == bufferBytes);

        /// <summary>
        /// Splits a destination range into unaligned head bytes, a natively addressable body and unaligned tail bytes
        /// (a range ending at the buffer's end has no tail: its padded body ends in the allocation's padding).
        /// </summary>
        private static (long Head, long Body, long Tail) SplitRange(long start, long length, long bufferBytes)
        {
            long end = start + length;
            long bodyStart = (start + 3) & ~3L;
            long bodyEnd = end == bufferBytes ? end : end & ~3L;
            if (bodyStart >= bodyEnd)
                return (length, 0, 0);
            return (bodyStart - start, bodyEnd - bodyStart, end - bodyEnd);
        }

        /// <summary>
        /// Records a device copy of <paramref name="source"/> into <paramref name="destination"/>: natively when both
        /// offsets are aligned, a native body plus byte-kernel edges when they share the same offset mod 4, and the
        /// byte kernel for the whole range otherwise.
        /// </summary>
        private static void CopyDeviceRange(
            WebGPUAccelerator accelerator, AcceleratorStream stream,
            ArrayView<byte> source, GPUBuffer srcGpuBuffer, long srcStart,
            ArrayView<byte> destination, GPUBuffer dstGpuBuffer, long dstStart, long dstBufferBytes)
        {
            long length = destination.Length;
            if ((srcStart & 3) == 0 && IsNativeAddressable(dstStart, length, dstBufferBytes))
            {
                accelerator.RecordCopyBuffer(stream,
                    srcGpuBuffer, (ulong)srcStart, dstGpuBuffer, (ulong)dstStart,
                    (ulong)WebGPUAlignment.AlignTo4(length));
                return;
            }
            if ((srcStart & 3) != (dstStart & 3))
            {
                CopyBytes(accelerator, stream, source, destination);
                return;
            }
            var (head, body, tail) = SplitRange(dstStart, length, dstBufferBytes);
            if (head > 0)
                CopyBytes(accelerator, stream, source.SubView(0, head), destination.SubView(0, head));
            if (body > 0)
                accelerator.RecordCopyBuffer(stream,
                    srcGpuBuffer, (ulong)(srcStart + head), dstGpuBuffer, (ulong)(dstStart + head),
                    (ulong)WebGPUAlignment.AlignTo4(body));
            if (tail > 0)
                CopyBytes(accelerator, stream, source.SubView(head + body, tail), destination.SubView(head + body, tail));
        }

        /// <summary>
        /// Uploads <paramref name="length"/> host bytes into an unaligned <paramref name="destination"/>: one
        /// <c>writeBuffer</c> into an aligned scratch buffer, then the byte kernel, ordered on <paramref name="stream"/>.
        /// Disposing the scratch buffer submits the pending work first (see <see cref="DisposeAcceleratorObject"/>).
        /// </summary>
        private static void UploadThroughScratch(WebGPUAccelerator accelerator, AcceleratorStream stream, IntPtr src, int length, ArrayView<byte> destination)
        {
            using var heapView = new HeapView<byte, Uint8Array>(src, length);
            UploadThroughScratch(accelerator, stream, heapView.View, destination);
        }

        private static void UploadThroughScratch(WebGPUAccelerator accelerator, AcceleratorStream stream, Uint8Array bytes, ArrayView<byte> destination)
        {
            int length = (int)destination.Length;
            var padded = WebGPUAlignment.AlignTo4(length);
            using var scratch = accelerator.Allocate1D<byte>(padded);
            var scratchView = scratch.View.BaseView;
            var scratchGpu = ((WebGPUMemoryBuffer)((IContiguousArrayView)scratchView).Buffer).NativeBuffer.NativeBuffer!;
            using (var aligned = new Uint8Array(padded))
            {
                aligned.Set(bytes);
                accelerator.NativeAccelerator.Queue!.WriteBuffer(scratchGpu, 0, aligned);
            }
            CopyBytes(accelerator, stream, scratchView.SubView(0, length), destination);
        }

        /// <summary>
        /// Uploads JS bytes at any byte offset: natively when the range is aligned (padding the size when it ends at
        /// the buffer's end), through the scratch buffer and the byte kernel otherwise.
        /// </summary>
        private void UploadJSBytes(Uint8Array bytes, long targetByteOffset)
        {
            long length = bytes.Length;
            if (targetByteOffset < 0 || targetByteOffset + length > LengthInBytes)
                throw new ArgumentOutOfRangeException(nameof(targetByteOffset),
                    $"Byte range [{targetByteOffset}, {targetByteOffset + length}) exceeds the buffer's {LengthInBytes} bytes.");
            if (length == 0) return;
            if (IsNativeAddressable(targetByteOffset, length, LengthInBytes))
            {
                if ((length & 3) == 0)
                {
                    NativeBuffer.CopyFromJS(bytes, targetByteOffset);
                    return;
                }
                using var aligned = new Uint8Array(WebGPUAlignment.AlignTo4(length));
                aligned.Set(bytes);
                NativeBuffer.CopyFromJS(aligned, targetByteOffset);
                return;
            }
            var accelerator = (WebGPUAccelerator)Accelerator;
            UploadThroughScratch(accelerator, accelerator.DefaultStream, bytes, AsRawArrayView(targetByteOffset, length));
        }

        // DisposeAcceleratorObject is protected (not protected internal) in base AcceleratorObject
        /// <summary>Submits any pending accelerator work that may reference this buffer, then destroys the GPU buffer.</summary>
        /// <param name="disposing">True when disposing managed resources; nothing is done otherwise.</param>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (!disposing) return;
            // A plain-dispatch record batch names buffers by id and resolves them only at submit, and a small
            // CopyFromCPU is now a record in it. Disposing right after an upload was always legal (the upload used
            // to happen on the spot), so it must stay legal: submit first, then destroy - WebGPU lets a buffer be
            // destroyed once the work using it is submitted. MEASURED 2026-09-30 without this: "upload target ...
            // is not held" in FloatRoundToEven_And_LongMinMax / CopyFromJS_ArrayBuffer_WritesCorrectDataTest.
            // The same holds for the per-dispatch path's open command encoder (capture, bind-group caching): a
            // MemSetToZero recorded there references this buffer until submit. MEASURED 2026-09-30 (Tuvok, SpawnScene):
            // allocate, MemSetToZero, Dispose before any flush -> the NEXT unrelated submit failed "used in submit while
            // destroyed" (Dispose_WithPendingClear_DoesNotBreakNextSubmit). Any pending work is submitted first.
            if (Accelerator is WebGPUAccelerator acc && !acc.IsDisposed && acc.HasPendingWorkAny)
                acc.FlushPendingCommands();
            _buffer?.Dispose();
        }



    }
}
