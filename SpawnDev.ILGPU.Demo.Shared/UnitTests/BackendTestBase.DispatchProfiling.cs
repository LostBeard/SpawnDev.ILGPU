using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Dispatch profiling (WebGPU): WebGPUBackend.EnableDispatchProfiling accumulates a step's
    // GPU-wait so a consumer (e.g. the ML fixed-shape decode loop) can split a step's wall-time
    // into total-GPU-wait vs .NET-side build/submit and find where the per-step time goes.
    //
    // A step has TWO distinct GPU-wait surfaces, and naming a bottleneck from only one is the
    // partial-profile trap (the slices must SUM TO THE MEASURED TOTAL):
    //   1. SynchronizeAsync's queue.OnSubmittedWorkDone() drain -> ProfileSyncWaitMs / ProfileSyncWaitCount
    //   2. the GPU->CPU readback staging.MapAsync(Read) wait inside WebGPUBuffer.CopyToHostAsync
    //      -> ProfileReadbackWaitMs / ProfileReadbackWaitCount
    // When a decode's GPU-wait hides in logits/shape readbacks (the mapAsync is queued behind prior
    // work), it lands in the readback surface and reads ~0 in the sync surface. This test proves
    // BOTH surfaces are instrumented on the real production path so neither slice goes missing.
    //
    // The kernel + readback run on EVERY backend (cross-backend scale+add); the counter assertions
    // are WebGPU-only (where the instrumentation lives). EnableDispatchProfiling is a process-global
    // static, so the test resets it (and zeros the counters) in a finally so it can never leak.
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task DispatchProfiling_CapturesSyncAndReadbackWait() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 256;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i * 0.5f - 2f;

            // Reuse the cross-backend scale+add kernel defined in BackendTestBase.BindGroupCache.cs:
            // output[i] = input[i] * mul + add.
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                BindGroupCache_ScaleAddKernel);

            var webgpu = accelerator as WebGPUAccelerator;

            WebGPUBackend.EnableDispatchProfiling = true;
            try
            {
                using var input = accelerator.Allocate1D(src);
                using var output = accelerator.Allocate1D<float>(N);

                // ResetDispatchProfiling() must zero ALL FOUR counters (both surfaces).
                WebGPUBackend.ResetDispatchProfiling();
                if (WebGPUBackend.ProfileSyncWaitMs != 0 || WebGPUBackend.ProfileSyncWaitCount != 0 ||
                    WebGPUBackend.ProfileReadbackWaitMs != 0 || WebGPUBackend.ProfileReadbackWaitCount != 0)
                    throw new Exception(
                        "ResetDispatchProfiling() must zero all four counters; got " +
                        $"sync=({WebGPUBackend.ProfileSyncWaitMs}ms,{WebGPUBackend.ProfileSyncWaitCount}) " +
                        $"readback=({WebGPUBackend.ProfileReadbackWaitMs}ms,{WebGPUBackend.ProfileReadbackWaitCount}).");

                // Sync-wait surface: a real dispatch + SynchronizeAsync drains via OnSubmittedWorkDone.
                k((Index1D)N, input.View, output.View, 2f, 1f);
                await accelerator.SynchronizeAsync();

                // Readback surface: a real GPU->CPU readback waits in MapAsync(Read).
                var got = await output.CopyToHostAsync<float>();

                // Correctness: instrumentation must not alter the readback result.
                for (int i = 0; i < N; i++)
                {
                    float expected = src[i] * 2f + 1f;
                    if (MathF.Abs(got[i] - expected) > MathF.Abs(expected) * 1e-5f + 1e-6f)
                        throw new Exception(
                            $"DispatchProfiling: profiled dispatch wrong at {i}: expected {expected} got {got[i]} " +
                            "(timing the readback must not change its data).");
                }

                long syncCountAfterOne = WebGPUBackend.ProfileSyncWaitCount;
                long readbackCountAfterOne = WebGPUBackend.ProfileReadbackWaitCount;

                // A second readback must increment the readback counter AGAIN — proves it times every
                // readback rather than flipping a one-shot flag.
                _ = await output.CopyToHostAsync<float>();

                if (webgpu != null)
                {
                    if (syncCountAfterOne < 1)
                        throw new Exception(
                            $"DispatchProfiling: SynchronizeAsync after a dispatch must record >=1 OnSubmittedWorkDone drain, " +
                            $"got ProfileSyncWaitCount={syncCountAfterOne}.");
                    if (readbackCountAfterOne < 1)
                        throw new Exception(
                            $"DispatchProfiling: a CopyToHostAsync readback must record >=1 MapAsync(Read) wait, " +
                            $"got ProfileReadbackWaitCount={readbackCountAfterOne}. The readback GPU-wait surface is NOT " +
                            "instrumented — a step's readback wait would read as 0 and the slices would not sum to the measured total.");
                    if (WebGPUBackend.ProfileReadbackWaitCount <= readbackCountAfterOne)
                        throw new Exception(
                            $"DispatchProfiling: the second readback must increment ProfileReadbackWaitCount again " +
                            $"({readbackCountAfterOne} -> {WebGPUBackend.ProfileReadbackWaitCount}); the counter must time EVERY readback.");
                    if (WebGPUBackend.ProfileSyncWaitMs < 0 || WebGPUBackend.ProfileReadbackWaitMs < 0)
                        throw new Exception(
                            $"DispatchProfiling: accumulated wait ms must be non-negative, got " +
                            $"sync={WebGPUBackend.ProfileSyncWaitMs}ms readback={WebGPUBackend.ProfileReadbackWaitMs}ms.");
                }
                else
                {
                    // Non-WebGPU backends never hit the WebGPU GPU-wait surfaces, so the counters stay 0.
                    // The dispatch + two readbacks above still ran on this backend (real production path).
                    if (WebGPUBackend.ProfileSyncWaitCount != 0 || WebGPUBackend.ProfileReadbackWaitCount != 0)
                        throw new Exception(
                            "DispatchProfiling: a non-WebGPU backend must not touch the WebGPU profiling counters, got " +
                            $"sync={WebGPUBackend.ProfileSyncWaitCount} readback={WebGPUBackend.ProfileReadbackWaitCount}.");
                }
            }
            finally
            {
                WebGPUBackend.EnableDispatchProfiling = false;
                WebGPUBackend.ResetDispatchProfiling();
            }
        });
    }
}
