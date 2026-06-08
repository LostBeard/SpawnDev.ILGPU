// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: MemoryPressure.cs
//
// Device-memory-pressure-aware allocation helper.
// ---------------------------------------------------------------------------------------

using System;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Helpers for performing device allocations under memory pressure: try, and on failure
    /// reclaim caller-owned reclaimable memory and retry.
    /// </summary>
    public static class MemoryPressure
    {
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
