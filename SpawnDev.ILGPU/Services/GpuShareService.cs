using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.Services
{
    public class GPUAdapterReturnOverride
    {
        public GPUAdapter? Adapter { get; set; }
        public SpawnJSObject? Options { get; set; }

    }
    public class GPUDeviceReturnOverride
    {
        public GPUDevice? Device { get; set; }
        public SpawnJSObject? Options { get; set; }
    }
    /// <summary>
    /// gpuShareService intercepts navigator.gpu.requestAdapter calls to capture the GPUAdapter and GPUDevice created by ORT WebGPU. This allows ILGPU to share the same device for zero-copy buffers.
    /// device and adapter request options can be modified by subscribing to the OnAdapterRequested and OnDeviceRequested events, and the created adapter and device can be accessed in the event handlers for sharing with ILGPU.
    /// </summary>
    public class GpuShareService : IBackgroundService
    {
        public bool ForceShare = true; // set to false to disable sharing and just log the created adapter
        /// <summary>
        /// Gets the first available GPU adapter from the collection of GPU adapters.
        /// </summary>
        /// <remarks>If no adapters are available, this property returns null. This can be useful for
        /// determining the primary GPU for rendering tasks.</remarks>
        public GPUAdapter? FirstAdapter => GPUAdapterHooks.FirstOrDefault()?.Adapter;
        /// <summary>
        /// Gets the first available GPU device from the collection of devices associated with the first GPU adapter, or
        /// null if no adapters or devices are present.
        /// </summary>
        /// <remarks>This property provides a convenient way to access the primary GPU device in
        /// environments where multiple adapters or devices may exist. If no GPU adapters are available, or if the first
        /// adapter does not contain any devices, the property returns null.</remarks>
        public GPUDevice? FirstDevice => GPUAdapterHooks.FirstOrDefault()?.Devices.FirstOrDefault();
        public event Func<GPUAdapterHook, GPUDeviceReturnOverride, Task> OnDeviceRequested = default!;
        public event Func<GPUAdapterHook, GPUDeviceReturnOverride, Task> OnDeviceCreated = default!;
        public event Func<GPUAdapterReturnOverride, Task> OnAdapterRequested = default!;
        public event Func<GPUAdapterHook, GPUAdapterReturnOverride, Task> OnAdapterCreated = default!;
        public List<GPUAdapterHook> GPUAdapterHooks { get; } = new List<GPUAdapterHook>();
        private SpawnJSRuntime _js;
        FuncCallback<SpawnJSObject?, Task<GPUAdapter>>? _requestAdapterHookCallback;
        Function? _requestAdapterOrig;
        public bool Verbose { get; set; } = false;
        public GpuShareService(SpawnJSRuntime js)
        {
            _js = js;
            if (!_js.IsBrowser) return;
            using var navigator = _js.Get<Navigator>("navigator");
            if (navigator != null)
            {
                using var gpu = navigator.Gpu;
                if (gpu != null)
                {
                    // Patch navigator.gpu.requestAdapter to capture the GPUDevice created by ORT WebGPU.
                    // This allows ILGPU to share the same device for zero-copy buffers.
                    _requestAdapterOrig = gpu.JSRef!.Get<Function>("requestAdapter");
                    // make the orignal requestAdapter available
                    gpu.JSRef!.Set("requestAdapterOrig", _requestAdapterOrig);
                    // set our hook as the new requestAdapter
                    _requestAdapterHookCallback = new FuncCallback<SpawnJSObject?, Task<GPUAdapter>>(RequestAdapter_Hook);
                    gpu.JSRef!.Set("requestAdapter", _requestAdapterHookCallback);
                }
            }
        }
        private async Task<GPUAdapter> RequestAdapter_Hook(SpawnJSObject? options)
        {
            if (Verbose) _js.Log("TT OnAdapterRequested called", options);
            if (ForceShare)
            {
                var firstAdapter = FirstAdapter;
                if (firstAdapter != null)
                {
                    if (Verbose) _js.Log("Forcing shared adapter:", firstAdapter);
                    return firstAdapter;
                }
            }
            var args = new GPUAdapterReturnOverride() { Adapter = null, Options = options };
            // fire OnAdapterRequested 
            if (OnAdapterRequested != null)
            {
                // Get every individual delegate registered to the event
                var delegates = OnAdapterRequested.GetInvocationList();
                // async call each delegate sequentially so they can modify the options before the next one runs
                var handlers = delegates.Cast<Func<GPUAdapterReturnOverride, Task>>();
                foreach (var task in handlers)
                {
                    await task(args);
                }
            }
            if (args.Adapter != null)
            {
                return args.Adapter;
            }
            // call the original requestAdapter
            args.Adapter = await _requestAdapterOrig!.CallAsync<GPUAdapter>(null, args.Options);
            // create a hook for the adapter to capture requestDevice calls
            var adapterHook = new GPUAdapterHook(args.Adapter);
            GPUAdapterHooks.Add(adapterHook);
            // attach the service's events to the adapter hook so they can be used to share the created device with ILGPU
            adapterHook.OnDisposing += AdapterHook_OnDisposing;
            adapterHook.OnDeviceRequested += AdapterHook_OnDeviceRequested;
            adapterHook.OnDeviceCreated += AdapterHook_OnDeviceCreated;
            // fire OnAdapterCreated 
            if (OnAdapterCreated != null)
            {
                // Get every individual delegate registered to the event
                var delegates = OnAdapterCreated.GetInvocationList();
                // async call each delegate sequentially so they can modify the options before the next one runs
                var handlers = delegates.Cast<Func<GPUAdapterHook, GPUAdapterReturnOverride, Task>>();
                foreach (var task in handlers)
                {
                    await task(adapterHook, args);
                }
            }
            return args.Adapter;
        }
        private void AdapterHook_OnDisposing(GPUAdapterHook adapterHook)
        {
            if (!GPUAdapterHooks.Contains(adapterHook)) return;
            GPUAdapterHooks.Remove(adapterHook);
            adapterHook.OnDisposing -= AdapterHook_OnDisposing;
            adapterHook.OnDeviceRequested -= AdapterHook_OnDeviceRequested;
            adapterHook.OnDeviceCreated -= AdapterHook_OnDeviceCreated;
        }
        // called before a GPUDevice is created by ORT WebGPU in the adapter hook, allowing us to modify the request options before the device is created
        private async Task AdapterHook_OnDeviceRequested(GPUAdapterHook adapterHook, GPUDeviceReturnOverride args)
        {
            if (ForceShare)
            {
                var firstDevice = FirstDevice;
                if (firstDevice != null)
                {
                    if (Verbose) _js.Log("Forcing shared device:", firstDevice);
                    args.Device = firstDevice;
                    return;
                }
            }
            if (OnDeviceRequested != null)
            {
                // Get every individual delegate registered to the event
                var delegates = OnDeviceRequested.GetInvocationList();
                // async call each delegate sequentially so they can modify the options before the next one runs
                var handlers = delegates.Cast<Func<GPUAdapterHook, GPUDeviceReturnOverride, Task>>();
                foreach (var task in handlers)
                {
                    await task(adapterHook, args);
                }
            }
        }
        // called after a GPUDevice is created by ORT WebGPU in the adapter hook, allowing us to share the created device with ILGPU for zero-copy buffers
        private async Task AdapterHook_OnDeviceCreated(GPUAdapterHook adapterHook, GPUDeviceReturnOverride args)
        {
            if (OnDeviceCreated != null)
            {
                // Get every individual delegate registered to the event
                var delegates = OnDeviceCreated.GetInvocationList();
                // async call each delegate sequentially so they can modify the options before the next one runs
                var handlers = delegates.Cast<Func<GPUAdapterHook, GPUDeviceReturnOverride, Task>>();
                foreach (var task in handlers)
                {
                    await task(adapterHook, args);
                }
            }
        }
    }
    /// <summary>
    /// A GPUAdapter hook that intercepts requestDevice calls to capture the GPUDevice created by ORT WebGPU. This allows ILGPU to share the same device for zero-copy buffers.
    /// </summary>
    public class GPUAdapterHook : IDisposable
    {
        FuncCallback<SpawnJSObject?, Task<GPUDevice>>? _requestDeviceHookCallback;
        public GPUAdapter Adapter { get; }
        Function? _requestDeviceOrig;
        public List<GPUDevice> Devices { get; } = new List<GPUDevice>();
        public event Func<GPUAdapterHook, GPUDeviceReturnOverride, Task> OnDeviceRequested = default!;
        public event Func<GPUAdapterHook, GPUDeviceReturnOverride, Task> OnDeviceCreated = default!;
        public bool Disposed { get; private set; }
        public event Action<GPUAdapterHook> OnDisposing = default!;
        /// <summary>
        /// Creates a hook for a GPUAdapter to intercept requestDevice calls. This allows us to capture the GPUDevice created by ORT WebGPU and share it with ILGPU for zero-copy buffers.
        /// </summary>
        /// <param name="adapter"></param>
        public GPUAdapterHook(GPUAdapter adapter)
        {
            Adapter = adapter;
            // patch the adapter's requestDevice
            _requestDeviceOrig = adapter.JSRef!.Get<Function>("requestDevice");
            // make the orignal requestDevice available
            adapter.JSRef!.Set("requestDeviceOrig", _requestDeviceOrig);
            // set our hook as the new requestDevice
            _requestDeviceHookCallback = new FuncCallback<SpawnJSObject?, Task<GPUDevice>>(RequestDevice_Hook);
            adapter.JSRef!.Set("requestDevice", _requestDeviceHookCallback);
        }
        private async Task<GPUDevice> RequestDevice_Hook(SpawnJSObject? options)
        {
            var args = new GPUDeviceReturnOverride() { Device = null, Options = options };
            // fire OnDeviceRequested 
            if (OnDeviceRequested != null)
            {
                // Get every individual delegate registered to the event
                var delegates = OnDeviceRequested.GetInvocationList();
                // async call each delegate sequentially so they can modify the options before the next one runs
                var handlers = delegates.Cast<Func<GPUAdapterHook, GPUDeviceReturnOverride, Task>>();
                foreach (var task in handlers)
                {
                    await task(this, args);
                }
            }
            if (args.Device == null)
            {
                // call the original requestAdapter
                var device = await _requestDeviceOrig!.CallAsync<GPUDevice>(null, args.Options);
                args.Device = device;
                // store the device for later sharing
                Devices.Add(device);
            }
            // fire OnDeviceCreated 
            if (OnDeviceCreated != null)
            {
                // Get every individual delegate registered to the event
                var delegates = OnDeviceCreated.GetInvocationList();
                // async call each delegate sequentially so they can modify the options before the next one runs
                var handlers = delegates.Cast<Func<GPUAdapterHook, GPUDeviceReturnOverride, Task>>();
                foreach (var task in handlers)
                {
                    await task(this, args);
                }
            }
            return args.Device;
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            try
            {
                OnDisposing?.Invoke(this);
            }
            catch { }
            // restore the original requestDevice
            if (_requestDeviceOrig != null)
            {
                Adapter.JSRef!.Set("requestDevice", _requestDeviceOrig);
                _requestDeviceOrig.Dispose();
            }
            _requestDeviceHookCallback?.Dispose();
        }
    }
}
