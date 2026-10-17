using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // WebGPUAccelerator.PrecompileAsync / PrecompileAutoGroupedAsync: build pipelines in the background before the first launch.
    // The property that matters is that the first launch FINDS the precompiled shader: dispatch looks shaders up by their exact WGSL
    // text, so a precompile that resolved a different source (e.g. without the @workgroup_size patch a KernelConfig launch applies)
    // would compile a second pipeline at the first launch and the precompile would have been wasted work. The test asserts the cache
    // does NOT grow at the first launches, and checks real output.
    public abstract partial class BackendTestBase
    {
        private static void Precompile_GroupedKernel(ArrayView<float> input, ArrayView<float> output, float mul)
        {
            int i = Grid.GlobalIndex.X;
            if (i < output.Length) output[i] = input[i] * mul + Group.IdxX;
        }

        [TestMethod]
        public async Task ShaderPrecompile_FirstLaunchUsesPrecompiledPipeline() => await RunEmulatedTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only (pipeline precompilation).");

            var savedCap = WebGPUBackend.MaxCachedShaders;
            WebGPUBackend.MaxCachedShaders = 0;
            WebGPUBackend.EnableShaderCaching = true;
            try
            {
                webgpu.ClearShaderCache();
                const int N = 1000, Group = 128;   // 128 differs from the compiled default group size: the launch patches @workgroup_size
                await System.Threading.Tasks.Task.WhenAll(
                    webgpu.PrecompileAsync(Precompile_GroupedKernel, new Index3D(Group, 1, 1)),
                    webgpu.PrecompileAutoGroupedAsync(BindGroupCache_ScaleAddKernel));
                int precompiled = webgpu.CachedShaderCount;
                if (precompiled != 2) throw new Exception($"Expected the two precompiles to cache 2 shaders, CachedShaderCount={precompiled}.");

                var src = new float[N];
                for (int i = 0; i < N; i++) src[i] = i * 0.5f - 7f;
                using var input = accelerator.Allocate1D(src);
                using var output = accelerator.Allocate1D<float>(N);
                using var output2 = accelerator.Allocate1D<float>(N);

                var grouped = accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>, float>(Precompile_GroupedKernel);
                grouped(new KernelConfig((N + Group - 1) / Group, Group), input.View, output.View, 3f);
                var auto = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(BindGroupCache_ScaleAddKernel);
                auto((Index1D)N, input.View, output2.View, 2f, 1f);
                await accelerator.SynchronizeAsync();

                if (webgpu.CachedShaderCount != precompiled)
                    throw new Exception($"The first launches compiled new shaders ({precompiled} -> {webgpu.CachedShaderCount}): the precompiled source did not match the launch's.");

                var got = await output.CopyToHostAsync<float>();
                var got2 = await output2.CopyToHostAsync<float>();
                for (int i = 0; i < N; i++)
                {
                    float want = src[i] * 3f + (i % Group);
                    if (Math.Abs(got[i] - want) > 1e-5f) throw new Exception($"grouped kernel [{i}] = {got[i]}, want {want}");
                    float want2 = src[i] * 2f + 1f;
                    if (Math.Abs(got2[i] - want2) > 1e-5f) throw new Exception($"auto-grouped kernel [{i}] = {got2[i]}, want {want2}");
                }
            }
            finally
            {
                WebGPUBackend.MaxCachedShaders = savedCap;
                webgpu.ClearShaderCache();
            }
        });
    }
}
