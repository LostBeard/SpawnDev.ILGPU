// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGPU
//                 WebGPU Compute Library for Blazor WebAssembly
//
// File: WebGPUILGPUDevice.cs
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// Represents an ILGPU Device implementation for WebGPU.
    /// </summary>
    [DeviceType(WebGPUILGPUDevice.WebGPUAcceleratorType)]
    public sealed class WebGPUILGPUDevice : Device
    {
        /// <summary>
        /// The accelerator type constant for WebGPU devices.
        /// </summary>
        public const AcceleratorType WebGPUAcceleratorType = AcceleratorType.WebGPU;
        #region Static

        /// <summary>
        /// Asynchronously detects all available WebGPU devices and registers them.
        /// </summary>
        public static async Task<DeviceRegistry> GetDevicesAsync()
        {
            var registry = new DeviceRegistry();
            await GetDevicesAsync(device => true, registry);
            return registry;
        }

        /// <summary>
        /// Asynchronously detects WebGPU devices matching the predicate.
        /// </summary>
        public static async Task GetDevicesAsync(
            Predicate<WebGPUILGPUDevice> predicate,
            DeviceRegistry registry)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));
            if (registry is null)
                throw new ArgumentNullException(nameof(registry));

            var webGpuDevices = await WebGPUDevice.GetDevicesAsync();
            for (int i = 0; i < webGpuDevices.Length; i++)
            {
                var device = new WebGPUILGPUDevice(webGpuDevices[i], i);
                registry.Register(device, predicate);
            }
        }

        #endregion

        #region Instance

        private readonly WebGPUDevice nativeDevice;

        /// <summary>
        /// Constructs a new WebGPU ILGPU device.
        /// </summary>
        public WebGPUILGPUDevice(WebGPUDevice device, int deviceIndex)
        {
            nativeDevice = device ?? throw new ArgumentNullException(nameof(device));

            // Set device properties from native WebGPU device
            Name = device.Name;
            WarpSize = 32; // WebGPU subgroup size is typically 32 (similar to CUDA warps)

            // Map WebGPU workgroup limits to ILGPU concepts
            MaxGroupSize = new Index3D(
                device.MaxComputeWorkgroupSizeX,
                device.MaxComputeWorkgroupSizeY,
                device.MaxComputeWorkgroupSizeZ);

            MaxNumThreadsPerGroup = device.MaxComputeInvocationsPerWorkgroup;

            // WebGPU doesn't expose multiprocessor count directly
            // Use a reasonable estimate based on workgroup capacity
            NumMultiprocessors = 16; // Conservative estimate
            MaxNumThreadsPerMultiprocessor = device.MaxComputeInvocationsPerWorkgroup;

            // Grid size (dispatch) limits
            MaxGridSize = new Index3D(
                device.MaxComputeWorkgroupsPerDimension,
                device.MaxComputeWorkgroupsPerDimension,
                device.MaxComputeWorkgroupsPerDimension);

            // Memory limits
            MemorySize = device.MaxBufferSize;
            MaxSharedMemoryPerGroup = device.MaxComputeWorkgroupStorageSize;
            MaxConstantMemory = 65536; // WebGPU uniform buffer limit

            CapGroupSizeForSharedMemory();

            // Create basic capability context
            Capabilities = new WebGPUCapabilityContext();
        }

        /// <summary>
        /// Constructs a WebGPU ILGPU device from an externally-provided GPUDevice.
        /// This is used when sharing a device with another library (e.g., ONNX Runtime Web)
        /// that has already created its own GPUDevice. Skips adapter/device probing.
        /// </summary>
        /// <param name="externalDevice">An existing GPUDevice (e.g., from ort.env.webgpu.device).</param>
        public WebGPUILGPUDevice(GPUDevice externalDevice)
        {
            ExternalGPUDevice = externalDevice ?? throw new ArgumentNullException(nameof(externalDevice));

            // Read device info from the GPUDevice if available
            Name = "WebGPU Device (External)";
            WarpSize = 32;

            // Read limits from the external device
            int maxWorkgroupSizeX = 256, maxWorkgroupSizeY = 256, maxWorkgroupSizeZ = 64;
            int maxInvocations = 256, maxWorkgroups = 65535, maxSharedMem = 16384;
            long maxBufferSize = 268435456;

            try
            {
                using var limits = externalDevice.Limits;
                if (limits != null)
                {
                    maxWorkgroupSizeX = (int)(limits.MaxComputeWorkgroupSizeX ?? 256);
                    maxWorkgroupSizeY = (int)(limits.MaxComputeWorkgroupSizeY ?? 256);
                    maxWorkgroupSizeZ = (int)(limits.MaxComputeWorkgroupSizeZ ?? 64);
                    maxInvocations = (int)(limits.MaxComputeInvocationsPerWorkgroup ?? 256);
                    maxWorkgroups = (int)(limits.MaxComputeWorkgroupsPerDimension ?? 65535);
                    maxSharedMem = (int)(limits.MaxComputeWorkgroupStorageSize ?? 16384);
                    maxBufferSize = (long)(limits.MaxBufferSize ?? 268435456);
                }
            }
            catch { }

            MaxGroupSize = new Index3D(maxWorkgroupSizeX, maxWorkgroupSizeY, maxWorkgroupSizeZ);
            MaxNumThreadsPerGroup = maxInvocations;
            NumMultiprocessors = 16;
            MaxNumThreadsPerMultiprocessor = maxInvocations;
            MaxGridSize = new Index3D(maxWorkgroups, maxWorkgroups, maxWorkgroups);
            MemorySize = maxBufferSize;
            MaxSharedMemoryPerGroup = maxSharedMem;
            MaxConstantMemory = 65536;

            CapGroupSizeForSharedMemory();

            Capabilities = new WebGPUCapabilityContext();
        }

        /// <summary>
        /// Caps MaxNumThreadsPerGroup to avoid exceeding the device's workgroup storage
        /// limit when kernels use GroupExtensions.ExclusiveScan (which is compiled via
        /// ILGroupExtensions.InclusiveScanImplementation and always allocates a fixed
        /// 2048-element shared memory array). RadixSortKernel1 additionally allocates
        /// groupSize * UnrollFactor (4) elements. The total shared memory must fit within
        /// MaxSharedMemoryPerGroup.
        /// </summary>
        private void CapGroupSizeForSharedMemory()
        {
            const int ScanFixedElements = 2048;
            const int RadixSortUnrollFactor = 4;
            const int ElementSize = sizeof(int); // 4 bytes

            int scanOverheadBytes = ScanFixedElements * ElementSize;
            int availableBytes = MaxSharedMemoryPerGroup - scanOverheadBytes;
            if (availableBytes <= 0)
            {
                MaxNumThreadsPerGroup = 64;
                if (Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPUILGPUDevice] SharedMem={MaxSharedMemoryPerGroup}B too small, capping group size to {MaxNumThreadsPerGroup}");
                return;
            }

            int maxGroupForSharedMem = availableBytes / (RadixSortUnrollFactor * ElementSize);
            int original = MaxNumThreadsPerGroup;
            if (maxGroupForSharedMem < MaxNumThreadsPerGroup)
            {
                // Round down to nearest power of 2 for clean workgroup sizes
                int capped = 1;
                while (capped * 2 <= maxGroupForSharedMem) capped *= 2;
                MaxNumThreadsPerGroup = Math.Max(64, capped);
            }

            if (Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WebGPUILGPUDevice] SharedMem={MaxSharedMemoryPerGroup}B, maxGroupForSharedMem={maxGroupForSharedMem}, MaxNumThreadsPerGroup: {original} -> {MaxNumThreadsPerGroup}");
        }

        /// <summary>
        /// An externally-provided GPUDevice, set when this device was created via the
        /// external device constructor. Null for standard adapter-based devices.
        /// </summary>
        internal GPUDevice? ExternalGPUDevice { get; }

        /// <summary>
        /// Gets the native WebGPU device.
        /// </summary>
        public WebGPUDevice NativeDevice => nativeDevice;

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new WebGPU accelerator synchronously.
        /// NOTE: This will throw as WebGPU requires async initialization.
        /// Use CreateAcceleratorAsync instead.
        /// </summary>
        public override Accelerator CreateAccelerator(Context context)
        {
            throw new NotSupportedException(
                "WebGPU requires async initialization. Use CreateAcceleratorAsync instead.");
        }

        /// <summary>
        /// Creates a new WebGPU accelerator asynchronously.
        /// </summary>
        public override async Task<Accelerator> CreateAcceleratorAsync(Context context)
        {
            return await CreateAcceleratorAsync(context, null);
        }

        /// <summary>
        /// Creates a new WebGPU accelerator asynchronously with the specified options.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="options">The backend configuration options (null for defaults).</param>
        /// <returns>A task that represents the async creation of the WebGPU accelerator.</returns>
        public async Task<WebGPUAccelerator> CreateAcceleratorAsync(Context context, WebGPUBackendOptions? options)
        {
            return await WebGPUAccelerator.CreateAsync(context, this, options);
        }

        #endregion
    }
}
