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

        static void LaunchCostHeavyKernel(Index1D i, ArrayView<float> a, ArrayView<float> b)
        {
            float x = b[i];
            for (int k = 0; k < 16; k++) x = MathF.Sqrt(x * x + 1f) * 0.5f;
            a[i] = x;
        }

        // Wasm-only: batched per-launch time by element count, for each candidate
        // WasmAccelerator.NonBarrierMinItemsPerWorker, light and heavy kernels, every value checked.
        // Prints "[LaunchCost] WasmSweep ..." rows (PMT_CONSOLE_LOG=LaunchCost) - how that default was chosen.
        [TestMethod(Timeout = 600000)]
        public async Task LaunchCost_WasmWorkerSizing_Sweep() => await RunTest(async accelerator =>
        {
            if (accelerator is not SpawnDev.ILGPU.Wasm.WasmAccelerator)
                throw new UnsupportedTestException("Wasm-only: sizes the Wasm worker fan-out");
            int saved = SpawnDev.ILGPU.Wasm.WasmAccelerator.NonBarrierMinItemsPerWorker;
            try
            {
                var light = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(LaunchCostKernel);
                var heavy = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(LaunchCostHeavyKernel);
                foreach (int n in new[] { 32, 1024, 8192, 65536, 262144, 1 << 20 })
                {
                    var host = new float[n];
                    for (int i = 0; i < n; i++) host[i] = (i % 97) * 0.25f;
                    using var b = accelerator.Allocate1D(host);
                    using var a = accelerator.Allocate1D<float>(n);
                    foreach (var (name, k) in new[] { ("light", light), ("heavy", heavy) })
                    {
                        var row = new System.Text.StringBuilder($"[LaunchCost] WasmSweep {name,-5} n={n,8}:");
                        foreach (int minItems in new[] { 1, 1024, 4096, 16384, 65536 })
                        {
                            SpawnDev.ILGPU.Wasm.WasmAccelerator.NonBarrierMinItemsPerWorker = minItems;
                            k(n, a.View, b.View);
                            await accelerator.SynchronizeAsync();
                            int reps = n <= 65536 ? 24 : 8;
                            var sw = Stopwatch.StartNew();
                            for (int r = 0; r < reps; r++) k(n, a.View, b.View);
                            await accelerator.SynchronizeAsync();
                            row.Append($" min{minItems}={sw.Elapsed.TotalMilliseconds / reps:F3}");
                            var got = await a.CopyToHostAsync<float>();
                            for (int i = 0; i < n; i++)
                            {
                                float x = host[i], want;
                                if (name == "light") want = x * 2f - 0.5f;
                                else { for (int q = 0; q < 16; q++) x = MathF.Sqrt(x * x + 1f) * 0.5f; want = x; }
                                if (got[i] != want)
                                    throw new Exception($"{name} n={n} minItems={minItems}: [{i}] = {got[i]}, expected {want}");
                            }
                        }
                        Console.WriteLine(row.Append(" ms/launch").ToString());
                    }
                }
            }
            finally { SpawnDev.ILGPU.Wasm.WasmAccelerator.NonBarrierMinItemsPerWorker = saved; }
        });

        static void LaunchCostPoolTouchKernel(Index1D i, ArrayView<int> pool, int offset) => pool[offset + i] += 1;

        // Wasm-only: per-launch time of a 64-thread kernel that touches 64 ints of a "pool" buffer, by pool size.
        // SpawnDev.Codecs' VP9 wavefront launches tiny kernels over a whole frame pool (~46 per 1080p frame) and
        // reconstruction ran 81x slower on Wasm than WebGPU (Geordi, 2026-11-17); if per-launch time grows with the
        // pool and not with the work, the cost is the dispatch's copy of the buffer into / out of linear memory.
        // Prints "[LaunchCost] WasmPool ..." rows with the WasmDispatchProfile split (PMT_CONSOLE_LOG=LaunchCost).
        [TestMethod(Timeout = 600000)]
        public async Task LaunchCost_WasmPoolSize() => await RunTest(async accelerator =>
        {
            if (accelerator is not SpawnDev.ILGPU.Wasm.WasmAccelerator)
                throw new UnsupportedTestException("Wasm-only: measures the Wasm per-dispatch buffer copy");
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(LaunchCostPoolTouchKernel);
            const int threads = 64, reps = 40;
            // Forward, then reversed: separates "this size is slow" from "the Nth size measured is slow" (2026-11-17:
            // the 1 MB row, always 2nd, cost ~1.45 ms/launch vs 0.17-0.35 for the others, with the worker idle).
            int[] sizes = { 16 << 10, 256 << 10, 2 << 20, 8 << 20 };   // 64 KB .. 32 MB
            foreach (var (ints, pass) in sizes.Select(s => (s, "fwd")).Concat(sizes.Reverse().Select(s => (s, "rev"))))
            {
                using var pool = accelerator.Allocate1D<int>(ints);
                pool.MemSetToZero();
                int offset = ints / 2;
                k(threads, pool.View, offset);
                await accelerator.SynchronizeAsync();
                SpawnDev.ILGPU.Wasm.WasmDispatchProfile.Reset();
                SpawnDev.ILGPU.Wasm.WasmDispatchProfile.Enabled = true;
                // The launch calls themselves (k returns once the dispatch is queued) are timed one by one:
                // a few long ones point at a timer (the worker's 4 ms ack), many even ones at per-launch work.
                double maxCall = 0, sumCall = 0;
                var sw = Stopwatch.StartNew();
                for (int r = 0; r < reps; r++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    k(threads, pool.View, offset);
                    double c = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    sumCall += c; if (c > maxCall) maxCall = c;
                }
                double launchedAt = sw.Elapsed.TotalMilliseconds;
                await accelerator.SynchronizeAsync();
                double ms = sw.Elapsed.TotalMilliseconds / reps;
                SpawnDev.ILGPU.Wasm.WasmDispatchProfile.Enabled = false;
                Console.WriteLine($"[LaunchCost] WasmPool {ints * 4 / 1024,6} KB ({pass}): {ms:F3} ms/launch " +
                    $"(calls {sumCall / reps:F3} avg, {maxCall:F3} max; drain {(sw.Elapsed.TotalMilliseconds - launchedAt):F2} ms) | " +
                    SpawnDev.ILGPU.Wasm.WasmDispatchProfile.Summary());
                var got = await pool.CopyToHostAsync<int>();
                for (int i = 0; i < threads; i++)
                    if (got[offset + i] != reps + 1)
                        throw new Exception($"pool {ints * 4} B: [{offset + i}] = {got[offset + i]}, expected {reps + 1}");
            }
        });

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

            bool wasm = accelerator is SpawnDev.ILGPU.Wasm.WasmAccelerator;
            if (wasm) { SpawnDev.ILGPU.Wasm.WasmDispatchProfile.Reset(); SpawnDev.ILGPU.Wasm.WasmDispatchProfile.Enabled = true; }
            sw.Restart();
            for (int r = 0; r < batches; r++)
            {
                for (int j = 0; j < perBatch; j++) k(n, a.View, b.View);
                await accelerator.SynchronizeAsync();
            }
            double batchedMs = sw.Elapsed.TotalMilliseconds / (batches * perBatch);
            string profile = "";
            if (wasm)
            {
                SpawnDev.ILGPU.Wasm.WasmDispatchProfile.Enabled = false;
                profile = " | " + SpawnDev.ILGPU.Wasm.WasmDispatchProfile.Summary();
            }

            // Deep batches: the steady-state cost once a launch no longer waits on the one before.
            sw.Restart();
            double launchLoopMs = 0;
            for (int r = 0; r < 4; r++)
            {
                long t0 = Stopwatch.GetTimestamp();
                for (int j = 0; j < 64; j++) k(n, a.View, b.View);
                launchLoopMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                await accelerator.SynchronizeAsync();
            }
            string deep = $", batched x64 {sw.Elapsed.TotalMilliseconds / 256:F3} ms/launch " +
                          $"(launch calls {launchLoopMs / 256:F3}, drain {(sw.Elapsed.TotalMilliseconds - launchLoopMs) / 256:F3})";

            // Wasm A/B: the same batched loop on the serialized path (no pipelined dispatch).
            string serialized = "";
            if (wasm)
            {
                bool savedPipe = SpawnDev.ILGPU.Wasm.WasmAccelerator.EnablePipelinedDispatch;
                SpawnDev.ILGPU.Wasm.WasmAccelerator.EnablePipelinedDispatch = false;
                try
                {
                    sw.Restart();
                    for (int r = 0; r < batches; r++)
                    {
                        for (int j = 0; j < perBatch; j++) k(n, a.View, b.View);
                        await accelerator.SynchronizeAsync();
                    }
                    serialized = $", serialized x{perBatch} {sw.Elapsed.TotalMilliseconds / (batches * perBatch):F3} ms/launch";
                }
                finally { SpawnDev.ILGPU.Wasm.WasmAccelerator.EnablePipelinedDispatch = savedPipe; }
            }

            var got = await a.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (got[i] != host[i] * 2f - 0.5f)
                    throw new Exception($"[{i}] = {got[i]}, expected {host[i] * 2f - 0.5f}");
            Console.WriteLine($"[LaunchCost] {accelerator.AcceleratorType}: synced {syncedMs:F3} ms/launch, " +
                              $"batched x{perBatch} {batchedMs:F3} ms/launch{deep}{serialized} (32-element kernel){profile}");
        });
    }
}
