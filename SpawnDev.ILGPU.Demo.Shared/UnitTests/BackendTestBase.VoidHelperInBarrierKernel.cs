using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A kernel with barriers that calls a VOID helper which is not inlined (a loop: a multi-block helper). On Wasm a
    // barrier kernel runs in phase mode, and a void helper there made an invalid module: the call site dropped a value
    // the helper never returned ("not enough arguments on the stack for drop"), and a void helper compiled in phase
    // mode left its yield flag on the stack of a `-> nil` function. Found through SpawnDev.Codecs (Geordi 2026-10-27):
    // the GDeflate PlanKernel, whose lane 0 merges the tile's matches in a void helper between two barriers. Shapes:
    // lane 0 calls a void loop helper between barriers; every lane calls a void helper that has its own barrier.
    public abstract partial class BackendTestBase
    {
        const int VoidHelperTile = 64;

        // Compacts the non-zero values of s[at..at+n) to its front (in order) and writes how many to s[at+n]
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void VoidHelper_Compact(ArrayView<int> s, int at, int n)
        {
            int w = 0;
            for (int t = 0; t < n; t++)
            {
                int v = s[at + t];
                if (v != 0) { s[at + w] = v; w++; }
            }
            for (int t = w; t < n; t++) s[at + t] = 0;
            s[at + n] = w;
        }

        // Clears the group's histogram, waits for every lane, then counts this lane's value
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void VoidHelper_ClearThenCount(ArrayView<int> h, ArrayView<int> s, int at, int lane, int lanes)
        {
            for (int i = lane; i < 16; i += lanes) h[i] = 0;
            Group.Barrier();
            if (lane < VoidHelperTile) Atomic.Add(ref h[s[at + lane] & 15], 1);
        }

        static void VoidHelper_CompactThenCountKernel(ArrayView<int> s, ArrayView<int> o)
        {
            var h = SharedMemory.Allocate<int>(16);
            int lane = Group.IdxX;
            int at = Grid.IdxX * (VoidHelperTile + 1);
            for (int i = lane; i < 16; i += Group.DimX) h[i] = 0;
            Group.Barrier();
            if (lane == 0) VoidHelper_Compact(s, at, VoidHelperTile);
            Group.Barrier();
            int count = s[at + VoidHelperTile];
            for (int t = lane; t < count; t += Group.DimX) Atomic.Add(ref h[s[at + t] & 15], 1);
            Group.Barrier();
            for (int i = lane; i < 16; i += Group.DimX) o[Grid.IdxX * 16 + i] = h[i];
        }

        static void VoidHelper_BarrierHelperKernel(ArrayView<int> s, ArrayView<int> o)
        {
            var h = SharedMemory.Allocate<int>(16);
            int lane = Group.IdxX;
            int at = Grid.IdxX * (VoidHelperTile + 1);
            VoidHelper_ClearThenCount(h, s, at, lane, Group.DimX);
            Group.Barrier();
            for (int i = lane; i < 16; i += Group.DimX) o[Grid.IdxX * 16 + i] = h[i];
        }

        [TestMethod]
        public async Task VoidHelperInBarrierKernel_MatchesOracle() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(VoidHelperTile, accelerator.MaxNumThreadsPerGroup);
            const int groups = 3;
            var rng = new Random(41);
            var input = new int[groups * (VoidHelperTile + 1)];
            for (int g = 0; g < groups; g++)
                for (int t = 0; t < VoidHelperTile; t++)
                    input[g * (VoidHelperTile + 1) + t] = rng.Next(3) == 0 ? 0 : rng.Next(1, 1000);

            // CPU oracle: per group, the histogram (low 4 bits) of the tile's values - the compaction keeps every non-zero
            // value, so lane 0's compaction then counting the compacted values must give the non-zero values' histogram
            var wantCompact = new int[groups * 16];
            var wantAll = new int[groups * 16];
            for (int g = 0; g < groups; g++)
                for (int t = 0; t < VoidHelperTile; t++)
                {
                    int v = input[g * (VoidHelperTile + 1) + t];
                    if (v != 0) wantCompact[g * 16 + (v & 15)]++;
                    if (t < groupSize) wantAll[g * 16 + (v & 15)]++;
                }

            using var dS1 = accelerator.Allocate1D(input);
            using var dO1 = accelerator.Allocate1D<int>(groups * 16);
            accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(VoidHelper_CompactThenCountKernel)(
                new KernelConfig(groups, groupSize), dS1.View, dO1.View);
            using var dS2 = accelerator.Allocate1D(input);
            using var dO2 = accelerator.Allocate1D<int>(groups * 16);
            accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(VoidHelper_BarrierHelperKernel)(
                new KernelConfig(groups, groupSize), dS2.View, dO2.View);
            await accelerator.SynchronizeAsync();
            var got1 = await dO1.CopyToHostAsync<int>();
            var got2 = await dO2.CopyToHostAsync<int>();
            var s1 = await dS1.CopyToHostAsync<int>();
            for (int g = 0; g < groups; g++)
            {
                int nonZero = 0;
                for (int t = 0; t < VoidHelperTile; t++) if (input[g * (VoidHelperTile + 1) + t] != 0) nonZero++;
                if (s1[g * (VoidHelperTile + 1) + VoidHelperTile] != nonZero)
                    throw new Exception($"void loop helper: group {g} count {s1[g * (VoidHelperTile + 1) + VoidHelperTile]}, expected {nonZero}");
            }
            for (int i = 0; i < groups * 16; i++)
            {
                if (got1[i] != wantCompact[i]) throw new Exception($"void loop helper: group {i / 16} bin {i % 16} = {got1[i]}, expected {wantCompact[i]}");
                if (got2[i] != wantAll[i]) throw new Exception($"void barrier helper: group {i / 16} bin {i % 16} = {got2[i]}, expected {wantAll[i]}");
            }
        });
    }
}
