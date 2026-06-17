using System.Linq.Expressions;
using System.Reflection;
using ILGPU;
using ILGPU.Backends;
using ILGPU.Runtime;
using SpawnDev.WebTorrent;

namespace SpawnDev.ILGPU.P2P;

/// <summary>
/// P2P accelerator — distributes kernel dispatch across connected peers
/// via SpawnDev.WebTorrent WebRTC data channels.
///
/// Extends KernelAccelerator with P2PCompiledKernel/P2PKernel types.
/// Delegates execution to remote peers running their own local backends.
/// </summary>
public class P2PAccelerator : KernelAccelerator<P2PCompiledKernel, P2PKernel>
{
    private readonly P2PDevice _device;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RemotePeer> _peers = new();

    /// <summary>
    /// Connected remote peers.
    /// </summary>
    public IReadOnlyList<RemotePeer> Peers => _peers.Values.ToList();

    /// <summary>
    /// The WebTorrent client used for P2P communication.
    /// </summary>
    public WebTorrentClient? TorrentClient { get; set; }

    /// <summary>
    /// Creates a new P2P accelerator.
    /// </summary>
    internal P2PAccelerator(Context context, P2PDevice device)
        : base(context, device)
    {
        _device = device;
        DefaultStream = CreateStreamInternal();
        Init(new P2PBackend(context));
    }

    /// <summary>
    /// Add a remote peer to the accelerator.
    /// </summary>
    public void AddPeer(RemotePeer peer)
    {
        _peers[peer.PeerId] = peer;
        peer.Accelerator = this;
    }

    /// <summary>
    /// Remove a remote peer.
    /// </summary>
    public void RemovePeer(RemotePeer peer)
    {
        _peers.TryRemove(peer.PeerId, out _);
        peer.Accelerator = null;
    }

    /// <summary>
    /// Select the best peer for executing a kernel, based on data locality.
    /// </summary>
    public RemotePeer? SelectPeer(P2PMemoryBuffer[]? dataBuffers = null)
    {
        var peers = _peers.Values.ToList();
        if (peers.Count == 0) return null;
        if (dataBuffers != null)
        {
            foreach (var peer in peers)
            {
                if (dataBuffers.All(b => b.ResidentPeer == peer))
                    return peer;
            }
        }
        return peers[Random.Shared.Next(peers.Count)];
    }

    #region KernelAccelerator Implementation

    /// <inheritdoc/>
    protected override P2PKernel CreateKernel(P2PCompiledKernel compiledKernel)
    {
        return new P2PKernel(this, compiledKernel, null);
    }

    /// <inheritdoc/>
    protected override P2PKernel CreateKernel(
        P2PCompiledKernel compiledKernel,
        MethodInfo launcher)
    {
        return new P2PKernel(this, compiledKernel, launcher);
    }

    /// <inheritdoc/>
    protected override MethodInfo GenerateKernelLauncherMethod(
        P2PCompiledKernel kernel, int customGroupSize)
    {
        // P2P dispatch doesn't use IL-generated launchers.
        // Use DispatchAsync/DispatchToSwarmAsync for coordinator-side dispatch.
        throw new NotSupportedException(
            "P2P accelerator uses DispatchAsync() for remote dispatch, not LoadAutoGroupedStreamKernel. " +
            "See P2PAccelerator.DispatchAsync() or P2PCompute.DispatchAsync().");
    }

    #endregion

    #region P2P Dispatch API — Coordinator Side

    /// <summary>
    /// The dispatcher for routing work to peers. Set by P2PCompute facade.
    /// </summary>
    public P2PDispatcher? Dispatcher { get; set; }

    /// <summary>
    /// Dispatch a kernel to the best available peer and wait for the result.
    /// This is the coordinator-side API — the kernel executes on a remote peer's GPU.
    ///
    /// Usage:
    ///   var result = await p2pAccelerator.DispatchAsync(MyKernel, 1024,
    ///       ("a", aData, 4), ("b", bData, 4), ("result", null, 4));
    /// </summary>
    /// <param name="kernelMethod">Static kernel method (must be registered via P2PKernelSerializer).</param>
    /// <param name="gridDimX">Total work items.</param>
    /// <param name="buffers">Buffer bindings: (bufferId, data, elementSize). Null data = output-only.</param>
    /// <returns>Dispatch ID for tracking.</returns>
    public string DispatchToSwarm(MethodInfo kernelMethod, long gridDimX,
        params (string bufferId, byte[]? data, int elementSize)[] buffers)
    {
        if (Dispatcher == null)
            throw new InvalidOperationException("Dispatcher not set. Use P2PCompute facade.");

        var request = P2PKernelSerializer.CreateDispatch(kernelMethod, gridDimX);
        request.Buffers = BuildBufferBindings(buffers, gridDimX);
        return Dispatcher.Dispatch(request, ExtractInputBuffers(buffers));
    }

