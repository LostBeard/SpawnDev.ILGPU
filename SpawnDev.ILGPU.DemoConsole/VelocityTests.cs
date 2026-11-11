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

        // a single wide launch is the adaptive choice's first (untimed, Velocity) warm-up: still not on the host
        // (AdaptiveHostLaunch_WideLaunchesChooseAPath_SameResults covers the choice itself)

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

    static void TeamCountBarrierKernel(ArrayView<int> data, ArrayView<int> output)
    {
        var shared = SharedMemory.Allocate<int>(256);
        int t = Group.IdxX;
        int g = Grid.GlobalIndex.X;
        shared[t] = data[g];
        Group.Barrier();
        int v = shared[(t + 1) % Group.DimX];
        Group.Barrier();
        shared[t] = v * 3 + Grid.IdxX;
        Group.Barrier();
        output[g] = shared[(t + Group.DimX - 1) % Group.DimX];
    }

    // A barrier kernel whose groups do not divide the processors evenly measures one extra team of warps against the
    // teams that fit (VelocityAccelerator.AdaptiveTeamCount; SpawnDev.Codecs' VP9 loop filter on Vector256 84.1 -> 64.7 ms
    // per frame, 2026-11-11). Which count wins depends on the kernel and the machine, so this checks the mechanics: groups
    // of (processors - 1) warps (1 team vs 2), the right results on every launch while measuring and after, a decision
    // after the 8 measuring launches (a warm-up and 3 timed launches per count), none with the option off.
    [TestMethod]
    public async Task AdaptiveTeamCount_GroupsThatDoNotDivideTheProcessors_DecideAndKeepResults() => await RunTest(async accelerator =>
    {
        var velocity = (VelocityAccelerator)accelerator;
        int processors = velocity.NumMultiprocessors, warp = velocity.WarpSize;
        int warps = Math.Min(processors - 1, 256 / warp);
        if (warps < 2 || processors % warps == 0)
            throw new UnsupportedTestException($"{processors} processors: no group shape where an extra team differs");
        int groupSize = warps * warp, groups = 4, n = groupSize * groups;
        if (!velocity.AdaptiveTeamCount) throw new Exception("AdaptiveTeamCount is off by default");
        var data = new int[n];
        var rnd = new Random(12);
        for (int i = 0; i < n; i++) data[i] = rnd.Next(1 << 20);
        using var dData = accelerator.Allocate1D(data);
        using var dOut = accelerator.Allocate1D<int>(n);
        var kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(TeamCountBarrierKernel);

        int Expected(int g)
        {
            int group = g / groupSize, t = g % groupSize;
            int prevT = (t + groupSize - 1) % groupSize;
            int v = data[group * groupSize + (prevT + 1) % groupSize];
            return v * 3 + group;
        }

        async Task Launch(int round)
        {
            dOut.MemSetToZero();
            kernel(new KernelConfig(groups, groupSize), dData.View, dOut.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            for (int g = 0; g < n; g++)
                if (got[g] != Expected(g))
                    throw new Exception($"launch {round} (groups of {warps} warps, {processors} processors): [{g}] = {got[g]}, expected {Expected(g)}");
        }

        long before = velocity.TeamCountDecisions;
        for (int r = 0; r < 8; r++)
        {
            await Launch(r);
            if (r < 7 && velocity.TeamCountDecisions != before)
                throw new Exception($"decided after {r + 1} launches, before measuring both team counts 3 times");
        }
        if (velocity.TeamCountDecisions != before + 1)
            throw new Exception($"{velocity.TeamCountDecisions - before} team-count decisions after 8 launches, expected 1");
        for (int r = 8; r < 12; r++) await Launch(r);

        velocity.AdaptiveTeamCount = false;
        try
        {
            long off = velocity.TeamCountDecisions;
            for (int r = 0; r < 9; r++) await Launch(100 + r);
            if (velocity.TeamCountDecisions != off) throw new Exception("with AdaptiveTeamCount off, a team count was still decided");
        }
        finally { velocity.AdaptiveTeamCount = true; }
    });

    // Wide launches of a barrier-free kernel run on whichever path measures faster for that kernel
    // (VelocityAccelerator.AdaptiveHostLaunches; SpawnDev.Codecs' VP9 reconstruction 51.2 -> 9.6 ms per frame on
    // Vector256, 2026-11-10). Which path wins depends on the kernel and the machine, so this checks the mechanics, not the
    // winner: the first 8 wide launches measure (one untimed warm-up per path, then 3 timed per path, alternating: 4 on
    // the host), then the kernel has decided and the next launches all take the chosen path; every launch gives the same
    // results either way; with the option off, wide launches stay on Velocity.
    [TestMethod]
    public async Task AdaptiveHostLaunch_WideLaunchesChooseAPath_SameResults() => await RunTest(async accelerator =>
    {
        var velocity = (VelocityAccelerator)accelerator;
        var data = new int[1024];
        var rnd = new Random(11);
        for (int i = 0; i < data.Length; i++) data[i] = rnd.Next();
        const int threads = 4096, iters = 200;
        using var dData = accelerator.Allocate1D(data);
        using var dOut = accelerator.Allocate1D<int>(threads);
        var kernel = accelerator.LoadAutoGroupedKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(HostSerialKernel);
        var velocityKernel = ((Delegate)kernel).Target as VelocityKernel
            ?? throw new Exception("the launcher delegate's target is not the VelocityKernel");
        if (!velocity.AdaptiveHostLaunches) throw new Exception("AdaptiveHostLaunches is off by default");
        if (velocityKernel.WideLaunchesOnHost != null) throw new Exception("a fresh kernel has already chosen a path");

        async Task Launch(int round)
        {
            dOut.MemSetToZero();
            kernel(accelerator.DefaultStream, threads, dData.View, dOut.View, iters);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < threads; i++)
                if (got[i] != HostSerialExpected(i, data, iters))
                    throw new Exception($"launch {round} (host launches so far {velocity.HostLaunchCount}): [{i}] = {got[i]}, expected {HostSerialExpected(i, data, iters)}");
        }

        long before = velocity.HostLaunchCount;
        for (int r = 0; r < 8; r++)
        {
            await Launch(r);
            if (r < 7 && velocityKernel.WideLaunchesOnHost != null)
                throw new Exception($"decided after {r + 1} wide launches, before measuring both paths 3 times");
        }
        var onHost = velocityKernel.WideLaunchesOnHost
            ?? throw new Exception("no path chosen after 8 wide launches");
        long measuring = velocity.HostLaunchCount - before;
        if (measuring != 4) throw new Exception($"{measuring} of the 8 measuring launches ran on the host, expected 4 (alternating)");
        for (int r = 8; r < 12; r++) await Launch(r);
        long chosen = velocity.HostLaunchCount - before - measuring;
        if (chosen != (onHost ? 4 : 0))
            throw new Exception($"chose {(onHost ? "the host" : "Velocity")}, but {chosen} of the next 4 launches ran on the host");

        velocity.AdaptiveHostLaunches = false;
        try
        {
            var other = accelerator.LoadAutoGroupedKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(HostSerialKernel);
            long off = velocity.HostLaunchCount;
            for (int r = 0; r < 3; r++)
            {
                other(accelerator.DefaultStream, threads, dData.View, dOut.View, iters);
                await accelerator.SynchronizeAsync();
            }
            if (velocity.HostLaunchCount != off) throw new Exception("with AdaptiveHostLaunches off, a wide launch ran on the host");
        }
        finally { velocity.AdaptiveHostLaunches = true; }
    });
}
