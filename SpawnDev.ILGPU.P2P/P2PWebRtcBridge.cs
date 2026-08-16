using SpawnDev.WebTorrent;
using SpawnDev.SpawnJS;

namespace SpawnDev.ILGPU.P2P;

/// <summary>
/// Bridges WebTorrent peer connections to the P2P compute system.
/// Registers sd_compute extension on each new peer, wires message routing.
///
/// Usage:
///   var bridge = new P2PWebRtcBridge(transport);
///   // Attach to a torrent — automatically wires sd_compute to every peer:
///   bridge.AttachToSwarm(torrent);
/// </summary>
public class P2PWebRtcBridge : IAsyncDisposable
{
    private readonly P2PTransport _transport;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SdComputeExtension> _extensions = new();

    /// <summary>
    /// Active wires grouped by canonical (remote BitTorrent) peer ID. Two wires from
    /// the same remote during a handshake-race duplicate scenario share the same
    /// `wire.PeerId`, so we can dedupe at the bridge layer and present ONE peer to
    /// the dispatcher regardless of how many transient wires the WebRTC layer churns
    /// through. When the set for a canonical ID goes empty, the peer is fully gone
    /// and UnregisterPeer fires.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<SpawnDev.WebTorrent.Wire>> _wiresByBtPeerId = new();
    private readonly object _wiresByBtPeerIdLock = new();

    /// <summary>
    /// Reverse map: wire reference -> the canonical peer id it was registered under in
    /// NotifyCanonical. Captured at registration time so wire.OnClose has the correct
    /// id even if wire.PeerId is cleared by the destroy path.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<SpawnDev.WebTorrent.Wire, string> _wireToCanonical = new();

    /// <summary>
    /// Number of peers with sd_compute wired.
    /// </summary>
    public int ComputePeerCount => _extensions.Count;

    /// <summary>
    /// Fired when a new compute-capable peer connects.
    /// </summary>
    public event Action<string>? OnComputePeerConnected;

    /// <summary>
    /// Fired when a peer's capabilities are received.
    /// </summary>
    public event Action<string, PeerCapabilities?>? OnComputePeerCapabilities;

