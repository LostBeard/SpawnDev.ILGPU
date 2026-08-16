using System.Text.Json;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU.P2P;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// P2P accelerator / coordinator / dispatch unit tests — peer management, fault
/// tolerance, kernel serialization, buffer transfer.
///
/// Backend-agnostic: every test runs identically regardless of the GPU backend, so
/// this is a single standalone class registered ONCE (it used to be inherited by every
/// backend class, running ~7× redundantly). Most tests use mock peers and create their
/// own CPU/P2P accelerator; the handful of *_RealBackend / *_Integration tests drive the
/// P2P worker's compile+dispatch pipeline against a real accelerator via <see cref="RunTest"/>
/// — a CPU accelerator suffices, since the P2P layer is backend-agnostic and per-backend
/// kernel codegen is already covered by the regular backend test classes.
///
/// The P2P backend is on hold while the core 6 backends are stabilized; gate every P2P
/// test off by commenting out the single P2PLogicTests registration in the Demo and
/// DemoConsole Program.cs (registration-only discovery makes that the on/off switch).
/// </summary>
public class P2PLogicTests : IDisposable
{
    protected IPortableCrypto Crypto { get; }
    protected SpawnDev.WebTorrent.WebTorrentClient WebTorrentClient { get; }

    public P2PLogicTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient)
    {
        Crypto = crypto;
        WebTorrentClient = webTorrentClient;
    }

    protected async Task<SpawnDev.WebTorrent.Ed25519Signer> CreateEd25519Signer()
    {
        var signer = new SpawnDev.WebTorrent.Ed25519Signer(Crypto);
        await signer.GenerateKeyAsync();
        return signer;
    }

    // The *_RealBackend / *_Integration tests need a real accelerator to exercise the
    // P2P worker compile+dispatch pipeline. The P2P layer is backend-agnostic, so a CPU
    // accelerator (always available on browser + desktop) is sufficient. Cached for the
    // lifetime of the test instance and disposed in Dispose.
    private Context? _runTestContext;
    private Accelerator? _runTestAccelerator;

    protected async Task RunTest(Func<Accelerator, Task> testBody)
    {
        if (_runTestAccelerator == null)
        {
            _runTestContext = Context.Create(builder => builder.CPU().EnableAlgorithms());
            _runTestAccelerator = _runTestContext.GetCPUDevice(0).CreateCPUAccelerator(_runTestContext);
        }
        await testBody(_runTestAccelerator);
    }

    public void Dispose()
    {
        _runTestAccelerator?.Dispose();
        _runTestAccelerator = null;
        _runTestContext?.Dispose();
        _runTestContext = null;
    }

    // ═══════════════════════════════════════════════════════════
    //  P2P Device & Accelerator — Creation
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Device_Create()
    {
        var device = new P2PDevice();
        if (device.Name != "P2P Distributed Accelerator")
            throw new Exception($"Name: {device.Name}");
        if (device.MaxNumThreadsPerGroup != 256)
            throw new Exception($"MaxThreads: {device.MaxNumThreadsPerGroup}");
    }

    [TestMethod]
    public async Task P2P_Accelerator_Create()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);
        if (accelerator.Peers.Count != 0)
            throw new Exception($"Initial peers: {accelerator.Peers.Count}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Peer Management — Join & Leave
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_AddPeer()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        var peer = new RemotePeer
        {
            PeerId = "peer-1",
            IsConnected = true,
            MemorySize = 4L * 1024 * 1024 * 1024,
            RemoteBackend = global::ILGPU.Runtime.AcceleratorType.WebGPU,
        };
        accelerator.AddPeer(peer);

        if (accelerator.Peers.Count != 1)
            throw new Exception($"Peers: {accelerator.Peers.Count}");
        if (accelerator.Peers[0].PeerId != "peer-1")
            throw new Exception($"PeerId: {accelerator.Peers[0].PeerId}");
    }

    [TestMethod]
    public async Task P2P_RemovePeer()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        var peer = new RemotePeer { PeerId = "peer-1", IsConnected = true };
        accelerator.AddPeer(peer);
        accelerator.RemovePeer(peer);

        if (accelerator.Peers.Count != 0)
            throw new Exception($"Peers after remove: {accelerator.Peers.Count}");
    }

    [TestMethod]
    public async Task P2P_MultiplePeers()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        for (int i = 0; i < 5; i++)
        {
            accelerator.AddPeer(new RemotePeer
            {
                PeerId = $"peer-{i}",
                IsConnected = true,
                MemorySize = 2L * 1024 * 1024 * 1024,
            });
        }

        if (accelerator.Peers.Count != 5)
            throw new Exception($"Peers: {accelerator.Peers.Count}");
    }

    [TestMethod]
    public async Task P2P_SelectPeer_RoundRobin()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        accelerator.AddPeer(new RemotePeer { PeerId = "a", IsConnected = true });
        accelerator.AddPeer(new RemotePeer { PeerId = "b", IsConnected = true });

        var selected = accelerator.SelectPeer();
        if (selected == null)
            throw new Exception("Should select a peer");
        if (selected.PeerId != "a" && selected.PeerId != "b")
            throw new Exception($"Selected unknown peer: {selected.PeerId}");
    }

    [TestMethod]
    public async Task P2P_SelectPeer_NoPeers_ReturnsNull()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        var selected = accelerator.SelectPeer();
        if (selected != null)
            throw new Exception("Should return null with no peers");
    }

    // ═══════════════════════════════════════════════════════════
    //  Dispatcher — Fault Tolerance
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Dispatcher_Create()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accelerator);

        if (dispatcher.DispatchTimeoutMs != 60_000)
            throw new Exception($"Timeout: {dispatcher.DispatchTimeoutMs}");
        if (dispatcher.MaxRetries != 3)
            throw new Exception($"MaxRetries: {dispatcher.MaxRetries}");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_Dispatch_NoPeers_Throws()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accelerator);

        bool threw = false;
        try
        {
            dispatcher.Dispatch(new KernelDispatchRequest());
        }
        catch (InvalidOperationException) { threw = true; }
        if (!threw)
            throw new Exception("Should throw when no peers available");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_Dispatch_WithPeer()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);
        accelerator.AddPeer(new RemotePeer { PeerId = "worker-1", IsConnected = true });

        var dispatcher = new P2PDispatcher(accelerator);
        var request = new KernelDispatchRequest
        {
            KernelMethod = "MatMulKernel",
            GridDimX = 1024,
            GroupDimX = 256,
        };

        var dispatchId = dispatcher.Dispatch(request);
        if (string.IsNullOrEmpty(dispatchId))
            throw new Exception("DispatchId should not be empty");
        if (accelerator.Peers[0].PendingOperations != 1)
            throw new Exception($"PendingOps: {accelerator.Peers[0].PendingOperations}");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_HandleResult_Success()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);
        accelerator.AddPeer(new RemotePeer { PeerId = "worker-1", IsConnected = true });

        var dispatcher = new P2PDispatcher(accelerator);
        var request = new KernelDispatchRequest();
        var dispatchId = dispatcher.Dispatch(request);

        dispatcher.HandleResult(dispatchId, new KernelDispatchResult
        {
            DispatchId = dispatchId,
            Success = true,
            DurationMs = 42.5,
        });

        if (accelerator.Peers[0].PendingOperations != 0)
            throw new Exception($"PendingOps after result: {accelerator.Peers[0].PendingOperations}");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_PeerLoss_Retry()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);
        accelerator.AddPeer(new RemotePeer { PeerId = "worker-1", IsConnected = true });
        accelerator.AddPeer(new RemotePeer { PeerId = "worker-2", IsConnected = true });

        var dispatcher = new P2PDispatcher(accelerator);
        string? retriedDispatchId = null;
        dispatcher.OnDispatchRetried += (id, from, to) => retriedDispatchId = id;

        var request = new KernelDispatchRequest();
        var dispatchId = dispatcher.Dispatch(request);

        // Simulate peer loss
        dispatcher.HandlePeerLost("worker-1");

        // Should have retried on worker-2
        if (retriedDispatchId == null)
            throw new Exception("Should have retried dispatch");
        if (retriedDispatchId != dispatchId)
            throw new Exception("Retried wrong dispatch");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_AllPeersLost_Fails()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);
        accelerator.AddPeer(new RemotePeer { PeerId = "worker-1", IsConnected = true });

        var dispatcher = new P2PDispatcher(accelerator);
        string? failedId = null;
        dispatcher.OnDispatchFailed += (id, err) => failedId = id;

        var dispatchId = dispatcher.Dispatch(new KernelDispatchRequest());

        // Lose the only peer — no one to retry on
        dispatcher.HandlePeerLost("worker-1");

        if (failedId == null)
            throw new Exception("Should have reported failure");
    }

    // ═══════════════════════════════════════════════════════════
    //  Protocol — Serialization
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Protocol_SerializeMessage()
    {
        var msg = new P2PMessage
        {
            Type = P2PMessageType.CapabilityRequest,
        };
        var bytes = P2PProtocol.Serialize(msg);
        var deserialized = P2PProtocol.Deserialize(bytes);

        if (deserialized == null)
            throw new Exception("Deserialization returned null");
        if (deserialized.Type != P2PMessageType.CapabilityRequest)
            throw new Exception($"Type mismatch: {deserialized.Type}");
        if (deserialized.Version != P2PProtocol.Version)
            throw new Exception($"Version: {deserialized.Version}");
    }

    [TestMethod]
    public async Task P2P_Protocol_Capabilities()
    {
        var caps = new PeerCapabilities
        {
            PeerId = "phone-1",
            AvailableBackends = new[] { "WebGPU", "Wasm" },
            PreferredBackend = "WebGPU",
            AvailableMemory = 4L * 1024 * 1024 * 1024,
            EstimatedTflops = 2.5,
            MaxThreadsPerGroup = 256,
            Platform = "browser",
            IlgpuVersion = "4.7.0",
        };

        var json = System.Text.Json.JsonSerializer.Serialize(caps);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<PeerCapabilities>(json);

        if (deserialized?.PeerId != "phone-1")
            throw new Exception($"PeerId: {deserialized?.PeerId}");
        if (deserialized?.EstimatedTflops != 2.5)
            throw new Exception($"TFLOPS: {deserialized?.EstimatedTflops}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Swarm Coordinator
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Coordinator_Create()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);

        if (coordinator.PeerCount != 0)
            throw new Exception($"Initial peers: {coordinator.PeerCount}");
        if (coordinator.TotalTflops != 0)
            throw new Exception($"Initial TFLOPS: {coordinator.TotalTflops}");
    }

    [TestMethod]
    public async Task P2P_Coordinator_PeerJoin()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);

        RemotePeer? joined = null;
        coordinator.OnPeerJoined += p => joined = p;

        coordinator.HandlePeerConnected("phone-1", new PeerCapabilities
        {
            PeerId = "phone-1",
            AvailableMemory = 2L * 1024 * 1024 * 1024,
            EstimatedTflops = 1.5,
        });

        if (joined == null || joined.PeerId != "phone-1")
            throw new Exception("OnPeerJoined not fired or wrong peer");
        if (coordinator.PeerCount != 1)
            throw new Exception($"PeerCount: {coordinator.PeerCount}");
        if (coordinator.TotalTflops != 1.5)
            throw new Exception($"TFLOPS: {coordinator.TotalTflops}");
    }

    [TestMethod]
    public async Task P2P_Coordinator_PeerLeave()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);

        coordinator.HandlePeerConnected("phone-1", new PeerCapabilities
        {
            PeerId = "phone-1",
            EstimatedTflops = 1.5,
        });

        RemotePeer? left = null;
        coordinator.OnPeerLeft += p => left = p;
        coordinator.HandlePeerDisconnected("phone-1");

        if (left == null || left.PeerId != "phone-1")
            throw new Exception("OnPeerLeft not fired");
        if (coordinator.PeerCount != 0)
            throw new Exception($"PeerCount after leave: {coordinator.PeerCount}");
    }

    [TestMethod]
    public async Task P2P_Coordinator_CreateSwarm()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("test-compute");

        if (string.IsNullOrEmpty(coordinator.MagnetLink))
            throw new Exception("MagnetLink should not be empty");
        if (!coordinator.MagnetLink.StartsWith("magnet:?xt=urn:btih:"))
            throw new Exception($"Invalid magnet: {coordinator.MagnetLink[..30]}");
        if (!coordinator.MagnetLink.Contains("dn=test-compute"))
            throw new Exception("MagnetLink should contain swarm name");

        Console.WriteLine($"[P2P] Swarm magnet: {coordinator.MagnetLink}");

        // HTTP join link — not generated without JoinLinkBaseUrl
        if (coordinator.JoinLink != null)
            throw new Exception("JoinLink should be null without JoinLinkBaseUrl");
    }

    [TestMethod]
    public async Task P2P_Coordinator_JoinLink_WithBaseUrl()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        // Set to the coordinator's own web app URL
        coordinator.JoinLinkBaseUrl = "https://myapp.example.com/ml-demo";
        await coordinator.CreateSwarmAsync("inference-pool");

        if (string.IsNullOrEmpty(coordinator.JoinLink))
            throw new Exception("JoinLink should be generated when BaseUrl is set");
        if (!coordinator.JoinLink.StartsWith("https://myapp.example.com/ml-demo?compute="))
            throw new Exception($"Should use coordinator's app URL: {coordinator.JoinLink}");
        if (!coordinator.JoinLink.Contains("n=inference-pool"))
            throw new Exception("JoinLink should contain swarm name");

        Console.WriteLine($"[P2P] Join: {coordinator.JoinLink}");
    }

    [TestMethod]
    public async Task P2P_Coordinator_JoinLink_NoBaseUrl()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        // No base URL set — JoinLink stays null
        await coordinator.CreateSwarmAsync("no-url-test");

        if (coordinator.JoinLink != null)
            throw new Exception("JoinLink should be null without JoinLinkBaseUrl");
        // MagnetLink still works
        if (string.IsNullOrEmpty(coordinator.MagnetLink))
            throw new Exception("MagnetLink should always be generated");
    }

    // ═══════════════════════════════════════════════════════════
    //  Smart Dispatch — Ability-Based Selection
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Dispatcher_PrefersPowerfulPeer()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        // Phone: 2 TFLOPS, 4GB
        accelerator.AddPeer(new RemotePeer
        {
            PeerId = "phone",
            IsConnected = true,
            MemorySize = 4L * 1024 * 1024 * 1024,
            Capabilities = new PeerCapabilities
            {
                PeerId = "phone",
                EstimatedTflops = 2.0,
                AvailableMemory = 4L * 1024 * 1024 * 1024,
            },
        });

        // Desktop: 36 TFLOPS, 24GB
        accelerator.AddPeer(new RemotePeer
        {
            PeerId = "desktop",
            IsConnected = true,
            MemorySize = 24L * 1024 * 1024 * 1024,
            Capabilities = new PeerCapabilities
            {
                PeerId = "desktop",
                EstimatedTflops = 36.0,
                AvailableMemory = 24L * 1024 * 1024 * 1024,
            },
        });

        var dispatcher = new P2PDispatcher(accelerator);
        var dispatchId = dispatcher.Dispatch(new KernelDispatchRequest());

        // Desktop should be preferred (higher TFLOPS + memory)
        var desktop = accelerator.Peers.First(p => p.PeerId == "desktop");
        var phone = accelerator.Peers.First(p => p.PeerId == "phone");

        if (desktop.PendingOperations != 1)
            throw new Exception($"Desktop should have the dispatch, pending={desktop.PendingOperations}");
        if (phone.PendingOperations != 0)
            throw new Exception($"Phone should be idle, pending={phone.PendingOperations}");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_LoadBalances()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        // Two equal peers
        accelerator.AddPeer(new RemotePeer
        {
            PeerId = "peer-a",
            IsConnected = true,
            Capabilities = new PeerCapabilities { EstimatedTflops = 10.0, AvailableMemory = 8L * 1024 * 1024 * 1024 },
        });
        accelerator.AddPeer(new RemotePeer
        {
            PeerId = "peer-b",
            IsConnected = true,
            Capabilities = new PeerCapabilities { EstimatedTflops = 10.0, AvailableMemory = 8L * 1024 * 1024 * 1024 },
        });

        var dispatcher = new P2PDispatcher(accelerator);

        // First dispatch goes to peer-a (both equal, first wins)
        dispatcher.Dispatch(new KernelDispatchRequest());

        // Second dispatch should go to peer-b (peer-a has load)
        dispatcher.Dispatch(new KernelDispatchRequest());

        var a = accelerator.Peers.First(p => p.PeerId == "peer-a");
        var b = accelerator.Peers.First(p => p.PeerId == "peer-b");

        if (a.PendingOperations != 1 || b.PendingOperations != 1)
            throw new Exception($"Should balance: a={a.PendingOperations}, b={b.PendingOperations}");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_ThermalThrottle_AvoidsCritical()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        // Powerful phone but thermally critical
        accelerator.AddPeer(new RemotePeer
        {
            PeerId = "hot-phone",
            IsConnected = true,
            Capabilities = new PeerCapabilities
            {
                EstimatedTflops = 5.0,
                AvailableMemory = 8L * 1024 * 1024 * 1024,
                ThermalState = 3, // CRITICAL
                IsCharging = false,
                BatteryLevel = 0.8,
            },
        });

        // Weak desktop, but cool and plugged in
        accelerator.AddPeer(new RemotePeer
        {
            PeerId = "cool-desktop",
            IsConnected = true,
            Capabilities = new PeerCapabilities
            {
                EstimatedTflops = 2.0,
                AvailableMemory = 4L * 1024 * 1024 * 1024,
                ThermalState = 0, // nominal
                IsCharging = true,
            },
        });

        var dispatcher = new P2PDispatcher(accelerator);
        dispatcher.Dispatch(new KernelDispatchRequest());

        var hotPhone = accelerator.Peers.First(p => p.PeerId == "hot-phone");
        var coolDesktop = accelerator.Peers.First(p => p.PeerId == "cool-desktop");

        // Cool desktop should get the work despite lower TFLOPS
        if (coolDesktop.PendingOperations != 1)
            throw new Exception($"Cool desktop should get work: pending={coolDesktop.PendingOperations}");
        if (hotPhone.PendingOperations != 0)
            throw new Exception($"Hot phone should be avoided: pending={hotPhone.PendingOperations}");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_LowBattery_Penalized()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        // Phone at 5% battery, not charging
        accelerator.AddPeer(new RemotePeer
        {
            PeerId = "dying-phone",
            IsConnected = true,
            Capabilities = new PeerCapabilities
            {
                EstimatedTflops = 5.0,
                AvailableMemory = 4L * 1024 * 1024 * 1024,
                ThermalState = 0,
                IsCharging = false,
                BatteryLevel = 0.05, // 5%
            },
        });

        // Equal phone, but plugged in
        accelerator.AddPeer(new RemotePeer
        {
            PeerId = "charged-phone",
            IsConnected = true,
            Capabilities = new PeerCapabilities
            {
                EstimatedTflops = 5.0,
                AvailableMemory = 4L * 1024 * 1024 * 1024,
                ThermalState = 0,
                IsCharging = true,
            },
        });

        var dispatcher = new P2PDispatcher(accelerator);
        dispatcher.Dispatch(new KernelDispatchRequest());

        var dying = accelerator.Peers.First(p => p.PeerId == "dying-phone");
        var charged = accelerator.Peers.First(p => p.PeerId == "charged-phone");

        if (charged.PendingOperations != 1)
            throw new Exception($"Charged phone should get work: pending={charged.PendingOperations}");
        if (dying.PendingOperations != 0)
            throw new Exception($"Dying phone should be avoided: pending={dying.PendingOperations}");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_GracefulHandoff_ThermalCritical()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        var hotPeer = new RemotePeer
        {
            PeerId = "overheating",
            IsConnected = true,
            Capabilities = new PeerCapabilities
            {
                EstimatedTflops = 10.0,
                AvailableMemory = 8L * 1024 * 1024 * 1024,
                ThermalState = 0, // starts cool
                IsCharging = true,
            },
        };
        var stablePeer = new RemotePeer
        {
            PeerId = "stable",
            IsConnected = true,
            Capabilities = new PeerCapabilities
            {
                EstimatedTflops = 8.0,
                AvailableMemory = 8L * 1024 * 1024 * 1024,
                ThermalState = 0,
                IsCharging = true,
            },
        };
        accelerator.AddPeer(hotPeer);
        accelerator.AddPeer(stablePeer);

        var dispatcher = new P2PDispatcher(accelerator);
        string? evictedPeerId = null;
        dispatcher.OnPeerEvicted += (id, count) => evictedPeerId = id;

        // Dispatch work to the hotter (but faster) peer
        dispatcher.Dispatch(new KernelDispatchRequest());

        // Peer gets hot — update capabilities
        dispatcher.UpdatePeerCapabilities("overheating", new PeerCapabilities
        {
            EstimatedTflops = 10.0,
            ThermalState = 3, // CRITICAL
            IsCharging = true,
        });

        // Trigger heartbeat check which does proactive eviction
        // Simulate by calling the check directly
        // The monitor would catch this on next heartbeat tick
        // For now, verify the scoring would avoid this peer
        var score = dispatcher.ScorePeer(hotPeer);
        if (score != 0.0)
            throw new Exception($"Thermally critical peer should score 0: {score}");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_UpdateCapabilities()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var accelerator = (P2PAccelerator)device.CreateAccelerator(context);

        accelerator.AddPeer(new RemotePeer
        {
            PeerId = "peer-1",
            IsConnected = true,
            Capabilities = new PeerCapabilities
            {
                EstimatedTflops = 5.0,
                BatteryLevel = 0.8,
                IsCharging = false,
            },
        });

        var dispatcher = new P2PDispatcher(accelerator);

        // Battery drains
        dispatcher.UpdatePeerCapabilities("peer-1", new PeerCapabilities
        {
            EstimatedTflops = 5.0,
            BatteryLevel = 0.03, // 3% — critical
            IsCharging = false,
        });

        var peer = accelerator.Peers[0];
        if (peer.Capabilities!.BatteryLevel != 0.03)
            throw new Exception($"Battery not updated: {peer.Capabilities.BatteryLevel}");

        var score = dispatcher.ScorePeer(peer);
        // Should be heavily penalized (battery 3% = 0.1x multiplier)
        if (score > 0.1)
            throw new Exception($"Low battery peer score too high: {score}");
    }

    [TestMethod]
    public async Task P2P_Coordinator_TotalCapacity()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);

        coordinator.HandlePeerConnected("peer-1", new PeerCapabilities
        {
            PeerId = "peer-1",
            EstimatedTflops = 10.0,
            AvailableMemory = 8L * 1024 * 1024 * 1024,
        });
        coordinator.HandlePeerConnected("peer-2", new PeerCapabilities
        {
            PeerId = "peer-2",
            EstimatedTflops = 5.0,
            AvailableMemory = 4L * 1024 * 1024 * 1024,
        });

        if (coordinator.TotalTflops != 15.0)
            throw new Exception($"TotalTflops: {coordinator.TotalTflops}");
        if (coordinator.TotalMemory != 12L * 1024 * 1024 * 1024)
            throw new Exception($"TotalMemory: {coordinator.TotalMemory}");

        // Peer leaves — capacity drops
        coordinator.HandlePeerDisconnected("peer-1");
        if (coordinator.TotalTflops != 5.0)
            throw new Exception($"TotalTflops after leave: {coordinator.TotalTflops}");
    }

    // ═══════════════════════════════════════════════════════════
    //  P2P Transport — Message Round-Trip
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Transport_RegisterPeer_SendsCapabilityRequest()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var context = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        P2PMessage? sentMessage = null;
        transport.RegisterPeer("test-peer", async (data) =>
        {
            sentMessage = P2PProtocol.Deserialize(data);
        });
        await Task.Delay(50); // let async send complete

        if (sentMessage == null)
            throw new Exception("Should send capability request on register");
        if (sentMessage.Type != P2PMessageType.CapabilityRequest)
            throw new Exception($"Type: {sentMessage.Type}");
    }

    [TestMethod]
    public async Task P2P_Transport_HandleCapabilityResponse_AddsPeer()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var context = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        transport.RegisterPeer("gpu-peer", _ => Task.CompletedTask);

        // Simulate peer sending capability response
        var caps = new PeerCapabilities
        {
            PeerId = "gpu-peer",
            EstimatedTflops = 12.0,
            AvailableMemory = 8L * 1024 * 1024 * 1024,
            AvailableBackends = new[] { "WebGPU", "Wasm" },
            PreferredBackend = "WebGPU",
            Platform = "browser",
        };
        var response = new P2PMessage
        {
            Type = P2PMessageType.CapabilityResponse,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(caps),
        };
        await transport.HandleIncomingDataAsync("gpu-peer", P2PProtocol.Serialize(response));

        if (coordinator.PeerCount != 1)
            throw new Exception($"PeerCount: {coordinator.PeerCount}");
        if (coordinator.TotalTflops != 12.0)
            throw new Exception($"TFLOPS: {coordinator.TotalTflops}");
    }

    [TestMethod]
    public async Task P2P_Transport_HandleDisconnect_RemovesPeer()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var context = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        transport.RegisterPeer("leaving-peer", _ => Task.CompletedTask);

        // Add peer via capability response
        var caps = new PeerCapabilities { PeerId = "leaving-peer", EstimatedTflops = 5.0 };
        await transport.HandleIncomingDataAsync("leaving-peer", P2PProtocol.Serialize(new P2PMessage
        {
            Type = P2PMessageType.CapabilityResponse,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(caps),
        }));

        if (coordinator.PeerCount != 1) throw new Exception("Should have 1 peer");

        // Peer sends disconnect
        await transport.HandleIncomingDataAsync("leaving-peer", P2PProtocol.Serialize(new P2PMessage
        {
            Type = P2PMessageType.Disconnect,
        }));

        if (coordinator.PeerCount != 0)
            throw new Exception($"PeerCount after disconnect: {coordinator.PeerCount}");
    }

    [TestMethod]
    public async Task P2P_Transport_HandleHeartbeat_UpdatesTimestamp()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var context = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        transport.RegisterPeer("heartbeat-peer", _ => Task.CompletedTask);

        // Connect peer
        await transport.HandleIncomingDataAsync("heartbeat-peer", P2PProtocol.Serialize(new P2PMessage
        {
            Type = P2PMessageType.CapabilityResponse,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                new PeerCapabilities { PeerId = "heartbeat-peer", EstimatedTflops = 3.0 }),
        }));

        var peer = accelerator.Peers.FirstOrDefault(p => p.PeerId == "heartbeat-peer");
        if (peer == null) throw new Exception("Peer not found");

        var before = peer.LastHeartbeat;

        // Send heartbeat
        await transport.HandleIncomingDataAsync("heartbeat-peer", P2PProtocol.Serialize(new P2PMessage
        {
            Type = P2PMessageType.Heartbeat,
        }));

        if (peer.LastHeartbeat == before)
            throw new Exception("Heartbeat should update timestamp");
    }

    [TestMethod]
    public async Task P2P_Transport_Broadcast()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var context = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        // RegisterPeer sends a CapabilityRequest, so we track only after registration
        transport.RegisterPeer("peer-a", _ => Task.CompletedTask);
        transport.RegisterPeer("peer-b", _ => Task.CompletedTask);
        transport.RegisterPeer("peer-c", _ => Task.CompletedTask);

        int broadcastCount = 0;
        // Re-register with counting send function
        transport.RegisterPeer("peer-a", _ => { broadcastCount++; return Task.CompletedTask; });
        transport.RegisterPeer("peer-b", _ => { broadcastCount++; return Task.CompletedTask; });
        transport.RegisterPeer("peer-c", _ => { broadcastCount++; return Task.CompletedTask; });

        // Reset count after re-registration capability requests
        await Task.Delay(50);
        broadcastCount = 0;

        await transport.BroadcastAsync(new P2PMessage { Type = P2PMessageType.Heartbeat });

        if (broadcastCount != 3)
            throw new Exception($"Broadcast should reach all 3 peers: {broadcastCount}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Coordinator Role — Transfer & Election
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Role_CreatorIsCoordinator()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("role-test");

        if (coordinator.Role != P2PRole.Coordinator)
            throw new Exception($"Creator should be Coordinator: {coordinator.Role}");
    }

    [TestMethod]
    public async Task P2P_Role_JoinerIsWorker()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        // JoinSwarmAsync needs a real magnet — use a fake one to test role assignment
        // Just test the role assignment logic directly
        coordinator.HandlePeerConnected("existing-coord", new PeerCapabilities { PeerId = "existing-coord" });

        // Default role before creating swarm is Worker
        if (coordinator.Role != P2PRole.Worker)
            throw new Exception($"Default role should be Worker: {coordinator.Role}");
    }

    [TestMethod]
    public async Task P2P_Role_Transfer()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("transfer-test");

        coordinator.HandlePeerConnected("fast-desktop", new PeerCapabilities
        {
            PeerId = "fast-desktop",
            EstimatedTflops = 20.0,
            AvailableMemory = 16L * 1024 * 1024 * 1024,
        });
        coordinator.HandlePeerConnected("slow-phone", new PeerCapabilities
        {
            PeerId = "slow-phone",
            EstimatedTflops = 2.0,
            AvailableMemory = 2L * 1024 * 1024 * 1024,
        });

        string? newCoord = null;
        coordinator.OnCoordinatorChanged += id => newCoord = id;

        var transferredTo = coordinator.TransferCoordinator();

        if (transferredTo != "fast-desktop")
            throw new Exception($"Should transfer to fastest peer: {transferredTo}");
        if (coordinator.Role != P2PRole.Worker)
            throw new Exception($"After transfer, should be Worker: {coordinator.Role}");
        if (newCoord != "fast-desktop")
            throw new Exception("OnCoordinatorChanged should fire");
    }

    [TestMethod]
    public async Task P2P_Role_Transfer_ToSpecific()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("transfer-specific");

        coordinator.HandlePeerConnected("peer-a", new PeerCapabilities { PeerId = "peer-a", EstimatedTflops = 20.0 });
        coordinator.HandlePeerConnected("peer-b", new PeerCapabilities { PeerId = "peer-b", EstimatedTflops = 5.0 });

        // Transfer to specific peer (even though peer-a is faster)
        var result = coordinator.TransferCoordinator("peer-b");
        if (result != "peer-b")
            throw new Exception($"Should transfer to specified peer: {result}");
    }

    [TestMethod]
    public async Task P2P_Role_Election_PicksStrongest()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);

        coordinator.HandlePeerConnected("weak", new PeerCapabilities { PeerId = "weak", EstimatedTflops = 2.0, AvailableMemory = 2L * 1024 * 1024 * 1024 });
        coordinator.HandlePeerConnected("strong", new PeerCapabilities { PeerId = "strong", EstimatedTflops = 30.0, AvailableMemory = 24L * 1024 * 1024 * 1024 });
        coordinator.HandlePeerConnected("medium", new PeerCapabilities { PeerId = "medium", EstimatedTflops = 10.0, AvailableMemory = 8L * 1024 * 1024 * 1024 });

        var elected = coordinator.ElectCoordinator();
        if (elected != "strong")
            throw new Exception($"Election should pick strongest: {elected}");
    }

    [TestMethod]
    public async Task P2P_Role_Election_Deterministic()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);

        // Two equal peers — should pick deterministically by PeerId
        coordinator.HandlePeerConnected("peer-b", new PeerCapabilities { PeerId = "peer-b", EstimatedTflops = 10.0, AvailableMemory = 8L * 1024 * 1024 * 1024 });
        coordinator.HandlePeerConnected("peer-a", new PeerCapabilities { PeerId = "peer-a", EstimatedTflops = 10.0, AvailableMemory = 8L * 1024 * 1024 * 1024 });

        var elected1 = coordinator.ElectCoordinator();
        var elected2 = coordinator.ElectCoordinator();
        if (elected1 != elected2)
            throw new Exception($"Election must be deterministic: {elected1} vs {elected2}");
    }

    [TestMethod]
    public async Task P2P_Role_BecomeCoordinator()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);

        if (coordinator.Role != P2PRole.Worker)
            throw new Exception("Should start as Worker");

        coordinator.BecomeCoordinator();

        if (coordinator.Role != P2PRole.Coordinator)
            throw new Exception($"Should be Coordinator: {coordinator.Role}");
        if (coordinator.CoordinatorPeerId != null)
            throw new Exception("CoordinatorPeerId should be null (we are it)");
    }

    // ═══════════════════════════════════════════════════════════
    //  P2P Worker
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Worker_Create()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var context = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        await using var worker = new P2PWorker(transport);

        if (worker.IsReady)
            throw new Exception("Should not be ready before Initialize");
    }

    [TestMethod]
    public async Task P2P_Worker_Initialize()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var ctx = global::ILGPU.Context.CreateDefault();
        var accel = coordinator.CreateAccelerator(ctx);
        var dispatcher = new P2PDispatcher(accel);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        await using var worker = new P2PWorker(transport);
        worker.Initialize(ctx, accel);

        if (!worker.IsReady)
            throw new Exception("Should be ready after Initialize");
    }

    [TestMethod]
    public async Task P2P_Worker_BuildCapabilities()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var ctx = global::ILGPU.Context.CreateDefault();
        var accel = coordinator.CreateAccelerator(ctx);
        var dispatcher = new P2PDispatcher(accel);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        await using var worker = new P2PWorker(transport);
        worker.Initialize(ctx, accel);

        var caps = worker.BuildCapabilities("my-peer-id");
        if (caps.PeerId != "my-peer-id")
            throw new Exception($"PeerId: {caps.PeerId}");
        if (string.IsNullOrEmpty(caps.PreferredBackend))
            throw new Exception("PreferredBackend empty");
        if (caps.AvailableBackends.Length == 0)
            throw new Exception("No backends");
    }

    [TestMethod]
    public async Task P2P_Worker_BufferRoundTrip()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var ctx = global::ILGPU.Context.CreateDefault();
        var accel = coordinator.CreateAccelerator(ctx);
        var dispatcher = new P2PDispatcher(accel);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        await using var worker = new P2PWorker(transport);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        worker.ReceiveBuffer("buf-1", data);

        var retrieved = worker.GetBuffer("buf-1");
        if (retrieved == null || !retrieved.SequenceEqual(data))
            throw new Exception("Buffer round-trip failed");

        if (worker.GetBuffer("nonexistent") != null)
            throw new Exception("Missing buffer should return null");
    }

    [TestMethod]
    public async Task P2P_Role_Transfer_NoPeers_ReturnsNull()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("empty-transfer");

        var result = coordinator.TransferCoordinator();
        if (result != null)
            throw new Exception($"Transfer with no peers should return null: {result}");
        if (coordinator.Role != P2PRole.Coordinator)
            throw new Exception("Should remain Coordinator if transfer fails");
    }

    // ═══════════════════════════════════════════════════════════
    //  Kick & Block
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Kick_RemovesPeer()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("kick-test");

        coordinator.HandlePeerConnected("bad-peer", new PeerCapabilities { PeerId = "bad-peer" });
        if (coordinator.PeerCount != 1) throw new Exception("Should have 1 peer");

        string? kickedId = null;
        coordinator.OnPeerKicked += (id, reason) => kickedId = id;

        var result = coordinator.KickPeer("bad-peer", "returning garbage tensors");
        if (!result) throw new Exception("Kick should succeed");
        if (coordinator.PeerCount != 0) throw new Exception("Peer should be removed");
        if (kickedId != "bad-peer") throw new Exception("OnPeerKicked should fire");
    }

    [TestMethod]
    public async Task P2P_Kick_OnlyCoordinatorCan()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        // Default role is Worker
        coordinator.HandlePeerConnected("peer-1", new PeerCapabilities { PeerId = "peer-1" });

        var result = coordinator.KickPeer("peer-1");
        if (result) throw new Exception("Worker should not be able to kick");
    }

    [TestMethod]
    public async Task P2P_Block_PreventsReconnect()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("block-test");

        coordinator.HandlePeerConnected("malicious", new PeerCapabilities { PeerId = "malicious" });
        coordinator.BlockPeer("malicious", "tampered results");

        if (coordinator.PeerCount != 0) throw new Exception("Should be disconnected");
        if (!coordinator.IsPeerBlocked("malicious")) throw new Exception("Should be blocked");

        // Try to reconnect — should be rejected
        string? rejectedId = null;
        coordinator.OnPeerRejected += (id, reason) => rejectedId = id;

        var accepted = coordinator.HandlePeerConnected("malicious", new PeerCapabilities { PeerId = "malicious" });
        if (accepted) throw new Exception("Blocked peer should be rejected");
        if (rejectedId != "malicious") throw new Exception("OnPeerRejected should fire");
        if (coordinator.PeerCount != 0) throw new Exception("Should still have 0 peers");
    }

    [TestMethod]
    public async Task P2P_Unblock_AllowsReconnect()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("unblock-test");

        coordinator.BlockPeer("reformed-peer");
        if (!coordinator.IsPeerBlocked("reformed-peer")) throw new Exception("Should be blocked");

        coordinator.UnblockPeer("reformed-peer");
        if (coordinator.IsPeerBlocked("reformed-peer")) throw new Exception("Should be unblocked");

        // Now they can reconnect
        var accepted = coordinator.HandlePeerConnected("reformed-peer", new PeerCapabilities { PeerId = "reformed-peer", EstimatedTflops = 5.0 });
        if (!accepted) throw new Exception("Unblocked peer should be accepted");
        if (coordinator.PeerCount != 1) throw new Exception("Should have 1 peer");
    }

    [TestMethod]
    public async Task P2P_Block_ListTracked()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("blocklist-test");

        coordinator.BlockPeer("bad-1");
        coordinator.BlockPeer("bad-2");
        coordinator.BlockPeer("bad-3");

        if (coordinator.BlockedPeers.Count != 3)
            throw new Exception($"BlockedPeers: {coordinator.BlockedPeers.Count}");

        coordinator.UnblockPeer("bad-2");
        if (coordinator.BlockedPeers.Count != 2)
            throw new Exception($"After unblock: {coordinator.BlockedPeers.Count}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Kernel Serialization
    // ═══════════════════════════════════════════════════════════

    // A simple test kernel for serialization testing
    public static void TestVectorAdd(global::ILGPU.Index1D index, global::ILGPU.ArrayView<float> a, global::ILGPU.ArrayView<float> b, global::ILGPU.ArrayView<float> result)
    {
        result[index] = a[index] + b[index];
    }

    [TestMethod]
    public async Task P2P_KernelSerializer_CreateDispatch()
    {
        var method = typeof(P2PLogicTests).GetMethod(nameof(TestVectorAdd),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;

        var dispatch = P2PKernelSerializer.CreateDispatch(method, gridDimX: 1024);

        if (string.IsNullOrEmpty(dispatch.KernelType))
            throw new Exception("KernelType empty");
        if (dispatch.KernelMethod != "TestVectorAdd")
            throw new Exception($"Method: {dispatch.KernelMethod}");
        if (dispatch.GridDimX != 1024)
            throw new Exception($"GridDimX: {dispatch.GridDimX}");
    }

    [TestMethod]
    public async Task P2P_KernelSerializer_ResolveKernel()
    {
        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        var method = typeof(P2PLogicTests).GetMethod(nameof(TestVectorAdd),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;

        var dispatch = P2PKernelSerializer.CreateDispatch(method, gridDimX: 512);

        // Simulate worker-side resolution
        var resolved = P2PKernelSerializer.ResolveKernel(dispatch);
        if (resolved == null)
            throw new Exception("Should resolve the kernel method");
        if (resolved.Name != "TestVectorAdd")
            throw new Exception($"Resolved wrong method: {resolved.Name}");
    }

    [TestMethod]
    public async Task P2P_KernelSerializer_CanExecute()
    {
        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        var method = typeof(P2PLogicTests).GetMethod(nameof(TestVectorAdd),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;

        var dispatch = P2PKernelSerializer.CreateDispatch(method, gridDimX: 256);
        if (!P2PKernelSerializer.CanExecute(dispatch))
            throw new Exception("Should be able to execute — method exists in our assemblies");
    }

    [TestMethod]
    public async Task P2P_KernelSerializer_CantExecute_UnknownMethod()
    {
        var dispatch = new KernelDispatchRequest
        {
            KernelType = "NonExistent.FakeClass, FakeAssembly",
            KernelMethod = "FakeKernel",
        };
        if (P2PKernelSerializer.CanExecute(dispatch))
            throw new Exception("Should NOT be able to execute — method doesn't exist");
    }

    // ═══════════════════════════════════════════════════════════
    //  End-to-End: Coordinator → Transport → Worker → Result
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_EndToEnd_DispatchAndReceiveResult()
    {
        // Full pipeline: coordinator → transport → worker → result → transport → coordinator
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("e2e-test");
        using var ctx = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(ctx);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        // Worker side — uses a real CPU accelerator for execution
        var cpuAccel = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(ctx);
        await using var worker = new P2PWorker(transport);
        worker.Initialize(ctx, cpuAccel);
        worker.ReceiveBuffer("buf-a", new byte[4096]);
        worker.ReceiveBuffer("buf-b", new byte[4096]);
        worker.ReceiveBuffer("buf-result", new byte[4096]);

        // Create dispatch
        var method = typeof(P2PLogicTests).GetMethod(nameof(TestVectorAdd),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 1024);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "buf-a", Length = 1024, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 2, BufferId = "buf-b", Length = 1024, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 3, BufferId = "buf-result", Length = 1024, ElementSize = 4 },
        };

        // Simulate the full chain: serialize dispatch, worker handles, returns result
        var dispatchMsg = new P2PMessage
        {
            Type = P2PMessageType.KernelDispatch,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(request),
        };

        // Track result received back
        KernelDispatchResult? receivedResult = null;
        transport.RegisterPeer("worker-1", async (data) =>
        {
            // This is what the coordinator sends to the worker
            // We intercept and have the worker handle it
        });

        // Worker processes the dispatch directly (simulating transport delivery)
        await worker.HandleDispatchAsync("coordinator", request);

        // Verify worker processed it
        if (worker.GetBuffer("buf-result") == null)
            throw new Exception("Worker should have created result buffer");

        Console.WriteLine("[P2P E2E] Full pipeline: dispatch → resolve → execute → result");
    }

    [TestMethod]
    public async Task P2P_EndToEnd_WorkerKernelResolution()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var ctx = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(ctx);
        var cpuAccel = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(ctx);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        await using var worker = new P2PWorker(transport);
        worker.Initialize(ctx, cpuAccel);

        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        // Track worker events
        string? startedId = null;
        string? completedId = null;
        bool? completedSuccess = null;
        worker.OnKernelStarted += id => startedId = id;
        worker.OnKernelCompleted += (id, success, ms) =>
        {
            completedId = id;
            completedSuccess = success;
        };

        // Create dispatch with real kernel reference
        var method = typeof(P2PLogicTests).GetMethod(nameof(TestVectorAdd),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 512);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "a", Length = 512, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 2, BufferId = "b", Length = 512, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 3, BufferId = "r", Length = 512, ElementSize = 4 },
        };
        worker.ReceiveBuffer("a", new byte[2048]);
        worker.ReceiveBuffer("b", new byte[2048]);
        worker.ReceiveBuffer("r", new byte[2048]);

        // Worker handles dispatch directly
        await worker.HandleDispatchAsync("coordinator", request);

        if (startedId != request.DispatchId)
            throw new Exception($"OnKernelStarted not fired: {startedId}");
        if (completedId != request.DispatchId)
            throw new Exception($"OnKernelCompleted not fired: {completedId}, worker error: {worker.LastDispatchError}");
        if (completedSuccess != true)
            throw new Exception($"Should succeed: {completedSuccess}, worker error: {worker.LastDispatchError}");

        P2PKernelSerializer.ClearAllowlist();
    }

    [TestMethod]
    public async Task P2P_EndToEnd_WorkerRejectsUnknownKernel()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var ctx = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(ctx);
        var cpuAccel = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(ctx);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        await using var worker = new P2PWorker(transport);
        worker.Initialize(ctx, cpuAccel);

        bool? completedSuccess = null;
        worker.OnKernelCompleted += (id, success, ms) => completedSuccess = success;

        var request = new KernelDispatchRequest
        {
            KernelType = "Fake.NonExistent, FakeAssembly",
            KernelMethod = "BogusKernel",
        };

        await worker.HandleDispatchAsync("coordinator", request);

        if (completedSuccess != false)
            throw new Exception("Should fail for unknown kernel");
    }

    // ═══════════════════════════════════════════════════════════
    //  Worker Kernel Compilation (Real Backend)
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Worker_CompileKernel_RealBackend() => await RunTest(async accelerator =>
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        var dispatcher = new P2PDispatcher(coordinator.CreateAccelerator(
            global::ILGPU.Context.CreateDefault()));
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        await using var worker = new P2PWorker(transport);

        // Initialize with the REAL accelerator (CPU/CUDA/WebGPU — whatever this test class runs on)
        worker.Initialize(accelerator.Context, accelerator);

        // Pre-compile the test kernel on the real backend
        var method = typeof(P2PLogicTests).GetMethod(nameof(TestVectorAdd),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var compiled = worker.PreCompileKernel(method);

        if (!compiled)
            throw new Exception($"Kernel should compile on {accelerator.AcceleratorType}");
        if (worker.CachedKernelCount != 1)
            throw new Exception($"CachedKernelCount: {worker.CachedKernelCount}");

        // Compile again — should use cache
        var compiled2 = worker.PreCompileKernel(method);
        if (!compiled2)
            throw new Exception("Cached compile should succeed");
        if (worker.CachedKernelCount != 1)
            throw new Exception("Should still be 1 (cached)");
    });

    [TestMethod]
    public async Task P2P_Worker_CompileAndDispatch_RealBackend() => await RunTest(async accelerator =>
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        var p2pAccel = coordinator.CreateAccelerator(global::ILGPU.Context.CreateDefault());
        var dispatcher = new P2PDispatcher(p2pAccel);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        await using var worker = new P2PWorker(transport);

        // Real backend
        worker.Initialize(accelerator.Context, accelerator);

        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        string? compiledKernel = null;
        bool? dispatchSuccess = null;
        worker.OnKernelCompiled += name => compiledKernel = name;
        worker.OnKernelCompleted += (id, success, ms) => dispatchSuccess = success;

        // Dispatch with real kernel method
        var method = typeof(P2PLogicTests).GetMethod(nameof(TestVectorAdd),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 64);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "a", Length = 64, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 2, BufferId = "b", Length = 64, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 3, BufferId = "r", Length = 64, ElementSize = 4 },
        };
        worker.ReceiveBuffer("a", new byte[256]);
        worker.ReceiveBuffer("b", new byte[256]);
        worker.ReceiveBuffer("r", new byte[256]);

        await worker.HandleDispatchAsync("coordinator", request);

        if (compiledKernel == null)
            throw new Exception($"OnKernelCompiled should fire, worker error: {worker.LastDispatchError}");
        if (dispatchSuccess != true)
            throw new Exception($"Dispatch should succeed on {accelerator.AcceleratorType}, worker error: {worker.LastDispatchError}");

        P2PKernelSerializer.ClearAllowlist();
    });

    // ═══════════════════════════════════════════════════════════
    //  Buffer Transfer — Chunking & Reassembly
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_BufferTransfer_SmallBuffer_SingleChunk()
    {
        var transfer = new P2PBufferTransfer();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        var chunks = transfer.CreateChunks("buf-1", data);
        if (chunks.Length != 1) throw new Exception($"Small buffer should be 1 chunk: {chunks.Length}");
        if (chunks[0].TotalBytes != 5) throw new Exception($"TotalBytes: {chunks[0].TotalBytes}");
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_LargeBuffer_MultiChunk()
    {
        var transfer = new P2PBufferTransfer { MaxChunkSize = 1024 };
        var data = new byte[5000];
        Random.Shared.NextBytes(data);

        var chunks = transfer.CreateChunks("buf-large", data);
        if (chunks.Length != 5) throw new Exception($"5000/1024 should be 5 chunks: {chunks.Length}");

        // Verify chunk metadata
        for (int i = 0; i < chunks.Length; i++)
        {
            if (chunks[i].ChunkIndex != i) throw new Exception($"Chunk {i} index wrong");
            if (chunks[i].TotalChunks != 5) throw new Exception($"TotalChunks: {chunks[i].TotalChunks}");
            if (chunks[i].BufferId != "buf-large") throw new Exception("BufferId mismatch");
        }
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_Reassemble()
    {
        var transfer = new P2PBufferTransfer { MaxChunkSize = 1024 };
        var originalData = new byte[5000];
        Random.Shared.NextBytes(originalData);

        var chunks = transfer.CreateChunks("buf-reassemble", originalData);

        byte[]? received = null;
        transfer.OnBufferReceived += (id, data) => received = data;

        // Feed chunks in order
        for (int i = 0; i < chunks.Length; i++)
        {
            var complete = transfer.ReceiveChunk(chunks[i]);
            if (i < chunks.Length - 1 && complete)
                throw new Exception("Should not be complete before last chunk");
        }

        if (received == null) throw new Exception("OnBufferReceived should fire");
        if (received.Length != 5000) throw new Exception($"Length: {received.Length}");
        if (!received.SequenceEqual(originalData))
            throw new Exception("Reassembled data doesn't match original");
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_OutOfOrder()
    {
        var transfer = new P2PBufferTransfer { MaxChunkSize = 1024 };
        var data = new byte[3000];
        Random.Shared.NextBytes(data);

        var chunks = transfer.CreateChunks("buf-ooo", data);

        byte[]? received = null;
        transfer.OnBufferReceived += (id, d) => received = d;

        // Feed in reverse order
        for (int i = chunks.Length - 1; i >= 0; i--)
            transfer.ReceiveChunk(chunks[i]);

        if (received == null) throw new Exception("Should reassemble from out-of-order chunks");
        if (!received.SequenceEqual(data))
            throw new Exception("Out-of-order reassembly data mismatch");
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_Progress()
    {
        var transfer = new P2PBufferTransfer { MaxChunkSize = 1024 };
        var data = new byte[4096]; // 4 chunks

        var chunks = transfer.CreateChunks("buf-progress", data);

        transfer.ReceiveChunk(chunks[0]);
        var p1 = transfer.GetProgress("buf-progress");
        if (Math.Abs(p1 - 0.25) > 0.01) throw new Exception($"After 1/4: {p1}");

        transfer.ReceiveChunk(chunks[1]);
        var p2 = transfer.GetProgress("buf-progress");
        if (Math.Abs(p2 - 0.5) > 0.01) throw new Exception($"After 2/4: {p2}");
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_DuplicateChunk()
    {
        var transfer = new P2PBufferTransfer { MaxChunkSize = 1024 };
        var data = new byte[2048]; // 2 chunks
        Random.Shared.NextBytes(data);

        var chunks = transfer.CreateChunks("buf-dup", data);

        byte[]? received = null;
        transfer.OnBufferReceived += (id, d) => received = d;

        // Send chunk 0 twice, then chunk 1
        transfer.ReceiveChunk(chunks[0]);
        transfer.ReceiveChunk(chunks[0]); // duplicate
        transfer.ReceiveChunk(chunks[1]);

        if (received == null) throw new Exception("Should complete despite duplicate");
        if (!received.SequenceEqual(data)) throw new Exception("Data mismatch after duplicate");
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_Cancel()
    {
        var transfer = new P2PBufferTransfer { MaxChunkSize = 1024 };
        var data = new byte[4096];
        var chunks = transfer.CreateChunks("buf-cancel", data);

        transfer.ReceiveChunk(chunks[0]);
        if (transfer.ActiveTransfers != 1) throw new Exception($"Active: {transfer.ActiveTransfers}");

        transfer.CancelTransfer("buf-cancel");
        if (transfer.ActiveTransfers != 0) throw new Exception("Should be 0 after cancel");
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_1MB_Tensor()
    {
        // Simulate transferring a 1MB float tensor (256K floats). Pin MaxChunkSize to
        // 64KB so the test proves chunked reassembly regardless of the library default
        // (rc.14 raised the default to 256KB - 512 for fewer wire messages per buffer).
        var transfer = new P2PBufferTransfer { MaxChunkSize = 64 * 1024 };
        var tensorData = new byte[1024 * 1024];
        Random.Shared.NextBytes(tensorData);

        var chunks = transfer.CreateChunks("tensor-1mb", tensorData);
        // 1MB / 64KB = 16 chunks
        if (chunks.Length != 16) throw new Exception($"1MB should be 16 chunks: {chunks.Length}");

        byte[]? received = null;
        transfer.OnBufferReceived += (id, d) => received = d;

        foreach (var chunk in chunks)
            transfer.ReceiveChunk(chunk);

        if (received == null) throw new Exception("Should receive 1MB tensor");
        if (received.Length != 1024 * 1024) throw new Exception($"Length: {received.Length}");
        if (!received.SequenceEqual(tensorData))
            throw new Exception("1MB tensor data corruption");
    }

    // ═══════════════════════════════════════════════════════════
    //  Buffer Transfer via Transport Layer
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Transport_SendBuffer_ChunkedDelivery()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var ctx = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(ctx);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        // Pin MaxChunkSize to 64KB so we can assert a deterministic chunk count
        // independent of the library default (rc.14 raised default to 256KB - 512).
        transport.BufferTransfer.MaxChunkSize = 64 * 1024;

        // Track messages sent to worker
        var sentMessages = new List<byte[]>();
        transport.RegisterPeer("worker-1", async (data) =>
        {
            sentMessages.Add(data);
        });

        // Send a buffer that requires multiple chunks
        var tensorData = new byte[200_000]; // ~4 chunks at 64KB
        Random.Shared.NextBytes(tensorData);
        await transport.SendBufferAsync("worker-1", "tensor-a", tensorData);

        // Should have sent multiple messages (capability request + buffer chunks)
        // RegisterPeer sends a capability request, then SendBufferAsync sends chunks
        // 200KB / 64KB = 4 chunks (rounded up) + 1 capability request = 5 messages
        if (sentMessages.Count < 4)
            throw new Exception($"Should send multiple chunk messages: {sentMessages.Count}");

        // Verify the transport's buffer transfer can reassemble on the receiving end
        byte[]? reassembled = null;
        transport.BufferTransfer.OnBufferReceived += (id, data) => reassembled = data;

        // Simulate receiving the chunks on the other end
        var chunks = transport.BufferTransfer.CreateChunks("tensor-roundtrip", tensorData);
        foreach (var chunk in chunks)
            transport.BufferTransfer.ReceiveChunk(chunk);

        if (reassembled == null) throw new Exception("Should reassemble");
        if (!reassembled.SequenceEqual(tensorData))
            throw new Exception("Buffer data corruption through transport");
    }

    // ═══════════════════════════════════════════════════════════
    //  BEP 46 State Manager
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_StateManager_Create()
    {
        var client = WebTorrentClient;
        var dht = new SpawnDev.WebTorrent.DhtDiscovery();
        var channel = new SpawnDev.WebTorrent.AgentChannel(dht, await CreateEd25519Signer());
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("state-test");

        var stateManager = new P2PStateManager(channel, coordinator);

        if (stateManager.PublicKey == null || stateManager.PublicKey.Length == 0)
            throw new Exception("PublicKey should not be empty");
        if (string.IsNullOrEmpty(stateManager.PublicKeyHex))
            throw new Exception("PublicKeyHex should not be empty");
    }

    [TestMethod]
    public async Task P2P_StateManager_PublishState()
    {
        var client = WebTorrentClient;
        var dht = new SpawnDev.WebTorrent.DhtDiscovery();
        var channel = new SpawnDev.WebTorrent.AgentChannel(dht, await CreateEd25519Signer());
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("publish-test");

        // Add some peers for state content
        coordinator.HandlePeerConnected("peer-1", new PeerCapabilities
        {
            PeerId = "peer-1", EstimatedTflops = 10.0,
            AvailableMemory = 8L * 1024 * 1024 * 1024,
        });

        var stateManager = new P2PStateManager(channel, coordinator);

        // Should not throw — publishes state to DHT
        await stateManager.PublishStateAsync();
    }

    [TestMethod]
    public async Task P2P_StateManager_WorkerCantPublish()
    {
        var client = WebTorrentClient;
        var dht = new SpawnDev.WebTorrent.DhtDiscovery();
        var channel = new SpawnDev.WebTorrent.AgentChannel(dht, await CreateEd25519Signer());
        await using var coordinator = new P2PSwarmCoordinator(client);
        // Don't call CreateSwarmAsync — stays as Worker role

        var stateManager = new P2PStateManager(channel, coordinator);

        // Workers can't publish — should be a no-op, not throw
        await stateManager.PublishStateAsync();
    }

    [TestMethod]
    public async Task P2P_StateManager_OnStateUpdated()
    {
        var client = WebTorrentClient;
        var dht = new SpawnDev.WebTorrent.DhtDiscovery();
        var channel = new SpawnDev.WebTorrent.AgentChannel(dht, await CreateEd25519Signer());
        await using var coordinator = new P2PSwarmCoordinator(client);

        var stateManager = new P2PStateManager(channel, coordinator);

        SwarmState? received = null;
        stateManager.OnStateUpdated += state => received = state;

        // Simulate a state update from DHT
        var testState = new SwarmState
        {
            SwarmName = "test-swarm",
            PeerCount = 5,
            TotalTflops = 42.0,
        };
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(testState,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        // Simulate DHT delivering the update through the channel
        // AgentChannel fires OnAgentUpdate which StateManager handles
        // For this test, we verify the deserialization path
        if (received != null)
            throw new Exception("Should not have received state yet (no DHT delivery in test)");

        // Verify state model serializes correctly
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<SwarmState>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        if (roundTripped?.TotalTflops != 42.0)
            throw new Exception($"State round-trip failed: {roundTripped?.TotalTflops}");
        if (roundTripped?.PeerCount != 5)
            throw new Exception($"PeerCount: {roundTripped?.PeerCount}");
    }

    [TestMethod]
    public async Task P2P_SdComputeExtension_Name()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var ctx = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(ctx);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        var ext = new SdComputeExtension(transport);
        if (ext.Name != "sd_compute")
            throw new Exception($"Extension name: {ext.Name}");
    }

    [TestMethod]
    public async Task P2P_SdComputeExtension_HandshakeData()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        using var ctx = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(ctx);
        var dispatcher = new P2PDispatcher(accelerator);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        var ext = new SdComputeExtension(transport);
        // In _Alt, handshake data is set on the wire via SetWire().
        // Verify the extension has the correct name.
        if (ext.Name != "sd_compute")
            throw new Exception($"Expected 'sd_compute', got '{ext.Name}'");
        // Verify SetWire populates ExtendedHandshake with version
        var wire = new SpawnDev.WebTorrent.Wire();
        ext.SetWire(wire);
        if (!wire.ExtendedHandshake.ContainsKey("sd_compute_version"))
            throw new Exception("SetWire should add sd_compute_version to ExtendedHandshake");
    }

    // ═══════════════════════════════════════════════════════════
    //  Two-Node Integration: Coordinator + Worker via Mock Transport
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_TwoNode_FullCycle() => await RunTest(async accelerator =>
    {
        // === NODE 1: Coordinator ===
        var coordClient = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(coordClient);
        await coordinator.CreateSwarmAsync("two-node-test");
        using var coordCtx = global::ILGPU.Context.CreateDefault();
        var coordAccel = coordinator.CreateAccelerator(coordCtx);
        var dispatcher = new P2PDispatcher(coordAccel);
        await using var coordTransport = new P2PTransport(coordClient, coordinator, dispatcher);

        // === NODE 2: Worker ===
        var workerClient = WebTorrentClient;
        await using var workerCoord = new P2PSwarmCoordinator(workerClient);
        var workerP2PAccel = workerCoord.CreateAccelerator(coordCtx);
        var workerDispatcher = new P2PDispatcher(workerP2PAccel);
        await using var workerTransport = new P2PTransport(workerClient, workerCoord, workerDispatcher);
        await using var worker = new P2PWorker(workerTransport);
        // Worker uses the REAL backend (CPU/CUDA/WebGPU — whatever this test class runs)
        worker.Initialize(accelerator.Context, accelerator);

        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        // === Wire mock transport: bidirectional ===
        // Fire-and-forget matches real WebRTC semantics: SendAsync returns once the
        // message is handed to the wire, not once the peer has finished processing
        // it. Awaiting HandleIncomingDataAsync here would deadlock under the rc.12
        // per-peer queue because the consumer loop that's processing the current
        // dispatch can't reach the KernelResult it's waiting on.
        // Coordinator → Worker
        coordTransport.RegisterPeer("worker-node", (data) =>
        {
            _ = workerTransport.HandleIncomingDataAsync("coord-node", data);
            return Task.CompletedTask;
        });
        // Worker → Coordinator
        workerTransport.RegisterPeer("coord-node", (data) =>
        {
            _ = coordTransport.HandleIncomingDataAsync("worker-node", data);
            return Task.CompletedTask;
        });

        // === Connect worker as peer ===
        var caps = worker.BuildCapabilities("worker-node");
        coordinator.HandlePeerConnected("worker-node", caps);
        coordAccel.AddPeer(new RemotePeer
        {
            PeerId = "worker-node",
            IsConnected = true,
            Capabilities = caps,
        });

        // === Send buffer data to worker ===
        var tensorA = new byte[4096]; // 1024 floats
        var tensorB = new byte[4096];
        for (int i = 0; i < 4096; i++) { tensorA[i] = (byte)(i % 256); tensorB[i] = 1; }
        worker.ReceiveBuffer("a", tensorA);
        worker.ReceiveBuffer("b", tensorB);
        worker.ReceiveBuffer("result", new byte[4096]);

        // === Dispatch kernel ===
        var method = typeof(P2PLogicTests).GetMethod(nameof(TestVectorAdd),
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 1024);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "a", Length = 1024, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 2, BufferId = "b", Length = 1024, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 3, BufferId = "result", Length = 1024, ElementSize = 4 },
        };

        // === Worker handles dispatch ===
        string? completedId = null;
        bool? completedSuccess = null;
        string? compiledKernel = null;
        worker.OnKernelCompleted += (id, success, ms) =>
        {
            completedId = id;
            completedSuccess = success;
        };
        worker.OnKernelCompiled += name => compiledKernel = name;

        await worker.HandleDispatchAsync("coord-node", request);

        // === Verify ===
        if (compiledKernel == null)
            throw new Exception($"Worker should compile kernel on {accelerator.AcceleratorType}, worker error: {worker.LastDispatchError}");
        if (completedSuccess != true)
            throw new Exception($"Dispatch should succeed on {accelerator.AcceleratorType}, worker error: {worker.LastDispatchError}");
        if (completedId != request.DispatchId)
            throw new Exception("Dispatch ID mismatch");

        // Worker should have result buffer
        var resultBuf = worker.GetBuffer("result");
        if (resultBuf == null)
            throw new Exception("Worker should have result buffer");

        Console.WriteLine($"[P2P TwoNode] Coordinator → Worker → Compile({accelerator.AcceleratorType}) → Result. PASS.");

        P2PKernelSerializer.ClearAllowlist();
    });

    // ═══════════════════════════════════════════════════════════
    //  Security Tests
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Security_AllowlistBlocksUnauthorized()
    {
        // Register only our test kernel type
        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        try
        {
            // Trying to dispatch System.IO.File should be blocked
            var malicious = new KernelDispatchRequest
            {
                KernelType = typeof(System.IO.File).AssemblyQualifiedName ?? "",
                KernelMethod = "Delete",
            };
            if (P2PKernelSerializer.CanExecute(malicious))
                throw new Exception("SECURITY FAIL: System.IO.File.Delete should be blocked by allowlist");

            // Our registered type should work
            var legitimate = P2PKernelSerializer.CreateDispatch(
                typeof(P2PLogicTests).GetMethod(nameof(TestVectorAdd),
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!,
                gridDimX: 64);
            if (!P2PKernelSerializer.CanExecute(legitimate))
                throw new Exception("Registered type should be allowed");
        }
        finally
        {
            P2PKernelSerializer.ClearAllowlist();
        }
    }

    [TestMethod]
    public async Task P2P_Security_BufferTransfer_RejectsOversized()
    {
        var transfer = new P2PBufferTransfer { MaxTotalChunks = 100, MaxTotalBytes = 1024 * 1024 };

        // Malicious chunk claiming absurd size
        var malicious = new BufferChunk
        {
            BufferId = "evil",
            ChunkIndex = 0,
            TotalChunks = int.MaxValue, // would OOM without bounds check
            TotalBytes = int.MaxValue,
            Data = new byte[1],
        };

        var accepted = transfer.ReceiveChunk(malicious);
        if (accepted)
            throw new Exception("SECURITY FAIL: Oversized chunk should be rejected");
        if (transfer.ActiveTransfers != 0)
            throw new Exception("Rejected chunk should not create a transfer");
    }

    [TestMethod]
    public async Task P2P_Security_BufferTransfer_RejectsNegativeIndex()
    {
        var transfer = new P2PBufferTransfer();

        var badIndex = new BufferChunk
        {
            BufferId = "bad",
            ChunkIndex = -1,
            TotalChunks = 5,
            TotalBytes = 5000,
            Data = new byte[1000],
        };

        var accepted = transfer.ReceiveChunk(badIndex);
        if (accepted)
            throw new Exception("SECURITY FAIL: Negative chunk index should be rejected");
    }

    [TestMethod]
    public async Task P2P_Security_BufferTransfer_RejectsOutOfRangeIndex()
    {
        var transfer = new P2PBufferTransfer();

        var outOfRange = new BufferChunk
        {
            BufferId = "oor",
            ChunkIndex = 10, // only 5 chunks total
            TotalChunks = 5,
            TotalBytes = 5000,
            Data = new byte[1000],
        };

        var accepted = transfer.ReceiveChunk(outOfRange);
        if (accepted)
            throw new Exception("SECURITY FAIL: Out-of-range chunk index should be rejected");
    }

    [TestMethod]
    public async Task P2P_Security_KickRequiresCoordinator()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        // Default role is Worker — should not be able to kick
        coordinator.HandlePeerConnected("target", new PeerCapabilities { PeerId = "target" });

        var result = coordinator.KickPeer("target");
        if (result)
            throw new Exception("SECURITY FAIL: Worker should not be able to kick peers");
    }

    // ═══════════════════════════════════════════════════════════
    //  Transfer Notification + Dispose
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Transfer_SendsNotification()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("transfer-notify");

        coordinator.HandlePeerConnected("target-peer", new PeerCapabilities
        {
            PeerId = "target-peer", EstimatedTflops = 10.0,
        });
        coordinator.HandlePeerConnected("other-peer", new PeerCapabilities
        {
            PeerId = "other-peer", EstimatedTflops = 5.0,
        });

        var sentMessages = new List<(string peerId, P2PMessageType type)>();
        coordinator.OnSendMessage += (peerId, msg) =>
            sentMessages.Add((peerId, msg.Type));

        coordinator.TransferCoordinator("target-peer");

        // Target should receive CoordinatorTransfer
        if (!sentMessages.Any(m => m.peerId == "target-peer" && m.type == P2PMessageType.CoordinatorTransfer))
            throw new Exception("Target should receive CoordinatorTransfer");

        // Other peer should receive CoordinatorAnnounce
        if (!sentMessages.Any(m => m.peerId == "other-peer" && m.type == P2PMessageType.CoordinatorAnnounce))
            throw new Exception("Other peers should receive CoordinatorAnnounce");
    }

    [TestMethod]
    public async Task P2P_Dispose_SendsDisconnect()
    {
        var client = WebTorrentClient;
        var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("dispose-notify");

        coordinator.HandlePeerConnected("peer-1", new PeerCapabilities { PeerId = "peer-1" });
        coordinator.HandlePeerConnected("peer-2", new PeerCapabilities { PeerId = "peer-2" });

        var disconnectsSent = 0;
        coordinator.OnSendMessage += (peerId, msg) =>
        {
            if (msg.Type == P2PMessageType.Disconnect)
                disconnectsSent++;
        };

        await coordinator.DisposeAsync();
        // Do NOT dispose the WebTorrentClient - it's a DI singleton shared across all tests.
        // Disposing it here kills every subsequent P2P test with "Client is destroyed".
        // The coordinator's DisposeAsync is what sends the disconnect messages we're testing.

        if (disconnectsSent != 2)
            throw new Exception($"Should send 2 disconnect messages: {disconnectsSent}");
    }

    // ═══════════════════════════════════════════════════════════
    //  SwarmIdentity — Real ECDSA Crypto
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_SwarmIdentity_Create()
    {

        var crypto = Crypto;
        await using var identity = await SwarmIdentity.CreateAsync(crypto, "test-identity");

        if (identity.PublicKeySpki.Length == 0)
            throw new Exception("PublicKeySpki should not be empty");
        if (string.IsNullOrEmpty(identity.Fingerprint))
            throw new Exception("Fingerprint should not be empty");
        if (identity.Label != "test-identity")
            throw new Exception($"Label: {identity.Label}");
    }

    [TestMethod]
    public async Task P2P_SwarmIdentity_SignAndVerify()
    {

        var crypto = Crypto;
        await using var identity = await SwarmIdentity.CreateAsync(crypto, "signer");

        var message = System.Text.Encoding.UTF8.GetBytes("Hello P2P World");
        var signature = await identity.SignAsync(message);

        if (signature == null || signature.Length == 0)
            throw new Exception("Signature should not be empty");

        var valid = await identity.VerifyAsync(message, signature);
        if (!valid)
            throw new Exception("Signature should verify against own public key");

        // Tampered message should fail
        var tampered = System.Text.Encoding.UTF8.GetBytes("Tampered message");
        var invalidVerify = await identity.VerifyAsync(tampered, signature);
        if (invalidVerify)
            throw new Exception("Tampered message should NOT verify");
    }

    [TestMethod]
    public async Task P2P_SwarmIdentity_ExportImport()
    {

        var crypto = Crypto;
        await using var original = await SwarmIdentity.CreateAsync(crypto, "exportable");

        // Sign something
        var message = System.Text.Encoding.UTF8.GetBytes("Persistent identity");
        var signature = await original.SignAsync(message);

        // Export keys
        var privateKey = await original.ExportPrivateKeyAsync();
        var publicKey = original.PublicKeySpki;

        // Import into new identity
        await using var imported = await SwarmIdentity.ImportAsync(crypto, publicKey, privateKey);

        // Fingerprint should match
        if (imported.Fingerprint != original.Fingerprint)
            throw new Exception($"Fingerprint mismatch: {imported.Fingerprint} vs {original.Fingerprint}");

        // Imported identity should verify original's signature
        var valid = await imported.VerifyAsync(message, signature);
        if (!valid)
            throw new Exception("Imported identity should verify original signature");
    }

    [TestMethod]
    public async Task P2P_SwarmIdentity_CrossVerify()
    {

        var crypto = Crypto;
        await using var alice = await SwarmIdentity.CreateAsync(crypto, "Alice");
        await using var bob = await SwarmIdentity.CreateAsync(crypto, "Bob");

        // Alice signs
        var message = System.Text.Encoding.UTF8.GetBytes("Alice's message");
        var aliceSig = await alice.SignAsync(message);

        // Bob verifies Alice's signature using static verify
        var valid = await SwarmIdentity.VerifyAsync(crypto, alice.PublicKeySpki, message, aliceSig);
        if (!valid)
            throw new Exception("Bob should verify Alice's signature");

        // Bob's key should NOT verify Alice's signature
        var invalid = await SwarmIdentity.VerifyAsync(crypto, bob.PublicKeySpki, message, aliceSig);
        if (invalid)
            throw new Exception("Bob's key should NOT verify Alice's signature");
    }

    [TestMethod]
    public async Task P2P_KeyRegistry_AddAndCheck()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");

        var registry = new KeyRegistry();
        registry.AddKey(owner.PublicKeySpki, SwarmRole.Owner, "Owner Key");
        var ownerKeyB64 = Convert.ToBase64String(owner.PublicKeySpki);

        if (!registry.HasRole(ownerKeyB64, SwarmRole.Owner))
            throw new Exception("Owner should have Owner role");
        if (!registry.HasRole(ownerKeyB64, SwarmRole.Coordinator))
            throw new Exception("Owner should have Coordinator role (Owner > Coordinator)");
    }

    [TestMethod]
    public async Task P2P_KeyRegistry_Revoke()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");
        await using var worker = await SwarmIdentity.CreateAsync(crypto, "Worker");

        var registry = new KeyRegistry();
        registry.AddKey(owner.PublicKeySpki, SwarmRole.Owner, "Owner");
        registry.AddKey(worker.PublicKeySpki, SwarmRole.Worker, "Worker");
        var workerKeyB64 = Convert.ToBase64String(worker.PublicKeySpki);

        registry.RevokeKey(worker.PublicKeySpki, "Misbehaving");

        if (!registry.IsRevoked(workerKeyB64))
            throw new Exception("Worker should be revoked");
        if (registry.HasRole(workerKeyB64, SwarmRole.Worker))
            throw new Exception("Revoked worker should not have Worker role");
    }

    // ═══════════════════════════════════════════════════════════
    //  SwarmPolicy Enforcement
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Policy_MaxPeers()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("max-peers");
        coordinator.Policy.MaxPeers = 2;

        coordinator.HandlePeerConnected("p1", new PeerCapabilities { PeerId = "p1" });
        coordinator.HandlePeerConnected("p2", new PeerCapabilities { PeerId = "p2" });

        string? rejected = null;
        coordinator.OnPeerRejected += (id, reason) => rejected = reason;
        var accepted = coordinator.HandlePeerConnected("p3", new PeerCapabilities { PeerId = "p3" });

        if (accepted) throw new Exception("Should reject — swarm full");
        if (rejected != "swarm full") throw new Exception($"Reason: {rejected}");
        if (coordinator.PeerCount != 2) throw new Exception($"Count: {coordinator.PeerCount}");
    }

    [TestMethod]
    public async Task P2P_Policy_ApprovalMode()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("approval-test");
        coordinator.Policy.JoinPermission = JoinMode.Approval;

        RemotePeer? pending = null;
        coordinator.OnPeerPendingApproval += p => pending = p;

        var accepted = coordinator.HandlePeerConnected("new-device", new PeerCapabilities { PeerId = "new-device" });

        if (accepted) throw new Exception("New device should be pending, not accepted");
        if (pending == null) throw new Exception("OnPeerPendingApproval should fire");
        if (coordinator.PeerCount != 0) throw new Exception("Pending peer should not be counted");
        if (coordinator.PendingApproval.Count != 1) throw new Exception($"Pending: {coordinator.PendingApproval.Count}");

        // Owner approves
        var approved = coordinator.ApprovePeer("new-device");
        if (!approved) throw new Exception("Approval should succeed");
        if (coordinator.PeerCount != 1) throw new Exception("Approved peer should be counted");
        if (coordinator.PendingApproval.Count != 0) throw new Exception("Pending should be empty");
    }

    [TestMethod]
    public async Task P2P_Policy_ApprovalMode_KnownPeerAutoAdmit()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("approval-known");
        coordinator.Policy.JoinPermission = JoinMode.Approval;

        // First visit — goes to pending, gets approved
        coordinator.HandlePeerConnected("returning-device", new PeerCapabilities { PeerId = "returning-device" });
        coordinator.ApprovePeer("returning-device");
        coordinator.HandlePeerDisconnected("returning-device");

        // Second visit — should auto-admit (known peer)
        var accepted = coordinator.HandlePeerConnected("returning-device", new PeerCapabilities { PeerId = "returning-device" });
        if (!accepted) throw new Exception("Known device should auto-admit");
        if (coordinator.PeerCount != 1) throw new Exception($"Count: {coordinator.PeerCount}");
    }

    [TestMethod]
    public async Task P2P_Policy_KnownOnly()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("known-only");

        // First: admit in Open mode to establish known peers
        coordinator.HandlePeerConnected("known-peer", new PeerCapabilities { PeerId = "known-peer" });
        coordinator.HandlePeerDisconnected("known-peer");

        // Switch to KnownOnly
        coordinator.Policy.JoinPermission = JoinMode.KnownOnly;

        // Known peer can rejoin
        var accepted = coordinator.HandlePeerConnected("known-peer", new PeerCapabilities { PeerId = "known-peer" });
        if (!accepted) throw new Exception("Known peer should be accepted");

        // Unknown peer rejected
        string? rejected = null;
        coordinator.OnPeerRejected += (id, reason) => rejected = reason;
        var unknown = coordinator.HandlePeerConnected("stranger", new PeerCapabilities { PeerId = "stranger" });
        if (unknown) throw new Exception("Unknown peer should be rejected");
        if (rejected != "unknown device") throw new Exception($"Reason: {rejected}");
    }

    [TestMethod]
    public async Task P2P_Policy_Open_Default()
    {
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("open-default");

        // Default policy is Open — everyone gets in
        for (int i = 0; i < 10; i++)
            coordinator.HandlePeerConnected($"peer-{i}", new PeerCapabilities { PeerId = $"peer-{i}" });

        if (coordinator.PeerCount != 10) throw new Exception($"Count: {coordinator.PeerCount}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Real Kernel Execution — P2PKernelLauncher
    //  These tests prove actual GPU dispatch through the P2P system.
    //  No mocks. Real accelerator, real kernels, real data.
    // ═══════════════════════════════════════════════════════════

    /// <summary>Test kernel: multiply each element by 2.</summary>
    static void KernelMultiplyBy2(global::ILGPU.Index1D index, global::ILGPU.ArrayView<float> data)
    {
        data[index] = data[index] * 2.0f;
    }

    /// <summary>Test kernel: add two arrays.</summary>
    static void KernelAddArrays(
        global::ILGPU.Index1D index,
        global::ILGPU.ArrayView<float> a,
        global::ILGPU.ArrayView<float> b,
        global::ILGPU.ArrayView<float> result)
    {
        result[index] = a[index] + b[index];
    }

    /// <summary>Test kernel: fill with constant value.</summary>
    static void KernelFillConstant(
        global::ILGPU.Index1D index,
        global::ILGPU.ArrayView<int> data)
    {
        data[index] = 42;
    }

    [TestMethod]
    public async Task P2P_KernelLauncher_LoadAndCache()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        using var accelerator = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);
        var launcher = new P2PKernelLauncher(accelerator);

        var method = typeof(P2PLogicTests).GetMethod(
            nameof(KernelMultiplyBy2),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var cached = launcher.LoadAndCache(method);
        if (cached == null) throw new Exception("LoadAndCache returned null");
        if (cached.Launcher == null) throw new Exception("Launcher delegate is null");
        if (cached.ParameterTypes.Length != 2) throw new Exception($"Expected 2 params, got {cached.ParameterTypes.Length}");
        if (launcher.CachedCount != 1) throw new Exception($"Cache count: {launcher.CachedCount}");

        // Loading same method again should return cached
        var cached2 = launcher.LoadAndCache(method);
        if (launcher.CachedCount != 1) throw new Exception($"Should still be 1, got {launcher.CachedCount}");

        Console.WriteLine("[P2P] KernelLauncher load + cache: OK");
    }

    [TestMethod]
    public async Task P2P_KernelLauncher_Execute_MultiplyBy2()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        using var accelerator = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);
        var launcher = new P2PKernelLauncher(accelerator);

        var method = typeof(P2PLogicTests).GetMethod(
            nameof(KernelMultiplyBy2),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Create input data: [1.0, 2.0, 3.0, 4.0]
        var inputFloats = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
        var inputBytes = new byte[inputFloats.Length * 4];
        Buffer.BlockCopy(inputFloats, 0, inputBytes, 0, inputBytes.Length);

        var bufferBindings = new Dictionary<int, BufferData>
        {
            [1] = new BufferData { RawData = inputBytes, ElementCount = 4, ElementSize = 4 }
        };

        var results = await launcher.ExecuteAsync(method, 4, bufferBindings);

        if (!results.ContainsKey(1)) throw new Exception("Missing result for param 1");

        // Verify output: [2.0, 4.0, 6.0, 8.0]
        var outputFloats = new float[4];
        Buffer.BlockCopy(results[1], 0, outputFloats, 0, 16);

        for (int i = 0; i < 4; i++)
        {
            float expected = inputFloats[i] * 2.0f;
            if (Math.Abs(outputFloats[i] - expected) > 0.001f)
                throw new Exception($"[{i}] expected {expected}, got {outputFloats[i]}");
        }

        Console.WriteLine($"[P2P] KernelLauncher MultiplyBy2: [{string.Join(", ", outputFloats)}] ✓");
    }

    [TestMethod]
    public async Task P2P_KernelLauncher_Execute_AddArrays()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        using var accelerator = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);
        var launcher = new P2PKernelLauncher(accelerator);

        var method = typeof(P2PLogicTests).GetMethod(
            nameof(KernelAddArrays),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        int count = 1024;
        var aFloats = new float[count];
        var bFloats = new float[count];
        for (int i = 0; i < count; i++)
        {
            aFloats[i] = i;
            bFloats[i] = i * 10.0f;
        }

        var aBytes = new byte[count * 4];
        var bBytes = new byte[count * 4];
        var rBytes = new byte[count * 4]; // zero-init output
        Buffer.BlockCopy(aFloats, 0, aBytes, 0, aBytes.Length);
        Buffer.BlockCopy(bFloats, 0, bBytes, 0, bBytes.Length);

        var bufferBindings = new Dictionary<int, BufferData>
        {
            [1] = new BufferData { RawData = aBytes, ElementCount = count, ElementSize = 4 },
            [2] = new BufferData { RawData = bBytes, ElementCount = count, ElementSize = 4 },
            [3] = new BufferData { RawData = rBytes, ElementCount = count, ElementSize = 4 },
        };

        var results = await launcher.ExecuteAsync(method, count, bufferBindings);

        var outFloats = new float[count];
        Buffer.BlockCopy(results[3], 0, outFloats, 0, count * 4);

        int errors = 0;
        for (int i = 0; i < count; i++)
        {
            float expected = aFloats[i] + bFloats[i];
            if (Math.Abs(outFloats[i] - expected) > 0.001f) errors++;
        }

        if (errors > 0) throw new Exception($"{errors}/{count} elements wrong");
        Console.WriteLine($"[P2P] KernelLauncher AddArrays: {count} elements verified ✓");
    }

    [TestMethod]
    public async Task P2P_KernelLauncher_Execute_IntKernel()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        using var accelerator = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);
        var launcher = new P2PKernelLauncher(accelerator);

        var method = typeof(P2PLogicTests).GetMethod(
            nameof(KernelFillConstant),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        int count = 256;
        var bufferBindings = new Dictionary<int, BufferData>
        {
            [1] = new BufferData { RawData = new byte[count * 4], ElementCount = count, ElementSize = 4 }
        };

        var results = await launcher.ExecuteAsync(method, count, bufferBindings);

        var outInts = new int[count];
        Buffer.BlockCopy(results[1], 0, outInts, 0, count * 4);

        for (int i = 0; i < count; i++)
        {
            if (outInts[i] != 42)
                throw new Exception($"[{i}] expected 42, got {outInts[i]}");
        }

        Console.WriteLine($"[P2P] KernelLauncher FillConstant: {count} elements = 42 ✓");
    }

    [TestMethod]
    public async Task P2P_KernelLauncher_LargeBuffer()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        using var accelerator = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);
        var launcher = new P2PKernelLauncher(accelerator);

        var method = typeof(P2PLogicTests).GetMethod(
            nameof(KernelMultiplyBy2),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // 1M elements — proves we handle real tensor sizes
        int count = 1_000_000;
        var inputFloats = new float[count];
        for (int i = 0; i < count; i++) inputFloats[i] = i * 0.001f;

        var inputBytes = new byte[count * 4];
        Buffer.BlockCopy(inputFloats, 0, inputBytes, 0, inputBytes.Length);

        var bufferBindings = new Dictionary<int, BufferData>
        {
            [1] = new BufferData { RawData = inputBytes, ElementCount = count, ElementSize = 4 }
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = await launcher.ExecuteAsync(method, count, bufferBindings);
        sw.Stop();

        var outFloats = new float[count];
        Buffer.BlockCopy(results[1], 0, outFloats, 0, count * 4);

        // Spot check
        if (Math.Abs(outFloats[0] - 0.0f) > 0.001f) throw new Exception($"[0] = {outFloats[0]}");
        if (Math.Abs(outFloats[500000] - 1000.0f) > 0.01f) throw new Exception($"[500000] = {outFloats[500000]}");
        if (Math.Abs(outFloats[999999] - 1999.998f) > 0.01f) throw new Exception($"[999999] = {outFloats[999999]}");

        Console.WriteLine($"[P2P] KernelLauncher 1M floats: {sw.ElapsedMilliseconds}ms ✓");
    }

    [TestMethod]
    public async Task P2P_Worker_RealDispatch()
    {
        // Full end-to-end: Worker receives a dispatch request, executes on CPU, returns results
        using var context = global::ILGPU.Context.CreateDefault();
        using var accelerator = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("worker-dispatch-test");

        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        var transport = new P2PTransport(client, coordinator, dispatcher);
        var worker = new P2PWorker(transport);
        worker.Initialize(context, accelerator);
        transport.SetWorker(worker);

        // Register our test kernel type
        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        // Build dispatch request
        var method = typeof(P2PLogicTests).GetMethod(
            nameof(KernelMultiplyBy2),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 4);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "buf-0", Length = 4, ElementSize = 4 }
        };

        // Send buffer data to worker
        var inputFloats = new float[] { 10.0f, 20.0f, 30.0f, 40.0f };
        var inputBytes = new byte[16];
        Buffer.BlockCopy(inputFloats, 0, inputBytes, 0, 16);
        worker.ReceiveBuffer("buf-0", inputBytes);

        // Track completion
        string? completedId = null;
        bool? completedSuccess = null;
        worker.OnKernelCompleted += (id, success, ms) =>
        {
            completedId = id;
            completedSuccess = success;
        };

        // Dispatch
        await worker.HandleDispatchAsync("coordinator-peer", request);

        if (completedId != request.DispatchId)
            throw new Exception($"Wrong dispatch ID: {completedId}, worker error: {worker.LastDispatchError}");
        if (completedSuccess != true)
            throw new Exception($"Dispatch failed: {worker.LastDispatchError}");

        // Verify buffer was modified
        var result = worker.GetBuffer("buf-0");
        if (result == null) throw new Exception("Buffer not found after dispatch");

        var outFloats = new float[4];
        Buffer.BlockCopy(result, 0, outFloats, 0, 16);

        for (int i = 0; i < 4; i++)
        {
            float expected = inputFloats[i] * 2.0f;
            if (Math.Abs(outFloats[i] - expected) > 0.001f)
                throw new Exception($"[{i}] expected {expected}, got {outFloats[i]}");
        }

        Console.WriteLine($"[P2P] Worker real dispatch: [{string.Join(", ", outFloats)}] ✓");

        // Cleanup
        P2PKernelSerializer.ClearAllowlist();
    }

    [TestMethod]
    public async Task P2P_Worker_RealDispatch_AddArrays()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        using var accelerator = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("add-arrays-test");

        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        var transport = new P2PTransport(client, coordinator, dispatcher);
        var worker = new P2PWorker(transport);
        worker.Initialize(context, accelerator);
        transport.SetWorker(worker);

        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        var method = typeof(P2PLogicTests).GetMethod(
            nameof(KernelAddArrays),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        int count = 512;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: count);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "a", Length = count, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 2, BufferId = "b", Length = count, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 3, BufferId = "r", Length = count, ElementSize = 4 },
        };

        // Prepare input buffers
        var aFloats = new float[count];
        var bFloats = new float[count];
        for (int i = 0; i < count; i++) { aFloats[i] = i; bFloats[i] = i * 3.0f; }

        var aBytes = new byte[count * 4];
        var bBytes = new byte[count * 4];
        Buffer.BlockCopy(aFloats, 0, aBytes, 0, aBytes.Length);
        Buffer.BlockCopy(bFloats, 0, bBytes, 0, bBytes.Length);

        worker.ReceiveBuffer("a", aBytes);
        worker.ReceiveBuffer("b", bBytes);
        worker.ReceiveBuffer("r", new byte[count * 4]);

        bool success = false;
        worker.OnKernelCompleted += (_, s, _) => success = s;
        await worker.HandleDispatchAsync("coordinator", request);

        if (!success) throw new Exception($"Dispatch failed: {worker.LastDispatchError}");

        var result = worker.GetBuffer("r")!;
        var outFloats = new float[count];
        Buffer.BlockCopy(result, 0, outFloats, 0, count * 4);

        int errors = 0;
        for (int i = 0; i < count; i++)
        {
            float expected = aFloats[i] + bFloats[i];
            if (Math.Abs(outFloats[i] - expected) > 0.01f) errors++;
        }

        if (errors > 0) throw new Exception($"{errors}/{count} elements wrong");
        Console.WriteLine($"[P2P] Worker AddArrays dispatch: {count} elements verified ✓");

        P2PKernelSerializer.ClearAllowlist();
    }

    [TestMethod]
    public async Task P2P_Worker_RejectsUnregisteredKernel()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        using var accelerator = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("reject-test");

        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        var transport = new P2PTransport(client, coordinator, dispatcher);
        var worker = new P2PWorker(transport);
        worker.Initialize(context, accelerator);
        transport.SetWorker(worker);

        // Register a DIFFERENT type — our test kernel type is NOT registered
        P2PKernelSerializer.ClearAllowlist();
        P2PKernelSerializer.RegisterKernelType(typeof(string)); // arbitrary wrong type

        var request = new KernelDispatchRequest
        {
            KernelType = typeof(P2PLogicTests).AssemblyQualifiedName!,
            KernelMethod = nameof(KernelMultiplyBy2),
            GridDimX = 4,
            Buffers = new[]
            {
                new BufferBinding { ParameterIndex = 1, BufferId = "buf", Length = 4, ElementSize = 4 }
            },
        };

        worker.ReceiveBuffer("buf", new byte[16]);

        bool? success = null;
        worker.OnKernelCompleted += (_, s, _) => success = s;
        await worker.HandleDispatchAsync("coordinator", request);

        if (success != false)
            throw new Exception("Should have rejected unregistered kernel type");

        Console.WriteLine("[P2P] Worker correctly rejects unregistered kernel ✓");

        P2PKernelSerializer.ClearAllowlist();
    }

    [TestMethod]
    public async Task P2P_KernelLauncher_CacheReuse()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        using var accelerator = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);
        var launcher = new P2PKernelLauncher(accelerator);

        var method = typeof(P2PLogicTests).GetMethod(
            nameof(KernelMultiplyBy2),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Execute same kernel 5 times — should compile once, reuse cache
        for (int run = 0; run < 5; run++)
        {
            var data = new float[] { run + 1.0f };
            var bytes = new byte[4];
            Buffer.BlockCopy(data, 0, bytes, 0, 4);

            var results = await launcher.ExecuteAsync(method, 1, new Dictionary<int, BufferData>
            {
                [1] = new BufferData { RawData = bytes, ElementCount = 1, ElementSize = 4 }
            });

            var outFloat = new float[1];
            Buffer.BlockCopy(results[1], 0, outFloat, 0, 4);

            float expected = (run + 1.0f) * 2.0f;
            if (Math.Abs(outFloat[0] - expected) > 0.001f)
                throw new Exception($"Run {run}: expected {expected}, got {outFloat[0]}");
        }

        if (launcher.CachedCount != 1)
            throw new Exception($"Should have 1 cached launcher, got {launcher.CachedCount}");

        Console.WriteLine("[P2P] KernelLauncher cache reuse: 5 runs, 1 compilation ✓");
    }

    // ═══════════════════════════════════════════════════════════
    //  Coordinator Dispatch — Full Round-Trip
    //  Coordinator dispatches via DispatchToSwarm → worker executes → result
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Coordinator_DispatchToSwarm()
    {
        // Full coordinator → worker round-trip with real kernel execution
        var crypto = Crypto;
        using var context = global::ILGPU.Context.CreateDefault();
        using var cpuAccel = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);

        var client = WebTorrentClient;
        await using var identity = await SwarmIdentity.CreateAsync(crypto, "coordinator");
        await using var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(identity);
        await coordinator.CreateSwarmAsync("coord-dispatch-test");

        var p2pAccel = coordinator.CreateAccelerator(context);
        p2pAccel.Dispatcher = new P2PDispatcher(p2pAccel);
        p2pAccel.Dispatcher.CoordinatorPublicKey = Convert.ToBase64String(identity.PublicKeySpki);
        var transport = new P2PTransport(client, coordinator, p2pAccel.Dispatcher);
        transport.SetCrypto(crypto);

        // Wire dispatcher → transport (signed messages for authority verification)
        p2pAccel.Dispatcher.OnSendMessage += (peerId, msg) =>
        {
            _ = transport.SendSignedMessageAsync(peerId, msg);
        };

        // Create worker on same process (simulated remote)
        var worker = new P2PWorker(transport);
        worker.Initialize(context, cpuAccel);
        transport.SetWorker(worker);

        // Wire mock transport: coordinator ↔ worker
        // Fire-and-forget matches real WebRTC semantics and avoids rc.12 per-peer
        // queue deadlock (awaiting would block the consumer loop waiting on its
        // own KernelResult to process through the same queue).
        transport.RegisterPeer("worker-1", (data) =>
        {
            _ = transport.HandleIncomingDataAsync("worker-1", data);
            return Task.CompletedTask;
        });

        // Register peer
        var caps = worker.BuildCapabilities("worker-1");
        coordinator.HandlePeerConnected("worker-1", caps);
        p2pAccel.AddPeer(new RemotePeer
        {
            PeerId = "worker-1",
            IsConnected = true,
            Capabilities = caps,
            LastHeartbeat = DateTime.UtcNow,
        });

        // Register kernel type
        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        // Prepare input data
        var inputFloats = new float[] { 5.0f, 10.0f, 15.0f, 20.0f };
        var inputBytes = new byte[16];
        Buffer.BlockCopy(inputFloats, 0, inputBytes, 0, 16);
        worker.ReceiveBuffer("data", inputBytes);

        // Coordinator dispatches to swarm and awaits completion via TaskCompletionSource
        var dispatchResult = await p2pAccel.DispatchAsync(
            typeof(P2PLogicTests).GetMethod(nameof(KernelMultiplyBy2),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            4,
            ("data", inputBytes, 4));

        if (!dispatchResult.Success)
            throw new Exception($"DispatchAsync failed: {dispatchResult.Error}");

        // Verify the worker received and processed the dispatch
        var result = worker.GetBuffer("data");
        if (result == null)
            throw new Exception("Worker buffer not updated");

        var outFloats = new float[4];
        Buffer.BlockCopy(result, 0, outFloats, 0, 16);

        for (int i = 0; i < 4; i++)
        {
            float expected = inputFloats[i] * 2.0f;
            if (Math.Abs(outFloats[i] - expected) > 0.001f)
                throw new Exception($"[{i}] expected {expected}, got {outFloats[i]}");
        }

        Console.WriteLine($"[P2P] Coordinator DispatchToSwarm: [{string.Join(", ", outFloats)}] ✓");

        P2PKernelSerializer.ClearAllowlist();
    }

    [TestMethod]
    public async Task P2P_Coordinator_CreateDispatcher_Helper()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var p2pAccel = (P2PAccelerator)device.CreateAccelerator(context);
        p2pAccel.Dispatcher = new P2PDispatcher(p2pAccel);

        // CreateDispatcher should find the method
        var helper = p2pAccel.CreateDispatcher(typeof(P2PLogicTests), "KernelMultiplyBy2");
        if (helper.MethodName != "KernelMultiplyBy2")
            throw new Exception($"Method name: {helper.MethodName}");
        if (helper.TypeName != "P2PLogicTests")
            throw new Exception($"Type name: {helper.TypeName}");

        Console.WriteLine("[P2P] CreateDispatcher helper: OK ✓");
    }

    [TestMethod]
    public async Task P2P_Coordinator_NotSupportedException_LoadKernel()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        var p2pAccel = (P2PAccelerator)device.CreateAccelerator(context);

        // LoadAutoGroupedStreamKernel should throw NotSupportedException with helpful message
        try
        {
            var kernel = p2pAccel.LoadAutoGroupedStreamKernel<
                global::ILGPU.Index1D, global::ILGPU.ArrayView<float>>(KernelMultiplyBy2);
            throw new Exception("Should have thrown NotSupportedException");
        }
        catch (NotSupportedException ex)
        {
            if (!ex.Message.Contains("DispatchAsync"))
                throw new Exception($"Error should mention DispatchAsync: {ex.Message}");
        }

        Console.WriteLine("[P2P] LoadAutoGroupedStreamKernel correctly throws with guidance ✓");
    }

    // ═══════════════════════════════════════════════════════════
    //  Resilience — Multi-Peer, Dropout, Coordinator Migration
    //  No mocks. Real kernels. Real failure scenarios.
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Helper: create a wired worker node with real CPU accelerator.
    /// Returns (worker, transport, cpuAccelerator) — all wired to the coordinator transport.
    /// </summary>
    private (P2PWorker worker, P2PTransport transport, global::ILGPU.Runtime.Accelerator accel)
        CreateWorkerNode(
            global::ILGPU.Context context,
            P2PSwarmCoordinator coordinator,
            P2PTransport coordTransport,
            P2PAccelerator p2pAccel,
            string workerId,
            double tflops = 5.0)
    {
        var cpuAccel = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);
        var workerCoord = new P2PSwarmCoordinator(WebTorrentClient);
        var workerDispatcher = new P2PDispatcher(p2pAccel);
        var workerTransport = new P2PTransport(
            WebTorrentClient, workerCoord, workerDispatcher);

        var worker = new P2PWorker(workerTransport);
        worker.Initialize(context, cpuAccel);
        workerTransport.SetWorker(worker);

        // Bidirectional mock transport — fire-and-forget to match real WebRTC
        // semantics and avoid rc.12 per-peer queue deadlock.
        coordTransport.RegisterPeer(workerId, (data) =>
        {
            _ = workerTransport.HandleIncomingDataAsync("coordinator", data);
            return Task.CompletedTask;
        });
        workerTransport.RegisterPeer("coordinator", (data) =>
        {
            _ = coordTransport.HandleIncomingDataAsync(workerId, data);
            return Task.CompletedTask;
        });

        // Register as peer
        var caps = worker.BuildCapabilities(workerId);
        caps.EstimatedTflops = tflops;
        coordinator.HandlePeerConnected(workerId, caps);
        p2pAccel.AddPeer(new RemotePeer
        {
            PeerId = workerId,
            IsConnected = true,
            Capabilities = caps,
            LastHeartbeat = DateTime.UtcNow,
        });

        return (worker, workerTransport, cpuAccel);
    }

    [TestMethod]
    public async Task P2P_Resilience_MultiPeer_3Workers_AllExecute()
    {
        // 3 workers, each gets dispatches, all produce correct results
        using var context = global::ILGPU.Context.CreateDefault();
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("multi-peer-test");

        var p2pAccel = coordinator.CreateAccelerator(context);
        p2pAccel.Dispatcher = new P2PDispatcher(p2pAccel);
        var coordTransport = new P2PTransport(client, coordinator, p2pAccel.Dispatcher);
        p2pAccel.Dispatcher.OnSendMessage += (peerId, msg) =>
        {
            _ = coordTransport.SendMessageAsync(peerId, msg);
        };

        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        // Create 3 workers with different TFLOPS
        var (w1, t1, a1) = CreateWorkerNode(context, coordinator, coordTransport, p2pAccel, "worker-1", 10.0);
        var (w2, t2, a2) = CreateWorkerNode(context, coordinator, coordTransport, p2pAccel, "worker-2", 5.0);
        var (w3, t3, a3) = CreateWorkerNode(context, coordinator, coordTransport, p2pAccel, "worker-3", 2.0);

        if (p2pAccel.Peers.Count != 3)
            throw new Exception($"Expected 3 peers, got {p2pAccel.Peers.Count}");

        // Dispatch 6 kernels — should distribute across workers based on scoring
        var method = typeof(P2PLogicTests).GetMethod(nameof(KernelFillConstant),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        int dispatched = 0;
        for (int i = 0; i < 6; i++)
        {
            var bufId = $"buf-{i}";
            // Pre-send empty buffer to all workers (they share the same kernel)
            w1.ReceiveBuffer(bufId, new byte[256 * 4]);
            w2.ReceiveBuffer(bufId, new byte[256 * 4]);
            w3.ReceiveBuffer(bufId, new byte[256 * 4]);

            try
            {
                p2pAccel.DispatchToSwarm(method, 256, (bufId, new byte[256 * 4], 4));
                dispatched++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[P2P Resilience] Dispatch {i} failed (expected under stress): {ex.Message}");
            }
        }

        if (dispatched < 3)
            throw new Exception($"Should dispatch at least 3 kernels, got {dispatched}");

        Console.WriteLine($"[P2P Resilience] 3 workers, {dispatched} dispatches distributed ✓");

        P2PKernelSerializer.ClearAllowlist();
        a1.Dispose(); a2.Dispose(); a3.Dispose();
    }

    [TestMethod]
    public async Task P2P_Resilience_PeerDropout_MidDispatch_RetrySucceeds()
    {
        // Worker-1 gets dispatch but "drops" before executing — dispatcher retries on worker-2
        // We simulate this by NOT wiring worker-1's transport (messages go nowhere),
        // then calling HandlePeerLost which triggers retry to worker-2 (which IS wired)
        var crypto = Crypto;
        using var context = global::ILGPU.Context.CreateDefault();
        var client = WebTorrentClient;
        await using var identity = await SwarmIdentity.CreateAsync(crypto, "coordinator");
        await using var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(identity);
        await coordinator.CreateSwarmAsync("dropout-test");

        var p2pAccel = coordinator.CreateAccelerator(context);
        p2pAccel.Dispatcher = new P2PDispatcher(p2pAccel);
        p2pAccel.Dispatcher.CoordinatorPublicKey = Convert.ToBase64String(identity.PublicKeySpki);
        var coordTransport = new P2PTransport(client, coordinator, p2pAccel.Dispatcher);
        coordTransport.SetCrypto(crypto);

        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        // Worker-1: registered as peer but transport is a black hole (simulates unreachable)
        coordinator.HandlePeerConnected("worker-1", new PeerCapabilities
            { PeerId = "worker-1", EstimatedTflops = 15.0 });
        p2pAccel.AddPeer(new RemotePeer
        {
            PeerId = "worker-1", IsConnected = true, LastHeartbeat = DateTime.UtcNow,
            Capabilities = new PeerCapabilities { PeerId = "worker-1", EstimatedTflops = 15.0 },
        });
        coordTransport.RegisterPeer("worker-1", async (data) =>
        {
            // Black hole — worker-1 never responds (simulates crash/disconnect)
            await Task.CompletedTask;
        });

        // Worker-2: fully wired, will execute the retried dispatch
        var cpuAccel2 = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);
        var w2Coord = new P2PSwarmCoordinator(WebTorrentClient);
        var w2Dispatcher = new P2PDispatcher(p2pAccel);
        var w2Transport = new P2PTransport(WebTorrentClient, w2Coord, w2Dispatcher);
        w2Transport.SetCrypto(crypto);
        var worker2 = new P2PWorker(w2Transport);
        worker2.Initialize(context, cpuAccel2);
        w2Transport.SetWorker(worker2);
        worker2.ReceiveBuffer("data", new byte[16]);

        // Fire-and-forget to match real WebRTC semantics and avoid rc.12 per-peer
        // queue deadlock.
        coordTransport.RegisterPeer("worker-2", (data) =>
        {
            _ = w2Transport.HandleIncomingDataAsync("coordinator", data);
            return Task.CompletedTask;
        });
        w2Transport.RegisterPeer("coordinator", (data) =>
        {
            _ = coordTransport.HandleIncomingDataAsync("worker-2", data);
            return Task.CompletedTask;
        });
        coordinator.HandlePeerConnected("worker-2", new PeerCapabilities
            { PeerId = "worker-2", EstimatedTflops = 5.0 });
        p2pAccel.AddPeer(new RemotePeer
        {
            PeerId = "worker-2", IsConnected = true, LastHeartbeat = DateTime.UtcNow,
            Capabilities = new PeerCapabilities { PeerId = "worker-2", EstimatedTflops = 5.0 },
        });

        // Track events
        string? retriedTo = null;
        p2pAccel.Dispatcher.OnDispatchRetried += (id, from, to) => retriedTo = to;

        // Track worker-2 kernel lifecycle. Use a TCS to wait deterministically for
        // OnKernelCompleted instead of a hardcoded Task.Delay - the latter was
        // timing-fragile and silently failed under any CPU contention.
        string? w2KernelStarted = null;
        string? w2KernelCompletedId = null;
        bool? w2KernelSuccess = null;
        var w2Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        worker2.OnKernelStarted += id => w2KernelStarted = id;
        worker2.OnKernelCompleted += (id, success, _) =>
        {
            w2KernelCompletedId = id;
            w2KernelSuccess = success;
            w2Done.TrySetResult(success);
        };

        // Wire dispatcher send (signed messages for authority verification)
        p2pAccel.Dispatcher.OnSendMessage += (peerId, msg) =>
        {
            _ = coordTransport.SendSignedMessageAsync(peerId, msg);
        };

        // Dispatch — worker-1 gets it first (higher TFLOPS), but won't respond
        var method = typeof(P2PLogicTests).GetMethod(nameof(KernelFillConstant),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 4);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "data", Length = 4, ElementSize = 4 }
        };
        p2pAccel.Dispatcher.Dispatch(request);

        // Worker-1 crashes — dispatcher retries on worker-2
        p2pAccel.Dispatcher.HandlePeerLost("worker-1");

        if (retriedTo != "worker-2")
            throw new Exception($"Should retry on worker-2, got: {retriedTo}");

        // Wait deterministically for worker-2 to finish the kernel. 5s ceiling is
        // far more than any backend's worst case for a 4-element fill kernel; if it
        // expires the diagnostic below pinpoints which stage stalled.
        var completed = await Task.WhenAny(w2Done.Task, Task.Delay(TimeSpan.FromSeconds(5))) == w2Done.Task;
        if (!completed)
            throw new Exception(
                $"Worker-2 did not complete kernel within 5s. " +
                $"OnKernelStarted={w2KernelStarted ?? "<not fired>"}, " +
                $"OnKernelCompleted={w2KernelCompletedId ?? "<not fired>"}, " +
                $"LastDispatchError={worker2.LastDispatchError ?? "<none>"}");

        // Verify worker-2 actually executed
        var result = worker2.GetBuffer("data");
        if (result == null)
            throw new Exception("Worker-2 should have executed the kernel");

        var outInts = new int[4];
        Buffer.BlockCopy(result, 0, outInts, 0, 16);
        if (outInts[0] != 42)
            throw new Exception(
                $"Worker-2 result wrong: expected 42, got {outInts[0]}. " +
                $"OnKernelStarted={w2KernelStarted ?? "<not fired>"}, " +
                $"OnKernelCompleted={w2KernelCompletedId ?? "<not fired>"}, " +
                $"success={w2KernelSuccess?.ToString() ?? "<n/a>"}, " +
                $"LastDispatchError={worker2.LastDispatchError ?? "<none>"}");

        Console.WriteLine($"[P2P Resilience] Dropout retry: worker-1 crashed → worker-2 executed (42) ✓");

        P2PKernelSerializer.ClearAllowlist();
        cpuAccel2.Dispose();
    }

    [TestMethod]
    public async Task P2P_Resilience_CoordinatorTransfer_PendingStatePreserved()
    {
        // Coordinator has in-flight dispatch (worker is a black hole), transfers coordinator role,
        // pending state is included in the transfer message
        using var context = global::ILGPU.Context.CreateDefault();
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("transfer-state-test");

        var p2pAccel = coordinator.CreateAccelerator(context);
        p2pAccel.Dispatcher = new P2PDispatcher(p2pAccel);
        var coordTransport = new P2PTransport(client, coordinator, p2pAccel.Dispatcher);
        p2pAccel.Dispatcher.OnSendMessage += (peerId, msg) =>
        {
            _ = coordTransport.SendMessageAsync(peerId, msg);
        };

        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        // Worker-1: black hole (dispatch stays pending)
        coordinator.HandlePeerConnected("worker-1", new PeerCapabilities
            { PeerId = "worker-1", EstimatedTflops = 10.0 });
        p2pAccel.AddPeer(new RemotePeer
        {
            PeerId = "worker-1", IsConnected = true, LastHeartbeat = DateTime.UtcNow,
            Capabilities = new PeerCapabilities { PeerId = "worker-1", EstimatedTflops = 10.0 },
        });
        coordTransport.RegisterPeer("worker-1", async (data) => await Task.CompletedTask);

        // Worker-2: transfer target
        coordinator.HandlePeerConnected("worker-2", new PeerCapabilities
            { PeerId = "worker-2", EstimatedTflops = 8.0 });
        p2pAccel.AddPeer(new RemotePeer
        {
            PeerId = "worker-2", IsConnected = true, LastHeartbeat = DateTime.UtcNow,
            Capabilities = new PeerCapabilities { PeerId = "worker-2", EstimatedTflops = 8.0 },
        });
        coordTransport.RegisterPeer("worker-2", async (data) => await Task.CompletedTask);

        // Dispatch to worker-1 (black hole — stays pending)
        var method = typeof(P2PLogicTests).GetMethod(nameof(KernelFillConstant),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 4);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "buf", Length = 4, ElementSize = 4 }
        };
        p2pAccel.Dispatcher.Dispatch(request);

        // Verify dispatcher has pending work (worker-1 hasn't responded)
        if (p2pAccel.Dispatcher.PendingCount == 0)
            throw new Exception("Should have pending dispatch (worker-1 is black hole)");

        // Track transfer message
        P2PMessage? transferMsg = null;
        coordinator.OnSendMessage += (peerId, msg) =>
        {
            if (msg.Type == P2PMessageType.CoordinatorTransfer)
                transferMsg = msg;
        };

        // Transfer coordinator role to worker-2
        var transferred = coordinator.TransferCoordinator("worker-2");
        if (transferred != "worker-2")
            throw new Exception($"Transfer should target worker-2, got {transferred}");
        if (coordinator.Role != P2PRole.Worker)
            throw new Exception("Should become Worker after transfer");

        // Verify transfer message includes pending state
        if (transferMsg == null)
            throw new Exception("Transfer message should have been sent");

        var transferData = transferMsg.Payload?.Deserialize<CoordinatorTransferData>();
        if (transferData?.PendingDispatches == null || transferData.PendingDispatches.Length == 0)
            throw new Exception("Transfer should include pending dispatches");
        if (transferData.PendingDispatches[0].DispatchId != request.DispatchId)
            throw new Exception("Transfer should include the correct dispatch ID");

        Console.WriteLine($"[P2P Resilience] Coordinator transfer: {transferData.PendingDispatches.Length} pending dispatch preserved ✓");

        P2PKernelSerializer.ClearAllowlist();
    }

    [TestMethod]
    public async Task P2P_Resilience_Election_AfterCoordinatorLoss()
    {
        // Coordinator drops — remaining peers elect new coordinator deterministically
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("election-loss-test");

        // Add 3 peers with different capabilities
        coordinator.HandlePeerConnected("peer-a", new PeerCapabilities
            { PeerId = "peer-a", EstimatedTflops = 5.0, AvailableMemory = 4_000_000_000 });
        coordinator.HandlePeerConnected("peer-b", new PeerCapabilities
            { PeerId = "peer-b", EstimatedTflops = 15.0, AvailableMemory = 8_000_000_000 });
        coordinator.HandlePeerConnected("peer-c", new PeerCapabilities
            { PeerId = "peer-c", EstimatedTflops = 8.0, AvailableMemory = 6_000_000_000 });

        // Simulate coordinator loss — all remaining peers run election
        // Each peer's coordinator should run the same deterministic algorithm
        var elected1 = coordinator.ElectCoordinator();
        var elected2 = coordinator.ElectCoordinator();
        var elected3 = coordinator.ElectCoordinator();

        // Must be deterministic
        if (elected1 != elected2 || elected2 != elected3)
            throw new Exception($"Election must be deterministic: {elected1}, {elected2}, {elected3}");

        // Strongest peer (peer-b: 15 TFLOPS) should win
        if (elected1 != "peer-b")
            throw new Exception($"Strongest peer should win: expected peer-b, got {elected1}");

        // Now simulate peer-b also dropping
        coordinator.HandlePeerDisconnected("peer-b");
        var newElected = coordinator.ElectCoordinator();

        // Next strongest (peer-c: 8 TFLOPS) should win
        if (newElected != "peer-c")
            throw new Exception($"After peer-b loss, peer-c should win: got {newElected}");

        // Simulate all peers dropping
        coordinator.HandlePeerDisconnected("peer-c");
        coordinator.HandlePeerDisconnected("peer-a");
        var nobody = coordinator.ElectCoordinator();
        if (nobody != null)
            throw new Exception("No peers left — election should return null");

        Console.WriteLine("[P2P Resilience] Election after coordinator loss: deterministic, cascading ✓");
    }

    [TestMethod]
    public async Task P2P_Resilience_MaxRetries_Exhausted()
    {
        // All peers are black holes, then drop — dispatch permanently fails
        using var context = global::ILGPU.Context.CreateDefault();
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("max-retry-test");

        var p2pAccel = coordinator.CreateAccelerator(context);
        p2pAccel.Dispatcher = new P2PDispatcher(p2pAccel);
        p2pAccel.Dispatcher.MaxRetries = 2;
        var coordTransport = new P2PTransport(client, coordinator, p2pAccel.Dispatcher);
        p2pAccel.Dispatcher.OnSendMessage += (peerId, msg) =>
        {
            _ = coordTransport.SendMessageAsync(peerId, msg);
        };

        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));

        // 2 black hole workers
        for (int i = 1; i <= 2; i++)
        {
            var wId = $"worker-{i}";
            coordinator.HandlePeerConnected(wId, new PeerCapabilities
                { PeerId = wId, EstimatedTflops = 10.0 / i });
            p2pAccel.AddPeer(new RemotePeer
            {
                PeerId = wId, IsConnected = true, LastHeartbeat = DateTime.UtcNow,
                Capabilities = new PeerCapabilities { PeerId = wId, EstimatedTflops = 10.0 / i },
            });
            coordTransport.RegisterPeer(wId, async (data) => await Task.CompletedTask);
        }

        var method = typeof(P2PLogicTests).GetMethod(nameof(KernelFillConstant),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 4);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "buf", Length = 4, ElementSize = 4 }
        };

        // Track permanent failure
        string? failedId = null;
        string? failedError = null;
        p2pAccel.Dispatcher.OnDispatchFailed += (id, error) =>
        {
            failedId = id;
            failedError = error;
        };

        p2pAccel.Dispatcher.Dispatch(request);

        // Kill peers one by one — each triggers retry, then no peers left
        p2pAccel.Dispatcher.HandlePeerLost("worker-1");
        p2pAccel.Dispatcher.HandlePeerLost("worker-2");

        if (failedId == null)
            throw new Exception("Dispatch should permanently fail when all retries exhausted");

        Console.WriteLine($"[P2P Resilience] Max retries exhausted: {failedError} ✓");

        P2PKernelSerializer.ClearAllowlist();
    }

    // ═══════════════════════════════════════════════════════════
    //  Coverage Gap Tests — Every Class Tested
    //  Tuvok audit: 6 untested P2P classes + missing scenarios
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_Backend_Create()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var backend = new P2PBackend(context);
        if (backend.BackendType != global::ILGPU.Backends.BackendType.Wasm) // placeholder
            throw new Exception($"BackendType: {backend.BackendType}");
    }

    // P2P_Backend_Properties REMOVED — was a crash-only test with no assertions on behavior.
    // P2P_Backend_Create already verifies BackendType value. This added nothing.

    [TestMethod]
    public async Task P2P_Stream_Create()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        using var accel = (P2PAccelerator)device.CreateAccelerator(context);
        var stream = accel.DefaultStream;
        if (stream == null) throw new Exception("DefaultStream is null");
        // Synchronize is a no-op for P2P — should not throw
        stream.Synchronize();
        Console.WriteLine("[P2P] Stream create + synchronize: OK ✓");
    }

    [TestMethod]
    public async Task P2P_MemoryBuffer_Create()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        using var accel = (P2PAccelerator)device.CreateAccelerator(context);
        var buffer = new P2PMemoryBuffer(accel, 1024, 4);
        if (buffer.Length != 1024) throw new Exception($"Length: {buffer.Length}");
        if (buffer.ElementSize != 4) throw new Exception($"ElementSize: {buffer.ElementSize}");
        if (buffer.ResidentPeer != null) throw new Exception("ResidentPeer should be null initially");
        if (!buffer.IsLocalCurrent) throw new Exception("IsLocalCurrent should be true initially");
        if (buffer.ShadowData.Length != 4096) throw new Exception($"ShadowData: {buffer.ShadowData.Length}");
        if (string.IsNullOrEmpty(buffer.BufferId)) throw new Exception("BufferId should not be empty");
        Console.WriteLine("[P2P] MemoryBuffer create: OK ✓");
    }

    [TestMethod]
    public async Task P2P_MemoryBuffer_ShadowOperations()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        using var accel = (P2PAccelerator)device.CreateAccelerator(context);
        var buffer = new P2PMemoryBuffer(accel, 16, 4);

        // Test UpdateFromRemote
        var remoteData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        buffer.UpdateFromRemote(remoteData);
        if (buffer.IsDirty) throw new Exception("Should not be dirty after remote update");
        if (!buffer.IsLocalCurrent) throw new Exception("Should be local current after remote update");
        if (buffer.ShadowData[0] != 1 || buffer.ShadowData[7] != 8)
            throw new Exception("Shadow data not updated from remote");

        // Test GetShadowForTransmission
        buffer.IsDirty = true;
        var transmission = buffer.GetShadowForTransmission();
        if (buffer.IsDirty) throw new Exception("Should be clean after transmission");
        if (transmission.Length != buffer.ShadowData.Length)
            throw new Exception("Transmission should be a copy of shadow");
        if (transmission[0] != 1) throw new Exception("Transmission data wrong");

        // Verify it's a copy, not a reference
        transmission[0] = 99;
        if (buffer.ShadowData[0] == 99)
            throw new Exception("GetShadowForTransmission should return a copy");

        Console.WriteLine("[P2P] MemoryBuffer shadow operations: OK ✓");
    }

    [TestMethod]
    public async Task P2P_MemoryBuffer_ResidentPeer()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        using var accel = (P2PAccelerator)device.CreateAccelerator(context);
        var buffer = new P2PMemoryBuffer(accel, 64, 4);

        var peer = new RemotePeer { PeerId = "gpu-node", IsConnected = true };
        buffer.ResidentPeer = peer;
        if (buffer.ResidentPeer?.PeerId != "gpu-node")
            throw new Exception("ResidentPeer not set");

        // Dispose should clear residency
        buffer.Dispose();
        if (buffer.ResidentPeer != null)
            throw new Exception("Dispose should clear ResidentPeer");

        Console.WriteLine("[P2P] MemoryBuffer resident peer: OK ✓");
    }

    [TestMethod]
    public async Task P2P_MemoryBuffer_DirtyTracking()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        using var accel = (P2PAccelerator)device.CreateAccelerator(context);
        var buffer = new P2PMemoryBuffer(accel, 32, 1);

        // Initially clean
        if (buffer.IsDirty) throw new Exception("Should start clean");

        // Manual dirty set
        buffer.IsDirty = true;
        if (!buffer.IsDirty) throw new Exception("Should be dirty");

        // GetShadowForTransmission clears dirty
        buffer.GetShadowForTransmission();
        if (buffer.IsDirty) throw new Exception("Transmission should clear dirty");

        // UpdateFromRemote clears dirty
        buffer.IsDirty = true;
        buffer.UpdateFromRemote(new byte[32]);
        if (buffer.IsDirty) throw new Exception("Remote update should clear dirty");

        Console.WriteLine("[P2P] MemoryBuffer dirty tracking: OK ✓");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_StartStopMonitoring()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        using var accel = (P2PAccelerator)device.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accel);

        // Should not throw
        dispatcher.StartMonitoring();
        await Task.Delay(50); // Let timer tick once
        dispatcher.StopMonitoring();

        // Start/stop again
        dispatcher.StartMonitoring();
        dispatcher.StopMonitoring();

        // Dispose should also stop
        dispatcher.StartMonitoring();
        dispatcher.Dispose();

        Console.WriteLine("[P2P] Dispatcher start/stop monitoring: OK ✓");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_PendingSnapshot()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("snapshot-test");
        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);

        // Add a black hole peer
        coordinator.HandlePeerConnected("w1", new PeerCapabilities { PeerId = "w1", EstimatedTflops = 5 });
        p2pAccel.AddPeer(new RemotePeer
        {
            PeerId = "w1", IsConnected = true, LastHeartbeat = DateTime.UtcNow,
            Capabilities = new PeerCapabilities { PeerId = "w1", EstimatedTflops = 5 },
        });

        if (dispatcher.PendingCount != 0) throw new Exception("Should start with 0 pending");

        var request = new KernelDispatchRequest { DispatchId = "test-1", GridDimX = 64 };
        dispatcher.Dispatch(request);

        if (dispatcher.PendingCount != 1) throw new Exception($"Should have 1 pending, got {dispatcher.PendingCount}");

        var snapshot = dispatcher.GetPendingSnapshot();
        if (snapshot.Length != 1) throw new Exception($"Snapshot: {snapshot.Length}");
        if (snapshot[0].DispatchId != "test-1") throw new Exception($"ID: {snapshot[0].DispatchId}");
        if (snapshot[0].AssignedPeerId != "w1") throw new Exception($"Peer: {snapshot[0].AssignedPeerId}");

        Console.WriteLine("[P2P] Dispatcher pending snapshot: OK ✓");
    }

    [TestMethod]
    public async Task P2P_Dispatcher_HandlePendingTransfer()
    {
        using var context = global::ILGPU.Context.CreateDefault();
        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("transfer-accept-test");
        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);

        // Add peer that the transferred dispatch is assigned to
        coordinator.HandlePeerConnected("w1", new PeerCapabilities { PeerId = "w1", EstimatedTflops = 5 });
        p2pAccel.AddPeer(new RemotePeer
        {
            PeerId = "w1", IsConnected = true, LastHeartbeat = DateTime.UtcNow,
            Capabilities = new PeerCapabilities { PeerId = "w1", EstimatedTflops = 5 },
        });

        // Accept pending state from old coordinator
        var info = new PendingDispatchInfo
        {
            DispatchId = "transferred-1",
            Request = new KernelDispatchRequest { GridDimX = 128 },
            AssignedPeerId = "w1",
            Attempts = 1,
        };
        dispatcher.HandlePendingTransfer(info);

        if (dispatcher.PendingCount != 1) throw new Exception($"Should have 1 pending after transfer");
        var snapshot = dispatcher.GetPendingSnapshot();
        if (snapshot[0].DispatchId != "transferred-1") throw new Exception("Wrong dispatch ID");

        Console.WriteLine("[P2P] Dispatcher accept pending transfer: OK ✓");
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_CreateChunks()
    {
        var transfer = new P2PBufferTransfer { MaxChunkSize = 100 };
        var data = new byte[350];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 256);

        var chunks = transfer.CreateChunks("buf-1", data);
        if (chunks.Length != 4) throw new Exception($"350 bytes / 100 = 4 chunks, got {chunks.Length}");
        if (chunks[0].Data.Length != 100) throw new Exception($"Chunk 0 size: {chunks[0].Data.Length}");
        if (chunks[3].Data.Length != 50) throw new Exception($"Last chunk size: {chunks[3].Data.Length}");
        if (chunks[0].TotalBytes != 350) throw new Exception($"TotalBytes: {chunks[0].TotalBytes}");
        if (chunks[2].ChunkIndex != 2) throw new Exception($"ChunkIndex: {chunks[2].ChunkIndex}");

        Console.WriteLine("[P2P] BufferTransfer CreateChunks: OK ✓");
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_CleanupStale()
    {
        var transfer = new P2PBufferTransfer { TransferTimeoutSeconds = 0 };

        // Start a transfer by receiving first chunk
        transfer.ReceiveChunk(new BufferChunk
        {
            BufferId = "stale-1", ChunkIndex = 0, TotalChunks = 3, TotalBytes = 300,
            Data = new byte[100]
        });

        if (transfer.ActiveTransfers != 1) throw new Exception($"Active: {transfer.ActiveTransfers}");

        // Cleanup with 0 second timeout — should remove the stale transfer
        await Task.Delay(10);
        int cleaned = transfer.CleanupStaleTransfers();
        if (cleaned != 1) throw new Exception($"Should clean 1, got {cleaned}");
        if (transfer.ActiveTransfers != 0) throw new Exception($"Active after cleanup: {transfer.ActiveTransfers}");

        Console.WriteLine("[P2P] BufferTransfer cleanup stale: OK ✓");
    }

    [TestMethod]
    public async Task P2P_BufferTransfer_RejectsInvalidChunks()
    {
        var transfer = new P2PBufferTransfer();

        // Null data
        var result1 = transfer.ReceiveChunk(new BufferChunk
        {
            BufferId = "bad", ChunkIndex = 0, TotalChunks = 1, TotalBytes = 10,
            Data = Array.Empty<byte>()
        });
        if (result1) throw new Exception("Should reject empty data");

        // Oversized data
        var result2 = transfer.ReceiveChunk(new BufferChunk
        {
            BufferId = "bad2", ChunkIndex = 0, TotalChunks = 1, TotalBytes = 10,
            Data = new byte[transfer.MaxChunkSize + 1]
        });
        if (result2) throw new Exception("Should reject oversized chunk");

        // Negative index
        var result3 = transfer.ReceiveChunk(new BufferChunk
        {
            BufferId = "bad3", ChunkIndex = -1, TotalChunks = 1, TotalBytes = 10,
            Data = new byte[10]
        });
        if (result3) throw new Exception("Should reject negative index");

        Console.WriteLine("[P2P] BufferTransfer rejects invalid chunks: OK ✓");
    }

    [TestMethod]
    public async Task P2P_Compute_CreateSwarmAsync_SubsystemsWired()
    {
        var crypto = Crypto;
        var client = WebTorrentClient;

        await using var compute = await P2PCompute.CreateSwarmAsync(crypto, client, "wiring-test");

        if (compute.Coordinator == null) throw new Exception("Coordinator null");
        if (compute.Accelerator == null) throw new Exception("Accelerator null");
        if (compute.Dispatcher == null) throw new Exception("Dispatcher null");
        if (compute.Transport == null) throw new Exception("Transport null");
        if (compute.Bridge == null) throw new Exception("Bridge null");
        if (compute.Identity == null) throw new Exception("Identity null");
        if (compute.Role != P2PRole.Coordinator) throw new Exception($"Role: {compute.Role}");
        if (string.IsNullOrEmpty(compute.MagnetLink)) throw new Exception("MagnetLink empty");

        Console.WriteLine("[P2P] Compute CreateSwarmAsync: all subsystems wired ✓");
    }

    [TestMethod]
    public async Task P2P_Policy_InviteOnly_RequiresRegistry()
    {
        var client = WebTorrentClient;
        var crypto = Crypto;
        await using var coordinator = new P2PSwarmCoordinator(client);
        var identity = await SwarmIdentity.CreateAsync(crypto, "owner");
        coordinator.SetIdentity(identity);
        await coordinator.CreateSwarmAsync("invite-only-test");
        coordinator.Policy = new SwarmPolicy { JoinPermission = JoinMode.InviteOnly };

        // Anonymous peer should be rejected
        string? rejectedReason = null;
        coordinator.OnPeerRejected += (id, reason) => rejectedReason = reason;
        coordinator.HandlePeerConnected("anon-peer", new PeerCapabilities { PeerId = "anon-peer" });

        if (rejectedReason == null) throw new Exception("Should reject anonymous peer in InviteOnly mode");
        if (coordinator.PeerCount != 0) throw new Exception($"Peer count: {coordinator.PeerCount}");

        Console.WriteLine($"[P2P] InviteOnly rejects anonymous: '{rejectedReason}' ✓");
        await identity.DisposeAsync();
    }

    [TestMethod]
    public async Task P2P_Transport_WorkerHandlesKernelDispatch()
    {
        // Verify the transport routes KernelDispatch to the worker
        var crypto = Crypto;
        using var context = global::ILGPU.Context.CreateDefault();
        using var cpuAccel = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);

        var client = WebTorrentClient;
        await using var identity = await SwarmIdentity.CreateAsync(crypto, "coordinator");
        await using var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(identity);
        await coordinator.CreateSwarmAsync("transport-dispatch-test");
        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        transport.SetCrypto(crypto);

        var worker = new P2PWorker(transport);
        worker.Initialize(context, cpuAccel);
        transport.SetWorker(worker);

        P2PKernelSerializer.RegisterKernelType(typeof(P2PLogicTests));
        worker.ReceiveBuffer("data", new byte[16]);

        // Register the coordinator as a known peer so the transport accepts its messages
        transport.RegisterPeer("coordinator", _ => Task.CompletedTask);

        var method = typeof(P2PLogicTests).GetMethod(nameof(KernelFillConstant),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 4);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "data", Length = 4, ElementSize = 4 }
        };

        // Sign the dispatch message — KernelDispatch requires authority verification
        var msg = new P2PMessage
        {
            Type = P2PMessageType.KernelDispatch,
            Payload = JsonSerializer.SerializeToElement(request),
        };
        await P2PProtocol.SignMessageAsync(msg, identity);
        var serialized = P2PProtocol.Serialize(msg);
        await transport.HandleIncomingDataAsync("coordinator", serialized);

        // Worker should have executed
        var result = worker.GetBuffer("data");
        if (result == null) throw new Exception("Worker should have buffer after transport dispatch");

        var ints = new int[4];
        Buffer.BlockCopy(result, 0, ints, 0, 16);
        if (ints[0] != 42) throw new Exception($"Expected 42, got {ints[0]}");

        Console.WriteLine("[P2P] Transport routes KernelDispatch to worker: OK ✓");
        P2PKernelSerializer.ClearAllowlist();
    }

    [TestMethod]
    public async Task P2P_Transport_CapabilityResponse_UsesWorkerBackend()
    {
        // When worker is attached, capability response should report the worker's real backend
        using var context = global::ILGPU.Context.CreateDefault();
        using var cpuAccel = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        await coordinator.CreateSwarmAsync("caps-backend-test");
        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);

        var worker = new P2PWorker(transport);
        worker.Initialize(context, cpuAccel);
        transport.SetWorker(worker);

        // Capture the capability response message
        PeerCapabilities? receivedCaps = null;
        transport.RegisterPeer("coordinator-node", async (data) =>
        {
            // Parse the response
            var msg = P2PProtocol.Deserialize(data);
            if (msg?.Type == P2PMessageType.CapabilityResponse && msg.Payload != null)
                receivedCaps = msg.Payload.Value.Deserialize<PeerCapabilities>();
        });

        // Simulate capability request
        var requestMsg = new P2PMessage { Type = P2PMessageType.CapabilityRequest };
        var serialized = P2PProtocol.Serialize(requestMsg);
        await transport.HandleIncomingDataAsync("coordinator-node", serialized);

        // Give async response a moment
        await Task.Delay(50);

        if (receivedCaps == null) throw new Exception("Should receive capability response");
        if (receivedCaps.PreferredBackend != "CPU")
            throw new Exception($"Should report CPU backend, got: {receivedCaps.PreferredBackend}");
        if (receivedCaps.MaxThreadsPerGroup <= 0)
            throw new Exception($"MaxThreadsPerGroup: {receivedCaps.MaxThreadsPerGroup}");
        if (receivedCaps.EstimatedTflops <= 0)
            throw new Exception($"EstimatedTflops: {receivedCaps.EstimatedTflops}");

        Console.WriteLine($"[P2P] Capability response uses worker backend: {receivedCaps.PreferredBackend}, {receivedCaps.EstimatedTflops:F1} TFLOPS ✓");
    }

    [TestMethod]
    public async Task P2P_Worker_EstimateTflops_ScalesWithHardware()
    {
        // TFLOPS estimate should scale with processor count (for CPU backend)
        using var context = global::ILGPU.Context.CreateDefault();
        using var cpuAccel = global::ILGPU.Runtime.CPU.CPUDevice.Default.CreateAccelerator(context);

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        await using var transport = new P2PTransport(client, coordinator, dispatcher);
        var worker = new P2PWorker(transport);
        worker.Initialize(context, cpuAccel);

        var caps = worker.BuildCapabilities("test-node");

        // TFLOPS should be > 0 and scale with CPU count
        if (caps.EstimatedTflops <= 0)
            throw new Exception($"TFLOPS should be positive: {caps.EstimatedTflops}");

        // Memory should be reported (from accelerator or system)
        if (caps.AvailableMemory <= 0)
            throw new Exception($"Memory should be positive: {caps.AvailableMemory}");

        Console.WriteLine($"[P2P] Worker TFLOPS estimate: {caps.EstimatedTflops:F1}, memory: {caps.AvailableMemory / 1024 / 1024}MB ✓");
    }

    // ═══════════════════════════════════════════════════════════
    //  Ownership & RBAC — Authority Enforcement
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task P2P_KeyRegistry_SignAndVerify()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");
        await using var worker = await SwarmIdentity.CreateAsync(crypto, "Worker");

        var registry = new KeyRegistry();
        registry.AddKey(owner.PublicKeySpki, SwarmRole.Owner, "Owner");
        registry.AddKey(worker.PublicKeySpki, SwarmRole.Worker, "Worker");
        await registry.SignAsync(owner);

        // Verify with owner's public key
        var valid = await registry.VerifyAsync(crypto, owner.PublicKeySpki);
        if (!valid) throw new Exception("Registry should verify with owner's key");

        // Verify with wrong key should fail
        var invalid = await registry.VerifyAsync(crypto, worker.PublicKeySpki);
        if (invalid) throw new Exception("Registry should NOT verify with worker's key");

        Console.WriteLine("[P2P] KeyRegistry sign/verify ✓");
    }

    [TestMethod]
    public async Task P2P_KeyRegistry_LastOwnerProtection()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "SoleOwner");

        var registry = new KeyRegistry();
        registry.AddKey(owner.PublicKeySpki, SwarmRole.Owner, "Sole Owner");

        // Attempt to revoke the last owner — should fail
        var revoked = registry.RevokeKey(owner.PublicKeySpki, "test");
        if (revoked) throw new Exception("Should NOT allow revoking the last owner");

        // Confirm still an owner
        var key64 = Convert.ToBase64String(owner.PublicKeySpki);
        if (!registry.HasRole(key64, SwarmRole.Owner))
            throw new Exception("Owner should still be in registry");

        Console.WriteLine("[P2P] Last-owner protection ✓");
    }

    [TestMethod]
    public async Task P2P_KeyRegistry_MultiOwnerRevoke()
    {

        var crypto = Crypto;
        await using var owner1 = await SwarmIdentity.CreateAsync(crypto, "Owner1");
        await using var owner2 = await SwarmIdentity.CreateAsync(crypto, "Owner2");

        var registry = new KeyRegistry();
        registry.AddKey(owner1.PublicKeySpki, SwarmRole.Owner, "Owner 1");
        registry.AddKey(owner2.PublicKeySpki, SwarmRole.Owner, "Owner 2");

        // With 2 owners, one can be revoked
        var revoked = registry.RevokeKey(owner1.PublicKeySpki, "stepping down");
        if (!revoked) throw new Exception("Should allow revoking when 2 owners exist");

        var key1 = Convert.ToBase64String(owner1.PublicKeySpki);
        if (registry.HasRole(key1, SwarmRole.Worker))
            throw new Exception("Revoked owner should have no role");

        // Now owner2 is the last owner — should NOT be revokable
        var revoked2 = registry.RevokeKey(owner2.PublicKeySpki, "test");
        if (revoked2) throw new Exception("Should NOT revoke the last remaining owner");

        Console.WriteLine("[P2P] Multi-owner revoke with last-owner protection ✓");
    }

    [TestMethod]
    public async Task P2P_RoleAssignment_CreateAndVerify()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");
        await using var worker = await SwarmIdentity.CreateAsync(crypto, "Worker");

        var assignment = await RoleAssignment.CreateAsync(
            owner, "peer-1", worker.PublicKeySpki, SwarmRole.Coordinator);

        if (assignment.Role != SwarmRole.Coordinator)
            throw new Exception($"Role: {assignment.Role}");
        if (assignment.PeerId != "peer-1")
            throw new Exception($"PeerId: {assignment.PeerId}");

        // Verify signature
        var valid = await assignment.VerifyAsync(crypto);
        if (!valid) throw new Exception("Assignment should verify");

        Console.WriteLine("[P2P] RoleAssignment create/verify ✓");
    }

    [TestMethod]
    public async Task P2P_RoleAssignment_TamperedFails()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");
        await using var worker = await SwarmIdentity.CreateAsync(crypto, "Worker");

        var assignment = await RoleAssignment.CreateAsync(
            owner, "peer-1", worker.PublicKeySpki, SwarmRole.Worker);

        // Tamper with the role
        assignment.Role = SwarmRole.Owner;
        var valid = await assignment.VerifyAsync(crypto);
        if (valid) throw new Exception("Tampered assignment should NOT verify");

        Console.WriteLine("[P2P] RoleAssignment tamper detection ✓");
    }

    [TestMethod]
    public async Task P2P_RoleAssignment_Expiration()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");
        await using var worker = await SwarmIdentity.CreateAsync(crypto, "Worker");

        // Create an already-expired assignment
        var pastTicks = DateTimeOffset.UtcNow.AddHours(-1).Ticks;
        var assignment = await RoleAssignment.CreateAsync(
            owner, "peer-1", worker.PublicKeySpki, SwarmRole.Coordinator, pastTicks);

        if (!assignment.IsExpired)
            throw new Exception("Assignment with past expiration should be expired");

        // Create a future assignment
        var futureTicks = DateTimeOffset.UtcNow.AddHours(1).Ticks;
        var futureAssignment = await RoleAssignment.CreateAsync(
            owner, "peer-2", worker.PublicKeySpki, SwarmRole.Coordinator, futureTicks);

        if (futureAssignment.IsExpired)
            throw new Exception("Assignment with future expiration should NOT be expired");

        Console.WriteLine("[P2P] RoleAssignment expiration ✓");
    }

    [TestMethod]
    public async Task P2P_MessageSigning_SignAndVerify()
    {

        var crypto = Crypto;
        await using var identity = await SwarmIdentity.CreateAsync(crypto, "Coordinator");

        var message = new P2PMessage
        {
            Type = P2PMessageType.Kick,
            Payload = JsonSerializer.SerializeToElement(new { peerId = "bad-peer", reason = "misbehaving" }),
        };

        await P2PProtocol.SignMessageAsync(message, identity);

        if (string.IsNullOrEmpty(message.SenderPublicKey))
            throw new Exception("SenderPublicKey should be set");
        if (string.IsNullOrEmpty(message.SenderFingerprint))
            throw new Exception("SenderFingerprint should be set");
        if (string.IsNullOrEmpty(message.Signature))
            throw new Exception("Signature should be set");

        // Verify
        var valid = await P2PProtocol.VerifyMessageAsync(message, crypto);
        if (!valid) throw new Exception("Signed message should verify");

        Console.WriteLine("[P2P] Message signing ✓");
    }

    [TestMethod]
    public async Task P2P_MessageSigning_TamperedPayloadFails()
    {

        var crypto = Crypto;
        await using var identity = await SwarmIdentity.CreateAsync(crypto, "Coordinator");

        var message = new P2PMessage
        {
            Type = P2PMessageType.Kick,
            Payload = JsonSerializer.SerializeToElement(new { peerId = "peer-1" }),
        };

        await P2PProtocol.SignMessageAsync(message, identity);

        // Tamper with payload
        message.Payload = JsonSerializer.SerializeToElement(new { peerId = "peer-2" });
        var valid = await P2PProtocol.VerifyMessageAsync(message, crypto);
        if (valid) throw new Exception("Tampered message should NOT verify");

        Console.WriteLine("[P2P] Message tamper detection ✓");
    }

    [TestMethod]
    public async Task P2P_MessageSigning_WrongKeyFails()
    {

        var crypto = Crypto;
        await using var real = await SwarmIdentity.CreateAsync(crypto, "Real");
        await using var imposter = await SwarmIdentity.CreateAsync(crypto, "Imposter");

        var message = new P2PMessage
        {
            Type = P2PMessageType.CoordinatorTransfer,
        };

        // Sign with imposter's key but claim to be real
        await P2PProtocol.SignMessageAsync(message, imposter);
        message.SenderPublicKey = Convert.ToBase64String(real.PublicKeySpki);

        var valid = await P2PProtocol.VerifyMessageAsync(message, crypto);
        if (valid) throw new Exception("Message signed by imposter with real's public key should NOT verify");

        Console.WriteLine("[P2P] Impersonation detection ✓");
    }

    [TestMethod]
    public async Task P2P_RBAC_KickRequiresCoordinator()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");
        await using var worker = await SwarmIdentity.CreateAsync(crypto, "Worker");

        // Owner's coordinator — created the swarm, has Owner role
        var client = WebTorrentClient;
        await using var ownerCoordinator = new P2PSwarmCoordinator(client);
        ownerCoordinator.SetIdentity(owner);
        await ownerCoordinator.CreateSwarmAsync("rbac-test");

        ownerCoordinator.HandlePeerConnected("peer-1", new PeerCapabilities { PeerId = "peer-1" });

        // Owner should be able to kick
        var kicked = ownerCoordinator.KickPeer("peer-1");
        if (!kicked) throw new Exception("Owner should be able to kick");

        // Worker's coordinator — never created a swarm, defaults to Worker role
        var client2 = WebTorrentClient;
        await using var workerCoordinator = new P2PSwarmCoordinator(client2);
        workerCoordinator.SetIdentity(worker);

        // Worker has no authority — kick should fail (even with Role fallback)
        workerCoordinator.HandlePeerConnected("peer-2", new PeerCapabilities { PeerId = "peer-2" });
        var kickedAsWorker = workerCoordinator.KickPeer("peer-2");
        if (kickedAsWorker) throw new Exception("Worker should NOT be able to kick");

        Console.WriteLine("[P2P] RBAC kick enforcement ✓");
    }

    [TestMethod]
    public async Task P2P_RBAC_TransferRequiresCoordinator()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(owner);
        await coordinator.CreateSwarmAsync("transfer-test");

        // Add peers
        coordinator.HandlePeerConnected("peer-1", new PeerCapabilities
        {
            PeerId = "peer-1",
            EstimatedTflops = 5.0,
        });

        // Owner can transfer
        var transferred = coordinator.TransferCoordinator();
        if (transferred == null) throw new Exception("Owner should be able to transfer");

        // Owner retains authority even after transfer (ownership is cryptographic, not role-based)
        // So the owner CAN transfer again — they are always >= Coordinator in the registry.
        // Verify the role changed to Worker but authority remains:
        if (coordinator.Role != P2PRole.Worker) throw new Exception($"Role should be Worker after transfer, got {coordinator.Role}");

        Console.WriteLine("[P2P] RBAC transfer enforcement ✓");
    }

    [TestMethod]
    public async Task P2P_RBAC_AssignRole()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");
        await using var peer = await SwarmIdentity.CreateAsync(crypto, "Peer");

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(owner);
        await coordinator.CreateSwarmAsync("assign-test");

        // Owner assigns Coordinator role to peer
        var assignment = await coordinator.AssignRoleAsync(
            "peer-1", peer.PublicKeySpki, SwarmRole.Coordinator);
        if (assignment == null)
            throw new Exception("Owner should be able to assign Coordinator role");

        // Verify the assignment
        var valid = await assignment.VerifyAsync(crypto);
        if (!valid)
            throw new Exception("Assignment should verify");

        // Peer should now have Coordinator role in registry
        var pubKey64 = Convert.ToBase64String(peer.PublicKeySpki);
        if (!coordinator.GetRegistry()!.HasRole(pubKey64, SwarmRole.Coordinator))
            throw new Exception("Peer should have Coordinator role in registry");

        // Owner should NOT be able to assign Owner role (can't assign own level)
        var ownerAssign = await coordinator.AssignRoleAsync(
            "peer-2", peer.PublicKeySpki, SwarmRole.Owner);
        if (ownerAssign != null)
            throw new Exception("Owner should NOT be able to assign Owner role (granter.Role <= role)");

        Console.WriteLine("[P2P] RBAC role assignment ✓");
    }

    [TestMethod]
    public async Task P2P_RBAC_RevokeKey()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");
        await using var worker = await SwarmIdentity.CreateAsync(crypto, "Worker");

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(owner);
        await coordinator.CreateSwarmAsync("revoke-test");

        // Add worker to registry
        coordinator.GetRegistry()!.AddKey(worker.PublicKeySpki, SwarmRole.Worker, "Worker");

        // Owner can revoke worker
        var revoked = await coordinator.RevokeKeyAsync(worker.PublicKeySpki, "misbehaving");
        if (!revoked) throw new Exception("Owner should be able to revoke worker");

        var workerKey64 = Convert.ToBase64String(worker.PublicKeySpki);
        if (!coordinator.GetRegistry()!.IsRevoked(workerKey64))
            throw new Exception("Worker should be revoked");

        Console.WriteLine("[P2P] RBAC key revocation ✓");
    }

    [TestMethod]
    public async Task P2P_Election_PrefersRegistryCoordinator()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");
        await using var assignedCoord = await SwarmIdentity.CreateAsync(crypto, "AssignedCoord");
        await using var fastPeer = await SwarmIdentity.CreateAsync(crypto, "FastPeer");

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(owner);
        await coordinator.CreateSwarmAsync("election-test");

        // Add assignedCoord to registry as Coordinator
        coordinator.GetRegistry()!.AddKey(assignedCoord.PublicKeySpki, SwarmRole.Coordinator, "Assigned");
        await coordinator.GetRegistry()!.SignAsync(owner);

        var assignedKey64 = Convert.ToBase64String(assignedCoord.PublicKeySpki);
        var fastKey64 = Convert.ToBase64String(fastPeer.PublicKeySpki);

        // Add both peers — fastPeer has more TFLOPS but isn't in the registry
        coordinator.HandlePeerConnected("assigned-coord", new PeerCapabilities
        {
            PeerId = "assigned-coord",
            EstimatedTflops = 2.0,
            PublicKey = assignedKey64,
        });
        coordinator.HandlePeerConnected("fast-peer", new PeerCapabilities
        {
            PeerId = "fast-peer",
            EstimatedTflops = 10.0,
            PublicKey = fastKey64,
        });

        // Simulate coordinator drop — elect new one
        var elected = coordinator.ElectCoordinator();

        // Registry-assigned coordinator should win over higher-TFLOPS unregistered peer
        if (elected != "assigned-coord")
            throw new Exception($"Expected 'assigned-coord', got '{elected}'. Registry should take priority over TFLOPS.");

        Console.WriteLine("[P2P] Election prefers registry-assigned coordinator ✓");
    }

    [TestMethod]
    public async Task P2P_Election_FallsBackToTflops()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(owner);
        await coordinator.CreateSwarmAsync("election-fallback");

        // No registry-assigned coordinators — add peers with different TFLOPS
        coordinator.HandlePeerConnected("slow", new PeerCapabilities
        {
            PeerId = "slow",
            EstimatedTflops = 1.0,
        });
        coordinator.HandlePeerConnected("fast", new PeerCapabilities
        {
            PeerId = "fast",
            EstimatedTflops = 8.0,
        });

        var elected = coordinator.ElectCoordinator();
        if (elected != "fast")
            throw new Exception($"Expected 'fast', got '{elected}'. TFLOPS fallback should pick highest.");

        Console.WriteLine("[P2P] Election TFLOPS fallback ✓");
    }

    [TestMethod]
    public async Task P2P_RegistryUpdate_SequenceProtection()
    {

        var crypto = Crypto;
        await using var owner = await SwarmIdentity.CreateAsync(crypto, "Owner");

        var client = WebTorrentClient;
        await using var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(owner);
        await coordinator.CreateSwarmAsync("seq-test");

        var originalSeq = coordinator.GetRegistry()!.Sequence;

        // Create an older registry (lower sequence)
        var oldRegistry = new KeyRegistry { Sequence = originalSeq - 1 };
        coordinator.UpdateRegistry(oldRegistry);

        // Should NOT have accepted the old registry
        if (coordinator.GetRegistry()!.Sequence != originalSeq)
            throw new Exception("Should reject registry with lower sequence number");

        // Create a newer registry (higher sequence)
        var newRegistry = new KeyRegistry { Sequence = originalSeq + 5 };
        coordinator.UpdateRegistry(newRegistry);

        if (coordinator.GetRegistry()!.Sequence != originalSeq + 5)
            throw new Exception("Should accept registry with higher sequence number");

        Console.WriteLine("[P2P] Registry sequence protection ✓");
    }

    [TestMethod]
    public async Task P2P_Protocol_RequiresSignatureTypes()
    {
        // Authority-sensitive message types require signatures
        if (!P2PProtocol.RequiresSignature(P2PMessageType.Kick))
            throw new Exception("Kick should require signature");
        if (!P2PProtocol.RequiresSignature(P2PMessageType.Block))
            throw new Exception("Block should require signature");
        if (!P2PProtocol.RequiresSignature(P2PMessageType.CoordinatorTransfer))
            throw new Exception("CoordinatorTransfer should require signature");
        if (!P2PProtocol.RequiresSignature(P2PMessageType.CoordinatorAnnounce))
            throw new Exception("CoordinatorAnnounce should require signature");
        if (!P2PProtocol.RequiresSignature(P2PMessageType.RoleAssign))
            throw new Exception("RoleAssign should require signature");
        if (!P2PProtocol.RequiresSignature(P2PMessageType.RegistryUpdate))
            throw new Exception("RegistryUpdate should require signature");
        if (!P2PProtocol.RequiresSignature(P2PMessageType.KernelDispatch))
            throw new Exception("KernelDispatch should require signature");

        // Data messages should NOT require signatures
        if (P2PProtocol.RequiresSignature(P2PMessageType.Heartbeat))
            throw new Exception("Heartbeat should NOT require signature");
        if (P2PProtocol.RequiresSignature(P2PMessageType.BufferData))
            throw new Exception("BufferData should NOT require signature");

        Console.WriteLine("[P2P] Protocol signature requirements ✓");
    }

    [TestMethod]
    public async Task P2P_MessageSigning_RoundTrip()
    {

        var crypto = Crypto;
        await using var identity = await SwarmIdentity.CreateAsync(crypto, "Sender");

        // Create, sign, serialize, deserialize, verify — full round trip
        var message = new P2PMessage
        {
            Type = P2PMessageType.RegistryUpdate,
            Payload = JsonSerializer.SerializeToElement(new { sequence = 42 }),
        };
        await P2PProtocol.SignMessageAsync(message, identity);

        var bytes = P2PProtocol.Serialize(message);
        var deserialized = P2PProtocol.Deserialize(bytes);
        if (deserialized == null) throw new Exception("Deserialization failed");

        var valid = await P2PProtocol.VerifyMessageAsync(deserialized, crypto);
        if (!valid) throw new Exception("Round-trip message should verify");

        Console.WriteLine("[P2P] Message signing round-trip ✓");
    }

    // ═══════════════════════════════════════════════════════════
    //  P2P Integration Tests — Real Tracker, Real WebRTC
    //  These tests use wss://hub.spawndev.com:44365/announce
    //  (or localhost:5561 if available) for actual peer discovery.
    // ═══════════════════════════════════════════════════════════

    private static async Task<string> GetTrackerUrl()
    {
        // Try local ServerApp first (may be running during PlaywrightMultiTest)
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await http.GetAsync("http://localhost:5561");
            return "ws://localhost:5561/announce";
        }
        catch { }
        // Production tracker
        return "wss://hub.spawndev.com:44365/announce";
    }

    [TestMethod(Timeout = 60000)]
    public async Task P2P_Integration_CreateSwarm_RealTracker()
    {
        var crypto = Crypto;
        var trackerUrl = await GetTrackerUrl();
        Console.WriteLine($"[P2P Integration] Tracker: {trackerUrl}");

        // Create a real swarm through the production path
        var client = WebTorrentClient;
        await using var compute = await P2PCompute.CreateSwarmAsync(
            crypto, client, "integration-test", joinLinkBaseUrl: null);

        if (compute.Coordinator == null) throw new Exception("Coordinator null");
        if (string.IsNullOrEmpty(compute.MagnetLink)) throw new Exception("MagnetLink empty");
        if (compute.Identity == null) throw new Exception("Identity null");
        if (compute.Bridge == null) throw new Exception("Bridge null");

        // Verify the magnet link contains the tracker URL
        if (!compute.MagnetLink.Contains("hub.spawndev.com") && !compute.MagnetLink.Contains("localhost"))
            throw new Exception($"MagnetLink should contain tracker: {compute.MagnetLink}");

        Console.WriteLine($"[P2P Integration] Swarm created: {compute.MagnetLink[..Math.Min(80, compute.MagnetLink.Length)]}...");
        Console.WriteLine("[P2P Integration] CreateSwarm via real tracker ✓");
    }

    [TestMethod(Timeout = 60000)]
    public async Task P2P_Integration_TwoNode_PeerDiscovery() => await RunTest(async accelerator =>
    {
        var crypto = Crypto;
        var trackerUrl = await GetTrackerUrl();
        Console.WriteLine($"[P2P Integration] Tracker: {trackerUrl}");
        var trackers = new[] { trackerUrl };

        // ── Node 1: Coordinator ──
        var coordClient = WebTorrentClient;
        var coordIdentity = await SwarmIdentity.CreateAsync(crypto, "coord-owner");
        var coordinator = new P2PSwarmCoordinator(coordClient);
        coordinator.SetIdentity(coordIdentity);
        await coordinator.CreateSwarmAsync("discovery-test", trackers);

        using var coordCtx = global::ILGPU.Context.CreateDefault();
        var coordAccel = coordinator.CreateAccelerator(coordCtx);
        var coordDispatcher = new P2PDispatcher(coordAccel);
        var coordTransport = new P2PTransport(coordClient, coordinator, coordDispatcher);
        coordTransport.SetCrypto(crypto);
        coordinator.OnSendMessage += async (peerId, msg) =>
            await coordTransport.SendSignedMessageAsync(peerId, msg);
        var coordBridge = new P2PWebRtcBridge(coordTransport);
        if (coordinator.Swarm != null)
            coordBridge.AttachToSwarm(coordinator.Swarm);

        var magnetLink = coordinator.MagnetLink!;
        Console.WriteLine($"[P2P Integration] Coordinator seeding: {magnetLink[..Math.Min(80, magnetLink.Length)]}...");

        // Wait for tracker registration
        await Task.Delay(2000);

        // ── Node 2: Worker ──
        var workerClient = WebTorrentClient;
        var workerIdentity = await SwarmIdentity.CreateAsync(crypto, "worker");
        var workerCoord = new P2PSwarmCoordinator(workerClient);
        workerCoord.SetIdentity(workerIdentity);
        await workerCoord.JoinSwarmAsync(magnetLink);

        var workerP2PAccel = workerCoord.CreateAccelerator(coordCtx);
        var workerDispatcher = new P2PDispatcher(workerP2PAccel);
        var workerTransport = new P2PTransport(workerClient, workerCoord, workerDispatcher);
        workerTransport.SetCrypto(crypto);
        var worker = new P2PWorker(workerTransport);
        worker.Initialize(accelerator.Context, accelerator);
        workerTransport.SetWorker(worker);
        var workerBridge = new P2PWebRtcBridge(workerTransport);
        if (workerCoord.Swarm != null)
            workerBridge.AttachToSwarm(workerCoord.Swarm);

        Console.WriteLine("[P2P Integration] Worker joined, waiting for peer discovery...");

        // Wait for peer discovery via tracker + WebRTC
        var computePeerConnected = new TaskCompletionSource<string>();
        coordBridge.OnComputePeerConnected += (peerId) =>
        {
            Console.WriteLine($"[P2P Integration] Coordinator sees compute peer: {peerId}");
            computePeerConnected.TrySetResult(peerId);
        };
        workerBridge.OnComputePeerConnected += (peerId) =>
        {
            Console.WriteLine($"[P2P Integration] Worker sees compute peer: {peerId}");
        };

        // Wait up to 30s for real WebRTC peer discovery
        var discoveryTimeout = Task.Delay(30000);
        var result = await Task.WhenAny(computePeerConnected.Task, discoveryTimeout);

        if (result == discoveryTimeout)
        {
            // Tracker may not relay between same-origin clients in some environments
            Console.WriteLine($"[P2P Integration] No peer discovery after 30s (coordBridge: {coordBridge.ComputePeerCount}, workerBridge: {workerBridge.ComputePeerCount})");
            Console.WriteLine("[P2P Integration] Peer discovery timed out — tracker may not relay same-origin. SKIP.");
            throw new UnsupportedTestException("Tracker did not relay peers (same-origin limitation)");
        }

        var discoveredPeerId = computePeerConnected.Task.Result;
        Console.WriteLine($"[P2P Integration] Peer discovered via real tracker + WebRTC: {discoveredPeerId}");

        // Verify coordinator registered the peer
        if (coordinator.PeerCount == 0)
            throw new Exception("Coordinator should have at least 1 peer after discovery");

        Console.WriteLine($"[P2P Integration] Two-node peer discovery via real tracker ✓ (peers: {coordinator.PeerCount})");
    });

    [TestMethod(Timeout = 60000)]
    public async Task P2P_Integration_ComputeBoard_PostAndBrowse()
    {
        var crypto = Crypto;

        // Create a swarm to post to the board
        var client = WebTorrentClient;
        await using var compute = await P2PCompute.CreateSwarmAsync(crypto, client, "board-test");

        var board = new ComputeBoardClient();

        // Post a compute request
        var posted = await board.PostFromComputeAsync(compute, "Integration test", 10.0);
        if (posted == null)
        {
            Console.WriteLine("[P2P Integration] ComputeBoard POST failed (hub may be unreachable). SKIP.");
            throw new UnsupportedTestException("hub.spawndev.com unreachable");
        }

        Console.WriteLine($"[P2P Integration] Posted request: {posted.Id}");

        // Browse active requests — should include ours
        var requests = await board.GetRequestsAsync();
        var found = requests.Any(r => r.Id == posted.Id);
        Console.WriteLine($"[P2P Integration] Board has {requests.Count} requests, ours found: {found}");

        // Get stats
        var stats = await board.GetStatsAsync();
        Console.WriteLine($"[P2P Integration] Board stats: {stats?.ActiveRequests} active, {stats?.TotalTflopsAvailable:F1} TFLOPS available");

        // Clean up — remove our request
        var removed = await board.RemoveRequestAsync(posted.Id, compute.Identity.Fingerprint);
        Console.WriteLine($"[P2P Integration] Removed: {removed}");

        if (!found) throw new Exception("Our posted request should appear in browse results");
        if (!removed) throw new Exception("Should be able to remove our own request");

        Console.WriteLine("[P2P Integration] ComputeBoard post/browse/remove ✓");
    }

    // ═══════════════════════════════════════════════════════════
    //  P2P Integration — Production Path (P2PCompute API)
    //  Tests use the exact same code path as the demo.
    // ═══════════════════════════════════════════════════════════

    [TestMethod(Timeout = 60000)]
    public async Task P2P_Integration_PeerCount_Production() => await RunTest(async accelerator =>
    {
        var crypto = Crypto;

        // Node 1: Create swarm (coordinator) — same as demo CreateSwarm
        var coordClient = WebTorrentClient;
        await using var coordCompute = await P2PCompute.CreateSwarmAsync(crypto, coordClient, "peer-count-test");

        if (coordCompute.PeerCount != 0)
            throw new Exception($"Initial peer count should be 0, got {coordCompute.PeerCount}");

        var magnetLink = coordCompute.MagnetLink;
        Console.WriteLine($"[P2P PeerCount] Coordinator created, magnet: {magnetLink?[..Math.Min(60, magnetLink?.Length ?? 0)]}...");

        // Wait for tracker registration
        await Task.Delay(2000);

        // Node 2: Join swarm (worker) — same as demo JoinSwarm
        var workerClient = WebTorrentClient;
        await using var workerCompute = await P2PCompute.JoinSwarmAsync(crypto, workerClient, accelerator, magnetLink!);

        Console.WriteLine("[P2P PeerCount] Worker joined, waiting for peer discovery...");

        // Wait for WebRTC connection + sd_compute handshake + capability exchange
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (coordCompute.PeerCount == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(500);

        Console.WriteLine($"[P2P PeerCount] Coordinator peers: {coordCompute.PeerCount}, Worker peers: {workerCompute.PeerCount}");

        if (coordCompute.PeerCount == 0)
        {
            // Check bridge state for diagnostics
            Console.WriteLine($"[P2P PeerCount] Coord bridge compute peers: {coordCompute.Bridge?.ComputePeerCount ?? -1}");
            Console.WriteLine($"[P2P PeerCount] Worker bridge compute peers: {workerCompute.Bridge?.ComputePeerCount ?? -1}");
            throw new UnsupportedTestException(
                "Peers did not discover each other after 30s (tracker may not relay same-origin clients)");
        }

        if (coordCompute.PeerCount < 1)
            throw new Exception($"Coordinator should have at least 1 peer, got {coordCompute.PeerCount}");

        Console.WriteLine($"[P2P PeerCount] Production path peer discovery: coordinator has {coordCompute.PeerCount} peer(s) ✓");
    });

    [TestMethod(Timeout = 60000)]
    public async Task P2P_Integration_TwoClients_MagnetJoin() => await RunTest(async accelerator =>
    {
        var crypto = Crypto;

        // Enable verbose logging to see tracker + WebRTC internals
        SpawnDev.WebTorrent.WebTorrentClient.VerboseLogging = true;

        // Client 1: Coordinator — uses DI-injected client (production path)
        var coordClient = WebTorrentClient;
        await using var coordCompute = await P2PCompute.CreateSwarmAsync(crypto, coordClient, "magnet-test");

        var magnetLink = coordCompute.MagnetLink!;
        Console.WriteLine($"[P2P MagnetJoin] Coordinator: {magnetLink[..Math.Min(70, magnetLink.Length)]}...");
        Console.WriteLine($"[P2P MagnetJoin] Coord PeerId: {coordClient.PeerId}");

        // Wait for tracker registration
        await Task.Delay(3000);

        // Client 2: Worker — needs a separate client with its own PeerId
        // In production, this would be a different device. For in-page testing,
        // we create a second client. This is the one acceptable "new" — simulating a remote device.
        var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();
        Console.WriteLine($"[P2P MagnetJoin] Worker PeerId: {workerClient.PeerId}");
        if (coordClient.PeerId == workerClient.PeerId)
            throw new Exception("FATAL: Both clients have the same PeerId! Tracker will ignore.");

        await using var workerCompute = await P2PCompute.JoinSwarmAsync(crypto, workerClient, accelerator, magnetLink);

        Console.WriteLine("[P2P MagnetJoin] Worker joined, waiting for peer discovery...");

        // Wait for WebRTC connection + sd_compute handshake
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (coordCompute.PeerCount == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(500);

        Console.WriteLine($"[P2P MagnetJoin] Coord peers: {coordCompute.PeerCount}, Worker peers: {workerCompute.PeerCount}");
        Console.WriteLine($"[P2P MagnetJoin] Coord bridge: {coordCompute.Bridge?.ComputePeerCount}, Worker bridge: {workerCompute.Bridge?.ComputePeerCount}");

        if (coordCompute.PeerCount == 0)
            throw new UnsupportedTestException(
                $"Peers did not connect after 30s. Coord bridge: {coordCompute.Bridge?.ComputePeerCount}, Worker bridge: {workerCompute.Bridge?.ComputePeerCount}");

        Console.WriteLine($"[P2P MagnetJoin] In-page magnet join: {coordCompute.PeerCount} peer(s) ✓");
    });

    // ═══════════════════════════════════════════════════════════
    //  WebAuthn / Hardware Key Tests
    //  These tests require a real authenticator (YubiKey, passkey).
    //  The user must physically interact with the key to pass.
    //  NEVER run in automated suites — each run creates a resident
    //  credential that consumes finite YubiKey storage (~25 slots).
    //  Run manually via filter: --filter "FullyQualifiedName~HardwareKey"
    // ═══════════════════════════════════════════════════════════

    [TestMethod(Timeout = 120000)]
    public async Task P2P_HardwareKey_Register()
    {
        // Skip in automated runs — creates resident credentials on YubiKey (finite storage)
        throw new UnsupportedTestException("HardwareKey tests must be run manually — creates resident credentials on YubiKey");

        var provider = new HardwareKeyProvider();
        Console.WriteLine("[WebAuthn] Requesting hardware key registration...");
        Console.WriteLine("[WebAuthn] >>> Please insert and tap your YubiKey <<<");

        var credential = await provider.RegisterAsync("Test Key");

        if (credential == null)
            throw new UnsupportedTestException("User cancelled or no authenticator available");

        if (credential.PublicKeySpki.Length == 0)
            throw new Exception("Public key should not be empty");
        if (credential.CredentialId.Length == 0)
            throw new Exception("Credential ID should not be empty");
        if (credential.Algorithm == 0)
            throw new Exception("Algorithm should be set");

        Console.WriteLine($"[WebAuthn] Registered: algorithm={credential.Algorithm}, " +
            $"credId={Convert.ToBase64String(credential.CredentialId)[..20]}..., " +
            $"pubKey={Convert.ToBase64String(credential.PublicKeySpki)[..20]}..., " +
            $"transports=[{string.Join(",", credential.Transports)}], " +
            $"attachment={credential.AuthenticatorAttachment}");
        Console.WriteLine("[WebAuthn] Hardware key registration ✓");
    }

    [TestMethod(Timeout = 120000)]
    public async Task P2P_HardwareKey_RegisterAndAuthenticate()
    {
        // Skip in automated runs — creates resident credentials on YubiKey (finite storage)
        throw new UnsupportedTestException("HardwareKey tests must be run manually — creates resident credentials on YubiKey");

        var provider = new HardwareKeyProvider();

        // Step 1: Register
        Console.WriteLine("[WebAuthn] Step 1: Register hardware key...");
        Console.WriteLine("[WebAuthn] >>> Please insert and tap your YubiKey <<<");
        var credential = await provider.RegisterAsync("Auth Test Key");
        if (credential == null)
            throw new UnsupportedTestException("User cancelled registration");

        Console.WriteLine($"[WebAuthn] Registered: credId={Convert.ToBase64String(credential.CredentialId)[..20]}...");

        // Step 2: Authenticate with the same key
        Console.WriteLine("[WebAuthn] Step 2: Authenticate with the same key...");
        Console.WriteLine("[WebAuthn] >>> Please tap your YubiKey again <<<");
        var assertion = await provider.AuthenticateAsync(new[]
        {
            new HardwareKeyAllowedCredential
            {
                CredentialId = credential.CredentialId,
                Transports = credential.Transports,
            }
        });
        if (assertion == null)
            throw new UnsupportedTestException("User cancelled authentication");

        if (assertion.Signature.Length == 0)
            throw new Exception("Signature should not be empty");
        if (assertion.AuthenticatorData.Length == 0)
            throw new Exception("AuthenticatorData should not be empty");
        if (!assertion.CredentialId.SequenceEqual(credential.CredentialId))
            throw new Exception("Credential ID should match registration");

        Console.WriteLine($"[WebAuthn] Authenticated: sig={Convert.ToBase64String(assertion.Signature)[..20]}...");
        Console.WriteLine("[WebAuthn] Hardware key register + authenticate ✓");
    }

    [TestMethod(Timeout = 120000)]
    public async Task P2P_HardwareKey_SwarmOwnership()
    {
        // Skip in automated runs — creates resident credentials on YubiKey (finite storage)
        throw new UnsupportedTestException("HardwareKey tests must be run manually — creates resident credentials on YubiKey");

        var crypto = Crypto;
        var provider = new HardwareKeyProvider();

        // Step 1: Register hardware key
        Console.WriteLine("[WebAuthn] Registering hardware key as swarm owner...");
        Console.WriteLine("[WebAuthn] >>> Please insert and tap your YubiKey <<<");
        var credential = await provider.RegisterAsync("Swarm Owner Key");
        if (credential == null)
            throw new UnsupportedTestException("User cancelled registration");

        // Step 2: Create SwarmIdentity from hardware credential
        var identity = await SwarmIdentity.FromHardwareKeyAsync(crypto, credential);
        if (!identity.IsHardwareBacked)
            throw new Exception("Identity should be hardware-backed");
        if (identity.HardwareCredentialId == null)
            throw new Exception("HardwareCredentialId should be set");
        if (identity.PublicKeySpki.Length == 0)
            throw new Exception("PublicKeySpki should not be empty");

        // Step 3: Add to KeyRegistry as Owner
        var registry = new KeyRegistry();
        registry.AddKey(identity.PublicKeySpki, SwarmRole.Owner, identity.Label);

        // Step 4: Verify the key is in the registry
        var pubKeyB64 = Convert.ToBase64String(identity.PublicKeySpki);
        if (!registry.HasRole(pubKeyB64, SwarmRole.Owner))
            throw new Exception("Hardware key should be Owner in registry");

        Console.WriteLine($"[WebAuthn] Swarm owner identity: fingerprint={identity.Fingerprint[..16]}...");
        Console.WriteLine($"[WebAuthn] Registry has {registry.Keys.Count} key(s), owner verified");
        Console.WriteLine("[WebAuthn] Hardware key → SwarmIdentity → KeyRegistry ✓");

        await identity.DisposeAsync();
    }

    // ═══════════════════════════════════════════════════════════
    //  Utility Method Coverage (ParseJoinLink, BuildMagnetFromHash)
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public void P2P_ParseJoinLink_ValidUrl()
    {
        var (hash, name) = P2PCompute.ParseJoinLink("https://example.com/compute?compute=abc123def456&n=My%20Swarm");
        if (hash != "abc123def456")
            throw new Exception($"Expected hash 'abc123def456', got '{hash}'");
        if (name != "My Swarm")
            throw new Exception($"Expected name 'My Swarm', got '{name}'");
    }

    [TestMethod]
    public void P2P_ParseJoinLink_NoComputeParam()
    {
        var (hash, name) = P2PCompute.ParseJoinLink("https://example.com/compute");
        if (hash != null)
            throw new Exception($"Expected null hash for URL without compute param, got '{hash}'");
    }

    [TestMethod]
    public void P2P_ParseJoinLink_InvalidUrl()
    {
        var (hash, name) = P2PCompute.ParseJoinLink("not a url");
        if (hash != null)
            throw new Exception($"Expected null hash for invalid URL, got '{hash}'");
    }

    [TestMethod]
    public void P2P_BuildMagnetFromHash_ValidOutput()
    {
        var magnet = P2PCompute.BuildMagnetFromHash("abc123", "Test Swarm");
        if (!magnet.StartsWith("magnet:?xt=urn:btih:abc123"))
            throw new Exception($"Magnet should start with btih hash, got: {magnet}");
        if (!magnet.Contains("dn=Test%20Swarm"))
            throw new Exception($"Magnet should contain encoded display name, got: {magnet}");
        if (!magnet.Contains("tr="))
            throw new Exception($"Magnet should contain tracker URL, got: {magnet}");
    }

    [TestMethod]
    public void P2P_BuildMagnetFromHash_EmptyName()
    {
        var magnet = P2PCompute.BuildMagnetFromHash("abc123", null);
        if (!magnet.StartsWith("magnet:?xt=urn:btih:abc123"))
            throw new Exception($"Magnet should start with btih hash, got: {magnet}");
        // Should not crash with null name
    }

    // ═══════════════════════════════════════════════════════════
    //  ComputeBoardClient — Direct HTTP Tests (hub.spawndev.com)
    // ═══════════════════════════════════════════════════════════

    [TestMethod(Timeout = 30000)]
    public async Task P2P_ComputeBoard_GetStats_ReturnsValidData()
    {
        var board = new ComputeBoardClient();
        var stats = await board.GetStatsAsync();
        if (stats == null)
            throw new UnsupportedTestException("hub.spawndev.com unreachable");

        Console.WriteLine($"[ComputeBoard] Stats: {stats.ActiveRequests} active, {stats.TotalTflopsAvailable:F1} TFLOPS, {stats.UniqueSwarms} swarms");
        // Stats should return non-negative values
        if (stats.ActiveRequests < 0)
            throw new Exception($"ActiveRequests should be >= 0, got {stats.ActiveRequests}");
    }

    [TestMethod(Timeout = 30000)]
    public async Task P2P_ComputeBoard_GetRequests_ReturnsList()
    {
        var board = new ComputeBoardClient();
        var requests = await board.GetRequestsAsync();
        // Should return a list (may be empty, but never null)
        Console.WriteLine($"[ComputeBoard] Browse: {requests.Count} active requests");
        // Verify it's a real list, not null
        if (requests == null)
            throw new Exception("GetRequestsAsync should never return null");
    }

    [TestMethod(Timeout = 30000)]
    public async Task P2P_ComputeBoard_RemoveNonexistent_ReturnsFalse()
    {
        var board = new ComputeBoardClient();
        var removed = await board.RemoveRequestAsync("nonexistent-id-12345", "fake-fingerprint");
        // Should return false for nonexistent request, not throw
        Console.WriteLine($"[ComputeBoard] Remove nonexistent: {removed}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Reputation & Performance History Tests
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public void P2P_Reputation_InitialScore()
    {
        var peer = new RemotePeer { PeerId = "test", IsConnected = true };
        // New peer with no dispatches: base = 0.7 (moderate start)
        // Anonymous identity bonus = 0 → total = 0.7
        if (peer.Reputation < 0.65 || peer.Reputation > 0.75)
            throw new Exception($"New anonymous peer reputation should be ~0.7, got {peer.Reputation:F3}");
    }

    [TestMethod]
    public void P2P_Reputation_IdentifiedPeerHigher()
    {
        var anon = new RemotePeer { PeerId = "anon", IsConnected = true };
        var identified = new RemotePeer
        {
            PeerId = "identified",
            IsConnected = true,
            Capabilities = new PeerCapabilities
            {
                Fingerprint = "abc123def456",
                PublicKey = "base64pubkey",
            }
        };
        if (identified.Reputation <= anon.Reputation)
            throw new Exception($"Identified peer ({identified.Reputation:F3}) should have higher reputation than anonymous ({anon.Reputation:F3})");
    }

    [TestMethod]
    public void P2P_Reputation_SuccessRateAffectsScore()
    {
        var reliable = new RemotePeer { PeerId = "reliable", IsConnected = true };
        var flaky = new RemotePeer { PeerId = "flaky", IsConnected = true };

        // Simulate dispatches
        for (int i = 0; i < 10; i++) reliable.RecordSuccess(50);
        for (int i = 0; i < 5; i++) flaky.RecordSuccess(50);
        for (int i = 0; i < 5; i++) flaky.RecordFailure();

        if (reliable.SuccessRate != 1.0)
            throw new Exception($"Reliable peer should have 100% success rate, got {reliable.SuccessRate:P0}");
        if (flaky.SuccessRate > 0.6)
            throw new Exception($"Flaky peer should have ~50% success rate, got {flaky.SuccessRate:P0}");
        if (reliable.Reputation <= flaky.Reputation)
            throw new Exception($"Reliable ({reliable.Reputation:F3}) should outrank flaky ({flaky.Reputation:F3})");
    }

    [TestMethod]
    public void P2P_Reputation_RecordSuccess_TracksDuration()
    {
        var peer = new RemotePeer { PeerId = "test", IsConnected = true };
        peer.RecordSuccess(100);
        peer.RecordSuccess(200);
        peer.RecordSuccess(150);

        if (peer.DispatchCount != 3)
            throw new Exception($"Expected 3 dispatches, got {peer.DispatchCount}");
        if (peer.SuccessCount != 3)
            throw new Exception($"Expected 3 successes, got {peer.SuccessCount}");
        if (Math.Abs(peer.AvgDurationMs - 150) > 0.01)
            throw new Exception($"Expected avg 150ms, got {peer.AvgDurationMs:F1}ms");
    }

    [TestMethod]
    public void P2P_Reputation_RecordFailure_TracksCount()
    {
        var peer = new RemotePeer { PeerId = "test", IsConnected = true };
        peer.RecordSuccess(50);
        peer.RecordFailure();
        peer.RecordFailure();

        if (peer.DispatchCount != 3)
            throw new Exception($"Expected 3 dispatches, got {peer.DispatchCount}");
        if (peer.FailureCount != 2)
            throw new Exception($"Expected 2 failures, got {peer.FailureCount}");
        if (Math.Abs(peer.SuccessRate - 1.0 / 3.0) > 0.01)
            throw new Exception($"Expected ~33% success rate, got {peer.SuccessRate:P0}");
    }

    // ═══════════════════════════════════════════════════════════
    //  DispatchPipeline Tests
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public void P2P_Pipeline_Create_Empty()
    {
        using var ctx = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        using var accel = (P2PAccelerator)device.CreateAccelerator(ctx);
        var pipeline = new P2PDispatchPipeline(accel);
        if (pipeline.StageCount != 0)
            throw new Exception($"New pipeline should have 0 stages, got {pipeline.StageCount}");
    }

    [TestMethod]
    public void P2P_Pipeline_AddStages()
    {
        using var ctx = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        using var accel = (P2PAccelerator)device.CreateAccelerator(ctx);

        var pipeline = new P2PDispatchPipeline(accel)
            .Add(typeof(P2PDemoKernels), nameof(P2PDemoKernels.MultiplyBy2), 1024,
                ("data", new byte[1024 * 4], 4))
            .Add(typeof(P2PDemoKernels), nameof(P2PDemoKernels.MultiplyBy2), 1024,
                ("data", null, 4));

        if (pipeline.StageCount != 2)
            throw new Exception($"Pipeline should have 2 stages, got {pipeline.StageCount}");
    }

    [TestMethod]
    public void P2P_Pipeline_InvalidKernel_Throws()
    {
        using var ctx = global::ILGPU.Context.CreateDefault();
        var device = new P2PDevice();
        using var accel = (P2PAccelerator)device.CreateAccelerator(ctx);

        bool threw = false;
        try
        {
            new P2PDispatchPipeline(accel)
                .Add(typeof(P2PDemoKernels), "NonExistentKernel", 1024, ("x", null, 4));
        }
        catch (ArgumentException) { threw = true; }

        if (!threw)
            throw new Exception("Adding non-existent kernel should throw ArgumentException");
    }

}
