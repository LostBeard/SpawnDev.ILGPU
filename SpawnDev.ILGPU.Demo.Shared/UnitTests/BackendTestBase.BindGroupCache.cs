using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Bind-group cache (WebGPU): WebGPUBackend.EnableBindGroupCaching reuses the GPUBindGroup
    // across dispatches that share an identical (pipeline + data-buffer bindings) signature instead
    // of recreating + disposing one per dispatch — the per-step GPU floor for fixed-shape ML decode.
    //
    // This test runs on EVERY backend (the scale+add kernel is cross-backend) and additionally
    // asserts the cache HIT/MISS behavior on WebGPU, where the feature lives. EnableBindGroupCaching
    // is a process-global static, so the test resets it (and clears the cache) in a finally so it
    // can never leak into another test.
    public abstract partial class BackendTestBase
    {
        // 2 ArrayView params + 2 scalar params. The scalars exercise the per-dispatch
        // _scalar_params buffer, which a cached entry must OWN (stable identity) and rewrite on each
        // hit. output[i] = input[i] * mul + add.
        static void BindGroupCache_ScaleAddKernel(
            Index1D idx, ArrayView<float> input, ArrayView<float> output, float mul, float add)
        {
            output[idx] = input[idx] * mul + add;
        }

        // A cache HIT on the 2nd identical-signature dispatch — issued with NEW scalar values — must:
        //   (a) reuse the cached bind group (BindGroupCacheHits increments on WebGPU), AND
        //   (b) still compute the correct result, because the cached entry's OWNED scalar buffer is
        //       rewritten with the new scalars. We assert the cached output is bit-identical to an
        //       uncached run and within tolerance of the CPU reference.
        [TestMethod]
        public async Task BindGroupCache_HitReusesGroup_AndMatchesUncached() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 256;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i * 0.25f - 3f;
            const float mul1 = 2.5f, add1 = 1.0f;   // first (cache-miss) dispatch
            const float mul2 = -1.5f, add2 = 7.0f;  // second (cache-hit) dispatch — DIFFERENT scalars

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                BindGroupCache_ScaleAddKernel);

            // Uncached reference for the (mul2, add2) pass — caching OFF (default).
            float[] reference;
            using (var refIn = accelerator.Allocate1D(src))
            using (var refOut = accelerator.Allocate1D<float>(N))
            {
                k((Index1D)N, refIn.View, refOut.View, mul2, add2);
                await accelerator.SynchronizeAsync();
                reference = await refOut.CopyToHostAsync<float>();
            }

            var webgpu = accelerator as WebGPUAccelerator;
            long missesAfterFirst = 0;

            WebGPUBackend.EnableBindGroupCaching = true;
            try
            {
                using var input = accelerator.Allocate1D(src);
                using var output = accelerator.Allocate1D<float>(N);

                // Dispatch 1 — cache MISS. Sync between dispatches so the owned scalar buffer is safe
                // to rewrite (mirrors the fixed-shape decode loop's per-step Synchronize).
                k((Index1D)N, input.View, output.View, mul1, add1);
                await accelerator.SynchronizeAsync();
                if (webgpu != null) missesAfterFirst = webgpu.BindGroupCacheMisses;

                long hitsBefore = webgpu?.BindGroupCacheHits ?? 0;

                // Dispatch 2 — same buffers, NEW scalars. Expect a cache HIT.
                k((Index1D)N, input.View, output.View, mul2, add2);
                await accelerator.SynchronizeAsync();

                var got = await output.CopyToHostAsync<float>();

                for (int i = 0; i < N; i++)
                {
                    float expected = src[i] * mul2 + add2;
                    if (MathF.Abs(got[i] - expected) > MathF.Abs(expected) * 1e-5f + 1e-6f)
                        throw new Exception(
                            $"BindGroupCache HIT wrong result at {i}: expected {expected} got {got[i]}. " +
                            "The cached bind group's owned scalar buffer was not rewritten with the new (mul2, add2).");
                    if (got[i] != reference[i])
                        throw new Exception(
                            $"BindGroupCache cached output != uncached reference at {i}: cached {got[i]} vs uncached {reference[i]}.");
                }

                // WebGPU-only: the feature must actually have cached (hit on dispatch 2, miss on 1).
                if (webgpu != null)
                {
                    if (webgpu.BindGroupCacheHits <= hitsBefore)
                        throw new Exception(
                            $"BindGroupCache: expected a HIT on the 2nd identical dispatch but BindGroupCacheHits " +
                            $"did not increase ({hitsBefore} -> {webgpu.BindGroupCacheHits}). " +
                            "Buffer identity not stable across dispatches, or the cache key/lookup is wrong.");
                    if (missesAfterFirst < 1)
                        throw new Exception(
                            $"BindGroupCache: expected a MISS on the 1st dispatch but BindGroupCacheMisses={missesAfterFirst}.");

                    // ClearBindGroupCache() must release cached entries and reset the counters.
                    await accelerator.SynchronizeAsync();
                    webgpu.ClearBindGroupCache();
                    if (webgpu.BindGroupCacheHits != 0 || webgpu.BindGroupCacheMisses != 0)
                        throw new Exception(
                            $"ClearBindGroupCache() should reset counters to 0; got hits={webgpu.BindGroupCacheHits} misses={webgpu.BindGroupCacheMisses}.");
                }
            }
            finally
            {
                await accelerator.SynchronizeAsync();
                webgpu?.ClearBindGroupCache();
                WebGPUBackend.EnableBindGroupCaching = false;
            }
        });

        // Guard against the catastrophic failure mode: a key that does NOT discriminate by data-buffer
        // identity would let a cached bind group bind the WRONG GPU memory (silent corruption). Two
        // dispatches over two DISTINCT buffer pairs must both MISS (zero hits), and each must compute
        // against its own buffers. Runs the real kernel on every backend; asserts cache counters on WebGPU.
        [TestMethod]
        public async Task BindGroupCache_DifferentBuffersMiss_NotFalseHit() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 128;
            var srcA = new float[N];
            var srcB = new float[N];
            for (int i = 0; i < N; i++) { srcA[i] = i; srcB[i] = 1000 + i; }

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                BindGroupCache_ScaleAddKernel);

            var webgpu = accelerator as WebGPUAccelerator;
            WebGPUBackend.EnableBindGroupCaching = true;
            try
            {
                using var inA = accelerator.Allocate1D(srcA);
                using var outA = accelerator.Allocate1D<float>(N);
                using var inB = accelerator.Allocate1D(srcB);
                using var outB = accelerator.Allocate1D<float>(N);

                // Same kernel + same scalars, DIFFERENT buffers -> two distinct keys -> two misses.
                k((Index1D)N, inA.View, outA.View, 2f, 1f);
                await accelerator.SynchronizeAsync();
                k((Index1D)N, inB.View, outB.View, 2f, 1f);
                await accelerator.SynchronizeAsync();

                var gotA = await outA.CopyToHostAsync<float>();
                var gotB = await outB.CopyToHostAsync<float>();
                for (int i = 0; i < N; i++)
                {
                    float eA = srcA[i] * 2f + 1f, eB = srcB[i] * 2f + 1f;
                    if (MathF.Abs(gotA[i] - eA) > MathF.Abs(eA) * 1e-5f + 1e-4f)
                        throw new Exception($"Pair A wrong at {i}: expected {eA} got {gotA[i]} (cache bound the wrong buffer?).");
                    if (MathF.Abs(gotB[i] - eB) > MathF.Abs(eB) * 1e-5f + 1e-4f)
                        throw new Exception($"Pair B wrong at {i}: expected {eB} got {gotB[i]} (cache bound the wrong buffer?).");
                }

                if (webgpu != null)
                {
                    if (webgpu.BindGroupCacheHits != 0)
                        throw new Exception(
                            $"BindGroupCache: dispatches over DIFFERENT buffers must MISS, but BindGroupCacheHits={webgpu.BindGroupCacheHits}. " +
                            "The key is not discriminating by buffer identity - a cached bind group could bind the WRONG GPU memory.");
                    if (webgpu.BindGroupCacheMisses < 2)
                        throw new Exception($"BindGroupCache: expected >=2 misses for 2 distinct buffer sets, got {webgpu.BindGroupCacheMisses}.");
                }
            }
            finally
            {
                await accelerator.SynchronizeAsync();
                webgpu?.ClearBindGroupCache();
                WebGPUBackend.EnableBindGroupCaching = false;
            }
        });
    }
}
