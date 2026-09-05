using System;
// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGPU
//                 WebGPU Compute Library for Blazor WebAssembly
//
// File: WebGPUBuffer.cs
// ---------------------------------------------------------------------------------------

using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.ILGPU.WebGPU.Backend;
using System.Runtime.InteropServices;

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// Represents a typed GPU memory buffer in WebGPU.
    /// </summary>
    public sealed class WebGPUBuffer<T> : IDisposable where T : unmanaged
    {
        #region Instance

        private static readonly GPUCommandBuffer[] _submitArray = new GPUCommandBuffer[1];

        /// <summary>Serial for buffer labels. Per closed generic, which is why the label carries T's name.</summary>
        private static int _labelSerial;

        /// <summary>This buffer's WebGPU label, so a destroy trace can name it. See WebGPUBackend.TraceBufferDestroy.</summary>
        private readonly string? _label;

        private GPUBuffer? _buffer;
        private bool _disposed;
        private readonly bool _ownsBuffer;

        /// <summary>
        /// Constructs a new WebGPU buffer.
        /// </summary>
        internal WebGPUBuffer(WebGPUNativeAccelerator accelerator, long length)
        {
            Accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            Length = length;
            ElementSize = Marshal.SizeOf<T>();
            LengthInBytes = length * ElementSize;
            _ownsBuffer = true;

            var device = accelerator.NativeDevice;
            if (device == null)
                throw new InvalidOperationException("GPU device not initialized");

            // Create GPU buffer (WebGPU requires size multiple of 4).
            // ⚠️ And a MINIMUM of 4 bytes, even for a zero-length allocation. An EMPTY tensor is legal -
            // ONNX uses one to say "no padding here", and a Slice can legitimately select nothing - but
            // WebGPU refuses to bind a zero-sized storage buffer ("Binding size for [Buffer] is zero",
            // against minBindingSize: 4) and the whole CommandBuffer becomes invalid. A 4-byte floor costs
            // nothing, keeps the buffer bindable, and changes no semantics: the VIEW still has length 0, so
            // no kernel reads or writes an element of it.
            var gpuSize = Math.Max(4L, WebGPUAlignment.AlignTo4(LengthInBytes));
            // ⚠️ LABEL EVERY BUFFER. Dawn reports a use-after-destroy as
            // "[Buffer (unlabeled)] used in submit while destroyed", which names neither the buffer nor
            // the kind of buffer, and the error arrives asynchronously at submit - long after the call
            // that destroyed it. A label is the ONLY identity that survives into that message. Pooled
            // scalar and coalesced param buffers were already labelled; the main storage allocation and
            // the readback staging buffer were not, which is exactly why the 2026-09-04 hunt for
            // "[Buffer (unlabeled)]" could not say which of them it was.
            _label = $"Storage#{System.Threading.Interlocked.Increment(ref _labelSerial)}:{gpuSize}B";
            var descriptor = new GPUBufferDescriptor
            {
                Label = _label,
                Size = (ulong)gpuSize,
                Usage = GPUBufferUsage.Storage | GPUBufferUsage.CopySrc | GPUBufferUsage.CopyDst,
                MappedAtCreation = false
            };

            _buffer = device.CreateBuffer(descriptor);
        }

        /// <summary>
        /// Constructs a non-owning wrapper around an externally-managed GPUBuffer.
        /// The buffer will NOT be destroyed when this instance is disposed.
        /// Both the external buffer and the accelerator must share the same GPUDevice.
        /// </summary>
        internal WebGPUBuffer(WebGPUNativeAccelerator accelerator, GPUBuffer externalBuffer, long length)
        {
            Accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
            Length = length;
            ElementSize = Marshal.SizeOf<T>();
            LengthInBytes = length * ElementSize;
            _buffer = externalBuffer;
            _ownsBuffer = false;
        }


        #endregion

        #region Properties

        /// <summary>
        /// Returns the parent accelerator.
        /// </summary>
        public WebGPUNativeAccelerator Accelerator { get; }

        /// <summary>
        /// Returns the native GPU buffer.
        /// </summary>
        public GPUBuffer? NativeBuffer => _buffer;

        /// <summary>
        /// This buffer's WebGPU label, or null for a non-owning wrapper. Survives disposal, so a
        /// use-after-dispose can still name the buffer it is talking about.
        /// </summary>
        public string? Label => _label;

        /// <summary>True once <see cref="Dispose"/> has run and the native buffer is gone.</summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// The stack that disposed this buffer, captured only when its label matched
        /// <see cref="WebGPUBackend.TraceBufferDestroy"/> at disposal time. Null otherwise.
        /// </summary>
        /// <remarks>
        /// RECORDED ON THE BUFFER rather than printed, so whoever DISCOVERS the use-after-dispose can
        /// report whoever CAUSED it, in the same message. A printed trace has to be correlated by eye
        /// against every other destroy in the run; carried on the object, the two halves of the bug
        /// arrive together.
        /// </remarks>
        public string? DestroyStack { get; private set; }

        /// <summary>
        /// Returns the number of elements.
        /// </summary>
        public long Length { get; }

        /// <summary>
        /// Returns the element size in bytes.
        /// </summary>
        public int ElementSize { get; }

        /// <summary>
        /// Returns the total size in bytes.
        /// </summary>
        public long LengthInBytes { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Copies data from a host array to the GPU buffer.
        /// Data crosses the .NET/JS boundary. For browser backends, prefer
        /// <see cref="CopyFromJS(TypedArray, long)"/> when data is already in JS.
        /// </summary>
        public void CopyFromHost(T[] sourceArray, long targetOffset = 0)
        {
            if (_buffer == null)
                throw new ObjectDisposedException(nameof(WebGPUBuffer<T>));
            if (sourceArray.Length > Length - targetOffset)
                throw new ArgumentException("Source array is too large for the buffer");

            var queue = Accelerator.Queue;
            if (queue == null)
                throw new InvalidOperationException("GPU queue not available");

            // WebGPU writeBuffer requires the number of bytes to write to be a multiple of 4
            var copyBytes = sourceArray.Length * ElementSize;
            var paddedBytes = WebGPUAlignment.AlignTo4(copyBytes);
            using var uint8Array = new Uint8Array((int)paddedBytes);
            uint8Array.Write(sourceArray);
            queue.WriteBuffer(_buffer, (long)(targetOffset * ElementSize), uint8Array);
            // A host write cannot be replayed from a dispatch plan - see WebGPUDispatchPlan.HostWriteCount.
            WebGPUDispatchPlan.Recording?.NoteHostWrite(paddedBytes);
        }

        /// <summary>
        /// Copies data from a JS TypedArray directly to the GPU buffer without crossing into .NET.
        /// This is the zero-copy path for browser backends - data stays in JS/GPU land.
        /// Use this when data originates from JS (WebSocket, IndexedDB, fetch, etc.).
        /// The TypedArray is NOT disposed by this method - caller manages its lifetime.
        /// </summary>
        /// <param name="source">JS TypedArray (Uint8Array, Float32Array, Int32Array, etc.) containing the data.</param>
        /// <param name="targetByteOffset">Byte offset into the GPU buffer to write at.</param>
        public void CopyFromJS(TypedArray source, long targetByteOffset = 0)
        {
            if (_buffer == null)
                throw new ObjectDisposedException(nameof(WebGPUBuffer<T>));

            var queue = Accelerator.Queue;
            if (queue == null)
                throw new InvalidOperationException("GPU queue not available");

            queue.WriteBuffer(_buffer, targetByteOffset, source);
            WebGPUDispatchPlan.Recording?.NoteHostWrite(source.ByteLength);
        }

        /// <summary>
        /// Copies data from a JS ArrayBuffer directly to the GPU buffer without crossing into .NET.
        /// This is the zero-copy path for browser backends - data stays in JS/GPU land.
        /// Use this when data originates from JS (WebSocket, IndexedDB, fetch, etc.).
        /// The ArrayBuffer is NOT disposed by this method - caller manages its lifetime.
        /// </summary>
        /// <param name="source">JS ArrayBuffer containing the data.</param>
        /// <param name="targetByteOffset">Byte offset into the GPU buffer to write at.</param>
        public void CopyFromJS(ArrayBuffer source, long targetByteOffset = 0)
        {
            if (_buffer == null)
                throw new ObjectDisposedException(nameof(WebGPUBuffer<T>));

            var queue = Accelerator.Queue;
            if (queue == null)
                throw new InvalidOperationException("GPU queue not available");

            queue.WriteBuffer(_buffer, targetByteOffset, source);
            WebGPUDispatchPlan.Recording?.NoteHostWrite(source.ByteLength);
        }

        /// <summary>
        /// Awaits the GPU→CPU readback map (<c>staging.MapAsync(Read)</c>), timing the wait into
        /// <see cref="WebGPUBackend.ProfileReadbackWaitMs"/> when <see cref="WebGPUBackend.EnableDispatchProfiling"/>
        /// is on. This is the readback GPU-wait surface — distinct from <c>SynchronizeAsync</c>'s
        /// <c>OnSubmittedWorkDone</c> drain — so a profiled step accounts for ALL GPU-wait, not just the sync
        /// drain (a decode whose wait hides here reads ~0 in <see cref="WebGPUBackend.ProfileSyncWaitMs"/>).
        /// </summary>
        private static async Task ProfiledMapReadAsync(GPUBuffer stagingBuffer)
        {
            if (WebGPUBackend.EnableDispatchProfiling)
            {
                var profSw = System.Diagnostics.Stopwatch.StartNew();
                await stagingBuffer.MapAsync(GPUMapMode.Read);
                WebGPUBackend.ProfileReadbackWaitMs += profSw.Elapsed.TotalMilliseconds;
                WebGPUBackend.ProfileReadbackWaitCount++;
            }
            else
            {
                await stagingBuffer.MapAsync(GPUMapMode.Read);
            }
        }

        /// <summary>
        /// Copies data from the GPU buffer to a host array asynchronously.
        /// Allocates and returns a new T[] array.
        /// For hot-path rendering loops, prefer the overload that accepts a destination array
        /// to avoid per-call allocations.
        /// </summary>
        public async Task<T[]> CopyToHostAsync(long sourceOffset = 0, long? length = null)
        {
            var copyLength = length ?? Length - sourceOffset;
            var result = new T[copyLength];
            await CopyToHostAsync(result, sourceOffset, copyLength);
            return result;
        }

        // Cached staging buffer for zero-allocation readback
        private GPUBuffer? _cachedStagingBuffer;
        private long _cachedStagingSize;

        /// <summary>
        /// Copies GPU data into a caller-provided array, reusing a cached staging buffer.
        /// This is the zero-allocation hot path — no GPU buffer or .NET array allocation per call.
        /// The staging buffer is created once and reused for subsequent calls of the same or smaller size.
        /// </summary>
        /// <param name="destination">Pre-allocated array to receive the data. Must have enough space for count elements.</param>
        /// <param name="sourceOffset">Offset in elements from the start of the GPU buffer.</param>
        /// <param name="count">Number of elements to copy. If null, copies as many as will fit in destination.</param>
        /// <returns>Number of elements actually copied.</returns>
        public async Task<long> CopyToHostAsync(T[] destination, long sourceOffset = 0, long? count = null)
        {
            if (_buffer == null)
                throw new ObjectDisposedException(nameof(WebGPUBuffer<T>));

            var copyLength = count ?? Math.Min(destination.Length, Length - sourceOffset);
            if (copyLength <= 0) return 0;

            var copyBytes = copyLength * ElementSize;
            var paddedBytes = WebGPUAlignment.AlignTo4(copyBytes);
            var sourceByteOffset = sourceOffset * ElementSize;

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] CopyToHostAsync: SourceOffset={sourceOffset}, Length={copyLength} elements");

            var device = Accelerator.NativeDevice;
            if (device == null)
                throw new InvalidOperationException("GPU device not initialized");

            // Ensure cached staging buffer is large enough (created once, reused)
            // WebGPU CopyBufferToBuffer requires copy size to be a multiple of 4
            if (_cachedStagingBuffer == null || _cachedStagingSize < paddedBytes)
            {
                _cachedStagingBuffer?.Destroy();
                _cachedStagingBuffer?.Dispose();

                var stagingDescriptor = new GPUBufferDescriptor
                {
                    Label = $"Staging#{System.Threading.Interlocked.Increment(ref _labelSerial)}:{paddedBytes}B",
                    Size = (ulong)paddedBytes,
                    Usage = GPUBufferUsage.CopyDst | GPUBufferUsage.MapRead,
                    MappedAtCreation = false
                };
                _cachedStagingBuffer = device.CreateBuffer(stagingDescriptor);
                _cachedStagingSize = paddedBytes;
            }

            // Flush pending ILGPU kernel dispatches before copying
            Accelerator.FlushPendingCommands?.Invoke();

            // Copy from GPU buffer to cached staging buffer (size must be multiple of 4)
            using var encoder = device.CreateCommandEncoder();
            encoder.CopyBufferToBuffer(_buffer, (ulong)sourceByteOffset, _cachedStagingBuffer, 0, (ulong)paddedBytes);
            using var commandBuffer = encoder.Finish();
            _submitArray[0] = commandBuffer;
            Accelerator.Queue?.Submit(_submitArray);

            // Map, read into caller's destination array, unmap
            await ProfiledMapReadAsync(_cachedStagingBuffer);
            // The mapped-range ArrayBuffer wrapper holds a JS slot and must be released, or every
            // readback leaks one (the sibling CopyToHostUint8ArrayAsync path already does this).
            using var mappedRange = _cachedStagingBuffer.GetMappedRange();
            if (mappedRange != null)
            {
                using var uint8Array = new Uint8Array(mappedRange);
                uint8Array.Read(0, destination, 0, copyLength);
            }
            _cachedStagingBuffer.Unmap();

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] CopyToHostAsync: Finished");

            return copyLength;
        }

        /// <summary>
        /// Copies GPU data into a caller-provided array, reusing a cached staging buffer.
        /// This is the zero-allocation hot path — no GPU buffer or .NET array allocation per call.
        /// The staging buffer is created once and reused for subsequent calls of the same or smaller size.
        /// </summary>
        /// <param name="sourceByteOffset">Offset in bytes from the start of the GPU buffer.</param>
        /// <param name="copyBytes">Number of bytes to copy. If null, copies as many as will fit in destination.</param>
        /// <returns>Number of elements actually copied.</returns>
        public async Task<Uint8Array> CopyToHostUint8ArrayAsync(long sourceByteOffset = 0, long? copyBytes = null)
        {
            if (_buffer == null)
                throw new ObjectDisposedException(nameof(Buffer));

            copyBytes ??= Length * ElementSize - sourceByteOffset;
            if (copyBytes <= 0) return new Uint8Array();

            var paddedBytes = WebGPUAlignment.AlignTo4(copyBytes.Value);

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] CopyToHostUint8ArrayAsync: SourceByteOffset={sourceByteOffset}, CopyBytes={copyBytes} elements");

            var device = Accelerator.NativeDevice;
            if (device == null)
                throw new InvalidOperationException("GPU device not initialized");

            // Ensure cached staging buffer is large enough (created once, reused)
            if (_cachedStagingBuffer == null || _cachedStagingSize < paddedBytes)
            {
                _cachedStagingBuffer?.Destroy();
                _cachedStagingBuffer?.Dispose();

                var stagingDescriptor = new GPUBufferDescriptor
                {
                    Label = $"Staging#{System.Threading.Interlocked.Increment(ref _labelSerial)}:{paddedBytes}B",
                    Size = (ulong)paddedBytes,
                    Usage = GPUBufferUsage.CopyDst | GPUBufferUsage.MapRead,
                    MappedAtCreation = false
                };
                _cachedStagingBuffer = device.CreateBuffer(stagingDescriptor);
                _cachedStagingSize = paddedBytes;
            }

            // Flush pending ILGPU kernel dispatches before copying
            Accelerator.FlushPendingCommands?.Invoke();

            // Copy from GPU buffer to cached staging buffer (size must be multiple of 4)
            using var encoder = device.CreateCommandEncoder();
            encoder.CopyBufferToBuffer(_buffer, (ulong)sourceByteOffset, _cachedStagingBuffer, 0, (ulong)paddedBytes);
            using var commandBuffer = encoder.Finish();
            _submitArray[0] = commandBuffer;
            Accelerator.Queue?.Submit(_submitArray);

            // Map, read into caller's destination array, unmap
            await ProfiledMapReadAsync(_cachedStagingBuffer);
            Uint8Array result = default!;
            try
            {
                using var mappedRange = _cachedStagingBuffer.GetMappedRange();
                if (mappedRange != null)
                {
                    // Must copy the data out of the mapped range before unmapping, as the mapped range becomes invalid after unmap
                    // Slice to actual requested size (paddedBytes may be larger for alignment)
                    // Slice() returns a NEW ArrayBuffer holding its own JS slot; the Uint8Array copies
                    // the reference, so the intermediate must be released or it leaks per readback.
                    using var sliced = mappedRange.Slice(0, (int)copyBytes.Value);
                    result = new Uint8Array(sliced);
                }
            }
            finally
            {
                _cachedStagingBuffer.Unmap();
            }

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] CopyToHostAsync: Finished");

            return result ?? new Uint8Array();
        }

        /// <summary>
        /// Copies GPU data into a caller-provided array of a different element type,
        /// reusing a cached staging buffer. The data is reinterpreted as TDest elements.
        /// This is used by extension methods where the native buffer is byte-typed
        /// but the caller wants to read as a different struct type (e.g., uint, float).
        /// </summary>
        /// <typeparam name="TDest">The destination element type.</typeparam>
        /// <param name="destination">Pre-allocated array to receive the data.</param>
        /// <param name="sourceOffset">Offset in TDest elements from the start of the GPU buffer.</param>
        /// <param name="count">Number of TDest elements to copy.</param>
        /// <param name="destElementSize">Size of TDest in bytes (Marshal.SizeOf&lt;TDest&gt;()).</param>
        /// <returns>Number of TDest elements actually copied.</returns>
        public async Task<long> CopyToHostAsync<TDest>(TDest[] destination, long sourceOffset, long count, int destElementSize) where TDest : struct
        {
            if (_buffer == null)
                throw new ObjectDisposedException(nameof(WebGPUBuffer<T>));

            if (count <= 0) return 0;

            var copyBytes = count * destElementSize;
            var paddedBytes = WebGPUAlignment.AlignTo4(copyBytes);
            var sourceByteOffset = sourceOffset * destElementSize;

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] CopyToHostAsync<{typeof(TDest).Name}>: SourceOffset={sourceOffset}, Length={count} elements, ByteSize={copyBytes}");

            var device = Accelerator.NativeDevice;
            if (device == null)
                throw new InvalidOperationException("GPU device not initialized");

            // Ensure cached staging buffer is large enough (created once, reused)
            if (_cachedStagingBuffer == null || _cachedStagingSize < paddedBytes)
            {
                _cachedStagingBuffer?.Destroy();
                _cachedStagingBuffer?.Dispose();

                var stagingDescriptor = new GPUBufferDescriptor
                {
                    Label = $"Staging#{System.Threading.Interlocked.Increment(ref _labelSerial)}:{paddedBytes}B",
                    Size = (ulong)paddedBytes,
                    Usage = GPUBufferUsage.CopyDst | GPUBufferUsage.MapRead,
                    MappedAtCreation = false
                };
                _cachedStagingBuffer = device.CreateBuffer(stagingDescriptor);
                _cachedStagingSize = paddedBytes;
            }

            // Flush pending ILGPU kernel dispatches before copying
            Accelerator.FlushPendingCommands?.Invoke();

            // Copy from GPU buffer to cached staging buffer (size must be multiple of 4)
            using var encoder = device.CreateCommandEncoder();
            encoder.CopyBufferToBuffer(_buffer, (ulong)sourceByteOffset, _cachedStagingBuffer, 0, (ulong)paddedBytes);
            using var commandBuffer = encoder.Finish();
            _submitArray[0] = commandBuffer;
            Accelerator.Queue?.Submit(_submitArray);

            // Map, read as TDest into destination array, unmap
            await ProfiledMapReadAsync(_cachedStagingBuffer);
            var mappedRange = _cachedStagingBuffer.GetMappedRange();
            if (mappedRange != null)
            {
                using var uint8Array = new Uint8Array(mappedRange);
                uint8Array.Read(0, destination, 0, count);
            }
            _cachedStagingBuffer.Unmap();

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] CopyToHostAsync<{typeof(TDest).Name}>: Finished");

            return count;
        }

        /// <summary>
        /// Fills the buffer with a value.
        /// </summary>
        public void Fill(T value)
        {
            var data = new T[Length];
            System.Array.Fill(data, value);
            CopyFromHost(data);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cachedStagingBuffer?.Destroy();
            _cachedStagingBuffer?.Dispose();
            _cachedStagingBuffer = null;

            // Only destroy the underlying GPUBuffer if we own it.
            // Non-owning instances (wrapping external buffers) must not destroy the buffer.
            if (_ownsBuffer)
            {
                // DIAGNOSTIC: WHO destroyed it. Dawn reports a use-after-destroy asynchronously, at the
                // next submit, so the throwing stack belongs to an innocent caller - the destroy site is
                // the fact worth having, and nothing else records it. Set
                // WebGPUBackend.TraceBufferDestroy to a label substring (or "*") to print it.
                var trace = WebGPUBackend.TraceBufferDestroy;
                if (!string.IsNullOrEmpty(trace))
                {
                    var lbl = _label ?? "(unlabeled)";
                    if (trace == "*" || lbl.Contains(trace, StringComparison.Ordinal))
                        DestroyStack = Environment.StackTrace;   // recorded, not printed - see DestroyStack
                }
                _buffer?.Destroy();
                _buffer?.Dispose();
            }
            _buffer = null;
        }

        #endregion
    }
}
