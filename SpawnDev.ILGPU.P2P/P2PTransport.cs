using System.Collections.Concurrent;
using SpawnDev.SpawnJS;
using System.Text.Json;
using System.Threading.Channels;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.WebTorrent;

namespace SpawnDev.ILGPU.P2P;

/// <summary>
/// Bridges SpawnDev.WebTorrent peer connections to the P2P compute protocol.
/// Handles message routing between the P2PDispatcher and WebRTC data channels.
///
/// Each connected WebTorrent peer gets a P2P compute channel layered on top
/// of the existing BitTorrent wire protocol connection.
/// </summary>
public class P2PTransport : IAsyncDisposable
{
    private readonly WebTorrentClient _client;
    private readonly P2PSwarmCoordinator _coordinator;
    private readonly P2PDispatcher _dispatcher;
    private readonly ConcurrentDictionary<string, PeerChannel> _channels = new();
    private readonly ConcurrentDictionary<string, PeerMessageQueue> _peerQueues = new();
    private readonly P2PBufferTransfer _bufferTransfer = new();
    private P2PWorker? _worker;
    private IPortableCrypto? _crypto;

    /// <summary>
    /// Fired when a compute message is received from a peer.
    /// </summary>
    public event Action<string, P2PMessage>? OnMessageReceived;

    public P2PTransport(
        WebTorrentClient client,
        P2PSwarmCoordinator coordinator,
        P2PDispatcher dispatcher)
    {
        _client = client;
        _coordinator = coordinator;
        _dispatcher = dispatcher;

        // When a complete buffer lands on this side, forward it into the worker's
        // buffer store so HandleDispatchAsync can find it. No-op on the coordinator
        // side where _worker is null — the coordinator observes via BufferTransfer.OnBufferReceived.
        _bufferTransfer.OnBufferReceived += (bufferId, data) =>
        {
            _worker?.ReceiveBuffer(bufferId, data);
        };
    }

    /// <summary>
    /// Set the crypto provider for message signature verification.
    /// </summary>
    public void SetCrypto(IPortableCrypto crypto)
    {
        _crypto = crypto;
    }

    /// <summary>
    /// Set the worker for handling kernel dispatch on this node.
    /// </summary>
    public void SetWorker(P2PWorker worker)
    {
        _worker = worker;
    }

    /// <summary>
    /// Register a peer connection for P2P compute messaging.
    /// Called when a WebTorrent peer connects via WebRTC.
    /// </summary>
    public void RegisterPeer(string peerId, Func<byte[], Task> sendFunc)
    {
        var channel = new PeerChannel
        {
            PeerId = peerId,
            SendAsync = sendFunc,
        };
        _channels[peerId] = channel;

        // Request capabilities from the new peer
        _ = SendMessageAsync(peerId, new P2PMessage
        {
            Type = P2PMessageType.CapabilityRequest,
        });
    }

    /// <summary>
    /// Unregister a peer (disconnected).
    /// </summary>
    public void UnregisterPeer(string peerId)
    {
        // Capture the call site (gated on VerboseLogging) for diagnosing mid-
        // dispatch peer disconnects. Identifies which path triggered the
        // unregister: bridge wire OnClose, P2PMessageType.Disconnect message,
        // etc. Cheap - only fires once per UnregisterPeer when enabled.
        if (P2PCompute.VerboseLogging)
        {
            try
            {
                var stack = new System.Diagnostics.StackTrace(1, false).ToString();
                var firstFew = string.Join(" | ", stack.Split('\n').Take(3).Select(l => l.Trim()));
                Console.WriteLine($"[P2PTransport][UNREG-DIAG] peerId={peerId} stack=[{firstFew}]");
                try { SpawnJSRuntime.Instance.Set("__p2p_unregister_caller",
                    $"peerId={peerId} stack=[{firstFew}]"); } catch { }
            } catch { }
        }
        _channels.TryRemove(peerId, out _);
        if (_peerQueues.TryRemove(peerId, out var queue))
            _ = queue.ShutdownAsync();
        _coordinator.HandlePeerDisconnected(peerId);
        _dispatcher.HandlePeerLost(peerId);
    }

