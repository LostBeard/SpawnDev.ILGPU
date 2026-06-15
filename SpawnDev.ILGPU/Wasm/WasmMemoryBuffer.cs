// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmMemoryBuffer.cs
//
// Manages GPU memory buffers backed by SharedArrayBuffer regions.
// Each buffer is a slice of a SharedArrayBuffer for zero-copy sharing across workers.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.BlazorJS.Toolbox;

namespace SpawnDev.ILGPU.Wasm
{
    /// <summary>
    /// Wasm memory buffer backed by a SharedArrayBuffer for zero-copy sharing across workers.
    /// </summary>
    public class WasmMemoryBuffer : MemoryBuffer, IBrowserMemoryBuffer
    {
        /// <summary>
        /// The SharedArrayBuffer backing this buffer.
        /// </summary>
        public SharedArrayBuffer SharedBuffer { get; private set; }

        /// <summary>
        /// The typed array view for this buffer (e.g., Int32Array, Float32Array).
        /// </summary>
        public TypedArray TypedArrayView { get; private set; }

        /// <summary>
        /// Byte offset within the SharedArrayBuffer where this buffer starts.
        /// When the buffer owns its own SharedArrayBuffer, this is 0.
        /// </summary>
        public int ByteOffset { get; set; } = 0;

        /// <summary>
        /// Monotonic counter bumped on every host-side write to <see cref="SharedBuffer"/>
        /// (CopyFromHost / CopyFromJS / CopyFrom override). The dispatch's copy-OUT
        /// phase compares the buffer's current <see cref="HostWriteCounter"/> to the
        /// snapshot it took at copy-IN time; if they differ, the host has overwritten
        /// SharedBuffer during the in-flight dispatch and copy-OUT skips that buffer
        /// to preserve the host's write. Closes the 2026-05-03 Wasm copy-OUT race
        /// (per `_DevComms/SpawnDev.ILGPU/geordi-to-team-wasm-copy-out-race-2026-05-03.md`).
        /// </summary>
        public int HostWriteCounter { get; private set; }

        /// <summary>
        /// Snapshot-capture hook. MUST be called BEFORE any host-side mutation of
        /// <see cref="SharedBuffer"/> (i.e. before <c>TypedArrayView.Write</c> /
        /// <c>Set</c> / <c>WriteBytes</c>). If at least one in-flight dispatch
        /// has a pending intent on this buffer, this materializes the lazy
        /// snapshot from the CURRENT (pre-write) SharedBuffer contents so that
        /// dispatches whose queue-time <see cref="HostWriteCounter"/> matches the
        /// current value can still see their pre-write data when they run.
        ///
        /// For unchanged buffers (e.g. ML weights uploaded once with no follow-on
        /// host write) this is a no-op: <c>_pendingSnapshotIntents</c> may be
        /// non-zero but no second write triggers materialization. Multi-pass
        /// kernels with no host writes between dispatches similarly never
        /// materialize a snapshot — zero allocation overhead in the common case.
        /// (rc.16 RadixSort multi-pass regression fix + StyleMosaic ML reuse
        /// perf, 2026-05-05.)
        /// </summary>
        internal void PrepareHostWrite()
        {
            if (_pendingSnapshotIntents <= 0)
                return;

            // Snapshot the CURRENT SharedBuffer (pre-write contents) under a
            // tier keyed by the current HostWriteCounter. Every dispatch
            // currently pending whose queue-time HWC equals this value will
            // resolve to this snapshot at copy-IN time. If a tier already
            // exists at this HWC (multiple host writes happen back-to-back
            // with no intervening dispatch queue), keep the FIRST tier — its
            // snapshot still represents the pre-FIRST-write data the pending
            // intents wanted; the additional writes don't change what those
            // intents need.
            int hwcKey = HostWriteCounter;
            _snapshotsByHWC ??= new Dictionary<int, SharedArrayBuffer>();
            _snapshotRefCounts ??= new Dictionary<int, int>();
            if (_snapshotsByHWC.ContainsKey(hwcKey))
                return;
            var fresh = new SharedArrayBuffer((int)LengthInBytes);
            using var dst = new Uint8Array(fresh);
            using var src = new Uint8Array(SharedBuffer);
            dst.JSRef!.CallVoid("set", src);
            _snapshotsByHWC[hwcKey] = fresh;
            _snapshotRefCounts[hwcKey] = _pendingSnapshotIntents; // every pending intent shares this tier
            s_liveSnapshotBytes += LengthInBytes; // resident-SAB diagnostic (see LiveSnapshotBytes)
        }