    public P2PWebRtcBridge(P2PTransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// Attach to a Torrent — automatically wires sd_compute
    /// to every peer that connects to this torrent.
    /// Uses the OnWire event (replaces old OnPeerConnect pattern).
    /// </summary>
    public void AttachToSwarm(Torrent torrent)
    {
        torrent.OnWire += (wire, peerId) =>
        {
            if (string.IsNullOrEmpty(peerId))
                peerId = Guid.NewGuid().ToString("N");

            // Resolve the canonical (cross-wire-stable) peer key. After BEP-10 handshake
            // wire.PeerId is set to the remote BitTorrent peerId, which two wires from the
            // same remote (handshake-race duplicate scenario) share. Falls back to the
            // per-wire generated id only until handshake completes.
            string CanonicalPeerId() =>
                !string.IsNullOrEmpty(wire.PeerId) ? wire.PeerId! : peerId;

            // Wire-close detection: when a peer's underlying transport dies (tab closed,
            // network drop, browser navigated away, etc.), we remove that wire from the
            // bridge's tracking. UnregisterPeer fires only when EVERY wire for the
            // canonical peer is gone - the rc.21+ handshake-race duplicate-destroy from
            // SpawnDev.WebTorrent.Torrent.OnHandshake closes the loser wire while the
            // surviving wire is still live; we must not let that close path mistakenly
            // surface as a peer-departure to the dispatcher.
            wire.OnClose += () =>
            {
                var ctr = System.Threading.Interlocked.Increment(ref _bridgeOnCloseCounter);
                if (P2PCompute.VerboseLogging)
                    Console.WriteLine($"[P2PWebRtcBridge][CLOSE-DIAG] #{ctr} peer={peerId} btPeer={wire.PeerId} destroyed={wire.Destroyed} stackTop={new System.Diagnostics.StackTrace(1, false).GetFrame(0)?.GetMethod()?.Name}");
                try
                {
                    SpawnJSRuntime.Instance.Set("__bridge_wire_onclose",
                        $"#{ctr} peer={peerId} btPeer={wire.PeerId} destroyed={wire.Destroyed}");
                } catch { }
                _extensions.TryRemove(peerId, out _);
                // wasTracked = this wire was registered under canonical (BEP-10 fired
                // OR NotifyCanonical fired). Both paths populate _wireToCanonical.
                bool wasTracked = _wireToCanonical.TryRemove(wire, out var canonical)
                    && !string.IsNullOrEmpty(canonical);

                // Decrement the canonical-to-wires bookkeeping. When the BEP-10
                // handshake race produces N wires for one canonical peer, the losers
                // close while the winner stays live. UnregisterPeer must only fire
                // when EVERY wire for the canonical is gone.
                //
                // Phantom-wire filter: the bridge's wire.OnHandshake hook is called
                // AFTER Torrent's own OnHandshake handler. If Torrent's handler runs
                // first and destroys the wire (duplicate-handshake-destroy), the
                // bridge's handler still fires on the destroyed wire and adds it to
                // _wiresByBtPeerId. The wire's subsequent OnClose runs with
                // _wireToCanonical missing for that wire (race during the same
                // handshake invocation chain), so wasTracked=false and the phantom
                // entry stays. When the REAL surviving wire later closes, the
                // phantom inflates the wireSet count and isLastWireForCanonical
                // wrongly stays false, blocking ScheduleDeferredUnregister. Filter
                // destroyed phantoms out of the count to recover.
                bool isLastWireForCanonical = true;
                int beforeFilter = -1, afterFilter = -1;
                string wireSetDump = "?";
                if (wasTracked)
                {
                    lock (_wiresByBtPeerIdLock)
                    {
                        if (_wiresByBtPeerId.TryGetValue(canonical!, out var wireSet))
                        {
                            wireSet.Remove(wire);
                            beforeFilter = wireSet.Count;
                            wireSetDump = string.Join(",",
                                wireSet.Select(w => $"d={w.Destroyed}/td={w.SimplePeer?.IsTransportDead ?? false}/p={w.PeerId?[..Math.Min(8, w.PeerId.Length)] ?? "null"}"));
                            // Filter phantom wires whose Destroyed flag has not yet been set
                            // (Chromium-under-Playwright: connectionstatechange event chain
                            // doesn't propagate on remote tab close, so wire.OnClose never
                            // fires -> Destroyed stays false -> wireSet count stays inflated
                            // -> isLastWireForCanonical wrongly false -> peer never
                            // unregisters). IsTransportDead consults the peer's underlying
                            // transport directly: PC connectionState in {failed,closed} OR
                            // data channel was once open and is no longer.
                            wireSet.RemoveWhere(w => w.Destroyed || (w.SimplePeer?.IsTransportDead ?? false));
                            afterFilter = wireSet.Count;
                            if (wireSet.Count == 0)
                                _wiresByBtPeerId.TryRemove(canonical!, out _);
                            else
                                isLastWireForCanonical = false;
                        }
                    }
                }
                try { SpawnJSRuntime.Instance.Set("__bridge_wireset_dump",
                    $"canonical={canonical} before={beforeFilter} after={afterFilter} dump=[{wireSetDump}]"); } catch { }

                // If this wire was never tracked under any canonical, it didn't
                // contribute to peer registration - nothing to unregister. If
                // other wires are still live for this canonical (after the
                // RemoveWhere(Destroyed) filter above), the survivor keeps the
                // peer up and the duplicate-handshake-destroy cascade is just
                // shifting ownership.
                //
                // NOTE: an earlier draft added a long-grace fallback for the
                // Chromium-under-Playwright bug where some RTCPeerConnections
                // never fire OnClose even after the remote tab closes (their
                // wires stay phantom-alive). That fallback regressed
                // LargeBuffer_100MB - the duplicate-handshake-destroy at the
                // start of the connection left isLastWireForCanonical=false and
                // the fallback then fired 30s in, killing the live dispatch.
                // Reverted to the strict "wait for actual last wire" behavior;
                // the TwoTab Chromium-under-Playwright phantom-alive case needs
                // a separate active-liveness probe (data channel readyState
                // poll, last-activity-time, etc.) before it can be closed
                // without the 100MB regression.
                if (!wasTracked || !isLastWireForCanonical)
                {
                    try { SpawnJSRuntime.Instance.Set("__bridge_short_circuit",
                        $"wasTracked={wasTracked} isLastWire={isLastWireForCanonical} canonical={canonical} wirePeerId={wire.PeerId}"); } catch { }
                    return;
                }

                // The canonical was never promoted to a registered peer (no wire's
                // CapabilityResponse arrived -> NotifyCanonical never fired ->
                // _notified[canonical] never set). Nothing to unregister.
                if (!_notified.ContainsKey(canonical!))
                {
                    try { SpawnJSRuntime.Instance.Set("__bridge_short_circuit",
                        $"notified-miss canonical={canonical}"); } catch { }
                    return;
                }

                // Cross-check torrent.Wires for a LIVE replacement wire bound to the same
                // canonical BT peer id. Closes the BEP-10 vs CapabilityResponse race that
                // rc.27's _wiresByBtPeerId-only check still missed:
                //
                //   1. Wire #1 connects, BEP-10 completes, capability response arrives ->
                //      bridge's NotifyCanonical fires, _wiresByBtPeerId[canonical] = {wire1}
                //   2. Wire #2 connects, OnWire fires (bridge subscribes wire2.OnClose).
                //   3. Wire #2's BEP-10 completes:
                //        a. Torrent's OnHandshake handler (subscribed FIRST in Torrent.AddPeer
                //           BEFORE OnWire?.Invoke fires) detects duplicate, calls
                //           existingPeer.Destroy() on wire1. wire1.OnClose fires INLINE.
                //        b. Bridge's wire1.OnClose runs: _wiresByBtPeerId[canonical] = {wire1},
                //           remove wire1 -> set empty -> isLastWireForCanonical = true ->
                //           UnregisterPeer fires, evicting the peer from coord/_peers.
                //        c. Wire #2's CapabilityResponse hadn't arrived yet, so wire2 was
                //           never in _wiresByBtPeerId.
                //   4. Wire #2's CapabilityResponse arrives later, P2PTransport
                //      .HandleCapabilityResponse re-registers via HandlePeerConnected, but
                //      the test's dispatch fires inside the unregistered window and sees
                //      "No healthy peers available for dispatch."
                //
                // Fix: at this point, walk torrent.Wires for a non-destroyed wire (other
                // than this closing one) with PeerId == canonical. If one exists, the
                // duplicate-detection just shifted ownership; the canonical peer is still
                // live on the new wire. Skip UnregisterPeer entirely. The new wire's
                // CapabilityResponse will fire bridge events when it arrives (or already
                // did, ahead of the close). Diagnosed 2026-04-29 against
                // LargeBuffer_1MB_DispatchedOverRealWebRtc_BitExact: stack trace pinned
                // every close to Wire._onHandshakeBuffer -> Torrent.AddPeer.b__5 ->
                // Peer.Destroy(null), all from the duplicate-handshake destroy path.
                foreach (var otherWire in torrent.Wires.ToArray())
                {
                    if (otherWire == wire) continue;
                    if (otherWire.Destroyed) continue;
                    // Skip phantom-alive wires whose underlying transport is gone but whose
                    // Destroyed flag has not yet been set. Same Chromium-under-Playwright
                    // bug as the wireSet filter above; without this check the foreach would
                    // see the phantom and wrongly conclude a live replacement exists,
                    // skipping UnregisterPeer indefinitely.
                    if (otherWire.SimplePeer?.IsTransportDead == true) continue;
                    if (string.Equals(otherWire.PeerId, canonical, StringComparison.Ordinal))
                        return;
                }

                // Even with no live replacement RIGHT NOW, the duplicate-handshake-destroy
                // cascade typical of a fanned-out tracker announce (4-8 ICE-candidate-driven
                // RTCPeerConnections converging on the same remote BT peerId) can have a
                // brand-new wire's BEP-10 handshake about to fire within a few hundred ms.
                // Unregistering immediately and then re-registering on the next capability
                // response races the consumer's dispatch path - we saw `LargeBuffer_1MB
                // _DispatchedOverRealWebRtc_BitExact` blow through 3 retries (240s) because
                // every dispatch landed in the unregistered-but-about-to-recover window.
                //
                // Defer the unregister by a grace period; cancel if a new wire registers for
                // the same canonical before the timer fires. This preserves the genuine-
                // departure case (no recovery within UnregisterGraceMs -> peer is gone for
                // real) while absorbing transient cascades.
                try
                {
                    SpawnJSRuntime.Instance.Set("__bridge_schedule_unreg",
                        $"canonical={canonical} wire={wire.PeerId} transient={peerId}");
                } catch { }
                ScheduleDeferredUnregister(canonical!, wire.PeerId, peerId);
            };

            // Hook BEP-10 handshake completion to register the wire under its canonical
            // peer-id IMMEDIATELY (before capability exchange completes). Two reasons:
            //
            //   (a) The cross-check in wire.OnClose walks `torrent.Wires` for a
            //       non-destroyed wire with PeerId == canonical to detect "duplicate
            //       handshake destroyed the loser, but the winner is alive." That
            //       check works at the torrent level. Adding to the bridge's own
            //       `_wiresByBtPeerId` set here lets `isLastWireForCanonical`
            //       computation in OnClose see early arrivals too.
            //   (b) Cancels any pending deferred-unregister for this canonical: a new
            //       wire's BEP-10 handshake means the peer is back, even if its
            //       capability response hasn't arrived yet.
            wire.OnHandshake += (infoHash, btPeerId, exts) =>
            {
                if (string.IsNullOrEmpty(btPeerId)) return;
                _wireToCanonical[wire] = btPeerId;
                lock (_wiresByBtPeerIdLock)
                {
                    var wireSet = _wiresByBtPeerId.GetOrAdd(btPeerId, _ => new HashSet<SpawnDev.WebTorrent.Wire>());
                    wireSet.Add(wire);
                }
                // Cancel any pending deferred unregister - the peer is back online.
                CancelDeferredUnregister(btPeerId);
            };

            // Check if the wire already has an SdComputeExtension (from UseExtension factory)
            var existing = wire.GetExtension<SdComputeExtension>();
            if (existing != null)
            {
                // Do NOT pre-seed the extension's _peerId with the OnWire-time per-wire id.
                // SdComputeExtension.OnHandshake will set _peerId to the canonical (remote
                // BitTorrent) peerId once BEP-10 handshake completes; that is the same id
                // both sides see and the same id the bridge uses for OnComputePeerCapabilities.
                // Pre-seeding here would lock the extension to the per-wire id, which causes
                // the extension's transport.RegisterPeer call to register a different peerId
                // than the bridge surfaces - so HandlePeerConnected fires twice (once via the
                // bridge with canonical id, once via P2PTransport.HandleCapabilityResponse with
                // the per-wire id) and the dispatcher's _peers ends up with two entries for
                // one physical worker.
                _extensions[peerId] = existing;

                // Helper that registers the wire under the canonical peer-id and fires
                // the bridge events ONCE per canonical peer regardless of how many wires
                // a handshake race produces.
                void NotifyCanonical(PeerCapabilities? caps)
                {
                    var canonical = CanonicalPeerId();
                    // Capture both directions of the wire <-> canonical mapping so OnClose
                    // can resolve the correct canonical id even after the wire's destroy
                    // clears wire.PeerId.
                    _wireToCanonical[wire] = canonical;
                    lock (_wiresByBtPeerIdLock)
                    {
                        var wireSet = _wiresByBtPeerId.GetOrAdd(canonical, _ => new HashSet<SpawnDev.WebTorrent.Wire>());
                        wireSet.Add(wire);
                    }
                    if (_notified.TryAdd(canonical, true))
                    {
                        OnComputePeerConnected?.Invoke(canonical);
                        OnComputePeerCapabilities?.Invoke(canonical, caps);
                    }
                }

                // Wire up the capability tracking for future messages
                existing.OnComputeMessage += (msg) =>
                {
                    if (msg.Type == P2PMessageType.CapabilityResponse)
                    {
                        PeerCapabilities? caps = null;
                        try
                        {
                            if (msg.Payload.HasValue)
                                caps = System.Text.Json.JsonSerializer.Deserialize<PeerCapabilities>(msg.Payload.Value);
                        }
                        catch (Exception ex)
                        {
                            if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PBridge] Failed to deserialize capabilities from peer {peerId}: {ex.Message}");
                        }
                        NotifyCanonical(caps);
                    }
                };

                // Check if CapabilityResponse already arrived before we wired the handler
                if (existing.LastCapabilityResponse != null)
                {
                    PeerCapabilities? caps = null;
                    try
                    {
                        if (existing.LastCapabilityResponse.Payload.HasValue)
                            caps = System.Text.Json.JsonSerializer.Deserialize<PeerCapabilities>(
                                existing.LastCapabilityResponse.Payload.Value);
                    }
                    catch (Exception ex)
                    {
                        if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PBridge] Failed to deserialize buffered capabilities from peer {peerId}: {ex.Message}");
                    }
                    NotifyCanonical(caps);
                }
            }
            else
            {
                // No factory extension — UseExtension was NOT called before the torrent was created.
                if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PBridge] WARNING: No factory extension for peer {peerId}. " +
                    "UseExtension must be called BEFORE creating/joining the torrent. " +
                    "sd_compute will not work for this peer.");
            }
        };
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _notified = new();

    /// <summary>
    /// Pending deferred unregister timers, keyed by canonical BT peer id. Set when the
    /// last wire for a canonical closes; cancelled if a new wire's BEP-10 handshake
    /// fires for the same canonical before the timer elapses.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.CancellationTokenSource> _pendingUnregisters = new();

    /// <summary>
    /// Grace period before firing UnregisterPeer when the last wire for a canonical
    /// peer dies. Absorbs the duplicate-handshake-destroy cascade typical of a
    /// tracker-fanned-out connection (4-8 ICE-driven RTCPeerConnections converging
    /// on the same BT peerId, each round of BEP-10 handshakes destroying one wire
    /// to keep the swarm at one stable peer connection).
    /// </summary>
    public int UnregisterGraceMs { get; set; } = 5_000;

    private void ScheduleDeferredUnregister(string canonical, string? wirePeerId, string transientPeerId)
    {
        var cts = new System.Threading.CancellationTokenSource();
        // Cancel any prior pending unregister for this canonical and replace.
        if (_pendingUnregisters.TryRemove(canonical, out var prior))
        {
            try { prior.Cancel(); } catch { }
            prior.Dispose();
        }
        _pendingUnregisters[canonical] = cts;

        // Capture identifiers up front. The wire reference is going away; these
        // ids are what UnregisterPeer needs.
        var ids = new HashSet<string>(StringComparer.Ordinal) { canonical };
        if (!string.IsNullOrEmpty(wirePeerId)) ids.Add(wirePeerId!);
        if (!string.IsNullOrEmpty(transientPeerId)) ids.Add(transientPeerId);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(UnregisterGraceMs, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // Cancelled by a new wire's BEP-10 handshake.
            }

            // Grace period elapsed without a new wire arriving. Make sure no other
            // path re-armed _pendingUnregisters[canonical] in between.
            if (!_pendingUnregisters.TryGetValue(canonical, out var current) || current != cts)
                return;

            _pendingUnregisters.TryRemove(canonical, out _);
            // Final safety check: if a wire materialized for this canonical after the
            // delay started but before we got here, bail out.
            lock (_wiresByBtPeerIdLock)
            {
                if (_wiresByBtPeerId.TryGetValue(canonical, out var liveSet) && liveSet.Count > 0)
                    return;
            }

            try
            {
                SpawnJSRuntime.Instance.Set("__bridge_unregister_fired",
                    $"canonical={canonical} ids={string.Join(",", ids)}");
            } catch { }
            foreach (var id in ids)
            {
                _notified.TryRemove(id, out _);
                _transport.UnregisterPeer(id);
            }
        });
    }

    private static int _bridgeOnCloseCounter;

    private void CancelDeferredUnregister(string canonical)
    {
        if (_pendingUnregisters.TryRemove(canonical, out var cts))
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }
    }

    /// <summary>
    /// Send a compute message to a specific peer via the real WebRTC channel.
    /// </summary>
    public async Task SendAsync(string peerId, P2PMessage message)
    {
        if (_extensions.TryGetValue(peerId, out var ext) && ext.IsSupported)
        {
            await ext.SendP2PMessageAsync(message);
        }
    }

    /// <summary>
    /// Check if a peer has sd_compute support.
    /// </summary>
    public bool IsComputeCapable(string peerId)
    {
        return _extensions.TryGetValue(peerId, out var ext) && ext.IsSupported;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _extensions.Clear();
        _notified.Clear();
        _wireToCanonical.Clear();
        lock (_wiresByBtPeerIdLock)
        {
            _wiresByBtPeerId.Clear();
        }
        foreach (var kv in _pendingUnregisters)
        {
            try { kv.Value.Cancel(); } catch { }
            kv.Value.Dispose();
        }
        _pendingUnregisters.Clear();
        return ValueTask.CompletedTask;
    }
}
