using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // The Length of a LocalMemory view: read in the kernel itself, inside an inlined helper taking the view (whole and as a
    // sub-view), and next to a byte LocalMemory array. A local array's view traces to no kernel parameter, and three
    // generators fell back on that (2026-11-09, found bisecting LocalViewHelpers_LocalMemoryThroughHelpers_MatchHost):
    // WebGPU emitted arrayLength(&v_arr) (an invalid shader), WebGL emitted 0 (a loop to Length ran zero times, or the draw
    // failed), Wasm passed 0 to a helper function ("remainder by zero").
    public abstract partial class BackendTestBase
    {
        static void LocalViewLengthIntKernel(Index1D i, ArrayView<int> output)
        {
            var cache = LocalMemory.Allocate<int>(LocalViewCacheSize);
            for (int k = 0; k < cache.IntLength; k++) cache[k] = (i * 7 + k * 3) % 5;
            output[i] = LocalViewCount(cache, 2) * 100 + LocalViewCount(cache.SubView(4, 12), 3);
        }

        static void LocalViewLengthByteKernel(Index1D i, ArrayView<int> output)
        {
            var ctx = LocalMemory.Allocate<byte>(8);
            for (int k = 0; k < 8; k++) ctx[k] = (byte)((int)i + k);
            int c = 0;
            for (int k = 0; k < 8; k++) c = c * 3 + ctx[k];
            output[i] = c;
        }

        static void LocalViewLengthNestedKernel(Index1D i, ArrayView<int> output)
        {
            var cache = LocalMemory.Allocate<int>(LocalViewCacheSize);
            var ctx = LocalMemory.Allocate<byte>(8);
            for (int k = 0; k < 8; k++) ctx[k] = (byte)((int)i + k);
            output[i] = LocalViewFill(cache, ctx, i);
        }

        async Task LocalViewLengthRun(Accelerator accelerator, Action<Index1D, ArrayView<int>> kernelMethod)
        {
            const int n = 64;
            using var dOut = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(kernelMethod);
            kernel((Index1D)n, dOut.View);
            var got = await dOut.CopyToHostAsync<int>();
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hOut = cpu.Allocate1D<int>(n);
            var cpuKernel = cpu.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(kernelMethod);
            cpuKernel((Index1D)n, hOut.View);
            cpu.Synchronize();
            var want = await hOut.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
                if (got[t] != want[t]) throw new Exception($"output[{t}] = {got[t]}, host {want[t]}");
        }

        [TestMethod] public async Task LocalMemory_ViewLength_InKernelAndHelper_MatchHost() => await RunTest(a => LocalViewLengthRun(a, LocalViewLengthIntKernel));
        [TestMethod] public async Task LocalMemory_ViewLength_ByteArray_MatchHost() => await RunTest(a => LocalViewLengthRun(a, LocalViewLengthByteKernel));
        [TestMethod] public async Task LocalMemory_ViewLength_NestedHelpers_MatchHost() => await RunTest(a => LocalViewLengthRun(a, LocalViewLengthNestedKernel));
    }
}