        /// <summary>
        /// Bumps <see cref="HostWriteCounter"/>. MUST be called AFTER the host
        /// finishes writing <see cref="SharedBuffer"/>. Pair with
        /// <see cref="PrepareHostWrite"/> at the start of every host-write path.
        /// </summary>
        internal void NotifyHostWrite() => HostWriteCounter++;

        // ── Lazy queued-dispatch snapshot — host-write race defense + perf ──
        //
        // The original race that motivates this mechanism:
        //   1. CopyFromCPU writes data1 to SharedBuffer.
        //   2. RunKernel queues dispatch D1 (returns immediately, async task in flight).
        //   3. CopyFromCPU writes data2 to SharedBuffer.
        //   4. RunKernel queues dispatch D2.
        //   5. D1 resumes after awaiting prior tasks. Its copy-IN reads SharedBuffer
        //      = data2 (already overwritten by step 3). D1 runs with WRONG data.
        //
        // The previous implementation eagerly snapshotted SharedBuffer at every queue
        // time, which closed the race but allocated MB-sized SABs for every dispatch
        // even when no race ever fires. Multi-pass kernels (RadixSort) that sort
        // in-place hit a separate failure mode: the cached snapshot pinned pre-pass-1
        // data; pass-2 read it via the cache and silently re-sorted the original input
        // (TJ regression report 2026-05-05). And ML pipelines with reused weight
        // buffers paid 5GB+ of wasted allocations across 100+ dispatches.
        //
        // The lazy approach below fixes both:
        //   - Each dispatch registers a queue-time HostWriteCounter intent.
        //   - The snapshot is allocated ONLY on the first NotifyHostWrite that fires
        //     while at least one intent is pending — i.e. only when the race actually
        //     occurs. For multi-pass GPU kernels with no host writes, no allocation.
        //     For ML weight reuse, no allocation.
        //   - At dispatch start, copy-IN uses the snapshot iff HostWriteCounter has
        //     advanced past the dispatch's queue-time value (host wrote during our
        //     wait). Otherwise it reads SharedBuffer directly — which by then carries
        //     all prior dispatches' copy-OUT data, exactly what multi-pass needs.
        //   - The pinned snapshot is released when the LAST pending intent completes
        //     (no in-flight dispatch can still need it).

        /// <summary>
        /// Active dispatch-snapshot intent count. Incremented at RunKernel queue
        /// time, decremented when the dispatch's task completes (success or fault).
        /// Read by <see cref="PrepareHostWrite"/> to know whether to materialize a
        /// snapshot before mutating SharedBuffer.
        /// </summary>
        private int _pendingSnapshotIntents;

        /// <summary>
        /// Per-HWC pinned snapshots. Key = HostWriteCounter value AT WHICH the
        /// snapshot was captured (i.e. AT the moment of the host write that
        /// triggered the materialization, equivalent to the queue-time HWC of all
        /// dispatches that registered their intent before that host write).
        /// Each snapshot is reference-shared across every pending dispatch whose
        /// queue-time HWC equals the key. Released when the last referencing
        /// intent completes (tracked separately via <see cref="_snapshotRefCounts"/>).
        ///
        /// Null until the first host write fires while intents are pending (so the
        /// common case — ML weight reuse with no follow-on host writes, or
        /// multi-pass GPU kernels with no host writes between dispatches — never
        /// allocates this dictionary either).
        /// </summary>
        private Dictionary<int, SharedArrayBuffer>? _snapshotsByHWC;

