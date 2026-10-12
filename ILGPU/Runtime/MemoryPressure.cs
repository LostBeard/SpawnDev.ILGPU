// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: MemoryPressure.cs
//
// Device-memory-pressure-aware allocation helper.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Something that holds device memory it does not need right now and can give back on demand: a buffer
    /// pool's free (returned, not live) buffers. Registered with an accelerator through
    /// <see cref="MemoryPressure.RegisterReclaimer"/>, it lets an allocation ANYWHERE on that accelerator make
    /// room, not only allocations made by the holder itself.
    /// </summary>
    /// <remarks>
    /// <see cref="Reclaim"/> is called from whichever thread is allocating, which is usually NOT the holder's
    /// own thread. An implementation must be safe against its owner using it at the same moment; the simple
    /// correct form is a <c>Monitor.TryEnter</c> on the owner's lock that returns 0 when the owner is busy.
    /// It must never allocate device memory.
    /// </remarks>
    public interface IDeviceMemoryReclaimer
    {
        /// <summary>Bytes that <see cref="Reclaim"/> could free right now.</summary>
        long ReclaimableBytes { get; }

        /// <summary>
        /// When the holder last used its memory, as a <see cref="Stopwatch.GetTimestamp"/> value. The least
        /// recently used holder gives memory back first.
        /// </summary>
        long LastUsedTimestamp { get; }

        /// <summary>
        /// Frees reclaimable device memory, at least <paramref name="bytesWanted"/> when it holds that much,
        /// and returns the number of bytes freed (0 when the holder is busy or holds nothing reclaimable).
        /// </summary>
        long Reclaim(long bytesWanted);
    }

    /// <summary>
    /// Helpers for performing device allocations under memory pressure: try, and on failure
    /// reclaim caller-owned reclaimable memory and retry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reclaim before the driver spills, not after an allocation fails.</b> On Windows the NVIDIA driver's
    /// default "CUDA sysmem fallback" never fails a <c>cuMemAlloc</c> that does not fit: it places the memory in
    /// system RAM, and every kernel touching it then runs over PCIe. A reclaim that waits for a failure
    /// therefore never runs there. MEASURED 2026-10-12 (Vertex: a chat model, Whisper, Kokoro and a sentence
    /// encoder in one process on a 12 GB RTX 4070): 14.7 GB of live buffers, 7.3 GB of it idle in buffer
    /// pools, 1.4 GB spilled to system RAM, and a one-sentence reply took 5.5 minutes instead of seconds.
    /// </para>
    /// <para>
    /// So once anything has registered an <see cref="IDeviceMemoryReclaimer"/>, every
    /// <see cref="Accelerator.AllocateRaw(long, int, int)"/> first checks the device's free memory (on
    /// backends that can report it, see <see cref="Accelerator.TryGetFreeMemory"/>) and, when the allocation
    /// would leave less than <see cref="GetHeadroomBytes"/>, reclaims from the registered holders, least
    /// recently used first. An allocation that still throws gets the same reclaim and one retry. With nothing
    /// registered the check costs one null test.
    /// </para>
    /// </remarks>
    public static class MemoryPressure
    {
        /// <summary>
        /// Free device memory to keep in reserve (for the driver, other processes, and allocations nobody
        /// can reclaim), capped at 5% of the device's memory. Per accelerator:
        /// <see cref="Accelerator.DeviceMemoryHeadroomBytes"/>.
        /// </summary>
        public static long DefaultHeadroomBytes { get; set; } = 512L * 1024 * 1024;

        /// <summary>
        /// How long a free-memory measurement is trusted (other processes allocate too). Within it, allocations
        /// are counted against the measurement instead of querying the driver each time.
        /// </summary>
        public static TimeSpan FreeMemoryCacheDuration { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>Times an allocation made room by reclaiming BEFORE allocating (diagnostics).</summary>
        public static long ProactiveReclaimCount => Interlocked.Read(ref _proactiveReclaimCount);

        /// <summary>Bytes freed by those proactive reclaims (diagnostics).</summary>
        public static long ProactiveReclaimedBytes => Interlocked.Read(ref _proactiveReclaimedBytes);

        private static long _proactiveReclaimCount;
        private static long _proactiveReclaimedBytes;

        [ThreadStatic]
        private static bool _reclaiming;

        /// <summary>Per-accelerator list of reclaimers plus the cached free-memory measurement.</summary>
        internal sealed class ReclaimRegistry
        {
            public readonly object Sync = new();
            public readonly List<WeakReference<IDeviceMemoryReclaimer>> Items = new();
            /// <summary>False once the backend has said it cannot report free memory.</summary>
            public bool CanQuery = true;
            /// <summary>Last measured free bytes minus what was allocated since; -1 = unknown.</summary>
            public long EstimatedFree = -1;
            public long MeasuredAt;
        }

        /// <summary>The headroom in effect for <paramref name="accelerator"/>.</summary>
        public static long GetHeadroomBytes(Accelerator accelerator) =>
            accelerator.DeviceMemoryHeadroomBytes
            ?? Math.Min(DefaultHeadroomBytes, Math.Max(0, accelerator.MemorySize / 20));

        /// <summary>
        /// Registers <paramref name="reclaimer"/> with <paramref name="accelerator"/> (held weakly; registering
        /// twice is a no-op). From then on allocations on this accelerator check free memory first.
        /// </summary>
        public static void RegisterReclaimer(this Accelerator accelerator, IDeviceMemoryReclaimer reclaimer)
        {
            if (accelerator is null)
                throw new ArgumentNullException(nameof(accelerator));
            if (reclaimer is null)
                throw new ArgumentNullException(nameof(reclaimer));
            var registry = accelerator.ReclaimRegistry;
            if (registry == null)
            {
                Interlocked.CompareExchange(ref accelerator.ReclaimRegistry, new ReclaimRegistry(), null);
                registry = accelerator.ReclaimRegistry!;
            }
            lock (registry.Sync)
            {
                foreach (var weak in registry.Items)
                    if (weak.TryGetTarget(out var existing) && ReferenceEquals(existing, reclaimer))
                        return;
                registry.Items.Add(new WeakReference<IDeviceMemoryReclaimer>(reclaimer));
            }
        }

        /// <summary>Removes <paramref name="reclaimer"/> (and any collected entries).</summary>
        public static void UnregisterReclaimer(this Accelerator accelerator, IDeviceMemoryReclaimer reclaimer)
        {
            var registry = accelerator?.ReclaimRegistry;
            if (registry == null)
                return;
            lock (registry.Sync)
                registry.Items.RemoveAll(w => !w.TryGetTarget(out var r) || ReferenceEquals(r, reclaimer));
        }

        /// <summary>
        /// Asks the registered holders to free at least <paramref name="bytesWanted"/>, least recently used
        /// first, and returns the bytes actually freed. Pending work is flushed first so a buffer about to be
        /// freed is not still referenced by an un-submitted dispatch (browser command-encoder semantics).
        /// </summary>
        public static long ReclaimDeviceMemory(this Accelerator accelerator, long bytesWanted)
        {
            var registry = accelerator?.ReclaimRegistry;
            if (registry == null || bytesWanted <= 0 || _reclaiming
                || Volatile.Read(ref accelerator!.ActiveStreamCaptures) > 0)
                return 0;
            List<IDeviceMemoryReclaimer> holders;
            lock (registry.Sync)
            {
                holders = new List<IDeviceMemoryReclaimer>(registry.Items.Count);
                registry.Items.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var weak in registry.Items)
                    if (weak.TryGetTarget(out var r))
                        holders.Add(r);
            }
            holders.Sort((a, b) => a.LastUsedTimestamp.CompareTo(b.LastUsedTimestamp));

            long freed = 0;
            _reclaiming = true;
            try
            {
                try { accelerator!.Synchronize(); } catch { /* best-effort flush, as in AllocateWithReclaim */ }
                foreach (var holder in holders)
                {
                    if (freed >= bytesWanted)
                        break;
                    if (holder.ReclaimableBytes <= 0)
                        continue;
                    freed += holder.Reclaim(bytesWanted - freed);
                }
            }
            finally
            {
                _reclaiming = false;
            }
            lock (registry.Sync)
                registry.EstimatedFree = -1; // freed memory changes the picture; measure again next time
            return freed;
        }

        /// <summary>
        /// Called by <see cref="Accelerator.AllocateRaw(long, int, int)"/> before every allocation: makes room
        /// when the allocation would leave less than the headroom free. No-op until something registers.
        /// </summary>
        internal static void BeforeAllocate(Accelerator accelerator, long bytes)
        {
            var registry = accelerator.ReclaimRegistry;
            if (registry == null || !registry.CanQuery || _reclaiming
                || Volatile.Read(ref accelerator.ActiveStreamCaptures) > 0
                || registry.Items.Count == 0) // nothing to reclaim from: no reason to measure
                return;
            long headroom = GetHeadroomBytes(accelerator);
            long now = Stopwatch.GetTimestamp();
            lock (registry.Sync)
            {
                // Plenty of room by a recent measurement: count this allocation against it and skip the query.
                // Frees are not counted, so the estimate only errs toward measuring again.
                if (registry.EstimatedFree >= 0
                    && Stopwatch.GetElapsedTime(registry.MeasuredAt, now) < FreeMemoryCacheDuration
                    && registry.EstimatedFree - bytes >= 2 * headroom)
                {
                    registry.EstimatedFree -= bytes;
                    return;
                }
            }
            if (!accelerator.TryGetFreeMemory(out long free))
            {
                registry.CanQuery = false;
                return;
            }
            long shortfall = bytes + headroom - free;
            if (shortfall > 0)
            {
                long reclaimed = accelerator.ReclaimDeviceMemory(shortfall);
                if (reclaimed > 0)
                {
                    Interlocked.Increment(ref _proactiveReclaimCount);
                    Interlocked.Add(ref _proactiveReclaimedBytes, reclaimed);
                    if (!accelerator.TryGetFreeMemory(out free))
                        free = -1;
                }
            }
            lock (registry.Sync)
            {
                registry.EstimatedFree = free < 0 ? -1 : free - bytes;
                registry.MeasuredAt = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// Called by <see cref="Accelerator.AllocateRaw(long, int, int)"/> when the backend's allocation threw:
        /// reclaims from the registered holders and reports whether a retry is worth making.
        /// </summary>
        internal static bool ReclaimAfterFailure(Accelerator accelerator, long bytes) =>
            accelerator.ReclaimRegistry != null
            && accelerator.ReclaimDeviceMemory(bytes + GetHeadroomBytes(accelerator)) > 0;

        /// <summary>
        /// Performs a device allocation that can recover from running out of device memory.
        /// Runs <paramref name="allocate"/>; if it throws (a device out-of-memory surfaces as a
        /// backend-specific exception - <c>CudaException</c>, an OpenCL error, a WebGPU/JS device
        /// error - NOT a single .NET <see cref="OutOfMemoryException"/>, which is why this catches
        /// broadly), it flushes pending GPU work via <see cref="Accelerator.Synchronize"/> (so a
        /// buffer that <paramref name="reclaim"/> is about to dispose is not still referenced by an
        /// in-flight dispatch under WebGPU/WebGL command-encoder semantics), invokes
        /// <paramref name="reclaim"/> to free reclaimable device memory, and retries ONCE. If the
        /// retry still fails it throws, surfacing how much was reclaimed and the caller's diagnostic
        /// context (with the original failure as the inner exception).
        /// </summary>
        /// <remarks>
        /// This is the flush -&gt; reclaim -&gt; retry MECHANISM only. The eviction POLICY - which
        /// buffers are safe to free (e.g. a pool's Returned-but-not-live buffers, never the live
        /// working set or permanent weights) - belongs to the caller's <paramref name="reclaim"/>
        /// callback, so a pool composes this in without surrendering its own size-bucketing, naming,
        /// or per-dtype tracking. Lifted from the SpawnDev.ILGPU.ML <c>BufferPool.Rent</c> pattern.
        ///
        /// The <see cref="Accelerator.Synchronize"/> flush is best-effort (browser backends flush
        /// rather than block); a reclaim that only frees already-Returned buffers - the intended use -
        /// is safe regardless, since no pending dispatch references them.
        /// </remarks>
        /// <typeparam name="T">The allocation result type (e.g. a <c>MemoryBuffer1D&lt;T, TStride&gt;</c>).</typeparam>
        /// <param name="accelerator">The accelerator whose pending work is flushed before reclaiming.</param>
        /// <param name="allocate">Performs the device allocation. Called once, or twice if the first throws.</param>
        /// <param name="reclaim">Frees reclaimable device memory on pressure; returns the number of bytes freed.</param>
        /// <param name="describeState">
        /// Optional: builds the exception message when allocation fails even after reclaiming, given the
        /// reclaimed byte count - the place to report the live working set. If null a generic message is used.
        /// </param>
        /// <returns>The allocation result.</returns>
        public static T AllocateWithReclaim<T>(
            this Accelerator accelerator,
            Func<T> allocate,
            Func<long> reclaim,
            Func<long, string>? describeState = null)
        {
            if (accelerator is null)
                throw new ArgumentNullException(nameof(accelerator));
            if (allocate is null)
                throw new ArgumentNullException(nameof(allocate));
            if (reclaim is null)
                throw new ArgumentNullException(nameof(reclaim));

            try
            {
                return allocate();
            }
            catch
            {
                // Flush pending GPU work first, so a buffer reclaim() is about to dispose is not still
                // referenced by an in-flight dispatch (WebGPU/WebGL command-encoder semantics).
                try { accelerator.Synchronize(); } catch { /* best-effort flush */ }

                long reclaimed = reclaim();

                try
                {
                    return allocate();
                }
                catch (Exception retryFailure)
                {
                    string detail = describeState?.Invoke(reclaimed)
                        ?? "Device allocation failed under memory pressure even after reclaiming " +
                           $"{reclaimed} bytes of reclaimable device memory.";
                    throw new InvalidOperationException(detail, retryFailure);
                }
            }
        }
    }
}
