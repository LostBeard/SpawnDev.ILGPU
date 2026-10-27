using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // An Atomic.* op on an element of a SharedMemory array (a per-group histogram). WGSL atomics need an atomic element
    // type, and the shared array was declared `array<i32, N>`: shader creation failed ("atomic operation is done on a
    // pointer to a non-atomic"; SpawnDev.Codecs WeightPackGpu.CompressCountKernel, Geordi 2026-10-26). The array is now
    // `array<atomic<i32>, N>` and its plain loads and stores are atomicLoad / atomicStore. int and uint, against a CPU oracle.
    public abstract partial class BackendTestBase
    {
        const int SharedAtomicsBins = 16;

        static void SharedAtomics_HistogramKernel(ArrayView<int> values, ArrayView<int> counts, ArrayView<uint> ucounts, int length)
        {
            var hist = SharedMemory.Allocate<int>(SharedAtomicsBins);
            var uhist = SharedMemory.Allocate<uint>(SharedAtomicsBins);
            int lidx = Group.IdxX;
            if (lidx < SharedAtomicsBins) { hist[lidx] = 0; uhist[lidx] = 0u; }
            Group.Barrier();
            int gidx = Grid.IdxX * Group.DimX + lidx;
            if (gidx < length)
            {
                int bin = values[gidx] & (SharedAtomicsBins - 1);
                Atomic.Add(ref hist[bin], 1);
                Atomic.Add(ref uhist[bin], (uint)(values[gidx] >> 4) + 1u);
            }
            Group.Barrier();
            if (lidx < SharedAtomicsBins)
            {
                counts[Grid.IdxX * SharedAtomicsBins + lidx] = hist[lidx];
                ucounts[Grid.IdxX * SharedAtomicsBins + lidx] = uhist[lidx];
            }
        }

        [TestMethod]
        public async Task SharedMemoryAtomics_GroupHistogram_MatchesOracle() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(128, accelerator.MaxNumThreadsPerGroup);
            const int groups = 3;
            int n = groupSize * groups - 5;
            var values = new int[n];
            var rng = new Random(23);
            for (int i = 0; i < n; i++) values[i] = rng.Next(0, 1000);
            using var dValues = accelerator.Allocate1D(values);
            using var dCounts = accelerator.Allocate1D<int>(groups * SharedAtomicsBins);
            using var dUCounts = accelerator.Allocate1D<uint>(groups * SharedAtomicsBins);
            accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, ArrayView<uint>, int>(SharedAtomics_HistogramKernel)(
                new KernelConfig(groups, groupSize), dValues.View, dCounts.View, dUCounts.View, n);
            await accelerator.SynchronizeAsync();
            var counts = await dCounts.CopyToHostAsync<int>();
            var ucounts = await dUCounts.CopyToHostAsync<uint>();
            for (int g = 0; g < groups; g++)
                for (int b = 0; b < SharedAtomicsBins; b++)
                {
                    int want = 0;
                    uint uwant = 0;
                    for (int i = g * groupSize; i < Math.Min(n, (g + 1) * groupSize); i++)
                        if ((values[i] & (SharedAtomicsBins - 1)) == b) { want++; uwant += (uint)(values[i] >> 4) + 1u; }
                    if (counts[g * SharedAtomicsBins + b] != want)
                        throw new Exception($"group {g} bin {b}: count {counts[g * SharedAtomicsBins + b]}, expected {want}");
                    if (ucounts[g * SharedAtomicsBins + b] != uwant)
                        throw new Exception($"group {g} bin {b}: uint sum {ucounts[g * SharedAtomicsBins + b]}, expected {uwant}");
                }
        });
    }
}
