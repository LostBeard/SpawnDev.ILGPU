using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.BlazorJS.Toolbox;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// Cross-browser-backend policy for host→device transfers. Applies to EVERY browser backend
    /// (WebGPU, WebGL, Wasm) - pulling bulk data from JS into the single-threaded .NET/WASM managed
    /// heap on its way to an accelerator is the same main-thread tax on all three, not a WebGPU-only
    /// problem. The whole point of the browser backends is to keep compute (and its data) off the
    /// Blazor WASM main thread.
    /// </summary>
    public static class BrowserBufferPolicy
    {
        /// <summary>
        /// Fail-loud guard (default <c>-1</c> = OFF, no behavior change). When set to a non-negative
        /// byte count, a synchronous host→GPU <c>CopyFromCPU</c> on ANY browser backend whose transfer
        /// exceeds this many bytes THROWS <see cref="System.InvalidOperationException"/> naming the size -
        /// because on a browser backend bulk data (model weights especially) must stream JS-side via
        /// <c>CopyFromStreamAsync</c> / <c>CopyFromJS</c> over an <see cref="IJSReadStream"/> and never
        /// enter the .NET heap. A consumer wraps a load window in <c>StrictHostCopyMaxBytes = N;
        /// try { ...load... } finally { StrictHostCopyMaxBytes = -1; }</c> so any weight that regresses
        /// onto the .NET <c>CopyFromCPU</c> path trips the guard IN THE PMT RUN (on whichever browser
        /// backend the test lands) rather than silently costing seconds of main-thread copies. A small
        /// positive threshold (e.g. 65536) still lets genuinely-tiny, genuinely-.NET-origin constants
        /// through while catching every real weight. Captain directive 2026-07-05: the code enforces
        /// "model bytes stay JS+GPU" across every browser backend, not human review.
        /// </summary>
        public static long StrictHostCopyMaxBytes { get; set; } = -1;

        /// <summary>
        /// Throws if <see cref="StrictHostCopyMaxBytes"/> is enabled and <paramref name="byteLength"/>
        /// exceeds it. Called from each browser backend's host→device <c>CopyFrom</c> CPU branch.
        /// <paramref name="backend"/> names the backend in the message.
        /// </summary>
        public static void CheckHostCopy(long byteLength, string backend)
        {
            var max = StrictHostCopyMaxBytes;
            if (max >= 0 && byteLength > max)
                throw new System.InvalidOperationException(
                    $"{backend} host->GPU CopyFromCPU of {byteLength} bytes exceeds " +
                    $"BrowserBufferPolicy.StrictHostCopyMaxBytes={max}. Bulk browser data (model weights) " +
                    "must stream JS-side via CopyFromStreamAsync/CopyFromJS over an IJSReadStream and never " +
                    "enter the .NET heap. This transfer pulled bulk data through the single-threaded WASM " +
                    "managed heap.");
        }
    }

    /// <summary>
    /// Shared helper for the browser <c>MemoryBuffer.CopyFromStreamRawAsync</c> overrides:
    /// streams an <see cref="IJSReadStream"/> into a browser GPU buffer chunk-by-chunk via
    /// <see cref="IBrowserMemoryBuffer.CopyFromJS(TypedArray, long)"/>, keeping the bytes JS-side
    /// (never entering the .NET/WASM managed heap). Reads EXACTLY <paramref name="lengthInBytes"/>;
    /// throws <see cref="EndOfStreamException"/> if the stream ends early.
    /// </summary>
    internal static class BrowserStreamUpload
    {
        public static async Task CopyFromJSReadStreamAsync(
            IBrowserMemoryBuffer buffer,
            IJSReadStream source,
            long targetOffsetInBytes,
            long lengthInBytes,
            long bufferLengthInBytes,
            int chunkSizeInBytes,
            CancellationToken cancellationToken)
        {
            if (lengthInBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(lengthInBytes));
            if (chunkSizeInBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSizeInBytes));
            if (targetOffsetInBytes < 0 ||
                targetOffsetInBytes + lengthInBytes > bufferLengthInBytes)
                throw new ArgumentOutOfRangeException(nameof(targetOffsetInBytes));
            if (lengthInBytes == 0)
                return;

            long remaining = lengthInBytes;
            long destOffset = targetOffsetInBytes;
            while (remaining > 0)
            {
                int want = (int)Math.Min((long)chunkSizeInBytes, remaining);
                using var u8 = await source
                    .ReadUint8ArrayAsync(want, cancellationToken)
                    .ConfigureAwait(false);
                long got = u8?.Length ?? 0;
                if (got < want)
                    throw new EndOfStreamException(
                        $"Stream ended after {lengthInBytes - remaining + got} of " +
                        $"{lengthInBytes} bytes; CopyFromStreamAsync requires the exact length.");
                buffer.CopyFromJS(u8, destOffset);
                destOffset += want;
                remaining -= want;
            }
        }
    }

    /// <summary>
    /// Shared helper for the browser <c>MemoryBuffer.CopyToStreamRawAsync</c> overrides:
    /// streams a browser GPU buffer OUT to an <see cref="IJSWriteStream"/> chunk-by-chunk via
    /// <see cref="IBrowserMemoryBuffer.CopyToHostUint8ArrayAsync(long, long?)"/>, keeping the bytes
    /// JS-side (never entering the .NET/WASM managed heap). The save-side mirror of
    /// <see cref="BrowserStreamUpload"/>. Writes EXACTLY <paramref name="lengthInBytes"/> bytes.
    /// </summary>
    internal static class BrowserStreamDownload
    {
        public static async Task CopyToJSWriteStreamAsync(
            IBrowserMemoryBuffer buffer,
            IJSWriteStream target,
            long sourceOffsetInBytes,
            long lengthInBytes,
            long bufferLengthInBytes,
            int chunkSizeInBytes,
            CancellationToken cancellationToken)
        {
            if (lengthInBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(lengthInBytes));
            if (chunkSizeInBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSizeInBytes));
            if (sourceOffsetInBytes < 0 ||
                sourceOffsetInBytes + lengthInBytes > bufferLengthInBytes)
                throw new ArgumentOutOfRangeException(nameof(sourceOffsetInBytes));
            if (lengthInBytes == 0)
                return;

            long remaining = lengthInBytes;
            long srcOffset = sourceOffsetInBytes;
            while (remaining > 0)
            {
                int want = (int)Math.Min((long)chunkSizeInBytes, remaining);
                using var u8 = await buffer
                    .CopyToHostUint8ArrayAsync(srcOffset, want)
                    .ConfigureAwait(false);
                await target
                    .WriteUint8ArrayAsync(u8, cancellationToken)
                    .ConfigureAwait(false);
                srcOffset += want;
                remaining -= want;
            }
        }
    }

    /// <summary>
    /// Defines a contract for managing a memory buffer in a browser environment and provides asynchronous methods to
    /// copy its contents to a host-side Uint8Array.
    /// </summary>
    /// <remarks>Implementations of this interface enable efficient transfer of memory buffer data from
    /// browser-managed memory to .NET-managed arrays, which is useful for interoperability scenarios such as
    /// WebAssembly or JavaScript interop in web applications. The asynchronous nature of the copy operation allows for
    /// non-blocking data transfers, which can improve application responsiveness.</remarks>
    public interface IBrowserMemoryBuffer
    {
        /// <summary>
        /// Asynchronously copies a specified range of bytes from the buffer to a new Uint8Array on the host.
        /// </summary>
        /// <remarks>Use this method to transfer data from the buffer to a host-accessible Uint8Array for
        /// further processing or interoperability with JavaScript APIs. Ensure that the specified offset and byte count
        /// do not exceed the bounds of the source buffer to avoid errors.</remarks>
        /// <param name="sourceByteOffset">The zero-based byte offset in the source buffer at which to begin copying. Must be greater than or equal to
        /// 0.</param>
        /// <param name="copyBytes">The number of bytes to copy from the source buffer. If null, copies all bytes from the specified offset to
        /// the end of the buffer.</param>
        /// <returns>A task that represents the asynchronous copy operation. The task result contains a Uint8Array with the
        /// copied bytes.</returns>
        Task<Uint8Array> CopyToHostUint8ArrayAsync(long sourceByteOffset = 0, long? copyBytes = null);

        /// <summary>
        /// Copies data from a JS TypedArray directly into the GPU buffer without crossing into .NET managed memory.
        /// This is the zero-copy path for browser backends - data stays in JS/GPU land.
        /// Use this when data originates from JS (WebSocket, IndexedDB, fetch, FileReader, etc.).
        /// </summary>
        /// <param name="source">JS TypedArray containing the source data. Not disposed by this method.</param>
        /// <param name="targetByteOffset">Byte offset into the GPU buffer to write at.</param>
        void CopyFromJS(TypedArray source, long targetByteOffset = 0);

        /// <summary>
        /// Copies data from a JS ArrayBuffer directly into the GPU buffer without crossing into .NET managed memory.
        /// This is the zero-copy path for browser backends - data stays in JS/GPU land.
        /// Use this when data originates from JS (WebSocket, IndexedDB, fetch, FileReader, etc.).
        /// </summary>
        /// <param name="source">JS ArrayBuffer containing the source data. Not disposed by this method.</param>
        /// <param name="targetByteOffset">Byte offset into the GPU buffer to write at.</param>
        void CopyFromJS(ArrayBuffer source, long targetByteOffset = 0);
    }
}