        /// <summary>
        /// Reference counts per snapshot tier. Aligned with <see cref="_snapshotsByHWC"/>.
        /// When a tier's count drops to zero, both entries are removed and the
        /// SAB becomes eligible for JS GC.
        /// </summary>
        private Dictionary<int, int>? _snapshotRefCounts;

        /// <summary>
        /// Registers a dispatch-snapshot intent for this buffer. Returns the
        /// queue-time HostWriteCounter — pass it back to
        /// <see cref="GetSnapshotForDispatch"/> at copy-IN time. Always pair with
        /// a corresponding <see cref="CompleteDispatchIntent"/> in finally.
        ///
        /// If a snapshot already exists at the current HostWriteCounter (because
        /// a previous host-write tier captured it), this dispatch joins that
        /// tier — no new allocation. Otherwise, no snapshot is created here; the
        /// next host write (if any) materializes one for all currently-pending
        /// intents at the current HWC.
        /// </summary>
        internal int RegisterDispatchIntent()
        {
            _pendingSnapshotIntents++;
            int qhwc = HostWriteCounter;
            // Bump ref count if there's already a snapshot at our HWC tier (we
            // share it with any earlier intents queued in the same window).
            if (_snapshotRefCounts != null && _snapshotRefCounts.TryGetValue(qhwc, out var rc))
                _snapshotRefCounts[qhwc] = rc + 1;
            return qhwc;
        }

        /// <summary>
        /// Releases a dispatch-snapshot intent. When the last referencer of any
        /// snapshot tier completes, that tier's SAB is freed.
        /// </summary>
        internal void CompleteDispatchIntent(int queueTimeHostWriteCounter)
        {
            if (_pendingSnapshotIntents > 0) _pendingSnapshotIntents--;
            if (_snapshotRefCounts != null
                && _snapshotRefCounts.TryGetValue(queueTimeHostWriteCounter, out var rc))
            {
                rc--;
                if (rc <= 0)
                {
                    _snapshotRefCounts.Remove(queueTimeHostWriteCounter);
                    // BUGFIX 2026-06-14: actually free the snapshot SAB. The previous code Remove()'d
                    // the dict entry but never Disposed the SharedArrayBuffer (despite this method's
                    // doc claiming "that tier's SAB is freed"), so every full-buffer-size host-write
                    // snapshot leaked in the JS heap — the ~1.5 GiB ML-lane late-test leak (Tuvok trace
                    // 2026-06-14; invisible to LiveBufferBytes because it's a separate SAB path).
                    if (_snapshotsByHWC != null
                        && _snapshotsByHWC.TryGetValue(queueTimeHostWriteCounter, out var doneSnap))
                    {
                        _snapshotsByHWC.Remove(queueTimeHostWriteCounter);
                        doneSnap.Dispose();
                        s_liveSnapshotBytes -= LengthInBytes;
                    }
                }
                else
                {
                    _snapshotRefCounts[queueTimeHostWriteCounter] = rc;
                }
            }
            if (_pendingSnapshotIntents == 0)
            {
                // Dispose any snapshot SABs still resident (tiers not released by refcount) before
                // dropping the dicts — otherwise their JS SharedArrayBuffers leak (same root bug).
                DisposeAllSnapshots();
                _snapshotsByHWC = null;
                _snapshotRefCounts = null;
            }
        }

        /// <summary>Disposes every snapshot SAB still in <see cref="_snapshotsByHWC"/> and clears the
        /// resident-bytes accounting. Idempotent. Called on the last-intent-completes path and on Dispose.</summary>
        private void DisposeAllSnapshots()
        {
            if (_snapshotsByHWC == null) return;
            foreach (var kv in _snapshotsByHWC)
            {
                kv.Value.Dispose();
                s_liveSnapshotBytes -= LengthInBytes;
            }
            _snapshotsByHWC.Clear();
        }

