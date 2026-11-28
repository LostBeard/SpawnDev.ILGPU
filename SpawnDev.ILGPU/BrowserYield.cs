using System.Threading.Tasks;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// Returning to the browser's event loop from the single .NET thread of a Blazor WebAssembly page.
    /// </summary>
    internal static class BrowserYield
    {
        /// <summary>
        /// Completes from a JS <c>setTimeout(0)</c> registered NOW, so everything the page queued before it (input, its own
        /// timers, rendering) runs first: timers of the same delay fire in the order they were registered. It was
        /// <c>Task.Delay(1)</c>, which rides the .NET runtime's single JS timer: when that timer had been scheduled before the
        /// page's own <c>setTimeout(0)</c> (other .NET timers pending, as in a long run), the continuation came due in the same
        /// tick and ran first (KernelLoadAsync_YieldsToBrowserBeforeCompile failed about 1 run in 3 of the Node WebGPU suite,
        /// and in the browser suites of 2026-11-08). <c>globalThis</c>: a page's window, a worker's scope, Node.
        /// </summary>
        public static Task ToEventLoopAsync()
        {
            var done = new TaskCompletionSource();
            try
            {
                using var scope = SpawnJSRuntime.Instance.Get<Window>("globalThis");
                scope.SetTimeout(() => done.TrySetResult(), 0); // a one-shot callback: released after it runs
            }
            catch
            {
                return Task.Delay(1); // no JS global to schedule on: the .NET timer still yields
            }
            return done.Task;
        }
    }
}
