using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Velocity;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// The Velocity backend with 256-bit vectors: a wider device type of <see cref="VelocityTests"/>. It is
/// not registered in the default lane list (see Program.cs): it runs the same tests through a second set of operations.
/// </summary>
public class VelocityVector256Tests : BackendTestBase
{
    public VelocityVector256Tests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
    protected override string BackendName => "VelocityVector256";

    protected override Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
    {
        var context = Context.Create(builder => builder
            .Velocity(VelocityDeviceType.Vector256)
            .EnableAlgorithms());
        var accelerator = context.GetVelocityDevice(0).CreateAccelerator(context);
        return Task.FromResult<(Context, Accelerator)>((context, accelerator));
    }
}
