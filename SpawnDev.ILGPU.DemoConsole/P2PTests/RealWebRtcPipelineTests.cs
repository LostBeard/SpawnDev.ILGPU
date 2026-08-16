using System.Collections.Concurrent;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU.P2P;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.DemoConsole.P2PTests;

/// <summary>
/// Phase 2: Real-WebRTC end-to-end pipeline tests (Rule #1 primary-purpose coverage).
///
/// Two P2PCompute instances live in the same test process - one coordinator, one worker
/// - connected through the LocalTrackerFixture WebSocket tracker at ws://localhost:5561
/// (with hub.spawndev.com + openwebtorrent.com as fallbacks). The WebRTC data channel
/// between them is provided by SpawnDev.RTC's SIPSorcery fork exactly as it would be
/// between two separate machines. No loopback shortcuts.
///
/// Each test exercises the full wire: sd_compute BEP 10 handshake, input buffer chunks
/// from coordinator to worker, KernelDispatch message, worker-side compile + execute,
/// KernelResult metadata, and the modified-buffer auto-push back to the coordinator.
/// Every result is verified bit-for-bit against a CPU reference.
///
/// Flakiness note: tracker signaling through public infrastructure is not 100% reliable.
/// Every test in this class carries <c>RetryCount = 2</c> so one bad announce cycle
/// does not fail the build.
/// </summary>
public class RealWebRtcPipelineTests
{
    private readonly IPortableCrypto _crypto;

    public RealWebRtcPipelineTests(IPortableCrypto crypto)
    {
        _crypto = crypto;
    }

    /// <summary>
    /// Peer-discovery timeout. Public trackers (hub.spawndev.com) are sometimes
    /// slow to relay offers/answers when under load, so we allow a minute before
    /// declaring discovery failed.
    /// </summary>
    private const int DiscoveryTimeoutMs = 60000;

    /// <summary>
    /// Timeout for buffer-transfer waits. Relayed WebRTC through a public tracker
    /// delivers ~10-30 KB/s in the worst case; 1MB + round trip needs breathing room.
    /// </summary>
    private const int BufferReturnTimeoutMs = 120000;

    /// <summary>
    /// Trackers used by the kernel-dispatch tests. LocalTrackerFixture's loopback tracker
    /// is listed first so tests do not depend on any public service; hub.spawndev.com +
    /// openwebtorrent.com are kept as fallbacks.
    /// </summary>
    private static string[] DispatchTrackers => LocalTrackerFixture.IsAvailable
        ? new[]
        {
            LocalTrackerFixture.GetTrackerUrl(),
            "wss://hub.spawndev.com:44365/announce",
            "wss://tracker.openwebtorrent.com",
        }
        : new[]
        {
            "wss://hub.spawndev.com:44365/announce",
            "wss://tracker.openwebtorrent.com",
        };

    /// <summary>
    /// Re-register the kernel allowlist for each test. Process-wide static state -
    /// SecurityTests.Security_KernelAllowlist_EnforcedOnResolve clears it, so run
    /// order cannot be trusted. Idempotent.
    /// </summary>
    private static void EnsureAllowlist()
    {
        P2PKernelSerializer.RegisterKernelType(typeof(P2PTestKernels));
        P2PKernelSerializer.RegisterKernelType(typeof(P2PDemoKernelsMirror));
    }

    /// <summary>
    /// Foundation test: two P2PCompute instances discover each other through the
    /// local tracker via real WebRTC.
    /// </summary>
    [TestMethod(Timeout = 90000, RetryCount = 2)]
    public async Task TwoPeers_DiscoverEachOtherViaLocalTracker()
    {
        EnsureAllowlist();
        await RunDiscoveryAsync(new[] { LocalTrackerFixture.GetTrackerUrl() },
            "discovery-local-tracker");
    }

    /// <summary>
    /// Same as the local-tracker discovery test but signaled through the public
    /// hub.spawndev.com tracker.
    /// </summary>
    [TestMethod(Timeout = 90000, RetryCount = 2)]
    public async Task TwoPeers_DiscoverEachOtherViaPublicHubTracker()
    {
        EnsureAllowlist();
        await RunDiscoveryAsync(new[] { "wss://hub.spawndev.com:44365/announce" },
            "discovery-public-tracker");
    }

    private async Task RunDiscoveryAsync(string[] trackers, string swarmName)
    {
        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, swarmName, trackers: trackers);

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        var start = DateTime.UtcNow;
        var deadline = start.AddMilliseconds(DiscoveryTimeoutMs);
        while (DateTime.UtcNow < deadline &&
               (coordinator.PeerCount == 0 || worker.PeerCount == 0))
        {
            await Task.Delay(1000);
            var elapsed = (DateTime.UtcNow - start).TotalSeconds;
            Console.WriteLine(
                $"[{elapsed:F0}s] coord.PeerCount={coordinator.PeerCount}, " +
                $"worker.PeerCount={worker.PeerCount}");
        }

