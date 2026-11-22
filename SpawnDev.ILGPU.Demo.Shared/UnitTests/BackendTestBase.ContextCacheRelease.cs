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
    // running, and kernels loaded after it compile (re-importing what they share with the earlier ones), through the public
    // accelerator.ReleaseCompilerCaches() (5.4.44). It reports the load
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

            accelerator.ReleaseCompilerCaches();   // = accelerator.Context.ClearCache(ClearCacheMode.Default)

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

        // A SpecializedValue kernel keeps the IR its load built and compiles each NEW value from it at launch
        // (SpecializationCache.SpecializeKernel). That IR was built before the release; a value first launched after it must
        // still compile and run its own path. Reuses SpecializedBranchKernel / SpecializedBranchHost.
        [TestMethod(Timeout = 180000)]
        public async Task Context_ClearCacheDefault_SpecializedKernelCompilesNewValuesAfter() => await RunTest(async accelerator =>
        {
            const int n = 1024;
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = i * 37 - 5000;
            using var x = accelerator.Allocate1D(input);
            using var y = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, SpecializedValue<int>, ArrayView<int>, ArrayView<int>>(SpecializedBranchKernel);
            async Task Check(string when, int op)
            {
                kernel(n, new SpecializedValue<int>(op), x.View, y.View);
                var got = await y.CopyToHostAsync<int>();
                for (int i = 0; i < n; i++)
                    if (got[i] != SpecializedBranchHost(op, input[i]))
                        throw new Exception($"{accelerator.AcceleratorType}: op {op} {when}: x = {input[i]}: got {got[i]}, want {SpecializedBranchHost(op, input[i])}");
            }
            await Check("before the release", 0);
            accelerator.ReleaseCompilerCaches();
            await Check("first launched after the release", 2);
            await Check("compiled before the release", 0);
            accelerator.ReleaseCompilerCaches();
            await Check("first launched after a second release", 1);
        });

        // Distinct kernel METHODS (one per salt type), so every load below really compiles: a reload of one method is served
        // from the accelerator's kernel cache without touching the compiler.
        public interface ICacheReleaseSalt { int Salt { get; } }
        public struct CacheReleaseSalt00 : ICacheReleaseSalt { public int Salt => 100; }
        public struct CacheReleaseSalt01 : ICacheReleaseSalt { public int Salt => 101; }
        public struct CacheReleaseSalt02 : ICacheReleaseSalt { public int Salt => 102; }
        public struct CacheReleaseSalt03 : ICacheReleaseSalt { public int Salt => 103; }
        public struct CacheReleaseSalt04 : ICacheReleaseSalt { public int Salt => 104; }
        public struct CacheReleaseSalt05 : ICacheReleaseSalt { public int Salt => 105; }
        public struct CacheReleaseSalt06 : ICacheReleaseSalt { public int Salt => 106; }
        public struct CacheReleaseSalt07 : ICacheReleaseSalt { public int Salt => 107; }
        public struct CacheReleaseSalt08 : ICacheReleaseSalt { public int Salt => 108; }
        public struct CacheReleaseSalt09 : ICacheReleaseSalt { public int Salt => 109; }
        public struct CacheReleaseSalt10 : ICacheReleaseSalt { public int Salt => 110; }
        public struct CacheReleaseSalt11 : ICacheReleaseSalt { public int Salt => 111; }
        public struct CacheReleaseSalt12 : ICacheReleaseSalt { public int Salt => 112; }
        public struct CacheReleaseSalt13 : ICacheReleaseSalt { public int Salt => 113; }
        public struct CacheReleaseSalt14 : ICacheReleaseSalt { public int Salt => 114; }
        public struct CacheReleaseSalt15 : ICacheReleaseSalt { public int Salt => 115; }

        static void CacheRelease_SaltKernel<TSalt>(Index1D i, ArrayView<int> o) where TSalt : struct, ICacheReleaseSalt =>
            o[i] = CacheRelease_Mix(i, default(TSalt).Salt) - default(TSalt).Salt;

        // ReleaseCompilerCaches from one thread while another thread loads kernels on the same accelerator (SpawnDev.Codecs:
        // one codec's warm-up releases at the end of its loads while another codec's warm-up is still compiling). Before
        // 5.4.44's compile lock nothing excluded the two: codeGenerationSemaphore covers only the frontend phase, and the
        // release emptied the IR methods, the type unification and the CPU argument mapper under a live compile (Geordi,
        // 2026-11-22, from source). Every kernel loaded during the release storm must compile and compute the right values.
        // Browsers run .NET on one thread: there is no second thread to race, so the browser lanes do not apply.
        [TestMethod(Timeout = 300000)]
        public async Task Context_ClearCacheDefault_WhileAnotherThreadLoadsKernels_EveryKernelCorrect() => await RunTest(async accelerator =>
        {
            if (OperatingSystem.IsBrowser())
                throw new UnsupportedTestException("browser .NET runs on one thread: a release cannot overlap a load");
            var loaders = new Func<Action<Index1D, ArrayView<int>>>[]
            {
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt00>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt01>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt02>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt03>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt04>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt05>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt06>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt07>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt08>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt09>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt10>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt11>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt12>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt13>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt14>),
                () => accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(CacheRelease_SaltKernel<CacheReleaseSalt15>),
            };
            var kernels = new Action<Index1D, ArrayView<int>>[loaders.Length];
            int releases = 0;
            using var loadsDone = new System.Threading.ManualResetEventSlim(false);
            // the releaser runs until every load has returned, so each compile overlaps releases. Thread.Yield between them
            // lets a waiting load in (the lock prefers a waiting writer); Thread.Sleep(1) was too coarse (~15 ms on Windows):
            // the CPU / Velocity compiles (~3 ms) finished 16 loads within 6 releases
            var releaser = Task.Factory.StartNew(() =>
            {
                while (!loadsDone.IsSet) { accelerator.ReleaseCompilerCaches(); releases++; System.Threading.Thread.Yield(); }
            }, TaskCreationOptions.LongRunning);
            var loader = Task.Factory.StartNew(() =>
            {
                try { for (int k = 0; k < loaders.Length; k++) kernels[k] = loaders[k](); }
                finally { loadsDone.Set(); }
            }, TaskCreationOptions.LongRunning);
            Exception? loadError = null, releaseError = null;
            try { await loader; } catch (Exception e) { loadError = e; }
            try { await releaser; } catch (Exception e) { releaseError = e; }
            if (loadError != null) throw new Exception($"{accelerator.AcceleratorType}: a kernel load failed while another thread released the compiler caches ({releases} releases): {loadError}");
            if (releaseError != null) throw new Exception($"{accelerator.AcceleratorType}: ReleaseCompilerCaches failed while another thread loaded kernels: {releaseError}");
            if (releases < loaders.Length)
                throw new Exception($"{accelerator.AcceleratorType}: only {releases} releases ran during {loaders.Length} loads; the test did not overlap them");

            const int n = 4096;
            using var buf = accelerator.Allocate1D<int>(n);
            for (int k = 0; k < kernels.Length; k++)
            {
                int salt = 100 + k;
                kernels[k](n, buf.View);
                await accelerator.SynchronizeAsync();
                var got = await buf.CopyToHostAsync();
                for (int i = 0; i < n; i++)
                    if (got[i] != CacheRelease_Mix(i, salt) - salt)
                        throw new Exception($"{accelerator.AcceleratorType}: kernel {k} (loaded during the releases): element {i} is {got[i]}, expected {CacheRelease_Mix(i, salt) - salt}");
            }
            Console.WriteLine($"[report] {accelerator.AcceleratorType} ReleaseCompilerCaches concurrent: {loaders.Length} loads overlapped {releases} releases, all correct");
        });
    }
}
