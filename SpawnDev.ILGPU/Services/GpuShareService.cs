using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.Services
{
    /// <summary>
    /// Arguments for an intercepted <c>navigator.gpu.requestAdapter</c> call, passed to
    /// <see cref="GpuShareService.OnAdapterRequested"/> and <see cref="GpuShareService.OnAdapterCreated"/> handlers.
    /// </summary>
    public class GPUAdapterReturnOverride
    {
        /// <summary>
        /// The adapter to return. Null when the request arrives; a <see cref="GpuShareService.OnAdapterRequested"/>
        /// handler that sets it makes the hook return this adapter without calling the original
        /// <c>requestAdapter</c>. Holds the created adapter when <see cref="GpuShareService.OnAdapterCreated"/> fires.
        /// </summary>
        public GPUAdapter? Adapter { get; set; }
        /// <summary>
        /// The request options passed by the caller (may be null). Handlers may replace them; the final value is
        /// passed to the original <c>requestAdapter</c>.
        /// </summary>
        public SpawnJSObject? Options { get; set; }

    }
    /// <summary>
    /// Arguments for an intercepted <c>GPUAdapter.requestDevice</c> call, passed to the
    /// <c>OnDeviceRequested</c> and <c>OnDeviceCreated</c> handlers of <see cref="GPUAdapterHook"/> and
    /// <see cref="GpuShareService"/>.
    /// </summary>
    public class GPUDeviceReturnOverride
    {
        /// <summary>
        /// The device to return. Null when the request arrives; an <c>OnDeviceRequested</c> handler that sets it
        /// makes the hook return this device without calling the original <c>requestDevice</c>. Holds the
        /// returned device when <c>OnDeviceCreated</c> fires.
        /// </summary>
        public GPUDevice? Device { get; set; }
        /// <summary>
        /// The request options passed by the caller (may be null). Handlers may replace them; the final value is
        /// passed to the original <c>requestDevice</c>.
        /// </summary>
        public SpawnJSObject? Options { get; set; }
    }
    /// <summary>
    /// gpuShareService intercepts navigator.gpu.requestAdapter calls to capture the GPUAdapter and GPUDevice created by ORT WebGPU. This allows ILGPU to share the same device for zero-copy buffers.
    /// device and adapter request options can be modified by subscribing to the OnAdapterRequested and OnDeviceRequested events, and the created adapter and device can be accessed in the event handlers for sharing with ILGPU.
    /// </summary>
    public class GpuShareService : IBackgroundService
    {
        /// <summary>
        /// When true (default), a <c>requestAdapter</c> call returns <see cref="FirstAdapter"/> and a
        /// <c>requestDevice</c> call on a hooked adapter returns <see cref="FirstDevice"/> once one has been
        /// captured, so every caller shares one adapter and device. This service's <see cref="OnAdapterRequested"/>
        /// and <see cref="OnDeviceRequested"/> are not raised for a request answered this way. When false, every
        /// request goes through the events and the original API.
        /// </summary>
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
        /// <summary>
        /// Raised before a hooked adapter calls its original <c>requestDevice</c> (not raised when
        /// <see cref="ForceShare"/> supplies an existing device). Handlers run one at a time in subscription
        /// order and are awaited; they may change <see cref="GPUDeviceReturnOverride.Options"/> or set
        /// <see cref="GPUDeviceReturnOverride.Device"/> to skip the original call.
        /// </summary>
        public event Func<GPUAdapterHook, GPUDeviceReturnOverride, Task> OnDeviceRequested = default!;
        /// <summary>
        /// Raised after a hooked adapter's <c>requestDevice</c> has a device (created or supplied by a handler),
        /// before it is returned to the caller. Handlers run one at a time in subscription order and are awaited.
        /// </summary>
        public event Func<GPUAdapterHook, GPUDeviceReturnOverride, Task> OnDeviceCreated = default!;
        /// <summary>
        /// Raised before the original <c>navigator.gpu.requestAdapter</c> is called (not raised when
        /// <see cref="ForceShare"/> returns an existing adapter). Handlers run one at a time in subscription order
        /// and are awaited; they may change <see cref="GPUAdapterReturnOverride.Options"/> or set
        /// <see cref="GPUAdapterReturnOverride.Adapter"/>, which is then returned without being hooked and without
        /// raising <see cref="OnAdapterCreated"/>.
        /// </summary>
        public event Func<GPUAdapterReturnOverride, Task> OnAdapterRequested = default!;
        /// <summary>
        /// Raised after the original <c>requestAdapter</c> returns and the adapter has been wrapped in a
        /// <see cref="GPUAdapterHook"/> and added to <see cref="GPUAdapterHooks"/>. Handlers run one at a time in
        /// subscription order and are awaited.
        /// </summary>
        public event Func<GPUAdapterHook, GPUAdapterReturnOverride, Task> OnAdapterCreated = default!;
        /// <summary>
        /// One hook per adapter created through the original <c>requestAdapter</c>, in creation order. A hook is
        /// removed when it is disposed.
        /// </summary>
        public List<GPUAdapterHook> GPUAdapterHooks { get; } = new List<GPUAdapterHook>();
        private SpawnJSRuntime _js;
        FuncCallback<SpawnJSObject?, Task<GPUAdapter>>? _requestAdapterHookCallback;
        Function? _requestAdapterOrig;
        /// <summary>When true, logs intercepted requests and forced sharing to the browser console. Default false.</summary>
        public bool Verbose { get; set; } = false;
        /// <summary>
        /// Creates the service. In a browser with WebGPU, replaces <c>navigator.gpu.requestAdapter</c> with this
        /// service's hook and keeps the original as <c>navigator.gpu.requestAdapterOrig</c>. Outside a browser,
        /// or when <c>navigator.gpu</c> is missing, nothing is patched.
        /// </summary>
        /// <param name="js">The SpawnJS runtime.</param>
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
        /// <summary>The hooked adapter.</summary>
        public GPUAdapter Adapter { get; }
        Function? _requestDeviceOrig;
        /// <summary>
        /// Devices created through this adapter's original <c>requestDevice</c>, in creation order. Devices
        /// supplied by an <see cref="OnDeviceRequested"/> handler are not added.
        /// </summary>
        public List<GPUDevice> Devices { get; } = new List<GPUDevice>();
        /// <summary>
        /// Raised before the original <c>requestDevice</c> is called. Handlers run one at a time in subscription
        /// order and are awaited; they may change <see cref="GPUDeviceReturnOverride.Options"/> or set
        /// <see cref="GPUDeviceReturnOverride.Device"/> to skip the original call.
        /// </summary>
        public event Func<GPUAdapterHook, GPUDeviceReturnOverride, Task> OnDeviceRequested = default!;
        /// <summary>
        /// Raised after <c>requestDevice</c> has a device (created or supplied by a handler), before it is returned
        /// to the caller. Handlers run one at a time in subscription order and are awaited.
        /// </summary>
        public event Func<GPUAdapterHook, GPUDeviceReturnOverride, Task> OnDeviceCreated = default!;
        /// <summary>True once <see cref="Dispose"/> has run.</summary>
        public bool Disposed { get; private set; }
        /// <summary>
        /// Raised at the start of <see cref="Dispose"/>, before the original <c>requestDevice</c> is restored.
        /// Exceptions thrown by handlers are swallowed.
        /// </summary>
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
