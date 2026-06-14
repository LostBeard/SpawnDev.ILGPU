// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                    Reusable Web Worker Pool for Blazor WebAssembly
//
// File: WorkerPool.cs
//
// Creates a fixed pool of Web Workers at initialization. Each worker runs a
// universal bootstrap script that accepts work via messages, avoiding the
// overhead of creating/destroying workers per kernel dispatch.
// ---------------------------------------------------------------------------------------

using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.BlazorJS.Toolbox;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// A reusable pool of Web Workers. Workers are created once with a universal
    /// bootstrap script and accept work via PostMessage. After completing work,
    /// workers return to the pool for reuse.
    /// </summary>
    public class WorkerPool : IDisposable
    {
        private readonly List<Worker> _allWorkers = new();
        private readonly Queue<Worker> _available = new();
        private readonly object _lock = new();
        private bool _disposed;

        /// <summary>
        /// Gets the total number of workers in this pool.
        /// </summary>
        public int Size => _allWorkers.Count;

        /// <summary>
        /// Universal bootstrap script for JS-based workers (Workers backend).
        /// The worker listens for messages containing a { script, data } payload.
        /// It evaluates the script as a function body with 'data' parameter,
        /// then calls it with the message data.
        /// </summary>
        private static readonly string JSBootstrapScript = @"
var _fnCache = {};
self.onmessage = function(e) {
  var d = e.data;
  try {
    var fn;
    if (d.scriptHash && _fnCache[d.scriptHash]) {
      fn = _fnCache[d.scriptHash];
    } else {
      fn = new Function('d', d.script);
      if (d.scriptHash) _fnCache[d.scriptHash] = fn;
    }
    fn(d);
  } catch(ex) {
    self.postMessage({ done: false, error: (ex && ex.message) ? ex.message : String(ex) });
  }
};
";

        /// <summary>
        /// Universal bootstrap script for Wasm-based workers (Wasm backend).
        /// The worker listens for messages containing { script, ... } payload.
        /// The script is an async function body that receives the full message data.
        /// </summary>
        private static readonly string WasmBootstrapScript = @"
// Per-kernel module + instance cache. Multi-kernel pipelines (e.g. ML inference
// alternating Conv2D / InstanceNorm / ReLU) used to re-compile every kernel
// switch because the worker only kept ONE _cachedModule. With per-kernel
// caching, each worker compiles each distinct kernel ONCE and keeps it for
// the lifetime of the worker - subsequent dispatches of the same kernel
// just look it up by kernelId and re-instantiate (cheap) only when memory
// changes. Surfaced 2026-05-04 by Data's StyleMosaic Wasm 10+ minute hang.
var _modulesById = {};
var _instancesById = {};
var _lastMemoryBuffer = null;
var _cachedFn = null;
var _cachedFnSrc = null;
const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
const _mathImports = {
  sin: Math.sin, cos: Math.cos, tan: Math.tan,
  asin: Math.asin, acos: Math.acos, atan: Math.atan,
  sinh: Math.sinh, cosh: Math.cosh, tanh: Math.tanh,
  exp: Math.exp, log: Math.log, log2: Math.log2,
  log10: Math.log10, round: Math.round,
  truncate: Math.trunc, sign: Math.sign,
  exp2: (x) => Math.pow(2, x),
  sqrt: Math.sqrt, abs: Math.abs,
  ceil: Math.ceil, floor: Math.floor,
  pow: Math.pow, atan2: Math.atan2
};

self.onmessage = async function(e) {
  var d = e.data;
  try {
    // Module-cache flush (bounds the per-worker _modulesById accumulation that drives late-lane
    // memory pressure — Tuvok's trace 2026-06-14: kernels 2->1057 unbounded on the ML Wasm lane,
    // heavy tests time out at high count). The host triggers this at a fresh accelerator's FIRST
    // dispatch when the cumulative-kernels-since-flush crosses a threshold, BEFORE sending any kernel
    // bytes — so dropping every cached module is safe (this accelerator re-sends its own; older
    // disposed accelerators' modules are the dead weight being cleared). Drop instances + force a
    // memory re-bind too. (Sequential-accelerator assumption, like the rest of the shared pool.)
    if (d.clearModuleCache) {
      _modulesById = {};
      _instancesById = {};
      _lastMemoryBuffer = null;
    }
    // kernelId identifies which Wasm module to use. Sent on every dispatch.
    // The C# side sends wasmBytes only the FIRST time this worker sees this kernel.
    var kid = d.kernelId;
    if (kid === undefined || kid === null) {
      // Backwards-compat path: legacy callers without kernelId. Treat as a single
      // global kernel slot. (Old WasmBootstrapScript behavior preserved.)
      kid = 0;
    }
    // Compile module on first arrival of this kernelId (or refresh if wasmBytes
    // explicitly re-sent — e.g. kernel was rebuilt).
    if (d.wasmBytes) {
      var wasmBuf = new Uint8Array(d.wasmBytes).buffer;
      _modulesById[kid] = await WebAssembly.compile(wasmBuf);
      // Memory or module change invalidates this kernel's cached instance.
      _instancesById[kid] = null;
    }
    var module = _modulesById[kid];
    if (!module) {
      throw new Error('Module not cached for kernelId ' + kid + ' (C# should have sent wasmBytes on first dispatch to this worker)');
    }
    // Memory buffer change invalidates ALL cached instances (they're tied to the
    // memory's underlying SharedArrayBuffer). PostMessage creates a new Memory
    // wrapper but the underlying SAB is the same — compare .buffer to detect
    // genuine memory swaps (e.g. WebAssembly.Memory.grow() that allocates new SAB).
    if (_lastMemoryBuffer !== d.memory.buffer) {
      _lastMemoryBuffer = d.memory.buffer;
      _instancesById = {};
    }
    var instance = _instancesById[kid];
    if (!instance) {
      instance = await WebAssembly.instantiate(module, {
        env: {
          memory: d.memory,
          // Variant C (Trip 2026-05-27): env.notify shim. The wasm dispatcher's last-
          // arriving worker calls this AFTER bumping the gen counter, waking all parked
          // Atomics.wait(Infinity)ers on the gen slot. The wasm side passes count as
          // int.MaxValue (positive wake-all), NOT -1: the ECMAScript spec coerces negative
          // counts to +Infinity, but in V8 the negative form passed through WASM-to-host
          // signed-i32 conversion did NOT wake parked waiters in our oversub repro (Trip
          // 2026-05-27); int.MaxValue works reliably. Use the same value if you ever swap
          // shims. View is constructed per-call because d.memory.buffer can swap on
          // WebAssembly.Memory.grow; cost is negligible vs. the OS-park wake. Note: the
          // `d` captured here is from the FIRST onmessage that instantiated this kernel
          // for this worker, but d.memory is the WebAssembly.Memory object (stable across
          // dispatches; .buffer follows growth). For accelerators that never grow memory
          // (the common case), this is byte-equivalent to capturing fresh d each call.
          notify: function (byteAddr, count) {
            var view = new Int32Array(d.memory.buffer);
            return Atomics.notify(view, byteAddr >>> 2, count);
          },
        },
        Math: _mathImports
      });
      _instancesById[kid] = instance;
    }
    d._instance = instance;
    if (_cachedFnSrc !== d.script) { _cachedFn = new AsyncFunction('d', d.script); _cachedFnSrc = d.script; }
    await _cachedFn(d);
  } catch(ex) {
    self.postMessage({ done: false, error: (ex && ex.message) ? ex.message : String(ex) });
  }
};
";

        /// <summary>
        /// Creates a new worker pool with the specified number of workers.
        /// </summary>
        /// <param name="size">Number of workers to create.</param>
        /// <param name="useAsync">If true, uses the async bootstrap for Wasm workers.</param>
        public WorkerPool(int size, bool useAsync = false)
        {
            var script = useAsync ? WasmBootstrapScript : JSBootstrapScript;
            var workers = QuickWorker.CreateWorkersFromJS(script, size);
            foreach (var worker in workers)
            {
                _allWorkers.Add(worker);
                _available.Enqueue(worker);
            }
        }

        /// <summary>
        /// Acquires all currently available workers (up to the pool size).
        /// The caller is responsible for returning workers via <see cref="Return"/>.
        /// </summary>
        /// <param name="count">Number of workers requested.</param>
        /// <returns>List of available workers. May be fewer than requested if pool is busy.</returns>
        public List<Worker> Acquire(int count)
        {
            var result = new List<Worker>();
            lock (_lock)
            {
                while (result.Count < count && _available.Count > 0)
                {
                    result.Add(_available.Dequeue());
                }
            }
            return result;
        }

        /// <summary>
        /// Returns a worker to the pool for reuse.
        /// </summary>
        public void Return(Worker worker)
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (!_disposed)
                {
                    _available.Enqueue(worker);
                }
            }
        }

        /// <summary>
        /// Returns multiple workers to the pool.
        /// </summary>
        public void Return(IEnumerable<Worker> workers)
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (!_disposed)
                {
                    foreach (var worker in workers)
                    {
                        _available.Enqueue(worker);
                    }
                }
            }
        }

        /// <summary>
        /// Permanently removes a worker from the pool (both the all-workers roster and the
        /// available queue). The caller owns terminating/disposing the worker. Used when an
        /// accelerator reclaims a checked-out worker that may be running an orphaned kernel at
        /// Dispose: it must NOT be returned to the pool for another accelerator to message.
        /// </summary>
        public void Remove(Worker worker)
        {
            lock (_lock)
            {
                _allWorkers.Remove(worker);
                // Rebuild the available queue without the removed worker (Queue has no random
                // remove). The available set is small (<= pool size), so this is cheap.
                if (_available.Count > 0)
                {
                    var kept = new Queue<Worker>(_available.Count);
                    while (_available.Count > 0)
                    {
                        var w = _available.Dequeue();
                        if (!ReferenceEquals(w, worker)) kept.Enqueue(w);
                    }
                    while (kept.Count > 0) _available.Enqueue(kept.Dequeue());
                }
            }
        }

        /// <summary>
        /// Ensures the pool has at least the specified number of workers.
        /// Creates additional workers if needed.
        /// </summary>
        public void EnsureSize(int requiredSize, bool useAsync = false)
        {
            lock (_lock)
            {
                if (_allWorkers.Count >= requiredSize) return;

                int toCreate = requiredSize - _allWorkers.Count;
                var script = useAsync ? WasmBootstrapScript : JSBootstrapScript;
                var newWorkers = QuickWorker.CreateWorkersFromJS(script, toCreate);
                foreach (var worker in newWorkers)
                {
                    _allWorkers.Add(worker);
                    _available.Enqueue(worker);
                }
            }
        }

        /// <summary>
        /// Disposes all workers in the pool.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
            {
                _available.Clear();
                foreach (var worker in _allWorkers)
                {
                    try
                    {
                        worker.Terminate();
                        worker.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WorkerPool] Worker termination failed: {ex.Message}");
                    }
                }
                _allWorkers.Clear();
            }
        }
    }
}