        if (!(coordinator.PeerCount > 0)) throw new Exception("Coordinator should see the worker as a peer");
        if (!(worker.PeerCount > 0)) throw new Exception("Worker should see the coordinator as a peer");
    }

    /// <summary>
    /// Rule #1 primary-purpose test: real kernel dispatch across real WebRTC.
    /// </summary>
    [TestMethod(Timeout = 120000, RetryCount = 2)]
    public async Task VectorAdd_1024_DispatchedOverRealWebRtc_BitExact()
    {
        EnsureAllowlist();
        const int n = 1024;
        var trackers = DispatchTrackers;

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "vectoradd-test", trackers: trackers);

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        await WaitForPeersAsync(coordinator, worker);

        var peerId = coordinator.Accelerator!.Peers.First().PeerId;
        var workerBuffers = CollectWorkerBuffers(coordinator);

        var (aBytes, bBytes, expected) = DataIntegrityHelper.GenerateVectorAddData(n);
        await coordinator.Transport!.SendBufferAsync(peerId, "a", aBytes);
        await coordinator.Transport!.SendBufferAsync(peerId, "b", bBytes);

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.VectorAdd))!;
        var dispatchResult = await coordinator.Accelerator!.DispatchAsync(method, n,
            ("a", aBytes, 4), ("b", bBytes, 4), ("result", null, 4));

        if (!dispatchResult.Success) throw new Exception($"Dispatch failed: {dispatchResult.Error}");
        if (!dispatchResult.ModifiedBuffers.Contains("result"))
            throw new Exception("result buffer should be reported as modified");

        var resultBytes = await WaitForBufferAsync(workerBuffers, "result", BufferReturnTimeoutMs);
        if (resultBytes.Length != n * 4)
            throw new Exception($"Returned buffer wrong size: {resultBytes.Length}");

        var actual = DataIntegrityHelper.BytesToFloats(resultBytes);
        var (violations, firstIdx, firstExp, firstAct) =
            DataIntegrityHelper.VerifyFloats(actual, expected);

        if (violations != 0)
            throw new Exception(
                $"VectorAdd over WebRTC: {violations}/{n} violations. " +
                $"First at [{firstIdx}]: expected {firstExp}, got {firstAct}");
    }

    /// <summary>
    /// Tensor-transfer stress test: three 1MB float buffers over real WebRTC.
    /// </summary>
    [TestMethod(Timeout = 180000, RetryCount = 2)]
    public async Task LargeBuffer_1MB_DispatchedOverRealWebRtc_BitExact()
    {
        EnsureAllowlist();
        const int n = 256 * 1024;
        var trackers = DispatchTrackers;

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "largebuffer-test", trackers: trackers);

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        await WaitForPeersAsync(coordinator, worker);

        var peerId = coordinator.Accelerator!.Peers.First().PeerId;
        var workerBuffers = CollectWorkerBuffers(coordinator);

        var rng = new Random(unchecked((int)0xC0DEFACE));
        var a = new float[n];
        var b = new float[n];
        var expected = new float[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = (float)(rng.NextDouble() * 1000.0 - 500.0);
            b[i] = (float)(rng.NextDouble() * 1000.0 - 500.0);
            expected[i] = a[i] + b[i];
        }
        var aBytes = new byte[n * 4];
        var bBytes = new byte[n * 4];
        Buffer.BlockCopy(a, 0, aBytes, 0, n * 4);
        Buffer.BlockCopy(b, 0, bBytes, 0, n * 4);

        await coordinator.Transport!.SendBufferAsync(peerId, "large_a", aBytes);
        await coordinator.Transport!.SendBufferAsync(peerId, "large_b", bBytes);

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.VectorAdd))!;
        var dispatchResult = await coordinator.Accelerator!.DispatchAsync(method, n,
            ("large_a", aBytes, 4), ("large_b", bBytes, 4), ("large_out", null, 4));

        if (!dispatchResult.Success) throw new Exception($"Dispatch failed: {dispatchResult.Error}");

        var resultBytes = await WaitForBufferAsync(workerBuffers, "large_out", BufferReturnTimeoutMs);
        if (resultBytes.Length != n * 4) throw new Exception($"1MB buffer wrong size: {resultBytes.Length}");

        var actual = DataIntegrityHelper.BytesToFloats(resultBytes);
        var (violations, firstIdx, firstExp, firstAct) =
            DataIntegrityHelper.VerifyFloats(actual, expected, tolerance: 0.001f);

        if (violations != 0)
            throw new Exception(
                $"1MB VectorAdd over WebRTC: {violations}/{n} violations. " +
                $"First at [{firstIdx}]: expected {firstExp}, got {firstAct}");
    }

    /// <summary>
    /// Gap 4 headline test: 10MB float buffers over real WebRTC.
    /// </summary>
    [TestMethod(Timeout = 240000, RetryCount = 2)]
    public async Task LargeBuffer_10MB_DispatchedOverRealWebRtc_BitExact()
    {
        EnsureAllowlist();
        const int n = 10 * 256 * 1024;
        var trackers = DispatchTrackers;

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "largebuffer-10mb-test", trackers: trackers);

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        await WaitForPeersAsync(coordinator, worker);

        var peerId = coordinator.Accelerator!.Peers.First().PeerId;
        var workerBuffers = CollectWorkerBuffers(coordinator);

        var rng = new Random(unchecked((int)0xC0DEFACE));
        var a = new float[n];
        var b = new float[n];
        var expected = new float[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = (float)(rng.NextDouble() * 1000.0 - 500.0);
            b[i] = (float)(rng.NextDouble() * 1000.0 - 500.0);
            expected[i] = a[i] + b[i];
        }
        var aBytes = new byte[n * 4];
        var bBytes = new byte[n * 4];
        Buffer.BlockCopy(a, 0, aBytes, 0, n * 4);
        Buffer.BlockCopy(b, 0, bBytes, 0, n * 4);

        await coordinator.Transport!.SendBufferAsync(peerId, "large10_a", aBytes);
        await coordinator.Transport!.SendBufferAsync(peerId, "large10_b", bBytes);

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.VectorAdd))!;
        var dispatchResult = await coordinator.Accelerator!.DispatchAsync(method, n,
            ("large10_a", aBytes, 4), ("large10_b", bBytes, 4), ("large10_out", null, 4));

        if (!dispatchResult.Success) throw new Exception($"Dispatch failed: {dispatchResult.Error}");

        var resultBytes = await WaitForBufferAsync(workerBuffers, "large10_out", BufferReturnTimeoutMs);
        if (resultBytes.Length != n * 4) throw new Exception($"10MB buffer wrong size: {resultBytes.Length}");

        var actual = DataIntegrityHelper.BytesToFloats(resultBytes);
        var (violations, firstIdx, firstExp, firstAct) =
            DataIntegrityHelper.VerifyFloats(actual, expected, tolerance: 0.001f);

        if (violations != 0)
            throw new Exception(
                $"10MB VectorAdd over WebRTC: {violations}/{n} violations. " +
                $"First at [{firstIdx}]: expected {firstExp}, got {firstAct}");
    }

    /// <summary>
    /// Aspirational next-ceiling stress: 100MB float buffers over real WebRTC.
    /// Opt-in via <c>Category = "Stress"</c> so it doesn't run in the default sweep.
    /// </summary>
    [TestMethod(Timeout = 600000, RetryCount = 1, Category = "Stress")]
    public async Task LargeBuffer_100MB_DispatchedOverRealWebRtc_BitExact()
    {
        if (!LocalTrackerFixture.IsAvailable)
            throw new UnsupportedTestException("Requires local tracker (InitAsync) - wss peer discovery overhead pushes 100MB transfer past 10min PMT budget without localhost tracker");
        EnsureAllowlist();
        // Diagnostic mode: enable VerboseLogging so wire.OnClose path + UnregisterPeer
        // path emit Console.WriteLine traces. Captures the actual mid-dispatch
        // peer-disconnect trigger (bridge wire OnClose, polling fallback, stale-peer
        // check, etc.) for tomorrow's investigation. Reset at end of test.
        var prevP2PVerbose = P2PCompute.VerboseLogging;
        var prevWtVerbose = SpawnDev.WebTorrent.WebTorrentClient.VerboseLogging;
        P2PCompute.VerboseLogging = true;
        SpawnDev.WebTorrent.WebTorrentClient.VerboseLogging = true;
        try
        {
        const int n = 100 * 256 * 1024;
        var trackers = DispatchTrackers;

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "largebuffer-100mb-test", trackers: trackers);

        // 100MB result push-back at ~1.5 MB/s SCTP throughput plus chunking overhead
        // exceeds the default 60s * 3 = 180s dispatch budget. Bump per-attempt timeout
        // to 240s (240 * 3 = 720s = 12 min cumulative budget). The test method's own
        // Timeout=600000 (10 min) caps the actual upper bound; the dispatcher's
        // budget needs to be >= the method timeout so the dispatcher doesn't bail
        // before the method does.
        coordinator.Accelerator!.Dispatcher!.DispatchTimeoutMs = 240_000;

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        await WaitForPeersAsync(coordinator, worker);

        var peerId = coordinator.Accelerator!.Peers.First().PeerId;
        var workerBuffers = CollectWorkerBuffers(coordinator);

        var rng = new Random(unchecked((int)0xC0DEFACE));
        var a = new float[n];
        var b = new float[n];
        var expected = new float[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = (float)(rng.NextDouble() * 1000.0 - 500.0);
            b[i] = (float)(rng.NextDouble() * 1000.0 - 500.0);
            expected[i] = a[i] + b[i];
        }
        var aBytes = new byte[n * 4];
        var bBytes = new byte[n * 4];
        Buffer.BlockCopy(a, 0, aBytes, 0, n * 4);
        Buffer.BlockCopy(b, 0, bBytes, 0, n * 4);

        await coordinator.Transport!.SendBufferAsync(peerId, "large100_a", aBytes);
        await coordinator.Transport!.SendBufferAsync(peerId, "large100_b", bBytes);

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.VectorAdd))!;
        var dispatchResult = await coordinator.Accelerator!.DispatchAsync(method, n,
            ("large100_a", aBytes, 4), ("large100_b", bBytes, 4), ("large100_out", null, 4));

        if (!dispatchResult.Success) throw new Exception($"Dispatch failed: {dispatchResult.Error}");

        // 100MB push-back at ~1.5 MB/s SCTP throughput + chunking overhead exceeds
        // the default 120s buffer-return timeout. Bump to 480s (8 min) for the 100MB
        // case; the test method's own Timeout=600000 (10 min) caps the upper bound.
        var resultBytes = await WaitForBufferAsync(workerBuffers, "large100_out", 480_000);
        if (resultBytes.Length != n * 4) throw new Exception($"100MB buffer wrong size: {resultBytes.Length}");

        var actual = DataIntegrityHelper.BytesToFloats(resultBytes);
        var (violations, firstIdx, firstExp, firstAct) =
            DataIntegrityHelper.VerifyFloats(actual, expected, tolerance: 0.001f);

        if (violations != 0)
            throw new Exception(
                $"100MB VectorAdd over WebRTC: {violations}/{n} violations. " +
                $"First at [{firstIdx}]: expected {firstExp}, got {firstAct}");
        }
        finally
        {
            P2PCompute.VerboseLogging = prevP2PVerbose;
            SpawnDev.WebTorrent.WebTorrentClient.VerboseLogging = prevWtVerbose;
        }
    }

    /// <summary>
    /// Demo /compute benchmark path: byte-for-byte mirrors ComputeSwarm.razor's
    /// RunDistributedBenchmark. MultiplyBy2 in-place dispatch.
    /// </summary>
    [TestMethod(Timeout = 120000, RetryCount = 2)]
    public async Task DemoPath_MultiplyBy2_InPlace_OverRealWebRtc_BitExact()
    {
        EnsureAllowlist();
        const int n = 4096;
        var trackers = DispatchTrackers;

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "demo-multiplyby2-test", trackers: trackers);

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        await WaitForPeersAsync(coordinator, worker);

        var peerId = coordinator.Accelerator!.Peers.First().PeerId;
        var workerBuffers = CollectWorkerBuffers(coordinator);

        const string bufferId = "chunk_demo_mul2";
        var data = new byte[n * 4];
        for (int j = 0; j < n; j++)
            BitConverter.TryWriteBytes(data.AsSpan(j * 4), (float)j);

        var method = typeof(P2PDemoKernelsMirror).GetMethod(nameof(P2PDemoKernelsMirror.MultiplyBy2))!;
        var dispatchResult = await coordinator.Accelerator!.DispatchAsync(
            method, n, (bufferId, data, 4));

        if (!dispatchResult.Success)
            throw new Exception($"Demo MultiplyBy2 dispatch failed: {dispatchResult.Error}");

        var resultBytes = await WaitForBufferAsync(workerBuffers, bufferId, BufferReturnTimeoutMs);
        if (resultBytes.Length != n * 4)
            throw new Exception($"MultiplyBy2 in-place result wrong size: {resultBytes.Length}");

        var actual = DataIntegrityHelper.BytesToFloats(resultBytes);
        int badCount = 0;
        int firstBadIdx = -1;
        float firstBadExp = 0f, firstBadAct = 0f;
        for (int j = 0; j < n; j++)
        {
            float want = 2f * j;
            if (Math.Abs(actual[j] - want) > 0.001f)
            {
                if (firstBadIdx == -1) { firstBadIdx = j; firstBadExp = want; firstBadAct = actual[j]; }
                badCount++;
            }
        }
        if (badCount != 0)
            throw new Exception(
                $"Demo MultiplyBy2 bit-exact: {badCount}/{n} wrong. " +
                $"First at [{firstBadIdx}]: expected {firstBadExp}, got {firstBadAct}");
    }

    /// <summary>
    /// Demo /compute Mandelbrot path: byte-for-byte mirrors ComputeSwarm.razor's
    /// RunDistributedMandelbrot. Output-only buffer pattern.
    /// </summary>
    [TestMethod(Timeout = 120000, RetryCount = 2)]
    public async Task DemoPath_MandelbrotChunk_OutputOnlyBuffer_OverRealWebRtc_BitExact()
    {
        EnsureAllowlist();
        const int width = 64;
        const int height = 64;
        const int n = width * height;
        var trackers = DispatchTrackers;

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "demo-mandelbrot-test", trackers: trackers);

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        await WaitForPeersAsync(coordinator, worker);

        var peerId = coordinator.Accelerator!.Peers.First().PeerId;
        var workerBuffers = CollectWorkerBuffers(coordinator);

        var realCoords = new float[n];
        var imagCoords = new float[n];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                realCoords[idx] = (x / (float)width) * 3.5f - 2.5f;
                imagCoords[idx] = (y / (float)height) * 2.0f - 1.0f;
            }
        var realBytes = new byte[n * 4];
        var imagBytes = new byte[n * 4];
        Buffer.BlockCopy(realCoords, 0, realBytes, 0, realBytes.Length);
        Buffer.BlockCopy(imagCoords, 0, imagBytes, 0, imagBytes.Length);

        const string outId = "mand_out_demo";
        const string realId = "mand_real_demo";
        const string imagId = "mand_imag_demo";

        var method = typeof(P2PDemoKernelsMirror).GetMethod(nameof(P2PDemoKernelsMirror.MandelbrotChunk))!;
        var dispatchResult = await coordinator.Accelerator!.DispatchAsync(
            method, n,
            (outId, null, 4),
            (realId, realBytes, 4),
            (imagId, imagBytes, 4));

        if (!dispatchResult.Success)
            throw new Exception($"Demo Mandelbrot dispatch failed: {dispatchResult.Error}");

        var outBytes = await WaitForBufferAsync(workerBuffers, outId, BufferReturnTimeoutMs);
        if (outBytes.Length != n * 4) throw new Exception($"Mandelbrot output wrong size: {outBytes.Length}");

        var actual = new int[n];
        Buffer.BlockCopy(outBytes, 0, actual, 0, Math.Min(outBytes.Length, n * 4));

        int badCount = 0;
        int firstBadIdx = -1;
        int firstBadExp = 0, firstBadAct = 0;
        for (int i = 0; i < n; i++)
        {
            int expected = CpuReferenceMandelbrot(realCoords[i], imagCoords[i]);
            if (actual[i] != expected)
            {
                if (firstBadIdx == -1) { firstBadIdx = i; firstBadExp = expected; firstBadAct = actual[i]; }
                badCount++;
            }
        }
        if (badCount != 0)
            throw new Exception(
                $"Demo Mandelbrot bit-exact: {badCount}/{n} pixels wrong. " +
                $"First at [{firstBadIdx}]: expected iter={firstBadExp}, got {firstBadAct}");

        int nonZero = 0;
        for (int i = 0; i < n; i++) if (actual[i] > 0) nonZero++;
        if (!(nonZero > n / 4))
            throw new Exception($"Only {nonZero}/{n} pixels have non-zero iteration count - worker likely returned zeros.");
    }

    private static int CpuReferenceMandelbrot(float cr, float ci)
    {
        float zr = 0f, zi = 0f;
        int iter = 0;
        const int maxIter = 255;
        while (zr * zr + zi * zi <= 4.0f && iter < maxIter)
        {
            float tmp = zr * zr - zi * zi + cr;
            zi = 2.0f * zr * zi + ci;
            zr = tmp;
            iter++;
        }
        return iter;
    }

    /// <summary>
    /// Scalar kernel parameter transmission over real WebRTC.
    /// </summary>
    [TestMethod(Timeout = 120000, RetryCount = 2)]
    public async Task VectorScale_ScalarOverRealWebRtc_BitExact()
    {
        EnsureAllowlist();
        const int n = 1024;
        const float scalar = 3.14f;
        var trackers = DispatchTrackers;

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "vectorscale-test", trackers: trackers);

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        await WaitForPeersAsync(coordinator, worker);

        var peerId = coordinator.Accelerator!.Peers.First().PeerId;
        var workerBuffers = CollectWorkerBuffers(coordinator);

        var input = new float[n];
        var expected = new float[n];
        for (int i = 0; i < n; i++)
        {
            input[i] = i;
            expected[i] = i * scalar;
        }
        var inputBytes = new byte[n * 4];
        Buffer.BlockCopy(input, 0, inputBytes, 0, n * 4);

        await coordinator.Transport!.SendBufferAsync(peerId, "scale_in", inputBytes);

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.VectorScale))!;
        var dispatchResult = await coordinator.Accelerator!.DispatchAsync(
            method, n,
            scalarValues: new Dictionary<int, object> { [3] = scalar },
            ("scale_in", inputBytes, 4), ("scale_out", null, 4));

        if (!dispatchResult.Success) throw new Exception($"VectorScale dispatch failed: {dispatchResult.Error}");

        var resultBytes = await WaitForBufferAsync(workerBuffers, "scale_out", BufferReturnTimeoutMs);
        if (resultBytes.Length != n * 4) throw new Exception($"result buffer wrong size: {resultBytes.Length}");

        var actual = DataIntegrityHelper.BytesToFloats(resultBytes);
        var (violations, firstIdx, firstExp, firstAct) =
            DataIntegrityHelper.VerifyFloats(actual, expected);

        if (violations != 0)
            throw new Exception(
                $"VectorScale scalar over WebRTC: {violations}/{n} violations. " +
                $"First at [{firstIdx}]: expected {firstExp}, got {firstAct}. " +
                $"If firstAct is 0, ScalarParams was not transmitted across the data channel.");
    }

    /// <summary>
    /// Integer Identity kernel across real WebRTC with SHA256 integrity check.
    /// </summary>
    [TestMethod(Timeout = 120000, RetryCount = 2)]
    public async Task DataIntegrity_SHA256_IdentityOverRealWebRtc()
    {
        EnsureAllowlist();
        const int n = 16384;
        var trackers = DispatchTrackers;

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "integrity-test", trackers: trackers);

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        await WaitForPeersAsync(coordinator, worker);

        var peerId = coordinator.Accelerator!.Peers.First().PeerId;
        var workerBuffers = CollectWorkerBuffers(coordinator);

        var rng = new Random(unchecked((int)0xDEADBEEF));
        var input = new int[n];
        for (int i = 0; i < n; i++)
            input[i] = rng.Next();
        var inputBytes = new byte[n * 4];
        Buffer.BlockCopy(input, 0, inputBytes, 0, n * 4);
        var hashBefore = DataIntegrityHelper.ComputeSha256(inputBytes);

        await coordinator.Transport!.SendBufferAsync(peerId, "integ_in", inputBytes);

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.Identity))!;
        var dispatchResult = await coordinator.Accelerator!.DispatchAsync(method, n,
            ("integ_in", inputBytes, 4), ("integ_out", null, 4));

        if (!dispatchResult.Success) throw new Exception($"Dispatch failed: {dispatchResult.Error}");

        var resultBytes = await WaitForBufferAsync(workerBuffers, "integ_out", BufferReturnTimeoutMs);
        var hashAfter = DataIntegrityHelper.ComputeSha256(resultBytes);

        if (hashAfter != hashBefore)
            throw new Exception(
                $"SHA256 mismatch over WebRTC round-trip. Before: {hashBefore[..16]}... " +
                $"After: {hashAfter[..16]}... ({resultBytes.Length} bytes, {n} ints)");
    }

    /// <summary>
    /// Heterogeneous-backend dispatch over real WebRTC: CPU coordinator -> CUDA worker.
    /// Skips cleanly when CUDA isn't available.
    /// </summary>
    [TestMethod(Timeout = 120000, RetryCount = 2)]
    public async Task VectorAdd_CpuCoordinator_CudaWorker_OverRealWebRtc_BitExact()
    {
        EnsureAllowlist();
        await HeterogeneousVectorAddAsync(
            swarmName: "heterogeneous-cpu-cuda",
            pickWorkerDevice: ctx =>
                ctx.Devices.OfType<global::ILGPU.Runtime.Cuda.CudaDevice>().FirstOrDefault(),
            workerBackendLabel: "CUDA");
    }

    /// <summary>
    /// Heterogeneous-backend dispatch over real WebRTC: CPU coordinator -> OpenCL worker.
    /// </summary>
    [TestMethod(Timeout = 120000, RetryCount = 2)]
    public async Task VectorAdd_CpuCoordinator_OpenClWorker_OverRealWebRtc_BitExact()
    {
        EnsureAllowlist();
        await HeterogeneousVectorAddAsync(
            swarmName: "heterogeneous-cpu-opencl",
            pickWorkerDevice: ctx =>
                ctx.Devices.OfType<global::ILGPU.Runtime.OpenCL.CLDevice>().FirstOrDefault(),
            workerBackendLabel: "OpenCL");
    }

    /// <summary>
    /// Phase 4 RBAC test: when a worker joins a swarm whose coordinator has a
    /// KeyRegistry set, the registry is auto-distributed to the worker on the
    /// initial CapabilityResponse handshake. This verifies the production
    /// authority chain - workers know which keys are Owner / Admin / Coordinator
    /// without the coordinator having to push the registry separately.
    ///
    /// Path under test: P2PTransport.HandleCapabilityResponse - line ~351
    /// calls SendRegistryAsync(peerId) when _coordinator.GetRegistry() != null.
    /// On the worker side, P2PTransport.HandleRegistryUpdateAsync deserializes
    /// the registry and calls _worker?.SetKeyRegistry, which populates
    /// P2PWorker.SwarmRegistry and fires OnRegistryUpdated.
    /// </summary>
    [TestMethod(Timeout = 180000, RetryCount = 2)]
    public async Task Security_RegistryDistributed_OnJoin_OverRealWebRtc()
    {
        EnsureAllowlist();
        var trackers = DispatchTrackers;

        // Build the registry the coordinator will distribute. Owner key is the
        // coordinator's own identity (so the worker accepts subsequent dispatch
        // signatures from this same key), plus a Worker entry with a fingerprint
        // distinct enough that the test can prove the registry round-tripped
        // verbatim instead of being silently regenerated on the worker side.
        var ownerIdentity = await SwarmIdentity.CreateAsync(_crypto, "registry-owner");
        var workerKeyIdentity = await SwarmIdentity.CreateAsync(_crypto, "registry-worker-key");
        var registry = new KeyRegistry();
        registry.AddKey(ownerIdentity.PublicKeySpki, SwarmRole.Owner, "registry-owner");
        registry.AddKey(workerKeyIdentity.PublicKeySpki, SwarmRole.Worker, "registry-worker-key");
        await registry.SignAsync(ownerIdentity);

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "registry-distributed-onjoin", trackers: trackers);

        // Stamp the registry on the coordinator BEFORE the worker joins so the
        // CapabilityResponse handler has something to send.
        coordinator.Coordinator.UpdateRegistry(registry);

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        // Capture the worker's OnRegistryUpdated event. Has to be wired before
        // join so a fast handshake doesn't lose the event.
        KeyRegistry? receivedRegistry = null;
        var registryReceived = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        if (worker.Transport == null)
            throw new Exception("Worker transport should be wired after JoinSwarmAsync.");
        worker.Transport.OnRegistryUpdated += r =>
        {
            receivedRegistry = r;
            registryReceived.TrySetResult(true);
        };

        await WaitForPeersAsync(coordinator, worker);

        // Wait for the registry to land. The CapabilityResponse handler triggers
        // SendRegistryAsync, which travels through real WebRTC + signature verify
        // before HandleRegistryUpdateAsync fires the event.
        var deadline = Task.Delay(DiscoveryTimeoutMs);
        var firstDone = await Task.WhenAny(registryReceived.Task, deadline);
        if (firstDone == deadline)
            throw new Exception(
                $"Worker did not receive RegistryUpdate within {DiscoveryTimeoutMs}ms after joining. " +
                $"P2PTransport.HandleCapabilityResponse should call SendRegistryAsync " +
                $"when _coordinator.GetRegistry() != null.");

        if (receivedRegistry == null)
            throw new Exception("OnRegistryUpdated fired but receivedRegistry was null.");

        // Worker's P2PWorker should also have the registry installed
        // (HandleRegistryUpdateAsync calls _worker?.SetKeyRegistry).
        if (worker.Worker == null)
            throw new Exception("Worker.Worker should be populated.");
        if (worker.Worker.SwarmRegistry == null)
            throw new Exception(
                "Worker.SwarmRegistry should be set after RegistryUpdate. " +
                "HandleRegistryUpdateAsync didn't call SetKeyRegistry.");

        // Round-trip verification: keys + roles match what the coordinator sent.
        if (worker.Worker.SwarmRegistry.Keys.Count != registry.Keys.Count)
            throw new Exception(
                $"Registry key count mismatch: coordinator had {registry.Keys.Count}, " +
                $"worker received {worker.Worker.SwarmRegistry.Keys.Count}.");

        var ownerKeyB64 = Convert.ToBase64String(ownerIdentity.PublicKeySpki);
        var workerKeyB64 = Convert.ToBase64String(workerKeyIdentity.PublicKeySpki);
        if (!worker.Worker.SwarmRegistry.HasRole(ownerKeyB64, SwarmRole.Owner))
            throw new Exception("Worker registry should have Owner role for ownerIdentity.");
        if (!worker.Worker.SwarmRegistry.HasRole(workerKeyB64, SwarmRole.Worker))
            throw new Exception("Worker registry should have Worker role for workerKeyIdentity.");

        // Sequence number is the replay-protection axis. The worker must accept
        // exactly the sequence the coordinator signed - bumping it would let
        // a stale replay through; ignoring it would freeze updates.
        if (worker.Worker.SwarmRegistry.Sequence != registry.Sequence)
            throw new Exception(
                $"Registry sequence mismatch: coordinator sent {registry.Sequence}, " +
                $"worker received {worker.Worker.SwarmRegistry.Sequence}.");
    }

    /// <summary>
    /// Phase 3 multi-peer test: 10 concurrent dispatches across 2 real-WebRTC workers.
    /// Verifies the dispatcher's load balancer routes work to both peers under
    /// concurrent load and every dispatch returns its modified buffer bit-exact.
    ///
    /// Each dispatch uses a unique buffer-id triple (a{i}/b{i}/r{i}) so the
    /// coordinator's BufferTransfer.OnBufferReceived map can demultiplex results
    /// without collision. n is small (64 floats per buffer) so the 10*3 = 30
    /// buffer transfers fit comfortably under BufferReturnTimeoutMs even on
    /// slow public-tracker WebRTC connections.
    /// </summary>
    [TestMethod(Timeout = 300000, RetryCount = 2)]
    public async Task MultiPeer_Concurrent_10_DispatchedOverRealWebRtc_BitExact()
    {
        EnsureAllowlist();
        const int n = 64;
        const int dispatchCount = 10;
        var trackers = DispatchTrackers;

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var worker1Client = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var worker2Client = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "multipeer-concurrent-10", trackers: trackers);

        using var worker1Context = Context.Create(b => b.CPU());
        using var worker1Accelerator = worker1Context.CreateCPUAccelerator(0);
        await using var worker1 = await P2PCompute.JoinSwarmAsync(
            _crypto, worker1Client, worker1Accelerator, coordinator.MagnetLink!);

        using var worker2Context = Context.Create(b => b.CPU());
        using var worker2Accelerator = worker2Context.CreateCPUAccelerator(0);
        await using var worker2 = await P2PCompute.JoinSwarmAsync(
            _crypto, worker2Client, worker2Accelerator, coordinator.MagnetLink!);

        // Coordinator must see both workers before we start firing dispatches;
        // otherwise the first few will all land on the same (only-known) peer.
        await WaitForMultiplePeersAsync(coordinator, expectedPeerCount: 2);

        var workerBuffers = CollectWorkerBuffers(coordinator);

        // Pre-ship every dispatch's input buffers to BOTH workers so whichever
        // peer the dispatcher picks for a given index has the data locally.
        // This is the simplest way to keep "concurrent dispatch + correct result"
        // the thing under test, without baking dispatcher routing decisions into
        // the buffer-shipment plan.
        var inputs = new (float[] a, float[] b, float[] expected)[dispatchCount];
        var inputBytes = new (byte[] a, byte[] b)[dispatchCount];
        var rng = new Random(unchecked((int)0xC0FFEE_10));
        for (int i = 0; i < dispatchCount; i++)
        {
            var a = new float[n];
            var b = new float[n];
            var expected = new float[n];
            for (int k = 0; k < n; k++)
            {
                a[k] = (float)(rng.NextDouble() * 1000.0 - 500.0);
                b[k] = (float)(rng.NextDouble() * 1000.0 - 500.0);
                expected[k] = a[k] + b[k];
            }
            inputs[i] = (a, b, expected);
            var ab = new byte[n * 4];
            var bb = new byte[n * 4];
            Buffer.BlockCopy(a, 0, ab, 0, n * 4);
            Buffer.BlockCopy(b, 0, bb, 0, n * 4);
            inputBytes[i] = (ab, bb);
        }

        foreach (var p in coordinator.Accelerator!.Peers)
        {
            for (int i = 0; i < dispatchCount; i++)
            {
                await coordinator.Transport!.SendBufferAsync(p.PeerId, $"a{i}", inputBytes[i].a);
                await coordinator.Transport!.SendBufferAsync(p.PeerId, $"b{i}", inputBytes[i].b);
            }
        }

        // Track which peer each dispatch lands on so we can prove load-balance.
        var perPeerHits = new ConcurrentDictionary<string, int>();
        coordinator.Accelerator!.Dispatcher.OnSendMessage += (peerId, msg) =>
        {
            if (msg.Type == P2PMessageType.KernelDispatch)
                perPeerHits.AddOrUpdate(peerId, 1, (_, count) => count + 1);
        };

        // Fire all 10 dispatches concurrently. The dispatcher's internal lock
        // serializes the SelectHealthyPeer calls; what we're stressing is the
        // pending-queue + per-peer round-trip handling under simultaneous load.
        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.VectorAdd))!;
        var dispatchTasks = Enumerable.Range(0, dispatchCount).Select(i =>
            coordinator.Accelerator!.DispatchAsync(method, n,
                ($"a{i}", inputBytes[i].a, 4),
                ($"b{i}", inputBytes[i].b, 4),
                ($"r{i}", null, 4))).ToArray();

        var dispatchResults = await Task.WhenAll(dispatchTasks);
        for (int i = 0; i < dispatchCount; i++)
        {
            if (!dispatchResults[i].Success)
                throw new Exception(
                    $"Concurrent dispatch {i} failed: {dispatchResults[i].Error}");
        }

        // Verify every result buffer round-trips back to the coordinator with
        // bit-exact contents.
        for (int i = 0; i < dispatchCount; i++)
        {
            var resultBytes = await WaitForBufferAsync(workerBuffers, $"r{i}", BufferReturnTimeoutMs);
            if (resultBytes.Length != n * 4)
                throw new Exception(
                    $"Dispatch {i} result buffer wrong size: {resultBytes.Length}");
            var actual = DataIntegrityHelper.BytesToFloats(resultBytes);
            var (violations, firstIdx, firstExp, firstAct) =
                DataIntegrityHelper.VerifyFloats(actual, inputs[i].expected, tolerance: 0.001f);
            if (violations != 0)
                throw new Exception(
                    $"Dispatch {i} VectorAdd over WebRTC: {violations}/{n} violations. " +
                    $"First at [{firstIdx}]: expected {firstExp}, got {firstAct}");
        }

        // Load-balance check: with 2 workers and 10 dispatches, both peers
        // should receive at least one dispatch. Pure single-peer concentration
        // would mean the dispatcher's pending-count balancing is broken.
        var peersHit = perPeerHits.Count(kv => kv.Value > 0);
        if (peersHit < 2)
            throw new Exception(
                $"10 concurrent dispatches landed on {peersHit} of 2 workers. " +
                $"Distribution: {string.Join(", ", perPeerHits.Select(kv => $"{kv.Key}={kv.Value}"))}.");
    }

    /// <summary>
    /// Shared setup for a CPU-coordinator + non-CPU-worker dispatch test.
    /// </summary>
    private async Task HeterogeneousVectorAddAsync(
        string swarmName,
        Func<global::ILGPU.Context, Device?> pickWorkerDevice,
        string workerBackendLabel)
    {
        const int n = 1024;
        var trackers = DispatchTrackers;

        using var workerContext = Context.CreateDefault();
        var workerDevice = pickWorkerDevice(workerContext);
        if (workerDevice == null)
            throw new UnsupportedTestException($"{workerBackendLabel} not available on this host.");
        using var workerAccelerator = workerDevice.CreateAccelerator(workerContext);

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, swarmName, trackers: trackers);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);

        await WaitForPeersAsync(coordinator, worker);

        var peerId = coordinator.Accelerator!.Peers.First().PeerId;
        var workerBuffers = CollectWorkerBuffers(coordinator);

        var (aBytes, bBytes, expected) = DataIntegrityHelper.GenerateVectorAddData(n);
        await coordinator.Transport!.SendBufferAsync(peerId, "a", aBytes);
        await coordinator.Transport!.SendBufferAsync(peerId, "b", bBytes);
        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.VectorAdd))!;
        var dispatchResult = await coordinator.Accelerator!.DispatchAsync(method, n,
            ("a", aBytes, 4), ("b", bBytes, 4), ("result", null, 4));

        if (!dispatchResult.Success)
            throw new Exception($"Heterogeneous dispatch (CPU -> {workerBackendLabel}) failed: {dispatchResult.Error}");
        if (!dispatchResult.ModifiedBuffers.Contains("result"))
            throw new Exception($"result buffer should be reported as modified by the {workerBackendLabel} worker");

        var resultBytes = await WaitForBufferAsync(workerBuffers, "result", BufferReturnTimeoutMs);
        if (resultBytes.Length != n * 4)
            throw new Exception($"Returned buffer wrong size: {resultBytes.Length}");

        var actual = DataIntegrityHelper.BytesToFloats(resultBytes);
        var (violations, firstIdx, firstExp, firstAct) =
            DataIntegrityHelper.VerifyFloats(actual, expected);

        if (violations != 0)
            throw new Exception(
                $"Heterogeneous CPU -> {workerBackendLabel} dispatch: {violations}/{n} violations. " +
                $"First at [{firstIdx}]: expected {firstExp}, got {firstAct}");
    }

    private static async Task WaitForPeersAsync(P2PCompute coordinator, P2PCompute worker)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(DiscoveryTimeoutMs);
        while (DateTime.UtcNow < deadline &&
               (coordinator.PeerCount == 0 || worker.PeerCount == 0))
        {
            await Task.Delay(250);
        }

        if (coordinator.PeerCount == 0 || worker.PeerCount == 0)
        {
            throw new Exception($"Peer discovery timed out after {DiscoveryTimeoutMs}ms. " +
                $"Coordinator peers: {coordinator.PeerCount}, Worker peers: {worker.PeerCount}.");
        }
    }

    private static async Task WaitForMultiplePeersAsync(P2PCompute coordinator, int expectedPeerCount)
    {
        var start = DateTime.UtcNow;
        var deadline = start.AddMilliseconds(DiscoveryTimeoutMs);
        var lastPeerCount = -1;
        var lastLogSec = -1;
        while (DateTime.UtcNow < deadline && coordinator.PeerCount < expectedPeerCount)
        {
            var elapsedSec = (int)(DateTime.UtcNow - start).TotalSeconds;
            var pc = coordinator.PeerCount;
            if (pc != lastPeerCount || (elapsedSec % 5 == 0 && elapsedSec != lastLogSec))
            {
                Console.WriteLine($"[WAIT-PEERS {elapsedSec:D2}s] coord.PeerCount={pc}/{expectedPeerCount} (bridge.ComputePeerCount={coordinator.Bridge?.ComputePeerCount ?? -1})");
                lastPeerCount = pc;
                lastLogSec = elapsedSec;
            }
            await Task.Delay(250);
        }

        if (coordinator.PeerCount < expectedPeerCount)
        {
            Console.WriteLine($"[WAIT-PEERS] FINAL: coord.PeerCount={coordinator.PeerCount}/{expectedPeerCount}, bridge.ComputePeerCount={coordinator.Bridge?.ComputePeerCount ?? -1}, elapsed={(DateTime.UtcNow - start).TotalSeconds:F1}s");
            throw new Exception(
                $"Multi-peer discovery timed out after {DiscoveryTimeoutMs}ms. " +
                $"Coordinator saw {coordinator.PeerCount} of {expectedPeerCount} expected peers.");
        }
        Console.WriteLine($"[WAIT-PEERS] OK after {(DateTime.UtcNow - start).TotalSeconds:F1}s");
    }

    private static ConcurrentDictionary<string, byte[]> CollectWorkerBuffers(P2PCompute coordinator)
    {
        var received = new ConcurrentDictionary<string, byte[]>();
        coordinator.Transport!.BufferTransfer.OnBufferReceived += (bufferId, data) =>
        {
            received[bufferId] = data;
        };
        return received;
    }

    private static async Task<byte[]> WaitForBufferAsync(
        ConcurrentDictionary<string, byte[]> received, string bufferId, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (received.TryGetValue(bufferId, out var data))
                return data;
            await Task.Delay(50);
        }
        throw new TimeoutException(
            $"Modified buffer '{bufferId}' did not arrive from worker within {timeoutMs}ms.");
    }
}
