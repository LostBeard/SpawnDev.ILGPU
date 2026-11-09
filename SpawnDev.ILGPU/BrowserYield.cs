using System.Threading.Tasks;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// Returning to the browser's event loop from the single .NET thread of a Blazor WebAssembly page.
    /// </summary>
    internal static class BrowserYield
    {
        /// <summary>
        /// Completes from a JS timer (Task.Delay schedules setTimeout in the browser), so the browser runs its queued input,
        /// timers and rendering first. Guarded by KernelLoadAsync_YieldsToBrowserBetweenKernels.
        /// </summary>
        public static Task ToEventLoopAsync() => Task.Delay(1);
    }
}
