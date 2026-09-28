using System;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // CPU lane-loop launch path (2026-09-28). A kernel with no barriers, warp shuffles, broadcasts or
    // shared memory runs each group's lanes in a plain loop instead of on the cooperative lane threads:
    // 0.685 -> 0.004 ms per trivial launch, 1.39 s -> 7 ms for a 4M-element element-wise kernel. These
    // pin the semantics that path must reproduce (index math, a partial last group, the parallel split of
    // a large grid) on every backend, and on CPU also prove WHICH path ran - a lane-independent kernel
    // must take the lane loop, a barrier kernel must not.
    public abstract partial class BackendTestBase
    {
        const int LaneLoopPartialLen = 1000;          // not a multiple of any group size in use
        const int LaneLoopPartialAlloc = 1024;
        const int LaneLoopSentinel = -7;
        const int LaneLoopW = 33, LaneLoopH = 17;     // 2D, odd extents
        const int LaneLoopBigLen = 1 << 20;           // well past LaneLoopInlineMaxWorkItems: parallel split

        static void LaneLoopIndexKernel(Index1D i, ArrayView<int> output) => output[i] = i * 3 + 1;

        static void LaneLoop2DKernel(Index2D i, ArrayView<int> output, int width) =>
            output[i.Y * width + i.X] = i.X * 1000 + i.Y;

        static void LaneLoopBarrierKernel(ArrayView<int> input, ArrayView<int> output)
        {
            var sh = SharedMemory.Allocate<int>(32);
            int t = Group.IdxX;
            sh[t] = input[Grid.IdxX * 32 + t];
            Group.Barrier();
            for (int s = 16; s > 0; s >>= 1)
            {
                if (t < s) sh[t] += sh[t + s];
                Group.Barrier();
            }
            if (t == 0) output[Grid.IdxX] = sh[0];
        }

        [TestMethod]
        public async Task CpuLaneLoop_LaneIndependentKernels_ExactAndOnTheLaneLoop() => await RunTest(async accelerator =>
        {
            bool isCpu = accelerator is CPUAccelerator;
            long loopsBefore = CPUAccelerator.LaneLoopLaunchCount;

            // 1) Partial last group: lanes past the end must not run (the sentinels must survive).
            var partialInit = new int[LaneLoopPartialAlloc];
            Array.Fill(partialInit, LaneLoopSentinel);
            using var partial = accelerator.Allocate1D(partialInit);
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(LaneLoopIndexKernel);
            k1(LaneLoopPartialLen, partial.View);

            // 2) 2D implicit grid with odd extents.
            using var grid2d = accelerator.Allocate1D<int>(LaneLoopW * LaneLoopH);
            var k2 = accelerator.LoadAutoGroupedStreamKernel<Index2D, ArrayView<int>, int>(LaneLoop2DKernel);
            k2(new Index2D(LaneLoopW, LaneLoopH), grid2d.View, LaneLoopW);

            // 3) Large grid: the lane loop splits it across multiprocessors on the thread pool.
            using var big = accelerator.Allocate1D<int>(LaneLoopBigLen);
            k1(LaneLoopBigLen, big.View);
            await accelerator.SynchronizeAsync();

            var p = await partial.CopyToHostAsync<int>();
            for (int i = 0; i < LaneLoopPartialAlloc; i++)
            {
                int want = i < LaneLoopPartialLen ? i * 3 + 1 : LaneLoopSentinel;
                if (p[i] != want)
                    throw new Exception($"partial-group launch: [{i}] = {p[i]}, expected {want}" +
                                        (i >= LaneLoopPartialLen ? " (a lane past the end ran)" : ""));
            }
            var g = await grid2d.CopyToHostAsync<int>();
            for (int y = 0; y < LaneLoopH; y++)
                for (int x = 0; x < LaneLoopW; x++)
                    if (g[y * LaneLoopW + x] != x * 1000 + y)
                        throw new Exception($"2D launch: ({x},{y}) = {g[y * LaneLoopW + x]}, expected {x * 1000 + y}");
            var b = await big.CopyToHostAsync<int>();
            for (int i = 0; i < LaneLoopBigLen; i++)
                if (b[i] != i * 3 + 1)
                    throw new Exception($"large (parallel-split) launch: [{i}] = {b[i]}, expected {i * 3 + 1}");

            if (isCpu)
            {
                long ran = CPUAccelerator.LaneLoopLaunchCount - loopsBefore;
                if (ran < 3)
                    throw new Exception($"only {ran} of 3 lane-independent launches took the CPU lane-loop path " +
                                        $"(DisableLaneLoop={CPUAccelerator.DisableLaneLoop})");
            }
        });

        [TestMethod]
        public async Task CpuLaneLoop_BarrierKernel_StaysCooperativeAndCorrect() => await RunTest(async accelerator =>
        {
            if (accelerator is not CPUAccelerator)
                throw new UnsupportedTestException("CPU-only: proves the CPU launch-path selection");
            const int groups = 64;
            var input = new int[groups * 32];
            for (int i = 0; i < input.Length; i++) input[i] = i % 32 + 1;   // each group sums 1..32 = 528
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<int>(groups);
            var k = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(LaneLoopBarrierKernel);

            long loopsBefore = CPUAccelerator.LaneLoopLaunchCount;
            k(new KernelConfig(groups, 32), inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            long ran = CPUAccelerator.LaneLoopLaunchCount - loopsBefore;

            var o = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < groups; i++)
                if (o[i] != 528)
                    throw new Exception($"barrier reduction: group {i} = {o[i]}, expected 528");
            if (ran != 0)
                throw new Exception($"a kernel with Group.Barrier + shared memory took the lane-loop path ({ran} launches) - " +
                                    "its lanes cannot run one after another");
        });
    }
}
