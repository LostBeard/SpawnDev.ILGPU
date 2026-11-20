using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part: releasing the context's compiler caches while the context lives.
    //
    // A Context keeps the IR and type information of every method it has compiled, for its whole life. In a browser page the
    // context lives as long as the page, so that grows with every kernel loaded: SpawnDev.Codecs' lossy WebP encoder (about 20
    // kernels) leaves +20.1 MiB on Wasm / +28.4 MiB on WebGPU, of which Context.ClearCache(ClearCacheMode.Default) frees 16.1 /
    // 19.6 MiB (ClearCacheMode.Everything frees nothing more; the launcher assembly nothing). Measured 2026-11-20.
    // This test checks the two things releasing it must not break, on every lane: kernels loaded before the release keep
    // running, and kernels loaded after it compile (re-importing what they share with the earlier ones). It reports the load
    // time of a kernel after a release against one with the cache warm: that re-import is what a release costs.
    public abstract partial class BackendTestBase
    {
        // a helper big enough that importing it again costs something measurable, shared by the three kernels below
        static int CacheRelease_Mix(int x, int salt)
        {
            int h = x * 73856093 ^ salt * 19349663;
            for (int r = 0; r < 4; r++)
            {
                h ^= h << 13; h ^= (int)((uint)h >> 17); h ^= h << 5;
                h += (r * 83492791) ^ (x >> r);
                h = (h & 1) != 0 ? h * 3 + salt : (h >> 1) ^ (salt << 2);
            }
            return h;
        }

        static void CacheRelease_KernelA(Index1D i, ArrayView<int> o) { o[i] = CacheRelease_Mix(i, 1); }
        static void CacheRelease_KernelB(Index1D i, ArrayView<int> o) { o[i] = CacheRelease_Mix(i, 2) + 7; }
        static void CacheRelease_KernelC(Index1D i, ArrayView<int> o) { o[i] = CacheRelease_Mix(i, 3) ^ 0x5A5A; }

        [TestMethod(Timeout = 180000)]
        public async Task Context_ClearCacheDefault_LoadedKernelsKeepRunning_NewOnesCompile() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            using var buf = accelerator.Allocate1D<int>(n);
            async Task Check(string what, Action<Index1D, ArrayView<int>> kernel, Func<int, int> expected)
            {
                kernel(n, buf.View);
                await accelerator.SynchronizeAsync();
                var got = await buf.CopyToHostAsync();
                for (int i = 0; i < n; i++)
                    if (got[i] != expected(i))
                        throw new Exception($"{accelerator.AcceleratorType}: {what}: element {i} is {got[i]}, expected {expected(i)}");
            }

            var a = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_KernelA);
            await Check("A", a, i => CacheRelease_Mix(i, 1));

            // a sibling with the cache warm (it shares CacheRelease_Mix with A)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var b = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_KernelB);
            double warmMs = sw.Elapsed.TotalMilliseconds;
            await Check("B (cache warm)", b, i => CacheRelease_Mix(i, 2) + 7);

            accelerator.Context.ClearCache(ClearCacheMode.Default);

            // kernels loaded before the release keep running
            await Check("A after ClearCache(Default)", a, i => CacheRelease_Mix(i, 1));
            await Check("B after ClearCache(Default)", b, i => CacheRelease_Mix(i, 2) + 7);

            // a kernel loaded after it compiles (re-importing the shared helper)
            sw.Restart();
            var c = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_KernelC);
            double afterMs = sw.Elapsed.TotalMilliseconds;
            await Check("C (loaded after ClearCache(Default))", c, i => CacheRelease_Mix(i, 3) ^ 0x5A5A);

            Console.WriteLine($"[report] {accelerator.AcceleratorType} ContextCacheRelease: sibling kernel load {warmMs:F1} ms with the cache warm, {afterMs:F1} ms after ClearCache(Default)");
        });
    }
}
