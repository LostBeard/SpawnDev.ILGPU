using System.Reflection;
using ILGPU;
using ILGPU.Runtime.CPU;
using SpawnDev.ILGPU.P2P;
using SpawnDev.UnitTesting;
using SpawnDev.WebTorrent;

namespace SpawnDev.ILGPU.DemoConsole.P2PTests;

/// <summary>
/// BEP 46 handshake-plumbing unit tests for <see cref="SdComputeExtension"/>.
///
/// Validates the wire-format half of the coordinator-to-worker DHT subscription
/// chain that Riker's <c>SpawnDev.WebTorrent 3.1.3-rc.26</c> put/get fix makes
/// functional end-to-end:
///   - Coordinator advertises its 32-byte Ed25519 DHT pubkey (same key used
///     by its <see cref="P2PStateManager"/>) in the sd_compute BEP 10 extended
///     handshake dict under <c>sd_compute_dht_pubkey</c>, hex-encoded.
///   - Worker extracts the pubkey from the handshake into
///     <see cref="SdComputeExtension.RemoteDhtPublicKey"/> and fires
///     <see cref="SdComputeExtension.OnRemoteDhtPublicKeyReceived"/>, which is
///     the hook P2PCompute.JoinSwarmAsync uses to drive
///     <see cref="P2PStateManager.SubscribeAsync"/>.
///
/// Pure in-process validation for the wire-format cases. The full coord-to-worker
/// state propagation test (<see cref="BEP46_CoordinatorStateReachesWorkerViaRealDht"/>)
/// uses two loopback <see cref="DhtDiscovery"/> instances bound to distinct ports
/// via <c>WebTorrentClient.EnsureDhtAsync</c> (shipped in SpawnDev.WebTorrent
/// 3.1.3-rc.28 on Geordi's request) and exercises the full BEP 44 put + get
/// wire round-trip plus Ed25519 verify.
/// </summary>
public class Bep46PropagationTests
{
    private readonly SpawnDev.SpawnJS.Cryptography.IPortableCrypto _crypto;

    public Bep46PropagationTests(SpawnDev.SpawnJS.Cryptography.IPortableCrypto crypto) => _crypto = crypto;

    private const int DiscoveryTimeoutMs = 60000;

    /// <summary>
    /// Tracker list - mirrors RealWebRtcPipelineTests.DispatchTrackers: prefer local,
    /// fall back to public. Kept local to this class so changes don't cross-contaminate.
    /// </summary>
    private static string[] Trackers => LocalTrackerFixture.IsAvailable
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

