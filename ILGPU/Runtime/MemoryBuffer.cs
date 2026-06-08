// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: MemoryBuffer.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Resources;
using ILGPU.Runtime.CPU;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Represents an abstract memory buffer that can be used in the scope of ILGPU
    /// runtime kernels.
    /// </summary>
    /// <remarks>Members of this class are not thread safe.</remarks>
    public abstract class MemoryBuffer : AcceleratorObject
    {
        #region Instance

        /// <summary>
        /// Initializes this array view buffer.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        /// <param name="length">The length of this buffer.</param>
        /// <param name="elementSize">The element size.</param>
        protected MemoryBuffer(
            Accelerator accelerator,
            long length,
            int elementSize)
            : base(accelerator)
        {
            Init(length, elementSize);
        }

        /// <summary>
        /// Initializes the internal length properties.
        /// </summary>
        /// <param name="length">The length of this buffer.</param>
        /// <param name="elementSize">The element size.</param>
        private void Init(long length, int elementSize)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (elementSize < 1)
                throw new ArgumentOutOfRangeException(nameof(elementSize));

            Length = length;
            ElementSize = elementSize;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the native pointer of this buffer.
        /// </summary>
        public IntPtr NativePtr { get; set; }

        /// <summary>
        /// Returns the length of this buffer.
        /// </summary>
        public long Length { get; private set; }

        /// <summary>
        /// Returns the element size.
        /// </summary>
        public int ElementSize { get; private set; }

        /// <summary>
        /// Returns the length of this buffer in bytes.
        /// </summary>
        public long LengthInBytes => Length * ElementSize;

        #endregion

        #region Methods

        /// <summary>
        /// Sets the contents of the current buffer to the given byte value.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="value">The value to write into the memory buffer.</param>
        /// <param name="targetOffsetInBytes">The target offset in bytes.</param>
        /// <param name="length">The number of bytes to set.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MemSet(
            AcceleratorStream stream,
            byte value,
            long targetOffsetInBytes,
            long length)
        {
            if (length == 0)
                return;
            var targetView = AsRawArrayView(targetOffsetInBytes, length);
            MemSet(stream, value, targetView);
        }

        /// <summary>
        /// Sets the contents of the current buffer to the given byte value.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="value">The value to write into the memory buffer.</param>
        /// <param name="targetView">The raw target view of this buffer.</param>
        protected internal abstract void MemSet(
            AcceleratorStream stream,
            byte value,
            in ArrayView<byte> targetView);

        /// <summary>
        /// Copies elements from the current buffer to the target view.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceOffsetInBytes">The source offset in bytes.</param>
        /// <param name="targetView">The target view.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyTo(
            AcceleratorStream stream,
            long sourceOffsetInBytes,
            in ArrayView<byte> targetView)
        {
            if (!targetView.IsValid)
                return;
            var sourceView = AsRawArrayView(
                sourceOffsetInBytes,
                targetView.LengthInBytes);
            CopyTo(stream, sourceView, targetView);
        }

        /// <summary>
        /// Copies elements from the current buffer to the target view.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceView">The source view of this buffer.</param>
        /// <param name="targetView">The target view.</param>
        protected internal abstract void CopyTo(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView);

        /// <summary>
        /// Asynchronously copies a raw byte range of this buffer back to the host.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceOffsetInBytes">The source offset in bytes.</param>
        /// <param name="lengthInBytes">The number of bytes to read back.</param>
        /// <returns>A task producing the copied bytes.</returns>
        /// <remarks>
        /// This is the overridable async GPU-&gt;CPU readback hook. The default
        /// implementation awaits <see cref="AcceleratorStream.SynchronizeAsync"/>
        /// (the real drain on backends that override it) and then performs the
        /// synchronous <see cref="CopyTo(AcceleratorStream, long, in ArrayView{byte})"/>,
        /// which is correct for CUDA / OpenCL / CPU. Browser backends MUST override
        /// this because their synchronous <see cref="CopyTo"/> either reads stale data
        /// before worker kernels finish (Wasm) or throws because synchronous GPU-&gt;CPU
        /// readback is impossible (WebGPU / WebGL); each provides a true async readback
        /// (SharedArrayBuffer read after drain, <c>mapAsync</c>, GL worker readback).
        /// Prefer the typed <c>ArrayView&lt;T&gt;.CopyToCPUAsync</c> extension over
        /// calling this directly.
        /// </remarks>
        protected internal virtual async Task<byte[]> CopyToRawAsync(
            AcceleratorStream stream,
            long sourceOffsetInBytes,
            long lengthInBytes)
        {
            if (lengthInBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(lengthInBytes));

            // Real drain first. Overridden to a true async wait on browser backends;
            // offloads the blocking Synchronize on CUDA / OpenCL / CPU.
            await stream.SynchronizeAsync().ConfigureAwait(false);

            var result = new byte[lengthInBytes];
            if (lengthInBytes == 0)
                return result;

            using var cpuBuffer = CPUMemoryBuffer.Create(
                Accelerator,
                ref result[0],
                lengthInBytes);
            CopyTo(stream, sourceOffsetInBytes, cpuBuffer.AsRawArrayView());
            return result;
        }

        /// <summary>
        /// The default chunk size (16 MiB) used by
        /// <see cref="CopyFromStreamRawAsync"/> and the
        /// <c>ArrayView&lt;T&gt;.CopyFromStreamAsync</c> extension when none is specified.
        /// Large enough to fully amortize the per-transfer fixed cost (browser JS interop,
        /// queue submit), small enough that a multi-GB upload never spikes a multi-GB
        /// intermediate allocation.
        /// </summary>
        public const int DefaultStreamChunkSizeInBytes = 16 * 1024 * 1024;

        /// <summary>
        /// Asynchronously streams <paramref name="lengthInBytes"/> bytes from a
        /// <see cref="Stream"/> into this buffer at <paramref name="targetOffsetInBytes"/>,
        /// in chunks, awaiting the source's async reads.
        /// </summary>
        /// <remarks>
        /// This is the overridable async Stream-&gt;GPU upload hook (the write-side mirror of
        /// <see cref="CopyToRawAsync"/>). The default implementation genuinely awaits
        /// <see cref="Stream.ReadExactlyAsync(Memory{byte}, CancellationToken)"/> into a pooled
        /// buffer and copies each chunk via <see cref="CopyFrom(AcceleratorStream, in ArrayView{byte}, long)"/>
        /// - so on CUDA / OpenCL / CPU a model streaming off disk or the network no longer blocks
        /// a thread on a synchronous read (unlike the older async paths that just wrapped a sync
        /// call). It is correct on every backend, including the browser backends via the managed
        /// hop. Browser backends override this to take a fast path when the source is a
        /// <c>SpawnDev.BlazorJS.Toolbox.IJSReadStream</c>: the data is read as a JS
        /// <c>Uint8Array</c> and uploaded via <c>IBrowserMemoryBuffer.CopyFromJS</c> without ever
        /// entering the .NET/WASM managed heap. Reads EXACTLY <paramref name="lengthInBytes"/>
        /// bytes - a stream that ends early throws <see cref="EndOfStreamException"/> (a truncated
        /// asset surfaces instead of silently zero-padding). Prefer the typed
        /// <c>ArrayView&lt;T&gt;.CopyFromStreamAsync</c> extension over calling this directly.
        /// </remarks>
        /// <param name="stream">The accelerator stream the chunk copies are issued on.</param>
        /// <param name="source">The byte source. Read sequentially from its current position.</param>
        /// <param name="targetOffsetInBytes">The destination byte offset within this buffer.</param>
        /// <param name="lengthInBytes">The exact number of bytes to read and upload.</param>
        /// <param name="chunkSizeInBytes">The per-chunk transfer size.</param>
        /// <param name="cancellationToken">Cancels the in-flight reads.</param>
        protected internal virtual async Task CopyFromStreamRawAsync(
            AcceleratorStream stream,
            Stream source,
            long targetOffsetInBytes,
            long lengthInBytes,
            int chunkSizeInBytes,
            CancellationToken cancellationToken)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            if (lengthInBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(lengthInBytes));
            if (chunkSizeInBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSizeInBytes));
            if (targetOffsetInBytes < 0 ||
                targetOffsetInBytes + lengthInBytes > LengthInBytes)
                throw new ArgumentOutOfRangeException(nameof(targetOffsetInBytes));
            if (lengthInBytes == 0)
                return;

            long remaining = lengthInBytes;
            long destOffset = targetOffsetInBytes;
            int bufferSize = (int)Math.Min((long)chunkSizeInBytes, remaining);
            byte[] chunk = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                while (remaining > 0)
                {
                    int want = (int)Math.Min((long)bufferSize, remaining);
                    // Reads exactly `want` bytes; throws EndOfStreamException on early EOF.
                    await source
                        .ReadExactlyAsync(chunk.AsMemory(0, want), cancellationToken)
                        .ConfigureAwait(false);
                    using var cpuBuffer = CPUMemoryBuffer.Create(
                        Accelerator,
                        ref chunk[0],
                        want);
                    CopyFrom(stream, cpuBuffer.AsRawArrayView(), destOffset);
                    destOffset += want;
                    remaining -= want;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }
        }

        /// <summary>
        /// Copies elements from the source view to the current buffer.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceView">The source view.</param>
        /// <param name="targetOffsetInBytes">The target offset in bytes.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            long targetOffsetInBytes)
        {
            if (!sourceView.IsValid)
                return;
            var targetView = AsRawArrayView(
                targetOffsetInBytes,
                sourceView.LengthInBytes);
            CopyFrom(stream, sourceView, targetView);
        }

        /// <summary>
        /// Copies elements from the source view to the current buffer.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceView">The source view.</param>
        /// <param name="targetView">The target view of this buffer.</param>
        protected internal abstract void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView);

        /// <summary>
        /// Copies data from the source buffer to this buffer.
        /// Override in derived classes (e.g., Wasm) to handle device-to-device
        /// copies where both buffers use non-native memory (SharedArrayBuffer).
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceBuffer">The source memory buffer.</param>
        /// <param name="sourceOffsetInBytes">Byte offset within the source.</param>
        /// <param name="targetOffsetInBytes">Byte offset within this buffer.</param>
        /// <param name="lengthInBytes">Number of bytes to copy.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected internal virtual void CopyFromBuffer(
            AcceleratorStream stream,
            MemoryBuffer sourceBuffer,
            long sourceOffsetInBytes,
            long targetOffsetInBytes,
            long lengthInBytes)
        {
            // Default: delegate to existing CopyFrom via ArrayView
            var sourceView = sourceBuffer.AsRawArrayView(
                sourceOffsetInBytes, lengthInBytes);
            CopyFrom(stream, sourceView, targetOffsetInBytes);
        }

        /// <summary>
        /// Returns a raw array view of the whole buffer.
        /// </summary>
        /// <returns>The raw array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView<byte> AsRawArrayView() =>
            AsRawArrayView(0L, LengthInBytes);

        /// <summary>
        /// Returns a raw array view starting at the given byte offset.
        /// </summary>
        /// <param name="offsetInBytes">The raw offset in bytes.</param>
        /// <returns>The raw array view.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArrayView<byte> AsRawArrayView(long offsetInBytes) =>
            AsRawArrayView(offsetInBytes, LengthInBytes - offsetInBytes);

        /// <summary>
        /// Returns a raw array slice of the this buffer.
        /// </summary>
        /// <param name="offsetInBytes">The raw offset in bytes.</param>
        /// <param name="lengthInBytes">The raw length in bytes.</param>
        /// <returns></returns>
        public ArrayView<byte> AsRawArrayView(long offsetInBytes, long lengthInBytes)
        {
            if (offsetInBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(offsetInBytes));
            if (LengthInBytes > 0 && offsetInBytes >= LengthInBytes)
                throw new ArgumentOutOfRangeException(nameof(offsetInBytes));
            if (lengthInBytes < 0 || offsetInBytes + lengthInBytes > LengthInBytes)
                throw new ArgumentOutOfRangeException(nameof(lengthInBytes));
            return new ArrayView<byte>(this, offsetInBytes, lengthInBytes);
        }

        /// <summary>
        /// Gets an array view that spans the given number of elements.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="offset">The offset.</param>
        /// <param name="length">The number of elements.</param>
        /// <returns>The created array view.</returns>
        public ArrayView<T> AsArrayView<T>(long offset, long length)
            where T : unmanaged
        {
            if (Interop.SizeOf<T>() != ElementSize)
            {
                throw new NotSupportedException(string.Format(
                    ErrorMessages.NotSupportedType,
                    nameof(T)));
            }
            if (Length > 0 && offset >= Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (offset + length > Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            return new ArrayView<T>(this, offset, length);
        }

        #endregion
    }

    /// <summary>
    /// An abstract memory buffer based on a specific view type.
    /// </summary>
    /// <typeparam name="TView">The underlying view type.</typeparam>
    public interface IMemoryBuffer<in TView> : IContiguousArrayView
        where TView : struct, IArrayView
    {
        /// <summary>
        /// Sets the contents of the current buffer to the given byte value.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="value">The value to write into the memory buffer.</param>
        /// <param name="targetOffsetInBytes">The target offset in bytes.</param>
        /// <param name="length">The number of bytes to set.</param>
        void MemSet(
            AcceleratorStream stream,
            byte value,
            long targetOffsetInBytes,
            long length);

        /// <summary>
        /// Copies elements from the current buffer to the target view.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceOffsetInBytes">The source offset in bytes.</param>
        /// <param name="targetView">The target view.</param>
        void CopyTo(
            AcceleratorStream stream,
            long sourceOffsetInBytes,
            in ArrayView<byte> targetView);

        /// <summary>
        /// Copies elements from the source view to the current buffer.
        /// </summary>
        /// <param name="stream">The used accelerator stream.</param>
        /// <param name="sourceView">The source view.</param>
        /// <param name="targetOffsetInBytes">The target offset in bytes.</param>
        void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            long targetOffsetInBytes);
    }

    /// <summary>
    /// Represents an opaque memory buffer that can be used in the scope of ILGPU runtime
    /// kernels.
    /// </summary>
    /// <typeparam name="TView">The view type.</typeparam>
    /// <remarks>Members of this class are not thread safe.</remarks>
    [DebuggerDisplay("{View}")]
    public class MemoryBuffer<TView> : MemoryBuffer, IMemoryBuffer<TView>
        where TView : struct, IArrayView
    {
        #region Instance

        /// <summary>
        /// Initializes this memory buffer.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        /// <param name="view">The extent (number of elements).</param>
        protected internal MemoryBuffer(Accelerator accelerator, in TView view)
            : base(accelerator, view.Length, view.ElementSize)
        {
            View = view;
            NativePtr = Buffer?.NativePtr ?? IntPtr.Zero;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the owned memory buffer instance.
        /// Public for GPU-to-GPU zero-copy operations (ILGPU buffer -> WebGPU vertex buffer).
        /// </summary>
        public MemoryBuffer Buffer => View.Buffer;

        /// <summary>
        /// Returns the same memory buffer instance.
        /// </summary>
        MemoryBuffer IArrayView.Buffer => Buffer;

        /// <summary>
        /// Returns the base offset 0.
        /// </summary>
        [SuppressMessage(
            "Design",
            "CA1033:Interface methods should be callable by child types",
            Justification = "We do not want to expose this implementation here.")]
        long IContiguousArrayView.Index => 0L;

        /// <summary>
        /// Returns the base offset 0.
        /// </summary>
        [SuppressMessage(
            "Design",
            "CA1033:Interface methods should be callable by child types",
            Justification = "We do not want to expose this implementation here.")]
        long IContiguousArrayView.IndexInBytes => 0L;

        /// <summary>
        /// Returns an array view that can access this buffer.
        /// </summary>
        public TView View { get; private set; }

        /// <summary>
        /// Returns true if this buffer has not been disposed.
        /// </summary>
        public bool IsValid => !IsDisposed;

        #endregion

        #region Methods

        /// <inheritdoc/>
        protected internal override void MemSet(
            AcceleratorStream stream,
            byte value,
            in ArrayView<byte> targetView) =>
            Buffer.MemSet(stream, value, targetView);

        /// <inheritdoc/>
        protected internal override void CopyTo(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView) =>
            Buffer.CopyTo(stream, sourceView, targetView);

        /// <inheritdoc/>
        protected internal override void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView) =>
            Buffer.CopyFrom(stream, sourceView, targetView);

        /// <summary>
        /// Returns an array view that can access this array.
        /// </summary>
        /// <returns>An array view that can access this array.</returns>
        public TView ToArrayView() => View;

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (!disposing)
                return;

            Buffer.Dispose();
            NativePtr = default;
            View = default;
        }

        #endregion

        #region Operators

        /// <summary>
        /// Implicitly converts this buffer into an array view.
        /// </summary>
        /// <param name="buffer">The source buffer.</param>
        public static implicit operator TView(MemoryBuffer<TView> buffer) =>
            buffer.View;

        #endregion
    }
}
