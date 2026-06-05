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
    // CORRECTNESS rests entirely on the key capturing EVERY resolution input. The dangerous failure mode is
    // a wrong HIT: for auto-grouped kernels the resolved shader bakes in _ilgpu_user_dim = the dispatch size
    // (the range check that stops excess threads writing OOB). If the key missed userDim, a LARGER dispatch
    // would falsely reuse a SMALLER dispatch's shader and range-check out the extra elements, leaving them
    // unwritten — wrong output. This guard dispatches small THEN large and asserts (a) a re-dispatch HITS,
    // (b) a new size MISSES (resolves its own shader), and (c) all large-dispatch elements are correct.
    // WebGPU-only (the cache lives in the WebGPU dispatch path); the kernel is the cross-backend scale+add.
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task ShaderResolveCache_HitsAndDiscriminatesByDispatchSize() => await RunEmulatedTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only shader-resolution cache.");

            // Auto-grouped kernel: output[i] = input[i] * mul + add, with the implicit _ilgpu_user_dim
            // range check baked into the shader per dispatch size.
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

                // 1) Dispatch SMALL — first sight of (kernel, userDim=Small): a MISS that resolves + caches.
                k((Index1D)Small, input.View, output.View, mul, add);
                await accelerator.SynchronizeAsync();
                long missesAfterSmall = webgpu.ShaderResolveCacheMisses;
                long hitsBeforeReSmall = webgpu.ShaderResolveCacheHits;
                if (missesAfterSmall < 1)
                    throw new Exception($"ShaderResolveCache: first dispatch must MISS + cache, got misses={missesAfterSmall}.");

                // 2) Re-dispatch SMALL — same (kernel, size) must HIT (reuse the cached shader).
                k((Index1D)Small, input.View, output.View, mul, add);
                await accelerator.SynchronizeAsync();
                if (webgpu.ShaderResolveCacheHits <= hitsBeforeReSmall)
                    throw new Exception(
                        $"ShaderResolveCache: re-dispatch at the same (kernel, size) must HIT, but hits stayed {webgpu.ShaderResolveCacheHits}.");

                // 3) Dispatch LARGE — a DIFFERENT userDim. Must MISS (resolve its own shader), NOT falsely
                //    reuse the Small shader, and all Large elements must be written correctly.
                k((Index1D)Large, input.View, output.View, mul, add);
                await accelerator.SynchronizeAsync();
                var got = await output.CopyToHostAsync<float>();
                for (int i = 0; i < Large; i++)
                {
                    float expected = src[i] * mul + add;
                    if (MathF.Abs(got[i] - expected) > MathF.Abs(expected) * 1e-5f + 1e-5f)
                        throw new Exception(
                            $"ShaderResolveCache DISCRIMINATION FAILED at element {i} of the Large dispatch: " +
                            $"expected {expected} got {got[i]}. The key likely missed userDim — the Large dispatch " +
                            $"reused the Small (userDim={Small}) shader, range-checking out elements >= {Small}.");
                }
                if (webgpu.ShaderResolveCacheMisses <= missesAfterSmall)
                    throw new Exception(
                        $"ShaderResolveCache: a NEW dispatch size (userDim={Large}) must MISS + resolve its own " +
                        $"shader, but misses stayed {webgpu.ShaderResolveCacheMisses} — the key is not discriminating by userDim.");
            }
            finally
            {
                webgpu.ClearShaderResolveCache();
            }
        });
    }
}
