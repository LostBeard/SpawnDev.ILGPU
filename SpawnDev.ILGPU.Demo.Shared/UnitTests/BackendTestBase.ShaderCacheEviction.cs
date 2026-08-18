using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Shader-cache retention + eviction (WebGPU).
    //
    // The compiled-shader cache holds one GPUShaderModule + GPUComputePipeline + GPUBindGroupLayout per
    // distinct (WGSL, override-constants) pair, and was historically bound to the accelerator's LIFETIME -
    // emptied only on Dispose. That is correct for an app with a fixed kernel set, but a long-lived
    // accelerator that keeps compiling new kernels retains every pipeline it ever built. These guards cover
    // the two escape hatches added for that: an explicit ClearShaderCache() and an LRU cap.
    //
    // The dangerous failure mode is a USE-AFTER-DISPOSE: the dispatch path's shader-resolution cache stores
    // raw references to shaders OWNED by this cache, so an eviction that does not purge those references
    // hands back a RELEASED pipeline on the next resolve hit. Both tests therefore re-dispatch and verify
    // real numeric output AFTER eviction - not just counters.
    public abstract partial class BackendTestBase
    {
        // Reuses the cross-backend scale+add kernel already defined for the bind-group-cache guard.
        [TestMethod]
        public async Task ShaderCache_ClearReleasesShaders_AndRedispatchStillCorrect() => await RunEmulatedTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only shader cache.");

            var savedCap = WebGPUBackend.MaxCachedShaders;
            WebGPUBackend.MaxCachedShaders = 0; // unlimited for this test; exercise the EXPLICIT clear
            WebGPUBackend.EnableShaderCaching = true;
            try
            {
                webgpu.ClearShaderCache();
                if (webgpu.CachedShaderCount != 0)
                    throw new Exception($"ClearShaderCache did not empty the cache: {webgpu.CachedShaderCount} left.");

                const int N = 128;
                const float mul = 2f, add = 3f;
                var src = new float[N];
                for (int i = 0; i < N; i++) src[i] = i * 0.25f - 2f;
                using var input = accelerator.Allocate1D(src);
                using var output = accelerator.Allocate1D<float>(N);

                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                    BindGroupCache_ScaleAddKernel);

                // 1) First dispatch compiles + caches exactly one shader.
                k((Index1D)N, input.View, output.View, mul, add);
                await accelerator.SynchronizeAsync();
                int afterFirst = webgpu.CachedShaderCount;
                if (afterFirst < 1)
                    throw new Exception($"Expected the dispatch to cache a shader, CachedShaderCount={afterFirst}.");

                // 2) Re-dispatching the SAME kernel at the SAME config must REUSE, not grow the cache.
                //    This is the "is the cache actually hitting?" measurement.
                long hitsBefore = webgpu.ShaderResolveCacheHits;
                for (int i = 0; i < 8; i++) k((Index1D)N, input.View, output.View, mul, add);
                await accelerator.SynchronizeAsync();
                if (webgpu.CachedShaderCount != afterFirst)
                    throw new Exception(
                        $"Re-dispatching an identical kernel grew the shader cache {afterFirst} -> {webgpu.CachedShaderCount}. " +
                        $"The cache key is not stable across dispatches, so every dispatch re-inserts and nothing ever hits.");
                if (webgpu.ShaderResolveCacheHits <= hitsBefore)
                    throw new Exception($"Repeat dispatches did not register resolve-cache hits (stayed {webgpu.ShaderResolveCacheHits}).");

                // 3) Clear releases the GPU resources + interop handles.
                webgpu.ClearShaderCache();
                if (webgpu.CachedShaderCount != 0)
                    throw new Exception($"ClearShaderCache left {webgpu.CachedShaderCount} shader(s) cached.");

                // 4) THE USE-AFTER-DISPOSE GUARD: dispatch again. This must recompile and produce correct
                //    output. If the resolve cache had kept a reference to the disposed shader, this either
                //    throws inside WebGPU or silently writes garbage.
                k((Index1D)N, input.View, output.View, mul, add);
                await accelerator.SynchronizeAsync();
                var got = await output.CopyToHostAsync<float>();
                for (int i = 0; i < N; i++)
                {
                    float expected = src[i] * mul + add;
                    if (MathF.Abs(got[i] - expected) > MathF.Abs(expected) * 1e-5f + 1e-5f)
                        throw new Exception(
                            $"Post-clear re-dispatch wrong at element {i}: expected {expected} got {got[i]}. " +
                            $"A cache likely retained a reference to a disposed shader.");
                }
                if (webgpu.CachedShaderCount < 1)
                    throw new Exception("Post-clear dispatch should have recompiled and re-cached the shader.");
            }
            finally
            {
                WebGPUBackend.MaxCachedShaders = savedCap;
                webgpu.ClearShaderCache();
            }
        });

        [TestMethod]
        public async Task ShaderCache_LruCapBoundsGrowth_AndEvictedKernelStillCorrect() => await RunEmulatedTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only shader cache.");

            var savedCap = WebGPUBackend.MaxCachedShaders;
            WebGPUBackend.EnableShaderCaching = true;
            try
            {
                const int Cap = 2;
                const int N = 64;
                var src = new float[N];
                for (int i = 0; i < N; i++) src[i] = i * 0.5f + 1f;
                using var input = accelerator.Allocate1D(src);
                using var output = accelerator.Allocate1D<float>(N);

                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                    BindGroupCache_ScaleAddKernel);

                webgpu.ClearShaderCache();
                WebGPUBackend.MaxCachedShaders = Cap;

                // Distinct DISPATCH SIZES produce distinct cached shaders (the auto-grouped shader bakes in
                // _ilgpu_user_dim), so this mints more distinct entries than the cap allows.
                int[] sizes = { 8, 16, 32, 64 };
                foreach (var n in sizes)
                {
                    k((Index1D)n, input.View, output.View, 2f, 3f);
                    await accelerator.SynchronizeAsync();
                    if (webgpu.CachedShaderCount > Cap)
                        throw new Exception(
                            $"Shader cache exceeded MaxCachedShaders={Cap}: {webgpu.CachedShaderCount} after dispatching size {n}. " +
                            $"LRU eviction is not bounding growth.");
                }

                // NON-VACUITY: `count > Cap` alone would pass trivially on an EMPTY cache. Assert the cache
                // actually filled TO the cap - which simultaneously proves (a) entries were really being
                // added, (b) the four distinct dispatch sizes really did produce four distinct shaders (if
                // they collapsed to one, count would be 1 here), and therefore (c) eviction genuinely ran.
                if (webgpu.CachedShaderCount != Cap)
                    throw new Exception(
                        $"Expected the cache to sit exactly AT the cap ({Cap}) after dispatching {sizes.Length} " +
                        $"distinct sizes, got {webgpu.CachedShaderCount}. If 1, the sizes did not produce distinct " +
                        $"shaders and this test never exercised eviction at all; if 0, nothing was cached.");

                // The size-8 shader is the least-recently-used and must have been evicted + disposed by now.
                // Re-dispatching it has to recompile cleanly and produce correct output - the real proof that
                // eviction purged every dependent reference rather than leaving a disposed pipeline reachable.
                k((Index1D)8, input.View, output.View, 2f, 3f);
                await accelerator.SynchronizeAsync();
                var got = await output.CopyToHostAsync<float>();
                for (int i = 0; i < 8; i++)
                {
                    float expected = src[i] * 2f + 3f;
                    if (MathF.Abs(got[i] - expected) > MathF.Abs(expected) * 1e-5f + 1e-5f)
                        throw new Exception(
                            $"Re-dispatch after LRU eviction wrong at element {i}: expected {expected} got {got[i]}. " +
                            $"An evicted shader was likely still referenced by the resolve or bind-group cache.");
                }
                if (webgpu.CachedShaderCount > Cap)
                    throw new Exception($"Cache still over cap after re-dispatch: {webgpu.CachedShaderCount} > {Cap}.");
            }
            finally
            {
                WebGPUBackend.MaxCachedShaders = savedCap;
                webgpu.ClearShaderCache();
            }
        });
    }
}