        /// <summary>
        /// Returns the snapshot tier matching the dispatch's queue-time HWC, or
        /// null if no host write has clobbered SharedBuffer since the dispatch
        /// was queued — in which case caller reads SharedBuffer directly.
        /// </summary>
        internal SharedArrayBuffer? GetSnapshotForDispatch(int queueTimeHostWriteCounter)
        {
            // No host write has clobbered our queue-time data — SharedBuffer is
            // exactly what we want.
            if (HostWriteCounter == queueTimeHostWriteCounter)
                return null;

            // A host write happened. We need the snapshot from our queue-time
            // HWC tier — captured by PrepareHostWrite right before the host
            // overwrote SharedBuffer.
            if (_snapshotsByHWC != null
                && _snapshotsByHWC.TryGetValue(queueTimeHostWriteCounter, out var snap))
            {
                return snap;
            }
            return null;
        }

        /// <summary>
        /// Creates a new Wasm memory buffer.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        /// <param name="length">The number of elements to allocate.</param>
        /// <param name="elementSize">The size of each element in bytes.</param>
        public WasmMemoryBuffer(
            Accelerator accelerator,
            long length,
            int elementSize)
            : base(accelerator, length, elementSize)
        {
            // Compute total bytes: length (elements) × elementSize (bytes per element).
            long totalBytesLong = length * elementSize;
            if (totalBytesLong > int.MaxValue || totalBytesLong < 0)
                throw new ArgumentOutOfRangeException(nameof(length),
                    $"Buffer size {totalBytesLong} bytes exceeds maximum SharedArrayBuffer capacity (2GB)");
            int totalBytes = (int)totalBytesLong;
            SharedBuffer = new SharedArrayBuffer(totalBytes);

            // Create a Uint8Array view for raw data access
            TypedArrayView = new Uint8Array(SharedBuffer);

            // Resident-count diagnostic (2026-06-14): RESIDENT live-buffer count + bytes (inc here,
            // dec on dispose — NOT cumulative), to attribute the ML Wasm late-lane JS-heap leak
            // (Tuvok trace: heap 154→1644 MiB, ~2.9 MiB/test). If LiveBufferBytes does NOT return to
            // ~baseline after each test's accelerator.Dispose, undisposed SharedArrayBuffer-backed
            // buffers are the leak (accelerator.Dispose not freeing children, or callers not disposing).
            _liveBytes = totalBytes;
            _liveCounted = true;
            s_liveBufferCount++;
            s_liveBufferBytes += totalBytes;

            // NativePtr = 0: Wasm buffers don't use native pointers.
            // ArrayView.LoadEffectiveAddressAsPtr() returns NativePtr + Index * ElementSize.
            // With NativePtr=0, SubView offsets are purely Index-based (correct for Wasm).
            // The multi-pass scan (which needs non-zero NativePtr) is not used for Wasm —
            // Wasm routes to single-group scan via AcceleratorType.Wasm in ScanExtensions.
        }

        /// <summary>
        /// Copies data from the host to this buffer.
        /// Data crosses the .NET/JS boundary. For browser backends, prefer
        /// <see cref="CopyFromJS(TypedArray, long)"/> when data is already in JS.
        /// </summary>
        public void CopyFromHost<T>(T[] data) where T : unmanaged
        {
            PrepareHostWrite();
            TypedArrayView.Write(data);
            NotifyHostWrite();
        }

        /// <inheritdoc/>
        public void CopyFromJS(TypedArray source, long targetByteOffset = 0)
        {
            if (TypedArrayView == null)
                throw new ObjectDisposedException(nameof(WasmMemoryBuffer));
            PrepareHostWrite();
            // Use the typed Set(TypedArray, long) overload - zero .NET copy, JS-to-JS
            using var srcBytes = new Uint8Array(source.Buffer, (int)source.ByteOffset, (int)source.ByteLength);
            TypedArrayView.Set(srcBytes, targetByteOffset);
            NotifyHostWrite();
        }

