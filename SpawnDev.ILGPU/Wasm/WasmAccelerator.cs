// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmAccelerator.cs
//
// Accelerator implementation that dispatches compiled Wasm kernels to Web Workers.
// Each worker instantiates the Wasm module with shared memory and runs the kernel.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Runtime;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.ILGPU.Wasm.Backend;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Wasm
{
    /// <summary>
    /// Wasm accelerator implementation for ILGPU.
    /// Compiles kernels to WebAssembly binary and executes them on the main thread (Phase 1).
    /// </summary>
    public class WasmAccelerator : KernelAccelerator<WasmCompiledKernel, WasmKernel>
    {
        // OOB diagnostic: last dispatch's view layout (for error messages)
        private string _lastViewLayoutDiag = "";

        // Pending async work (kernel dispatches)
        internal readonly List<Task> _pendingWork = new();
        public static string? _lastImplicitIndexDebug;
        public static string? _lastStructSerialDebug;

        // Worker count for parallel dispatch
        private int _workerCount = 4;

        /// <summary>
        /// Per-dispatch watchdog timeout in seconds. If the worker tasks for a single
        /// dispatch don't all complete within this window, throw a <see cref="TimeoutException"/>
        /// with diagnostic context (kernel name, dispatch number, completion ratio).
        ///
        /// Default 120s — large enough that legitimately-slow Wasm kernels (large Conv2D
        /// on big tensors) complete in time, small enough that an infinite-loop kernel
        /// surfaces in 2 minutes instead of hitting the outer harness 10-minute timeout.
        /// Set to 0 to disable. Surfaced 2026-05-04 by Data's StyleMosaic Wasm hang
        /// where the kernel didn't error but never posted back.
        /// </summary>
        public static int WasmDispatchWatchdogSeconds { get; set; } = 120;

        /// <summary>
        /// Maximum SharedArrayBuffer-backed WebAssembly.Memory size in 64 KiB pages.
        /// Set at construction from <see cref="WasmBackendOptions.MaxLinearMemoryPages"/>
        /// (default 16384 / 1 GiB). Used as the <c>maximum</c> argument when the accelerator
        /// allocates the cached <c>WebAssembly.Memory</c> on first dispatch — once the cached
        /// memory exists, <c>memory.grow</c> can extend its <c>initial</c> up to this value
        /// but cannot raise the <c>maximum</c>. Override before the first dispatch.
        /// </summary>
        private int _maxLinearMemoryPages = 16384;

        /// <summary>
        /// Number of Web Workers actually used for parallel kernel dispatch.
        /// Set at construction from <see cref="WasmBackendOptions.WorkerCount"/>
        /// or the default <c>Math.Max(2, navigator.hardwareConcurrency - 2)</c>.
        /// Read-only at runtime; pass <c>WasmBackendOptions.WorkerCount</c> to
        /// <see cref="Create"/> to override.
        /// </summary>
        public int WorkerCount => _workerCount;

        /// <summary>
        /// Maximum SharedArrayBuffer-backed WebAssembly.Memory size in 64 KiB pages,
        /// configured at <see cref="Create"/> time via <see cref="WasmBackendOptions.MaxLinearMemoryPages"/>.
        /// </summary>
        public int MaxLinearMemoryPages => _maxLinearMemoryPages;

        /// <summary>
        /// Reusable worker pool — lazily initialized on first dispatch.
        /// Workers are created once and reused across kernel dispatches.
        /// </summary>
        private WorkerPool? _workerPool;

        /// <summary>
        /// Tracks which pool workers have already received and cached
        /// the compiled Wasm module, KEYED BY wasmBytes reference. Multi-kernel
        /// pipelines (e.g. ML StyleMosaic alternating Conv2D / InstanceNorm /
        /// ReLU / Add) share the worker pool and would force a full re-init
        /// (Wasm module re-compile) on every kernel change if we tracked only a
        /// single set. With per-kernel tracking, each worker compiles each
        /// distinct kernel once and reuses the cached instance forever after.
        ///
        /// Dictionary key = wasmBytes reference (object identity). Same kernel
        /// re-loaded with the same WasmCompiledKernel instance shares the byte[]
        /// reference (CompiledKernel caches its bytes). Different kernel = different
        /// byte[] = different key.
        ///
        /// Surfaced 2026-05-04 by Data's StyleMosaic Wasm 10+ minute hang at rc.16:
        /// ~100 dispatches × ~6 alternating kernel types × 8 workers = ~4800 wasmBytes
        /// re-sends and full module re-compiles. Each compile of a non-trivial kernel
        /// takes 50-100ms; product = 4-8 minutes of Wasm-side compile work alone.
        /// </summary>
        private readonly Dictionary<byte[], KernelCacheEntry> _initializedWorkersByKernel = new();

        /// <summary>
        /// Monotonic source for the worker-side kernel-cache id (<see cref="KernelCacheEntry.KernelId"/>).
        /// MUST be unique-and-stable, NOT <c>RuntimeHelpers.GetHashCode(wasmBytes)</c>: an identity hash
        /// is a heuristic that RECYCLES under Mono/Wasm GC, so two distinct LIVE kernels can collide on a
        /// single id and stomp each other's worker-side module slot (<c>_modulesById[kid]</c>, WorkerPool.cs)
        /// — a worker then runs the WRONG cached module, producing silent sort corruption that only surfaces
        /// under full-sweep kernel churn. <see cref="Interlocked.Increment(ref int)"/> is collision-proof by
        /// construction. See Wasm/CLAUDE.md "kernelId MUST be a monotonic unique id".
        /// </summary>
        private static int _nextKernelId = 0;

        /// <summary>
        /// Worker-init tracking for one distinct kernel (keyed by its <c>wasmBytes</c> reference in
        /// <see cref="_initializedWorkersByKernel"/>). Carries a stable, unique <see cref="KernelId"/>
        /// (the worker-side <c>_modulesById</c> key) plus the set of workers that have already received
        /// and compiled this kernel's bytes (so they aren't re-sent).
        /// </summary>
        private sealed class KernelCacheEntry
        {
            /// <summary>Stable, process-unique id for this kernel's worker-side module cache slot.</summary>
            public int KernelId;
            /// <summary>Workers that already hold this kernel's compiled module.</summary>
            public readonly HashSet<Worker> Workers = new();
        }

        /// <summary>
        /// Per-worker dispatch state for persistent handlers (Hypothesis #1, 2026-04-26).
        /// The previous design attached/detached OnMessage and OnError handlers on every
        /// dispatch. That was suspected of triggering V8 deopt cycles that exposed timing
        /// windows in the wait/notify spinwait race. With persistent handlers, each worker
        /// has exactly ONE OnMessage and ONE OnError listener installed for its lifetime.
        /// Per-dispatch we update the state's CurrentTcs + diagnostic context before
        /// PostMessage, and the persistent handler reads CurrentTcs when the response
        /// arrives.
        ///
        /// Single-thread-safe by Blazor WASM's single-threaded execution model: the event
        /// loop runs handlers as microtasks, never interleaving with the C# call setting
        /// CurrentTcs.
        /// </summary>
        private readonly Dictionary<Worker, WorkerDispatchState> _workerHandlers = new();

        /// <summary>
        /// Per-worker mutable dispatch state. The TCS is swapped per dispatch; the handlers
        /// read it when a worker response arrives.
        /// </summary>
        private sealed class WorkerDispatchState
        {
            public TaskCompletionSource? CurrentTcs;
            public int WorkerIdx;
            public int DispNum;
            public bool HasBarriers;
            public int ScratchBase;
            public int SharedMemBase;
            public int FenceSlot;
            public int GroupSize;
            public int ScratchPerThread;
            public int TotalItems;
            public int WorkerCount;
            public string ViewLayoutDiag = "";
            // Kernel name for error reporting — surfaced 2026-05-04 by Data needing
            // to identify which op trapped at dispatch 176 in StyleMosaic Wasm.
            public string KernelName = "";
            public Action<MessageEvent>? MsgHandler;
            public Action<Event>? ErrHandler;
        }

        /// <summary>
        /// Cached WebAssembly.Memory to avoid per-frame allocation.
        /// SharedArrayBuffer-backed memories reserve large virtual address space,
        /// so creating a new one every frame causes OOM.
        /// </summary>
        private JSObject? _cachedWasmMemory;
        private SharedArrayBuffer? _cachedMemoryBuffer;
        private int _cachedWasmPages;

        /// <summary>
        /// Counts how many RunKernelAsync dispatches are currently active.
        /// Incremented BEFORE the async task starts so the count is visible
        /// during the synchronous phase of each task.
        /// </summary>
        internal int _activeDispatchCount;

        // Tracks every per-worker TaskCompletionSource currently awaiting a worker response.
        // On Dispose, these are faulted with ObjectDisposedException so that callers awaiting
        // Task.WhenAll do not hang forever after workers are terminated (zombie dispatch).
        private readonly List<TaskCompletionSource> _pendingTcs = new();
        private readonly object _pendingTcsLock = new();
        private volatile bool _disposed;

        private void RegisterTcs(TaskCompletionSource tcs)
        {
            lock (_pendingTcsLock) _pendingTcs.Add(tcs);
        }

        private void UnregisterTcs(TaskCompletionSource tcs)
        {
            lock (_pendingTcsLock) _pendingTcs.Remove(tcs);
        }

        // --- Reflection caches for hot-path stride extraction and struct marshaling ---
        private static readonly ConcurrentDictionary<Type, StrideReflectionCache> _strideCache = new();
        private static readonly ConcurrentDictionary<Type, MethodInfo> _unsafeWriteCache = new();

        private sealed class StrideReflectionCache
        {
            public PropertyInfo? StrideProp;
            public PropertyInfo? YStrideProp;
            public PropertyInfo? XStrideProp;
            public PropertyInfo? ZStrideProp;
        }

        private static StrideReflectionCache GetOrCreateStrideCache(Type argType, Type strideType)
        {
            // Two-level cache: argType → StrideProp, strideType → YStride/XStride/ZStride
            var argCache = _strideCache.GetOrAdd(argType, t => new StrideReflectionCache
            {
                StrideProp = t.GetProperty("Stride"),
            });
            if (argCache.StrideProp != null && argCache.YStrideProp == null && argCache.XStrideProp == null)
            {
                // Populate stride sub-properties from the stride object's type (done once per stride type)
                argCache.YStrideProp = strideType.GetProperty("YStride");
                argCache.XStrideProp = strideType.GetProperty("XStride");
                argCache.ZStrideProp = strideType.GetProperty("ZStride");
            }
            return argCache;
        }

        // Backend instance
        public WasmBackend Backend { get; private set; } = null!;

        /// <summary>
        /// Method info for the static RunKernel method used by dynamic kernel launchers.
        /// </summary>
        public static readonly MethodInfo RunKernelMethod = typeof(WasmAccelerator).GetMethod(
            nameof(RunKernel),
            BindingFlags.Public | BindingFlags.Static)!;

        #region Construction

        private WasmAccelerator(Context context, Device device) : base(context, device) { }

        /// <summary>
        /// Creates a new Wasm accelerator.
        /// </summary>
        public static async Task<WasmAccelerator> Create(Context context)
        {
            return await Create(context, new WasmBackendOptions());
        }

        /// <summary>
        /// Creates a new Wasm accelerator with options.
        /// </summary>
        public static async Task<WasmAccelerator> Create(Context context, WasmBackendOptions options)
        {
            var device = new WasmILGPUDevice();
            var accelerator = new WasmAccelerator(context, device);
            // Default to navigator.hardwareConcurrency - 2, leaving 2 cores
            // for the browser UI thread + Mono runtime + OS. This avoids
            // starving the host machine when one tab runs heavy compute and
            // dramatically reduces oversubscription severity in multi-tab
            // scenarios (each tab's pure-spin barrier needs the OS scheduler
            // to actually run all workers within the spin window). Override
            // with WasmBackendOptions.WorkerCount.
            int hwConcurrency = 4;
            try
            {
                var js = BlazorJSRuntime.JS;
                hwConcurrency = js.Get<int>("navigator.hardwareConcurrency");
            }
            catch { }
            accelerator._workerCount = options?.WorkerCount ?? Math.Max(2, hwConcurrency - 2);
            accelerator._maxLinearMemoryPages = options?.MaxLinearMemoryPages ?? 16384;
            // Variant C (Trip 2026-05-27, revised after TJ's Fallout76 contention repro):
            // default to ENABLED. The original section-9 design auto-gated on
            // (WorkerCount > hwConcurrency-2), assuming healthy default-config accelerators
            // never need yield. That assumption FAILS under external CPU contention (TJ's
            // Fallout76 case + 2 concurrent test runs pegging all cores): even with default
            // WorkerCount, external load can deschedule workers long enough that pure-spin
            // livelocks (Data 2026-04-28: "0 iters in 30 min" at 2-tab oversub). External
            // contention is unpredictable at construction time, so the safe default is
            // always-on Variant C: yield-to-JS on threshold + Atomics.wait(Infinity) park +
            // producer-side env.notify. Healthy-machine overhead is small (per-barrier
            // notify call with no waiters parked = ~500ns). Caller can opt out by setting
            // options.EnableYieldEscape = false to get byte-equivalent v4.8.0 pure-spin
            // (useful for micro-benchmarks where you want zero notify overhead AND you
            // KNOW the machine is healthy).
            options ??= new WasmBackendOptions();
            options.EnableYieldEscape ??= true;
            var backend = new WasmBackend(context, options);
            accelerator.Backend = backend;
            accelerator.Init(backend);
            accelerator.DefaultStream = accelerator.CreateStreamInternal();
            return accelerator;
        }

        #endregion

        #region Kernel Management

        /// <inheritdoc/>
        protected override WasmKernel CreateKernel(WasmCompiledKernel compiledKernel)
        {
            return new WasmKernel(this, compiledKernel, default!);
        }

        /// <inheritdoc/>
        protected override WasmKernel CreateKernel(WasmCompiledKernel compiledKernel, MethodInfo launcher)
        {
            return new WasmKernel(this, compiledKernel, launcher);
        }

        /// <inheritdoc/>
        protected override MethodInfo GenerateKernelLauncherMethod(
            WasmCompiledKernel wasmKernel, int customGroupSize)
        {
            // Build method signature: (Kernel, AcceleratorStream, indexType, param0, param1, ...)
            // This matches the pattern that Kernel.CreateLauncherDelegate binds:
            //   Delegate.CreateDelegate(typeof(TDelegate), kernel, launcher)
            // where 'kernel' becomes the first arg.
            var parameters = wasmKernel.EntryPoint.Parameters;
            var indexType = wasmKernel.EntryPoint.KernelIndexType;
            var argTypes = new List<Type> { typeof(Kernel), typeof(AcceleratorStream), indexType };
            for (int i = 0; i < parameters.Count; i++) argTypes.Add(parameters[i]);

            var dynamicMethod = new DynamicMethod(
                "WasmLauncher",
                typeof(void),
                argTypes.ToArray(),
                typeof(WasmAccelerator).Module);

            var ilGenerator = dynamicMethod.GetILGenerator();
            var argsLocal = ilGenerator.DeclareLocal(typeof(object[]));

            // Create object[] for kernel arguments
            ilGenerator.Emit(OpCodes.Ldc_I4, parameters.Count);
            ilGenerator.Emit(OpCodes.Newarr, typeof(object));
            ilGenerator.Emit(OpCodes.Stloc, argsLocal);

            // Pack each argument
            for (int i = 0; i < parameters.Count; i++)
            {
                ilGenerator.Emit(OpCodes.Ldloc, argsLocal);
                ilGenerator.Emit(OpCodes.Ldc_I4, i);
                ilGenerator.Emit(OpCodes.Ldarg, i + 3); // Skip Kernel, AcceleratorStream, dimension
                var paramType = parameters[i];
                if (paramType.IsValueType) ilGenerator.Emit(OpCodes.Box, paramType);
                ilGenerator.Emit(OpCodes.Stelem_Ref);
            }

            // Call RunKernel(kernel, stream, dimension, args)
            ilGenerator.Emit(OpCodes.Ldarg_0); // kernel
            ilGenerator.Emit(OpCodes.Ldarg_1); // stream
            ilGenerator.Emit(OpCodes.Ldarg_2); // dimension (index)
            if (indexType.IsValueType) ilGenerator.Emit(OpCodes.Box, indexType);
            ilGenerator.Emit(OpCodes.Ldloc, argsLocal);
            ilGenerator.EmitCall(OpCodes.Call, RunKernelMethod, null);
            ilGenerator.Emit(OpCodes.Ret);

            return dynamicMethod;
        }

        #endregion

        #region Kernel Execution

        /// <summary>
        /// Executes a Wasm kernel. Called by the dynamic launcher.
        /// </summary>
        /// <summary>
        /// Recent dispatch log for diagnostics. Captures last N kernel dispatches.
        /// </summary>
        public static string _dispatchLog = "";
        public static int _dispatchCount = 0;

        public static void RunKernel(Kernel kernel, AcceleratorStream stream, object dimension, object[] args)
        {
            var wasmAccel = (WasmAccelerator)kernel.Accelerator;
            var wasmKernel = (WasmKernel)kernel;
            var compiledKernel = (WasmCompiledKernel)wasmKernel.CompiledKernel;

            // Reject dispatches on a disposed accelerator so the caller fails fast instead of
            // silently hanging on a worker pool that was already torn down.
            if (wasmAccel._disposed)
                throw new ObjectDisposedException(nameof(WasmAccelerator), "Cannot dispatch kernels on a disposed WasmAccelerator.");

            int dispNum = ++_dispatchCount;

            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Debug] RunKernel called with {args.Length} args");

            // Register a queue-time snapshot intent on each unique WasmMemoryBuffer
            // referenced by this dispatch. The intent is a lightweight counter
            // bump + capture of the buffer's current HostWriteCounter — NO bytes
            // are copied here. A real snapshot SAB is allocated lazily, only IF a
            // host write fires (CopyFromHost / CopyFromJS / CopyFrom override)
            // while at least one intent is pending. Multi-pass kernels with no
            // intervening host writes never trigger materialization (RadixSort
            // reads its prior dispatch's copy-OUT data via SharedBuffer); ML
            // pipelines with reused weight buffers similarly skip the allocation.
            //
            // Closes the host-write-vs-queued-dispatch race (Tests23_HostWriteVs-
            // QueuedDispatchRace, 2026-05-04 YOLOv8 Wasm Softmax) AND the rc.13-
            // lazily-cached-snapshot regression that pinned pre-pass-1 data
            // across multi-pass RadixSort dispatches (TJ regression report
            // 2026-05-05). Replaces the previous eager snapshot path that ate
            // 5GB of SAB allocations on Data's StyleMosaic 102-dispatch
            // pipeline. (rc.16 RadixSort multi-pass + StyleMosaic perf, 2026-05-05.)
            var argDispatchIntents = new Dictionary<WasmMemoryBuffer, int>();
            for (int ai = 0; ai < args.Length; ai++)
            {
                var arg = args[ai];
                if (arg is IArrayView iav && iav.Buffer is WasmMemoryBuffer wb && !argDispatchIntents.ContainsKey(wb))
                {
                    argDispatchIntents[wb] = wb.RegisterDispatchIntent();
                }
                else if (arg != null && arg.GetType().IsValueType && !arg.GetType().IsPrimitive)
                {
                    // Struct-with-embedded-views (e.g. body-struct kernel params): walk
                    // fields and register intent on any IArrayView's underlying buffer.
                    try
                    {
                        var fields = arg.GetType().GetFields(
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        foreach (var f in fields)
                        {
                            var fv = f.GetValue(arg);
                            if (fv is IArrayView fav && fav.Buffer is WasmMemoryBuffer fwb && !argDispatchIntents.ContainsKey(fwb))
                            {
                                argDispatchIntents[fwb] = fwb.RegisterDispatchIntent();
                            }
                        }
                    }
                    catch { /* defensive — non-fatal */ }
                }
            }

            // Increment active dispatch count BEFORE starting the async task,
            // so the count is visible during the task's synchronous execution phase.
            wasmAccel._activeDispatchCount++;
            var task = wasmAccel.RunKernelAsync(compiledKernel, dimension, args, dispNum, argDispatchIntents);
            wasmAccel._pendingWork.Add(task);
        }

        private async Task RunKernelAsync(
            WasmCompiledKernel compiledKernel,
            object dimension,
            object[] args,
            int dispNum = 0,
            Dictionary<WasmMemoryBuffer, int>? argDispatchIntents = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WasmAccelerator));

            // Serialize kernel execution: wait for all previous dispatches to complete
            // before starting a new one. Prevents data races in multi-kernel algorithms.
            if (_pendingWork.Count > 0)
            {
                var pending = _pendingWork.ToArray();
                _pendingWork.Clear();
                await Task.WhenAll(pending);
            }

            // A Dispose could have happened while we awaited above. Bail out before
            // re-creating the worker pool below, which would resurrect a disposed accelerator.
            if (_disposed)
                throw new ObjectDisposedException(nameof(WasmAccelerator));

            try
            {
                var js = BlazorJSRuntime.JS;
                var (gridDimX, gridDimY, gridDimZ) = GetGridDimensions(dimension);
                int totalItems = gridDimX * gridDimY * gridDimZ;

                // Extract actual group size for barrier kernels
                int groupSize = GetGroupSize(dimension, compiledKernel);
                int numGroups = compiledKernel.HasBarriers && groupSize > 1
                    ? Math.Max(1, (totalItems + groupSize - 1) / groupSize)
                    : totalItems;

                // Real per-dimension group sizes so the kernel can decompose the linear thread/group
                // ids into per-dimension Group.Idx / Grid.Idx (2D/3D groups, mirroring the CPU backend).
                // An explicit KernelConfig carries GroupDim; auto-grouped (Index) launches are 1D.
                int realGroupDimX = groupSize, realGroupDimY = 1;
                if (dimension is KernelConfig groupConfig)
                {
                    realGroupDimX = Math.Max(1, groupConfig.GroupDim.X);
                    realGroupDimY = Math.Max(1, groupConfig.GroupDim.Y);
                }

                // Extract dynamic shared memory size from KernelConfig
                int dynamicSharedElements = 0;
                if (dimension is KernelConfig kConfig)
                    dynamicSharedElements = kConfig.SharedMemoryConfig.NumElements;

                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] Dispatching kernel: {totalItems} items ({gridDimX}x{gridDimY}x{gridDimZ}), groupSize={groupSize}, numGroups={numGroups}, hasBarriers={compiledKernel.HasBarriers}, dynamicSharedElements={dynamicSharedElements}");

                // Determine isView flags from compiled kernel metadata
                var paramInfos = compiledKernel.ParamInfos;

                // Skip the implicit extent argument (args[0]) when it's an Index or LongIndex value.
                // These are automatically added by ILGPU's kernel launcher and correspond to the
                // kernel's implicit index parameter (which is mapped to globalIdx in the Wasm func).
                // Skip the implicit extent argument (args[0]) only when it's an Index value.
                // Index types represent the kernel's implicit extent parameter which is
                // mapped to _globalIdxLocal in the kernel. LongIndex types are NOT skipped
                // because they represent data like paddedNumDataElements in GridStrideLoopKernel
                // which is used as the loop bound (different from globalIdx).
                bool hasImplicitIndex = args.Length > 0
                    && (args[0] is Index1D || args[0] is Index2D || args[0] is Index3D);
                if (_dispatchCount <= 4)
                    _lastImplicitIndexDebug = $"[D{_dispatchCount}] hasImpl={hasImplicitIndex}, dim={dimension}, argCnt={args.Length}, piCnt={paramInfos.Count}";
                else
                    _lastImplicitIndexDebug = $"[D{_dispatchCount}]";
                _lastStructSerialDebug = null;
                int argOffset = hasImplicitIndex ? 1 : 0;

                // Collect UNIQUE buffers (dedup SubViews of the same buffer) and
                // track per-view SubView byte offsets within the buffer.
                var uniqueBuffers = new Dictionary<WasmMemoryBuffer, int>(); // buffer → index
                var bufferInfos = new List<(WasmMemoryBuffer buffer, int byteOffset)>();
                var bufferRanges = new List<(int minByte, int maxByte)>(); // per-buffer: used byte range
                var viewBufferIdx = new List<int>();   // per-view: which buffer in bufferInfos
                var viewSubOffsets = new List<int>();   // per-view: SubView byte offset
                var viewElemSizes = new List<int>();    // per-view: bytes per element (from view's generic arg)
                var wasmArgs = new List<(bool isBuffer, WasmMemoryBuffer? buffer, int length, int stride, int stride2, object? value)>();
                // IR param index for each struct wasmArg (wasmArgIdx drifts when body-struct views expand).
                var wasmStructIrParamIndices = new List<int>();

                // Per-buffer host-write counter snapshots taken at copy-IN time.
                // The copy-OUT phase compares each buffer's current `HostWriteCounter`
                // to its snapshot; if they DIFFER, the host wrote to SharedBuffer
                // during the in-flight dispatch (host's intervening `CopyFromCPU` /
                // `CopyFromJS` / `CopyFromHost`) and copy-OUT skips that buffer to
                // preserve the host's write. Closes the 2026-05-03 Wasm copy-OUT
                // race (per `_DevComms/SpawnDev.ILGPU/geordi-to-team-wasm-copy-out-race-2026-05-03.md`).
                // Trace-coverage-independent — works for every kernel shape.
                var hostWriteSnapshot = new int[bufferInfos.Capacity > 0 ? bufferInfos.Capacity : 16];
                // (Unused) legacy diag snapshot kept for binary-compat with consumers
                // that read LastDispatchCopyOutDiag.
                var writtenBufferIndices = new HashSet<int>();
                if (WasmBackend.VerboseLogging)
                    WasmBackend.LastDispatchCopyOutDiag = new List<string>();

                for (int i = argOffset; i < args.Length; i++)
                {
                    int paramIdx = i - argOffset;
                    bool isView = paramIdx < paramInfos.Count && paramInfos[paramIdx].IsView;

                    // If the codegen marked this as a view but the CLR arg isn't IArrayView,
                    // it's a struct-with-embedded-view (e.g., ViewSourceSequencer). Extract
                    // the first IArrayView field to use as the view arg.
                    IArrayView iav = args[i] as IArrayView;
                    if (isView && iav == null)
                    {
                        // Find first IArrayView field in the struct
                        var structFields = args[i]?.GetType().GetFields(
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (structFields != null)
                        {
                            foreach (var sf in structFields)
                            {
                                try
                                {
                                    if (sf.GetValue(args[i]) is IArrayView innerView)
                                    {
                                        iav = innerView;
                                        break;
                                    }
                                    // Check nested structs (e.g., ViewSourceSequencer.ViewSource is ArrayView1D)
                                    var val = sf.GetValue(args[i]);
                                    if (val != null && val.GetType().IsValueType && !val.GetType().IsPrimitive)
                                    {
                                        var innerFields = val.GetType().GetFields(
                                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        foreach (var inf in innerFields)
                                        {
                                            if (inf.GetValue(val) is IArrayView deepView)
                                            {
                                                iav = deepView;
                                                break;
                                            }
                                        }
                                        if (iav != null) break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] Embedded view extraction failed for param {i}: {ex.GetType().Name}: {ex.Message}");
                                }
                            }
                        }
                    }

                    if (isView && iav != null)
                    {
                        // ArrayView<T> is a value struct that implements IArrayView.
                        // Extract the underlying MemoryBuffer via .Buffer property.
                        var wasmBuf = iav.Buffer as WasmMemoryBuffer;
                        if (wasmBuf != null)
                        {
                            // Deduplicate: add each unique buffer only once
                            if (!uniqueBuffers.TryGetValue(wasmBuf, out int bufIdx))
                            {
                                bufIdx = bufferInfos.Count;
                                uniqueBuffers[wasmBuf] = bufIdx;
                                bufferInfos.Add((wasmBuf, 0));
                                bufferRanges.Add((int.MaxValue, 0)); // will be updated below
                            }
                            viewBufferIdx.Add(bufIdx);

                            // If the codegen flagged this kernel parameter as a Store target,
                            // mark the underlying buffer for copy-OUT. Otherwise we treat the
                            // buffer as input-only for this dispatch and skip its copy-OUT
                            // (avoids clobbering host CopyFromCPU writes that arrived during
                            // the dispatch — the 2026-05-03 Wasm copy-OUT race). Falls back
                            // to copy-all when the codegen trace found no writes to any
                            // buffer-typed param (defensive — don't break unknown kernels).
                            //
                            // Match against the codegen IR-param-index space. Use the offset
                            // recorded at compile time (SetupParameters startIdx): 1 when IR
                            // param[0] is the implicit Index mapped to globalIdx, 0 when IR
                            // param[0] is LongIndex1D extent (GridStrideLoopKernel) or an
                            // explicit body struct at param[0].
                            int irParamIdx = paramIdx + compiledKernel.IrUserParamIndexOffset;
                            if (compiledKernel.WrittenParamIndices.Contains(irParamIdx))
                                writtenBufferIndices.Add(bufIdx);
                            if (WasmBackend.VerboseLogging)
                                WasmBackend.LastDispatchCopyOutDiag.Add(
                                    $"i={i} paramIdx={paramIdx} irParam={irParamIdx} bufIdx={bufIdx} written={writtenBufferIndices.Contains(bufIdx)}");

                            // Compute SubView byte offset within the buffer.
                            // Capture the view's element size (NOT the buffer's) so we can
                            // also use it for the byte-length calculation below — required
                            // when a buffer is Cast (e.g. `int[]` allocation viewed as
                            // `ArrayView<long>` via `.Cast<long>()`). buffer.ElementSize
                            // would underreport by 2× and the back half of the view would
                            // be off the end of the allocated wasm memory range, producing
                            // an OOB at dispatch time. Surfaced 2026-05-04 by Tuvok's
                            // Vp9FrameEntropyKernel `ArrayView<long> outLen` trap (V4=4
                            // bytes wide where it should be 8).
                            int viewElemSizeForLength = wasmBuf.ElementSize;
                            int subViewByteOffset = 0;
                            try
                            {
                                var viewType = args[i].GetType();
                                var baseProp = viewType.GetProperty("BaseView");
                                object viewObj = baseProp != null ? baseProp.GetValue(args[i])! : args[i];
                                // Get the actual element size from the view type.
                                var viewGenericType = viewObj.GetType();
                                if (viewGenericType.IsGenericType)
                                {
                                    var elemType = viewGenericType.GetGenericArguments()[0];
                                    int actualSize = global::ILGPU.Interop.SizeOf(elemType);
                                    if (actualSize > 0)
                                        viewElemSizeForLength = actualSize;
                                }
                                var indexProp = viewObj.GetType().GetProperty("Index",
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (indexProp != null)
                                {
                                    var idx = indexProp.GetValue(viewObj);
                                    if (idx is long longIdx)
                                    {
                                        subViewByteOffset = (int)(longIdx * viewElemSizeForLength);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                // SubView offset extraction failed — offset defaults to 0.
                                // This is WRONG for SubViews with non-zero indices (e.g., in-place
                                // RadixSort where output SubView is at a different offset).
                                // Log when verbose — non-fatal extraction path.
                                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SubView] OFFSET EXTRACTION FAILED for param {i}: {ex.GetType().Name}: {ex.Message}");
                            }
                            viewSubOffsets.Add(subViewByteOffset);
                            viewElemSizes.Add(viewElemSizeForLength);

                            // Update the buffer's used byte range to include this SubView.
                            // Use the VIEW's element size (computed above) — NOT the
                            // buffer's — so Cast views produce the right byte length.
                            int viewByteLen = (int)iav.Length * viewElemSizeForLength;
                            int viewEnd = subViewByteOffset + viewByteLen;
                            var (curMin, curMax) = bufferRanges[bufIdx];
                            bufferRanges[bufIdx] = (Math.Min(curMin, subViewByteOffset), Math.Max(curMax, viewEnd));

                            // Log buffer identity for dispatch debugging
                            if (dispNum >= 1 && dispNum <= 20)
                                if (WasmBackend.VerboseLogging) _dispatchLog += $"|D{dispNum}V{i}:buf={wasmBuf.GetHashCode()%1000},sub={subViewByteOffset},len={iav.Length}";

                            // Extract stride via cached reflection for multi-dimensional views
                            int stride = 1;
                            int stride2 = 0;
                            var argType = args[i].GetType();
                            var argCache = _strideCache.GetOrAdd(argType, t => new StrideReflectionCache
                            {
                                StrideProp = t.GetProperty("Stride"),
                            });
                            if (argCache.StrideProp != null)
                            {
                                var strideObj = argCache.StrideProp.GetValue(args[i]);
                                if (strideObj != null)
                                {
                                    var strideType = strideObj.GetType();
                                    // Lazily populate stride sub-properties (once per type)
                                    if (argCache.YStrideProp == null && argCache.XStrideProp == null)
                                    {
                                        argCache.YStrideProp = strideType.GetProperty("YStride");
                                        argCache.XStrideProp = strideType.GetProperty("XStride");
                                        argCache.ZStrideProp = strideType.GetProperty("ZStride");
                                    }

                                    if (argCache.YStrideProp != null)
                                        stride = (int)argCache.YStrideProp.GetValue(strideObj)!;
                                    else if (argCache.XStrideProp != null)
                                        stride = (int)argCache.XStrideProp.GetValue(strideObj)!;

                                    if (argCache.ZStrideProp != null)
                                        stride2 = (int)argCache.ZStrideProp.GetValue(strideObj)!;
                                }
                            }
                            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] View arg[{i}]: length={iav.Length}, stride={stride}, stride2={stride2}");
                            wasmArgs.Add((true, wasmBuf, (int)iav.Length, stride, stride2, null));
                        }
                        else
                        {
                            wasmArgs.Add((false, null, 0, 0, 0, 0));
                        }
                    }
                    else
                    {
                        // Check if this is a struct with embedded views — decompose it
                        bool decomposed = false;
                        if (args[i] != null && ((args[i].GetType().IsValueType
                            && !args[i].GetType().IsPrimitive && !args[i].GetType().IsEnum)
                            || args[i].GetType().IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)))
                        {
                            var structFields = args[i].GetType().GetFields(
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            bool hasViews = false;
                            foreach (var sf in structFields)
                            {
                                try { if (sf.GetValue(args[i]) is IArrayView) { hasViews = true; break; } }
                                catch (Exception ex) { if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] Struct view check failed for field {sf.Name}: {ex.Message}"); }
                            }

                            if (hasViews)
                            {
                                // Struct with embedded views: extract their buffers for
                                // copy-in/copy-out. The struct itself is serialized to scratch
                                // (NOT decomposed — ILGPU keeps it as a single param).
                                // NativePtr patching ensures the view pointer in the serialized
                                // bytes points to the correct Wasm memory offset.
                                ExtractBuffersFromStruct(args[i], uniqueBuffers, bufferInfos, bufferRanges);
                            }
                        }

                        // Map dispatch user-arg index to IR parameter index (see IrUserParamIndexOffset).
                        int irParamIndexForArg = paramIdx + compiledKernel.IrUserParamIndexOffset;
                        WasmParamInfo? piForArg = null;
                        foreach (var piScan in paramInfos)
                        {
                            if (piScan.Index == irParamIndexForArg)
                            {
                                piForArg = piScan;
                                break;
                            }
                        }

                        if (!decomposed && piForArg?.IsBodyStruct == true && args[i] != null)
                        {
                            AddBodyStructExpandedViewWasmArgs(
                                args[i],
                                i,
                                paramIdx,
                                paramInfos,
                                compiledKernel,
                                wasmArgs,
                                uniqueBuffers,
                                bufferInfos,
                                bufferRanges,
                                viewBufferIdx,
                                viewSubOffsets,
                                viewElemSizes,
                                writtenBufferIndices,
                                dispNum);
                        }

                        if (!decomposed)
                        {
                            // Unwrap LongIndex types to their underlying long value
                            object arg = args[i];
                            if (arg is LongIndex1D li1) arg = li1.X;
                            else if (arg is LongIndex2D)
                                throw new NotSupportedException("LongIndex2D kernel parameters are not yet supported on the Wasm backend. Use LongIndex1D or restructure the kernel.");
                            else if (arg is LongIndex3D)
                                throw new NotSupportedException("LongIndex3D kernel parameters are not yet supported on the Wasm backend. Use LongIndex1D or restructure the kernel.");
                            wasmArgs.Add((false, null, 0, 0, 0, arg));
                            if (arg != null && (
                                (arg.GetType().IsValueType && !arg.GetType().IsPrimitive && !arg.GetType().IsEnum)
                                || arg.GetType().IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)))
                            {
                                wasmStructIrParamIndices.Add(piForArg?.Index ?? irParamIndexForArg);
                            }
                        }
                    }
                }

                // Calculate total memory needed for all buffers.
                // For deduped buffers (SubViews of the same parent), allocate only the
                // union of SubView ranges — NOT the full parent buffer. This prevents
                // ML inference from OOMing Wasm memory (100+ SubViews of a 5MB weight buffer
                // would allocate 500MB instead of ~5MB).
                int totalMemoryBytes = 0;
                var bufferOffsets = new List<int>();

                for (int bi = 0; bi < bufferInfos.Count; bi++)
                {
                    var (buf, _) = bufferInfos[bi];
                    var (rangeMin, rangeMax) = bufferRanges[bi];
                    // DISABLED: Range optimization breaks multi-pass algorithms (RadixSort)
                    // that share a buffer via different SubViews across dispatches.
                    // Buffer deduplication (uniqueBuffers) already ensures each parent buffer
                    // is allocated only ONCE per dispatch regardless of SubView count.
                    // The ML OOB fix relies on dedup, not range trimming.
                    bufferRanges[bi] = (0, (int)buf.LengthInBytes);
                    int rangeSize = (int)buf.LengthInBytes;
                    totalMemoryBytes = (totalMemoryBytes + 7) & ~7; // 8-byte align
                    bufferOffsets.Add(totalMemoryBytes);
                    totalMemoryBytes += rangeSize;
                }
                // Grid-stride padding: kernels with KernelConfig (groupSize > 1) use
                // grid-stride loops that may access up to one stride past buffer boundaries.
                // Max overshoot = one stride = gridDimX elements. For auto-grouped kernels
                // (groupSize=1), no grid-stride overshoot. 8 bytes per element covers all types.
                // Grid-stride padding: kernels with KernelConfig (groupSize > 1) use
                // grid-stride loops that may access up to one stride past buffer boundaries.
                if (groupSize > 1)
                    totalMemoryBytes += gridDimX * 8;

                // Scratch memory for struct construction (after all buffers + padding).
                // For barrier kernels, each worker needs its own scratch to avoid races.
                int scratchPerThread = Math.Max(compiledKernel.ScratchPerThread, 64); // min 64 bytes
                int scratchBase = (totalMemoryBytes + 7) & ~7;
                // Compute actual worker count for scratch allocation (must match dispatch)
                int effectiveWorkers = compiledKernel.HasBarriers
                    ? Math.Min(_workerCount, groupSize)  // barrier: full hardwareConcurrency
                    : Math.Min(_workerCount, totalItems); // non-barrier: hardwareConcurrency
                if (effectiveWorkers < 1) effectiveWorkers = 1;
                int scratchSize = compiledKernel.HasBarriers
                    ? scratchPerThread * groupSize  // per-thread scratch for barrier kernels
                    : scratchPerThread * effectiveWorkers; // per-worker scratch (exact, no waste)

                // Pre-compute struct param bytes so we can place them AFTER per-thread
                // scratch. Without this, struct params at scratchBase + 0 overlap with
                // thread 0's scratch, and state saves during barrier yields corrupt the
                // struct fields (e.g., ReducedValue in ReductionImplementation).
                int totalStructBytes = 0;
                foreach (var pi in paramInfos)
                {
                    if (pi.IsScalar && pi.StructSize > 0)
                        totalStructBytes += ((pi.StructSize + 3) & ~3); // 4-byte aligned
                }
                int structRegionBase = (scratchBase + scratchSize + 7) & ~7; // 8-byte align
                int afterScratch = structRegionBase + totalStructBytes;

                // Shared memory region (for barrier kernels)
                int sharedMemBase = (afterScratch + 7) & ~7; // 8-byte align
                int sharedMemSize = compiledKernel.SharedMemorySize;
                // Add dynamic shared memory bytes (element count * element size)
                int dynamicSharedBytes = dynamicSharedElements * Math.Max(compiledKernel.DynamicSharedElementSize, 1);
                if (dynamicSharedElements > 0)
                {
                    sharedMemSize += dynamicSharedBytes;
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] Dynamic shared memory: {dynamicSharedElements} elements x {compiledKernel.DynamicSharedElementSize} bytes = {dynamicSharedBytes} bytes, total shared={sharedMemSize}");
                }
                int afterShared = sharedMemBase + sharedMemSize;

                // Barrier slots region: each barrier = 8 bytes (arrival counter i32 + sense flag i32)
                int barrierBase = (afterShared + 3) & ~3; // 4-byte align
                int barrierSize = compiledKernel.BarrierCount * 8;
                // Inter-worker synchronization region at fenceSlot:
                //   [0..3]   phase arrival counter (i32)
                //   [4..7]   phase generation     (i32)
                //   [8..11]  global yield count   (i32)
                //   [12..15] exit flag            (i32)
                //   [16..19] group arrival       (i32)
                //   [20..23] group generation    (i32)
                //   [24..23 + 16*N] per-worker spin-yield save/restore buffers (16 bytes each)
                //     where N = max workerCount (= _workerCount). Each worker's buffer holds
                //     [yieldFlag, savedG, savedPhase, savedGen]. See WasmBackend.GeneratePhaseDispatcher.
                int fenceSlot = barrierBase + barrierSize;
                int yieldStateRegionBase = fenceSlot + 24;
                int yieldStateRegionSize = 16 * Math.Max(1, _workerCount);
                int totalWithBarriers = yieldStateRegionBase + yieldStateRegionSize;

                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] Memory layout: buffers={totalMemoryBytes}, scratch={scratchBase}(spt={scratchPerThread}), structRegion={structRegionBase}({totalStructBytes}), sharedMem={sharedMemBase}({sharedMemSize}), barrier={barrierBase}({barrierSize}), hasBarriers={compiledKernel.HasBarriers}, groupSize={groupSize}");

                // Round up to Wasm page size (64 KB). totalWithBarriers already exactly
                // accounts for every region (buffers + scratch + struct + shared + barrier
                // + fence + yield-state); the prior 100% margin was gratuitous and pushed
                // ML inference workloads past the 2 GiB SharedArrayBuffer cap on Chromium
                // (Data's StyleMosaic 102-dispatch OOM, 2026-05-05). Replaced with a single
                // 64 KB safety pad to absorb any single-byte miscalculation without doubling
                // the linear-memory footprint.
                int wasmPagesExact = Math.Max(1, (totalWithBarriers + 65535) / 65536);
                int wasmPages = wasmPagesExact + 1; // 1-page (64 KB) absolute safety pad
                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-MEM] disp={dispNum} totalLayout={totalWithBarriers} exactPages={wasmPagesExact} pages={wasmPages} bytes={wasmPages * 65536} cap={_maxLinearMemoryPages * 65536} buf={totalMemoryBytes} scratch={scratchBase}+{scratchSize} struct={structRegionBase}+{totalStructBytes} shared={sharedMemBase}+{sharedMemSize} barrier={barrierBase}+{barrierSize} fence={fenceSlot} spt={scratchPerThread} gs={groupSize} _wc={_workerCount}");

                // DIAGNOSTIC (Tuvok 2026-05-26): force a real 1-page memory.grow on every
                // dispatch to test the SharedArrayBuffer-growth-lag hypothesis for the
                // residual large-sort corruption. Bumping the required page count to one
                // above the cached high-water mark drives the dispatch into the production
                // grow branch below (grow + re-get buffer + _initializedWorkersByKernel
                // clear → worker re-instantiation), exactly as a bigger kernel would. Capped
                // at the linear-memory budget so it degrades to a no-op near the ceiling
                // instead of throwing OOM. RETAINED default-off (grow hypothesis disfavored
                // but not killed) — use to re-test grow if the residual recurs after the
                // monotonic kernelId fix. See WasmBackend.ForceGrowEachDispatch.
                if (WasmBackend.ForceGrowEachDispatch
                    && _cachedWasmMemory != null
                    && _cachedWasmPages + 1 <= _maxLinearMemoryPages)
                {
                    wasmPages = Math.Max(wasmPages, _cachedWasmPages + 1);
                }

                // Determine whether we can reuse the cached memory.
                // If there are other pending kernel dispatches running concurrently,
                // they share the same SharedArrayBuffer and we'd corrupt their data.
                // In that case, create a dedicated per-dispatch memory.
                bool hasConcurrentWork = _activeDispatchCount > 1;
                JSObject wasmMemory;
                SharedArrayBuffer memoryBuffer;
                JSObject? disposeWasmMemory = null;   // track per-dispatch memory for cleanup
                SharedArrayBuffer? disposeBuffer = null;

                if (!hasConcurrentWork && _cachedWasmMemory != null && wasmPages <= _cachedWasmPages)
                {
                    // Reuse cached memory — fast path for render loops (same kernel, no overlap)
                    wasmMemory = _cachedWasmMemory;
                    memoryBuffer = _cachedMemoryBuffer!;
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-MEM-REUSE] disp={dispNum} need={wasmPages} cached={_cachedWasmPages} cap={_maxLinearMemoryPages}");
                }
                else if (!hasConcurrentWork)
                {
                    // No concurrent work, but need bigger (or first) memory.
                    // GROW existing memory instead of creating new — avoids browser
                    // SharedArrayBuffer allocation limits that cause OOM when many
                    // Wasm modules are compiled (e.g., RadixSort pairs pipeline).
                    if (_cachedWasmMemory == null)
                    {
                        // DIAGNOSTIC (Tuvok 2026-05-26, RETAINED default-off): PreGrowPages lets a
                        // run pre-reserve a large initial memory so NO dispatch ever needs
                        // memory.grow — the "remove the suspected cause" test of the SAB-growth-lag
                        // hypothesis (disfavored but not killed). No-op (0) in production.
                        int initialPages = Math.Min(_maxLinearMemoryPages,
                            Math.Max(wasmPages, WasmBackend.PreGrowPages));
                        _cachedWasmPages = initialPages;
                        _cachedWasmMemory = js.Call<JSObject>(
                            "eval",
                            $"new WebAssembly.Memory({{ initial: {initialPages}, maximum: {_maxLinearMemoryPages}, shared: true }})");
                        _cachedMemoryBuffer = _cachedWasmMemory.JSRef!.Get<SharedArrayBuffer>("buffer");
                        _initializedWorkersByKernel.Clear();
                        if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-MEM-INIT] disp={dispNum} pages={initialPages} (need={wasmPages}) bytes={initialPages * 65536} cap={_maxLinearMemoryPages}");
                    }
                    else
                    {
                        // Grow in place — SharedArrayBuffer stays valid, workers see growth
                        int growBy = wasmPages - _cachedWasmPages;
                        if (growBy > 0)
                        {
                            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-MEM-GROW] disp={dispNum} from={_cachedWasmPages} to={wasmPages} growBy={growBy} cap={_maxLinearMemoryPages}");
                            int growResult = _cachedWasmMemory.JSRef!.Call<int>("grow", growBy);
                            if (growResult == -1)
                                throw new OutOfMemoryException($"WebAssembly.Memory.grow({growBy} pages) failed. Current: {_cachedWasmPages} pages, requested: {wasmPages} pages ({wasmPages * 64}KB), cap: {_maxLinearMemoryPages} pages ({_maxLinearMemoryPages * 64}KB)");
                            _cachedWasmPages = wasmPages;
                            // Re-get buffer reference (same SAB but .buffer accessor may update)
                            _cachedMemoryBuffer?.Dispose();
                            _cachedMemoryBuffer = _cachedWasmMemory.JSRef!.Get<SharedArrayBuffer>("buffer");
                            _initializedWorkersByKernel.Clear();
                        }
                    }
                    wasmMemory = _cachedWasmMemory;
                    memoryBuffer = _cachedMemoryBuffer!;
                }
                else
                {
                    // "Concurrent" dispatches — on Blazor WASM (single-threaded), these
                    // are actually serialized via _pendingWork. They appear concurrent because
                    // _activeDispatchCount is incremented before the async task starts.
                    // Reuse cached memory instead of creating new — avoids OOM from
                    // repeated SharedArrayBuffer allocation (the root cause of pairs sort OOM).
                    if (_cachedWasmMemory == null)
                    {
                        // DIAGNOSTIC (Tuvok 2026-05-26): see PreGrowPages note in the
                        // non-concurrent branch above. No-op (0) in production.
                        int initialPages = Math.Min(_maxLinearMemoryPages,
                            Math.Max(wasmPages, WasmBackend.PreGrowPages));
                        _cachedWasmPages = initialPages;
                        _cachedWasmMemory = js.Call<JSObject>(
                            "eval",
                            $"new WebAssembly.Memory({{ initial: {initialPages}, maximum: {_maxLinearMemoryPages}, shared: true }})");
                        _cachedMemoryBuffer = _cachedWasmMemory.JSRef!.Get<SharedArrayBuffer>("buffer");
                        _initializedWorkersByKernel.Clear();
                        if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-MEM-INIT-CC] disp={dispNum} pages={initialPages} (need={wasmPages}) bytes={initialPages * 65536} cap={_maxLinearMemoryPages}");
                    }
                    else if (wasmPages > _cachedWasmPages)
                    {
                        int growBy = wasmPages - _cachedWasmPages;
                        if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-MEM-GROW-CC] disp={dispNum} from={_cachedWasmPages} to={wasmPages} growBy={growBy} cap={_maxLinearMemoryPages}");
                        int growResult = _cachedWasmMemory.JSRef!.Call<int>("grow", growBy);
                        if (growResult == -1)
                            throw new OutOfMemoryException($"WebAssembly.Memory.grow({growBy} pages) failed. Current: {_cachedWasmPages} pages, requested: {wasmPages} pages ({wasmPages * 64}KB), cap: {_maxLinearMemoryPages} pages ({_maxLinearMemoryPages * 64}KB)");
                        _cachedWasmPages = wasmPages;
                        _cachedMemoryBuffer?.Dispose();
                        _cachedMemoryBuffer = _cachedWasmMemory.JSRef!.Get<SharedArrayBuffer>("buffer");
                        _initializedWorkersByKernel.Clear();
                    }
                    wasmMemory = _cachedWasmMemory;
                    memoryBuffer = _cachedMemoryBuffer!;
                }

                // Zero out entire dispatch region including buffer area.
                // Previous dispatches with different buffer layouts leave stale data in
                // the buffer region [0..scratchBase). If a kernel reads from offsets that
                // weren't fully overwritten by Copy-In, it gets garbage or OOB.
                int zeroEnd = totalWithBarriers;
                if (zeroEnd > 0)
                {
                    using var zeroView = new Uint8Array(memoryBuffer, 0, zeroEnd);
                    zeroView.JSRef!.CallVoid("fill", 0);
                }

                // Copy buffer data into the Wasm linear memory at computed offsets.
                // Only the used SubView range is copied (not the full parent buffer).
                // Also snapshot each buffer's `HostWriteCounter` — the copy-OUT
                // phase will compare against the same counter post-dispatch and
                // skip copy-OUT if the counter changed (host wrote to SharedBuffer
                // during the dispatch — preserve that write, don't clobber).
                if (hostWriteSnapshot.Length < bufferInfos.Count)
                    hostWriteSnapshot = new int[bufferInfos.Count];
                // Track per-bufferIndex whether copy-IN read from a queue-time
                // snapshot (vs direct SharedBuffer). copy-OUT below uses this to
                // skip writing back stale snapshot data for inputs whose
                // SharedBuffer has been overwritten by a host write since queue.
                var bufIndicesReadFromSnapshot = new HashSet<int>();
                for (int i = 0; i < bufferInfos.Count; i++)
                {
                    var (buf, _) = bufferInfos[i];
                    int offset = bufferOffsets[i];
                    var (rangeMin, rangeMax) = bufferRanges[i];
                    if (rangeMin == int.MaxValue) { rangeMin = 0; rangeMax = (int)buf.LengthInBytes; }
                    int rangeSize = rangeMax - rangeMin;

                    // Read from the lazy-materialized snapshot iff a host write fired
                    // between this dispatch's queue time and now (i.e. the buffer's
                    // HostWriteCounter has advanced past the queue-time value AND a
                    // pinned snapshot exists). Otherwise read SharedBuffer directly,
                    // which by here carries every prior dispatch's copy-OUT data
                    // (correct for in-place multi-pass kernels like RadixSort).
                    // Falls back to SharedBuffer for buffers that weren't in args at
                    // queue time (e.g. struct-with-view fields discovered during the
                    // dispatcher's own iav extraction below).
                    SharedArrayBuffer srcSab = buf.SharedBuffer;
                    if (argDispatchIntents != null
                        && argDispatchIntents.TryGetValue(buf, out var qhwc))
                    {
                        var pinned = buf.GetSnapshotForDispatch(qhwc);
                        if (pinned != null)
                        {
                            srcSab = pinned;
                            bufIndicesReadFromSnapshot.Add(i);
                        }
                    }

                    using var srcView = new Uint8Array(srcSab, rangeMin, rangeSize);
                    using var dstView = new Uint8Array(memoryBuffer, offset, rangeSize);
                    dstView.JSRef!.CallVoid("set", srcView);
                    hostWriteSnapshot[i] = buf.HostWriteCounter;
                }
                // Debug: check buf.SharedBuffer and Wasm memory after copy-in.
                // Gate the entire block behind VerboseLogging — the JS interop calls allocate
                // typed array views per dispatch even when the log message is suppressed, and
                // the hardcoded `length=4` throws RangeError for sub-word buffers (1-element
                // short = 2 bytes < 4). Surfaced 2026-05-04 by Tests23_MinimalShortIntBodyStruct
                // on Wasm.
                if (WasmBackend.VerboseLogging
                    && dispNum >= 1 && dispNum <= 8
                    && bufferInfos.Count > 0)
                {
                    int sabLen = (int)Math.Min(4, bufferInfos[0].buffer.LengthInBytes);
                    int wmLen = (int)Math.Min(4, bufferInfos[0].buffer.LengthInBytes);
                    if (sabLen >= 4 && wmLen >= 4)
                    {
                        // What's in the buffer's SharedArrayBuffer?
                        using var sabView = new Uint8Array(bufferInfos[0].buffer.SharedBuffer, 0, sabLen);
                        int sabVal = BitConverter.ToInt32(sabView.ReadBytes());
                        // What's in Wasm memory after copy-in?
                        using var wmView = new Uint8Array(memoryBuffer, bufferOffsets[0], wmLen);
                        int wmVal = BitConverter.ToInt32(wmView.ReadBytes());
                        _dispatchLog += $"|D{dispNum}pre:sab={sabVal},wm={wmVal}";
                    }
                }
                _lastImplicitIndexDebug += $" | bufInfoCnt={bufferInfos.Count} bufOffCnt={bufferOffsets.Count}";
                // CRITICAL: Set each buffer's NativePtr to its Wasm memory offset.
                // When struct parameters contain ArrayViews, the struct serialization
                // (Unsafe.Write) copies the NativePtr value into the byte array. The
                // kernel reads this as the buffer's address. With NativePtr=0, the kernel
                // would write to address 0 instead of the buffer's actual Wasm position.
                // We restore NativePtr to 0 after serialization.
                // NOTE: NativePtr points to where the buffer's START (byte 0) would be in
                // Wasm memory. Since we only copy the used range starting at rangeMin,
                // we subtract rangeMin so SubView offsets resolve correctly:
                //   wasmAddr = NativePtr + subViewByteOffset
                //            = (bufferOffset - rangeMin) + subViewByteOffset
                for (int i = 0; i < bufferInfos.Count; i++)
                {
                    var (buf, _) = bufferInfos[i];
                    var (rangeMin, _) = bufferRanges[i];
                    if (rangeMin == int.MaxValue) rangeMin = 0;
                    buf.NativePtr = (IntPtr)(bufferOffsets[i] - rangeMin);
                }

                // Build flat argument list
                // Track struct scalar args that need to be written into scratch memory
                var structScratchWrites = new List<(int scratchOffset, byte[] bytes)>();
                int scratchCursor = 0; // offset within scratch region

                // OOB diagnostic: build view layout summary for error messages
                int memorySize = wasmPages * 65536;
                {
                    var diagSb = new System.Text.StringBuilder();
                    int viewCheckIdx = 0;
                    foreach (var (isB, bufCheck, lenCheck, _, _, _) in wasmArgs)
                    {
                        if (isB)
                        {
                            int bIdx = viewBufferIdx[viewCheckIdx];
                            var (diagRMin, _) = bufferRanges[bIdx];
                            if (diagRMin == int.MaxValue) diagRMin = 0;
                            int vOff = bufferOffsets[bIdx] + viewSubOffsets[viewCheckIdx] - diagRMin;
                            int elemSize = viewElemSizes[viewCheckIdx];
                            int dataEnd = vOff + lenCheck * elemSize;
                            diagSb.Append($" V{viewCheckIdx}:[{vOff}..{dataEnd})/{memorySize}");
                            viewCheckIdx++;
                        }
                    }
                    _lastViewLayoutDiag = diagSb.ToString();
                }

                var flatArgs = new List<string>();
                int viewIndex = 0; // tracks views for SubView offset lookup
                int wasmArgIdx = 0; // tracks current wasmArgs index for IR param lookup
                int structSerializeIdx = 0;
                foreach (var (isBuffer, buffer, length, stride, stride2, value) in wasmArgs)
                {
                    if (isBuffer)
                    {
                        // Compute the kernel's byte address for this view:
                        // = buffer's Wasm memory base + (SubView byte offset - rangeMin)
                        // Since only the used range [rangeMin, rangeMax) was copied to Wasm
                        // memory, we subtract rangeMin to get the offset within the copied region.
                        int bufIdx = viewBufferIdx[viewIndex];
                        var (rMin, _) = bufferRanges[bufIdx];
                        if (rMin == int.MaxValue) rMin = 0;
                        int viewOffset = bufferOffsets[bufIdx] + viewSubOffsets[viewIndex] - rMin;
                        flatArgs.Add(viewOffset.ToString());
                        flatArgs.Add(length.ToString());
                        if (stride != -1) // -1 = skip (struct-embedded views)
                            flatArgs.Add(stride.ToString());
                        if (stride2 != -1)
                            flatArgs.Add(stride2.ToString());
                        viewIndex++;
                    }
                    else
                    {
                        if (value is float fv) flatArgs.Add(fv.ToString("G9"));
                        else if (value is double dv) flatArgs.Add(dv.ToString("G17"));
                        else if (value is long lv) flatArgs.Add($"{lv}n"); // BigInt for i64
                        else if (value is ulong ulv) flatArgs.Add($"{ulv}n"); // BigInt for i64
                        else if (value != null && !value.GetType().IsPrimitive && !value.GetType().IsEnum
                            && (value.GetType().IsValueType
                                || value.GetType().IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)))
                        {
                            // Check if the IR treats this as a scalar (e.g., SpecializedValue<int>
                            // is a struct wrapping an int, but the IR lowers it to PrimitiveType).
                            // In that case, unwrap to the inner value instead of serializing to scratch.
                            int irIdx = structSerializeIdx < wasmStructIrParamIndices.Count
                                ? wasmStructIrParamIndices[structSerializeIdx]
                                : structSerializeIdx + compiledKernel.IrUserParamIndexOffset;
                            structSerializeIdx++;
                            WasmParamInfo? irParam = null;
                            foreach (var pi in paramInfos)
                            {
                                if (pi.Index == irIdx)
                                {
                                    irParam = pi;
                                    break;
                                }
                            }
                            if (irParam != null && irParam.IsScalar && irParam.StructFields == null && irParam.StructSize == 0)
                            {
                                // IR expects a scalar, but CLR has a wrapper struct. Extract first field.
                                var innerFields = value.GetType().GetFields(
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                object innerVal = value;
                                // Unwrap nested wrappers until we get a primitive
                                while (innerFields.Length > 0 && !innerVal.GetType().IsPrimitive)
                                {
                                    innerVal = innerFields[0].GetValue(innerVal);
                                    if (innerVal == null) break;
                                    innerFields = innerVal.GetType().GetFields(
                                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                }
                                if (innerVal != null && innerVal.GetType().IsPrimitive)
                                {
                                    if (innerVal is float fvi) flatArgs.Add(fvi.ToString("G9"));
                                    else if (innerVal is double dvi) flatArgs.Add(dvi.ToString("G17"));
                                    else if (innerVal is long lvi) flatArgs.Add($"{lvi}n");
                                    else if (innerVal is ulong ulvi) flatArgs.Add($"{ulvi}n");
                                    else flatArgs.Add(innerVal.ToString() ?? "0");
                                    wasmArgIdx++;
                                    continue;
                                }
                            }

                            // Look up the IR struct layout from the compiled kernel's param info.
                            // This tells us the exact byte offset and type of each leaf field
                            // as the kernel expects them in scratch memory.
                            int irParamIdx = irIdx;
                            var irLayout = irParam?.StructFields;
                            int irStructSize = irParam?.StructSize ?? 0;
                            var piEntry = irParam;
                            _lastImplicitIndexDebug += $" | STRUCT: idx={wasmArgIdx},irIdx={irParamIdx},piCnt={paramInfos.Count},hasLayout={irLayout != null},irSize={irStructSize},piIsView={piEntry?.IsView},piIsScalar={piEntry?.IsScalar},piName={piEntry?.Name},irType={piEntry?.IRTypeName}";

                            byte[] bytes;
                            int structSize;
                            if (irLayout != null && irLayout.Count > 0 && irStructSize > 0)
                            {
                                // Manual serialization using IR layout.
                                structSize = irStructSize;
                                bytes = new byte[structSize];
                                bool isDisplayClass = !value.GetType().IsValueType
                                    && value.GetType().IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false);

                                if (isDisplayClass)
                                {
                                    // Capturing-lambda display class (reference type): cannot
                                    // Unsafe.Write or SizeOf the CLR type. Walk capture fields in
                                    // declaration order and pack into IR layout (matches WebGL path).
                                    var captFields = value.GetType().GetFields(
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    int captIdx = 0;
                                    for (int fi = 0; fi < irLayout.Count; fi++)
                                    {
                                        var field = irLayout[fi];
                                        if (field.IsViewPtr)
                                            continue;
                                        object? fieldVal = captIdx < captFields.Length
                                            ? captFields[captIdx++].GetValue(value)
                                            : null;
                                        WritePrimitiveToBytes(bytes, field.Offset, fieldVal, field.WasmType, field.Size);
                                    }
                                    _lastImplicitIndexDebug += $" | DisplayClass: fields={captFields.Length}, irLeaves={irLayout.Count}";
                                }
                                else
                                {
                                int clrStructSize = global::ILGPU.Interop.SizeOf(value.GetType());
                                _lastImplicitIndexDebug += $" | IRLayout: fields={irLayout.Count}, size={structSize}, clrSize={clrStructSize}";

                                // Cross-assembly body structs (e.g. SpawnDev.ILGPU.ML.Tensors.TensorView<T>)
                                // share the same CLR sequential layout as same-assembly mirrors, but
                                // CLR flatten order can diverge from IR leaf order. When sizes match,
                                // blit the CLR struct verbatim then patch only the view-pointer slots.
                                if (clrStructSize == irStructSize)
                                {
                                    var handle = System.Runtime.InteropServices.GCHandle.Alloc(
                                        bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
                                    try
                                    {
                                        unsafe
                                        {
                                            byte* ptr = (byte*)handle.AddrOfPinnedObject();
                                            var unsafeWriteMethod = _unsafeWriteCache.GetOrAdd(value.GetType(), t =>
                                                typeof(Unsafe)
                                                    .GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!
                                                    .MakeGenericMethod(t));
                                            unsafeWriteMethod.Invoke(null, new object[] { (IntPtr)ptr, value });
                                        }
                                    }
                                    finally { handle.Free(); }

                                    var viewsInStruct = new List<IArrayView>();
                                    CollectArrayViewsFromStruct(value, viewsInStruct);
                                    int viewPatchIdx = 0;
                                    for (int fi = 0; fi < irLayout.Count; fi++)
                                    {
                                        var field = irLayout[fi];
                                        if (!field.IsViewPtr)
                                            continue;

                                        IArrayView? iavInner = viewPatchIdx < viewsInStruct.Count
                                            ? viewsInStruct[viewPatchIdx++]
                                            : null;
                                        WriteViewPtrToStructBytes(
                                            bytes, field.Offset, field.Size, iavInner,
                                            uniqueBuffers, bufferOffsets);
                                    }
                                }
                                else
                                {
                                    var flatValues = new List<object>();
                                    FlattenCLRStruct(value, flatValues);

                                    _lastImplicitIndexDebug += $" | clrFlat={flatValues.Count}";

                                    // When CLR flatten count matches IR leaf count, write field-by-field
                                    // in lockstep. The view/non-view split path mis-orders cross-assembly
                                    // TensorView<T> (clrSize 64, irSize 48, flatCount 8) — surfaced by
                                    // ML_TensorView_Half_RoundTrip_CrossAssembly (2026-05-28).
                                    if (flatValues.Count == irLayout.Count)
                                    {
                                        var viewsInStruct = new List<IArrayView>();
                                        CollectArrayViewsFromStruct(value, viewsInStruct);
                                        int viewPatchIdx = 0;
                                        for (int fi = 0; fi < irLayout.Count; fi++)
                                        {
                                            var field = irLayout[fi];
                                            object? fieldVal = field.IsViewPtr
                                                ? (viewPatchIdx < viewsInStruct.Count ? viewsInStruct[viewPatchIdx++] : null)
                                                : flatValues[fi];
                                            _lastImplicitIndexDebug += $" | f{fi}:off={field.Offset},t=0x{field.WasmType:X2},vp={field.IsViewPtr},v={fieldVal?.GetType().Name}";

                                            if (field.IsViewPtr)
                                            {
                                                WriteViewPtrToStructBytes(
                                                    bytes, field.Offset, field.Size, fieldVal as IArrayView,
                                                    uniqueBuffers, bufferOffsets);
                                            }
                                            else
                                            {
                                                WritePrimitiveToBytes(bytes, field.Offset, fieldVal, field.WasmType, field.Size);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        var viewValues = flatValues.Where(v => v is IArrayView).ToList();
                                        var nonViewValues = flatValues.Where(v => !(v is IArrayView)).ToList();
                                        int viewIdx = 0, nonViewIdx = 0;
                                        for (int fi = 0; fi < irLayout.Count; fi++)
                                        {
                                            var field = irLayout[fi];
                                            object? fieldVal;
                                            if (field.IsViewPtr && viewIdx < viewValues.Count)
                                                fieldVal = viewValues[viewIdx++];
                                            else if (!field.IsViewPtr && nonViewIdx < nonViewValues.Count)
                                                fieldVal = nonViewValues[nonViewIdx++];
                                            else
                                                fieldVal = null;
                                            _lastImplicitIndexDebug += $" | f{fi}:off={field.Offset},t=0x{field.WasmType:X2},vp={field.IsViewPtr},v={fieldVal?.GetType().Name}";

                                            if (field.IsViewPtr)
                                            {
                                                WriteViewPtrToStructBytes(
                                                    bytes, field.Offset, field.Size, fieldVal as IArrayView,
                                                    uniqueBuffers, bufferOffsets);
                                            }
                                            else
                                            {
                                                WritePrimitiveToBytes(bytes, field.Offset, fieldVal, field.WasmType, field.Size);
                                            }
                                        }
                                    }
                                }
                                }
                                // TEMP: store view pointer debug info
                                {
                                    var vpDbg = new System.Text.StringBuilder();
                                    for (int dfi = 0; dfi < irLayout.Count; dfi++)
                                    {
                                        var df = irLayout[dfi];
                                        int val = df.Size >= 4 ? BitConverter.ToInt32(bytes, df.Offset) : bytes[df.Offset];
                                        vpDbg.Append($"[{dfi}]off={df.Offset},t={df.WasmType:X},vp={df.IsViewPtr},sz={df.Size},val={val} ");
                                    }
                                    _lastStructSerialDebug = (_lastStructSerialDebug ?? "") + $" | param{wasmArgIdx}@{structRegionBase + scratchCursor}:{vpDbg}";
                                }
                            }
                            else if (value.GetType().IsValueType)
                            {
                                // Fallback: use Unsafe.Write (works for any value-type struct
                                // including those with class-reference fields via the GC heap).
                                // The previous Marshal.StructureToPtr branch (for non-generic
                                // value types) failed with `MarshalDirectiveException: Type
                                // ILGPU.Util.DisposeBase ... must have a StructLayout attribute`
                                // when the struct contained ArrayView<T> fields — ArrayView<T>
                                // has a `Buffer` reference field of type MemoryBuffer (which
                                // derives from DisposeBase, no StructLayout). User-authored
                                // body structs (Tests23_RegisterHeavyBody with 12 ArrayView<int>
                                // fields) trip this path because their IR struct layout was
                                // either not populated or not picked up at the IR-aware path
                                // above. Unsafe.Write is layout-agnostic and works in both
                                // generic and non-generic cases — switching unconditionally
                                // closes the marshaling crash without losing correctness for
                                // the kernel-side bytes (which are then over-written with the
                                // proper IR layout below by the existing struct-to-scratch
                                // serialization path that uses NativePtr for view-pointer
                                // patching). (rc.16 RegisterHeavyBody marshaling fix, 2026-05-05.)
                                structSize = global::ILGPU.Interop.SizeOf(value.GetType());
                                bytes = new byte[structSize];
                                var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
                                try
                                {
                                    unsafe
                                    {
                                        byte* ptr = (byte*)handle.AddrOfPinnedObject();
                                        var unsafeWriteMethod = _unsafeWriteCache.GetOrAdd(value.GetType(), t =>
                                            typeof(Unsafe)
                                                .GetMethod("Write", BindingFlags.Public | BindingFlags.Static)!
                                                .MakeGenericMethod(t));
                                        unsafeWriteMethod.Invoke(null, new object[] { (IntPtr)ptr, value });
                                    }
                                }
                                finally { handle.Free(); }
                            }
                            else
                            {
                                // Reference type (display class): flatten
                                // fields via reflection and serialize each
                                // primitive to scratch.
                                var captureValues = new List<object>();
                                FlattenCLRStruct(value, captureValues);
                                structSize = 0;
                                foreach (var cv in captureValues)
                                {
                                    if (cv is long || cv is double) structSize += 8;
                                    else structSize += 4;
                                }
                                bytes = new byte[structSize];
                                int off = 0;
                                foreach (var cv in captureValues)
                                {
                                    WritePrimitiveToBytes(bytes, off, cv,
                                        0x7F /* i32 default */, 4);
                                    if (cv is long || cv is double) off += 8;
                                    else off += 4;
                                }
                            }

                            // Align to 4 bytes within the struct region (AFTER per-thread scratch).
                            // Using structRegionBase instead of scratchBase prevents overlap
                            // with thread 0's scratch where state is saved during barrier yields.
                            scratchCursor = (scratchCursor + 3) & ~3;
                            int absoluteOffset = structRegionBase + scratchCursor;
                            structScratchWrites.Add((absoluteOffset, bytes));
                            flatArgs.Add(absoluteOffset.ToString());
                            scratchCursor += structSize;
                            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] Struct scalar arg: type={value.GetType().Name}, size={structSize}, scratchOffset={absoluteOffset}, structRegionBase={structRegionBase}");
                        }
                        else flatArgs.Add(value?.ToString() ?? "0");
                    }
                    wasmArgIdx++;
                }

                // Write struct scalar args into scratch memory region
                if (structScratchWrites.Count > 0)
                {
                    foreach (var (scratchOffset, bytes) in structScratchWrites)
                    {
                        using var dstView = new Uint8Array(memoryBuffer, scratchOffset, bytes.Length);
                        dstView.JSRef!.CallVoid("set", (object)bytes);
                    }
                }

                // Restore NativePtr to 0 (cleanup from the pre-serialization patching)
                for (int i = 0; i < bufferInfos.Count; i++)
                    bufferInfos[i].buffer.NativePtr = IntPtr.Zero;

                // Log first few dispatches' flat args for debugging
                if (dispNum >= 1 && dispNum <= 6)
                {
                    string flatArgStr = "";
                    for (int fi = 0; fi < flatArgs.Count; fi++)
                        flatArgStr += (fi > 0 ? "," : "") + flatArgs[fi];
                    if (WasmBackend.VerboseLogging) _dispatchLog += $"|D{dispNum}:items={totalItems},gs={groupSize},ng={numGroups},bar={compiledKernel.HasBarriers},flat=[{flatArgStr}]";
                }

                // Dispatch to workers. `traceFoundAnyBufferWrite` + `writtenBufferIndices`
                // are produced by the codegen Store-target trace and currently used only
                // for the diagnostic string (not gating copy-OUT) — a future iteration
                // will use them once the trace covers every Store-target IR shape.
                bool traceFoundAnyBufferWrite = writtenBufferIndices.Count > 0;
                await DispatchToWorkers(
                    totalItems, gridDimX, gridDimY, scratchBase, scratchPerThread,
                    sharedMemBase, barrierBase, fenceSlot, yieldStateRegionBase, compiledKernel,
                    groupSize, numGroups, realGroupDimX, realGroupDimY,
                    flatArgs, compiledKernel.WasmBinary,
                    wasmMemory, memoryBuffer, bufferOffsets, bufferInfos, bufferRanges,
                    writtenBufferIndices, traceFoundAnyBufferWrite, hostWriteSnapshot,
                    bufIndicesReadFromSnapshot,
                    dynamicSharedElements, dispNum);

                // Clean up per-dispatch memory (only created for concurrent dispatches)
                disposeBuffer?.Dispose();
                disposeWasmMemory?.Dispose();
            }
            catch (Exception ex)
            {
                if (WasmBackend.VerboseLogging) _dispatchLog += $"|ERR_D{dispNum}:{ex.Message}";
                WasmBackend.Log($"[Wasm] Kernel execution error: {ex}");
                throw;
            }
            finally
            {
                // Always decrement active dispatch count
                _activeDispatchCount--;
                // Release the lazy-snapshot intent for every buffer this dispatch
                // registered at queue time. When the last in-flight intent on a
                // buffer completes, the buffer drops its pinned snapshot (if one
                // was materialized) so memory is released eagerly.
                if (argDispatchIntents != null)
                {
                    foreach (var kv in argDispatchIntents)
                        kv.Key.CompleteDispatchIntent(kv.Value);
                }
            }
        }

        /// <summary>
        /// Installs persistent OnMessage and OnError handlers on a worker the first time we
        /// dispatch to it. The handlers stay installed for the worker's lifetime and read the
        /// current dispatch's TCS + diagnostic context from <see cref="WorkerDispatchState"/>.
        ///
        /// This was Hypothesis #1 in the wait/notify race investigation (2026-04-26). The prior
        /// per-dispatch attach/detach pattern was suspected of triggering V8 deopt that exposed
        /// timing windows in the wait/notify barrier protocol. Empirically, even with the
        /// switch from wait/notify to spin-yield, the persistent-handler refactor remains
        /// load-bearing for `RadixSortRepeatedResortTest`: per-dispatch handler churn over
        /// hundreds of phases triggers enough V8 deopt that the test misses its 120s timeout.
        /// </summary>
        private void EnsurePersistentHandlers(Worker worker)
        {
            if (_workerHandlers.ContainsKey(worker)) return;

            var state = new WorkerDispatchState();
            _workerHandlers[worker] = state;

            state.MsgHandler = new Action<MessageEvent>((msg) =>
            {
                var tcs = state.CurrentTcs;
                if (tcs == null) return; // No in-flight dispatch; ignore late or stray message.
                state.CurrentTcs = null;
                _workerPool?.Return(worker);
                UnregisterTcs(tcs);

                var response = msg.GetData<WasmDispatchResponse>();

                // Capture diagnostic from worker 0 (preserves prior behavior)
                if (state.WorkerIdx == 0 && _dispatchCount <= 6 && response.diag != null && WasmBackend.VerboseLogging)
                {
                    var d0 = response.diag.Length > 0 ? (int?)response.diag[0] : null;
                    var d1 = response.diag.Length > 1 ? (int?)response.diag[1] : null;
                    _dispatchLog += $"|W0mem=[{d0},{d1}]";
                }

                if (!response.done)
                {
                    var errorMsg = response.error ?? "Unknown worker error";
                    tcs.TrySetException(new Exception(
                        $"[Wasm] Worker {state.WorkerIdx} error: {errorMsg} | kernel={state.KernelName} disp={state.DispNum} pages={_cachedWasmPages} mem={_cachedWasmPages * 65536} barriers={state.HasBarriers} scratch={state.ScratchBase} shared={state.SharedMemBase} fence={state.FenceSlot} gs={state.GroupSize} spt={state.ScratchPerThread} items={state.TotalItems} wc={state.WorkerCount} views={state.ViewLayoutDiag}"));
                    return;
                }
                tcs.TrySetResult();
            });

            state.ErrHandler = new Action<Event>((err) =>
            {
                var tcs = state.CurrentTcs;
                if (tcs == null) return;
                state.CurrentTcs = null;
                _workerPool?.Return(worker);
                UnregisterTcs(tcs);
                tcs.TrySetException(new Exception($"[Wasm] Worker {state.WorkerIdx} error during kernel execution | kernel={state.KernelName} disp={state.DispNum}"));
            });

            worker.OnMessage += state.MsgHandler;
            worker.OnError += state.ErrHandler;
        }

        /// <summary>
        /// Dispatches the Wasm kernel across multiple Web Workers for true Wasm multithreading.
        /// For barrier kernels: distributes groups across workers. Each worker runs all threads within its groups.
        /// For non-barrier kernels: distributes items across workers with flat range dispatch.
        /// </summary>
        private async Task DispatchToWorkers(
            int totalItems,
            int gridDimX,
            int gridDimY,
            int scratchBase,
            int scratchPerThread,
            int sharedMemBase,
            int barrierBase,
            int fenceSlot,
            int yieldStateRegionBase,
            WasmCompiledKernel compiledKernel,
            int groupSize,
            int numGroups,
            int realGroupDimX,
            int realGroupDimY,
            List<string> flatArgs,
            byte[] wasmBytes,
            JSObject wasmMemory,
            SharedArrayBuffer memoryBuffer,
            List<int> bufferOffsets,
            List<(WasmMemoryBuffer buffer, int byteOffset)> bufferInfos,
            List<(int minByte, int maxByte)> bufferRanges,
            HashSet<int> writtenBufferIndices,
            bool traceFoundAnyBufferWrite,
            int[] hostWriteSnapshot,
            HashSet<int> bufIndicesReadFromSnapshot,
            int dynamicSharedElements = 0,
            int dispNum = 0)
        {
            bool hasBarriers = compiledKernel.HasBarriers;

            int workerCount;
            int phaseCount = compiledKernel.PhaseCount;
            if (hasBarriers)
            {
                // Full hardwareConcurrency for barrier kernels.
                // Pure spin barrier provides correct seq_cst ordering.
                workerCount = Math.Min(_workerCount, groupSize);
                int fibersPerWorker = (groupSize + workerCount - 1) / workerCount;
                workerCount = (groupSize + fibersPerWorker - 1) / fibersPerWorker;
                if (workerCount < 1) workerCount = 1;
            }
            else
            {
                // Non-barrier: full worker count for maximum parallelism
                workerCount = _workerCount;
                if (workerCount > totalItems) workerCount = Math.Max(1, totalItems);
            }

            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] Dispatch: workers={workerCount}, items={totalItems}, barriers={hasBarriers}, gs={groupSize}, ng={numGroups}, phases={phaseCount}");

            // Dump barrier kernel binary for debugging (only when verbose)
            if (hasBarriers && WasmBackend.VerboseLogging)
            {
                WasmBackend.Log($"[Wasm_DUMP_START] dispatch={_dispatchCount} size={wasmBytes.Length} phases={phaseCount} spt={scratchPerThread} shm={sharedMemBase} bar={barrierBase}");
                var b64 = Convert.ToBase64String(wasmBytes);
                for (int ci = 0; ci < b64.Length; ci += 1000)
                    WasmBackend.Log($"[Wasm_DUMP] {b64.Substring(ci, Math.Min(1000, b64.Length - ci))}");
                WasmBackend.Log("[Wasm_DUMP_END]");
            }

            // Build the worker script
            string argStr = string.Join(", ", flatArgs);
            int maxYieldIters = Math.Max(10000, compiledKernel.BarrierCount * Math.Max(phaseCount, 1) * 250);
            var workerScript = BuildWasmWorkerScript(
                gridDimX, gridDimY, scratchBase, scratchPerThread,
                sharedMemBase, barrierBase, fenceSlot,
                groupSize, numGroups, realGroupDimX, realGroupDimY, hasBarriers, argStr,
                dynamicSharedElements, workerCount, phaseCount,
                maxYieldIters,
                // Zero region covers shared memory + barrier counters only.
                // MUST NOT include fence slots — the group barrier uses fenceSlot+16/+20
                // immediately after the zero loop. If a slow worker zeroes the arrival
                // counter after a fast worker already incremented it, both deadlock.
                // Fence slots self-manage via the barrier protocol (last worker resets).
                fenceSlot - sharedMemBase);

            // Lazily initialize the worker pool, or grow it if needed
            if (_workerPool == null)
                _workerPool = new WorkerPool(workerCount, useAsync: true);
            else
                _workerPool.EnsureSize(workerCount, useAsync: true);

            var workers = _workerPool.Acquire(workerCount);
            // If pool didn't have enough idle workers, grow and re-acquire
            if (workers.Count < workerCount)
            {
                var shortfall = workerCount - workers.Count;
                _workerPool.EnsureSize(_workerPool.Size + shortfall, useAsync: true);
                var extra = _workerPool.Acquire(shortfall);
                workers.AddRange(extra);
            }

            // Per-kernel worker initialization tracking. Look up (or create) the
            // KernelCacheEntry for this kernel's wasmBytes — it carries a stable unique
            // KernelId (the worker-side module-cache key) plus the set of workers that
            // already received and compiled this specific kernel and don't need it
            // re-sent. Different kernels get distinct entries (and distinct ids) so
            // multi-kernel pipelines (ML inference) don't thrash the worker module cache.
            //
            // Surfaced 2026-05-04 by Data's StyleMosaic Wasm 10+ minute hang at rc.16:
            // ~100 dispatches × ~6 alternating kernel types × 8 workers = ~4800 worker-
            // side Wasm module re-compiles (50-100ms each) when the prior single-set
            // tracker was being cleared on every kernel switch. Now each (worker, kernel)
            // pair compiles once and reuses for the lifetime of the worker.
            if (!_initializedWorkersByKernel.TryGetValue(wasmBytes, out var kernelCacheEntry))
            {
                kernelCacheEntry = new KernelCacheEntry
                {
                    // Unique + stable per distinct kernel (keyed by wasmBytes ref). Replaces the old
                    // RuntimeHelpers.GetHashCode(wasmBytes) id: that identity hash RECYCLES under GC and
                    // let two live kernels collide on a single worker-side module slot, so a worker could
                    // run a stomped/stale module -> silent large-sort corruption under full-sweep churn.
                    // A monotonic id cannot collide; see _nextKernelId.
                    KernelId = Interlocked.Increment(ref _nextKernelId)
                };
                _initializedWorkersByKernel[wasmBytes] = kernelCacheEntry;
            }
            int kernelId = kernelCacheEntry.KernelId;

            var tasks = new List<Task>();

            if (hasBarriers)
            {
                // Barrier dispatch: each worker gets its threadId (0..groupSize-1)
                // All workers process the same groups sequentially.
                for (int w = 0; w < workerCount; w++)
                {
                    var worker = workers[w];
                    var tcs = new TaskCompletionSource();
                    RegisterTcs(tcs);

                    // Persistent handlers (Hypothesis #1, 2026-04-26): install OnMessage +
                    // OnError ONCE per worker, then update the per-worker state's CurrentTcs
                    // each dispatch. Eliminates the per-dispatch attach/detach churn that
                    // accumulated V8 deopt over hundreds of phases of RadixSortRepeatedResortTest.
                    EnsurePersistentHandlers(worker);
                    var handlerState = _workerHandlers[worker];
                    handlerState.CurrentTcs = tcs;
                    handlerState.WorkerIdx = w;
                    handlerState.DispNum = dispNum;
                    handlerState.HasBarriers = hasBarriers;
                    handlerState.ScratchBase = scratchBase;
                    handlerState.SharedMemBase = sharedMemBase;
                    handlerState.FenceSlot = fenceSlot;
                    handlerState.GroupSize = groupSize;
                    handlerState.ScratchPerThread = scratchPerThread;
                    handlerState.TotalItems = totalItems;
                    handlerState.WorkerCount = workerCount;
                    handlerState.ViewLayoutDiag = _lastViewLayoutDiag;
                    handlerState.KernelName = compiledKernel.EntryPoint?.Name ?? "<unknown>";

                    // Send fiber range to worker.
                    // Fiber dispatch: each worker handles a contiguous range of threads.
                    int fibersPerWorker = (groupSize + workerCount - 1) / workerCount;
                    int threadStart = w * fibersPerWorker;
                    int threadEnd = Math.Min(threadStart + fibersPerWorker, groupSize);
                    // Per-worker spin-yield save/restore buffer (16 bytes).
                    // Layout: [yieldFlag, savedG, savedPhase, savedGen]. The dispatcher
                    // uses this to persist its position when it yields back to JS so the
                    // re-dispatch can resume the spin loop where it left off.
                    int yieldStateAddr = yieldStateRegionBase + w * 16;

                    bool firstTimeOnWorker = kernelCacheEntry.Workers.Add(worker);
                    worker.PostMessage(new WasmBarrierDispatchMessage
                    {
                        script = workerScript,
                        wasmBytes = firstTimeOnWorker ? wasmBytes : null,
                        kernelId = kernelId,
                        memory = wasmMemory,
                        threadStart = threadStart,
                        threadEnd = threadEnd,
                        yieldStateAddr = yieldStateAddr,
                    });

                    tasks.Add(tcs.Task);
                }
            }
            else
            {
                // Non-barrier: flat item distribution
                int itemsPerWorker = totalItems / workerCount;
                int remainder = totalItems % workerCount;

                for (int w = 0; w < workerCount; w++)
                {
                    var worker = workers[w];
                    int startIdx = w * itemsPerWorker + Math.Min(w, remainder);
                    int endIdx = startIdx + itemsPerWorker + (w < remainder ? 1 : 0);
                    int myScratch = scratchBase + w * scratchPerThread; // per-worker scratch

                    var tcs = new TaskCompletionSource();
                    RegisterTcs(tcs);

                    EnsurePersistentHandlers(worker);
                    var handlerState = _workerHandlers[worker];
                    handlerState.CurrentTcs = tcs;
                    handlerState.WorkerIdx = w;
                    handlerState.DispNum = dispNum;
                    handlerState.HasBarriers = hasBarriers;
                    handlerState.ScratchBase = scratchBase;
                    handlerState.SharedMemBase = sharedMemBase;
                    handlerState.FenceSlot = fenceSlot;
                    handlerState.GroupSize = groupSize;
                    handlerState.ScratchPerThread = scratchPerThread;
                    handlerState.TotalItems = totalItems;
                    handlerState.WorkerCount = workerCount;
                    handlerState.ViewLayoutDiag = _lastViewLayoutDiag;
                    handlerState.KernelName = compiledKernel.EntryPoint?.Name ?? "<unknown>";

                    bool firstTimeOnWorker = kernelCacheEntry.Workers.Add(worker);
                    worker.PostMessage(new WasmFlatDispatchMessage
                    {
                        script = workerScript,
                        wasmBytes = firstTimeOnWorker ? wasmBytes : null,
                        kernelId = kernelId,
                        memory = wasmMemory,
                        startIdx = startIdx,
                        endIdx = endIdx,
                        myScratch = myScratch,
                    });

                    tasks.Add(tcs.Task);
                }
            }

            // Wait for all workers to complete — but fast-fault on the FIRST worker
            // exception, AND watchdog timeout to catch infinite-loop kernels.
            //
            // (1) Fast-fault: `Task.WhenAll(tasks)` waits for ALL tasks before throwing
            // the first exception, so a single worker trap (kernel OOB, divide-by-zero)
            // hangs the dispatch waiting for OTHER workers that may also be blocked or
            // never post back. Drain via WhenAny and surface the first fault immediately.
            // Surfaced 2026-05-04 by Data's StyleMosaic Wasm 10+ minute hang at rc.16:
            // worker 2 hit a kernel OOB at dispatch 176; dispatcher waited 10 min
            // (PMT timeout) instead of 20s actionable.
            //
            // (2) Watchdog: if a kernel infinite-loops (no error, no post-back), the
            // worker TCS never resolves and Task.WhenAny blocks forever. Add a per-
            // dispatch watchdog timeout (default 120s, configurable via
            // `WasmDispatchWatchdogSeconds`) so the test fails actionably instead of
            // hitting the outer harness timeout. Default chosen large enough that
            // legitimately-slow kernels (large Conv2D on Wasm) complete in time, but
            // small enough that a hang surfaces in 2 min vs 10+ min.
            {
                var remaining = new List<Task>(tasks);
                int watchdogMs = WasmDispatchWatchdogSeconds * 1000;
                while (remaining.Count > 0)
                {
                    Task done;
                    if (watchdogMs > 0)
                    {
                        var watchdog = Task.Delay(watchdogMs);
                        var first = await Task.WhenAny(Task.WhenAny(remaining), watchdog);
                        if (first == watchdog)
                        {
                            // Hit the watchdog. Surface a diagnostic hang error.
                            string hangDiag = $"[Wasm] Dispatcher hang watchdog fired after {WasmDispatchWatchdogSeconds}s. " +
                                $"kernel={(compiledKernel.EntryPoint?.Name ?? "<unknown>")} disp={dispNum} " +
                                $"workersCompleted={tasks.Count - remaining.Count}/{tasks.Count} " +
                                $"items={totalItems} workerCount={workerCount} hasBarriers={hasBarriers}. " +
                                $"Likely infinite-loop kernel or worker that never posted back. Set " +
                                $"WasmAccelerator.WasmDispatchWatchdogSeconds higher if this is a legitimately-slow kernel.";
                            throw new TimeoutException(hangDiag);
                        }
                        done = await ((Task<Task>)first); // unwrap WhenAny's inner task
                    }
                    else
                    {
                        done = await Task.WhenAny(remaining);
                    }
                    remaining.Remove(done);
                    if (done.IsFaulted)
                    {
                        var ex = done.Exception?.InnerException ?? done.Exception;
                        if (ex != null) throw ex;
                    }
                }
            }

            // Debug: dump first 4 bytes of each buffer in Wasm memory after kernel.
            // Gate the JS interop loop behind VerboseLogging too — the per-buffer typed
            // array allocations cost real time even when the log message is suppressed.
            if (WasmBackend.VerboseLogging
                && dispNum >= 1 && dispNum <= 6)
            {
                for (int bi = 0; bi < bufferInfos.Count; bi++)
                {
                    int off = bufferOffsets[bi];
                    int bufLen = (int)bufferInfos[bi].buffer.LengthInBytes;
                    int dumpLen = Math.Min(bufLen, 16); // first 4 ints
                    if (dumpLen <= 0) continue;
                    using var rawView = new Uint8Array(memoryBuffer, off, dumpLen);
                    var rawBytes = rawView.ReadBytes();
                    string vals = "";
                    for (int j = 0; j + 3 < rawBytes.Length; j += 4)
                        vals += (j > 0 ? "," : "") + BitConverter.ToInt32(rawBytes, j);
                    _dispatchLog += $"|D{dispNum}B{bi}=[{vals}]";
                }
            }

            // Copy results back from Wasm linear memory to individual buffers.
            // Only the used SubView range is copied back (matching copy-in).
            //
            // FIX for the 2026-05-03 Wasm copy-OUT race
            // (per _DevComms/SpawnDev.ILGPU/geordi-to-team-wasm-copy-out-race-2026-05-03.md):
            // skip copy-OUT for buffers whose `HostWriteCounter` advanced during the
            // in-flight dispatch. A counter advance means the host called
            // CopyFromCPU / CopyFromJS / CopyFromHost on that buffer between copy-IN
            // and copy-OUT — its SharedBuffer now holds the host's NEW data, and our
            // copy-OUT (which would write back the kernel's wasmMemory contents from
            // BEFORE the host write) would clobber it.
            //
            // Trace-coverage-independent: works for every kernel shape including
            // RadixSort multi-pass + barrier kernels. The host-write counter is
            // bumped on every host-side write path on `WasmMemoryBuffer`, so any
            // intervening write is detected without per-kernel write tracking.
            int copyOutCount = 0;
            int copyOutSkipped = 0;
            for (int i = 0; i < bufferInfos.Count; i++)
            {
                var (buf, _) = bufferInfos[i];
                if (i < hostWriteSnapshot.Length && buf.HostWriteCounter != hostWriteSnapshot[i])
                {
                    copyOutSkipped++;
                    continue;
                }
                // Skip copy-OUT iff this buffer's copy-IN read from a queue-time
                // snapshot (host wrote between this dispatch's queue and run, so
                // SharedBuffer has data that's NEWER than what this dispatch
                // wanted — copy-OUT would write our snapshot data back, clobbering
                // the newer host write that subsequent dispatches need).
                // Trace-independent: doesn't rely on the codegen Store-target trace
                // to identify input-only buffers. Multi-pass kernels with no host
                // writes (RadixSort) never hit this skip — no snapshots are
                // materialized — so every dispatch's copy-OUT correctly persists
                // its kernel writes for the next pass.
                // (rc.16 RadixSort + host-write-race + ML weight reuse, 2026-05-05.)
                if (bufIndicesReadFromSnapshot.Contains(i))
                {
                    copyOutSkipped++;
                    continue;
                }
                int offset = bufferOffsets[i];
                var (rangeMin, rangeMax) = bufferRanges[i];
                if (rangeMin == int.MaxValue) { rangeMin = 0; rangeMax = (int)buf.LengthInBytes; }
                int rangeSize = rangeMax - rangeMin;

                using var srcView = new Uint8Array(memoryBuffer, offset, rangeSize);
                using var dstView = new Uint8Array(buf.SharedBuffer, rangeMin, rangeSize);
                dstView.JSRef!.CallVoid("set", srcView);
                copyOutCount++;
                // (Obsolete after the lazy-snapshot refactor: the prior eager
                // `_gpuWriteSeq` bump existed to invalidate cached pre-pass-1
                // snapshots when the kernel had GPU-written. The lazy snapshot
                // never caches across the pass-1 boundary in the first place —
                // multi-pass dispatches just read SharedBuffer post-copy-OUT —
                // so nothing here needs to fire. The trace-based gate is also
                // unnecessary; copy-OUT writes back identical bytes for
                // unwritten buffers and a real write for written ones.
                // (rc.16 RadixSort multi-pass + StyleMosaic perf, 2026-05-05.)
                // Read first 4 bytes from Wasm memory at this offset for debugging
                if (rangeSize >= 4)
                {
                    var debugSrc = new Uint8Array(memoryBuffer, offset, 4);
                    var debugBytes = debugSrc.ReadBytes();
                    debugSrc.Dispose();
                    int debugVal = BitConverter.ToInt32(debugBytes);
                    _lastImplicitIndexDebug += $" | wasmMem[{offset}]={debugVal}";
                }
            }
            _lastImplicitIndexDebug += $" | copyOut={copyOutCount}/{bufferInfos.Count} skip={copyOutSkipped} traceBufWrites={traceFoundAnyBufferWrite}";
        }

        /// <summary>
        /// Builds the JS script that runs inside each Wasm worker.
        /// For barrier kernels: worker receives { wasmBytes, memory, threadId }
        ///   and iterates over groups, calling kernel(globalIdx, dimX, dimY, scratchBase, groupDimX, threadIdX, sharedMemBase, barrierBase, ...args)
        /// For non-barrier kernels: worker receives { wasmBytes, memory, startIdx, endIdx }
        ///   and iterates over its assigned items with the same kernel signature (groupDimX=dimX, threadIdX=globalIdx).
        /// </summary>
        private static string BuildWasmWorkerScript(
            int gridDimX, int gridDimY, int scratchBase, int scratchPerThread,
            int sharedMemBase, int barrierBase, int fenceSlot,
            int groupSize, int numGroups, int realGroupDimX, int realGroupDimY, bool hasBarriers,
            string argStr,
            int dynamicSharedLength = 0,
            int workerCount = 1,
            int phaseCount = 1,
            int maxYieldIters = 10000,
            int zeroRegionSize = 0)
        {
            // Produces an async function body string that is sent as the 'script' field
            // in the message to the pool worker's async bootstrap.
            // The bootstrap caches WebAssembly.compile/instantiate — the script body
            // just uses the pre-cached instance from d._instance.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("    const kernel = d._instance.exports.kernel;");
            sb.AppendLine();

            if (hasBarriers)
            {
                // Phase dispatcher: runs the thread/phase/group loop entirely in Wasm.
                // Eliminates ~1M JS-Wasm boundary crossings for large sorts.
                // The "dispatcher" function is compiled into the Wasm module.
                //
                // SPIN-YIELD LOOP (Variant C, Trip 2026-05-27): the dispatcher may return
                // mid-spin when a phase OR group barrier fails to advance within
                // YIELD_SPIN_THRESHOLD iterations (~5ms at ~5ns/iter on modern Wasm). When
                // it does, yieldStateAddr[0] is left as 1 (phase) or 2 (group) and the
                // dispatcher's spin-loop position is saved to the per-worker yield buffer.
                // This wrapper re-invokes the dispatcher with resumeMode=1 so it picks up
                // exactly where it left off.
                //
                // JS-side `Atomics.wait(..., Infinity)` parks the worker until the WASM
                // producer (last-arriving worker) calls `env.notify` after its seqcst
                // gen-bump store - see WasmBackend.GeneratePhaseDispatcher Step 2 notify
                // emits. The notify shim is spec-correct per tc39/ecma262 #3800 (syg):
                // `Atomics.wait` returning "not-equal" does NOT imply visibility of
                // surrounding stores; the standard pattern is `while (load != target)
                // wait(...)`, which our dispatcher's spin-on-re-entry provides.
                //
                // Why Infinity (no timeout): a timed wait re-polls every Tms even when the
                // gen never advances mid-window. Under Fallout76-class CPU contention,
                // hundreds of barrier yields * Tms re-polls = framework-cap timeout.
                // Infinity wait + producer notify = exactly one park per genuine stall,
                // exactly one wake per gen-bump. No polling overhead.
                sb.AppendLine("    const dispatcher = d._instance.exports.dispatcher;");
                sb.AppendLine("    const threadStart = d.threadStart;");
                sb.AppendLine("    const threadEnd = d.threadEnd;");
                sb.AppendLine("    const yieldStateAddr = d.yieldStateAddr;");
                // Verify memory is big enough before dispatching. Account for per-worker
                // yield region: yieldStateAddr + 16 bytes per worker is the upper bound.
                sb.AppendLine($"    const memBytes = d.memory.buffer.byteLength;");
                sb.AppendLine($"    const needed = yieldStateAddr + 16;");
                sb.AppendLine($"    if (memBytes < needed) {{ self.postMessage({{ done: false, error: 'MEM TOO SMALL: buffer=' + memBytes + ' needed=' + needed + ' fence={fenceSlot} scratch={scratchBase}+{scratchPerThread}*{groupSize} shared={sharedMemBase}' }}); return; }}");
                // i32 view over the SAB, used for yieldFlag + Atomics.wait on the phase gen.
                // (The same buffer underlies d.memory; aliasing is intentional.)
                sb.AppendLine("    const yMem32 = new Int32Array(d.memory.buffer);");
                sb.AppendLine($"    const yieldFlagIdx = yieldStateAddr >>> 2;");
                // gen index in i32 view: fenceSlot+4 is the phase generation slot, fenceSlot+20
                // the group generation slot. A spin-yield can happen at EITHER barrier — the park
                // below must wait on the gen slot matching the barrier we yielded at (yieldFlag).
                sb.AppendLine($"    const genIdx = {(fenceSlot + 4) >>> 2};");
                sb.AppendLine($"    const groupGenIdx = {(fenceSlot + 20) >>> 2};");
                sb.AppendLine("    let resumeMode = 0;");
                sb.AppendLine("    let yieldIters = 0;");
                // Scale with kernel barrier load: under Fallout76-class CPU contention a single
                // barrier crossing may need many park/wake cycles before all workers advance.
                // Floor 10K catches missed-notify / livelock quickly on healthy machines.
                sb.AppendLine($"    const MAX_YIELD_ITERS = {maxYieldIters};");
                sb.AppendLine("    while (true) {");
                sb.AppendLine("      try {");
                sb.Append($"        dispatcher(threadStart, threadEnd, {numGroups}, {groupSize}, {gridDimX}, {gridDimY}, {scratchBase}, {scratchPerThread}, {sharedMemBase}, {barrierBase}, {dynamicSharedLength}, {zeroRegionSize}, {workerCount}, {fenceSlot}, yieldStateAddr, resumeMode, {realGroupDimX}, {realGroupDimY}");
                if (argStr.Length > 0)
                {
                    sb.Append(", ");
                    sb.Append(argStr);
                }
                sb.AppendLine(");");
                sb.AppendLine("      } catch(e) { self.postMessage({ done: false, error: 'Dispatcher trap: ' + e.message + ' memSize=' + d.memory.buffer.byteLength + ' yieldIters=' + yieldIters }); return; }");
                sb.AppendLine("      const yieldFlag = Atomics.load(yMem32, yieldFlagIdx);");
                sb.AppendLine("      if (yieldFlag === 0) break;");
                sb.AppendLine("      yieldIters++;");
                sb.AppendLine("      if (yieldIters >= MAX_YIELD_ITERS) { self.postMessage({ done: false, error: 'Dispatcher exceeded MAX_YIELD_ITERS=' + MAX_YIELD_ITERS }); return; }");
                // Variant C Step 3 (Trip 2026-05-27): Atomics.wait WITHOUT timeout (Infinity).
                // OS-parks the worker thread until the wasm producer's `env.notify` shim is
                // called by the last-arriving worker (after its seqcst gen-bump). If gen has
                // already advanced past savedGen by the time we get here, the wait returns
                // "not-equal" immediately - zero overhead in that case. Otherwise we park
                // until the notify, no polling. Atomics.wait return value (`ok` / `not-equal`
                // / `timed-out`) is intentionally discarded: regardless of which fired, we
                // resume the dispatcher and let its spin-on-re-entry re-check the gen. This
                // matches the standard condition-variable `while (load != target) wait()`
                // pattern from tc39/ecma262 #3800 (syg). See NOTES_ecma262_3800_syg.md.
                sb.AppendLine("      const savedGen = yMem32[yieldFlagIdx + 3];");
                // yieldFlag 1 = phase-barrier yield (park on phase gen), 2 = group-barrier
                // yield (park on group gen). Parking on the wrong slot mismatches
                // immediately and busy-loops instead of parking.
                sb.AppendLine("      const waitGenIdx = (yieldFlag === 2) ? groupGenIdx : genIdx;");
                sb.AppendLine("      Atomics.wait(yMem32, waitGenIdx, savedGen);");
                sb.AppendLine("      resumeMode = 1;");
                sb.AppendLine("    }");
            }
            else
            {
                // Non-barrier kernel: flat item dispatch with per-worker scratch
                sb.AppendLine("    const startIdx = d.startIdx;");
                sb.AppendLine("    const endIdx = d.endIdx;");
                sb.AppendLine("    const myScratch = d.myScratch;");
                sb.AppendLine();
                sb.AppendLine("    for (let i = startIdx; i < endIdx; i++) {");
                // For non-barrier kernels: pass groupSize (groupDimX) + realGroupDimX/Y so
                // Grid.IdxX/Y and Group.IdxX/Y decompose correctly. Auto-grouped launches are 1D
                // (realGroupDimX == groupSize, realGroupDimY == 1).
                // Trailing zeros = sharedMemBase, barrierBase, dynamicSharedLen, phase.
                sb.Append($"      kernel(i, {gridDimX}, {gridDimY}, myScratch, {groupSize}, i % {groupSize}, 0, 0, 0, 0, {realGroupDimX}, {realGroupDimY}");
                if (argStr.Length > 0)
                {
                    sb.Append(", ");
                    sb.Append(argStr);
                }
                sb.AppendLine(");");
                sb.AppendLine("    }");
            }

            sb.AppendLine();
            sb.AppendLine("    self.postMessage({ done: true });");
            return sb.ToString();
        }

        /// <summary>
        /// TensorView-style body structs: emit flat view kernel args (offset, length, strides)
        /// before the struct-scratch arg so Wasm codegen can route GetField/LEA through view locals.
        /// </summary>
        private void AddBodyStructExpandedViewWasmArgs(
            object structArg,
            int argsIndex,
            int paramIdx,
            List<WasmParamInfo> paramInfos,
            WasmCompiledKernel compiledKernel,
            List<(bool isBuffer, WasmMemoryBuffer? buffer, int length, int stride, int stride2, object? value)> wasmArgs,
            Dictionary<WasmMemoryBuffer, int> uniqueBuffers,
            List<(WasmMemoryBuffer buffer, int byteOffset)> bufferInfos,
            List<(int minByte, int maxByte)> bufferRanges,
            List<int> viewBufferIdx,
            List<int> viewSubOffsets,
            List<int> viewElemSizes,
            HashSet<int> writtenBufferIndices,
            int dispNum)
        {
            var views = new List<IArrayView>();
            CollectArrayViewsFromStruct(structArg, views);
            foreach (var iav in views)
            {
                var wasmBuf = iav.Buffer as WasmMemoryBuffer;
                if (wasmBuf == null)
                {
                    wasmArgs.Add((false, null, 0, 0, 0, 0));
                    continue;
                }

                if (!uniqueBuffers.TryGetValue(wasmBuf, out int bufIdx))
                {
                    bufIdx = bufferInfos.Count;
                    uniqueBuffers[wasmBuf] = bufIdx;
                    bufferInfos.Add((wasmBuf, 0));
                    bufferRanges.Add((0, (int)wasmBuf.LengthInBytes));
                }

                viewBufferIdx.Add(bufIdx);

                int irParamIdx = paramIdx + compiledKernel.IrUserParamIndexOffset;
                if (compiledKernel.WrittenParamIndices.Contains(irParamIdx))
                    writtenBufferIndices.Add(bufIdx);

                int viewElemSizeForLength = wasmBuf.ElementSize;
                int subViewByteOffset = 0;
                try
                {
                    var viewType = iav.GetType();
                    var baseProp = viewType.GetProperty("BaseView");
                    object viewObj = baseProp != null ? baseProp.GetValue(iav)! : iav;
                    var viewGenericType = viewObj.GetType();
                    if (viewGenericType.IsGenericType)
                    {
                        var elemType = viewGenericType.GetGenericArguments()[0];
                        int actualSize = global::ILGPU.Interop.SizeOf(elemType);
                        if (actualSize > 0)
                            viewElemSizeForLength = actualSize;
                    }

                    var indexProp = viewObj.GetType().GetProperty("Index",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (indexProp?.GetValue(viewObj) is long longIdx)
                        subViewByteOffset = (int)(longIdx * viewElemSizeForLength);
                }
                catch { }

                viewSubOffsets.Add(subViewByteOffset);
                viewElemSizes.Add(viewElemSizeForLength);

                int stride = 1;
                int stride2 = 0;
                var argType = iav.GetType();
                var argCache = _strideCache.GetOrAdd(argType, t => new StrideReflectionCache
                {
                    StrideProp = t.GetProperty("Stride"),
                });
                if (argCache.StrideProp != null)
                {
                    var strideObj = argCache.StrideProp.GetValue(iav);
                    if (strideObj != null)
                    {
                        var strideType = strideObj.GetType();
                        if (argCache.YStrideProp == null && argCache.XStrideProp == null)
                        {
                            argCache.YStrideProp = strideType.GetProperty("YStride");
                            argCache.XStrideProp = strideType.GetProperty("XStride");
                            argCache.ZStrideProp = strideType.GetProperty("ZStride");
                        }

                        if (argCache.YStrideProp != null)
                            stride = (int)argCache.YStrideProp.GetValue(strideObj)!;
                        else if (argCache.XStrideProp != null)
                            stride = (int)argCache.XStrideProp.GetValue(strideObj)!;

                        if (argCache.ZStrideProp != null)
                            stride2 = (int)argCache.ZStrideProp.GetValue(strideObj)!;
                    }
                }

                if (WasmBackend.VerboseLogging)
                    WasmBackend.Log($"[Wasm] BodyStruct expanded view arg[{argsIndex}]: length={iav.Length}, stride={stride}, stride2={stride2}");
                wasmArgs.Add((true, wasmBuf, (int)iav.Length, stride, stride2, null));
            }
        }

        /// <summary>
        /// Recursively scans a struct for IArrayView fields and adds their
        /// buffers to the buffer collection (for copy-in/copy-out and NativePtr patching).
        /// </summary>
        private static void ExtractBuffersFromStruct(
            object structValue,
            Dictionary<WasmMemoryBuffer, int> uniqueBuffers,
            List<(WasmMemoryBuffer buffer, int byteOffset)> bufferInfos,
            List<(int minByte, int maxByte)>? bufferRanges = null)
        {
            try
            {
                var type = structValue.GetType();
                var fields = type.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _lastImplicitIndexDebug += $" | ExtractBuf: type={type.Name}, fields={fields.Length}";
                foreach (var field in fields)
                {
                    object? val;
                    try { val = field.GetValue(structValue); }
                    catch (Exception ex) { WasmBackend.Log($"[Wasm] ExtractBuffers: field '{field.Name}' get failed: {ex.Message}"); continue; }
                    if (val == null) continue;
                    _lastImplicitIndexDebug += $" | field='{field.Name}' type={val.GetType().Name} isView={val is IArrayView}";

                    if (val is IArrayView iav)
                    {
                        var wasmBuf = iav.Buffer as WasmMemoryBuffer;
                        _lastImplicitIndexDebug += $" | buf={wasmBuf?.GetType().Name ?? "null"} len={iav.Length} inDict={wasmBuf != null && uniqueBuffers.ContainsKey(wasmBuf)}";
                        if (wasmBuf != null)
                        {
                            // Struct-embedded views use FULL buffer range for safety.
                            // Extracting SubView ranges from structs breaks RadixSort's
                            // ViewSourceSequencer (struct with ArrayView1D) — the range
                            // extraction doesn't account for how the struct's view may be
                            // reinterpreted across multi-pass dispatches.
                            if (!uniqueBuffers.ContainsKey(wasmBuf))
                            {
                                int bufIdx = bufferInfos.Count;
                                uniqueBuffers[wasmBuf] = bufIdx;
                                bufferInfos.Add((wasmBuf, 0));
                                bufferRanges?.Add((0, (int)wasmBuf.LengthInBytes));
                                _lastImplicitIndexDebug += $" | ADDED buf#{bufIdx} range=[0,{wasmBuf.LengthInBytes}) (full/struct)";
                            }
                            else if (bufferRanges != null)
                            {
                                // Buffer already known — expand to full range
                                int bufIdx = uniqueBuffers[wasmBuf];
                                bufferRanges[bufIdx] = (0, (int)wasmBuf.LengthInBytes);
                            }
                        }
                    }
                    else if (val.GetType().IsValueType && !val.GetType().IsPrimitive && !val.GetType().IsEnum)
                    {
                        ExtractBuffersFromStruct(val, uniqueBuffers, bufferInfos, bufferRanges);
                    }
                }
            }
            catch (Exception ex)
            {
                WasmBackend.Log($"[Wasm-CRITICAL] ExtractBuffersFromStruct FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches ArrayView pointer fields inside a serialized struct.
        /// Scans the struct's fields for IArrayView instances and writes the correct
        /// Wasm buffer offset at the view's NativePtr position in the serialized bytes.
        /// </summary>
        private static void PatchViewPointersInStruct(
            object structValue,
            byte[] bytes,
            List<(WasmMemoryBuffer buffer, int byteOffset)> bufferInfos,
            List<int> bufferOffsets,
            List<int> viewSubOffsets,
            List<int> viewBufferIdx)
        {
            try
            {
                // Find ALL IArrayView fields in the struct (including nested)
                var viewFields = new List<(IArrayView view, int bytePos)>();
                FindViewFieldsInStruct(structValue, structValue.GetType(), 0, viewFields);

                foreach (var (view, bytePos) in viewFields)
                {
                    var wasmBuf = view.Buffer as WasmMemoryBuffer;
                    if (wasmBuf == null || bytePos + 4 > bytes.Length) continue;

                    // Find this buffer's Wasm offset
                    for (int bi = 0; bi < bufferInfos.Count; bi++)
                    {
                        if (bufferInfos[bi].buffer == wasmBuf)
                        {
                            int wasmOffset = bufferOffsets[bi];

                            // Get SubView byte offset
                            int subOffset = 0;
                            try
                            {
                                var baseProp = view.GetType().GetProperty("BaseView");
                                object viewObj = baseProp != null ? baseProp.GetValue(view)! : view;
                                var indexProp = viewObj.GetType().GetProperty("Index",
                                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (indexProp?.GetValue(viewObj) is long idx)
                                    subOffset = (int)(idx * GetViewElementSizeBytes(view));
                            }
                            catch { }

                            int correctPtr = wasmOffset + subOffset;
                            BitConverter.TryWriteBytes(bytes.AsSpan(bytePos), correctPtr);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WasmBackend.Log($"[Wasm-CRITICAL] PatchViewPointersInStruct FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Element size for SubView byte-offset math. Uses the view's generic element
        /// type, not <see cref="MemoryBuffer.ElementSize"/> (can disagree after Cast).
        /// </summary>
        private static int GetViewElementSizeBytes(IArrayView view)
        {
            int elemSize = view.Buffer.ElementSize;
            try
            {
                var viewType = view.GetType();
                var baseProp = viewType.GetProperty("BaseView");
                object viewObj = baseProp != null ? baseProp.GetValue(view)! : view;
                var genericType = viewObj.GetType();
                if (genericType.IsGenericType)
                {
                    int actualSize = global::ILGPU.Interop.SizeOf(genericType.GetGenericArguments()[0]);
                    if (actualSize > 0)
                        return actualSize;
                }
            }
            catch { }
            return elemSize;
        }

        private static void FindViewFieldsInStruct(
            object obj, Type type, int baseOffset,
            List<(IArrayView view, int bytePos)> results)
        {
            // Use Unsafe.SizeOf via ILGPU's Interop to get the struct size
            int runningOffset = baseOffset;
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                object? val;
                try { val = field.GetValue(obj); }
                catch { runningOffset += IntPtr.Size; continue; }
                if (val == null) { runningOffset += IntPtr.Size; continue; }

                if (val is IArrayView iav)
                {
                    // The NativePtr is at the start of the ArrayView's memory layout
                    results.Add((iav, runningOffset));
                    runningOffset += global::ILGPU.Interop.SizeOf(field.FieldType);
                }
                else if (val.GetType().IsValueType && !val.GetType().IsPrimitive && !val.GetType().IsEnum)
                {
                    // Recurse into nested struct
                    FindViewFieldsInStruct(val, field.FieldType, runningOffset, results);
                    runningOffset += global::ILGPU.Interop.SizeOf(field.FieldType);
                }
                else
                {
                    try { runningOffset += System.Runtime.InteropServices.Marshal.SizeOf(field.FieldType); }
                    catch { runningOffset += IntPtr.Size; }
                }
            }
        }

        private static void WriteViewPtrToStructBytes(
            byte[] bytes,
            int fieldOffset,
            int fieldSize,
            IArrayView? fieldVal,
            Dictionary<WasmMemoryBuffer, int> uniqueBuffers,
            List<int> bufferOffsets)
        {
            if (fieldVal is IArrayView iavInner)
            {
                var wasmBuf = iavInner.Buffer as WasmMemoryBuffer;
                if (wasmBuf != null && uniqueBuffers.TryGetValue(wasmBuf, out int bufIdxInner))
                {
                    int wasmOffset = bufferOffsets[bufIdxInner];
                    int subOffset = 0;
                    try
                    {
                        var viewType = fieldVal.GetType();
                        var baseProp = viewType.GetProperty("BaseView");
                        object viewObj = baseProp != null ? baseProp.GetValue(fieldVal)! : fieldVal;
                        var indexProp = viewObj.GetType().GetProperty("Index",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (indexProp?.GetValue(viewObj) is long idx)
                            subOffset = (int)(idx * GetViewElementSizeBytes(iavInner));
                    }
                    catch { }

                    long ptr = wasmOffset + subOffset;
                    if (fieldSize >= 8)
                        BitConverter.TryWriteBytes(bytes.AsSpan(fieldOffset, 8), ptr);
                    else
                        BitConverter.TryWriteBytes(bytes.AsSpan(fieldOffset, Math.Min(4, fieldSize)), (int)ptr);
                    return;
                }
            }

            if (fieldSize >= 8)
                BitConverter.TryWriteBytes(bytes.AsSpan(fieldOffset, 8), 0L);
            else
                BitConverter.TryWriteBytes(bytes.AsSpan(fieldOffset, Math.Min(4, fieldSize)), 0);
        }

        /// <summary>
        /// Depth-first collection of every <see cref="IArrayView"/> in a struct, in
        /// declaration order. Used to patch IR view-pointer slots after a CLR blit.
        /// </summary>
        private static void CollectArrayViewsFromStruct(object structValue, List<IArrayView> results)
        {
            var fields = structValue.GetType().GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                object? val;
                try { val = field.GetValue(structValue); }
                catch { continue; }
                if (val == null) continue;

                if (val is IArrayView iav)
                {
                    results.Add(iav);
                }
                else if (val.GetType().IsValueType && !val.GetType().IsPrimitive && !val.GetType().IsEnum)
                {
                    CollectArrayViewsFromStruct(val, results);
                }
            }
        }

        /// <summary>
        /// Recursively flattens a CLR struct into a list of leaf values (depth-first).
        /// The order matches ILGPU's IR StructureType flattening, since both process
        /// fields in declaration order.
        ///
        /// For IArrayView fields: adds the view itself (for view pointer patching).
        /// For primitive fields: adds the boxed primitive value.
        /// For nested structs: recurses depth-first.
        /// </summary>
        private static void FlattenCLRStruct(object structValue, List<object> result)
        {
            var type = structValue.GetType();
            var fields = type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                object? val;
                try { val = field.GetValue(structValue); }
                catch { result.Add(0); continue; }
                if (val == null) { result.Add(0); continue; }

                if (val is IArrayView)
                {
                    // ILGPU's IR represents ArrayView as a single AddressSpaceType (just
                    // the pointer). The view's Index and Length are NOT separate fields in
                    // the IR struct. So we add only the view itself for ptr patching.
                    //
                    // For ArrayView1D<T, TStride>, the IR also includes Extent and Stride
                    // as separate fields (from the wrapper struct, not from ArrayView<T>).
                    // We add those by recursing into the ArrayView1D's non-BaseView fields.
                    var viewType = val.GetType();
                    var baseProp = viewType.GetProperty("BaseView");

                    // The view pointer (maps to AddressSpaceType in IR)
                    object baseView = baseProp != null ? baseProp.GetValue(val)! : val;
                    result.Add(baseView is IArrayView ? baseView : val);

                    // If ArrayView1D: add Extent and Stride (NOT Index/Length — those
                    // are internal to ArrayView<T> and not in the IR struct)
                    if (baseProp != null)
                    {
                        // Extent (LongIndex1D → flattens to Int64)
                        var extentProp = viewType.GetProperty("Extent");
                        if (extentProp != null)
                        {
                            var extent = extentProp.GetValue(val);
                            if (extent != null)
                                FlattenCLRStruct(extent, result);
                        }

                        // Stride (e.g., Stride1D.Dense → flattens to its internal fields)
                        var strideProp = viewType.GetProperty("Stride");
                        if (strideProp != null)
                        {
                            var stride = strideProp.GetValue(val);
                            if (stride != null)
                            {
                                if (stride.GetType().IsPrimitive)
                                {
                                    result.Add(stride);
                                }
                                else
                                {
                                    int beforeStride = result.Count;
                                    FlattenCLRStruct(stride, result);
                                    // Empty structs (like Dense) have no instance fields.
                                    // ILGPU adds an Int8 padding field for them.
                                    if (result.Count == beforeStride)
                                        result.Add((byte)0);
                                }
                            }
                        }
                    }
                }
                else if (val.GetType().IsPrimitive || val.GetType().IsEnum)
                {
                    result.Add(val);
                }
                else if (val.GetType().IsValueType)
                {
                    // Nested struct: recurse
                    int beforeCount = result.Count;
                    FlattenCLRStruct(val, result);
                    // If the struct had no fields (like Stride1D.Dense which has only
                    // computed properties), ILGPU adds an Int8 padding field.
                    // Emit a default 0 to match.
                    if (result.Count == beforeCount)
                        result.Add((byte)0);
                }
                else
                {
                    result.Add(val);
                }
            }
        }

        /// <summary>
        /// Writes a primitive value to a byte array at the specified offset.
        /// </summary>
        private static void WritePrimitiveToBytes(byte[] bytes, int offset, object value, byte wasmType, int size)
        {
            if (offset + size > bytes.Length) return;
            try
            {
                switch (wasmType)
                {
                    case WasmOpCodes.I32:
                        int i32Val = value switch
                        {
                            int i => i,
                            uint u => (int)u,
                            short s => s,
                            ushort us => us,
                            byte b => b,
                            sbyte sb => sb,
                            bool bl => bl ? 1 : 0,
                            _ => Convert.ToInt32(value)
                        };
                        BitConverter.TryWriteBytes(bytes.AsSpan(offset), i32Val);
                        break;
                    case WasmOpCodes.I64:
                        long i64Val = value switch
                        {
                            long l => l,
                            ulong ul => (long)ul,
                            int i => i,
                            uint u => u,
                            _ => Convert.ToInt64(value)
                        };
                        BitConverter.TryWriteBytes(bytes.AsSpan(offset), i64Val);
                        break;
                    case WasmOpCodes.F32:
                        float f32Val = value switch
                        {
                            float f => f,
                            _ => Convert.ToSingle(value)
                        };
                        BitConverter.TryWriteBytes(bytes.AsSpan(offset), f32Val);
                        break;
                    case WasmOpCodes.F64:
                        double f64Val = value switch
                        {
                            double d => d,
                            float f => f,
                            _ => Convert.ToDouble(value)
                        };
                        BitConverter.TryWriteBytes(bytes.AsSpan(offset), f64Val);
                        break;
                }
            }
            catch { }
        }

        #endregion

        #region Memory Management

        /// <inheritdoc/>
        protected override MemoryBuffer AllocateRawInternal(long length, int elementSize)
        {
            return new WasmMemoryBuffer(this, length, elementSize);
        }

        protected override void SynchronizeInternal()
        {
            // Can't block-wait in single-threaded Blazor WASM.
            // But we CAN clean up completed tasks and surface errors,
            // matching WebGPU's Synchronize which flushes + checks errors.
            for (int i = _pendingWork.Count - 1; i >= 0; i--)
            {
                var task = _pendingWork[i];
                if (task.IsCompleted)
                {
                    _pendingWork.RemoveAt(i);
                    if (task.IsFaulted)
                        throw task.Exception!.InnerException ?? task.Exception;
                }
            }
        }

        /// <summary>
        /// Asynchronously waits for all pending kernel dispatches to complete.
        /// Overrides the core no-op-on-Wasm <see cref="Accelerator.SynchronizeAsync"/>
        /// with the real worker-dispatch drain (the synchronous
        /// <see cref="Accelerator.Synchronize"/> only reaps already-completed tasks).
        /// </summary>
        public override async Task SynchronizeAsync()
        {
            if (_pendingWork.Count > 0)
            {
                await Task.WhenAll(_pendingWork);
                _pendingWork.Clear();
            }
        }

        #endregion

        #region Helpers

        private static (int x, int y, int z) GetGridDimensions(object dimension)
        {
            return dimension switch
            {
                Index1D i1 => (i1.X, 1, 1),
                Index2D i2 => (i2.X, i2.Y, 1),
                Index3D i3 => (i3.X, i3.Y, i3.Z),
                LongIndex1D l1 => ((int)l1.X, 1, 1),
                LongIndex2D l2 => ((int)l2.X, (int)l2.Y, 1),
                LongIndex3D l3 => ((int)l3.X, (int)l3.Y, (int)l3.Z),
                KernelConfig config => (
                    config.GridDim.X * config.GroupDim.X,
                    config.GridDim.Y * config.GroupDim.Y,
                    config.GridDim.Z * config.GroupDim.Z),
                _ => throw new NotSupportedException($"Unsupported dimension type: {dimension.GetType()}")
            };
        }

        private (int x, int y, int z) GetGroupDimensions(KernelConfig config)
        {
            var groupDim = config.GroupDim;
            return (Math.Max(groupDim.X, 1), Math.Max(groupDim.Y, 1), Math.Max(groupDim.Z, 1));
        }

        /// <summary>
        /// Extracts the group size from the dimension object.
        /// For KernelConfig, returns GroupDim product.
        /// For auto-grouped kernels (Index1D/2D/3D), returns the estimated group size.
        /// </summary>
        private int GetGroupSize(object dimension, WasmCompiledKernel compiledKernel)
        {
            if (dimension is KernelConfig config)
            {
                return config.GroupDim.X * config.GroupDim.Y * config.GroupDim.Z;
            }
            // For auto-grouped kernels, the group size is determined by EstimateGroupSizeInternal
            // which returns 64 for the Wasm backend.
            return compiledKernel.HasBarriers ? 64 : 1;
        }

        #endregion

        #region Required Overrides (stubs)

        protected override int EstimateGroupSizeInternal(
            Kernel kernel, int dynamicSharedMemorySizeInBytes,
            int maxGroupSize,
            out int groupSize)
        {
            groupSize = 64;
            return 64;
        }

        protected override int EstimateGroupSizeInternal(
            Kernel kernel, Func<int, int> computeSharedMemorySize,
            int maxGroupSize, out int groupSize)
        {
            groupSize = 64;
            return 64;
        }

        protected override int EstimateMaxActiveGroupsPerMultiprocessorInternal(
            Kernel kernel, int groupSize, int dynamicSharedMemorySizeInBytes) => 1;

        protected override void EnablePeerAccessInternal(Accelerator otherAccelerator) { }
        protected override void DisablePeerAccessInternal(Accelerator otherAccelerator) { }
        protected override bool CanAccessPeerInternal(Accelerator otherAccelerator) => false;

        public override TExtension CreateExtension<TExtension, TExtensionProvider>(TExtensionProvider provider) =>
            default!;

        protected override AcceleratorStream CreateStreamInternal() =>
            new WasmStream(this);

        protected override PageLockScope<T> CreatePageLockFromPinnedInternal<T>(IntPtr ptr, long numElements) =>
            throw new NotSupportedException("Page locking is not supported in Wasm backend.");

        protected override void OnBind() { }
        protected override void OnUnbind() { }

        protected override void DisposeAccelerator_SyncRoot(bool disposing)
        {
            if (disposing)
            {
                // Mark disposed before tearing down workers so any in-flight RunKernel call
                // rejects cleanly instead of queuing work onto a dead pool.
                _disposed = true;

                // Fault every TCS that's still waiting on a worker response. Worker.Terminate()
                // below kills the Promise chain without replying, so without this the awaiting
                // Task.WhenAll would hang forever — the "zombie dispatch" that holds worker state
                // alive in the test runner and cascades into the next test.
                TaskCompletionSource[] stranded;
                lock (_pendingTcsLock)
                {
                    stranded = _pendingTcs.ToArray();
                    _pendingTcs.Clear();
                }
                foreach (var tcs in stranded)
                    tcs.TrySetException(new ObjectDisposedException(nameof(WasmAccelerator),
                        "WasmAccelerator disposed while a kernel dispatch was in flight. The worker pool has been terminated."));

                _pendingWork.Clear();
                _initializedWorkersByKernel.Clear();
                _activeDispatchCount = 0;
                _cachedMemoryBuffer?.Dispose();
                _cachedMemoryBuffer = null;
                _cachedWasmMemory?.Dispose();
                _cachedWasmMemory = null;
                _cachedWasmPages = 0;
                _workerPool?.Dispose();
                _workerPool = null;
            }
        }

        #endregion
    }

    /// <summary>
    /// Wasm-specific kernel wrapper.
    /// </summary>
    public class WasmKernel : Kernel
    {
        public WasmKernel(Accelerator accelerator, CompiledKernel compiledKernel, MethodInfo launcher)
            : base(accelerator, compiledKernel, launcher) { }

        public new WasmCompiledKernel CompiledKernel => (WasmCompiledKernel)base.CompiledKernel;

        protected override void DisposeAcceleratorObject(bool disposing) { }
    }
}
