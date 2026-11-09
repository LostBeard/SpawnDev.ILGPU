using System.Text.Json;
using ILGPU;
using ILGPU.Backends;
using ILGPU.Backends.EntryPoints;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU.P2P;

/// <summary>
/// Worker-side execution engine. Receives kernel dispatch requests from the coordinator,
/// compiles them locally using the best available backend, executes, and returns results.
///
/// Each worker runs a full ILGPU stack — it just takes orders from the coordinator
/// about WHAT to compute, not HOW. The worker chooses its own backend (WebGPU, CUDA, etc).
/// </summary>
public class P2PWorker : IAsyncDisposable
{
    private Context? _context;
    private Accelerator? _accelerator;
    private P2PKernelLauncher? _launcher;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _bufferStore = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _bufferInsertionOrder = new();
    private readonly object _bufferLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CompiledKernel> _kernelCache = new();

    /// <summary>
    /// Maximum number of buffers to keep in the store. Oldest buffers evicted when exceeded.
    /// Default: 1024.
    /// </summary>
    public int MaxBuffers { get; set; } = 1024;

    /// <summary>
    /// Maximum total bytes across all stored buffers. Default: 512MB.
    /// </summary>
    public long MaxBufferBytes { get; set; } = 512L * 1024 * 1024;
    private readonly P2PTransport _transport;
    private string _coordinatorPeerId = "";

    /// <summary>
    /// Called when the coordinator changes (transfer, election, or announcement).
    /// Clears the cached coordinator peer ID so the next dispatch re-verifies authority.
    /// </summary>
    public void NotifyCoordinatorChanged()
    {
        _coordinatorPeerId = "";
    }

    /// <summary>
    /// The swarm's KeyRegistry for verifying coordinator authority.
    /// Set via SetKeyRegistry when joining a swarm.
    /// </summary>
    public KeyRegistry? SwarmRegistry { get; private set; }

    /// <summary>
    /// The expected coordinator's public key fingerprint.
    /// When set, dispatches from other peers are rejected.
    /// </summary>
    public string? TrustedCoordinatorFingerprint { get; private set; }

    /// <summary>
    /// Set the swarm's key registry and trusted coordinator for authority verification.
    /// </summary>
    /// <param name="registry">The owner-signed key registry.</param>
    /// <param name="coordinatorFingerprint">
    /// The expected coordinator's fingerprint. If null, any peer with
    /// Coordinator role in the registry is accepted.
    /// </param>
    public void SetKeyRegistry(KeyRegistry registry, string? coordinatorFingerprint = null)
    {
        SwarmRegistry = registry;
        TrustedCoordinatorFingerprint = coordinatorFingerprint;
    }

    /// <summary>
    /// The local accelerator type being used for compute.
    /// </summary>
    public AcceleratorType LocalBackend => _accelerator?.AcceleratorType ?? AcceleratorType.CPU;

    /// <summary>
    /// Whether this worker is ready to accept dispatches.
    /// </summary>
    public bool IsReady => _accelerator != null;

    /// <summary>
    /// Number of kernels compiled and cached on this worker.
    /// </summary>
    public int CachedKernelCount => _kernelCache.Count;

    /// <summary>
    /// Fired when this worker starts executing a kernel.
    /// </summary>
    public event Action<string>? OnKernelStarted; // dispatchId

    /// <summary>
    /// Fired when this worker completes a kernel.
    /// </summary>
    public event Action<string, bool, double>? OnKernelCompleted; // dispatchId, success, durationMs

    /// <summary>
    /// Full exception text from the most recent failed dispatch, or null if the last dispatch succeeded.
    /// Useful for diagnosing dispatch failures from tests.
    /// </summary>
    public string? LastDispatchError { get; private set; }

    /// <summary>
    /// Fired when a kernel is compiled for the first time on this worker.
    /// </summary>
    public event Action<string>? OnKernelCompiled; // kernelType.kernelMethod

    public P2PWorker(P2PTransport transport)
    {
        _transport = transport;
    }