    [TestMethod]
    public Task SdCompute_Handshake_AdvertisesDhtPubkey_WhenSet()
    {
        // Arrange: an sd_compute extension with a real 32-byte raw Ed25519 key.
        var transport = MakeDormantTransport();
        var ext = new SdComputeExtension(transport);
        var pubKey = MakeRandom32();
        ext.DhtPublicKey = pubKey;

        // Act: SetWire is the call that populates the extended-handshake dict.
        var wire = new Wire();
        ext.SetWire(wire);

        // Assert: dict entry is present, hex-encoded, lowercase, 64 chars.
        if (!wire.ExtendedHandshake.TryGetValue("sd_compute_dht_pubkey", out var advertised))
            throw new Exception(
                "sd_compute_dht_pubkey missing from ExtendedHandshake dict after SetWire. " +
                "Coord's DhtPublicKey never reaches the wire, workers cannot subscribe.");
        if (advertised is not string hex)
            throw new Exception($"Expected string for sd_compute_dht_pubkey, got {advertised?.GetType().Name}");
        if (hex.Length != 64)
            throw new Exception($"Expected 64 hex chars, got {hex.Length}: {hex}");
        if (hex != Convert.ToHexString(pubKey).ToLowerInvariant())
            throw new Exception($"Hex mismatch. Expected {Convert.ToHexString(pubKey).ToLowerInvariant()}, got {hex}");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task SdCompute_Handshake_DoesNotAdvertisePubkey_WhenUnset()
    {
        // Workers and other non-publisher peers leave DhtPublicKey null. The
        // handshake must not carry a phantom entry that would have subscribers
        // subscribing to garbage.
        var transport = MakeDormantTransport();
        var ext = new SdComputeExtension(transport);
        // DhtPublicKey intentionally not set (null)
        var wire = new Wire();
        ext.SetWire(wire);
        if (wire.ExtendedHandshake.ContainsKey("sd_compute_dht_pubkey"))
            throw new Exception(
                "sd_compute_dht_pubkey present when DhtPublicKey was null. " +
                "Workers would advertise their fresh random key to the coord for no reason.");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task SdCompute_Handshake_RejectsWrongSizeKey()
    {
        // Invariant: only 32-byte raw Ed25519 keys are valid DHT identities.
        // An attempt to advertise a 31-byte or 64-byte "key" indicates a caller
        // passing the wrong thing (e.g. SPKI-format 44-byte or full PKCS#8) -
        // must not be put on the wire as if it were the raw 32-byte form.
        var transport = MakeDormantTransport();
        var ext = new SdComputeExtension(transport);
        ext.DhtPublicKey = new byte[44]; // SPKI length, NOT raw - bug if advertised
        var wire = new Wire();
        ext.SetWire(wire);
        if (wire.ExtendedHandshake.ContainsKey("sd_compute_dht_pubkey"))
            throw new Exception(
                "sd_compute_dht_pubkey advertised for a 44-byte key. The wire contract " +
                "requires raw 32-byte Ed25519 pubkeys; advertising other sizes causes " +
                "workers to subscribe with the wrong format and silently fail.");
        return Task.CompletedTask;
    }

    [TestMethod]
    public async Task SdCompute_Handshake_ReceivesRemoteDhtPubkey_FiresEvent()
    {
        // Simulate the inbound side: the remote peer advertised a hex pubkey.
        // Our extension should extract it into RemoteDhtPublicKey and fire
        // OnRemoteDhtPublicKeyReceived with the raw 32 bytes.
        var transport = MakeDormantTransport();
        var ext = new SdComputeExtension(transport);
        ext.SetPeerId("test-remote-peer");
        var wire = new Wire();
        ext.SetWire(wire);

        var expected = MakeRandom32();
        var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        ext.OnRemoteDhtPublicKeyReceived += (bytes) => received.TrySetResult(bytes);

        // Remote handshake dict mirrors what the sender writes on its end.
        var remoteHandshake = new Dictionary<string, object>
        {
            ["sd_compute_version"] = P2PProtocol.Version,
            ["sd_compute_dht_pubkey"] = Convert.ToHexString(expected).ToLowerInvariant(),
        };
        ext.OnExtendedHandshake(remoteHandshake);

        var completed = await Task.WhenAny(received.Task, Task.Delay(1000));
        if (completed != received.Task)
            throw new Exception("OnRemoteDhtPublicKeyReceived never fired within 1s of handshake.");
        var gotKey = await received.Task;
        if (!gotKey.SequenceEqual(expected))
            throw new Exception(
                $"Event fired with wrong key. Expected {Convert.ToHexString(expected)}, " +
                $"got {Convert.ToHexString(gotKey)}");

        if (ext.RemoteDhtPublicKey == null || !ext.RemoteDhtPublicKey.SequenceEqual(expected))
            throw new Exception("RemoteDhtPublicKey property not populated or mismatched.");
    }

    [TestMethod]
    public Task SdCompute_Handshake_IgnoresMalformedPubkey()
    {
        // Defensive: a remote peer sending garbage under sd_compute_dht_pubkey
        // must not be able to make us call SubscribeAsync with nonsense.
        var transport = MakeDormantTransport();
        var ext = new SdComputeExtension(transport);
        ext.SetPeerId("evil-peer");
        var wire = new Wire();
        ext.SetWire(wire);

        bool eventFired = false;
        ext.OnRemoteDhtPublicKeyReceived += (_) => eventFired = true;

        // Too short (not 64 chars of hex), too long, and non-hex - all should be dropped.
        foreach (var bogus in new object[]
        {
            "not-hex-at-all",              // wrong format
            "ab",                           // too short
            new string('f', 128),          // too long
            Convert.ToHexString(new byte[16]).ToLowerInvariant(), // 32 hex chars => 16 bytes
        })
        {
            ext.OnExtendedHandshake(new Dictionary<string, object>
            {
                ["sd_compute_dht_pubkey"] = bogus,
            });
        }

        if (eventFired)
            throw new Exception("OnRemoteDhtPublicKeyReceived fired for a malformed pubkey - attacker could induce garbage SubscribeAsync calls.");
        if (ext.RemoteDhtPublicKey != null)
            throw new Exception("RemoteDhtPublicKey populated for malformed input.");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task SdCompute_Handshake_MissingDhtPubkey_LeavesRemoteNull()
    {
        // Normal worker-to-coord case: worker's handshake has no sd_compute_dht_pubkey.
        // Coord's extension must treat the absence as "no DHT identity advertised"
        // and leave RemoteDhtPublicKey null without throwing.
        var transport = MakeDormantTransport();
        var ext = new SdComputeExtension(transport);
        ext.SetPeerId("worker-peer");
        var wire = new Wire();
        ext.SetWire(wire);

        var remoteHandshake = new Dictionary<string, object>
        {
            ["sd_compute_version"] = P2PProtocol.Version,
            // no sd_compute_dht_pubkey - worker doesn't have a publishable DHT identity
        };
        ext.OnExtendedHandshake(remoteHandshake);

        if (ext.RemoteDhtPublicKey != null)
            throw new Exception("RemoteDhtPublicKey populated despite handshake having no entry.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// End-to-end: real coord + real worker + real tracker + real WebRTC + real DHT.
    /// Coord publishes <see cref="SwarmState"/> when a peer joins. Worker's subscription
    /// (wired automatically when the sd_compute handshake arrives) polls the DHT for
    /// that key. BEP 44 put -> BEP 44 get -> Ed25519 verify (the exact path Riker's
    /// rc.26 fix unblocked) delivers the signed value. Worker's OnStateUpdated fires
    /// with a deserialized SwarmState carrying the coord's peer id + non-zero peer count.
    /// </summary>
    [TestMethod(Timeout = 300000, RetryCount = 2)]
    public async Task BEP46_CoordinatorStateReachesWorkerViaRealDht()
    {
        if (OperatingSystem.IsBrowser())
            throw new UnsupportedTestException("DHT requires UDP - desktop only.");

        P2PKernelSerializer.RegisterKernelType(typeof(P2PTestKernels));

        await using var coordClient = new SpawnDev.WebTorrent.WebTorrentClient();
        await using var workerClient = new SpawnDev.WebTorrent.WebTorrentClient();

        // Distinct loopback ports via rc.28's EnsureDhtAsync. Picked high + adjacent so
        // a second concurrent test-run on the same box can offset trivially if needed.
        await coordClient.EnsureDhtAsync(new DhtOptions { Port = 56830 });
        await workerClient.EnsureDhtAsync(new DhtOptions { Port = 56831 });
        if (coordClient.Dht == null || workerClient.Dht == null)
            throw new Exception(
                "EnsureDhtAsync returned without initializing Dht - library regression or browser fallback.");

        // Cross-bootstrap the two loopback DHTs so they see each other for BEP 44 put/get.
        // Mirrors Riker's Bep46LoopbackTests pattern - FindNodeAsync teaches each side
        // about the other via BEP 5 add-sender logic on the response.
        var coordEp = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 56830);
        var workerEp = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 56831);
        await coordClient.Dht.FindNodeAsync(workerEp);
        await workerClient.Dht.FindNodeAsync(coordEp);
        await Task.Delay(500);

        var bootDeadline = DateTime.UtcNow.AddSeconds(10);
        while ((coordClient.Dht.NodeCount == 0 || workerClient.Dht.NodeCount == 0)
               && DateTime.UtcNow < bootDeadline)
            await Task.Delay(200);
        if (coordClient.Dht.NodeCount == 0 || workerClient.Dht.NodeCount == 0)
            throw new Exception(
                $"DHT loopback bootstrap failed after 10s: coord.NodeCount={coordClient.Dht.NodeCount}, " +
                $"worker.NodeCount={workerClient.Dht.NodeCount}.");

        // Stand up the compute swarm. CreateSwarmAsync sees client.Dht != null and wires
        // P2PStateManager; factory will advertise its DHT pubkey on every sd_compute handshake.
        await using var coordinator = await P2PCompute.CreateSwarmAsync(
            _crypto, coordClient, "bep46-propagation-test", trackers: Trackers);
        if (coordinator.StateManager == null)
            throw new Exception("Coordinator StateManager null - DHT wiring never fired.");

        using var workerContext = Context.Create(b => b.CPU());
        using var workerAccelerator = workerContext.CreateCPUAccelerator(0);

        await using var worker = await P2PCompute.JoinSwarmAsync(
            _crypto, workerClient, workerAccelerator, coordinator.MagnetLink!);
        if (worker.StateManager == null)
            throw new Exception("Worker StateManager null - DHT wiring never fired.");

        // Hook state-update capture BEFORE peer discovery completes - the first published
        // state is triggered by coord's OnPeerJoined event the moment the WebRTC peer
        // connects, and we don't want to race that.
        var stateReceived = new TaskCompletionSource<SwarmState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allUpdates = new System.Collections.Generic.List<SwarmState>();
        worker.StateManager.OnStateUpdated += (state) =>
        {
            lock (allUpdates) allUpdates.Add(state);
            stateReceived.TrySetResult(state);
        };

        // The per-wire auto-subscribe from sd_compute handshake uses the 30s default poll
        // interval - too slow for a test. Fire a parallel subscription with a 1s poll on
        // the same coord pubkey; whichever GET lands first delivers the update. Safe to
        // have two subscriptions on the same key.
        var coordDhtPubKey = coordinator.StateManager.PublicKey;
        _ = Task.Run(() => worker.StateManager.SubscribeAsync(coordDhtPubKey, pollIntervalMs: 1000));

        await WaitForPeersAsync(coordinator, worker);

        // Coord's OnPeerJoined already fired by this point, which called PublishStateAsync.
        // Give the subscription loop up to 45s to observe it - with a 1s poll the first
        // GET should hit within ~2s, but we allow for DHT bootstrap + tracker signaling.
        var completedTask = await Task.WhenAny(stateReceived.Task, Task.Delay(45000));
        if (completedTask != stateReceived.Task)
        {
            throw new Exception(
                $"Worker did not receive any BEP 46 state update within 45s. " +
                $"coord.PeerCount={coordinator.PeerCount}, worker.PeerCount={worker.PeerCount}, " +
                $"coord.StateManager.Sequence={coordinator.StateManager.Sequence}, " +
                $"coord.Dht.NodeCount={coordClient.Dht.NodeCount}, " +
                $"worker.Dht.NodeCount={workerClient.Dht.NodeCount}, " +
                $"updates captured={allUpdates.Count}.");
        }

        var state = await stateReceived.Task;

        // The receipt itself is the strongest assertion: OnStateUpdated only fires after
        // BEP 46 GET -> Ed25519 verify -> JSON deserialize all succeed end-to-end. That
        // path was silently broken in the library before SpawnDev.WebTorrent rc.26.
        if (state.PeerCount < 1)
            throw new Exception(
                $"Received SwarmState.PeerCount={state.PeerCount} (<1). " +
                "The update that triggered publication was a peer-joined event, so the " +
                "state should show the joining peer.");
        if (coordinator.StateManager.Sequence < 1)
            throw new Exception(
                $"coord.StateManager.Sequence={coordinator.StateManager.Sequence} (<1). " +
                "PublishStateAsync should have incremented it.");

        // CoordinatorPeerId should now be populated (fix landed in P2PStateManager after
        // BEP 46 e2e shipped): when self-is-coordinator, BuildCurrentState substitutes
        // the SwarmIdentity fingerprint for the previously-null value.
        if (string.IsNullOrEmpty(state.CoordinatorPeerId))
            throw new Exception(
                "Received SwarmState.CoordinatorPeerId empty - coord's BuildCurrentState " +
                "should substitute Identity.Fingerprint when self-is-coordinator.");
        var expectedFingerprint = coordinator.Identity?.Fingerprint;
        if (!string.IsNullOrEmpty(expectedFingerprint) && state.CoordinatorPeerId != expectedFingerprint)
            throw new Exception(
                $"CoordinatorPeerId mismatch. Expected fingerprint '{expectedFingerprint}', " +
                $"got '{state.CoordinatorPeerId}'.");

        Console.WriteLine(
            $"[BEP46 e2e] PASS - received SwarmState via real DHT: " +
            $"coord='{state.CoordinatorPeerId[..Math.Min(16, state.CoordinatorPeerId.Length)]}...', " +
            $"peers={state.PeerCount}, updates={allUpdates.Count}, " +
            $"coord.Sequence={coordinator.StateManager.Sequence}");
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
            throw new Exception(
                $"Peer discovery timed out after {DiscoveryTimeoutMs}ms. " +
                $"coord.PeerCount={coordinator.PeerCount}, worker.PeerCount={worker.PeerCount}");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers

    /// <summary>
    /// Build a dormant <see cref="P2PTransport"/> that has no peers and no
    /// working WebTorrent client - enough to construct the extension under test
    /// without triggering background work. Uses reflection because the transport
    /// requires a real WebTorrentClient in its public constructor; the tests
    /// never actually send or receive, so the client is a stub.
    /// </summary>
    private static P2PTransport MakeDormantTransport()
    {
        var client = new SpawnDev.WebTorrent.WebTorrentClient();
        var coordinator = new P2PSwarmCoordinator(client);
        using var context = global::ILGPU.Context.Create(b => b.CPU());
        var accelerator = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(accelerator);
        return new P2PTransport(client, coordinator, dispatcher);
    }

    private static byte[] MakeRandom32()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
