using System;
using System.Diagnostics;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Per-launch cost of a trivial kernel, in the two patterns a training loop uses: synchronize after every
    // launch, and a batch of launches with one synchronize (SpawnDev.ILGPU.ML's Snake trainer flushes every 8
    // steps of ~35 launches). Prints "[LaunchCost] <backend> ..." - read it with PMT_CONSOLE_LOG=LaunchCost.
    // Added 2026-09-28: that trainer timed out at 300 s on the CPU and Wasm lanes (10,240 steps) while CUDA
    // took 3 s; the CPU cause was 0.65 ms per launch (fixed: CPU lane loop). The values are checked so the
    // measured path is a correct one.
    public abstract partial class BackendTestBase
    {
        static void LaunchCostKernel(Index1D i, ArrayView<float> a, ArrayView<float> b) => a[i] = b[i] * 2f - 0.5f;

        [TestMethod]
        public async Task LaunchCost_TrivialKernel_SyncedAndBatched() => await RunTest(async accelerator =>
        {
            const int n = 32, synced = 100, batches = 25, perBatch = 8;
            var host = new float[n];
            for (int i = 0; i < n; i++) host[i] = i * 0.25f;
            using var b = accelerator.Allocate1D(host);
            using var a = accelerator.Allocate1D<float>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(LaunchCostKernel);
            k(n, a.View, b.View);
            await accelerator.SynchronizeAsync();   // compile + first launch outside the measurement

            var sw = Stopwatch.StartNew();
            for (int r = 0; r < synced; r++) { k(n, a.View, b.View); await accelerator.SynchronizeAsync(); }
            double syncedMs = sw.Elapsed.TotalMilliseconds / synced;

            sw.Restart();
            for (int r = 0; r < batches; r++)
            {
                for (int j = 0; j < perBatch; j++) k(n, a.View, b.View);
                await accelerator.SynchronizeAsync();
            }
            double batchedMs = sw.Elapsed.TotalMilliseconds / (batches * perBatch);

            var got = await a.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (got[i] != host[i] * 2f - 0.5f)
                    throw new Exception($"[{i}] = {got[i]}, expected {host[i] * 2f - 0.5f}");
            Console.WriteLine($"[LaunchCost] {accelerator.AcceleratorType}: synced {syncedMs:F3} ms/launch, " +
                              $"batched x{perBatch} {batchedMs:F3} ms/launch (32-element kernel)");
        });
    }
}
