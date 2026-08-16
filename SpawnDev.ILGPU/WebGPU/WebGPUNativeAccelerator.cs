// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGPU
//                 WebGPU Compute Library for Blazor WebAssembly
//
// File: WebGPUNativeAccelerator.cs
// ---------------------------------------------------------------------------------------

using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// Represents a WebGPU accelerator for GPU compute in the browser.
    /// </summary>
    public sealed class WebGPUNativeAccelerator : IDisposable
    {
        #region Static

        /// <summary>
        /// Creates a WebGPU accelerator asynchronously.
        /// </summary>
        public static async Task<WebGPUNativeAccelerator> CreateAsync(WebGPUDevice device)
        {
            var accelerator = new WebGPUNativeAccelerator(device);
            await accelerator.InitializeAsync();
            return accelerator;
        }

        /// <summary>
        /// Creates a WebGPU accelerator from an externally-provided GPUDevice.
        /// This is used when sharing a device with another library (e.g., ONNX Runtime Web)
        /// that has already created its own GPUDevice. Skips adapter probing and device
        /// creation — uses the provided device directly.
        /// </summary>
        /// <param name="externalDevice">An existing GPUDevice (e.g., from ORT's env.webgpu.device).</param>
        public static WebGPUNativeAccelerator CreateFromExternalDevice(GPUDevice externalDevice)
        {
            if (externalDevice == null)
                throw new ArgumentNullException(nameof(externalDevice));

            var accelerator = new WebGPUNativeAccelerator(externalDevice);
            return accelerator;
        }

        #endregion

        #region Instance

        private static readonly GPUCommandBuffer[] _submitArray = new GPUCommandBuffer[1];
        private GPUDevice? _gpuDevice;
        private GPUQueue? _queue;
        private bool _isInitialized;
        private bool _disposed;

        // Accumulated GPU validation errors surfaced via ThrowIfGpuErrors()
        private readonly List<string> _pendingGpuErrors = new();

        // Tracks in-flight CheckShaderAsync tasks. SynchronizeAsync awaits these before
        // calling ThrowIfGpuErrors so a fire-and-forget shader-validation error from THIS
        // dispatch is surfaced HERE, not in the next caller's sync. Without tracking,
        // the test's `await accelerator.SynchronizeAsync()` could return before
        // CheckShaderAsync calls AddShaderError, and the queued error would leak to the
        // next test (rc.13 ILGPUReduceHalfTest seeing a stale 12-binding error from the
        // prior ManyViews_11Views test).
        private readonly List<System.Threading.Tasks.Task> _pendingShaderChecks = new();
        private readonly object _pendingShaderChecksLock = new();

        internal void TrackShaderCheck(System.Threading.Tasks.Task task)
        {
            lock (_pendingShaderChecksLock)
            {
                _pendingShaderChecks.Add(task);
            }
        }

        internal async System.Threading.Tasks.Task DrainShaderChecksAsync()
        {
            System.Threading.Tasks.Task[] snapshot;
            lock (_pendingShaderChecksLock)
            {
                if (_pendingShaderChecks.Count == 0) return;
                snapshot = _pendingShaderChecks.ToArray();
                _pendingShaderChecks.Clear();
            }
            try { await System.Threading.Tasks.Task.WhenAll(snapshot); }
            catch { /* individual task exceptions are surfaced via AddShaderError -> ThrowIfGpuErrors */ }
        }

        /// <summary>
        /// Called by CheckShaderAsync to feed shader compilation errors into the pending
        /// error list. These errors are surfaced as hard failures on the next Synchronize() call.
        /// </summary>
        internal void AddShaderError(string errorMessage)
        {
            _pendingGpuErrors.Add(errorMessage);
        }

        /// <summary>
        /// True once the GPU device has been lost (driver crash, GPU reset, etc.).
        /// All subsequent dispatch and synchronize calls will throw.
        /// </summary>
        public bool IsDeviceLost { get; private set; }

        /// <summary>
        /// Fired when the GPU device is lost. Parameters are (reason, message).
        /// Reason is one of: "destroyed", "unknown".
        /// </summary>
        public event Action<string, string>? DeviceLost;

        // Known-benign error substrings that should be logged but not propagated
        private static readonly string[] _benignErrorSubstrings = new[]
        {
            "powerPreference",
        };

        // Shader cache: key is (WGSL source, override-constants signature), value is compiled shader.
        // The composite key avoids allocating a full-WGSL-length string copy per lookup (the old
        // $"{wgsl}\n__CONSTANTS__:..." concat ran on every dispatch).
        private readonly Dictionary<(string Wgsl, string Constants), WebGPUComputeShader> _shaderCache = new();

        /// <summary>
        /// Constructs a new WebGPU accelerator from a WebGPUDevice (adapter-based, needs InitializeAsync).
        /// </summary>
        private WebGPUNativeAccelerator(WebGPUDevice device)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
        }

        /// <summary>
        /// Constructs a WebGPU accelerator from an existing GPUDevice (external device injection).
        /// The device is already initialized — no adapter probing or device creation needed.
        /// </summary>
        private WebGPUNativeAccelerator(GPUDevice externalDevice)
        {
            _gpuDevice = externalDevice ?? throw new ArgumentNullException(nameof(externalDevice));
            _queue = externalDevice.Queue;
            _isInitialized = true;

            // Read device features
            EnabledFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var features = externalDevice.Features;
                foreach (var featureName in new[] { "shader-f16", "subgroups", "timestamp-query" })
                {
                    // Honor ForceEmulatedF16 even on the external-device path so EnabledFeatures
                    // stays consistent with HasShaderF16.
                    if (featureName == "shader-f16" && Backend.WebGPUBackend.ForceEmulatedF16)
                        continue;
                    if (features.Has(featureName))
                        EnabledFeatures.Add(featureName);
                }
            }
            catch { }

            // Listen for uncaptured GPU errors
            _gpuDevice.OnUncapturedError += OnGPUUncapturedError;

            // Monitor device loss (resolves once when the device becomes invalid)
            _ = MonitorDeviceLostAsync();

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log("[WebGPU] Accelerator created from external GPUDevice (shared with ORT)");
        }

        /// <summary>
        /// Initializes the accelerator by requesting the GPU device with detected features.
        /// </summary>
        private async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            var adapter = Device.Adapter;
            var requestedFeatures = Device.SupportedFeatures.ToList();
            // Test-only: when ForceEmulatedF16 is set, never request shader-f16 from the
            // adapter even if it supports it. This drops the device onto the emulation
            // codegen path for test verification.
            if (Backend.WebGPUBackend.ForceEmulatedF16)
                requestedFeatures.Remove("shader-f16");

            // Query the adapter's actual limits so we can request the maximum supported.
            // WebGPU defaults are conservative (e.g. 256 for workgroup size, 8 for storage buffers).
            // ILGPU reads adapter limits to determine kernel compilation parameters like workgroup_size,
            // so we MUST request those same limits when creating the device — otherwise the device
            // falls back to defaults and rejects shaders compiled for the adapter's capabilities.
            int maxStorageBuffers = 10;
            int maxComputeInvocations = 256;
            int maxWorkgroupSizeX = 256;
            int maxWorkgroupSizeY = 256;
            int maxWorkgroupSizeZ = 64;
            int maxWorkgroupStorageSize = 16384;
            long maxBufferSize = 268435456;
            long maxStorageBufferBindingSize = 134217728; // WebGPU spec default: 128 MiB
            try
            {
                using var adapterLimits = adapter.Limits;
                maxStorageBuffers = adapterLimits.MaxStorageBuffersPerShaderStage ?? 10;
                maxComputeInvocations = (int)(adapterLimits.MaxComputeInvocationsPerWorkgroup ?? 256);
                maxWorkgroupSizeX = (int)(adapterLimits.MaxComputeWorkgroupSizeX ?? 256);
                maxWorkgroupSizeY = (int)(adapterLimits.MaxComputeWorkgroupSizeY ?? 256);
                maxWorkgroupSizeZ = (int)(adapterLimits.MaxComputeWorkgroupSizeZ ?? 64);
                maxWorkgroupStorageSize = (int)(adapterLimits.MaxComputeWorkgroupStorageSize ?? 16384);
                maxBufferSize = (long)(adapterLimits.MaxBufferSize ?? 268435456);
                maxStorageBufferBindingSize = adapterLimits.MaxStorageBufferBindingSize ?? 134217728;
            }
            catch
            {
                // Limits query failed — use fallback
            }
            MaxStorageBuffersPerShaderStage = maxStorageBuffers;
            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Adapter limits: maxStorageBuffers={maxStorageBuffers}, maxComputeInvocations={maxComputeInvocations}, maxWorkgroupSizeX={maxWorkgroupSizeX}, maxWorkgroupStorageSize={maxWorkgroupStorageSize}, maxStorageBufferBindingSize={maxStorageBufferBindingSize}");

            // Use Dictionary for RequiredLimits to ensure reliable JS interop serialization.
            // Anonymous objects may not serialize correctly through BlazorJS's interop layer.
            var requiredLimits = new Dictionary<string, object>
            {
                ["maxStorageBuffersPerShaderStage"] = maxStorageBuffers,
                ["maxComputeInvocationsPerWorkgroup"] = maxComputeInvocations,
                ["maxComputeWorkgroupSizeX"] = maxWorkgroupSizeX,
                ["maxComputeWorkgroupSizeY"] = maxWorkgroupSizeY,
                ["maxComputeWorkgroupSizeZ"] = maxWorkgroupSizeZ,
                ["maxComputeWorkgroupStorageSize"] = maxWorkgroupStorageSize,
                ["maxBufferSize"] = maxBufferSize,
                ["maxStorageBufferBindingSize"] = maxStorageBufferBindingSize,
            };

            // Request device with detected features and the adapter's max storage buffer limit.
            try
            {
                var descriptor = new GPUDeviceDescriptor
                {
                    RequiredFeatures = requestedFeatures.Count > 0 ? requestedFeatures : null,
                    RequiredLimits = requiredLimits
                };
                _gpuDevice = await adapter.RequestDevice(descriptor);
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Device created with features + limits (maxStorage={maxStorageBuffers})");
            }
            catch (Exception ex)
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Device creation with features failed: {ex.Message}");
                // Fall back: try without features but with limits
                try
                {
                    var descriptor = new GPUDeviceDescriptor
                    {
                        RequiredLimits = requiredLimits
                    };
                    _gpuDevice = await adapter.RequestDevice(descriptor);
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Device created with limits only (maxStorage={maxStorageBuffers})");
                    // Clear features since we couldn't request them
                    requestedFeatures.Clear();
                }
                catch (Exception ex2)
                {
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPU] Device creation with limits failed: {ex2.Message}");
                    // Fall back to fully default device
                    _gpuDevice = await adapter.RequestDevice();
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log("[WebGPU] Device created with defaults (no limits override)");
                    requestedFeatures.Clear();
                }
            }
            if (_gpuDevice == null)
                throw new InvalidOperationException("Failed to request WebGPU device");

            // Store actually enabled features
            EnabledFeatures = new HashSet<string>(requestedFeatures, StringComparer.OrdinalIgnoreCase);

            // Listen for uncaptured GPU errors (e.g., Invalid ComputePipeline, validation errors)
            _gpuDevice.OnUncapturedError += OnGPUUncapturedError;

            // Monitor device loss (resolves once when the device becomes invalid)
            _ = MonitorDeviceLostAsync();

            _queue = _gpuDevice.Queue;
            _isInitialized = true;
        }

        /// <summary>
        /// Handles uncaptured GPU validation errors.
        /// </summary>
        private void OnGPUUncapturedError(GPUUncapturedErrorEvent e)
        {
            try
            {
                var message = e.Error?.Message ?? "Unknown GPU error";
                if (WebGPUBackend.VerboseLogging)
                    WebGPUBackend.Log($"[WebGPU ERROR] {message}");

                if (WebGPUBackend.VerboseLogging && _lastCompiledWGSL != null)
                {
                    WebGPUBackend.Log($"[WebGPU ERROR] WGSL source ({_lastCompiledWGSL.Length} chars):");
                    WebGPUBackend.Log(_lastCompiledWGSL);
                }

                // Capture non-benign errors for propagation via ThrowIfGpuErrors()
                bool isBenign = false;
                foreach (var substr in _benignErrorSubstrings)
                {
                    if (message.Contains(substr, StringComparison.OrdinalIgnoreCase))
                    {
                        isBenign = true;
                        break;
                    }
                }
                if (!isBenign)
                    _pendingGpuErrors.Add(message);
            }
            catch
            {
                // Swallow any exceptions to avoid triggering Blazor's unhandled error banner
            }
        }

        /// <summary>
        /// Monitors the GPU device's lost promise. Resolves once when the device becomes invalid.
        /// Only fires the DeviceLost event for unexpected loss — not when the device is
        /// intentionally destroyed via Dispose().
        /// </summary>
        private async Task MonitorDeviceLostAsync()
        {
            try
            {
                var info = await _gpuDevice!.Lost;
                var reason = info.Reason ?? "unknown";
                var message = info.Message ?? "GPU device lost";

                // Don't treat intentional disposal as a device loss error
                if (_disposed || reason == "destroyed")
                    return;

                IsDeviceLost = true;
                if (WebGPUBackend.VerboseLogging)
                    WebGPUBackend.Log($"[WebGPU] Device lost: reason={reason}, message={message}");
                DeviceLost?.Invoke(reason, message);
            }
            catch
            {
                // Swallow — if the promise fails, we can't monitor device loss
            }
        }

        /// <summary>
        /// Throws an <see cref="InvalidOperationException"/> if any GPU validation errors
        /// have been captured since the last call. Clears the error list after throwing.
        /// Called by <see cref="WebGPUAccelerator.SynchronizeInternal"/> to surface GPU
        /// errors to the caller (e.g. test framework) instead of silently swallowing them.
        /// </summary>
        internal void ThrowIfGpuErrors()
        {
            if (IsDeviceLost)
                throw new InvalidOperationException("WebGPU device has been lost and cannot accept commands.");

            if (_pendingGpuErrors.Count == 0)
                return;

            var errors = new List<string>(_pendingGpuErrors);
            _pendingGpuErrors.Clear();

            var combined = string.Join("\n", errors);
            throw new InvalidOperationException(
                $"[WebGPU] {errors.Count} GPU error(s) during dispatch:\n{combined}");
        }

        /// <summary>
        /// The last WGSL source that was compiled, for error debugging.
        /// </summary>
        internal string? _lastCompiledWGSL;

        #endregion

        #region Properties

        /// <summary>
        /// Returns the parent WebGPU device. Null when using an externally-provided GPUDevice.
        /// </summary>
        public WebGPUDevice? Device { get; }

        /// <summary>
        /// Returns the native GPU device.
        /// </summary>
        public GPUDevice? NativeDevice => _gpuDevice;

        /// <summary>
        /// Returns the GPU command queue.
        /// </summary>
        public GPUQueue? Queue => _queue;

        /// <summary>
        /// Returns whether the accelerator is initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Gets the set of WebGPU features that were successfully enabled on this device.
        /// </summary>
        public HashSet<string> EnabledFeatures { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns true if shader-f16 is enabled on this device AND the test-only
        /// override <see cref="Backend.WebGPUBackend.ForceEmulatedF16"/> is not set.
        /// </summary>
        public bool HasShaderF16 => !Backend.WebGPUBackend.ForceEmulatedF16 && EnabledFeatures.Contains("shader-f16");

        /// <summary>Returns true if subgroups are enabled on this device.</summary>
        public bool HasSubgroups => EnabledFeatures.Contains("subgroups");

        /// <summary>Returns true if timestamp queries are enabled on this device.</summary>
        public bool HasTimestampQuery => EnabledFeatures.Contains("timestamp-query");

        /// <summary>
        /// Maximum number of storage buffer bindings per shader stage.
        /// WebGPU spec default: 8. Chrome typically supports 10.
        /// Kernels with more ArrayView parameters than this limit will fail at pipeline creation.
        /// Use structs to pack related data into fewer bindings.
        /// </summary>
        public int MaxStorageBuffersPerShaderStage { get; private set; } = 10;

        /// <summary>
        /// Optional callback to flush pending batched ILGPU kernel dispatches.
        /// Set by WebGPUAccelerator to allow WebGPUBuffer readback operations
        /// to auto-flush before copying, ensuring kernel results are available.
        /// </summary>
        internal Action? FlushPendingCommands { get; set; }

        #endregion

        #region Buffer Methods

        /// <summary>
        /// Allocates a GPU buffer with the specified size.
        /// </summary>
        public WebGPUBuffer<T> Allocate<T>(long length) where T : unmanaged
        {
            EnsureInitialized();
            return new WebGPUBuffer<T>(this, length);
        }

        /// <summary>
        /// Allocates a GPU buffer and copies data from an array.
        /// </summary>
        public WebGPUBuffer<T> Allocate<T>(T[] data) where T : unmanaged
        {
            EnsureInitialized();
            var buffer = new WebGPUBuffer<T>(this, data.Length);
            buffer.CopyFromHost(data);
            return buffer;
        }

        #endregion

        #region Compute Methods

        /// <summary>
        /// Creates a compute shader from WGSL source code.
        /// </summary>
        public WebGPUComputeShader CreateComputeShader(
            string wgslSource,
            string entryPoint = "main",
            Dictionary<string, object>? overrideConstants = null,
            string? label = null)
        {
            EnsureInitialized();
            return new WebGPUComputeShader(this, wgslSource, entryPoint, overrideConstants, label);
        }

        /// <summary>
        /// Gets a cached compute shader or creates a new one.
        /// When caching is enabled, shaders are reused across kernel invocations.
        /// Override constants are part of the cache key since they affect pipeline creation.
        /// </summary>
        public WebGPUComputeShader GetOrCreateComputeShader(
            string wgslSource,
            string entryPoint = "main",
            Dictionary<string, object>? overrideConstants = null,
            string? label = null)
        {
            if (!WebGPUBackend.EnableShaderCaching)
                return CreateComputeShader(wgslSource, entryPoint, overrideConstants, label);

            // Build cache key: (WGSL source, sorted override-constants signature). Using a composite
            // key instead of concatenating the constants onto the WGSL avoids allocating a full
            // WGSL-length string copy on every dispatch (constants string is small / "" when none).
            string constantsKey = "";
            if (overrideConstants != null && overrideConstants.Count > 0)
            {
                constantsKey = string.Join(",",
                    overrideConstants.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            }
            var cacheKey = (wgslSource, constantsKey);

            if (_shaderCache.TryGetValue(cacheKey, out var cached))
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log("[WebGPU] Using cached shader");
                return cached;
            }

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log("[WebGPU] Creating and caching new shader");
            var shader = CreateComputeShader(wgslSource, entryPoint, overrideConstants, label);
            _shaderCache[cacheKey] = shader;
            return shader;
        }

        /// <summary>
        /// Runs a compute shader with the specified dispatch size.
        /// </summary>
        public void Dispatch(WebGPUComputeShader shader, uint workgroupCountX, uint workgroupCountY = 1, uint workgroupCountZ = 1)
        {
            EnsureInitialized();

            var encoder = _gpuDevice!.CreateCommandEncoder();
            var passEncoder = encoder.BeginComputePass();

            passEncoder.SetPipeline(shader.Pipeline!);

            if (shader.BindGroup != null)
            {
                passEncoder.SetBindGroup(0, shader.BindGroup);
            }

            passEncoder.DispatchWorkgroups(workgroupCountX, workgroupCountY, workgroupCountZ);
            passEncoder.End();

            var commandBuffer = encoder.Finish();
            _submitArray[0] = commandBuffer;
            _queue!.Submit(_submitArray);

            // Clean up
            commandBuffer.Dispose();
            passEncoder.Dispose();
            encoder.Dispose();
        }

        #endregion

        #region Helpers

        private void EnsureInitialized()
        {
            if (!_isInitialized)
                throw new InvalidOperationException(
                    "Accelerator not initialized. Call InitializeAsync first.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Dispose cached shaders
            foreach (var shader in _shaderCache.Values)
            {
                shader.Dispose();
            }
            _shaderCache.Clear();

            _gpuDevice?.Destroy();
            _gpuDevice?.Dispose();
        }

        #endregion
    }
}
