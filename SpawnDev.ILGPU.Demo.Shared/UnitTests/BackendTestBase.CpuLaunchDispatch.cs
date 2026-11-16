using System;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // CPU launch dispatch (2026-11-15). A launch on the cooperative lane threads used to wake every runtime thread of
    // every multiprocessor through one shared monitor and pass them through a start and an end barrier: about 15 ms
    // per launch of a barrier kernel on 12 multiprocessors x 64 threads, even for one group. A launch now wakes only
    // its groups' threads, each on its own monitor, and the last thread of each multiprocessor reports the end. What
    // that must not break: every lane of every group runs exactly once per launch (a thread must not run a launch
    // twice, skip one, or pick up the next before it is published), and a group's lanes all meet at its barriers.
    // The shapes change on EVERY launch (group sizes 1..64, grids smaller and larger than the multiprocessor count),
    // so a thread idle in one launch and needed in the next is exercised constantly.
    public abstract partial class BackendTestBase
    {
        static readonly int[] CpuDispatchGroupSizes = { 64, 1, 7, 32, 64, 33 };
        static readonly int[] CpuDispatchGrids = { 1, 13, 2, 100, 12, 11, 25 };
        const int CpuDispatchRounds = 12;

        static void CpuDispatchKernel(ArrayView<int> counts, ArrayView<int> groupSums)
        {
            var sh = SharedMemory.Allocate<int>(64);
            int t = Group.IdxX;
            sh[t] = 1;
            Group.Barrier();
            if (t == 0)
            {
                int s = 0;
                for (int j = 0; j < Group.DimX; j++)
                    s += sh[j];
                groupSums[Grid.IdxX] += s;
            }
            counts[Grid.GlobalIndex.X] += 1;
        }

        [TestMethod]
        public async Task CpuLaunch_ChangingShapes_EveryLaneOncePerLaunch() => await RunTest(async accelerator =>
        {
            if (accelerator is not CPUAccelerator)
                throw new UnsupportedTestException("CPU-only: pins the CPU accelerator's launch dispatch");

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(CpuDispatchKernel);
            int shapes = CpuDispatchGroupSizes.Length * CpuDispatchGrids.Length;
            var counts = new MemoryBuffer1D<int, Stride1D.Dense>[shapes];
            var sums = new MemoryBuffer1D<int, Stride1D.Dense>[shapes];
            try
            {
                for (int s = 0; s < shapes; s++)
                {
                    int group = CpuDispatchGroupSizes[s % CpuDispatchGroupSizes.Length];
                    int grid = CpuDispatchGrids[s / CpuDispatchGroupSizes.Length];
                    counts[s] = accelerator.Allocate1D<int>(group * grid);
                    counts[s].MemSetToZero();
                    sums[s] = accelerator.Allocate1D<int>(grid);
                    sums[s].MemSetToZero();
                }
                // every round launches every shape once; consecutive launches always differ in shape
                for (int round = 0; round < CpuDispatchRounds; round++)
                    for (int s = 0; s < shapes; s++)
                    {
                        int group = CpuDispatchGroupSizes[s % CpuDispatchGroupSizes.Length];
                        int grid = CpuDispatchGrids[s / CpuDispatchGroupSizes.Length];
                        kernel(new KernelConfig(grid, group), counts[s].View, sums[s].View);
                    }
                await accelerator.SynchronizeAsync();

                for (int s = 0; s < shapes; s++)
                {
                    int group = CpuDispatchGroupSizes[s % CpuDispatchGroupSizes.Length];
                    int grid = CpuDispatchGrids[s / CpuDispatchGroupSizes.Length];
                    var c = await counts[s].CopyToHostAsync<int>();
                    for (int i = 0; i < c.Length; i++)
                        if (c[i] != CpuDispatchRounds)
                            throw new Exception($"group {group} x grid {grid}: lane {i % group} of group {i / group} ran " +
                                                $"{c[i]} times in {CpuDispatchRounds} launches");
                    var g = await sums[s].CopyToHostAsync<int>();
                    for (int i = 0; i < g.Length; i++)
                        if (g[i] != CpuDispatchRounds * group)
                            throw new Exception($"group {group} x grid {grid}: group {i} saw {g[i]} lanes at its barrier " +
                                                $"over {CpuDispatchRounds} launches, expected {CpuDispatchRounds * group}");
                }
            }
            finally
            {
                foreach (var b in counts) b?.Dispose();
                foreach (var b in sums) b?.Dispose();
            }
        });
    }
}
