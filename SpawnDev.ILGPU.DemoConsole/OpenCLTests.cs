using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.OpenCL;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

public class OpenCLTests : BackendTestBase
{
    public OpenCLTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
    protected override string BackendName => "OpenCL";

    protected override Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
    {
        var context = Context.Create(builder => builder.AllAccelerators().EnableAlgorithms());
        var clDevices = context.GetCLDevices();
        if (clDevices.Count == 0)
        {
            context.Dispose();
            throw new UnsupportedTestException("No OpenCL devices found");
        }
        var accelerator = clDevices[0].CreateAccelerator(context);
        return Task.FromResult<(Context, Accelerator)>((context, accelerator));
    }

    static void HalfAbsKernel(Index1D i, ArrayView<ILGPU.Half> a, ArrayView<ILGPU.Half> o) =>
        o[i] = (ILGPU.Half)Math.Abs((float)a[i]);

    // At O2 InferKernelAddressSpaces types buffer pointers `global float*` (generic at O1). The emulated Half load/store
    // passed that float pointer to vload_half / vstore_half, which convert it to the generic and the global half overload
    // alike: "call to 'vload_half' is ambiguous", CL_BUILD_PROGRAM_FAILURE (42 OpenCL Half tests in the O2 sweep of
    // 2026-10-27). The pointer is now cast to a half pointer in its own address space. Builds its own O2 context because
    // the lane's accelerator runs at the default level.
    [TestMethod]
    public async Task O2_EmulatedHalf_LoadStore_Builds()
    {
        using var context = Context.Create(builder => builder.OpenCL().Optimize(OptimizationLevel.O2));
        var clDevices = context.GetCLDevices();
        if (clDevices.Count == 0)
            throw new UnsupportedTestException("No OpenCL devices found");
        using var accelerator = clDevices[0].CreateAccelerator(context);
        var input = new[] { -1.5f, 2f, -0.25f, 0f, -65504f };
        using var dIn = accelerator.Allocate1D(input.Select(f => (ILGPU.Half)f).ToArray());
        using var dOut = accelerator.Allocate1D<ILGPU.Half>(input.Length);
        accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ILGPU.Half>, ArrayView<ILGPU.Half>>(HalfAbsKernel)(
            input.Length, dIn.View, dOut.View);
        await accelerator.SynchronizeAsync();
        var got = await dOut.CopyToHostAsync<ILGPU.Half>();
        for (int i = 0; i < input.Length; i++)
            if ((float)got[i] != Math.Abs(input[i]))
                throw new Exception($"|{input[i]}| at O2 = {(float)got[i]}, expected {Math.Abs(input[i])}");
    }

    protected override void RequireFeature(Accelerator accelerator, string featureName, string? reason = null)
    {
        if (accelerator is not ILGPU.Runtime.OpenCL.CLAccelerator clAccel)
            return;

        if (featureName == "subgroups")
        {
            if (!clAccel.Capabilities.SubGroups)
                throw new UnsupportedTestException(reason ?? "Subgroup support not available on this OpenCL device");
        }
        else if (featureName == "subgroup_shuffle")
        {
            if (!clAccel.Capabilities.SubGroupShuffle)
                throw new UnsupportedTestException(reason ?? "Subgroup shuffle (Warp.Shuffle) requires cl_intel_subgroups or cl_khr_subgroup_shuffle on this OpenCL device");
        }
    }
}
