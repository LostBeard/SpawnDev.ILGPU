using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Velocity;
using SpawnDev.ILGPU;
using SpawnDev.UnitTesting;

/// <summary>
/// Kernels compiled at <see cref="OptimizationLevel.O2"/>. Every test lane runs at the default level (O1), so these build
/// their own O2 context. A full sweep with O2 as the default (2026-10-27) failed 50 tests that pass at O1; these guard
/// the causes. The OpenCL one lives in <see cref="OpenCLTests"/> (the serialized opencl lane).
/// </summary>
public class OptimizationLevelO2Tests
{
    const int BarrierLoopGroupSize = 64;

    static void BarrierLoopKernel(ArrayView<int> data, ArrayView<int> output, int start, int end, int scale)
    {
        var shared = SharedMemory.Allocate<int>(BarrierLoopGroupSize);
        int t = Group.IdxX;
        int g = Grid.GlobalIndex.X;
        int acc = 0;
        for (int d = start; d < end; d++)
        {
            shared[t] = data[g] * d + scale;
            Group.Barrier();
            acc += shared[(t + d) % BarrierLoopGroupSize];
            Group.Barrier();
        }
        output[g] = acc;
    }

    // O2's aggressive CodePlacement rebuilds a block bottom-up: `acc += shared[j]` is placed first (it feeds the loop
    // phi), and its shared load moved below the Group.Barrier() that ends the body, because Movement's sweep skipped the
    // block's last memory value. Every other thread could then overwrite the slot first: a race on every backend at O2.
    // Velocity runs a group's warps one after another, so it gets it wrong every time (191 of 192 outputs); CUDA and
    // OpenCL survived by running in near lockstep.
    static async Task BarrierLoopAtO2(VelocityDeviceType deviceType)
    {
        using var context = Context.Create(builder => builder.Velocity(deviceType).Optimize(OptimizationLevel.O2));
        using var accelerator = context.GetVelocityDevice(0).CreateAccelerator(context);
        const int groups = 3, n = groups * BarrierLoopGroupSize, start = 2, end = 9, scale = 11;
        var data = new int[n];
        for (int i = 0; i < n; i++) data[i] = i * 3 - 70;
        using var dData = accelerator.Allocate1D(data);
        using var dOut = accelerator.Allocate1D<int>(n);
        accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, int, int, int>(BarrierLoopKernel)(
            new KernelConfig(groups, BarrierLoopGroupSize), dData.View, dOut.View, start, end, scale);
        await accelerator.SynchronizeAsync();
        var got = await dOut.CopyToHostAsync<int>();
        for (int g = 0; g < n; g++)
        {
            int group = g / BarrierLoopGroupSize, t = g % BarrierLoopGroupSize, want = 0;
            for (int d = start; d < end; d++)
                want += data[group * BarrierLoopGroupSize + (t + d) % BarrierLoopGroupSize] * d + scale;
            if (got[g] != want)
                throw new Exception($"{deviceType} at O2: output[{g}] = {got[g]}, expected {want}");
        }
    }

    [TestMethod]
    public Task O2_BarrierLoop_SharedLoadStaysBetweenBarriers_VelocityScalar2() => BarrierLoopAtO2(VelocityDeviceType.Scalar2);

    [TestMethod]
    public Task O2_BarrierLoop_SharedLoadStaysBetweenBarriers_VelocityVector128() => BarrierLoopAtO2(VelocityDeviceType.Vector128);
}