    /// <summary>
    /// Dispatch a kernel and await the result from the remote peer.
    /// </summary>
    public Task<KernelDispatchResult> DispatchAsync(MethodInfo kernelMethod, long gridDimX,
        params (string bufferId, byte[]? data, int elementSize)[] buffers)
        => DispatchAsync(kernelMethod, gridDimX, scalarValues: null, buffers);

    /// <summary>
    /// Dispatch a kernel with scalar parameter values and await the result from the remote peer.
    /// </summary>
    /// <param name="scalarValues">
    /// Scalar kernel parameter values keyed by parameter index. For a kernel signature
    /// <c>(Index1D, ArrayView&lt;float&gt; input, ArrayView&lt;float&gt; result, float scalar)</c>
    /// pass <c>new Dictionary&lt;int, object&gt; { [3] = 7.5f }</c>. Buffers still flow through the
    /// <paramref name="buffers"/> params array; only non-buffer scalar parameters belong here.
    /// </param>
    public async Task<KernelDispatchResult> DispatchAsync(MethodInfo kernelMethod, long gridDimX,
        IReadOnlyDictionary<int, object>? scalarValues,
        params (string bufferId, byte[]? data, int elementSize)[] buffers)
    {
        if (Dispatcher == null)
            throw new InvalidOperationException("Dispatcher not set. Use P2PCompute facade.");

        var request = P2PKernelSerializer.CreateDispatch(kernelMethod, gridDimX, scalarValues: scalarValues);
        request.Buffers = BuildBufferBindings(buffers, gridDimX);
        return await Dispatcher.DispatchAsync(request, ExtractInputBuffers(buffers));
    }

    /// <summary>
    /// Collect caller-provided input buffers (tuples with data != null) into a
    /// dispatch-level dictionary the dispatcher can ship to the selected peer
    /// before the KernelDispatch message fires. Tuples with null data are
    /// output-only buffers the worker allocates locally - not transmitted.
    /// </summary>
    private static IReadOnlyDictionary<string, byte[]>? ExtractInputBuffers(
        (string bufferId, byte[]? data, int elementSize)[] buffers)
    {
        Dictionary<string, byte[]>? inputs = null;
        foreach (var (bufferId, data, _) in buffers)
        {
            if (data == null) continue;
            inputs ??= new Dictionary<string, byte[]>(buffers.Length);
            inputs[bufferId] = data;
        }
        return inputs;
    }

    /// <summary>
    /// Build buffer bindings from the caller's (bufferId, data, elementSize) tuples.
    ///
    /// When <paramref name="buffers"/>[i].data is non-null it is treated as an input
    /// buffer and the element count is derived from the byte length. When data is
    /// null the buffer is treated as output-only and the element count defaults to
    /// <paramref name="gridDimX"/> — the natural size for a data-parallel kernel
    /// that writes one output per work item. Callers whose output size differs
    /// from the grid extent (e.g., reductions) should pass a pre-allocated buffer
    /// with the correct byte length as the data argument.
    /// </summary>
    private static BufferBinding[] BuildBufferBindings(
        (string bufferId, byte[]? data, int elementSize)[] buffers, long gridDimX)
    {
        var bindings = new BufferBinding[buffers.Length];
        for (int i = 0; i < buffers.Length; i++)
        {
            var buf = buffers[i];
            long length = buf.data != null
                ? buf.data.Length / buf.elementSize
                : gridDimX;
            bindings[i] = new BufferBinding
            {
                ParameterIndex = i + 1, // +1 for Index parameter at position 0
                BufferId = buf.bufferId,
                Length = length,
                ElementSize = buf.elementSize,
            };
        }
        return bindings;
    }

