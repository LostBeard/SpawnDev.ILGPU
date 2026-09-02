// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGL
//                 WebGL2 Compute Library for Blazor WebAssembly
//
// File: WebGLILGPUDevice.cs
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.ILGPU.WebGL.Backend;

namespace SpawnDev.ILGPU.WebGL
{
    /// <summary>
    /// Represents an ILGPU Device implementation for WebGL2.
    /// </summary>
    [DeviceType(WebGLILGPUDevice.WebGLAcceleratorType)]
    public sealed class WebGLILGPUDevice : Device, IDisposable
    {
        /// <summary>
        /// The accelerator type constant for WebGL devices.
        /// </summary>
        public const AcceleratorType WebGLAcceleratorType = AcceleratorType.WebGL;

        #region Static

        /// <summary>
        /// Asynchronously detects all available WebGL2 devices and registers them.
        /// </summary>
        public static async Task<DeviceRegistry> GetDevicesAsync()
        {
            var registry = new DeviceRegistry();
            await GetDevicesAsync(device => true, registry);
            return registry;
        }

        /// <summary>
        /// Asynchronously detects WebGL2 devices matching the predicate.
        /// </summary>
        public static async Task GetDevicesAsync(
            Predicate<WebGLILGPUDevice> predicate,
            DeviceRegistry registry)
        {
            if (predicate is null)
                throw new ArgumentNullException(nameof(predicate));
            if (registry is null)
                throw new ArgumentNullException(nameof(registry));

            var webGlDevices = await WebGLDevice.GetDevicesAsync();
            for (int i = 0; i < webGlDevices.Length; i++)
            {
                var device = new WebGLILGPUDevice(webGlDevices[i], i);
                registry.Register(device, predicate);
            }
        }

        #endregion

        #region Instance

        private readonly WebGLDevice nativeDevice;

        /// <summary>
        /// Constructs a new WebGL2 ILGPU device.
        /// </summary>
        public WebGLILGPUDevice(WebGLDevice device, int deviceIndex)
        {
            nativeDevice = device ?? throw new ArgumentNullException(nameof(device));

            Name = device.Name;
            WarpSize = 1; // WebGL vertex shaders are single-threaded per invocation

            // WebGL2 vertex shaders don't have workgroups; set to 1
            MaxGroupSize = new Index3D(1, 1, 1);
            MaxNumThreadsPerGroup = 1;

            // Conservative estimates for WebGL2 GPGPU
            NumMultiprocessors = 1;
            MaxNumThreadsPerMultiprocessor = device.MaxVertexCount;

            // Grid size — limited by max vertex count
            MaxGridSize = new Index3D(
                device.MaxVertexCount,
                1,
                1);

            // Memory limits (not directly queryable for WebGL2 GPU memory)
            MemorySize = 256 * 1024 * 1024; // 256 MB estimate
            MaxSharedMemoryPerGroup = 0; // No shared memory in vertex shaders
            MaxConstantMemory = device.MaxUniformBlockSize;

            Capabilities = new WebGLCapabilityContext();
        }

        /// <summary>
        /// Gets the native WebGL2 device.
        /// </summary>
        public WebGLDevice NativeDevice => nativeDevice;

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new WebGL2 accelerator synchronously.
        /// NOTE: This is supported for WebGL2 (unlike WebGPU, it doesn't require async init).
        /// </summary>
        public override Accelerator CreateAccelerator(Context context)
        {
            return WebGLAccelerator.Create(context, this, null);
        }

        /// <inheritdoc/>
        public override async Task<Accelerator> CreateAcceleratorAsync(Context context)
        {
            return await Task.FromResult(WebGLAccelerator.Create(context, this, null));
        }

        /// <summary>
        /// Creates a new WebGL2 accelerator with the specified options.
        /// </summary>
        public WebGLAccelerator CreateAccelerator(Context context, WebGLBackendOptions? options)
        {
            return WebGLAccelerator.Create(context, this, options);
        }

        #endregion
    
        /// <summary>Releases the underlying browser handle.</summary>
        /// <remarks>
        /// ⚠️ ILGPU's <c>Device</c> is not IDisposable and <c>Context</c> never disposed its registry, so
        /// the <c>WebGLDevice</c> created during device enumeration - and the JS handle it owns - was
        /// never released. MEASURED 2026-09-02 by dumping SpawnJS's object table from the demo console
        /// mid-run: ~36 live GPUAdapter objects, one per Context created, each holding real GPU driver
        /// resources. The fork's Context.Dispose now disposes every registered device that implements
        /// IDisposable; this is the other half of that fix.
        /// </remarks>
        public void Dispose() => nativeDevice?.Dispose();
}
}
