using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.BlazorJS.Toolbox;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SpawnDev.ILGPU
{
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
