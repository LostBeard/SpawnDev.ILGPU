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

        // Kernel with several ArrayView params + scalars + a sizable straight-line body so the
        // generated WGSL is non-trivial. The shader-resolve phase cost (workgroup regex + the
        // overrideConstants string-concat + the full-WGSL-string shader-cache lookup) is O(WGSL
        // length), so a tiny kernel would under-represent it. Auto-grouped (sets _ilgpu_user_dim),
        // matching how the decode dispatches elementwise/matmul nodes. Not bit-identical to GPT-2.
        static void CpuProlog_BigBodyKernel(
            Index1D idx,
            ArrayView<float> a, ArrayView<float> b, ArrayView<float> c, ArrayView<float> outp,
            float s0, float s1, float s2)
        {
            float va = a[idx], vb = b[idx], vc = c[idx];
            float t0 = va * s0 + vb * s1 - vc * s2;
            float t1 = t0 * t0 + va - vb;
            float t2 = t1 * s0 - t0 * s1 + vc;
            float t3 = t2 + t1 * t0 - va * vc;
            float t4 = t3 * s2 + t2 - t1;
            float t5 = t4 * t4 - t3 + s0 * s1;
            float t6 = t5 + t4 * s2 - t3 * va;
            float t7 = t6 * t5 - t4 + vb * vc;
            float t8 = t7 + t6 * s0 - t5 * s1;
            float t9 = t8 * t7 + t6 - s2 * va;
            outp[idx] = t0 + t1 + t2 + t3 + t4 + t5 + t6 + t7 + t8 + t9;
        }

        // Directly measures the per-dispatch CPU prologue split (shader-resolve / arg-build / encode)
        // by re-dispatching ONE fixed-shape kernel many times — the fixed-shape decode pattern. Turns
        // the (forward − GPU-wait − readback) RESIDUAL from Tuvok's measurement into directly-measured
        // slices so the prologue fix targets the real dominant phase, not an assumed one
        // (feedback-dont-name-bottleneck-from-partial-profile). WebGPU-only counters; kernel runs cross-backend.
        // Emits a grep-able ===CPUPHASES=== line via Console.WriteLine (NOT Console.Error — that trips
        // #blazor-error-ui per feedback-console-error-writeline-triggers-blazor-error-ui).
        [TestMethod]
        public async Task DispatchProfiling_CpuProloguePhases_FixedShapeRepeat() => await RunEmulatedTest(async accelerator =>
        {
            // WebGPU-only: the CPU-prologue phase counters only accumulate in the WebGPU dispatch path,
            // so the heavy 256-dispatch loop measures nothing on other backends (and times out the slow
            // Wasm lane). Skip cleanly elsewhere.
            if (accelerator is not WebGPUAccelerator)
                throw new UnsupportedTestException("WebGPU-only CPU-prologue phase measurement.");

            const int N = 4096;
            const int Dispatches = 256;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i * 0.001f;

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>, float, float, float>(
                CpuProlog_BigBodyKernel);

            var webgpu = accelerator as WebGPUAccelerator;
            using var a = accelerator.Allocate1D(src);
            using var b = accelerator.Allocate1D(src);
            using var c = accelerator.Allocate1D(src);
            using var outp = accelerator.Allocate1D<float>(N);

            // Warm up once (shader compile + first-dispatch costs) so the measured window is steady-state.
            k((Index1D)N, a.View, b.View, c.View, outp.View, 1.1f, 2.2f, 3.3f);
            await accelerator.SynchronizeAsync();

            WebGPUBackend.EnableDispatchProfiling = true;
            try
            {
                WebGPUBackend.ResetDispatchProfiling();
                for (int d = 0; d < Dispatches; d++)
                {
                    k((Index1D)N, a.View, b.View, c.View, outp.View, 1.1f, 2.2f, 3.3f);
                    if ((d & 31) == 31) await accelerator.SynchronizeAsync(); // flush every 32, like the decode loop
                }
                await accelerator.SynchronizeAsync();

                if (webgpu != null)
                {
                    long n = WebGPUBackend.ProfileCpuDispatchCount;
                    double sr = WebGPUBackend.ProfileCpuShaderResolveMs;
                    double ab = WebGPUBackend.ProfileCpuArgBuildMs;
                    double bg = WebGPUBackend.ProfileCpuBindGroupMs;
                    double en = WebGPUBackend.ProfileCpuEncodeMs;
                    double tot = sr + ab + bg + en;
                    // Permanent green form uses Console.WriteLine (stdout — PMT doesn't surface it, harmless).
                    // For an AD-HOC measurement, flip this to Console.Error.WriteLine: stderr IS PMT-captured,
                    // but it trips #blazor-error-ui so the test goes red while the data still prints
                    // (feedback-console-error-writeline-triggers-blazor-error-ui).
                    Console.WriteLine(
                        $"===CPUPHASES=== dispatches={n} totalCpuMs={tot:F1} perDispatchMs={(n > 0 ? tot / n : 0):F4} | " +
                        $"shaderResolveMs={sr:F1} ({(tot > 0 ? 100 * sr / tot : 0):F0}%) " +
                        $"argPrepMs={ab:F1} ({(tot > 0 ? 100 * ab / tot : 0):F0}%) " +
                        $"bindGroupMs={bg:F1} ({(tot > 0 ? 100 * bg / tot : 0):F0}%) " +
                        $"encodeMs={en:F1} ({(tot > 0 ? 100 * en / tot : 0):F0}%)");

                    if (n < Dispatches)
                        throw new Exception($"CpuProloguePhases: expected >= {Dispatches} profiled dispatches, got {n} — the CPU-phase timers did not fire.");
                    if (sr < 0 || ab < 0 || en < 0)
                        throw new Exception($"CpuProloguePhases: negative phase ms (sr={sr}, ab={ab}, en={en}).");
                }
            }
            finally
            {
                WebGPUBackend.EnableDispatchProfiling = false;
                WebGPUBackend.ResetDispatchProfiling();
            }
        });

        // Measures the per-drain OnSubmittedWorkDone round-trip overhead FLOOR: a trivial 1-element
        // kernel + one SynchronizeAsync, repeated, so each drain waits on ~no GPU work → the measured
        // time is ~pure round-trip. Disambiguates the real decode's ~43% sync-drain (~60ms/drain over
        // ~71 drains): if the floor is small, that 60ms is GPU EXECUTION (lever = fewer/bigger dispatches
        // = kernel fusion) and batching more before the drain won't help; if the floor is large, the
        // round-trip dominates and sync-frequency batching helps. One-off measurement: emits via
        // Console.Error (PMT-captured, trips #blazor-error-ui → red, data prints).
        [TestMethod]
        public async Task DispatchProfiling_SyncDrainOverhead_Floor() => await RunEmulatedTest(async accelerator =>
        {
            // WebGPU-only: only the WebGPU SynchronizeAsync path records ProfileSyncWait*, so this
            // 64-drain loop measures nothing on other backends (and burdens the slow Wasm lane).
            if (accelerator is not WebGPUAccelerator)
                throw new UnsupportedTestException("WebGPU-only sync-drain overhead measurement.");

            const int Syncs = 64;
            var src = new float[1] { 1f };
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                BindGroupCache_ScaleAddKernel);
            var webgpu = accelerator as WebGPUAccelerator;
            using var input = accelerator.Allocate1D(src);
            using var output = accelerator.Allocate1D<float>(1);

            k((Index1D)1, input.View, output.View, 2f, 1f); // warmup
            await accelerator.SynchronizeAsync();

            WebGPUBackend.EnableDispatchProfiling = true;
            try
            {
                WebGPUBackend.ResetDispatchProfiling();
                for (int i = 0; i < Syncs; i++)
                {
                    k((Index1D)1, input.View, output.View, 2f, 1f);
                    await accelerator.SynchronizeAsync(); // one drain per trivial dispatch
                }
                if (webgpu != null)
                {
                    double ms = WebGPUBackend.ProfileSyncWaitMs;
                    long n = WebGPUBackend.ProfileSyncWaitCount;
                    // Permanent green form: Console.WriteLine (stdout, harmless in PMT). Flip to
                    // Console.Error for an ad-hoc stderr-captured read (red test). Measured floor
                    // 2026-06-05: ~0.6-1.1ms/drain → the real decode's ~60ms/drain is ~98% GPU execution.
                    Console.WriteLine($"===SYNCDRAIN=== drains={n} totalMs={ms:F1} perDrainOverheadMs={(n > 0 ? ms / n : 0):F3}");
                    if (n < Syncs)
                        throw new Exception($"SyncDrainOverhead: expected >= {Syncs} drains, got {n}.");
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
