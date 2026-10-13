// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmILGPUDevice.cs
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.ILGPU.Wasm.Backend;

namespace SpawnDev.ILGPU.Wasm
{
    /// <summary>
    /// Represents an ILGPU Device implementation for the WebAssembly backend.
    /// Always available in Blazor WASM environments.
    /// </summary>
    [DeviceType(AcceleratorType.Wasm)]
    public sealed class WasmILGPUDevice : Device
    {
        #region Static

        /// <summary>
        /// Detects Wasm devices and adds them to the registry.
        /// </summary>
        public static void GetDevices(
            Predicate<WasmILGPUDevice> predicate,
            DeviceRegistry registry)
        {
            if (predicate is null) throw new ArgumentNullException(nameof(predicate));
            if (registry is null) throw new ArgumentNullException(nameof(registry));
            var device = new WasmILGPUDevice();
            registry.Register(device, predicate);
        }

        #endregion

        #region Instance

        /// <summary>
        /// The number of hardware threads reported by the browser.
        /// </summary>
        public int HardwareConcurrency { get; }

        /// <summary>
        /// Reads <c>navigator.hardwareConcurrency</c> from the browser. Returns 4 when it is not
        /// reported or cannot be read (for example on desktop, where there is no JS runtime).
        /// </summary>
        /// <returns>The number of logical processors, or 4.</returns>
        public static int GetHardwareConcurrency()
        {
            try
            {
                using var navigator = SpawnJSRuntime.Instance.Get<Navigator>("navigator");
                return navigator.HardwareConcurrency ?? 4;
            }
            catch
            {
                return 4;
            }
        }

        /// <summary>
        /// Creates the Wasm device description: warp size <see cref="WasmBackend.WasmWarpSize"/>,
        /// up to 256 threads per group (<c>MaxGroupSize</c> = (256, 1, 1)), one multiprocessor per
        /// <see cref="HardwareConcurrency"/>, 64 KiB shared memory per group, and a 2 GiB reported
        /// memory size.
        /// </summary>
        public WasmILGPUDevice()
        {
            HardwareConcurrency = GetHardwareConcurrency();
            Name = $"WebAssembly Compute ({HardwareConcurrency} cores)";
            WarpSize = WasmBackend.WasmWarpSize; // emulated warp (shared-mem shuffle); mirrors CPU's 8
            // 256 threads/group enables single-group for most algorithm kernels
            // (RadixSort up to 256 elements, DualScan, etc.) while keeping the
            // fiber dispatch overhead manageable (threads run sequentially per phase).
            MaxGroupSize = new Index3D(256, 1, 1);
            MaxNumThreadsPerGroup = 256;
            NumMultiprocessors = HardwareConcurrency;
            MaxNumThreadsPerMultiprocessor = 256;
            MaxGridSize = new Index3D(int.MaxValue, 1, 1);
            MemorySize = 2L * 1024 * 1024 * 1024;
            MaxSharedMemoryPerGroup = 65536;
            MaxConstantMemory = 65536;
            Capabilities = new WasmCapabilityContext();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Always throws: the Wasm accelerator must be created asynchronously. Use
        /// <see cref="CreateAcceleratorAsync(Context)"/>.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override Accelerator CreateAccelerator(Context context)
        {
            // Wasm accelerator requires async creation (Worker init, module compilation).
            // Blocking here deadlocks Blazor WASM's single-threaded scheduler.
            throw new NotSupportedException(
                "Wasm accelerator requires async creation. Use CreateAcceleratorAsync() instead.");
        }

        /// <summary>
        /// Creates a <see cref="WasmAccelerator"/> with default <see cref="WasmBackendOptions"/>.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <returns>The new accelerator.</returns>
        public override async Task<Accelerator> CreateAcceleratorAsync(Context context)
        {
            return await WasmAccelerator.Create(context);
        }

        /// <summary>
        /// Creates a <see cref="WasmAccelerator"/> with the given backend options.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="options">Backend options; default options are used when null.</param>
        /// <returns>The new accelerator.</returns>
        public async Task<WasmAccelerator> CreateAcceleratorAsync(Context context, WasmBackendOptions? options)
        {
            return await WasmAccelerator.Create(context, options ?? new WasmBackendOptions());
        }

        #endregion
    }
}
