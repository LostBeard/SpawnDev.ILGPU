using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // AcceleratorRequirements.RequiresWarpShuffle / accelerator.SupportsWarpShuffle() (5.4.22): a backend-neutral answer to
    // "can this device run Warp.Shuffle", so a consumer needs no backend type check (Tuvok, 2026-11-09: SpawnDev.ILGPU.ML's
    // Q4_K GEMV shuffle reduction had to test CLAccelerator.Capabilities.SubGroupShuffle itself - NVIDIA's OpenCL has
    // subgroups but no shuffle). Self-consistency on every lane: when the capability says yes, a rotate-by-one shuffle
    // matches the host model at the accelerator's warp size (this caught the WebGPU-without-subgroups case, whose emulated
    // warp is the whole workgroup while the accelerator reported 32).
    public abstract partial class BackendTestBase
    {
        static void WarpShuffleRotateKernel(ArrayView<int> data, ArrayView<int> output)
        {
            int g = Grid.GlobalIndex.X;
            int lane = Warp.LaneIdx;
            output[g] = Warp.Shuffle(data[g], (lane + 1) % Warp.WarpSize);
        }

        [TestMethod]
        public async Task WarpShuffle_CapabilityMatchesWhatTheBackendRuns() => await RunTest(async accelerator =>
        {
            bool supported = accelerator.SupportsWarpShuffle();
            if (!supported)
            {
                // No fixed-width shuffle (WebGL: identity of a 1-lane warp; WebGPU without subgroups: the warp is the
                // whole workgroup; OpenCL without the shuffle extensions: refused at compile). Nothing to run; a width query
                // must agree.
                if (accelerator.SupportsWarpShuffle(accelerator.WarpSize))
                    throw new Exception($"SupportsWarpShuffle() is false but SupportsWarpShuffle({accelerator.WarpSize}) is true");
                return;
            }

            int warp = accelerator.WarpSize;
            if (warp <= 0) throw new Exception($"SupportsWarpShuffle() is true but WarpSize is {warp}");
            if (!accelerator.SupportsWarpShuffle(warp) || accelerator.SupportsWarpShuffle(warp + 1))
                throw new Exception($"SupportsWarpShuffle(width) does not follow WarpSize {warp}");
            int groupSize = Math.Max(warp, Math.Min(64, accelerator.MaxNumThreadsPerGroup / warp * warp));
            int groups = 4, n = groupSize * groups;
            var data = new int[n];
            for (int i = 0; i < n; i++) data[i] = i * 7 + 3;
            using var dData = accelerator.Allocate1D(data);
            using var dOut = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(WarpShuffleRotateKernel);
            kernel(new KernelConfig(groups, groupSize), dData.View, dOut.View);
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int warpBase = i - (i % groupSize) + ((i % groupSize) / warp) * warp;
                int lane = (i % groupSize) % warp;
                int want = data[warpBase + (lane + 1) % warp];
                if (got[i] != want)
                    throw new Exception($"[{i}] = {got[i]}, want {want} (warp {warp}, group {groupSize})");
            }
        });
    }
}
