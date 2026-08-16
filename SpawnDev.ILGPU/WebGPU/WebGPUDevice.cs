// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGPU
//                 WebGPU Compute Library for Blazor WebAssembly
//
// File: WebGPUDevice.cs
// ---------------------------------------------------------------------------------------

using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;
using System.Collections.Immutable;

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// Represents a WebGPU device available in the browser.
    /// </summary>
    public sealed class WebGPUDevice : IDisposable
    {
        #region Static

        /// <summary>
        /// Checks if WebGPU is supported in the current browser.
        /// </summary>
        public static bool IsSupported
        {
            get
            {
                try
                {
                    var navigator = SpawnJSRuntime.Instance.Get<Navigator>("navigator");
                    return navigator.Gpu != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Asynchronously detects all available WebGPU devices.
        /// </summary>
        public static async Task<ImmutableArray<WebGPUDevice>> GetDevicesAsync()
        {
            var devices = ImmutableArray.CreateBuilder<WebGPUDevice>();

            if (!IsSupported)
                return devices.ToImmutable();

            try
            {
                var navigator = SpawnJSRuntime.Instance.Get<Navigator>("navigator");
                var gpu = navigator.Gpu;

                if (gpu == null)
                    return devices.ToImmutable();

                // Request the default adapter
                var adapter = await gpu.RequestAdapter();
                if (adapter != null)
                {
                    var device = new WebGPUDevice(adapter, 0);
                    devices.Add(device);
                }

                // Try requesting a high-performance adapter
                var highPerfOptions = new GPURequestAdapterOptions
                {
                    PowerPreference = "high-performance"
                };
                var highPerfAdapter = await gpu.RequestAdapter(highPerfOptions);

                if (highPerfAdapter != null &&
                    adapter != null &&
                    highPerfAdapter.Info?.Device != adapter.Info?.Device)
                {
                    var device = new WebGPUDevice(highPerfAdapter, devices.Count);
                    devices.Add(device);
                }
            }
            catch (Exception)
            {
                // WebGPU not available or error occurred
            }

            return devices.ToImmutable();
        }

        /// <summary>
        /// Gets the first available WebGPU device.
        /// </summary>
        public static async Task<WebGPUDevice?> GetDefaultDeviceAsync()
        {
            var devices = await GetDevicesAsync();
            return devices.Length > 0 ? devices[0] : null;
        }

        #endregion

        #region Instance

        private static T ReadLimit<T>(GPUAdapter adapter, Func<GPUSupportedLimits, T> reader, T fallback)
        {
            try
            {
                using var limits = adapter.Limits;
                if (limits != null) return reader(limits);
            }
            catch { }
            return fallback;
        }

        private readonly GPUAdapter _adapter;
        private readonly int _deviceIndex;
        private bool _disposed;

        /// <summary>
        /// Constructs a new WebGPU device.
        /// </summary>
        /// <summary>
        /// WebGPU feature names that we recognize and will request if available.
        /// </summary>
        private static readonly string[] KnownFeatures = new[]
        {
            "shader-f16",
            "subgroups",
            "timestamp-query",
            "float32-filterable",
            "float32-blendable",
            "bgra8unorm-storage",
            "rg11b10ufloat-renderable",
            "texture-compression-bc",
            "texture-compression-etc2",
            "texture-compression-astc",
            "indirect-first-instance",
            "depth-clip-control",
            "depth32float-stencil8",
            "clip-distances",
            "dual-source-blending",
        };

        internal WebGPUDevice(GPUAdapter adapter, int deviceIndex)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _deviceIndex = deviceIndex;

            // Get adapter info safely
            try
            {
                var info = adapter.Info;
                Name = info?.Device ?? "WebGPU Device";
                Vendor = info?.Vendor ?? "Unknown";
                Architecture = info?.Architecture ?? "Unknown";
            }
            catch
            {
                Name = "WebGPU Device";
                Vendor = "Unknown";
                Architecture = "Unknown";
            }

            try
            {
                IsFallbackAdapter = adapter.IsFallbackAdapter;
            }
            catch
            {
                IsFallbackAdapter = false;
            }

            // Detect supported features from the adapter
            SupportedFeatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var features = adapter.Features;
                foreach (var featureName in KnownFeatures)
                {
                    if (features.Has(featureName))
                    {
                        SupportedFeatures.Add(featureName);
                    }
                }
            }
            catch
            {
                // Features detection failed — leave empty set
            }

            // Read limits individually so a single property failure doesn't reset all limits.
            // Some WebGPU limits (e.g. maxBufferSize) are unsigned long long in the spec and
            // may overflow int32 on certain adapters.
            MaxComputeWorkgroupSizeX = ReadLimit(adapter, l => (int)(l.MaxComputeWorkgroupSizeX ?? 256), 256);
            MaxComputeWorkgroupSizeY = ReadLimit(adapter, l => (int)(l.MaxComputeWorkgroupSizeY ?? 256), 256);
            MaxComputeWorkgroupSizeZ = ReadLimit(adapter, l => (int)(l.MaxComputeWorkgroupSizeZ ?? 64), 64);
            MaxComputeInvocationsPerWorkgroup = ReadLimit(adapter, l => (int)(l.MaxComputeInvocationsPerWorkgroup ?? 256), 256);
            MaxComputeWorkgroupsPerDimension = ReadLimit(adapter, l => (int)(l.MaxComputeWorkgroupsPerDimension ?? 65535), 65535);
            MaxComputeWorkgroupStorageSize = ReadLimit(adapter, l => (int)(l.MaxComputeWorkgroupStorageSize ?? 16384), 16384);
            MaxBufferSize = ReadLimit(adapter, l => l.MaxBufferSize ?? 268435456L, 268435456L);
            if (Backend.WebGPUBackend.VerboseLogging) Backend.WebGPUBackend.Log($"[WebGPUDevice] Limits: Invocations={MaxComputeInvocationsPerWorkgroup}, WorkgroupSizeX={MaxComputeWorkgroupSizeX}, SharedMem={MaxComputeWorkgroupStorageSize}, MaxBuf={MaxBufferSize}, MaxBufType={typeof(GPUSupportedLimits).GetProperty("MaxBufferSize")?.PropertyType.Name ?? "?"}");
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the native WebGPU adapter.
        /// </summary>
        public GPUAdapter Adapter => _adapter;

        /// <summary>
        /// Returns the device name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Returns the adapter vendor name.
        /// </summary>
        public string Vendor { get; }

        /// <summary>
        /// Returns the adapter architecture.
        /// </summary>
        public string Architecture { get; }

        /// <summary>
        /// Returns whether this is a fallback (software) adapter.
        /// </summary>
        public bool IsFallbackAdapter { get; }

        /// <summary>
        /// Gets the maximum workgroup size in X dimension.
        /// </summary>
        public int MaxComputeWorkgroupSizeX { get; }

        /// <summary>
        /// Gets the maximum workgroup size in Y dimension.
        /// </summary>
        public int MaxComputeWorkgroupSizeY { get; }

        /// <summary>
        /// Gets the maximum workgroup size in Z dimension.
        /// </summary>
        public int MaxComputeWorkgroupSizeZ { get; }

        /// <summary>
        /// Gets the maximum compute invocations per workgroup.
        /// </summary>
        public int MaxComputeInvocationsPerWorkgroup { get; }

        /// <summary>
        /// Gets the maximum dispatch dimensions.
        /// </summary>
        public int MaxComputeWorkgroupsPerDimension { get; }

        /// <summary>
        /// Gets the maximum workgroup storage size in bytes.
        /// </summary>
        public int MaxComputeWorkgroupStorageSize { get; }

        /// <summary>
        /// Gets the maximum buffer size in bytes.
        /// </summary>
        public long MaxBufferSize { get; }

        /// <summary>
        /// Gets the set of WebGPU features supported by this adapter.
        /// </summary>
        public HashSet<string> SupportedFeatures { get; }

        /// <summary>Returns true if the adapter supports the shader-f16 feature (native f16 in WGSL).</summary>
        public bool SupportsShaderF16 => SupportedFeatures.Contains("shader-f16");

        /// <summary>Returns true if the adapter supports subgroups (subgroupBroadcast, subgroupShuffle, etc.).</summary>
        public bool SupportsSubgroups => SupportedFeatures.Contains("subgroups");

        /// <summary>Returns true if the adapter supports timestamp queries for GPU profiling.</summary>
        public bool SupportsTimestampQuery => SupportedFeatures.Contains("timestamp-query");

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new WebGPU accelerator from this device.
        /// </summary>
        public async Task<WebGPUNativeAccelerator> CreateAcceleratorAsync()
        {
            return await WebGPUNativeAccelerator.CreateAsync(this);
        }

        /// <summary>
        /// Prints device information to the console.
        /// </summary>
        public void PrintInfo(TextWriter writer)
        {
            writer.WriteLine($"WebGPU Device: {Name}");
            writer.WriteLine($"  Vendor:       {Vendor}");
            writer.WriteLine($"  Architecture: {Architecture}");
            writer.WriteLine($"  Fallback:     {IsFallbackAdapter}");
            writer.WriteLine($"  Max Workgroup Size: ({MaxComputeWorkgroupSizeX}, {MaxComputeWorkgroupSizeY}, {MaxComputeWorkgroupSizeZ})");
            writer.WriteLine($"  Max Invocations:    {MaxComputeInvocationsPerWorkgroup}");
            writer.WriteLine($"  Max Workgroups:     {MaxComputeWorkgroupsPerDimension}");
            writer.WriteLine($"  Max Shared Memory:  {MaxComputeWorkgroupStorageSize} bytes");
            writer.WriteLine($"  Max Buffer Size:    {MaxBufferSize / (1024 * 1024)} MB");
            writer.WriteLine($"  Features ({SupportedFeatures.Count}): {(SupportedFeatures.Count > 0 ? string.Join(", ", SupportedFeatures) : "none")}");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _adapter?.Dispose();
        }

        #endregion
    }
}
