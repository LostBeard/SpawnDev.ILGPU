using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU.P2P;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.DemoConsole.P2PTests;

/// <summary>
/// Phase 4: Security, RBAC, Identity, and Policy tests.
/// Proves Ed25519 signatures, coordinator authority verification,
/// kick/block enforcement, key registry, and join policies all work correctly.
/// Uses real crypto (IPortableCrypto via DI) - no mocks.
/// </summary>
public class SecurityTests
{
    private readonly IPortableCrypto _crypto;

    public SecurityTests(IPortableCrypto crypto)
    {
        _crypto = crypto;
    }

    /// <summary>
    /// Worker verifies that a dispatch comes from an authorized coordinator
    /// via Ed25519 signature on the dispatch request.
    /// </summary>
    [TestMethod]
    public async Task Security_SignedDispatch_Verified()
    {
        P2PKernelSerializer.RegisterKernelType(typeof(P2PTestKernels));

        var ownerIdentity = await SwarmIdentity.CreateAsync(_crypto, "owner");
        var registry = new KeyRegistry();
        registry.AddKey(ownerIdentity.PublicKeySpki, SwarmRole.Owner, "owner");
        await registry.SignAsync(ownerIdentity);

        using var context = Context.Create(builder => builder.CPU());
        using var accelerator = context.CreateCPUAccelerator(0);
        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        var transport = new P2PTransport(
            new SpawnDev.WebTorrent.WebTorrentClient(), coordinator, dispatcher);
        var worker = new P2PWorker(transport);
        worker.Initialize(context, accelerator);
        worker.SetKeyRegistry(registry);

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.Identity))!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 64);
        request.CoordinatorPublicKey = Convert.ToBase64String(ownerIdentity.PublicKeySpki);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "in", Length = 64, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 2, BufferId = "out", Length = 64, ElementSize = 4 },
        };

        var inputBytes = new byte[64 * 4];
        new Random(42).NextBytes(inputBytes);
        worker.ReceiveBuffer("in", inputBytes);

        bool completed = false;
        bool success = false;
        worker.OnKernelCompleted += (id, s, ms) => { completed = true; success = s; };

        await worker.HandleDispatchAsync("coordinator", request);

        if (!completed) throw new Exception("Dispatch should complete");
        if (!success) throw new Exception("Dispatch from authorized coordinator should succeed");
    }

    /// <summary>
    /// Worker rejects a dispatch from an unauthorized peer (no public key provided).
    /// </summary>
    [TestMethod]
    public async Task Security_UnsignedDispatch_Rejected()
    {
        P2PKernelSerializer.RegisterKernelType(typeof(P2PTestKernels));

        var ownerIdentity = await SwarmIdentity.CreateAsync(_crypto, "owner");
        var registry = new KeyRegistry();
        registry.AddKey(ownerIdentity.PublicKeySpki, SwarmRole.Owner, "owner");
        await registry.SignAsync(ownerIdentity);

        using var context = Context.Create(builder => builder.CPU());
        using var accelerator = context.CreateCPUAccelerator(0);
        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        var transport = new P2PTransport(
            new SpawnDev.WebTorrent.WebTorrentClient(), coordinator, dispatcher);
        var worker = new P2PWorker(transport);
        worker.Initialize(context, accelerator);
        worker.SetKeyRegistry(registry);

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.Identity))!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 64);
        // Intentionally NOT setting CoordinatorPublicKey
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "in", Length = 64, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 2, BufferId = "out", Length = 64, ElementSize = 4 },
        };

        worker.ReceiveBuffer("in", new byte[64 * 4]);

        bool completed = false;
        bool success = false;
        worker.OnKernelCompleted += (id, s, ms) => { completed = true; success = s; };

        await worker.HandleDispatchAsync("attacker", request);

        if (!completed) throw new Exception("Dispatch event should fire");
        if (success)
            throw new Exception("Dispatch without coordinator public key should be REJECTED when registry has entries");
    }

    /// <summary>
    /// A revoked key cannot dispatch work - the worker checks the revocation list.
    /// </summary>
    [TestMethod]
    public async Task Security_RevokedKey_Rejected()
    {
        P2PKernelSerializer.RegisterKernelType(typeof(P2PTestKernels));

        var ownerIdentity = await SwarmIdentity.CreateAsync(_crypto, "owner");
        var coordIdentity = await SwarmIdentity.CreateAsync(_crypto, "coordinator");

        var registry = new KeyRegistry();
        registry.AddKey(ownerIdentity.PublicKeySpki, SwarmRole.Owner, "owner");
        registry.AddKey(coordIdentity.PublicKeySpki, SwarmRole.Coordinator, "coordinator");
        registry.RevokeKey(coordIdentity.PublicKeySpki, "compromised");
        await registry.SignAsync(ownerIdentity);

        using var context = Context.Create(builder => builder.CPU());
        using var accelerator = context.CreateCPUAccelerator(0);
        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        var p2pAccel = coordinator.CreateAccelerator(context);
        var dispatcher = new P2PDispatcher(p2pAccel);
        var transport = new P2PTransport(
            new SpawnDev.WebTorrent.WebTorrentClient(), coordinator, dispatcher);
        var worker = new P2PWorker(transport);
        worker.Initialize(context, accelerator);
        worker.SetKeyRegistry(registry);

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.Identity))!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 64);
        request.CoordinatorPublicKey = Convert.ToBase64String(coordIdentity.PublicKeySpki);
        request.Buffers = new[]
        {
            new BufferBinding { ParameterIndex = 1, BufferId = "in", Length = 64, ElementSize = 4 },
            new BufferBinding { ParameterIndex = 2, BufferId = "out", Length = 64, ElementSize = 4 },
        };
        worker.ReceiveBuffer("in", new byte[64 * 4]);

        bool completed = false;
        bool success = false;
        worker.OnKernelCompleted += (id, s, ms) => { completed = true; success = s; };

        await worker.HandleDispatchAsync("revoked-coord", request);

        if (!completed) throw new Exception("Dispatch event should fire");
        if (success) throw new Exception("Dispatch from revoked key should be REJECTED");
    }

    /// <summary>
    /// Kick removes a connected peer from the swarm.
    /// </summary>
    [TestMethod]
    public async Task Security_Kick_RemovesPeer()
    {
        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        await coordinator.CreateSwarmAsync("kick-test");

        coordinator.HandlePeerConnected("peer-1", new PeerCapabilities
        {
            PeerId = "peer-1",
            EstimatedTflops = 2.0,
            IsCharging = true,
        });

        if (coordinator.PeerCount != 1) throw new Exception($"Should have 1 peer, got {coordinator.PeerCount}");

        string? kickedPeer = null;
        coordinator.OnPeerKicked += (peerId, reason) => { kickedPeer = peerId; };

        bool kicked = coordinator.KickPeer("peer-1", "test kick");

        if (!kicked) throw new Exception("Kick should succeed for coordinator");
        if (kickedPeer != "peer-1") throw new Exception($"Kicked event should fire, got '{kickedPeer}'");
        if (coordinator.PeerCount != 0) throw new Exception($"Peer should be removed, count={coordinator.PeerCount}");
    }

    /// <summary>
    /// Blocked peer cannot reconnect.
    /// </summary>
    [TestMethod]
    public async Task Security_Block_PreventsReconnect()
    {
        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        await coordinator.CreateSwarmAsync("block-test");

        coordinator.HandlePeerConnected("bad-peer", new PeerCapabilities
        {
            PeerId = "bad-peer",
            EstimatedTflops = 1.0,
            IsCharging = true,
        });

        bool blocked = coordinator.BlockPeer("bad-peer", "misbehavior");
        if (!blocked) throw new Exception("Block should succeed");
        if (coordinator.PeerCount != 0) throw new Exception("Blocked peer should be disconnected");
        if (!coordinator.IsPeerBlocked("bad-peer")) throw new Exception("Peer should be in block list");

        string? rejectedPeer = null;
        string? rejectedReason = null;
        coordinator.OnPeerRejected += (peerId, reason) =>
        {
            rejectedPeer = peerId;
            rejectedReason = reason;
        };

        bool reconnected = coordinator.HandlePeerConnected("bad-peer", new PeerCapabilities
        {
            PeerId = "bad-peer",
            EstimatedTflops = 1.0,
            IsCharging = true,
        });

        if (reconnected) throw new Exception("Blocked peer should not be admitted");
        if (rejectedPeer != "bad-peer") throw new Exception($"Rejection event should fire, got '{rejectedPeer}'");
        if (rejectedReason != "blocked") throw new Exception($"Rejection reason should be 'blocked', got '{rejectedReason}'");
    }

    /// <summary>
    /// Ed25519 identity: create, sign, and verify - proves real crypto works end-to-end.
    /// </summary>
    [TestMethod]
    public async Task Identity_CrossVerify_Ed25519()
    {
        var identity1 = await SwarmIdentity.CreateAsync(_crypto, "peer-1");
        var identity2 = await SwarmIdentity.CreateAsync(_crypto, "peer-2");

        var data = System.Text.Encoding.UTF8.GetBytes("dispatch:vectoradd:1024");
        var signature = await identity1.SignAsync(data);

        if (signature.Length != 64)
            throw new Exception($"Ed25519 signature should always be 64 bytes, got {signature.Length}");

        var verified = await SwarmIdentity.VerifyAsync(
            _crypto, identity1.PublicKeySpki, data, signature);
        if (!verified) throw new Exception("Verification of valid signature should succeed");

        var tampered = System.Text.Encoding.UTF8.GetBytes("dispatch:vectoradd:9999");
        var tamperedVerified = await SwarmIdentity.VerifyAsync(
            _crypto, identity1.PublicKeySpki, tampered, signature);
        if (tamperedVerified) throw new Exception("Verification of tampered data should fail");

        var wrongKeyVerified = await SwarmIdentity.VerifyAsync(
            _crypto, identity2.PublicKeySpki, data, signature);
        if (wrongKeyVerified) throw new Exception("Verification with wrong key should fail");

        await identity1.DisposeAsync();
        await identity2.DisposeAsync();
    }

    /// <summary>
    /// Signed role assignment: granter creates assignment, recipient verifies.
    /// </summary>
    [TestMethod]
    public async Task Identity_RoleAssignment_Signed()
    {
        var owner = await SwarmIdentity.CreateAsync(_crypto, "owner");
        var worker = await SwarmIdentity.CreateAsync(_crypto, "new-coordinator");

        var assignment = await RoleAssignment.CreateAsync(
            owner, "peer-1", worker.PublicKeySpki, SwarmRole.Coordinator);

        if (assignment.Role != SwarmRole.Coordinator) throw new Exception($"Role: {assignment.Role}");
        if (assignment.PeerId != "peer-1") throw new Exception($"PeerId: {assignment.PeerId}");
        if (string.IsNullOrEmpty(assignment.Signature)) throw new Exception("Signature should not be empty");

        var verified = await assignment.VerifyAsync(_crypto);
        if (!verified) throw new Exception("Role assignment signature should verify correctly");

        await owner.DisposeAsync();
        await worker.DisposeAsync();
    }

    /// <summary>
    /// Election respects registry: Admin wins over higher-TFLOPS Worker.
    /// </summary>
    [TestMethod]
    public async Task Identity_Election_RespectsRegistry()
    {
        var owner = await SwarmIdentity.CreateAsync(_crypto, "owner");
        var adminIdentity = await SwarmIdentity.CreateAsync(_crypto, "admin");
        var workerIdentity = await SwarmIdentity.CreateAsync(_crypto, "fast-worker");

        var registry = new KeyRegistry();
        registry.AddKey(owner.PublicKeySpki, SwarmRole.Owner, "owner");
        registry.AddKey(adminIdentity.PublicKeySpki, SwarmRole.Admin, "admin");
        registry.AddKey(workerIdentity.PublicKeySpki, SwarmRole.Worker, "fast-worker");
        await registry.SignAsync(owner);

        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        coordinator.UpdateRegistry(registry);

        coordinator.HandlePeerConnected("admin-peer", new PeerCapabilities
        {
            PeerId = "admin-peer",
            EstimatedTflops = 1.0,
            PublicKey = Convert.ToBase64String(adminIdentity.PublicKeySpki),
            IsCharging = true,
        });

        coordinator.HandlePeerConnected("worker-peer", new PeerCapabilities
        {
            PeerId = "worker-peer",
            EstimatedTflops = 20.0,
            PublicKey = Convert.ToBase64String(workerIdentity.PublicKeySpki),
            IsCharging = true,
        });

        var winner = coordinator.ElectCoordinator();

        if (winner != "admin-peer")
            throw new Exception($"Admin should win election over higher-TFLOPS Worker, got '{winner}'");

        await owner.DisposeAsync();
        await adminIdentity.DisposeAsync();
        await workerIdentity.DisposeAsync();
    }

    /// <summary>
    /// Open policy: any peer joins without credentials.
    /// </summary>
    [TestMethod]
    public Task Policy_Open_AnyPeerJoins()
    {
        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        coordinator.Policy = new SwarmPolicy { JoinPermission = JoinMode.Open };

        var result = coordinator.HandlePeerConnected("anonymous", new PeerCapabilities
        {
            PeerId = "anonymous",
            EstimatedTflops = 1.0,
            IsCharging = true,
        });

        if (!result) throw new Exception("Open policy should admit any peer");
        if (coordinator.PeerCount != 1) throw new Exception($"PeerCount: {coordinator.PeerCount}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// KnownOnly policy: unknown peer is rejected.
    /// </summary>
    [TestMethod]
    public Task Policy_KnownOnly_NewRejected()
    {
        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        coordinator.Policy = new SwarmPolicy { JoinPermission = JoinMode.KnownOnly };

        string? rejectedPeer = null;
        coordinator.OnPeerRejected += (peerId, reason) => { rejectedPeer = peerId; };

        var result = coordinator.HandlePeerConnected("stranger", new PeerCapabilities
        {
            PeerId = "stranger",
            EstimatedTflops = 1.0,
            IsCharging = true,
        });

        if (result) throw new Exception("KnownOnly should reject unknown peers");
        if (rejectedPeer != "stranger") throw new Exception($"rejectedPeer: {rejectedPeer}");
        if (coordinator.PeerCount != 0) throw new Exception($"PeerCount: {coordinator.PeerCount}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// MaxPeers policy: excess peers are rejected.
    /// </summary>
    [TestMethod]
    public Task Policy_MaxPeers_ExcessRejected()
    {
        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        coordinator.Policy = new SwarmPolicy { MaxPeers = 1 };

        var result1 = coordinator.HandlePeerConnected("first", new PeerCapabilities
        {
            PeerId = "first",
            EstimatedTflops = 1.0,
            IsCharging = true,
        });
        if (!result1) throw new Exception("First peer should be admitted");

        string? rejectedReason = null;
        coordinator.OnPeerRejected += (peerId, reason) => { rejectedReason = reason; };

        var result2 = coordinator.HandlePeerConnected("second", new PeerCapabilities
        {
            PeerId = "second",
            EstimatedTflops = 1.0,
            IsCharging = true,
        });
        if (result2) throw new Exception("Second peer should be rejected (MaxPeers=1)");
        if (rejectedReason == null || !rejectedReason.Contains("full"))
            throw new Exception($"Rejection reason should mention 'full', got: {rejectedReason}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// InviteOnly policy: peer without registry entry is rejected.
    /// </summary>
    [TestMethod]
    public async Task Policy_InviteOnly_RequiresRegistry()
    {
        var owner = await SwarmIdentity.CreateAsync(_crypto, "owner");
        var registry = new KeyRegistry();
        registry.AddKey(owner.PublicKeySpki, SwarmRole.Owner, "owner");
        await registry.SignAsync(owner);

        var coordinator = new P2PSwarmCoordinator(new SpawnDev.WebTorrent.WebTorrentClient());
        coordinator.SetIdentity(owner);
        coordinator.Policy = new SwarmPolicy { JoinPermission = JoinMode.InviteOnly };
        coordinator.UpdateRegistry(registry);

        var randomIdentity = await SwarmIdentity.CreateAsync(_crypto, "random");
        string? rejectedReason = null;
        coordinator.OnPeerRejected += (peerId, reason) => { rejectedReason = reason; };

        var result = coordinator.HandlePeerConnected("uninvited", new PeerCapabilities
        {
            PeerId = "uninvited",
            EstimatedTflops = 5.0,
            PublicKey = Convert.ToBase64String(randomIdentity.PublicKeySpki),
            IsCharging = true,
        });

        if (result) throw new Exception("InviteOnly should reject peer not in registry");
        if (rejectedReason != "invite only") throw new Exception($"rejectedReason: {rejectedReason}");

        await owner.DisposeAsync();
        await randomIdentity.DisposeAsync();
    }

    /// <summary>
    /// Key registry: last owner cannot be revoked (prevents lockout).
    /// </summary>
    [TestMethod]
    public async Task Security_LastOwner_CannotBeRevoked()
    {
        var owner = await SwarmIdentity.CreateAsync(_crypto, "owner");
        var registry = new KeyRegistry();
        registry.AddKey(owner.PublicKeySpki, SwarmRole.Owner, "owner");

        bool revoked = registry.RevokeKey(owner.PublicKeySpki, "test");

        if (revoked) throw new Exception("Revoking the last owner should fail (prevents lockout)");
        if (registry.Revocations.Count != 0)
            throw new Exception($"No revocation should be recorded, count={registry.Revocations.Count}");

        await owner.DisposeAsync();
    }

    /// <summary>
    /// Key registry: sequence number increases on modifications (replay protection).
    /// </summary>
    [TestMethod]
    public async Task Security_RegistrySequence_Monotonic()
    {
        var owner = await SwarmIdentity.CreateAsync(_crypto, "owner");
        var registry = new KeyRegistry();

        long seq0 = registry.Sequence;
        registry.AddKey(owner.PublicKeySpki, SwarmRole.Owner, "owner");
        long seq1 = registry.Sequence;

        var worker = await SwarmIdentity.CreateAsync(_crypto, "worker");
        registry.AddKey(worker.PublicKeySpki, SwarmRole.Worker, "worker");
        long seq2 = registry.Sequence;

        registry.RevokeKey(worker.PublicKeySpki, "test");
        long seq3 = registry.Sequence;

        if (!(seq1 > seq0)) throw new Exception("AddKey should increment sequence");
        if (!(seq2 > seq1)) throw new Exception("Second AddKey should increment");
        if (!(seq3 > seq2)) throw new Exception("RevokeKey should increment");

        await owner.DisposeAsync();
        await worker.DisposeAsync();
    }

    /// <summary>
    /// P2PProtocol message signing and verification round-trip.
    /// </summary>
    [TestMethod]
    public async Task Security_MessageSignVerify_RoundTrip()
    {
        var identity = await SwarmIdentity.CreateAsync(_crypto, "signer");

        var message = new P2PMessage
        {
            Type = P2PMessageType.KernelDispatch,
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { test = "data" }),
        };

        await P2PProtocol.SignMessageAsync(message, identity);

        if (string.IsNullOrEmpty(message.SenderPublicKey))
            throw new Exception("SenderPublicKey should be set");
        if (string.IsNullOrEmpty(message.Signature))
            throw new Exception("Signature should be set");

        var verified = await P2PProtocol.VerifyMessageAsync(message, _crypto);
        if (!verified) throw new Exception("Signed message should verify successfully");

        message.Type = P2PMessageType.Kick;
        var tamperedResult = await P2PProtocol.VerifyMessageAsync(message, _crypto);
        if (tamperedResult) throw new Exception("Tampered message should fail verification");

        await identity.DisposeAsync();
    }

    /// <summary>
    /// Kernel allowlist: unregistered types cannot be resolved.
    /// </summary>
    [TestMethod]
    public Task Security_KernelAllowlist_EnforcedOnResolve()
    {
        P2PKernelSerializer.ClearAllowlist();
        P2PKernelSerializer.RegisterKernelType(typeof(P2PTestKernels));

        var method = typeof(P2PTestKernels).GetMethod(nameof(P2PTestKernels.VectorAdd))!;
        var request = P2PKernelSerializer.CreateDispatch(method, gridDimX: 256);
        var resolved = P2PKernelSerializer.ResolveKernel(request);
        if (resolved == null) throw new Exception("Registered kernel type should resolve");

        var evilRequest = new KernelDispatchRequest
        {
            KernelType = typeof(Environment).AssemblyQualifiedName!,
            KernelMethod = "Exit",
        };
        var evilResolved = P2PKernelSerializer.ResolveKernel(evilRequest);
        if (evilResolved != null)
            throw new Exception("Unregistered kernel type should NOT resolve - security violation");

        // Re-register for other tests
        P2PKernelSerializer.RegisterKernelType(typeof(P2PTestKernels));
        return Task.CompletedTask;
    }
}