    /// <summary>
    /// Distribute work across ALL connected peers, splitting proportionally by TFLOPS.
    /// Each peer gets a chunk sized by its compute power. A 10 TFLOPS peer gets 5x
    /// the work of a 2 TFLOPS peer. Returns one dispatch per peer.
    ///
    /// The dataFactory callback generates the input data for a given chunk range.
    /// The kernel operates on elements [chunkStart..chunkStart+chunkSize).
    ///
    /// Usage:
    ///   var results = await p2pAccelerator.DispatchDistributedAsync(
    ///       typeof(MyKernels), nameof(MyKernels.VectorScale),
    ///       totalElements: 1_000_000,
    ///       elementSize: 4,
    ///       dataFactory: (start, count) => GenerateChunkData(start, count));
    /// </summary>
    public async Task<DistributedResult> DispatchDistributedAsync(
        Type kernelType, string methodName,
        long totalElements, int elementSize,
        Func<long, int, byte[]> dataFactory)
    {
        if (Dispatcher == null)
            throw new InvalidOperationException("Dispatcher not set.");

        var method = kernelType.GetMethod(methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new ArgumentException($"Kernel method not found: {kernelType.Name}.{methodName}");

        var peers = Peers.Where(p => p.IsConnected).ToList();
        if (peers.Count == 0)
            throw new InvalidOperationException("No connected peers for distributed dispatch.");

        // Split proportionally by TFLOPS
        double totalTflops = peers.Sum(p => p.Capabilities?.EstimatedTflops ?? 0.1);
        var chunks = new List<(RemotePeer peer, long start, int count)>();
        long assigned = 0;

        for (int i = 0; i < peers.Count; i++)
        {
            double peerTflops = peers[i].Capabilities?.EstimatedTflops ?? 0.1;
            int chunkCount = (i == peers.Count - 1)
                ? (int)(totalElements - assigned)
                : (int)(totalElements * (peerTflops / totalTflops));
            chunkCount = Math.Max(1, chunkCount);
            chunks.Add((peers[i], assigned, chunkCount));
            assigned += chunkCount;
        }

        // Dispatch all chunks in parallel
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = chunks.Select((chunk, idx) =>
        {
            var bufferId = $"dist_{idx}";
            var data = dataFactory(chunk.start, chunk.count);
            var request = P2PKernelSerializer.CreateDispatch(method, chunk.count);
            request.Buffers = new[]
            {
                new BufferBinding
                {
                    ParameterIndex = 1,
                    BufferId = bufferId,
                    Length = chunk.count,
                    ElementSize = elementSize,
                }
            };
            // Ship the chunk's input data alongside the dispatch - dispatcher will send it
            // to whichever peer it selects before firing the KernelDispatch message.
            var inputs = data != null
                ? (IReadOnlyDictionary<string, byte[]>)new Dictionary<string, byte[]> { [bufferId] = data }
                : null;
            return Dispatcher.DispatchAsync(request, inputs);
        }).ToList();

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        return new DistributedResult
        {
            TotalElements = totalElements,
            WallTimeMs = sw.Elapsed.TotalMilliseconds,
            Chunks = chunks.Select((c, i) => new DistributedChunk
            {
                PeerId = c.peer.PeerId,
                StartElement = c.start,
                ElementCount = c.count,
                Success = results[i].Success,
                DurationMs = results[i].DurationMs,
                Error = results[i].Error,
            }).ToArray(),
        };
    }

    /// <summary>
    /// Create a typed dispatch helper for a specific kernel method.
    /// Caches the method reference for repeated dispatch.
    ///
    /// Usage:
    ///   var dispatch = p2pAccelerator.CreateDispatcher(typeof(MyKernels), nameof(MyKernels.VectorAdd));
    ///   dispatch.Execute(1024, ("a", aData, 4), ("b", bData, 4), ("r", null, 4));
    /// </summary>
    public P2PDispatchHelper CreateDispatcher(Type kernelType, string methodName)
    {
        var method = kernelType.GetMethod(methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (method == null)
            throw new ArgumentException($"Kernel method not found: {kernelType.Name}.{methodName}");

        return new P2PDispatchHelper(this, method);
    }

    #endregion

    #region Accelerator Implementation

    /// <inheritdoc/>
    protected override AcceleratorStream CreateStreamInternal()
    {
        return new P2PStream(this);
    }

    /// <inheritdoc/>
    protected override void SynchronizeInternal() { }

    /// <inheritdoc/>
    protected override MemoryBuffer AllocateRawInternal(long length, int elementSize, int bitsPerElement)
    {
        return new P2PMemoryBuffer(this, length, elementSize, bitsPerElement);
    }

    /// <inheritdoc/>
    protected override int EstimateMaxActiveGroupsPerMultiprocessorInternal(
        Kernel kernel, int groupSize, int dynamicSharedMemorySizeInBytes) => 1;

    /// <inheritdoc/>
    protected override int EstimateGroupSizeInternal(
        Kernel kernel, int dynamicSharedMemorySizeInBytes, int maxGroupSize, out int minGridSize)
    {
        minGridSize = 1;
        return Math.Min(256, maxGroupSize);
    }

    /// <inheritdoc/>
    protected override int EstimateGroupSizeInternal(
        Kernel kernel, Func<int, int> computeSharedMemorySize, int maxGroupSize, out int minGridSize)
    {
        minGridSize = 1;
        return Math.Min(256, maxGroupSize);
    }

    /// <inheritdoc/>
    protected override PageLockScope<T> CreatePageLockFromPinnedInternal<T>(IntPtr ptr, long numElements) =>
        throw new NotSupportedException("Page locking not supported for P2P accelerator");

    /// <inheritdoc/>
    protected override void EnablePeerAccessInternal(Accelerator otherAccelerator) { }

    /// <inheritdoc/>
    protected override void DisablePeerAccessInternal(Accelerator otherAccelerator) { }

    /// <inheritdoc/>
    protected override bool CanAccessPeerInternal(Accelerator otherAccelerator) => false;

    /// <inheritdoc/>
    public override TExtension CreateExtension<TExtension, TExtensionProvider>(TExtensionProvider provider) =>
        throw new NotSupportedException("Extensions not supported for P2P accelerator");

    /// <inheritdoc/>
    protected override void OnBind() { }

    /// <inheritdoc/>
    protected override void OnUnbind() { }

    /// <inheritdoc/>
    protected override void DisposeAccelerator_SyncRoot(bool disposing)
    {
        if (disposing)
        {
            foreach (var peer in _peers.Values)
                peer.Disconnect();
            _peers.Clear();
        }
    }

    #endregion
}

/// <summary>
/// A kernel wrapper for P2P dispatch.
/// </summary>
public class P2PKernel : Kernel
{
    public new P2PCompiledKernel CompiledKernel { get; }

    public P2PKernel(P2PAccelerator accelerator, P2PCompiledKernel compiledKernel, MethodInfo? launcher)
        : base(accelerator, compiledKernel, launcher)
    {
        CompiledKernel = compiledKernel;
    }

    protected override void DisposeAcceleratorObject(bool disposing) { }
}

/// <summary>
/// Helper for repeated dispatch of a specific kernel method.
/// Caches the method reference and provides a clean API.
/// </summary>
public class P2PDispatchHelper
{
    private readonly P2PAccelerator _accelerator;
    private readonly MethodInfo _method;

    /// <summary>The kernel method name.</summary>
    public string MethodName => _method.Name;

    /// <summary>The kernel declaring type.</summary>
    public string TypeName => _method.DeclaringType?.Name ?? "";

    internal P2PDispatchHelper(P2PAccelerator accelerator, MethodInfo method)
    {
        _accelerator = accelerator;
        _method = method;
    }

    /// <summary>
    /// Dispatch the kernel to the swarm.
    /// </summary>
    public string Execute(long gridDimX,
        params (string bufferId, byte[]? data, int elementSize)[] buffers)
    {
        return _accelerator.DispatchToSwarm(_method, gridDimX, buffers);
    }
}

/// <summary>
/// Represents a remote peer device connected via WebRTC.
/// </summary>
public class RemotePeer
{
    public string PeerId { get; set; } = "";
    public AcceleratorType RemoteBackend { get; set; }
    public long MemorySize { get; set; }
    public bool IsConnected { get; set; }
    private int _pendingOperations;
    public int PendingOperations
    {
        get => _pendingOperations;
        set => Interlocked.Exchange(ref _pendingOperations, value);
    }
    public void IncrementPending() => Interlocked.Increment(ref _pendingOperations);
    public void DecrementPending() => Interlocked.Decrement(ref _pendingOperations);
    public PeerCapabilities? Capabilities { get; set; }
    public DateTime LastHeartbeat { get; set; } = DateTime.MinValue;
    internal P2PAccelerator? Accelerator { get; set; }

    // ── Performance History ──

    /// <summary>Total dispatches sent to this peer.</summary>
    public int DispatchCount { get; private set; }

    /// <summary>Number of successful dispatches.</summary>
    public int SuccessCount { get; private set; }

    /// <summary>Number of failed dispatches.</summary>
    public int FailureCount { get; private set; }

    /// <summary>Total execution time across all successful dispatches (ms).</summary>
    public double TotalDurationMs { get; private set; }

    /// <summary>Average execution time for successful dispatches (ms). 0 if none.</summary>
    public double AvgDurationMs => SuccessCount > 0 ? TotalDurationMs / SuccessCount : 0;

    /// <summary>Success rate (0.0 to 1.0). 1.0 if no dispatches yet.</summary>
    public double SuccessRate => DispatchCount > 0 ? (double)SuccessCount / DispatchCount : 1.0;

    /// <summary>
    /// Reputation score (0.0 to 1.0). Combines success rate with identity strength.
    /// Used by the dispatcher as a scoring factor.
    /// </summary>
    public double Reputation
    {
        get
        {
            // Base: success rate for established peers, moderate start for new peers.
            // New peers start at 0.7 (not 1.0) so identity bonus has room to differentiate.
            double base_ = DispatchCount >= 3 ? SuccessRate : 0.7;

            // Identity bonus: anonymous=0, identified=0.1, verified=0.2
            double identityBonus = 0;
            if (!string.IsNullOrEmpty(Capabilities?.PublicKey)) identityBonus = 0.1;
            if (!string.IsNullOrEmpty(Capabilities?.Fingerprint)) identityBonus = 0.2;

            return Math.Min(1.0, base_ + identityBonus);
        }
    }

    /// <summary>When this peer first connected.</summary>
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Record a successful dispatch.</summary>
    public void RecordSuccess(double durationMs)
    {
        Interlocked.Increment(ref _dispatchCountBacking);
        Interlocked.Increment(ref _successCountBacking);
        // Thread-safe double addition via CompareExchange
        double initial, updated;
        do
        {
            initial = TotalDurationMs;
            updated = initial + durationMs;
        } while (Interlocked.CompareExchange(ref _totalDurationMsBacking, updated, initial) != initial);
        DispatchCount = _dispatchCountBacking;
        SuccessCount = _successCountBacking;
        TotalDurationMs = _totalDurationMsBacking;
    }

    /// <summary>Record a failed dispatch.</summary>
    public void RecordFailure()
    {
        Interlocked.Increment(ref _dispatchCountBacking);
        Interlocked.Increment(ref _failureCountBacking);
        DispatchCount = _dispatchCountBacking;
        FailureCount = _failureCountBacking;
    }

    private int _dispatchCountBacking;
    private int _successCountBacking;
    private int _failureCountBacking;
    private double _totalDurationMsBacking;

    public void Disconnect()
    {
        IsConnected = false;
    }
}

/// <summary>
/// Result of a distributed dispatch across multiple peers.
/// </summary>
public record DistributedResult
{
    /// <summary>Total elements across all chunks.</summary>
    public long TotalElements { get; init; }

    /// <summary>Wall clock time for the entire distributed dispatch (ms).</summary>
    public double WallTimeMs { get; init; }

    /// <summary>Per-chunk results.</summary>
    public DistributedChunk[] Chunks { get; init; } = Array.Empty<DistributedChunk>();

    /// <summary>Number of successful chunks.</summary>
    public int SuccessCount => Chunks.Count(c => c.Success);

    /// <summary>Number of failed chunks.</summary>
    public int FailureCount => Chunks.Count(c => !c.Success);

    /// <summary>Aggregate throughput (elements/sec).</summary>
    public double ThroughputElemPerSec => WallTimeMs > 0 ? TotalElements / (WallTimeMs / 1000.0) : 0;
}

/// <summary>
/// One chunk of a distributed dispatch.
/// </summary>
public record DistributedChunk
{
    public string PeerId { get; init; } = "";
    public long StartElement { get; init; }
    public int ElementCount { get; init; }
    public bool Success { get; init; }
    public double DurationMs { get; init; }
    public string? Error { get; init; }
}
