using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Accelerator.LoadedKernelCount counts the kernels an accelerator loads that its own kernel cache did not hold:
    // compiled, OR rebuilt from SpawnDev.ILGPU's process-wide shader-artifact cache (browser backends), where
    // CompiledKernelCount does not move. A warm-up check ("this work loads no kernel") built on CompiledKernelCount passes
    // or fails depending on what else ran in the process (SpawnDev.Codecs' FLAC warm-up guard, Geordi 2026-11-11); built
    // on LoadedKernelCount it does not.
    public abstract partial class BackendTestBase
    {
        static void LoadedCountKernel(Index1D i, ArrayView<int> o, int k) => o[i] = i * k + 0x1C0DE;

        [TestMethod]
        public async Task LoadedKernelCount_CountsLoadsIncludingArtifactHits() => await RunTest(async accelerator =>
        {
            // start from an accelerator cache without this kernel (another test may have loaded it)
            accelerator.ClearCache(ClearCacheMode.Everything);
            long loaded0 = accelerator.LoadedKernelCount, compiled0 = accelerator.CompiledKernelCount;
            var first = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(LoadedCountKernel);
            long loaded1 = accelerator.LoadedKernelCount;
            if (loaded1 - loaded0 != 1)
                throw new Exception($"loading a kernel the accelerator did not hold: LoadedKernelCount +{loaded1 - loaded0}, expected +1");

            // the accelerator's own cache serves the same kernel again: no load
            var again = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(LoadedCountKernel);
            long loaded2 = accelerator.LoadedKernelCount;
            if (loaded2 != loaded1)
                throw new Exception($"reloading a kernel the accelerator holds: LoadedKernelCount +{loaded2 - loaded1}, expected +0");

            // its cache dropped: loaded again (+1). On the browser backends this load is a shader-artifact hit, so the
            // compiler does not run, and CompiledKernelCount must not have moved more than LoadedKernelCount.
            accelerator.ClearCache(ClearCacheMode.Everything);
            long compiledBefore = accelerator.CompiledKernelCount;
            var third = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(LoadedCountKernel);
            long loaded3 = accelerator.LoadedKernelCount, compiled3 = accelerator.CompiledKernelCount;
            if (loaded3 - loaded2 != 1)
                throw new Exception($"loading after ClearCache: LoadedKernelCount +{loaded3 - loaded2}, expected +1");
            if (compiled3 - compiledBefore > 1 || compiled3 - compiled0 > loaded3 - loaded0)
                throw new Exception($"CompiledKernelCount moved more than LoadedKernelCount (compiled +{compiled3 - compiled0}, loaded +{loaded3 - loaded0})");

            // and the kernel works
            using var o = accelerator.Allocate1D<int>(64);
            third((Index1D)64, o.View, 3);
            var got = await o.CopyToHostAsync<int>();
            for (int i = 0; i < 64; i++)
                if (got[i] != i * 3 + 0x1C0DE) throw new Exception($"o[{i}] = {got[i]}, expected {i * 3 + 0x1C0DE}");
            GC.KeepAlive(first);
            GC.KeepAlive(again);
        });
    }
}
