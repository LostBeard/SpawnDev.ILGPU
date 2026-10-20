using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Explicit group sizes: the thread, group and grid indices of every thread, for group sizes that are not one warp (3 and 6 are smaller than or not a multiple of a
    // Velocity warp's 4 lanes; 12 and 64 are several warps; 100, 256 and 1024 only where the device allows them). The Velocity backend (SIMD, a warp of 4 lanes) used to reject every group of
    // more than a warp ("Invalid group dimensions"); a group of several warps is now processed by consecutive warps, the group-local index being (threads of the group already
    // covered) + lane. No barriers here: kernels with barriers still need the warps of a group to meet. Every other backend must give the same answers, so this runs on all of them.
    public abstract partial class BackendTestBase
    {
        static void GroupIndicesKernel(ArrayView<int> o)
        {
            int g = Grid.GlobalIndex.X;
            o[g * 4 + 0] = Grid.IdxX;
            o[g * 4 + 1] = Group.IdxX;
            o[g * 4 + 2] = Group.DimX;
            o[g * 4 + 3] = Grid.DimX;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int GroupIndicesHelper(int groupIdx, int gridIdx) => groupIdx * 1000 + gridIdx;

        static void GroupIndicesViaHelperKernel(ArrayView<int> o)
        {
            // the index values reach a function that is not inlined as arguments
            o[Grid.GlobalIndex.X] = GroupIndicesHelper(Group.IdxX, Grid.IdxX);
        }

        [TestMethod]
        public async Task LargeGroups_ThreadGroupAndGridIndicesAreRight() => await RunTest(async accelerator =>
        {
            // groups need shared-memory semantics: not on WebGL (no workgroups in vertex shaders)
            if (!accelerator.Device.Satisfies(new AcceleratorRequirements { RequiresSharedMemory = true })) throw new UnsupportedTestException("needs thread groups");
            int ran = 0;
            // up to 64 threads every backend launches (the CPU backend's limit); the bigger sizes only where the device allows them
            foreach (var (groups, groupSize) in new[] { (5, 64), (3, 12), (7, 6), (9, 3), (3, 100), (2, 256), (1, 1024) })
            {
                if (groupSize > 64 && (groupSize > accelerator.MaxGroupSize.X || groupSize > accelerator.MaxNumThreadsPerGroup)) continue;
                int n = groups * groupSize;
                using var o = accelerator.Allocate1D<int>(n * 4); o.MemSetToZero();
                accelerator.LoadStreamKernel<ArrayView<int>>(GroupIndicesKernel)(new KernelConfig(groups, groupSize), o.View);
                await accelerator.SynchronizeAsync();
                var got = await o.CopyToHostAsync<int>();
                for (int g = 0; g < n; g++)
                {
                    int eGrid = g / groupSize, eGroup = g % groupSize;
                    if (got[g * 4] != eGrid || got[g * 4 + 1] != eGroup || got[g * 4 + 2] != groupSize || got[g * 4 + 3] != groups)
                        throw new Exception($"{groups} groups of {groupSize} threads, thread {g}: Grid.IdxX {got[g * 4]}, Group.IdxX {got[g * 4 + 1]}, Group.DimX {got[g * 4 + 2]}, Grid.DimX {got[g * 4 + 3]}; expected {eGrid}, {eGroup}, {groupSize}, {groups}");
                }

                using var o2 = accelerator.Allocate1D<int>(n); o2.MemSetToZero();
                accelerator.LoadStreamKernel<ArrayView<int>>(GroupIndicesViaHelperKernel)(new KernelConfig(groups, groupSize), o2.View);
                await accelerator.SynchronizeAsync();
                var got2 = await o2.CopyToHostAsync<int>();
                for (int g = 0; g < n; g++)
                    if (got2[g] != (g % groupSize) * 1000 + g / groupSize)
                        throw new Exception($"{groups} groups of {groupSize} threads, thread {g} through a helper: got {got2[g]}, expected {(g % groupSize) * 1000 + g / groupSize}");
                ran++;
            }
            if (ran < 4) throw new Exception($"only {ran} of the group sizes ran");
        });
    }
}
