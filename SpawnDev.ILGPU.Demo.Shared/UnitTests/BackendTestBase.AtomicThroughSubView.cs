using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Atomics through a sub-view cut inside the kernel. WebGPU's atomic scan stopped at the sub-view, so the parameter's
    // binding was declared non-atomic (and float / unsigned / 64-bit atomics took the plain integer path): shader creation
    // failed with "atomic operation is done on a pointer to a non-atomic". A sub-view's pointer belongs to the parameter
    // it was cut from.
    public abstract partial class BackendTestBase
    {
        static void AtomicSubView_Kernel(Index1D idx, ArrayView<int> counts, ArrayView<float> sums, ArrayView<uint> maxes, ArrayView<long> totals)
        {
            int i = idx;
            var c = counts.SubView(4, 8);
            Atomic.Add(ref c[i % 8], 1);
            var s = sums.SubView(2, 4);
            Atomic.Add(ref s[i % 4], 0.5f);
            var m = maxes.SubView(1, 3);
            Atomic.Max(ref m[i % 3], (uint)i * 2654435761u);
            var t = totals.SubView(1, 2);
            Atomic.Add(ref t[i % 2], (long)i << 33);
        }

        [TestMethod]
        public async Task Atomic_ThroughInKernelSubView_MatchesOracle() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 96;
            using var dCounts = accelerator.Allocate1D(new int[16]);
            using var dSums = accelerator.Allocate1D(new float[8]);
            using var dMaxes = accelerator.Allocate1D(new uint[6]);
            using var dTotals = accelerator.Allocate1D(new long[4]);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>, ArrayView<uint>, ArrayView<long>>(AtomicSubView_Kernel)(
                (Index1D)n, dCounts.View, dSums.View, dMaxes.View, dTotals.View);
            await accelerator.SynchronizeAsync();
            var counts = await dCounts.CopyToHostAsync<int>();
            var sums = await dSums.CopyToHostAsync<float>();
            var maxes = await dMaxes.CopyToHostAsync<uint>();
            var totals = await dTotals.CopyToHostAsync<long>();
            var wantCounts = new int[16]; var wantSums = new float[8]; var wantMaxes = new uint[6]; var wantTotals = new long[4];
            for (int i = 0; i < n; i++)
            {
                wantCounts[4 + i % 8]++;
                wantSums[2 + i % 4] += 0.5f;
                wantMaxes[1 + i % 3] = Math.Max(wantMaxes[1 + i % 3], (uint)i * 2654435761u);
                wantTotals[1 + i % 2] += (long)i << 33;
            }
            for (int k = 0; k < 16; k++) if (counts[k] != wantCounts[k]) throw new Exception($"counts[{k}] = {counts[k]}, expected {wantCounts[k]}");
            for (int k = 0; k < 8; k++) if (sums[k] != wantSums[k]) throw new Exception($"sums[{k}] = {sums[k]}, expected {wantSums[k]}");
            for (int k = 0; k < 6; k++) if (maxes[k] != wantMaxes[k]) throw new Exception($"maxes[{k}] = {maxes[k]}, expected {wantMaxes[k]}");
            for (int k = 0; k < 4; k++) if (totals[k] != wantTotals[k]) throw new Exception($"totals[{k}] = {totals[k]}, expected {wantTotals[k]}");
        });
    }
}
