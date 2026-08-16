using ILGPU;
using SpawnDev.SpawnJS;
using ILGPU.Runtime;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.RTC;
using SpawnDev.RTC.Desktop;
using SpawnDev.WebTorrent;

namespace SpawnDev.ILGPU.P2P;

/// <summary>
/// High-level facade for P2P distributed compute.
/// Simplifies the setup from ~20 lines to ~3 lines.
///
/// Usage — Coordinator (creates swarm):
///   var compute = await P2PCompute.CreateSwarmAsync(crypto, client, "My Inference");
///   Console.WriteLine(compute.JoinLink); // Share as QR code
///   // compute.Accelerator is ready for kernel dispatch
///
/// Usage — Worker (joins swarm):
///   var compute = await P2PCompute.JoinSwarmAsync(crypto, client, accelerator, magnetOrJoinLink);
///   // Automatically contributes compute power
///
/// Usage — Self-contained (both in one process, for testing):
///   var compute = await P2PCompute.CreateSelfTestAsync(crypto, client, context, accelerator);
///   // Full pipeline in-process
/// </summary>
public class P2PCompute : IAsyncDisposable
{
    /// <summary>
    /// Enables diagnostic Console.WriteLine output across the P2P stack
    /// (bridge / transport / extension / worker / compute facade).
    /// Default false — production code never builds the interpolated log
    /// strings unless this is set, matching the WebTorrentClient pattern.
    /// Toggle from a consumer's startup or set in unit tests.
    /// </summary>
    public static bool VerboseLogging { get; set; }

    /// <summary>The swarm coordinator.</summary>
    public P2PSwarmCoordinator Coordinator { get; }

    /// <summary>Default WebSocket tracker URL for peer discovery.</summary>
    public const string DefaultTrackerUrl = "wss://hub.spawndev.com:44365/announce";

    /// <summary>The P2P accelerator (coordinator side).</summary>
    public P2PAccelerator? Accelerator { get; }

    /// <summary>The fault-tolerant dispatcher.</summary>
    public P2PDispatcher? Dispatcher { get; }

    /// <summary>The transport layer.</summary>
    public P2PTransport? Transport { get; }

    /// <summary>The WebRTC bridge — auto-wires sd_compute to real peer connections.</summary>
    public P2PWebRtcBridge? Bridge { get; }

    /// <summary>The worker (worker side).</summary>
    public P2PWorker? Worker { get; }

    /// <summary>BEP 46 DHT state persistence - swarm state survives coordinator loss.</summary>
    public P2PStateManager? StateManager { get; private set; }

    /// <summary>The cryptographic identity.</summary>
    public SwarmIdentity Identity { get; }

    /// <summary>Magnet link for joining.</summary>
    public string? MagnetLink => Coordinator.MagnetLink;

    /// <summary>HTTP join link (QR-friendly).</summary>
    public string? JoinLink => Coordinator.JoinLink;

    /// <summary>Number of connected peers.</summary>
    public int PeerCount => Coordinator.PeerCount;

    /// <summary>Total TFLOPS across the swarm.</summary>
    public double TotalTflops => Coordinator.TotalTflops;

    /// <summary>This node's role.</summary>
    public P2PRole Role => Coordinator.Role;

    /// <summary>
    /// Dispatch a kernel to the swarm. Coordinator-side convenience method.
    ///
    /// Usage:
    ///   compute.DispatchToSwarm(typeof(MyKernels), "VectorAdd", 1024,
    ///       ("a", aData, 4), ("b", bData, 4), ("result", null, 4));
    /// </summary>
    public string DispatchToSwarm(Type kernelType, string methodName, long gridDimX,
        params (string bufferId, byte[]? data, int elementSize)[] buffers)
    {
        if (Accelerator == null)
            throw new InvalidOperationException("Not a coordinator node");

        var helper = Accelerator.CreateDispatcher(kernelType, methodName);
        return helper.Execute(gridDimX, buffers);
    }

    private readonly WebTorrentClient _client;
    private readonly global::ILGPU.Context? _context;

