using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Where the time of a first kernel load goes (Lore, 2026-11-08: SpawnGen's page stopped answering for 30-104 s while
    // SpawnDev.Codecs loaded its kernels in a non-AOT browser build). A cold kernel (its own entry method, so no cache hit)
    // must run correctly; its load (IL -> IR -> shader source), first launch (adds pipeline creation) and second launch
    // (run only) are logged as "[KernelLoadTiming]" lines (PMT_CONSOLE_LOG=KernelLoadTiming).
    public abstract partial class BackendTestBase
    {
        static void KernelLoadTimingDeformConv(Index1D idx, ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<float, Stride1D.Dense> weights, ArrayView1D<float, Stride1D.Dense> offsets,
            ArrayView1D<float, Stride1D.Dense> mask, ArrayView1D<float, Stride1D.Dense> bias,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr) =>
            DeformConvProbeKernel(idx, input, weights, offsets, mask, bias, output, paramsArr);

        [TestMethod]
        public async Task KernelLoad_ColdLargeKernel_RunsCorrectly_Timed() => await RunTest(async accelerator =>
        {
            const int H = 5, W = 5, K = 3, outC = 2, inC = 2;
            int hw = H * W, outLen = outC * hw;
            var rng = new Random(11);
            var x = new float[inC * H * W];
            for (int i = 0; i < x.Length; i++) x[i] = (float)(rng.NextDouble() * 4 - 2);
            var wt = new float[outC * inC * K * K];
            for (int i = 0; i < wt.Length; i++) wt[i] = (float)(rng.NextDouble() * 2 - 1);
            var off = new float[2 * K * K * hw];
            for (int i = 0; i < off.Length; i++) off[i] = (float)(rng.NextDouble() * 1.6 - 0.8);
            var prm = new float[] { inC, H, W, outC, H, W, K, K, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0 };

            async Task<(float[] y, double loadMs, double firstMs, double secondMs)> Run(Accelerator acc)
            {
                using var xb = acc.Allocate1D(x);
                using var wb = acc.Allocate1D(wt);
                using var ob = acc.Allocate1D(off);
                using var mb = acc.Allocate1D(new float[] { 0f });
                using var bb = acc.Allocate1D(new float[] { 0f, 0f });
                using var yb = acc.Allocate1D<float>(outLen);
                using var pb = acc.Allocate1D(prm);
                var sw = Stopwatch.StartNew();
                var kernel = acc.LoadAutoGroupedStreamKernel<Index1D,
                    ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    ArrayView1D<float, Stride1D.Dense>>(KernelLoadTimingDeformConv);
                double loadMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                kernel((Index1D)outLen, xb.View, wb.View, ob.View, mb.View, bb.View, yb.View, pb.View);
                await acc.SynchronizeAsync();
                double firstMs = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                kernel((Index1D)outLen, xb.View, wb.View, ob.View, mb.View, bb.View, yb.View, pb.View);
                await acc.SynchronizeAsync();
                double secondMs = sw.Elapsed.TotalMilliseconds;
                return (await yb.CopyToHostAsync<float>(), loadMs, firstMs, secondMs);
            }

            var (got, loadMs, firstMs, secondMs) = await Run(accelerator);
            Console.WriteLine($"[KernelLoadTiming] {accelerator.AcceleratorType} {accelerator.Name}: load (IL -> shader source) {loadMs:F1} ms, " +
                $"first launch (+ pipeline) {firstMs:F1} ms, second launch {secondMs:F1} ms");

            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            var (want, _, _, _) = await Run(cpu);
            for (int i = 0; i < outLen; i++)
                if (MathF.Abs(got[i] - want[i]) > MathF.Abs(want[i]) * 1e-5f + 1e-5f)
                    throw new Exception($"[{i}] cpu {want[i]:R} got {got[i]:R}");
        });

        // Cold copies for the async-load tests (each test must compile its kernel itself, not hit a cache).
        static void KernelLoadAsyncDeformConvA(Index1D idx, ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<float, Stride1D.Dense> weights, ArrayView1D<float, Stride1D.Dense> offsets,
            ArrayView1D<float, Stride1D.Dense> mask, ArrayView1D<float, Stride1D.Dense> bias,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr) =>
            DeformConvProbeKernel(idx, input, weights, offsets, mask, bias, output, paramsArr);
        static void KernelLoadAsyncDeformConvB(Index1D idx, ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<float, Stride1D.Dense> weights, ArrayView1D<float, Stride1D.Dense> offsets,
            ArrayView1D<float, Stride1D.Dense> mask, ArrayView1D<float, Stride1D.Dense> bias,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr) =>
            DeformConvProbeKernel(idx, input, weights, offsets, mask, bias, output, paramsArr);

        static (float[] x, float[] wt, float[] off, float[] prm, int outLen) KernelLoadInputs()
        {
            const int H = 5, W = 5, K = 3, outC = 2, inC = 2;
            var rng = new Random(11);
            var x = new float[inC * H * W];
            for (int i = 0; i < x.Length; i++) x[i] = (float)(rng.NextDouble() * 4 - 2);
            var wt = new float[outC * inC * K * K];
            for (int i = 0; i < wt.Length; i++) wt[i] = (float)(rng.NextDouble() * 2 - 1);
            var off = new float[2 * K * K * H * W];
            for (int i = 0; i < off.Length; i++) off[i] = (float)(rng.NextDouble() * 1.6 - 0.8);
            var prm = new float[] { inC, H, W, outC, H, W, K, K, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0 };
            return (x, wt, off, prm, outC * H * W);
        }

        // LoadAutoGroupedStreamKernelAsync returns the same working launcher as the synchronous loader, on every backend. On
        // WebGPU the pipeline is built during the load: the load adds exactly one shader to the cache and the first launch adds
        // none (it would add one if the launch still created the pipeline itself).
        [TestMethod]
        public async Task KernelLoadAsync_LauncherMatchesCpu() => await RunTest(async accelerator =>
        {
            var (x, wt, off, prm, outLen) = KernelLoadInputs();
            var webgpu = accelerator as global::SpawnDev.ILGPU.WebGPU.WebGPUAccelerator;
            // The suite caps the shader cache (TestShaderCacheCap): at the cap a new shader evicts one and the count
            // cannot rise (5.4.19 sweep: 64 / 64 / 64). Unlimited while this test counts.
            var savedCap = global::SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.MaxCachedShaders;
            global::SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.MaxCachedShaders = 0;
            float[] got;
            try
            {
            int shadersBefore = webgpu?.CachedShaderCount ?? 0;
            var kernel = await accelerator.LoadAutoGroupedStreamKernelAsync<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>>(KernelLoadAsyncDeformConvA);
            int shadersAfterLoad = webgpu?.CachedShaderCount ?? 0;
            using var xb = accelerator.Allocate1D(x);
            using var wb = accelerator.Allocate1D(wt);
            using var ob = accelerator.Allocate1D(off);
            using var mb = accelerator.Allocate1D(new float[] { 0f });
            using var bb = accelerator.Allocate1D(new float[] { 0f, 0f });
            using var yb = accelerator.Allocate1D<float>(outLen);
            using var pb = accelerator.Allocate1D(prm);
            kernel((Index1D)outLen, xb.View, wb.View, ob.View, mb.View, bb.View, yb.View, pb.View);
            await accelerator.SynchronizeAsync();
            int shadersAfterLaunch = webgpu?.CachedShaderCount ?? 0;
            got = await yb.CopyToHostAsync<float>();
            if (webgpu != null && (shadersAfterLoad != shadersBefore + 1 || shadersAfterLaunch != shadersAfterLoad))
                throw new Exception($"WebGPU shader cache: {shadersBefore} before the load, {shadersAfterLoad} after it (want +1), " +
                    $"{shadersAfterLaunch} after the first launch (want unchanged)");
            }
            finally { global::SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.MaxCachedShaders = savedCap; }

            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            var cpuKernel = cpu.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>>(KernelLoadAsyncDeformConvA);
            using var cx = cpu.Allocate1D(x);
            using var cw = cpu.Allocate1D(wt);
            using var co = cpu.Allocate1D(off);
            using var cm = cpu.Allocate1D(new float[] { 0f });
            using var cb = cpu.Allocate1D(new float[] { 0f, 0f });
            using var cy = cpu.Allocate1D<float>(outLen);
            using var cp = cpu.Allocate1D(prm);
            cpuKernel((Index1D)outLen, cx.View, cw.View, co.View, cm.View, cb.View, cy.View, cp.View);
            await cpu.SynchronizeAsync();
            var want = await cy.CopyToHostAsync<float>();
            for (int i = 0; i < outLen; i++)
                if (MathF.Abs(got[i] - want[i]) > MathF.Abs(want[i]) * 1e-5f + 1e-5f)
                    throw new Exception($"[{i}] cpu {want[i]:R} got {got[i]:R}");
        });

        // Browser backends: the async load returns to the browser's event loop BEFORE the synchronous IL compile. Observed by
        // ORDER: a JS setTimeout(0) registered just before the load runs at the first moment the page's event loop is free
        // and records Accelerator.CompiledKernelCount there. If the load yielded before compiling, the count is still the
        // one from before the load; if it compiled first, it is one more. (Timing the browser task was not usable: the
        // Wasm lane's event loop has a backlog of worker messages, and WebGL's second load of a kernel costs ~11 ms by
        // itself, which looked like a compile in a red-check.)
        [TestMethod]
        public async Task KernelLoadAsync_YieldsToBrowserBeforeCompile() => await RunTest(async accelerator =>
        {
            if (!OperatingSystem.IsBrowser())
                throw new UnsupportedTestException("Browser event-loop yield (browser backends only).");
            long atBrowserTask = -1;
            long before = accelerator.CompiledKernelCount;
            using var window = global::SpawnDev.SpawnJS.SpawnJSRuntime.Instance.Get<global::SpawnDev.SpawnJS.JSObjects.Window>("window");
            window.SetTimeout(() => atBrowserTask = accelerator.CompiledKernelCount, 0);
            var sw = Stopwatch.StartNew();
            var kernel = await accelerator.LoadAutoGroupedStreamKernelAsync<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>>(KernelLoadAsyncDeformConvB);
            double loaded = sw.Elapsed.TotalMilliseconds;
            long after = accelerator.CompiledKernelCount;
            while (atBrowserTask < 0) await Task.Delay(1);
            Console.WriteLine($"[KernelLoadTiming] async {accelerator.AcceleratorType}: compiles before {before}, at the browser task " +
                $"{atBrowserTask}, after the load {after}; async load {loaded:F1} ms");
            if (after != before + 1)
                throw new Exception($"the async load compiled {after - before} kernels, expected 1 (a cold kernel)");
            if (atBrowserTask != before)
                throw new Exception($"the browser task ran AFTER the compile (count {atBrowserTask}, {before} before the load): " +
                    "the async load did not yield before compiling");
        });
    }
}