        /// <inheritdoc/>
        public void CopyFromJS(ArrayBuffer source, long targetByteOffset = 0)
        {
            if (TypedArrayView == null)
                throw new ObjectDisposedException(nameof(WasmMemoryBuffer));
            PrepareHostWrite();
            using var srcBytes = new Uint8Array(source);
            TypedArrayView.Set(srcBytes, targetByteOffset);
            NotifyHostWrite();
        }

        /// <summary>
        /// Copies data from this buffer to the host.
        /// </summary>
        public T[] CopyToHost<T>(long length) where T : unmanaged
        {
            GuardHostBufferRace(nameof(CopyToHost));
            return TypedArrayView.Read<T>(0, length);
        }

        /// <summary>
        /// Copies data from this buffer to host asynchronously.
        /// Awaits all pending kernel dispatches before reading, matching
        /// desktop backends' implicit synchronization on readback.
        /// </summary>
        public async Task<T[]> CopyToHostAsync<T>(long length) where T : unmanaged
        {
            // Implicit sync before readback - match CUDA/OpenCL behavior where
            // CopyToCPU calls stream.Synchronize() before reading data
            if (Accelerator is WasmAccelerator wasmAccel)
                await wasmAccel.SynchronizeAsync();
            return CopyToHost<T>(length);
        }

