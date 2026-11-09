using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Velocity;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU;
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

    // One thread per item running a long serial loop (data-dependent loads / stores, branches): the shape Velocity's
    // vectorized code runs ~60x slower than the IL backend at a few threads (SpawnDev.Codecs' VP9 parse, 2026-11-09).
    static void HostSerialKernel(Index1D i, ArrayView<int> data, ArrayView<int> output, int iters)
    {
        int acc = i;
        for (int k = 0; k < iters; k++)
        {
            int v = data[(acc + k) & 1023];
            if ((v & 1) != 0) acc = acc * 3 + v;
            else acc ^= v >> 2;
        }
        output[i] = acc;
    }

    static int HostSerialExpected(int i, int[] data, int iters)
    {
        int acc = i;
        for (int k = 0; k < iters; k++)
        {
            int v = data[(acc + k) & 1023];
            if ((v & 1) != 0) acc = acc * 3 + v;
            else acc ^= v >> 2;
        }
        return acc;
    }

    static void HostBarrierKernel(ArrayView<int> data, ArrayView<int> output)
    {
        var shared = SharedMemory.Allocate<int>(16);
        int t = Group.IdxX;
        shared[t] = data[Grid.GlobalIndex.X];
        Group.Barrier();
        output[Grid.GlobalIndex.X] = shared[(t + 1) % Group.DimX];
    }

    [TestMethod]
    public async Task HostLaunch_SmallLaunchesRunOnTheHost_SameResults() => await RunTest(async accelerator =>
    {
        var velocity = (VelocityAccelerator)accelerator;
        var data = new int[1024];
        var rnd = new Random(7);
        for (int i = 0; i < data.Length; i++) data[i] = rnd.Next();
        using var dData = accelerator.Allocate1D(data);
        using var dOut = accelerator.Allocate1D<int>(4096);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(HostSerialKernel);

        async Task Check(int threads, int iters, bool expectHost)
        {
            long before = velocity.HostLaunchCount;
            dOut.MemSetToZero();
            kernel(threads, dData.View, dOut.View, iters);
            await accelerator.SynchronizeAsync();
            long ran = velocity.HostLaunchCount - before;
            if (ran != (expectHost ? 1 : 0))
                throw new Exception($"{threads} threads (HostLaunchMaxThreads {velocity.HostLaunchMaxThreads}): {ran} host launch(es), expected {(expectHost ? 1 : 0)}");
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < threads; i++)
                if (got[i] != HostSerialExpected(i, data, iters))
                    throw new Exception($"{threads} threads: [{i}] = {got[i]}, expected {HostSerialExpected(i, data, iters)}");
        }

        await Check(1, 5000, expectHost: true);
        await Check(8, 2000, expectHost: true);
        await Check(4096, 50, expectHost: false);
        int saved = velocity.HostLaunchMaxThreads;
        velocity.HostLaunchMaxThreads = 0;
        try { await Check(1, 5000, expectHost: false); }
        finally { velocity.HostLaunchMaxThreads = saved; }

        // a kernel with a barrier + shared memory never takes the host path, however small
        using var bOut = accelerator.Allocate1D<int>(16);
        var barrierKernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(HostBarrierKernel);
        long beforeBarrier = velocity.HostLaunchCount;
        barrierKernel(new KernelConfig(1, 16), dData.View.SubView(0, 16), bOut.View);
        await accelerator.SynchronizeAsync();
        if (velocity.HostLaunchCount != beforeBarrier)
            throw new Exception("a barrier kernel took the host path");
        var b = await bOut.CopyToHostAsync<int>();
        for (int t = 0; t < 16; t++)
            if (b[t] != data[(t + 1) % 16]) throw new Exception($"barrier kernel [{t}] = {b[t]}, expected {data[(t + 1) % 16]}");
    });
}
