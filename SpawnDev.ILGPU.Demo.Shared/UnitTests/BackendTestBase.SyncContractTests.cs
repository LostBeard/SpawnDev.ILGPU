using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part: sync/async surface contract tests. Governing principle (Captain-approved 2026-06-13,
    // see Plans/sync-async-contract-2026-06-13.md): ASYNC-ONLY where an op WAITS or OBSERVES;
    // SYNC stays for fire-and-forget (dispatch / alloc / upload / flush-submit).
    //
    // The two ops these tests pin down differ precisely:
    //  - Synchronize() = WAIT for completion. Cannot be honored on the single browser thread (or on
    //    a remote/P2P backend), so the sync form THROWS NotSupportedException on every browser
    //    backend (WebGPU/WebGL/Wasm); SynchronizeAsync() is the portable wait. This makes the
    //    silent-wrong-behavior class structurally impossible: there is no sync wait left to misuse.
    //  - Flush() = SUBMIT pending work without waiting. Fire-and-forget, so it is honest
    //    synchronously on every local backend (desktop: eager/no-op; WebGPU: encoder submit;
    //    WebGL/Wasm: no-op). It must NOT throw on browser. (Only a remote/P2P stream, whose submit
    //    is an async network send, throws on sync Flush — not exercised here; P2P isn't a PMT lane.)
    //
    // These tests exercise the real production path: dispatch a kernel, assert the sync-vs-async
    // contract, then drain + async-readback and verify the output is correct on every backend.
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// True for the browser backends (WebGPU, WebGL, Wasm), whose GPU boundary is async-only and
        /// where the synchronous Synchronize()/Flush() surface is unsupported (throws).
        /// </summary>
        private static bool IsBrowserBackend(AcceleratorType type) =>
            type == AcceleratorType.WebGPU ||
            type == AcceleratorType.WebGL ||
            type == AcceleratorType.Wasm;

        /// <summary>
        /// Asserts <paramref name="action"/> throws <see cref="NotSupportedException"/>. Throws a
        /// descriptive test failure otherwise (no-throw, or wrong exception type).
        /// </summary>
        private static void AssertThrowsNotSupported(Action action, string label)
        {
            try
            {
                action();
            }
            catch (NotSupportedException)
            {
                return; // contract satisfied
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"{label}: expected NotSupportedException (sync = desktop-only), but got " +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
            throw new Exception(
                $"{label}: expected NotSupportedException (sync = desktop-only) on a browser backend, " +
                $"but the call returned without throwing.");
        }

        // Fills buf[i] = i*2; used to put real pending work on the stream before exercising the
        // synchronization contract.
        private static void SyncContract_FillKernel(Index1D i, ArrayView<int> buf) => buf[i] = i.X * 2;

        /// <summary>
        /// Synchronize() contract: throws NotSupportedException on browser backends, completes on
        /// desktop. SynchronizeAsync() drains correctly on EVERY backend (dispatch -> drain ->
        /// async readback returns the kernel's output).
        /// </summary>
        [TestMethod]
        public async Task SyncSynchronizeContractTest() => await RunTest(async accelerator =>
        {
            bool browser = IsBrowserBackend(accelerator.AcceleratorType);
            const int count = 64;

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(
                SyncContract_FillKernel);
            using var buf = accelerator.Allocate1D<int>(count);
            kernel((Index1D)count, buf.View);

            if (browser)
            {
                // The sync wait surface is unsupported at the browser GPU boundary.
                AssertThrowsNotSupported(() => accelerator.Synchronize(), "accelerator.Synchronize()");
                AssertThrowsNotSupported(
                    () => accelerator.DefaultStream.Synchronize(), "stream.Synchronize()");
            }
            else
            {
                // Desktop: the sync wait must work (and is the canonical completion barrier).
                accelerator.Synchronize();
                accelerator.DefaultStream.Synchronize();
            }

            // The async drain is portable everywhere — and after it, the output must be correct.
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < count; i++)
            {
                if (result[i] != i * 2)
                    throw new Exception(
                        $"SynchronizeAsync drain produced wrong data at {i}: expected {i * 2}, got {result[i]}");
            }
        });

        /// <summary>
        /// Flush() contract: throws NotSupportedException on browser backends, completes on desktop.
        /// FlushAsync() (submit-without-wait) is portable on EVERY backend; following it with
        /// SynchronizeAsync() yields correct output.
        /// </summary>
        [TestMethod]
        public async Task SyncFlushContractTest() => await RunTest(async accelerator =>
        {
            bool browser = IsBrowserBackend(accelerator.AcceleratorType);
            const int count = 64;

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(
                SyncContract_FillKernel);
            using var buf = accelerator.Allocate1D<int>(count);
            kernel((Index1D)count, buf.View);

            // Flush is a fire-and-forget SUBMIT (start the work, don't wait) — honest synchronously on
            // EVERY local backend (desktop: eager/no-op; WebGPU: encoder submit; WebGL/Wasm: no-op).
            // It must NOT throw on browser; only the WAIT (Synchronize) is async-only. (A remote/P2P
            // stream whose submit is an async network send is the sole backend where sync Flush throws.)
            accelerator.Flush();
            accelerator.DefaultStream.Flush();

            // The async submit is portable everywhere; complete the work and verify the output.
            await accelerator.FlushAsync();
            await accelerator.DefaultStream.FlushAsync();
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < count; i++)
            {
                if (result[i] != i * 2)
                    throw new Exception(
                        $"FlushAsync + SynchronizeAsync produced wrong data at {i}: expected {i * 2}, got {result[i]}");
            }
        });
    }
}