    /// <summary>
    /// Handle incoming data from a peer's compute channel.
    ///
    /// Messages from the same peer are processed strictly in arrival order via a
    /// per-peer queue. This eliminates the race where buffer chunks from a chunked
    /// BufferSend could still be reassembling when a following KernelDispatch
    /// started executing — tests previously worked around it with
    /// <c>WaitForWorkerBuffersAsync</c>. Messages from DIFFERENT peers still run
    /// concurrently, so swarms with many peers parallelize naturally.
    ///
    /// Callers fire-and-forget the returned <see cref="Task"/>; the enqueue itself
    /// is synchronous and thread-safe, so arrival order at the enqueue point is
    /// the order in which the peer's consumer loop processes the messages.
    /// </summary>
    public Task HandleIncomingDataAsync(string peerId, byte[] data)
    {
        var queue = _peerQueues.GetOrAdd(peerId,
            pid => new PeerMessageQueue(d => ProcessIncomingDataAsync(pid, d)));
        return queue.EnqueueAsync(data);
    }

    /// <summary>
    /// The actual message-handling pipeline, invoked serially by the per-peer
    /// <see cref="PeerMessageQueue"/> consumer task. Authority-sensitive messages
    /// require valid signatures.
    ///
    /// Binary fast-path: frames starting with 0x02 (BufferSend) or 0x03 (BufferData)
    /// are decoded directly by <see cref="P2PBinaryFrame.TryDecodeBufferChunk"/> and
    /// handed straight to <see cref="P2PBufferTransfer.ReceiveChunk"/>, skipping JSON
    /// deserialization + base64 decoding entirely. JSON P2PMessage payloads always
    /// start with '{' (0x7B) so there is no ambiguity.
    /// </summary>
    private async Task ProcessIncomingDataAsync(string peerId, byte[] data)
    {
        // Binary fast-path for tensor chunk frames — no JSON, no base64, no allocations
        // beyond the chunk payload itself.
        if (P2PBinaryFrame.IsBinaryFrame(data))
        {
            if (!P2PBinaryFrame.TryDecodeBufferChunk(data, out var chunk, out _))
                return; // malformed frame, drop
            // Gate on registered peer same as the JSON path.
            if (!_channels.ContainsKey(peerId)) return;
            _bufferTransfer.ReceiveChunk(chunk);
            return;
        }

        P2PMessage? message;
        try
        {
            message = P2PProtocol.Deserialize(data);
        }
        catch
        {
            return; // Malformed message — ignore
        }

        if (message == null) return;

        // Verify the sender is a registered peer (transport-level identity from WebRTC)
        // CapabilityResponse is exempt — it arrives during initial handshake before registration
        if (message.Type != P2PMessageType.CapabilityResponse &&
            message.Type != P2PMessageType.CapabilityRequest &&
            !_channels.ContainsKey(peerId))
        {
            if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PTransport] Rejected {message.Type} from unregistered peer {peerId}");
            return;
        }

        // Verify signatures on authority-sensitive messages
        if (P2PProtocol.RequiresSignature(message.Type))
        {
            if (_crypto == null || !await VerifyAuthorityAsync(message))
                return; // Reject unsigned/forged authority messages
        }

        OnMessageReceived?.Invoke(peerId, message);

