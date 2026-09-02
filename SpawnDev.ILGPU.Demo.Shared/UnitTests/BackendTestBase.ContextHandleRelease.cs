using System;
using System.Threading.Tasks;
using SpawnDev.SpawnJS;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    /// <summary>
    /// A Context created and disposed must not retain browser interop handles.
    /// </summary>
    /// <remarks>
    /// ⚠️ WHAT SHIPPED BROKEN (fixed in fork 2.3.2). WebGPU device enumeration calls `RequestAdapter`, and
    /// the resulting `GPUAdapter` is held by the registered ILGPU Device for the life of the Context.
    /// Nothing released it, so every `Context.Create()` abandoned one adapter and pinned real GPU driver
    /// resources for the life of the page. Upstream `Device` is not IDisposable because a DESKTOP device is
    /// a description - a CUDA ordinal, an OpenCL device id - that owns nothing; a BROWSER device owns a live
    /// JS handle, which is why this was browser-only and no desktop lane could ever show it.
    ///
    /// ⚠️ THE GATE LIVES HERE, not only in the consumer that found it. SpawnDev.ILGPU.ML has an equivalent
    /// test, but the defect and the fix are BOTH in this repo - so a change here made without running ML
    /// would have nothing to catch it. A fix gated only downstream is a fix one refactor away from
    /// silently coming back.
    /// </remarks>
    public abstract partial class BackendTestBase
    {
        /// <summary>The number of live entries in SpawnJS's interop slot table.</summary>
        /// <remarks>
        /// A slot is released only when its .NET wrapper is disposed, so this counts exactly the handles the
        /// managed side still holds - which is what the leak was made of.
        /// </remarks>
        private static int CountInteropSlots(SpawnJSRuntime js)
        {
            using var table = js.Get<SpawnJSObjectReference>("SpawnJSInterop.spawnJSObjects");
            using var keys = js.Call<SpawnJSObjectReference, SpawnJSObjectReference>("Object.keys", table);
            return keys.Get<int>("length");
        }

        /// <summary>
        /// Creating and disposing this backend's Context+Accelerator pair must not grow the slot table.
        /// </summary>
        /// <remarks>
        /// ⚠️ Compares TWO round counts rather than asserting an absolute number. Startup and first-use
        /// caches legitimately add a few permanent slots, so "delta must be zero" would be flaky, and
        /// "delta under N" would silently tolerate a real per-Context leak the moment N exceeded the round
        /// count. Requiring the EXTRA rounds to add nothing measures growth per Context and is immune to any
        /// constant hold.
        ///
        /// ⚠️ Settles after WaitForPendingFinalizers before counting. A slot is released by the wrapper's
        /// finalizer and that release crosses into JS; counting immediately reads in-flight releases as a
        /// leak. Without the settle, WebGL measures a convincing and entirely fictional 2.00 per Context.
        /// </remarks>
        [TestMethod(Timeout = 300000)]
        public async Task Context_Dispose_ReleasesBrowserDeviceHandles()
        {
            var js = SpawnJSRuntime.Instance;
            if (js == null || !js.IsBrowser)
                throw new UnsupportedTestException("no JS interop on this lane - desktop devices own no handle");

            // ⚠️ NOT RunTest: this base CACHES one accelerator for the whole class, and a cached instance is
            // the one thing that cannot measure per-Context growth. Each round owns its own pair.
            async Task CycleAsync(int rounds)
            {
                for (int i = 0; i < rounds; i++)
                {
                    var (context, accelerator) = await CreateAcceleratorAsync();
                    accelerator.Dispose();
                    context.Dispose();
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                await Task.Delay(300);
            }

            await CycleAsync(1);   // warm: first-use caches are a one-time cost, not growth

            const int small = 2, large = 6;

            int start = CountInteropSlots(js);
            await CycleAsync(small);
            int afterSmall = CountInteropSlots(js);
            await CycleAsync(large);
            int afterLarge = CountInteropSlots(js);

            int deltaSmall = afterSmall - start;
            int deltaLarge = afterLarge - afterSmall;
            double perContext = deltaLarge / (double)large;

            Console.WriteLine($"[SlotGrowth] {BackendName} start={start} +{small} -> {afterSmall} "
                            + $"(delta {deltaSmall}); +{large} -> {afterLarge} (delta {deltaLarge}, "
                            + $"{perContext:F2} per Context)");

            if (deltaLarge > 1)
                throw new Exception(
                    $"{BackendName}: {large} Context create/dispose cycles added {deltaLarge} SpawnJS "
                  + $"interop slots ({perContext:F2} per Context) after a full GC, while the preceding "
                  + $"{small} cycles added {deltaSmall}. Growth that scales with the cycle count is an "
                  + "abandoned browser handle - a Device holding a GPUAdapter or a WebGL context that "
                  + "Context.Dispose is not releasing. Set ILGPU.Context.ContextDisposeTrace = true to see "
                  + "what Dispose actually did; if it prints NOTHING, the assembly you rebuilt is not the "
                  + "one the browser loaded (Context lives in SpawnDev.ILGPU.Fork, not the wrapper).");
        }
    }
}
