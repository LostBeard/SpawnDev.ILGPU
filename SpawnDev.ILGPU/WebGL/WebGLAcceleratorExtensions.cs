// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGL
//                 WebGL2 Compute Library for Blazor WebAssembly
//
// File: WebGLAcceleratorExtensions.cs
//
// Extension methods for WebGL accelerator synchronization.
// ---------------------------------------------------------------------------------------

namespace SpawnDev.ILGPU.WebGL
{
    /// <summary>
    /// Extension methods for WebGLAccelerator.
    /// </summary>
    public static class WebGLAcceleratorExtensions
    {
        /// <summary>
        /// Asynchronously waits for all submitted GL worker dispatches to complete.
        /// This awaits the PendingWork tasks set by the most recent RunKernel calls.
        /// </summary>
        /// <param name="accelerator">The WebGL accelerator.</param>
        /// <returns>A task that completes when all worker dispatches are done.</returns>
        public static async Task SynchronizeAsync(this WebGLAccelerator accelerator)
        {
            await accelerator.DrainPendingWorkAsync();
        }
    }
}