        switch (message.Type)
        {
            case P2PMessageType.CapabilityResponse:
                HandleCapabilityResponse(peerId, message);
                break;

            case P2PMessageType.KernelResult:
                HandleKernelResult(peerId, message);
                break;

            case P2PMessageType.BufferData:
                HandleBufferData(peerId, message);
                break;

            case P2PMessageType.Heartbeat:
                _dispatcher.HandleHeartbeat(peerId);
                _bufferTransfer.CleanupStaleTransfers();
                break;

            case P2PMessageType.StatusUpdate:
                HandleStatusUpdate(peerId, message);
                break;

            case P2PMessageType.Disconnect:
                UnregisterPeer(peerId);
                break;

            // Peer-side handling (when this node is a worker):
            case P2PMessageType.CapabilityRequest:
                await HandleCapabilityRequest(peerId);
                break;

            case P2PMessageType.KernelDispatch:
                await HandleKernelDispatchRequest(peerId, message);
                break;

            case P2PMessageType.BufferSend:
                HandleBufferReceive(peerId, message);
                break;

            // Coordinator role management:
            case P2PMessageType.CoordinatorTransfer:
                HandleCoordinatorTransfer(peerId, message);
                break;

            case P2PMessageType.CoordinatorAnnounce:
                HandleCoordinatorAnnounce(peerId, message);
                break;

            case P2PMessageType.Kick:
                HandleKick(peerId, message);
                break;

            case P2PMessageType.GracefulHandoff:
                HandleGracefulHandoff(peerId, message);
                break;

            // Ownership / RBAC:
            case P2PMessageType.RoleAssign:
                await HandleRoleAssignAsync(peerId, message);
                break;

            case P2PMessageType.RegistryUpdate:
                await HandleRegistryUpdateAsync(peerId, message);
                break;
        }
    }

    /// <summary>
    /// Verify that an authority-sensitive message has a valid signature
    /// and the sender has sufficient authority in the registry.
    /// </summary>
    private async Task<bool> VerifyAuthorityAsync(P2PMessage message)
    {
        if (_crypto == null) return false;

        // Must have a valid signature
        if (!await P2PProtocol.VerifyMessageAsync(message, _crypto))
            return false;

        // If we have a registry, verify the sender's role
        var registry = _coordinator.GetRegistry();
        if (registry != null && !string.IsNullOrEmpty(message.SenderPublicKey))
        {
            var minRole = message.Type switch
            {
                P2PMessageType.CoordinatorTransfer => SwarmRole.Coordinator,
                P2PMessageType.CoordinatorAnnounce => SwarmRole.Coordinator,
                P2PMessageType.Kick => SwarmRole.Coordinator,
                P2PMessageType.Block => SwarmRole.Coordinator,
                P2PMessageType.RoleAssign => SwarmRole.Admin,
                P2PMessageType.RegistryUpdate => SwarmRole.Owner,
                _ => SwarmRole.Worker,
            };
            if (!registry.HasRole(message.SenderPublicKey, minRole))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Send an authority-sensitive message, auto-signing if identity is available.
    /// </summary>
    public async Task SendSignedMessageAsync(string peerId, P2PMessage message)
    {
        if (_coordinator.Identity != null && P2PProtocol.RequiresSignature(message.Type))
            await P2PProtocol.SignMessageAsync(message, _coordinator.Identity);
        await SendMessageAsync(peerId, message);
    }

    /// <summary>
    /// Broadcast an authority-sensitive message to all peers, auto-signing.
    /// </summary>
    public async Task BroadcastSignedAsync(P2PMessage message)
    {
        if (_coordinator.Identity != null && P2PProtocol.RequiresSignature(message.Type))
            await P2PProtocol.SignMessageAsync(message, _coordinator.Identity);
        var data = P2PProtocol.Serialize(message);
        foreach (var channel in _channels.Values)
        {
            try { await channel.SendAsync(data); }
            catch (Exception ex) { Console.WriteLine($"[P2PTransport] Broadcast send to {channel.PeerId} failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Send the current registry to a specific peer.
    /// Called when a new peer joins to sync them with the swarm's authority state.
    /// </summary>
    public async Task SendRegistryAsync(string peerId)
    {
        var registry = _coordinator.GetRegistry();
        if (registry == null || _coordinator.Identity == null) return;

        var message = new P2PMessage
        {
            Type = P2PMessageType.RegistryUpdate,
            Payload = JsonSerializer.SerializeToElement(registry),
        };
        await SendSignedMessageAsync(peerId, message);
    }

    /// <summary>
    /// Send a P2P compute message to a specific peer.
    /// </summary>
    public async Task SendMessageAsync(string peerId, P2PMessage message)
    {
        if (_channels.TryGetValue(peerId, out var channel))
        {
            var data = P2PProtocol.Serialize(message);
            await channel.SendAsync(data);
        }
    }

    /// <summary>
    /// Broadcast a message to all connected compute peers.
    /// </summary>
    public async Task BroadcastAsync(P2PMessage message)
    {
        var data = P2PProtocol.Serialize(message);
        foreach (var channel in _channels.Values)
        {
            try { await channel.SendAsync(data); }
            catch (Exception ex) { Console.WriteLine($"[P2PTransport] Broadcast send to {channel.PeerId} failed: {ex.Message}"); }
        }
    }

    #region Message Handlers — Coordinator Side

    private async void HandleCapabilityResponse(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;
        var caps = message.Payload.Value.Deserialize<PeerCapabilities>();
        if (caps != null)
        {
            var accepted = _coordinator.HandlePeerConnected(peerId, caps);
            // Send registry to newly accepted peers so they know the authority chain
            if (accepted && _coordinator.GetRegistry() != null)
            {
                await SendRegistryAsync(peerId);
            }
        }
    }

    private void HandleKernelResult(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;
        var result = message.Payload.Value.Deserialize<KernelDispatchResult>();
        if (result != null)
        {
            _dispatcher.HandleResult(result.DispatchId, result);
        }
    }

    private void HandleBufferData(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;
        var chunk = message.Payload.Value.Deserialize<BufferChunk>();
        if (chunk != null)
        {
            _bufferTransfer.ReceiveChunk(chunk);
        }
    }

    /// <summary>
    /// Maximum number of outbound buffer chunks issued concurrently per
    /// <see cref="SendBufferAsync"/> call. Larger values increase pipeline
    /// parallelism (and therefore throughput) at the cost of peak memory
    /// held in-flight across SCTP send buffers. 8 is conservative: a 10 MB
    /// buffer at 256 KiB chunks = 40 chunks = 5 batches of 8, keeping peak
    /// in-flight payload around ~2 MB while cutting wall-clock time vs the
    /// fully-serialized `foreach await` pattern.
    /// </summary>
    public int OutboundChunkPipelineWindow { get; set; } = 8;

    /// <summary>
    /// Send buffer data to a peer in chunks. Chunks are encoded as binary wire
    /// frames via <see cref="P2PBinaryFrame.EncodeBufferChunk"/> — no JSON, no
    /// base64 — then written directly to the peer's registered send delegate in
    /// <see cref="OutboundChunkPipelineWindow"/>-sized concurrent batches.
    /// Bypassing <see cref="P2PProtocol.Serialize"/> for this hot path eliminates
    /// the 33% base64 bandwidth tax and the double-JSON-encode CPU cost that
    /// previously bottlenecked multi-megabyte tensor transfer.
    /// </summary>
    public async Task SendBufferAsync(string peerId, string bufferId, byte[] data)
    {
        if (!_channels.TryGetValue(peerId, out var channel))
            return;

        var chunks = _bufferTransfer.CreateChunks(bufferId, data);
        var window = Math.Max(1, OutboundChunkPipelineWindow);
        for (int batchStart = 0; batchStart < chunks.Length; batchStart += window)
        {
            var batchEnd = Math.Min(batchStart + window, chunks.Length);
            var sends = new Task[batchEnd - batchStart];
            for (int i = batchStart; i < batchEnd; i++)
            {
                var frame = P2PBinaryFrame.EncodeBufferChunk(P2PMessageType.BufferSend, chunks[i]);
                sends[i - batchStart] = channel.SendAsync(frame);
            }
            await Task.WhenAll(sends);
        }
    }

    /// <summary>
    /// Access the buffer transfer for progress tracking and events.
    /// </summary>
    public P2PBufferTransfer BufferTransfer => _bufferTransfer;

    private void HandleStatusUpdate(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;
        var caps = message.Payload.Value.Deserialize<PeerCapabilities>();
        if (caps != null)
        {
            _dispatcher.UpdatePeerCapabilities(peerId, caps);
        }
    }

    #endregion

    #region Message Handlers — Worker Side

    private async Task HandleCapabilityRequest(string peerId)
    {
        // Use worker's real capabilities if available, otherwise fallback
        var caps = _worker != null
            ? _worker.BuildCapabilities(peerId)
            : BuildLocalCapabilities();

        // Include cryptographic identity if available
        if (_coordinator.Identity != null)
        {
            caps.PublicKey = Convert.ToBase64String(_coordinator.Identity.PublicKeySpki);
            caps.Fingerprint = _coordinator.Identity.Fingerprint;
        }

        await SendMessageAsync(peerId, new P2PMessage
        {
            Type = P2PMessageType.CapabilityResponse,
            Payload = JsonSerializer.SerializeToElement(caps),
        });
    }

    private async Task HandleKernelDispatchRequest(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;
        var request = message.Payload.Value.Deserialize<KernelDispatchRequest>();
        if (request == null) return;

        // Route to the worker for local execution
        if (_worker != null)
        {
            await _worker.HandleDispatchAsync(peerId, request);
        }
        else
        {
            await SendMessageAsync(peerId, new P2PMessage
            {
                Type = P2PMessageType.KernelResult,
                ReplyTo = message.MessageId,
                Payload = JsonSerializer.SerializeToElement(new KernelDispatchResult
                {
                    DispatchId = request.DispatchId,
                    Success = false,
                    Error = "No worker initialized on this peer",
                }),
            });
        }
    }

    private void HandleBufferReceive(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;
        var chunk = message.Payload.Value.Deserialize<BufferChunk>();
        if (chunk != null)
        {
            _bufferTransfer.ReceiveChunk(chunk);
        }
    }

    #endregion

    #region Message Handlers — Coordinator Role Management

    private async Task HandleRoleAssignAsync(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;
        var assignment = message.Payload.Value.Deserialize<RoleAssignment>();
        if (assignment == null) return;

        // Verify the inner assignment signature matches the granter's key
        if (_crypto != null)
        {
            if (assignment.IsExpired) return;
            if (!await assignment.VerifyAsync(_crypto)) return;
        }

        OnRoleAssigned?.Invoke(assignment);
    }

    private async Task HandleRegistryUpdateAsync(string peerId, P2PMessage message)
    {
        if (message.Payload == null || _crypto == null) return;
        var registry = message.Payload.Value.Deserialize<KeyRegistry>();
        if (registry == null) return;

        // Verify the registry is signed by a known owner
        var currentRegistry = _coordinator.GetRegistry();
        if (currentRegistry != null)
        {
            // Only accept if sequence is higher (prevents replay)
            if (registry.Sequence <= currentRegistry.Sequence) return;

            // Verify against any known owner key
            var ownerKey = currentRegistry.Keys
                .FirstOrDefault(k => k.Role == SwarmRole.Owner && !currentRegistry.IsRevoked(k.PublicKey));
            if (ownerKey != null)
            {
                var ownerSpki = Convert.FromBase64String(ownerKey.PublicKey);
                if (!await registry.VerifyAsync(_crypto, ownerSpki)) return;
            }
        }

        // Accept the updated registry
        _coordinator.UpdateRegistry(registry);
        _worker?.SetKeyRegistry(registry);
        OnRegistryUpdated?.Invoke(registry);
    }

    /// <summary>Fired when a role assignment is received.</summary>
    public event Action<RoleAssignment>? OnRoleAssigned;

    /// <summary>Fired when the swarm registry is updated.</summary>
    public event Action<KeyRegistry>? OnRegistryUpdated;

    private void HandleCoordinatorTransfer(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;

        // We've been told we're the new coordinator
        _coordinator.BecomeCoordinator();

        // Accept pending dispatch state if included in the transfer
        var transferData = message.Payload.Value.Deserialize<CoordinatorTransferData>();
        if (transferData?.PendingDispatches != null)
        {
            foreach (var pending in transferData.PendingDispatches)
            {
                // Re-register pending dispatches in our dispatcher
                _dispatcher.HandlePendingTransfer(pending);
            }
        }

        OnCoordinatorTransferred?.Invoke(peerId);
        _worker?.NotifyCoordinatorChanged();
    }

    private void HandleCoordinatorAnnounce(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;
        var data = message.Payload.Value.Deserialize<CoordinatorAnnounceData>();
        if (data?.NewCoordinatorPeerId != null)
        {
            _coordinator.CoordinatorPeerId = data.NewCoordinatorPeerId;
            _coordinator.Role = P2PRole.Worker;
            _worker?.NotifyCoordinatorChanged();
        }
    }

    private void HandleKick(string peerId, P2PMessage message)
    {
        // Only workers can be kicked, and only by the coordinator
        if (_coordinator.Role != P2PRole.Worker || peerId != _coordinator.CoordinatorPeerId)
            return;

        // We've been kicked — disconnect from the swarm
        OnKicked?.Invoke(peerId);
    }

    private void HandleGracefulHandoff(string peerId, P2PMessage message)
    {
        if (message.Payload == null) return;
        // A peer is handing off its pending work to us (thermal/battery eviction)
        var handoff = message.Payload.Value.Deserialize<KernelDispatchRequest>();
        if (handoff != null && _worker != null)
        {
            _ = _worker.HandleDispatchAsync(peerId, handoff);
        }
    }

    /// <summary>Fired when this node becomes coordinator via transfer.</summary>
    public event Action<string>? OnCoordinatorTransferred; // fromPeerId

    /// <summary>Fired when this node is kicked from the swarm.</summary>
    public event Action<string>? OnKicked; // byPeerId

    #endregion

    /// <summary>
    /// Build capabilities for the local device.
    /// </summary>
    /// <summary>
    /// Fallback capabilities when no worker is attached.
    /// Reports minimal defaults — the worker's BuildCapabilities is preferred.
    /// </summary>
    public PeerCapabilities GetLocalCapabilities() => _worker?.BuildCapabilities("local") ?? BuildLocalCapabilities();

    private PeerCapabilities BuildLocalCapabilities()
    {
        return new PeerCapabilities
        {
            PeerId = !string.IsNullOrEmpty(_client.PeerId) ? _client.PeerId : Guid.NewGuid().ToString("N"),
            Platform = OperatingSystem.IsBrowser() ? "browser" : "desktop",
            IlgpuVersion = typeof(P2PAccelerator).Assembly.GetName().Version?.ToString() ?? "4.7.1",
            AvailableBackends = new[] { "CPU" },
            PreferredBackend = "CPU",
            AvailableMemory = Environment.WorkingSet,
            EstimatedTflops = 0.2 * Environment.ProcessorCount,
            MaxThreadsPerGroup = 256,
            IsCharging = true,
            BatteryLevel = -1, // Unknown without battery API
            ThermalState = 0,
        };
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Disable all channels before clearing to prevent sends on disposed transport
        foreach (var channel in _channels.Values)
            channel.SendAsync = _ => Task.CompletedTask;
        _channels.Clear();

        // Drain and stop every per-peer consumer loop so in-flight processors finish
        // before the transport goes away.
        var queues = _peerQueues.Values.ToArray();
        _peerQueues.Clear();
        foreach (var queue in queues)
            await queue.ShutdownAsync();
    }

    /// <summary>
    /// Serializes incoming messages for a single peer. The writer side is sync +
    /// thread-safe (called from <see cref="SdComputeExtension.OnMessage"/>), while
    /// a single background reader drains the channel and invokes the processor one
    /// message at a time. The <see cref="Task"/> returned from <see cref="EnqueueAsync"/>
    /// completes when that specific message has been fully processed, so callers
    /// can fire-and-forget while still getting strictly ordered per-peer processing.
    /// </summary>
    private sealed class PeerMessageQueue
    {
        private readonly Channel<PendingMessage> _channel;
        private readonly Task _consumer;
        private readonly CancellationTokenSource _cts = new();

        public PeerMessageQueue(Func<byte[], Task> processor)
        {
            _channel = Channel.CreateUnbounded<PendingMessage>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
            _consumer = Task.Run(() => ConsumeLoopAsync(processor, _cts.Token));
        }

        public Task EnqueueAsync(byte[] data)
        {
            var pending = new PendingMessage(data);
            // Unbounded channel: TryWrite only fails when the writer is completed
            // (i.e. after ShutdownAsync). Surface that as a cancelled task instead
            // of silently dropping the message so callers can detect shutdown.
            if (!_channel.Writer.TryWrite(pending))
                pending.Done.TrySetCanceled();
            return pending.Done.Task;
        }

        public async Task ShutdownAsync()
        {
            _channel.Writer.TryComplete();
            _cts.Cancel();
            try { await _consumer.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected on shutdown */ }
            _cts.Dispose();
        }

        private async Task ConsumeLoopAsync(Func<byte[], Task> processor, CancellationToken ct)
        {
            try
            {
                await foreach (var item in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        await processor(item.Data).ConfigureAwait(false);
                        item.Done.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        item.Done.TrySetException(ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown path — any remaining in-flight pendings complete via TryComplete.
            }
        }

        private sealed class PendingMessage
        {
            public byte[] Data { get; }
            public TaskCompletionSource Done { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingMessage(byte[] data) => Data = data;
        }
    }
}

/// <summary>
/// Represents a compute messaging channel to a specific peer.
/// </summary>
internal class PeerChannel
{
    public string PeerId { get; set; } = "";
    public Func<byte[], Task> SendAsync { get; set; } = _ => Task.CompletedTask;
}
