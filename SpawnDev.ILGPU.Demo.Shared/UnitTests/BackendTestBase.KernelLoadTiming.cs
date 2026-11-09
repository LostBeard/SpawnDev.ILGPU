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
            var got = await yb.CopyToHostAsync<float>();
            if (webgpu != null && (shadersAfterLoad != shadersBefore + 1 || shadersAfterLaunch != shadersAfterLoad))
                throw new Exception($"WebGPU shader cache: {shadersBefore} before the load, {shadersAfterLoad} after it (want +1), " +
                    $"{shadersAfterLaunch} after the first launch (want unchanged)");

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

        // Browser backends: the async load returns to the browser's event loop BEFORE the synchronous IL compile. Observed
        // by ORDER, not time: a JS setTimeout(0) registered just before the load runs at the first moment the page's event
        // loop is free, and there loads the SAME kernel synchronously. If the async load yielded before compiling, that
        // load is the one that compiles (tens to hundreds of ms); if the async load compiled first, it is a kernel-cache hit
        // (well under a ms). Time ratios were not usable: in the Wasm lane the event loop has a backlog of worker messages
        // from earlier dispatches, so even a yielding load saw the JS timer late (83.8 ms of a 118 ms load).
        [TestMethod]
        public async Task KernelLoadAsync_YieldsToBrowserBeforeCompile() => await RunTest(async accelerator =>
        {
            if (!OperatingSystem.IsBrowser())
                throw new UnsupportedTestException("Browser event-loop yield (browser backends only).");
            double callbackLoadMs = -1;
            using var window = global::SpawnDev.SpawnJS.SpawnJSRuntime.Instance.Get<global::SpawnDev.SpawnJS.JSObjects.Window>("window");
            window.SetTimeout(() =>
            {
                var t = Stopwatch.StartNew();
                accelerator.LoadAutoGroupedStreamKernel<Index1D,
                    ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    ArrayView1D<float, Stride1D.Dense>>(KernelLoadAsyncDeformConvB);
                callbackLoadMs = t.Elapsed.TotalMilliseconds;
            }, 0);
            var sw = Stopwatch.StartNew();
            var kernel = await accelerator.LoadAutoGroupedStreamKernelAsync<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>>(KernelLoadAsyncDeformConvB);
            double loaded = sw.Elapsed.TotalMilliseconds;
            while (callbackLoadMs < 0) await Task.Delay(1);
            // the same load again, now certainly cached: what a cache hit costs here
            var t2 = Stopwatch.StartNew();
            accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>>(KernelLoadAsyncDeformConvB);
            double cachedMs = t2.Elapsed.TotalMilliseconds;
            Console.WriteLine($"[KernelLoadTiming] async {accelerator.AcceleratorType}: load in the JS task {callbackLoadMs:F1} ms, " +
                $"cache hit {cachedMs:F2} ms, async load {loaded:F1} ms");
            // The JS task's load must be the COMPILE: far above a cache hit, and most of the async load's wall time (it ran
            // inside it). Measured: 69-88% when the load yields; a non-yielding load left the JS task 0.07-0.8 ms on WebGPU /
            // Wasm, and 29 ms of 166 (17%) on WebGL, where the interpreter's first call of this 8-type-argument loader costs
            // tens of ms by itself (red-check 2026-11-08).
            if (callbackLoadMs < 10 * cachedMs + 5 || callbackLoadMs < 0.4 * loaded)
                throw new Exception($"the browser task ran AFTER the compile (its load of the same kernel took {callbackLoadMs:F2} ms, " +
                    $"a cache hit {cachedMs:F2} ms): the async load did not yield before compiling");
        });
    }
}
