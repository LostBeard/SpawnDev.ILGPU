// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGPU
//                 WebGPU Compute Library for Blazor WebAssembly
//
// File: WebGPUAcceleratorExtensions.cs
// ---------------------------------------------------------------------------------------

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// Extension methods for WebGPUAccelerator.
    /// </summary>
    public static class WebGPUAcceleratorExtensions
    {
        /// <summary>
        /// Asynchronously waits for all submitted GPU work to complete.
        /// </summary>
        /// <remarks>
        /// In Blazor WebAssembly, blocking synchronization is not possible.
        /// Use this async method instead of Synchronize() to wait for GPU completion.
        /// </remarks>
        /// <param name="accelerator">The WebGPU accelerator.</param>
        /// <returns>A task that completes when all GPU work is done.</returns>
        public static async Task SynchronizeAsync(this WebGPUAccelerator accelerator)
        {
            // Flush any pending compute passes from the command encoder to the GPU queue.
            // Without this, OnSubmittedWorkDone() could return immediately because the
            // work is still in the encoder, not yet submitted.
            accelerator.FlushPendingCommands();
            var queue = accelerator.NativeAccelerator.Queue;
            if (queue != null)
            {
                if (Backend.WebGPUBackend.EnableDispatchProfiling)
                {
                    var profSw = System.Diagnostics.Stopwatch.StartNew();
                    await queue.OnSubmittedWorkDone();
                    Backend.WebGPUBackend.ProfileSyncWaitMs += profSw.Elapsed.TotalMilliseconds;
                    Backend.WebGPUBackend.ProfileSyncWaitCount++;
                }
                else
                {
                    await queue.OnSubmittedWorkDone();
                }
            }
            // Drain pending CheckShaderAsync background tasks. If the dispatch we just
            // flushed compiled a shader that fails validation, CheckShaderAsync queues
            // the error via AddShaderError. Without this drain, the queued error leaks
            // into the NEXT Synchronize and surfaces there with a stale message (the
            // rc.13 ILGPUReduceHalfTest 12-binding leak from a prior ManyViews_11Views
            // test was caused by exactly this). DrainShaderChecksAsync awaits all
            // tracked background validations so ThrowIfGpuErrors sees this dispatch's
            // errors HERE rather than later. Mirrors SynchronizeInternal()'s behavior.
            await accelerator.NativeAccelerator.DrainShaderChecksAsync();
            accelerator.NativeAccelerator.ThrowIfGpuErrors();
        }

        /// <summary>
        /// Asynchronously waits for all submitted GPU work to complete on the native accelerator.
        /// </summary>
        /// <param name="accelerator">The native WebGPU accelerator.</param>
        /// <returns>A task that completes when all GPU work is done.</returns>
        public static async Task SynchronizeAsync(this WebGPUNativeAccelerator accelerator)
        {
            var queue = accelerator.Queue;
            if (queue != null)
            {
                if (Backend.WebGPUBackend.EnableDispatchProfiling)
                {
                    var profSw = System.Diagnostics.Stopwatch.StartNew();
                    await queue.OnSubmittedWorkDone();
                    Backend.WebGPUBackend.ProfileSyncWaitMs += profSw.Elapsed.TotalMilliseconds;
                    Backend.WebGPUBackend.ProfileSyncWaitCount++;
                }
                else
                {
                    await queue.OnSubmittedWorkDone();
                }
            }
            // See note above on the WebGPUAccelerator overload — drain pending shader
            // validation tasks then surface any queued errors at THIS call site.
            await accelerator.DrainShaderChecksAsync();
            accelerator.ThrowIfGpuErrors();
        }
    }
}