        /// <summary>
        /// Real async GPU-&gt;CPU readback. Drains in-flight worker kernels first
        /// (the core synchronous <see cref="CopyTo"/> reads the SharedArrayBuffer
        /// immediately and would observe a partially-written buffer while workers are
        /// still running). Overrides <see cref="MemoryBuffer.CopyToRawAsync"/> so
        /// <c>ArrayView&lt;T&gt;.CopyToCPUAsync</c> and algorithm code are correct on Wasm.
        /// </summary>
        protected override async Task<byte[]> CopyToRawAsync(
            AcceleratorStream stream,
            long sourceOffsetInBytes,
            long lengthInBytes)
        {
            if (lengthInBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(lengthInBytes));
            if (Accelerator is WasmAccelerator wasmAccel)
                await wasmAccel.SynchronizeAsync();
            if (lengthInBytes == 0)
                return System.Array.Empty<byte>();
            using var u8 = new Uint8Array(
                SharedBuffer, (int)sourceOffsetInBytes, (int)lengthInBytes);
            return u8.ReadBytes();
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Wasm <see cref="CopyFromJS(TypedArray, long)"/> is a JS-to-JS SharedArrayBuffer write
        /// (no 4-byte-alignment rule), so an <see cref="IJSReadStream"/> source uploads zero-copy -
        /// the bytes never enter the .NET/WASM managed heap. A plain .NET <see cref="System.IO.Stream"/>
        /// falls back to the managed base implementation.
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

        public async Task<Uint8Array> CopyToHostUint8ArrayAsync(long sourceByteOffset = 0, long? copyBytes = null)
        {
            if (SharedBuffer == null) return new Uint8Array();
            // Implicit sync before readback - match desktop behavior
            if (Accelerator is WasmAccelerator wasmAccel)
                await wasmAccel.SynchronizeAsync();
            long bufferSize = SharedBuffer.ByteLength;
            long actualCopyBytes = copyBytes ?? (bufferSize - sourceByteOffset);
            if (sourceByteOffset < 0 || sourceByteOffset > bufferSize)
                throw new ArgumentOutOfRangeException(nameof(sourceByteOffset),
                    $"Source offset {sourceByteOffset} is outside buffer bounds [0, {bufferSize})");
            if (actualCopyBytes < 0 || sourceByteOffset + actualCopyBytes > bufferSize)
                throw new ArgumentOutOfRangeException(nameof(copyBytes),
                    $"Copy range [{sourceByteOffset}, {sourceByteOffset + actualCopyBytes}) exceeds buffer size {bufferSize}");
            return copyBytes == null ?
                new Uint8Array(SharedBuffer, sourceByteOffset) :
                new Uint8Array(SharedBuffer, sourceByteOffset, copyBytes.Value);
        }

        /// <summary>
        /// Diagnostic flag (default false). When true, the synchronous host buffer ops
        /// (<see cref="MemSet"/> / <see cref="CopyTo"/> / <see cref="CopyToHost{T}"/>)
        /// throw if a dispatch touching this buffer is still in flight
        /// (<see cref="_pendingSnapshotIntents"/> &gt; 0) - i.e. the host is reading or
        /// zeroing a <see cref="SharedArrayBuffer"/> that worker kernels may still be
        /// writing. Enable in tests / diagnostics to ENUMERATE the sync-readback races
        /// that the async APIs (<c>CopyToHostAsync</c> / <c>CopyFromAsync</c> /
        /// <c>MemSetToZeroAsync</c> / <c>SynchronizeAsync</c>) are meant to replace; the
        /// intent counter is incremented synchronously at dispatch queue time and drops
        /// to zero only after a real drain, so a properly-awaited path never trips it.
        /// <c>CopyFrom*</c> are intentionally NOT guarded - they are protected by the
        /// lazy snapshot mechanism (<see cref="PrepareHostWrite"/>) by design.
        /// </summary>
        public static bool DetectHostBufferRaces;

        private void GuardHostBufferRace(string op)
        {
            if (DetectHostBufferRaces && _pendingSnapshotIntents > 0)
            {
                throw new InvalidOperationException(
                    $"WasmMemoryBuffer.{op} on a buffer with {_pendingSnapshotIntents} " +
                    "in-flight dispatch(es): the host is touching a SharedArrayBuffer that " +
                    "worker kernels may still be writing. Await accelerator.SynchronizeAsync() " +
                    "(or use CopyToHostAsync / CopyFromAsync / MemSetToZeroAsync) before this " +
                    "synchronous operation.");
            }
        }

        /// <inheritdoc/>
        protected override void MemSet(
            AcceleratorStream stream,
            byte value,
            in ArrayView<byte> targetView)
        {
            GuardHostBufferRace(nameof(MemSet));
            int offset = (int)targetView.LoadEffectiveAddressAsPtr();
            int length = (int)targetView.LengthInBytes;
            using var view = new Uint8Array(SharedBuffer, offset, length);
            view.JSRef!.CallVoid("fill", (int)value);
        }

        /// <inheritdoc/>
        protected override void CopyTo(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView)
        {
            GuardHostBufferRace(nameof(CopyTo));
            int srcOffset = (int)sourceView.LoadEffectiveAddressAsPtr();
            int length = (int)sourceView.LengthInBytes;

            using var srcUint8 = new Uint8Array(SharedBuffer, srcOffset, length);
            byte[] data = srcUint8.ReadBytes();

            unsafe
            {
                var targetPtr = targetView.LoadEffectiveAddressAsPtr();
                System.Runtime.InteropServices.Marshal.Copy(data, 0, targetPtr, length);
            }
        }

        /// <inheritdoc/>
        protected override void CopyFrom(
            AcceleratorStream stream,
            in ArrayView<byte> sourceView,
            in ArrayView<byte> targetView)
        {
            int length = (int)sourceView.LengthInBytes;

            byte[] data = new byte[length];
            unsafe
            {
                var sourcePtr = sourceView.LoadEffectiveAddressAsPtr();
                System.Runtime.InteropServices.Marshal.Copy(sourcePtr, data, 0, length);
            }

            // Write to SharedArrayBuffer
            PrepareHostWrite();
            int dstOffset = (int)targetView.LoadEffectiveAddressAsPtr();
            using var dstUint8 = new Uint8Array(SharedBuffer, dstOffset, length);
            dstUint8.WriteBytes(data);
            NotifyHostWrite();
        }

        /// <summary>
        /// Copies data from the source buffer to this buffer.
        /// Handles Wasm-to-Wasm copies via SharedArrayBuffer directly,
        /// bypassing Marshal.Copy which requires native pointers.
        /// </summary>
        protected override void CopyFromBuffer(
            AcceleratorStream stream,
            MemoryBuffer sourceBuffer,
            long sourceOffsetInBytes,
            long targetOffsetInBytes,
            long lengthInBytes)
        {
            if (sourceBuffer is WasmMemoryBuffer wasmSource)
            {
                // Bounds validation
                if (sourceOffsetInBytes + lengthInBytes > wasmSource.SharedBuffer.ByteLength)
                    throw new ArgumentOutOfRangeException(nameof(sourceOffsetInBytes),
                        $"Source copy range [{sourceOffsetInBytes}, {sourceOffsetInBytes + lengthInBytes}) exceeds source buffer size {wasmSource.SharedBuffer.ByteLength}");
                if (targetOffsetInBytes + lengthInBytes > SharedBuffer.ByteLength)
                    throw new ArgumentOutOfRangeException(nameof(targetOffsetInBytes),
                        $"Target copy range [{targetOffsetInBytes}, {targetOffsetInBytes + lengthInBytes}) exceeds target buffer size {SharedBuffer.ByteLength}");

                // Wasm-to-Wasm: copy between SharedArrayBuffers via JS TypedArray
                using var srcView = new Uint8Array(
                    wasmSource.SharedBuffer,
                    (int)sourceOffsetInBytes,
                    (int)lengthInBytes);
                using var dstView = new Uint8Array(
                    SharedBuffer,
                    (int)targetOffsetInBytes,
                    (int)lengthInBytes);
                dstView.JSRef!.CallVoid("set", srcView);
                return;
            }
            // Non-Wasm source: fall back to default (via native pointer)
            base.CopyFromBuffer(
                stream, sourceBuffer,
                sourceOffsetInBytes, targetOffsetInBytes, lengthInBytes);
        }

        // ── Resident-count diagnostics (2026-06-14) ──
        // Live (currently-undisposed) WasmMemoryBuffer count + total resident bytes. RESIDENT, not
        // cumulative — they go back down on dispose, so a climbing curve across a test lane means a
        // genuine leak (see the ctor note). Lesson from the prior mis-diagnosis: a monotonic counter
        // (TotalKernelsCompiled) is NOT a memory proxy; these are the real resident measure.
        private static int s_liveBufferCount;
        private static long s_liveBufferBytes;
        private static long s_liveSnapshotBytes;
        private readonly int _liveBytes;
        private bool _liveCounted;
        /// <summary>Number of WasmMemoryBuffers currently alive (constructed, not yet disposed).</summary>
        public static int LiveBufferCount => s_liveBufferCount;
        /// <summary>Total resident bytes across all live WasmMemoryBuffers' primary SharedArrayBuffers.</summary>
        public static long LiveBufferBytes => s_liveBufferBytes;
        /// <summary>Total resident bytes of host-write SNAPSHOT SharedArrayBuffers (the SEPARATE SAB path —
        /// full-buffer-size copies materialized by <see cref="PrepareHostWrite"/>). This was the invisible
        /// ML-lane leak: snapshots were removed from the dict but never Disposed. If this climbs across a
        /// lane, snapshot SABs are leaking; it should now return to ~0 between tests.</summary>
        public static long LiveSnapshotBytes => s_liveSnapshotBytes;

        /// <inheritdoc/>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (disposing)
            {
                TypedArrayView?.Dispose();
                SharedBuffer?.Dispose();
                // Free any snapshot SABs still resident (buffer disposed with pending host-write
                // snapshots) — otherwise their full-buffer-size JS SharedArrayBuffers leak.
                DisposeAllSnapshots();
                _snapshotsByHWC = null;
                _snapshotRefCounts = null;
            }
            // Decrement the resident counters exactly once (dispose may run on the finalizer path too).
            if (_liveCounted)
            {
                _liveCounted = false;
                s_liveBufferCount--;
                s_liveBufferBytes -= _liveBytes;
            }
        }
    }
}