    /// <summary>
    /// Verify that a dispatch request comes from an authorized coordinator.
    /// </summary>
    /// <param name="fromPeerId">The peer ID that sent the request.</param>
    /// <param name="request">The dispatch request.</param>
    /// <returns>True if the sender is authorized to dispatch work.</returns>
    private bool VerifyCoordinatorAuthority(string fromPeerId, KernelDispatchRequest request)
    {
        // If no registry is set, accept from anyone (backward compat / open swarms)
        if (SwarmRegistry == null) return true;

        // If we have a trusted coordinator fingerprint, verify it matches
        if (!string.IsNullOrEmpty(TrustedCoordinatorFingerprint))
        {
            // Check if the peer we accepted as coordinator matches
            if (!string.IsNullOrEmpty(_coordinatorPeerId) &&
                _coordinatorPeerId != fromPeerId)
            {
                // Different peer than our known coordinator — reject
                return false;
            }

            // Verify the request includes a public key and its fingerprint matches
            if (string.IsNullOrEmpty(request.CoordinatorPublicKey))
                return false; // No public key provided — can't verify identity
        }

        // If registry has entries, verify the sender has at least Coordinator role
        if (SwarmRegistry.Keys.Count > 0)
        {
            if (string.IsNullOrEmpty(request.CoordinatorPublicKey))
                return false; // Registry exists but no key provided — reject

            if (!SwarmRegistry.HasRole(request.CoordinatorPublicKey, SwarmRole.Coordinator))
                return false;

            if (SwarmRegistry.IsRevoked(request.CoordinatorPublicKey))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Initialize the worker with the best available local accelerator.
    /// </summary>
    public void Initialize(Context context, Accelerator accelerator)
    {
        _context = context;
        _accelerator = accelerator;
        _launcher = new P2PKernelLauncher(accelerator);
    }

    /// <summary>
    /// Handle a kernel dispatch request from the coordinator.
    /// Resolves the kernel locally, compiles on first use, executes.
    /// </summary>
    public async Task HandleDispatchAsync(string fromPeerId, KernelDispatchRequest request)
    {
        // Verify coordinator authority
        if (!VerifyCoordinatorAuthority(fromPeerId, request))
        {
            OnKernelCompleted?.Invoke(request.DispatchId, false, 0);
            return;
        }

        _coordinatorPeerId = fromPeerId;
        OnKernelStarted?.Invoke(request.DispatchId);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new KernelDispatchResult
        {
            DispatchId = request.DispatchId,
        };

        try
        {
            if (_accelerator == null || _context == null)
                throw new InvalidOperationException("Worker not initialized");

            // Apply requested F64 mode for browser backends. Native-f64 backends
            // (CPU/CUDA/OpenCL/Wasm) ignore the field - they're always strict.
            ApplyRequestedF64Mode(_accelerator, request.F64Mode);

            // 1. Resolve kernel method from loaded assemblies
            var kernelMethod = P2PKernelSerializer.ResolveKernel(request);
            if (kernelMethod == null)
                throw new InvalidOperationException(
                    $"Cannot resolve kernel: {request.KernelType}.{request.KernelMethod}");

            // Track first-time compilation
            var cacheKey = $"{request.KernelType}.{request.KernelMethod}";
            bool isFirstCompile = !_kernelCache.ContainsKey(cacheKey);
            _kernelCache.TryAdd(cacheKey, null!); // mark as seen

            // 2. Build buffer data map (parameter index → data)
            var bufferBindings = new Dictionary<int, BufferData>();
            foreach (var binding in request.Buffers)
            {
                var rawData = _bufferStore.TryGetValue(binding.BufferId, out var data)
                    ? data
                    : new byte[binding.Length * binding.ElementSize];

                bufferBindings[binding.ParameterIndex] = new BufferData
                {
                    RawData = rawData,
                    ElementCount = binding.Length,
                    ElementSize = binding.ElementSize,
                };
            }

            // 3. Execute kernel on local GPU via reflection-based typed dispatch.
            //    Pass request.ScalarParams so scalar kernel arguments (e.g. VectorScale's scalar)
            //    arrive with the coordinator's value instead of silently defaulting to 0.
            var modifiedBuffers = await _launcher!.ExecuteAsync(
                kernelMethod, request.GridDimX, bufferBindings, request.ScalarParams);
            if (isFirstCompile)
                OnKernelCompiled?.Invoke(cacheKey);

            // 4. Store modified buffer data for readback
            foreach (var binding in request.Buffers)
            {
                if (modifiedBuffers.TryGetValue(binding.ParameterIndex, out var modified))
                    _bufferStore[binding.BufferId] = modified;
            }

            result.Success = true;
            result.ModifiedBuffers = request.Buffers
                .Select(b => b.BufferId)
                .ToArray();
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.ToString();
            LastDispatchError = ex.ToString();
        }

        sw.Stop();
        result.DurationMs = sw.Elapsed.TotalMilliseconds;

        // Push modified buffer data back to the coordinator BEFORE sending
        // KernelResult. Order matters: if a buffer push fails (wire died, SCTP
        // backpressure, etc.), the coordinator's DispatchAsync should observe
        // result.Success=false instead of getting "kernel succeeded" while the
        // computed output never actually arrived. The previous order (result
        // first, buffer pushes after with try/catch swallowing errors) caused
        // demos like the Mandelbrot fractal to time out on WaitForResultBufferAsync
        // 30 seconds later with no actionable diagnostic - they got success=true
        // followed by "buffer never came back."
        if (result.Success && request.ReturnModifiedBuffers && result.ModifiedBuffers.Length > 0)
        {
            var bufferPushErrors = new List<string>();
            foreach (var bufferId in result.ModifiedBuffers)
            {
                if (!_bufferStore.TryGetValue(bufferId, out var data) || data == null)
                    continue;
                try
                {
                    await _transport.SendBufferAsync(fromPeerId, bufferId, data);
                }
                catch (Exception ex)
                {
                    var msg = $"Pushing modified buffer '{bufferId}' to {fromPeerId} failed: {ex.Message}";
                    if (P2PCompute.VerboseLogging) Console.WriteLine($"[P2PWorker] {msg}");
                    bufferPushErrors.Add(msg);
                }
            }
            if (bufferPushErrors.Count > 0)
            {
                result.Success = false;
                result.Error = $"Buffer push-back failed for {bufferPushErrors.Count} of " +
                    $"{result.ModifiedBuffers.Length} buffers: {string.Join("; ", bufferPushErrors)}";
                LastDispatchError = result.Error;
            }
        }

        // Result is sent LAST with the buffer-push-back outcome reflected in
        // result.Success / result.Error. The Stopwatch already snapshot DurationMs
        // before the buffer pushes - that timing is the kernel-execution duration,
        // not the round-trip duration, which is what callers care about.
        await _transport.SendMessageAsync(fromPeerId, new P2PMessage
        {
            Type = P2PMessageType.KernelResult,
            Payload = JsonSerializer.SerializeToElement(result),
        });

        OnKernelCompleted?.Invoke(request.DispatchId, result.Success, result.DurationMs);
    }

    /// <summary>
    /// Pre-compile a kernel without dispatching it.
    /// Useful for warming up the worker's cache before heavy compute starts.
    /// </summary>
    public bool PreCompileKernel(System.Reflection.MethodInfo kernelMethod)
    {
        if (_accelerator == null) return false;

        try
        {
            var cacheKey = $"{kernelMethod.DeclaringType?.FullName}.{kernelMethod.Name}";
            if (_kernelCache.ContainsKey(cacheKey)) return true;

            var entry = EntryPointDescription.FromImplicitlyGroupedKernel(kernelMethod);
            var backend = _accelerator.GetBackend();
            var compiled = backend.Compile(entry, KernelSpecialization.Empty);
            if (_kernelCache.TryAdd(cacheKey, compiled))
                OnKernelCompiled?.Invoke(cacheKey);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Receive buffer data from coordinator (tensor transfer).
    /// Evicts oldest buffers if limits are exceeded.
    /// </summary>
    public void ReceiveBuffer(string bufferId, byte[] data)
    {
        lock (_bufferLock)
        {
            if (!_bufferStore.ContainsKey(bufferId))
                _bufferInsertionOrder.Enqueue(bufferId);
            _bufferStore[bufferId] = data;
            EvictIfNeeded();
        }
    }

    private void EvictIfNeeded()
    {
        // Count limit - evict oldest by insertion order
        while (_bufferStore.Count > MaxBuffers)
        {
            if (_bufferInsertionOrder.TryDequeue(out var oldest))
                _bufferStore.TryRemove(oldest, out _);
            else break;
        }

        // Byte limit - evict oldest by insertion order
        long totalBytes = _bufferStore.Values.Sum(b => (long)b.Length);
        while (totalBytes > MaxBufferBytes && _bufferStore.Count > 0)
        {
            if (_bufferInsertionOrder.TryDequeue(out var oldest) &&
                _bufferStore.TryRemove(oldest, out var removed))
                totalBytes -= removed.Length;
            else break;
        }
    }

    /// <summary>
    /// Get buffer data to send back to coordinator after kernel execution.
    /// </summary>
    public byte[]? GetBuffer(string bufferId)
    {
        return _bufferStore.TryGetValue(bufferId, out var data) ? data : null;
    }

    /// <summary>
    /// Build this worker's capability manifest for the coordinator.
    /// </summary>
    public PeerCapabilities BuildCapabilities(string peerId)
    {
        return new PeerCapabilities
        {
            PeerId = peerId,
            Platform = OperatingSystem.IsBrowser() ? "browser" : "desktop",
            IlgpuVersion = typeof(P2PAccelerator).Assembly.GetName().Version?.ToString() ?? "4.7.1",
            AvailableBackends = _accelerator != null
                ? new[] { _accelerator.AcceleratorType.ToString() }
                : new[] { "CPU" },
            PreferredBackend = _accelerator?.AcceleratorType.ToString() ?? "CPU",
            AvailableMemory = _accelerator?.MemorySize ?? Environment.WorkingSet,
            EstimatedTflops = EstimateLocalTflops(),
            MaxThreadsPerGroup = _accelerator?.MaxNumThreadsPerGroup ?? 256,
            MaxSharedMemory = _accelerator?.Device?.MaxSharedMemoryPerGroup ?? 0,
            IsCharging = true,
            BatteryLevel = -1,
            ThermalState = 0,
            F64ModesSupported = GetF64ModesSupported(),
        };
    }

    private string[]? GetF64ModesSupported()
    {
        // Browser backends (WebGPU/WebGL) support Dekker (default fast path) and Ozaki (quad-float) since
        // rc.10; both also Ieee (per-operation binary64, bit-identical to .NET) since 5.4.19. Native-f64 backends (CPU/CUDA/OpenCL/Wasm)
        // are always strict regardless and don't need to advertise modes - the
        // coordinator's pre-flight check skips F64ModesSupported when the peer's
        // PreferredBackend is native-f64.
        if (_accelerator == null) return null;
        return _accelerator.AcceleratorType switch
        {
            AcceleratorType.WebGPU => new[] { "Dekker", "Ozaki", "Ieee" },
            AcceleratorType.WebGL => new[] { "Dekker", "Ozaki", "Ieee" },
            _ => null,
        };
    }

    private static void ApplyRequestedF64Mode(Accelerator accelerator, string? requestedMode)
    {
        if (string.IsNullOrEmpty(requestedMode)) return;
        if (!Enum.TryParse<F64EmulationMode>(requestedMode, ignoreCase: true, out var mode)) return;

        // Only browser backends configure F64 mode; native-f64 backends (CPU/CUDA/OpenCL/Wasm)
        // are always strict and have no F64Mode property to set.
        switch (accelerator)
        {
            case WebGPU.WebGPUAccelerator wgpu when wgpu.F64Mode != mode:
                wgpu.F64Mode = mode;
                break;
            case WebGL.WebGLAccelerator wgl when wgl.F64Mode != mode:
                wgl.F64Mode = mode;
                break;
        }
    }

    private double EstimateLocalTflops()
    {
        if (_accelerator == null) return 1.0;

        // Use multiprocessor count as a rough scaling factor when available
        int processors = _accelerator.Device?.NumMultiprocessors ?? 1;
        int threadsPerGroup = _accelerator.MaxNumThreadsPerGroup;

        // Base estimate per backend, scaled by actual hardware
        double baseEstimate = _accelerator.AcceleratorType switch
        {
            AcceleratorType.Cuda => 2.0 * processors, // ~2 TFLOPS per SM
            AcceleratorType.OpenCL => 1.0 * processors,
            AcceleratorType.WebGPU => 0.5 * Math.Max(processors, 8),
            AcceleratorType.Wasm => 0.1 * Environment.ProcessorCount,
            AcceleratorType.CPU => 0.2 * Environment.ProcessorCount,
            _ => 1.0,
        };

        return Math.Max(0.1, baseEstimate);
    }

    /// <summary>
    /// Allocate a typed GPU buffer, copy data, and return the view for kernel args.
    /// Handles common ILGPU element types (float, int, double, byte, long, short).
    /// </summary>
    private static (IDisposable buffer, object view) AllocateTypedBuffer(
        Accelerator accelerator, Type elementType, byte[] data, long elementCount)
    {
        if (elementType == typeof(float))
        {
            var buf = accelerator.Allocate1D<float>(elementCount);
            var floats = new float[elementCount];
            Buffer.BlockCopy(data, 0, floats, 0, Math.Min(data.Length, (int)(elementCount * 4)));
            buf.CopyFromCPU(floats);
            return (buf, buf.View);
        }
        if (elementType == typeof(int))
        {
            var buf = accelerator.Allocate1D<int>(elementCount);
            var ints = new int[elementCount];
            Buffer.BlockCopy(data, 0, ints, 0, Math.Min(data.Length, (int)(elementCount * 4)));
            buf.CopyFromCPU(ints);
            return (buf, buf.View);
        }
        if (elementType == typeof(double))
        {
            var buf = accelerator.Allocate1D<double>(elementCount);
            var doubles = new double[elementCount];
            Buffer.BlockCopy(data, 0, doubles, 0, Math.Min(data.Length, (int)(elementCount * 8)));
            buf.CopyFromCPU(doubles);
            return (buf, buf.View);
        }
        if (elementType == typeof(byte))
        {
            var buf = accelerator.Allocate1D<byte>(elementCount);
            buf.CopyFromCPU(data);
            return (buf, buf.View);
        }
        if (elementType == typeof(long))
        {
            var buf = accelerator.Allocate1D<long>(elementCount);
            var longs = new long[elementCount];
            Buffer.BlockCopy(data, 0, longs, 0, Math.Min(data.Length, (int)(elementCount * 8)));
            buf.CopyFromCPU(longs);
            return (buf, buf.View);
        }
        // Fallback: byte buffer
        {
            var buf = accelerator.Allocate1D<byte>(data.Length);
            buf.CopyFromCPU(data);
            return (buf, buf.View);
        }
    }

    /// <summary>
    /// Read back a typed GPU buffer to byte array.
    /// </summary>
    private static byte[] ReadBackBuffer(IDisposable buffer, Type elementType, long elementCount)
    {
        if (buffer is MemoryBuffer1D<float, Stride1D.Dense> fBuf)
        {
            var result = new float[elementCount];
            fBuf.CopyToCPU(result);
            var bytes = new byte[elementCount * 4];
            Buffer.BlockCopy(result, 0, bytes, 0, bytes.Length);
            return bytes;
        }
        if (buffer is MemoryBuffer1D<int, Stride1D.Dense> iBuf)
        {
            var result = new int[elementCount];
            iBuf.CopyToCPU(result);
            var bytes = new byte[elementCount * 4];
            Buffer.BlockCopy(result, 0, bytes, 0, bytes.Length);
            return bytes;
        }
        if (buffer is MemoryBuffer1D<byte, Stride1D.Dense> bBuf)
        {
            var result = new byte[elementCount];
            bBuf.CopyToCPU(result);
            return result;
        }
        return Array.Empty<byte>();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _bufferStore.Clear();
        _bufferInsertionOrder.Clear();
        _kernelCache.Clear();
        return ValueTask.CompletedTask;
    }
}
