using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

public class CPUTests : BackendTestBase
{
    public CPUTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
    protected override string BackendName => "CPU";

    protected override Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
    {
        var context = Context.Create(builder => builder
            .CPU()
            .EnableAlgorithms());
        var accelerator = context.GetCPUDevice(0).CreateCPUAccelerator(context);
        return Task.FromResult<(Context, Accelerator)>((context, accelerator));
    }

}
