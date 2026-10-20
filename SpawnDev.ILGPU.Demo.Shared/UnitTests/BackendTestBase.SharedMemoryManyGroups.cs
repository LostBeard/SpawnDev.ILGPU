using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // The Velocity backend runs the groups of a chunk one after the other in one kernel loop. Every iteration allocated its shared and local arrays again from a pool of 2 MB that
    // was only reset at the start of the chunk: a kernel with shared or local arrays that runs tens of thousands of groups exhausted it and wrote outside the pool (access violation).
    // Groups of at most one warp (the 16 lane warps of the 512 bit device, or groups of 4 on the 128 bit one) never reached a team that resets the pool per group.
    // The shared memory test is red without the fix (access violation). The local memory test did not fail before (the arrays of that kernel do not come from the pool), it guards the reset.
    public abstract partial class BackendTestBase
    {
        static void SharedManyGroupsKernel(ArrayView<int> output)
        {
            var shared = SharedMemory.Allocate<int>(300);
            shared[Group.IdxX] = Grid.IdxX + Group.IdxX;
            Group.Barrier();
            int last = shared[Group.DimX - 1];
            Group.Barrier();
            if (Group.IsFirstThread)
                output[Grid.IdxX] = last;
        }

        static void LocalManyIterationsKernel(Index1D i, ArrayView<int> output)
        {
            var local = LocalMemory.Allocate<int>(64);
            // a data dependent index keeps the array in memory (a constant index is promoted to registers)
            for (int k = 0; k < 64; k++)
                local[(k + i) & 63] = i + k;
            int sum = 0;
            for (int k = 0; k < 64; k++)
                sum += local[k];
            output[i] = sum;
        }

        [TestMethod]
        public async Task SharedMemory_ManyGroupsOfOneWarp_DoNotExhaustThePool() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(4, accelerator.MaxNumThreadsPerGroup);
            int numGroups = 60000;
            using var output = accelerator.Allocate1D<int>(numGroups);
            accelerator.LoadStreamKernel<ArrayView<int>>(SharedManyGroupsKernel)(new KernelConfig(numGroups, groupSize), output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            for (int g = 0; g < numGroups; g += 997)
                if (got[g] != g + groupSize - 1)
                    throw new Exception($"group {g}: got {got[g]}, expected {g + groupSize - 1}");
        });

        [TestMethod]
        public async Task LocalMemory_ManyIterations_DoNotExhaustThePool() => await RunTest(async accelerator =>
        {
            int n = 200000;
            using var output = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(LocalManyIterationsKernel)(n, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            for (int i = 0; i < n; i += 991)
                if (got[i] != 64 * i + 63 * 64 / 2)
                    throw new Exception($"thread {i}: got {got[i]}, expected {64 * i + 63 * 64 / 2}");
        });
    }
}
