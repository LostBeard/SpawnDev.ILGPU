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
    // Eviction is RECUR-ONLY: a signature is cached only on its SECOND sighting, so a one-shot
    // dispatch never parks a live GPUBindGroup (the unbounded-growth / framework-error guard). That
    // means the first HIT for a recurring signature is on the THIRD dispatch (1st = record, 2nd =
    // cache, 3rd = hit), which these tests account for.
    //
    // These run on EVERY backend (the scale+add kernel is cross-backend) and additionally assert the
    // cache HIT/MISS/entry-count behavior on WebGPU, where the feature lives. EnableBindGroupCaching
    // is a process-global static, so the tests reset it (and clear the cache) in a finally so it can
    // never leak into another test.
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

        // Under recur-only eviction a signature is cached on its 2nd sighting and HITS from the 3rd.
        // Dispatch the same (pipeline + buffers) three times with DIFFERENT scalars each time:
        //   1st = first-sight miss (recorded, not cached),
        //   2nd = recurring miss (cached — its OWNED scalar buffer captured),
        //   3rd = HIT (reuses the cached group; owned scalar buffer rewritten with the 3rd scalars).
        // The 3rd dispatch must (a) increment BindGroupCacheHits and (b) still compute correctly,
        // proving the cached entry's owned scalar buffer is rewritten per hit. Asserted bit-identical
        // to an uncached run and within tolerance of the CPU reference.
        [TestMethod]
        public async Task BindGroupCache_HitReusesGroup_AndMatchesUncached() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 256;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i * 0.25f - 3f;
            const float mul1 = 2.5f, add1 = 1.0f;    // 1st dispatch (first-sight miss)
            const float mul2 = -1.5f, add2 = 7.0f;   // 2nd dispatch (recurring miss -> cached)
            const float mul3 = 0.75f, add3 = -4.0f;  // 3rd dispatch (HIT) — DIFFERENT scalars again

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                BindGroupCache_ScaleAddKernel);

            // Uncached reference for the (mul3, add3) pass — caching OFF (default).
            float[] reference;
            using (var refIn = accelerator.Allocate1D(src))
            using (var refOut = accelerator.Allocate1D<float>(N))
            {
                k((Index1D)N, refIn.View, refOut.View, mul3, add3);
                await accelerator.SynchronizeAsync();
                reference = await refOut.CopyToHostAsync<float>();
            }

            var webgpu = accelerator as WebGPUAccelerator;

            WebGPUBackend.EnableBindGroupCaching = true;
            try
            {
                using var input = accelerator.Allocate1D(src);
                using var output = accelerator.Allocate1D<float>(N);

                // 1st + 2nd dispatches: both misses under recur-only (1st recorded, 2nd cached).
                // Sync between dispatches so the owned scalar buffer is safe to rewrite (mirrors the
                // fixed-shape decode loop's per-step Synchronize).
                k((Index1D)N, input.View, output.View, mul1, add1);
                await accelerator.SynchronizeAsync();
                k((Index1D)N, input.View, output.View, mul2, add2);
                await accelerator.SynchronizeAsync();

                long missesAfterTwo = webgpu?.BindGroupCacheMisses ?? 0;
                long hitsBefore = webgpu?.BindGroupCacheHits ?? 0;

                // 3rd dispatch: same buffers, NEW scalars -> expect a cache HIT.
                k((Index1D)N, input.View, output.View, mul3, add3);
                await accelerator.SynchronizeAsync();

                var got = await output.CopyToHostAsync<float>();
                for (int i = 0; i < N; i++)
                {
                    float expected = src[i] * mul3 + add3;
                    if (MathF.Abs(got[i] - expected) > MathF.Abs(expected) * 1e-5f + 1e-6f)
                        throw new Exception(
                            $"BindGroupCache HIT wrong result at {i}: expected {expected} got {got[i]}. " +
                            "The cached bind group's owned scalar buffer was not rewritten with the new (mul3, add3).");
                    if (got[i] != reference[i])
                        throw new Exception(
                            $"BindGroupCache cached output != uncached reference at {i}: cached {got[i]} vs uncached {reference[i]}.");
                }

                // WebGPU-only: the feature must cache on the 2nd dispatch and hit on the 3rd.
                if (webgpu != null)
                {
                    if (missesAfterTwo < 2)
                        throw new Exception(
                            $"BindGroupCache: expected 2 misses over the first two dispatches (recur-only caches on the 2nd), got {missesAfterTwo}.");
                    if (webgpu.BindGroupCacheHits <= hitsBefore)
                        throw new Exception(
                            $"BindGroupCache: expected a HIT on the 3rd identical dispatch but BindGroupCacheHits " +
                            $"did not increase ({hitsBefore} -> {webgpu.BindGroupCacheHits}). " +
                            "Buffer identity not stable across dispatches, or the cache key/lookup is wrong.");
                    if (webgpu.BindGroupCacheEntryCount != 1)
                        throw new Exception(
                            $"BindGroupCache: exactly one signature recurred, so expected 1 cached entry, got {webgpu.BindGroupCacheEntryCount}.");

                    // ClearBindGroupCache() must release cached entries and reset everything.
                    await accelerator.SynchronizeAsync();
                    webgpu.ClearBindGroupCache();
                    if (webgpu.BindGroupCacheHits != 0 || webgpu.BindGroupCacheMisses != 0 || webgpu.BindGroupCacheEntryCount != 0)
                        throw new Exception(
                            $"ClearBindGroupCache() should reset everything; got hits={webgpu.BindGroupCacheHits} " +
                            $"misses={webgpu.BindGroupCacheMisses} entries={webgpu.BindGroupCacheEntryCount}.");
                }
            }
            finally
            {
                await accelerator.SynchronizeAsync();
                webgpu?.ClearBindGroupCache();
                WebGPUBackend.EnableBindGroupCaching = false;
            }
        });

        // Discrimination + no-accumulate guard. Three dispatches over THREE DISTINCT buffer pairs
        // (same kernel + scalars) produce three distinct keys. Under recur-only every one is a
        // first-sight miss: zero hits, nothing cached, each output computed against its OWN buffers.
        // This catches BOTH failure modes at once:
        //   - a non-discriminating key (e.g. the old NativePtr key, which is not unique per WebGPU
        //     buffer) would make the 2nd dispatch a recurring miss (cached) and the 3rd a HIT binding
        //     the WRONG buffers -> hits>0 + wrong output + a cached entry; and
        //   - a cache that stored every one-shot dispatch would accumulate entries (the bloat /
        //     framework-error mode) -> EntryCount>0.
        [TestMethod]
        public async Task BindGroupCache_DistinctSignaturesMiss_NoFalseHit_NoAccumulate() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 128;
            var srcA = new float[N];
            var srcB = new float[N];
            var srcC = new float[N];
            for (int i = 0; i < N; i++) { srcA[i] = i; srcB[i] = 1000 + i; srcC[i] = 50000 + i; }

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
                using var inC = accelerator.Allocate1D(srcC);
                using var outC = accelerator.Allocate1D<float>(N);

                // Same kernel + same scalars, DIFFERENT buffers -> three distinct keys -> three misses.
                k((Index1D)N, inA.View, outA.View, 2f, 1f);
                await accelerator.SynchronizeAsync();
                k((Index1D)N, inB.View, outB.View, 2f, 1f);
                await accelerator.SynchronizeAsync();
                k((Index1D)N, inC.View, outC.View, 2f, 1f);
                await accelerator.SynchronizeAsync();

                var gotA = await outA.CopyToHostAsync<float>();
                var gotB = await outB.CopyToHostAsync<float>();
                var gotC = await outC.CopyToHostAsync<float>();
                for (int i = 0; i < N; i++)
                {
                    float eA = srcA[i] * 2f + 1f, eB = srcB[i] * 2f + 1f, eC = srcC[i] * 2f + 1f;
                    if (MathF.Abs(gotA[i] - eA) > MathF.Abs(eA) * 1e-5f + 1e-4f)
                        throw new Exception($"Pair A wrong at {i}: expected {eA} got {gotA[i]} (cache bound the wrong buffer?).");
                    if (MathF.Abs(gotB[i] - eB) > MathF.Abs(eB) * 1e-5f + 1e-4f)
                        throw new Exception($"Pair B wrong at {i}: expected {eB} got {gotB[i]} (cache bound the wrong buffer?).");
                    if (MathF.Abs(gotC[i] - eC) > MathF.Abs(eC) * 1e-5f + 1e-4f)
                        throw new Exception($"Pair C wrong at {i}: expected {eC} got {gotC[i]} (cache bound the wrong buffer?).");
                }

                if (webgpu != null)
                {
                    if (webgpu.BindGroupCacheHits != 0)
                        throw new Exception(
                            $"BindGroupCache: dispatches over DIFFERENT buffers must all MISS, but BindGroupCacheHits={webgpu.BindGroupCacheHits}. " +
                            "The key is not discriminating by buffer identity - a cached bind group could bind the WRONG GPU memory.");
                    if (webgpu.BindGroupCacheMisses < 3)
                        throw new Exception($"BindGroupCache: expected >=3 misses for 3 distinct buffer sets, got {webgpu.BindGroupCacheMisses}.");
                    if (webgpu.BindGroupCacheEntryCount != 0)
                        throw new Exception(
                            $"BindGroupCache: no signature recurred, so recur-only eviction must cache NOTHING, but EntryCount={webgpu.BindGroupCacheEntryCount} " +
                            "(single-use dispatches are accumulating - the bloat / framework-error mode).");
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
