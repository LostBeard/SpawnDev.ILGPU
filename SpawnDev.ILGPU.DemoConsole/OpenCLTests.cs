using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.OpenCL;
using SpawnDev.SpawnJS.Cryptography;
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