    private P2PCompute(
        WebTorrentClient client,
        SwarmIdentity identity,
        P2PSwarmCoordinator coordinator,
        P2PAccelerator? accelerator = null,
        P2PDispatcher? dispatcher = null,
        P2PTransport? transport = null,
        P2PWebRtcBridge? bridge = null,
        P2PWorker? worker = null,
        global::ILGPU.Context? context = null)
    {
        _client = client;
        _context = context;
        Identity = identity;
        Coordinator = coordinator;
        Accelerator = accelerator;
        Dispatcher = dispatcher;
        Transport = transport;
        Bridge = bridge;
        Worker = worker;
    }

    /// <summary>
    /// Create a new compute swarm. This node becomes the coordinator/owner.
    /// </summary>
    /// <param name="crypto">Crypto provider (DotNetCrypto for desktop, BrowserCrypto for browser).</param>
    /// <param name="client">WebTorrent client for P2P transport.</param>
    /// <param name="name">Human-readable swarm name.</param>
    /// <param name="joinLinkBaseUrl">Base URL for HTTP join links (your web app URL).</param>
    /// <param name="trackers">Tracker URLs for peer discovery. Defaults to hub.spawndev.com.</param>
    public static async Task<P2PCompute> CreateSwarmAsync(
        IPortableCrypto crypto,
        WebTorrentClient client,
        string name,
        string? joinLinkBaseUrl = null,
        string[]? trackers = null)
    {
        ConfigureHighThroughputSctp(client);
        var identity = await SwarmIdentity.CreateAsync(crypto, name + "-owner");
        var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(identity);

        // Create context + accelerator + dispatcher + transport BEFORE swarm
        // so we can register sd_compute extension factory before any peers connect
        var context = global::ILGPU.Context.CreateDefault();
        var accelerator = coordinator.CreateAccelerator(context);
        accelerator.Dispatcher = new P2PDispatcher(accelerator);
        var dispatcher = accelerator.Dispatcher;
        dispatcher.CoordinatorPublicKey = Convert.ToBase64String(identity.PublicKeySpki);
        var transport = new P2PTransport(client, coordinator, dispatcher);
        transport.SetCrypto(crypto);

        // Bridge for wiring sd_compute to peer connections
        var bridge = new P2PWebRtcBridge(transport);

        // Build the BEP 46 state channel BEFORE the sd_compute factory runs so every
        // new wire's extended handshake carries our DHT pubkey. Workers need this key
        // to call StateManager.SubscribeAsync - without it the wire fix Riker shipped
        // in rc.26 can't actually propagate state from coordinator to workers.
        P2PStateManager? stateManager = null;
        AgentChannel? agentChannel = null;
        byte[]? coordDhtPubKey = null;
        if (client.Dht != null)
        {
            var dhtSigner = new Ed25519Signer(crypto);
            await dhtSigner.ImportKeyAsync(
                await crypto.ExportPublicKeySpki(identity.Key),
                await crypto.ExportPrivateKeyPkcs8(identity.Key));
            agentChannel = new AgentChannel(client.Dht, dhtSigner);
            coordDhtPubKey = agentChannel.PublicKey; // raw 32-byte Ed25519
            stateManager = new P2PStateManager(agentChannel, coordinator);
        }

        // Register sd_compute extension factory BEFORE creating swarm
        // This ensures the extension is in the BEP 10 handshake for every peer.
        // ProcessHandshakeData fires naturally, registering the peer in transport
        // and triggering capability exchange.
        client.UseExtension((wire) =>
        {
            var ext = new SdComputeExtension(transport);
            // Advertise our DHT identity so workers can subscribe to our state channel.
            // Must be set BEFORE SetWire, which is where the handshake dict is populated.
            if (coordDhtPubKey != null) ext.DhtPublicKey = coordDhtPubKey;
            ext.SetWire(wire);
            return ext;
        });

        // Auto-detect join link URL: explicit > browser location > null (desktop)
        if (joinLinkBaseUrl != null)
            coordinator.JoinLinkBaseUrl = joinLinkBaseUrl;
        else if (OperatingSystem.IsBrowser())
            coordinator.JoinLinkBaseUrl = GetBrowserBaseUrl();

        // NOW create the swarm — peers that connect will get sd_compute in handshake
        await coordinator.CreateSwarmAsync(name, trackers);

        // Wire coordinator messages to transport — auto-sign authority messages
        coordinator.OnSendMessage += async (peerId, msg) =>
        {
            await transport.SendSignedMessageAsync(peerId, msg);
        };

        // Wire dispatcher dispatch messages to transport — signed like all authority messages
        dispatcher.OnSendMessage += async (peerId, msg) =>
        {
            await transport.SendSignedMessageAsync(peerId, msg);
        };

        // Wire dispatcher input-buffer sends to transport so DispatchAsync's (bufferId, data, _)
        // tuples actually transmit their data to the selected peer before the dispatch fires.
        dispatcher.OnSendBuffer += async (peerId, bufferId, data) =>
        {
            await transport.SendBufferAsync(peerId, bufferId, data);
        };

        // Wire bridge peer discovery to coordinator BEFORE AttachToSwarm so the
        // subscriber is in place by the time torrent.OnWire fires.
        bridge.OnComputePeerCapabilities += (peerId, caps) =>
        {
            if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PCompute] Bridge OnComputePeerCapabilities: peerId={peerId}, caps={caps != null}");
            caps ??= new PeerCapabilities { PreferredBackend = "remote" };
            var result = coordinator.HandlePeerConnected(peerId, caps);
            if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PCompute] HandlePeerConnected result: {result}, PeerCount: {coordinator.PeerCount}");
        };

        // Attach bridge to torrent for peer connection events
        if (coordinator.Swarm != null)
            bridge.AttachToSwarm(coordinator.Swarm);

        var compute = new P2PCompute(client, identity, coordinator, accelerator, dispatcher, transport, bridge, context: context);

        // Attach the pre-built StateManager (created before the extension factory so its
        // public key could ride the first extended handshake) and wire auto-publish.
        if (stateManager != null)
        {
            compute.StateManager = stateManager;

            coordinator.OnPeerJoined += async (_) =>
            {
                if (compute.StateManager != null)
                    await compute.StateManager.PublishStateAsync();
            };
            coordinator.OnPeerLeft += async (_) =>
            {
                if (compute.StateManager != null)
                    await compute.StateManager.PublishStateAsync();
            };
        }

        return compute;
    }

    /// <summary>
    /// Join an existing compute swarm as a worker.
    /// </summary>
    /// <param name="crypto">Crypto provider.</param>
    /// <param name="client">WebTorrent client.</param>
    /// <param name="accelerator">Local accelerator for kernel execution.</param>
    /// <param name="magnetOrJoinLink">Magnet link or HTTP join link.</param>
    public static async Task<P2PCompute> JoinSwarmAsync(
        IPortableCrypto crypto,
        WebTorrentClient client,
        Accelerator accelerator,
        string magnetOrJoinLink)
    {
        ConfigureHighThroughputSctp(client);
        var identity = await SwarmIdentity.CreateAsync(crypto, "worker");
        var coordinator = new P2PSwarmCoordinator(client);
        coordinator.SetIdentity(identity);

        // Create transport + worker BEFORE joining so we can register sd_compute
        var p2pAccel = coordinator.CreateAccelerator(accelerator.Context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        var transport = new P2PTransport(client, coordinator, dispatcher);
        transport.SetCrypto(crypto);
        var worker = new P2PWorker(transport);
        worker.Initialize(accelerator.Context, accelerator);
        transport.SetWorker(worker);

        // Build the worker's BEP 46 state channel BEFORE the sd_compute factory runs so
        // the per-wire handshake handler can subscribe the instant a coordinator
        // advertises its DHT pubkey. Worker uses a fresh Ed25519 key - it's a subscriber,
        // not a publisher, so its own channel is never read. Callers that also want the
        // worker to publish state would override DhtPublicKey on the extension.
        P2PStateManager? workerStateManager = null;
        if (client.Dht != null)
        {
            var workerDhtSigner = new Ed25519Signer(crypto);
            await workerDhtSigner.GenerateKeyAsync();
            var workerChannel = new AgentChannel(client.Dht, workerDhtSigner);
            workerStateManager = new P2PStateManager(workerChannel, coordinator);
        }

        // Register sd_compute extension factory BEFORE joining
        client.UseExtension((wire) =>
        {
            var ext = new SdComputeExtension(transport);
            // Subscribe to the coordinator's DHT channel the moment the extended
            // handshake arrives with its pubkey. SubscribeAsync is fire-and-forget
            // here because the DHT subscription is a polling loop under the hood -
            // we don't need to await it inline.
            if (workerStateManager != null)
            {
                ext.OnRemoteDhtPublicKeyReceived += (remotePubKey) =>
                {
                    _ = SubscribeToRemoteDhtAsync(workerStateManager, remotePubKey);
                };
            }
            ext.SetWire(wire);
            return ext;
        });

        // Extract magnet from join link if needed
        string magnet;
        if (magnetOrJoinLink.StartsWith("magnet:"))
        {
            magnet = magnetOrJoinLink;
        }
        else
        {
            var (hash, name) = ParseJoinLink(magnetOrJoinLink);
            magnet = hash != null
                ? BuildMagnetFromHash(hash, name)
                : magnetOrJoinLink;
        }

        // NOW join — peers that connect will get sd_compute in handshake
        await coordinator.JoinSwarmAsync(magnet);

        // Bridge WebRTC peer connections to sd_compute extension
        var bridge = new P2PWebRtcBridge(transport);

        // Wire bridge peer discovery to coordinator BEFORE AttachToSwarm so the
        // subscriber is in place by the time torrent.OnWire fires. Otherwise a
        // peer that connects fast enough will deliver its CapabilityResponse
        // through bridge.OnComputePeerCapabilities with no listener attached yet.
        bridge.OnComputePeerCapabilities += (peerId, caps) =>
        {
            if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PCompute Join] Bridge OnComputePeerCapabilities: peerId={peerId}, caps={caps != null}");
            caps ??= worker.BuildCapabilities(peerId);
            var result = coordinator.HandlePeerConnected(peerId, caps);
            if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PCompute Join] HandlePeerConnected result: {result}, PeerCount: {coordinator.PeerCount}");
        };

        if (coordinator.Swarm != null)
            bridge.AttachToSwarm(coordinator.Swarm);

        var compute = new P2PCompute(client, identity, coordinator, p2pAccel, dispatcher, transport, bridge, worker);

        // Attach the pre-built worker StateManager (constructed before the extension
        // factory so the per-wire handshake handler could close over it) and wire
        // the diagnostic loggers that used to be gated on an unreachable code path.
        if (workerStateManager != null)
        {
            compute.StateManager = workerStateManager;

            compute.StateManager.OnStateUpdated += (state) =>
            {
                if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PCompute Join] DHT state update: coordinator={state.CoordinatorPeerId}, " +
                    $"peers={state.PeerCount}, TFLOPS={state.TotalTflops:F1}");
            };

            compute.StateManager.OnCoordinatorAnnounced += (announcement) =>
            {
                if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PCompute Join] New coordinator announced: {announcement.CoordinatorPeerId}");
                worker.NotifyCoordinatorChanged();
            };
        }

        return compute;
    }

    /// <summary>
    /// Create a self-contained test setup (coordinator + worker in one process).
    /// </summary>
    public static async Task<P2PCompute> CreateSelfTestAsync(
        IPortableCrypto crypto,
        WebTorrentClient client,
        global::ILGPU.Context context,
        Accelerator accelerator)
    {
        var compute = await CreateSwarmAsync(crypto, client, "SelfTest");

        // Create worker in same process
        var worker = new P2PWorker(compute.Transport!);
        worker.Initialize(context, accelerator);
        compute.Transport!.SetWorker(worker);

        // Connect worker as peer
        var caps = worker.BuildCapabilities("self-worker");
        compute.Coordinator.HandlePeerConnected("self-worker", caps);
        compute.Accelerator!.AddPeer(new RemotePeer
        {
            PeerId = "self-worker",
            IsConnected = true,
            Capabilities = caps,
        });

        return new P2PCompute(client, compute.Identity, compute.Coordinator,
            compute.Accelerator, compute.Dispatcher, compute.Transport, compute.Bridge, worker, context);
    }

    /// <summary>
    /// Get the current browser URL (origin + path) for auto-detecting join link base.
    /// Returns null on desktop.
    /// </summary>
    private static string? GetBrowserBaseUrl()
    {
        try
        {
            if (!OperatingSystem.IsBrowser()) return null;
            // In Blazor WASM: window.location.origin + window.location.pathname
            // Access via SpawnDev.SpawnJS if available, fallback to null
            var origin = SpawnJSRuntime.Instance?.Get<string>("window.location.origin");
            var path = SpawnJSRuntime.Instance?.Get<string>("window.location.pathname");
            if (!string.IsNullOrEmpty(origin))
                return origin + (path ?? "");
        }
        catch (Exception ex)
        {
            if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PCompute] GetBrowserBaseUrl failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Parse a join link URL to extract the compute hash and swarm name.
    /// Returns (hash, name) or (null, null) if not a join link.
    /// </summary>
    public static (string? computeHash, string? swarmName) ParseJoinLink(string url)
    {
        try
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var hash = query["compute"];
            var name = query["n"];
            return (hash, name);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Build a magnet link from a compute hash and default tracker.
    /// Used by workers joining via HTTP join link.
    /// </summary>
    public static string BuildMagnetFromHash(string computeHash, string? swarmName = null,
        string tracker = "wss://hub.spawndev.com:44365/announce")
    {
        var dn = !string.IsNullOrEmpty(swarmName)
            ? $"&dn={Uri.EscapeDataString(swarmName)}" : "";
        return $"magnet:?xt=urn:btih:{computeHash}{dn}&tr={Uri.EscapeDataString(tracker)}";
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Worker != null) await Worker.DisposeAsync();
        if (Bridge != null) await Bridge.DisposeAsync();
        if (Transport != null) await Transport.DisposeAsync();
        Dispatcher?.Dispose();
        Accelerator?.Dispose();
        await Coordinator.DisposeAsync();
        await Identity.DisposeAsync();
        _context?.Dispose();
    }

    /// <summary>
    /// Overrides the client's <see cref="WebTorrentClient.PeerFactory"/> so every peer
    /// that gets created raises SCTP burst knobs on its SIPSorcery data channel as soon
    /// as the channel opens. Default MAX_BURST=4 / BURST_PERIOD=50ms (RFC 4960 §7.2.2)
    /// caps WebRTC-loopback throughput at MAX_BURST * MTU / RTT ≈ 180 KB/s, which isn't
    /// enough for multi-MB tensor transfers. Tuning to MaxBurst=32 / BurstPeriod=10ms
    /// lifts the ceiling to ≈ 1.5 MB/s at the same RTT — fine for LAN / localhost where
    /// the spec's bursty-sender prevention isn't needed. Browser peers are a no-op:
    /// libwebrtc doesn't expose per-association SCTP tunables and has better defaults.
    /// </summary>
    /// <summary>
    /// Subscribe the worker's state channel to a coordinator-advertised DHT pubkey.
    /// Fire-and-forget from the sd_compute handshake handler - AgentChannel.SubscribeAsync
    /// runs a background poll loop so the Task it returns never completes under normal
    /// operation. Exceptions are logged and swallowed so a single bad pubkey doesn't
    /// crash the handshake path.
    /// </summary>
    private static async Task SubscribeToRemoteDhtAsync(P2PStateManager stateManager, byte[] remotePubKey)
    {
        try
        {
            if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PCompute] Subscribing worker state channel to coord DHT pubkey " +
                $"{Convert.ToHexString(remotePubKey)[..16].ToLowerInvariant()}...");
            await stateManager.SubscribeAsync(remotePubKey);
        }
        catch (Exception ex)
        {
            if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PCompute] SubscribeAsync failed: {ex.Message}");
        }
    }

    private static void ConfigureHighThroughputSctp(WebTorrentClient client)
    {
        var previousFactory = client.PeerFactory;
        client.PeerFactory = (initiator) =>
        {
            var peer = previousFactory != null
                ? previousFactory(initiator)
                : new RtcPeer(initiator, client.IceServers, trickle: false);

            if (peer is RtcPeer rtc)
            {
                peer.OnConnect += () =>
                {
                    // Desktop path: reach SIPSorcery's SctpAssociation and raise the burst knobs.
                    // Browser path: PeerConnection is a BrowserRTCPeerConnection; no-op.
                    if (rtc.PeerConnection is DesktopRTCPeerConnection dpc)
                    {
                        try
                        {
                            var sctp = dpc.NativeConnection?.sctp;
                            if (sctp?.RTCSctpAssociation != null)
                            {
                                sctp.RTCSctpAssociation.MaxBurst = 32;
                                sctp.RTCSctpAssociation.BurstPeriodMilliseconds = 10;
                            }
                        }
                        catch
                        {
                            // If the SCTP association isn't reachable (channel closed mid-race,
                            // future SIPSorcery API drift, etc.) leave RFC defaults in place.
                        }
                    }
                };
            }

            return peer;
        };
    }
}
