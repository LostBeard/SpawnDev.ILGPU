using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Velocity;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// The Velocity backend (SIMD CPU: four lanes per warp through <c>Vector128</c>, SSE / AVX / AdvSimd) as a test lane. The plain CPU backend is ILGPU's debugging
/// backend and runs kernels one lane at a time; Velocity is the candidate for a CPU backend that does real work.
/// </summary>
public class VelocityTests : BackendTestBase
{
    public VelocityTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
    protected override string BackendName => "Velocity";

    protected override Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
    {
        var context = Context.Create(builder => builder
            .Velocity(VelocityDeviceType.Vector128)
            .EnableAlgorithms());
        var accelerator = context.GetVelocityDevice(0).CreateAccelerator(context);
        return Task.FromResult<(Context, Accelerator)>((context, accelerator));
    }
}
