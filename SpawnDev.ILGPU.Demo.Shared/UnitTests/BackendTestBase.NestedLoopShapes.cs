using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-25 (found by LoopBackEdge_ThroughPassThroughBlock_MatchesCpuOracle on the Velocity lane): two loop shapes
    // Velocity got wrong while every other backend was right.
    // 1. An inner `while` that is the last thing in an outer loop's body exits straight into the outer loop's header,
    //    so that edge is both the inner loop's exit and the outer loop's back edge. Velocity's branch builder took it
    //    as a plain back edge: the lanes that left the inner loop were not removed from its mask and did not wait
    //    for the others (0 for every lane in NestedWhileKernel). Fix: VelocityLoopExitSplitting gives such an edge a
    //    block of its own.
    // 2. An if/else whose arms each end in an inner loop gives the outer loop two back edges. Velocity jumped back from
    //    the first one while lanes still waited to run the other arm, and lost their work. Fix: VelocityLoopLatches
    //    routes all back edges through one latch.
    // One store per thread at its own slot, so this runs on WebGL too.
    public abstract partial class BackendTestBase
    {
        const int NestedLoopSegment = 64;

        static void NestedWhileKernel(Index1D t, ArrayView<int> w, ArrayView<int> count)
        {
            int lb = (int)t * NestedLoopSegment;
            int nt = 0;
            int i = 0;
            while (i < NestedLoopSegment)
            {
                int run = w[lb + i];
                i += 1;
                while (run > 0)
                {
                    nt++;
                    run--;
                }
            }
            count[t] = nt;
        }

        static void ArmsEndInLoopsKernel(Index1D t, ArrayView<int> w, ArrayView<int> count)
        {
            int lb = (int)t * NestedLoopSegment;
            int nt = 0;
            int prev = 8;
            int i = 0;
            while (i < NestedLoopSegment)
            {
                int v = w[lb + i];
                i++;
                if (v < 4)
                {
                    int run = v;
                    while (run >= 2) { nt += 1000; run -= 2; }
                    while (run > 0) { nt++; run--; }
                }
                else
                {
                    if (v != prev) { nt += 100000; prev = v; }
                    int run = v;
                    while (run >= 3) { nt += 100; run -= 3; }
                    while (run > 0) { nt += 10; run--; }
                }
            }
            count[t] = nt;
        }

        [TestMethod]
        public async Task NestedLoops_InnerLoopEndsOuterBody_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            const int threads = 4096;
            var w = new int[threads * NestedLoopSegment];
            var rng = new Random(20261025);
            for (int k = 0; k < w.Length; k++) w[k] = rng.Next(8);

            var wantNested = new int[threads];
            var wantArms = new int[threads];
            for (int t = 0; t < threads; t++)
            {
                int nested = 0, arms = 0, prev = 8;
                for (int k = 0; k < NestedLoopSegment; k++)
                {
                    int v = w[t * NestedLoopSegment + k];
                    nested += v;
                    if (v < 4) arms += 1000 * (v / 2) + v % 2;
                    else
                    {
                        if (v != prev) { arms += 100000; prev = v; }
                        arms += 100 * (v / 3) + 10 * (v % 3);
                    }
                }
                wantNested[t] = nested;
                wantArms[t] = arms;
            }

            using var wBuf = accelerator.Allocate1D(w);
            using var countBuf = accelerator.Allocate1D<int>(threads);
            foreach (var (name, kernelMethod, want) in new (string, Action<Index1D, ArrayView<int>, ArrayView<int>>, int[])[]
                { ("nested while", NestedWhileKernel, wantNested), ("if/else arms ending in loops", ArmsEndInLoopsKernel, wantArms) })
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel(kernelMethod);
                kernel(threads, wBuf.View, countBuf.View);
                await accelerator.SynchronizeAsync();
                var got = await countBuf.CopyToHostAsync();
                for (int t = 0; t < threads; t++)
                    if (got[t] != want[t])
                        throw new Exception($"{name} on {BackendName}, thread {t}: got {got[t]}, expected {want[t]}");
            }
            Console.WriteLine($"[NestedLoops] nested while and if/else arms ending in loops, {threads} threads, match the CPU oracle on {BackendName} ✓");
        });
    }
}
