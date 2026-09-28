using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // An if/else inside a loop where one arm can BREAK out of the loop: both arms must still reach the loop latch
    // (the counter increment). The WGSL/GLSL structured emitters picked the else arm's own block as the merge, so the
    // then-arm swallowed the latch and the else arm never advanced the counter - an infinite loop on the GPU. Found by
    // SpawnScene's GPU FAST-9 detector: DXGI_ERROR_DEVICE_HUNG on the first photo (2026-09-28). Fixed with
    // CodeGen.StructuredLoopMerge (the first block BOTH arms reach in the loop).
    //
    // ⚠️ Red-checked on the generated WGSL/GLSL (desktop, `DemoConsole -- addr-helper-wgsl LoopLatchRunKernel`), NOT by
    // running the broken shader: it hangs the GPU, which resets the display driver on the machine running the test.
    public abstract partial class BackendTestBase
    {
        // Longest run of samples above 5 in a 32-step walk around a 16-entry ring, stopping early at 9.
        static void LoopLatchRunKernel(Index1D i, ArrayView1D<int, Stride1D.Dense> data, ArrayView1D<int, Stride1D.Dense> outp)
        {
            int maxRun = 0, run = 0;
            for (int k = 0; k < 32; k++)
            {
                if (data[(i + k) % 16] > 5)
                {
                    run++;
                    if (run > maxRun) maxRun = run;
                    if (maxRun >= 9) break;
                }
                else run = 0;
            }
            outp[i] = maxRun;
        }

        // FAST-9's shape: the loop in a helper that RETURNS from inside it, called twice with different limits.
        static bool LoopLatchNine(ArrayView1D<int, Stride1D.Dense> data, int start, int limit, bool brighter)
        {
            int maxRun = 0, run = 0;
            for (int k = 0; k < 32; k++)
            {
                int v = data[(start + k) % 16];
                bool passes = brighter ? v > limit : v < limit;
                if (passes)
                {
                    run++;
                    if (run > maxRun) maxRun = run;
                    if (maxRun >= 9) return true;
                }
                else run = 0;
            }
            return false;
        }

        static void LoopLatchHelperKernel(Index1D i, ArrayView1D<int, Stride1D.Dense> data, ArrayView1D<int, Stride1D.Dense> outp)
            => outp[i] = LoopLatchNine(data, i, 5, true) ? 1 : LoopLatchNine(data, i, 3, false) ? 2 : 0;

        static int[] LoopLatchRing(int seed)
        {
            // Mixed runs: long bright runs (hit the 9 break), short ones (reset the run), dark stretches.
            var rng = new Random(seed);
            var d = new int[16];
            for (int k = 0; k < 16; k++) d[k] = rng.Next(0, 10);
            for (int k = 2; k < 12; k++) d[k] = 9;      // a 10-long bright run somewhere on the ring
            d[5] = 0;                                    // ...broken once, so some starts see only short runs
            return d;
        }

        [TestMethod]
        public async Task LoopLatch_IfElseWithBreak_BothArmsReachLatch() => await RunTest(async accelerator =>
        {
            foreach (int seed in new[] { 1, 7, 42 })
            {
                var ring = LoopLatchRing(seed);
                const int n = 16;
                using var data = accelerator.Allocate1D(ring);
                using var runOut = accelerator.Allocate1D<int>(n);
                using var helperOut = accelerator.Allocate1D<int>(n);
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>>(LoopLatchRunKernel)(n, data.View, runOut.View);
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>>(LoopLatchHelperKernel)(n, data.View, helperOut.View);
                await accelerator.SynchronizeAsync();
                var gotRun = await runOut.CopyToHostAsync<int>();
                var gotHelper = await helperOut.CopyToHostAsync<int>();

                var dataView = ring;
                for (int i = 0; i < n; i++)
                {
                    // Host oracle: the same code on managed arrays.
                    int maxRun = 0, run = 0;
                    for (int k = 0; k < 32; k++)
                    {
                        if (dataView[(i + k) % 16] > 5) { run++; if (run > maxRun) maxRun = run; if (maxRun >= 9) break; }
                        else run = 0;
                    }
                    if (gotRun[i] != maxRun)
                        throw new Exception($"seed {seed} run[{i}] = {gotRun[i]}, expected {maxRun}");

                    bool Nine(int start, int limit, bool brighter)
                    {
                        int mr = 0, r = 0;
                        for (int k = 0; k < 32; k++)
                        {
                            int v = dataView[(start + k) % 16];
                            bool p = brighter ? v > limit : v < limit;
                            if (p) { r++; if (r > mr) mr = r; if (mr >= 9) return true; }
                            else r = 0;
                        }
                        return false;
                    }
                    int expectHelper = Nine(i, 5, true) ? 1 : Nine(i, 3, false) ? 2 : 0;
                    if (gotHelper[i] != expectHelper)
                        throw new Exception($"seed {seed} helper[{i}] = {gotHelper[i]}, expected {expectHelper}");
                }
            }
        });
    }
}
