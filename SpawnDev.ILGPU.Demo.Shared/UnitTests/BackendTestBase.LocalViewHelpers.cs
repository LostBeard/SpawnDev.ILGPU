using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // [NoInlining] helpers taking views of a kernel's LocalMemory arrays - SpawnDev.Codecs' VP9 parse with its per-thread
    // token cache / contexts in LocalMemory (Geordi's S3, Codecs 31a1073). On WebGPU (5.4.23) such a helper is a function
    // with a ptr<function, array<T, N>> parameter plus an element offset, instead of a copy at every call site. Covered:
    // the whole array and sub-views of it, a helper handing its local view on (the inner one reads its Length), stores
    // through the helper, an early return from a loop, and a byte LocalMemory array next to an int one.
    public abstract partial class BackendTestBase
    {
        const int LocalViewCacheSize = 24;

        // counts value hits; leaves early at the first negative entry
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int LocalViewCount(ArrayView<int> cache, int value)
        {
            int hits = 0;
            for (int k = 0; k < cache.IntLength; k++)
            {
                if (cache[k] < 0)
                    return -hits - 1;
                if (cache[k] == value)
                    hits++;
            }
            return hits;
        }

        // writes through the view; hands it on whole and as a sub-view
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int LocalViewFill(ArrayView<int> cache, ArrayView<byte> ctx, int seed)
        {
            for (int k = 0; k < cache.IntLength; k++)
            {
                cache[k] = (seed * 7 + k * 3) % 5;
                ctx[k % ctx.IntLength] = (byte)(ctx[k % ctx.IntLength] + cache[k]);
            }
            if ((seed & 3) == 0)
                cache[seed % cache.IntLength] = -1;
            return LocalViewCount(cache, 2) * 100 + LocalViewCount(cache.SubView(4, 12), 3);
        }

        static void LocalViewHelpersKernel(Index1D i, ArrayView<int> output)
        {
            var cache = LocalMemory.Allocate<int>(LocalViewCacheSize);
            var ctx = LocalMemory.Allocate<byte>(8);
            for (int k = 0; k < 8; k++) ctx[k] = (byte)((int)i + k);
            int a = LocalViewFill(cache, ctx, i);
            int b = LocalViewCount(cache.SubView(2, 10), 1);
            int b2 = LocalViewFill(cache.SubView(8, 16), ctx.SubView(2, 4), i + 5);
            int c = 0;
            for (int k = 0; k < 8; k++) c = c * 3 + ctx[k];
            output[i] = a * 7 + b * 13 + b2 * 31 + c;
        }

        [TestMethod]
        public async Task LocalViewHelpers_LocalMemoryThroughHelpers_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = 64;
            using var dOut = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(LocalViewHelpersKernel);
            kernel((Index1D)n, dOut.View);
            var got = await dOut.CopyToHostAsync<int>();

            // host oracle: the kernel on the CPU accelerator (LocalMemory needs a kernel context there)
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hOut = cpu.Allocate1D<int>(n);
            var cpuKernel = cpu.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(LocalViewHelpersKernel);
            cpuKernel((Index1D)n, hOut.View);
            cpu.Synchronize();
            var want = await hOut.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
                if (got[t] != want[t]) throw new Exception($"output[{t}] = {got[t]}, host {want[t]}");
        });
    }
}
