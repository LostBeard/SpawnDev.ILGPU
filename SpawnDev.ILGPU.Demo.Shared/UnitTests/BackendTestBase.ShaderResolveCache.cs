using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Shader-resolution cache (WebGPU): WebGPUBackend.EnableShaderResolveCache (default ON) caches the
    // resolved compute shader per (compiled-kernel identity + dispatch-config signature), so re-dispatching
    // the same kernel at the same config skips GetOrCreateComputeShader + its O(WGSL-length) content hash.
    //
    // The auto-grouped range check reads the dispatch's user dimension from _scalar_params[0]
    // (ScalarPackingEntry.IsUserDim). It used to be a pipeline `override` baked into the resolved shader, so
    // every distinct dispatch size compiled a NEW pipeline - MEASURED 2026-09-27 ~400 ms per batch for
    // SpawnScene's GPU RANSAC, whose batch sizes all differ. Now ONE shader serves every size, and the danger
    // is a STALE dimension: if a larger dispatch saw a smaller one's userDim, the range check would drop its
    // extra elements and leave them unwritten. This guard dispatches small, then large (must HIT - no new
    // shader - and write every element), then small + large back to back in ONE batch with no sync between
    // (each must see its own dimension). WebGPU-only (the cache lives in the WebGPU dispatch path).
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task ShaderResolveCache_OneShaderServesEveryDispatchSize() => await RunEmulatedTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only shader-resolution cache.");

            // Auto-grouped kernel: output[i] = input[i] * mul + add, range-checked against the user dimension.
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                BindGroupCache_ScaleAddKernel);

            WebGPUBackend.EnableShaderResolveCache = true; // default; set explicitly so the test is self-contained
            webgpu.ClearShaderResolveCache();
            try
            {
                const int Small = 64, Large = 256;
                const float mul = 2f, add = 3f;
                var src = new float[Large];
                for (int i = 0; i < Large; i++) src[i] = i * 0.5f - 1f;
                using var input = accelerator.Allocate1D(src);
                using var output = accelerator.Allocate1D<float>(Large);

                async Task CheckAsync(MemoryBuffer1D<float, Stride1D.Dense> buf, int written, string what)
                {
                    var got = await buf.CopyToHostAsync<float>();
                    for (int i = 0; i < Large; i++)
                    {
                        float expected = i < written ? src[i] * mul + add : 0f;
                        if (MathF.Abs(got[i] - expected) > MathF.Abs(expected) * 1e-5f + 1e-5f)
                            throw new Exception(
                                $"{what}: element {i} expected {expected} got {got[i]} (dispatch size {written}). " +
                                (i < written ? "A stale (smaller) user dimension range-checked it out."
                                             : "A stale (larger) user dimension let an excess thread write."));
                    }
                }

                // 1) SMALL - first sight of the kernel: a MISS that resolves + caches.
                k((Index1D)Small, input.View, output.View, mul, add);
                await accelerator.SynchronizeAsync();
                long missesAfterSmall = webgpu.ShaderResolveCacheMisses;
                if (missesAfterSmall < 1)
                    throw new Exception($"ShaderResolveCache: first dispatch must MISS + cache, got misses={missesAfterSmall}.");
                await CheckAsync(output, Small, "Small dispatch");

                // 2) LARGE - a different size must HIT the same shader (no new pipeline) and write every element.
                long hitsBeforeLarge = webgpu.ShaderResolveCacheHits;
                k((Index1D)Large, input.View, output.View, mul, add);
                await accelerator.SynchronizeAsync();
                if (webgpu.ShaderResolveCacheMisses != missesAfterSmall || webgpu.ShaderResolveCacheHits <= hitsBeforeLarge)
                    throw new Exception(
                        $"ShaderResolveCache: a new dispatch size must reuse the cached shader (misses {missesAfterSmall} -> " +
                        $"{webgpu.ShaderResolveCacheMisses}, hits {hitsBeforeLarge} -> {webgpu.ShaderResolveCacheHits}) - " +
                        "the user dimension is keying the shader again (one compile per dispatch size).");
                await CheckAsync(output, Large, "Large dispatch");

                // 3) SMALL then LARGE into separate outputs in ONE batch (no sync between): each dispatch must
                //    see its own user dimension, i.e. it travels with the dispatch, not with shared state.
                using var outA = accelerator.Allocate1D<float>(Large);
                using var outB = accelerator.Allocate1D<float>(Large);
                outA.MemSetToZero(); outB.MemSetToZero();
                k((Index1D)Small, input.View, outA.View, mul, add);
                k((Index1D)Large, input.View, outB.View, mul, add);
                await accelerator.SynchronizeAsync();
                await CheckAsync(outA, Small, "Batched small dispatch");
                await CheckAsync(outB, Large, "Batched large dispatch");
                if (webgpu.ShaderResolveCacheMisses != missesAfterSmall)
                    throw new Exception($"ShaderResolveCache: batched dispatches minted new shaders (misses {webgpu.ShaderResolveCacheMisses}).");
            }
            finally
            {
                webgpu.ClearShaderResolveCache();
            }
        });
    }
}
