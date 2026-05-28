// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmBackend.cs
//
// The ILGPU backend that compiles IR to WebAssembly binary modules.
// Mirrors WorkersBackend but emits Wasm instead of JavaScript.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Backends.EntryPoints;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Intrinsics;
using global::ILGPU.IR.Values;
using global::ILGPU.Runtime;
using System.IO;
using System.Reflection;
using System.Text;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>
    /// WebAssembly backend for ILGPU.
    /// Compiles ILGPU IR to WebAssembly binary modules for execution in Web Workers.
    /// </summary>
    public class WasmBackend : CodeGeneratorBackend<
        WasmIntrinsicHandler,
        WasmCodeGenerator.GeneratorArgs,
        WasmCodeGenerator,
        StringBuilder>
    {
        #region Static

        /// <summary>
        /// Backend type ID for Wasm (custom enum value).
        /// </summary>
        public static readonly BackendType BackendTypeWasm = BackendType.Wasm;

        /// <summary>
        /// Controls verbose debug logging.
        /// </summary>
        public static bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// DIAGNOSTIC RE-TEST HARNESS (default false — leave it false in production).
        /// When true, the DISPATCHER phase barrier and group barrier (in
        /// <see cref="GeneratePhaseDispatcher"/>) emit <c>memory.atomic.notify</c>
        /// (last worker, <c>int.MaxValue</c> = wake all) + <c>memory.atomic.wait32</c>
        /// (waiters, 1ms self-healing timeout + spurious-wakeup defense) so workers
        /// SLEEP instead of spin-waiting on the generation counter. Default false =
        /// pure spin-wait.
        ///
        /// VERDICT (2026-05-24, Tuvok — re-validating the rc.27 spin fallback):
        /// wait/notify STILL races on current Chrome + current backend. With this ON,
        /// large multi-group RadixSorts fail with sort-order violations / value
        /// duplicates (1.4M: 1067 violations, 500K: 187 violations, 1M: duplicate
        /// keys); small single-group sorts pass. The failures are memory-VISIBILITY
        /// failures (a woken worker proceeds on an advanced generation but does not
        /// see the writes that happened-before the gen bump), NOT a timeout-logic bug
        /// — our codegen is seq_cst-correct (fence before the gen store; seq_cst load
        /// of the gen in the waiter synchronizes-with it). This is a V8 linear-memory
        /// wait/notify ordering bug (chromium#490434403 family).
        ///
        /// The April "wait32 spills ~275 kernel locals" hypothesis is DISPROVEN: the
        /// barrier lives in the dispatcher function, which has only ~38 locals, and it
        /// still races. So reducing local count cannot dodge it — this is purely a V8
        /// platform bug, not fixable in our codegen. Pure spin (atomic.load loop)
        /// sidesteps the buggy futex path entirely and is correct.
        ///
        /// Kept as a gated re-test harness: flip ON and run the WasmTests RadixSort
        /// canaries to re-validate when a future Chrome/V8 ships a FutexEmulation fix.
        /// Full investigation: Plans/wasm-waitnotify-still-races-2026-05-24.md.
        /// </summary>
        public static bool UseWaitNotifyBarriers { get; set; } = false;

        /// <summary>
        /// DIAGNOSTIC (Tuvok 2026-05-26) — SharedArrayBuffer-growth-lag hypothesis test
        /// for the residual large-multi-group Wasm sort corruption. When set, the
        /// accelerator forces a real 1-page <c>WebAssembly.Memory.grow</c> (plus the full
        /// production re-get-buffer + worker re-instantiation path) on EVERY dispatch,
        /// instead of only when a bigger kernel arrives. This exercises the grow path
        /// ~16-40x per sort versus ~1x naturally, so if grow/re-instantiation visibility
        /// is the trigger, a WARM sort loop (otherwise clean, ~0/60 baseline) will corrupt.
        /// Capped at <c>MaxLinearMemoryPages</c> so it degrades to a no-op near the budget.
        /// RETAINED as default-off investigation tooling (2026-05-26): the grow/SAB-resize
        /// hypothesis is DISFAVORED (this amplify run was 0/750 on the localized kernel) but
        /// was NOT definitively killed. If the heavy-dup ±1 residual recurs after the monotonic
        /// kernelId fix (`WasmAccelerator._nextKernelId`), use this + <see cref="PreGrowPages"/>
        /// to re-test grow without rebuilding the harness. Default OFF — zero production impact.
        /// </summary>
        public static bool ForceGrowEachDispatch { get; set; } = false;

        /// <summary>
        /// DIAGNOSTIC (Tuvok 2026-05-26) — the INVERSE of <see cref="ForceGrowEachDispatch"/>,
        /// and the decisive test of the SharedArrayBuffer-growth-lag hypothesis. When &gt; 0,
        /// the accelerator allocates its shared <c>WebAssembly.Memory</c> with this many
        /// initial pages on first creation, so that no dispatch in a normal run ever needs to
        /// call <c>memory.grow</c> (and never triggers worker re-instantiation). Set it large
        /// enough to cover the biggest kernel in the run (e.g. 8192 = 512 MiB covers the 4M
        /// sort) and run a full WasmTests sweep: if the residual large-sort corruption STILL
        /// fires with ZERO grows, grow/re-instantiation is definitively NOT the cause — one
        /// failure suffices to rule it out. If many sweeps go clean, grow is implicated.
        /// Capped at <c>MaxLinearMemoryPages</c>. RETAINED as default-off investigation tooling
        /// (2026-05-26): grow hypothesis disfavored but NOT definitively killed — keep this ready
        /// to re-test if the residual recurs after the monotonic kernelId fix. Default 0 — off.
        /// </summary>
        public static int PreGrowPages { get; set; } = 0;

        /// <summary>
        /// When set, dumps generated Wasm binaries to this directory. Desktop only.
        /// </summary>
        public static string? WasmDumpPath { get; set; }

        /// <summary>Diagnostic: info about all compiled kernels.</summary>
        public static readonly List<string> AllKernelInfos = new();

        /// <summary>Diagnostic: last compiled Wasm binary (for inspection).</summary>
        public static byte[]? LastWasmBinary { get; set; }

        /// <summary>
        /// Diagnostic: snapshot of the codegen's per-Store trace results from the LAST
        /// kernel compile. Used to investigate copy-OUT skip misclassifications. Per
        /// entry: "TargetIRType -> param[N]" where N is the result of TraceToParameter
        /// or -1 if the target didn't resolve to a kernel parameter.
        /// </summary>
        public static List<string> LastStoreTargetTrace { get; set; } = new();

        /// <summary>
        /// Diagnostic: snapshot of the kernel-parameter indices that the last compile
        /// identified as Store targets (i.e., the kernel WRITES to these params).
        /// Used by the dispatcher to skip copy-OUT for input-only buffers, and by tests
        /// to verify the trace catches the expected writes.
        /// </summary>
        public static HashSet<int> LastWrittenParamIndices { get; set; } = new();

        /// <summary>
        /// Diagnostic: last dispatch's per-arg copy-OUT classification.
        /// Format per entry: "i=N kind=View/Scalar bufIdx=M ir-param-match=true/false"
        /// </summary>
        public static List<string> LastDispatchCopyOutDiag { get; set; } = new();

        /// <summary>Diagnostic: all compiled Wasm binaries (for capturing multi-kernel compilations like RadixSort).</summary>
        public static List<byte[]> AllWasmBinaries = new();

        /// <summary>Callback invoked whenever a Wasm kernel is compiled. Parameters: (kernelName, wasmBinary, info).</summary>
        public static Action<string, byte[], string>? OnKernelCompiled { get; set; }

        /// <summary>
        /// Circular buffer of recent log messages for diagnostics.
        /// Always captures the last N messages regardless of VerboseLogging.
        /// </summary>
        public static readonly List<string> RecentLogs = new();
        private static readonly int MaxRecentLogs = 500;

        /// <summary>
        /// Writes a message to the console and captures to RecentLogs.
        /// Caller MUST check <see cref="VerboseLogging"/> BEFORE constructing the message string
        /// to avoid allocating interpolated strings when logging is disabled.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void Log(string message)
        {
            Console.WriteLine(message);
            RecentLogs.Add(message);
            if (RecentLogs.Count > MaxRecentLogs)
                RecentLogs.RemoveAt(0);
        }

        #endregion

        #region Constructor

        public WasmBackend(Context context)
            : this(context, new WasmBackendOptions())
        {
        }

        public WasmBackend(Context context, WasmBackendOptions options)
            : base(
                  context,
                  new WasmCapabilityContext(),
                  BackendTypeWasm,
                  new WasmArgumentMapper(context))
        {
            Options = options ?? new WasmBackendOptions();

            InitIntrinsicProvider();
            RegisterMathIntrinsics();
            RegisterScanIntrinsics();

            InitializeKernelTransformers(builder =>
            {
                // No Wasm-specific transformers needed for Phase 1
            });

            // Hard reference for bundling
            _ = typeof(global::ILGPU.Algorithms.XMath);
        }

        #endregion

        #region Properties

        public WasmBackendOptions Options { get; }

        public new WasmArgumentMapper ArgumentMapper =>
            (WasmArgumentMapper)base.ArgumentMapper;

        /// <summary>
        /// The kernel function generator for the current compilation (set during CreateKernelCodeGenerator).
        /// </summary>
        internal WasmKernelFunctionGenerator? KernelGenerator { get; set; }

        #endregion

        #region Methods

        private static IntrinsicImplementationManager GetIntrinsicManager(Context context)
        {
            var prop = typeof(Context).GetProperty(
                "IntrinsicManager",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (prop == null)
                throw new InvalidOperationException("Could not find IntrinsicManager property on Context.");

            var manager = (IntrinsicImplementationManager)prop.GetValue(context)!;
            FixIntrinsicManager(manager);
            return manager;
        }

        private static void FixIntrinsicManager(IntrinsicImplementationManager manager)
        {
            try
            {
                var mgrType = typeof(IntrinsicImplementationManager);
                var containersField = mgrType.GetField("containers", BindingFlags.Instance | BindingFlags.NonPublic);
                if (containersField == null) return;

                var containers = (Array)containersField.GetValue(manager)!;
                int wasmIndex = (int)BackendTypeWasm;

                if (wasmIndex >= containers.Length || containers.GetValue(wasmIndex) == null)
                {
                    var containerType = mgrType.GetNestedType("BackendContainer", BindingFlags.NonPublic)!;
                    var createMethod = containerType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)!;

                    if (wasmIndex >= containers.Length)
                    {
                        if (VerboseLogging) Log($"Wasm: Resizing IntrinsicManager containers from {containers.Length} to {wasmIndex + 1}");
                        var newContainers = Array.CreateInstance(containerType, wasmIndex + 1);
                        Array.Copy(containers, newContainers, containers.Length);
                        containers = newContainers;
                        containersField.SetValue(manager, containers);
                    }

                    var newContainer = createMethod.Invoke(null, null);
                    containers.SetValue(newContainer, wasmIndex);
                    if (VerboseLogging) Log("Wasm: Initialized BackendContainer.");
                }
                else
                {
                    var containerType = mgrType.GetNestedType("BackendContainer", BindingFlags.NonPublic)!;
                    var container = containers.GetValue(wasmIndex);
                    var matchersField = containerType.GetField("matchers", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var matchers = matchersField.GetValue(container!);
                    if (matchers == null)
                    {
                        if (VerboseLogging) Log("Wasm: BackendContainer found but uninitialized. Re-initializing.");
                        var createMethod = containerType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)!;
                        var newContainer = createMethod.Invoke(null, null);
                        containers.SetValue(newContainer, wasmIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                if (VerboseLogging) Log($"Wasm: Error fixing IntrinsicManager: {ex}");
            }
        }

        private void RegisterRedirect(MethodInfo original, MethodInfo target)
        {
            if (original == null || target == null) return;
            if (VerboseLogging) Log($"Wasm: Redirecting {original.DeclaringType?.Name}.{original.Name} -> {target.DeclaringType?.Name}.{target.Name}");
            GetIntrinsicManager(Context).RegisterMethod(
                original,
                new global::ILGPU.Backends.Wasm.WasmIntrinsic(
                    target,
                    IntrinsicImplementationMode.Redirect));
        }

        private void RegisterScanIntrinsics()
        {
            var manager = GetIntrinsicManager(Context);
            var groupExtType = typeof(global::ILGPU.Algorithms.GroupExtensions);
            var wasmGroupType = typeof(SpawnDev.ILGPU.Wasm.Algorithms.WasmGroupExtensions);

            void RegScan(string name)
            {
                try
                {
                    var src = groupExtType.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
                    if (src == null) return;
                    manager.RegisterMethod(src, new global::ILGPU.Backends.Wasm.WasmIntrinsic(
                        wasmGroupType, name, IntrinsicImplementationMode.Redirect));
                    if (VerboseLogging) Log($"Wasm: Scan intrinsic {name} registered");
                }
                catch (Exception ex)
                {
                    if (VerboseLogging) Log($"Wasm: Scan intrinsic {name} FAILED: {ex.Message}");
                }
            }

            RegScan("Reduce");
            RegScan("AllReduce");
            RegScan("ExclusiveScan");
            RegScan("InclusiveScan");
            RegScan("ExclusiveScanWithBoundaries");
            RegScan("InclusiveScanWithBoundaries");
            RegScan("ExclusiveScanNextIteration");
            RegScan("InclusiveScanNextIteration");
        }

        private void RegisterMathIntrinsics()
        {
            var t = typeof(WasmIntrinsics);

            void RegAll(Type type, string name)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == name);

                foreach (var m in methods)
                {
                    MethodInfo target = m;
                    if (m.IsGenericMethod)
                    {
                        var gArgs = m.GetGenericArguments();
                        if (gArgs.Length == 1)
                        {
                            try { target = m.MakeGenericMethod(typeof(float)); } catch { continue; }
                        }
                        else continue;
                    }

                    var pTypes = target.GetParameters().Select(p => p.ParameterType).ToArray();
                    var wrapper = t.GetMethod(
                        name,
                        BindingFlags.Public | BindingFlags.Static,
                        null, pTypes, null);

                    if (wrapper != null)
                    {
                        if (VerboseLogging) Log($"Wasm: Mapping {type.Name}.{name}({string.Join(",", pTypes.Select(pt => pt.Name))}) to {t.Name}.{name}");
                        RegisterRedirect(target, wrapper);
                    }
                }
            }

            // Unary — redirect Math.Round/Truncate/Sign to throw-free wrappers
            RegAll(typeof(Math), "Abs");
            RegAll(typeof(MathF), "Abs");
            RegAll(typeof(Math), "Sign");
            RegAll(typeof(MathF), "Sign");
            RegAll(typeof(Math), "Round");
            RegAll(typeof(MathF), "Round");
            RegAll(typeof(Math), "Truncate");
            RegAll(typeof(MathF), "Truncate");

            // Binary
            RegAll(typeof(Math), "Atan2");
            RegAll(typeof(MathF), "Atan2");
            RegAll(typeof(Math), "Max");
            RegAll(typeof(MathF), "Max");
            RegAll(typeof(Math), "Min");
            RegAll(typeof(MathF), "Min");
            RegAll(typeof(Math), "Pow");
            RegAll(typeof(MathF), "Pow");

            // Ternary
            RegAll(typeof(Math), "Clamp");
            RegAll(typeof(MathF), "Clamp");
            RegAll(typeof(Math), "FusedMultiplyAdd");
            RegAll(typeof(MathF), "FusedMultiplyAdd");

            // IntrinsicMath (targets of RemappedIntrinsics)
            RegAll(typeof(IntrinsicMath), "Abs");
            RegAll(typeof(IntrinsicMath), "Min");
            RegAll(typeof(IntrinsicMath), "Max");

            // XMath Rsqrt/Rcp
            try
            {
                var xmathType = Type.GetType("ILGPU.Algorithms.XMath, ILGPU.Algorithms");
                if (xmathType != null)
                {
                    RegAll(xmathType, "Rsqrt");
                    RegAll(xmathType, "Rcp");
                    if (VerboseLogging) Log("Wasm: Registered XMath intrinsics (Rsqrt, Rcp)");
                }
            }
            catch (Exception ex)
            {
                if (VerboseLogging) Log($"Wasm: Error registering XMath intrinsics: {ex.Message}");
            }
        }

        protected override EntryPoint CreateEntryPoint(
            in EntryPointDescription entry,
            in BackendContext backendContext,
            in KernelSpecialization specialization) =>
            new EntryPoint(
                entry,
                backendContext.SharedMemorySpecification,
                specialization);

        protected override StringBuilder CreateKernelBuilder(
            EntryPoint entryPoint,
            in BackendContext backendContext,
            in KernelSpecialization specialization,
            out WasmCodeGenerator.GeneratorArgs data)
        {
            var builder = new StringBuilder();

            builder.AppendLine("//");
            builder.Append("// Generated by SpawnDev.ILGPU.Wasm v");
            builder.AppendLine(Context.Version);
            builder.AppendLine("//");
            builder.AppendLine();

            data = new WasmCodeGenerator.GeneratorArgs(
                this,
                entryPoint,
                backendContext.SharedAllocations,
                backendContext.DynamicSharedAllocations);

            return builder;
        }

        protected override WasmCodeGenerator CreateFunctionCodeGenerator(
            Method method,
            Allocas allocas,
            WasmCodeGenerator.GeneratorArgs data)
        {
            // Store helper methods so the kernel generator can inline them
            data.HelperMethods[method] = allocas;
            return new WasmFunctionGenerator(data, method, allocas);
        }

        /// <summary>
        /// Math function names that will be imported into every Wasm module.
        /// Order matters — the function index assignment must match CreateKernel.
        /// </summary>
        internal static readonly string[] UnaryMathFuncs = { "sin", "cos", "tan", "asin", "acos", "atan",
                                         "sinh", "cosh", "tanh", "exp", "log", "log2",
                                         "log10", "round", "truncate", "sign", "exp2",
                                         "sqrt", "abs", "ceil", "floor" };

        internal static readonly string[] BinaryMathFuncs = { "pow", "atan2" };

        protected override WasmCodeGenerator CreateKernelCodeGenerator(
            in AllocaKindInformation sharedAllocations,
            Method method,
            Allocas allocas,
            WasmCodeGenerator.GeneratorArgs data)
        {
            var gen = new WasmKernelFunctionGenerator(data, method, allocas);

            // Pre-populate math imports with deterministic indices.
            // These MUST match the import order in CreateKernel exactly.
            // Import function indices start at 0.
            var mathImports = new Dictionary<string, uint>();
            uint funcIdx = 0;
            foreach (var name in UnaryMathFuncs)
                mathImports[name] = funcIdx++;
            foreach (var name in BinaryMathFuncs)
                mathImports[name] = funcIdx++;
            gen.MathImports = mathImports;

            // Variant C Step 1 (Trip 2026-05-27): reserve one extra import slot for the
            // env.notify shim (i32 byteAddr, i32 count) -> i32. The physical import is
            // added in CreateKernel after the math imports; bumping ExtraImportCount here
            // ensures AssignHelperFunctionIndices (called from GenerateCode, which runs
            // BEFORE CreateKernel) computes correct helper function indices that account
            // for the notify slot. Declared unconditionally - non-barrier kernels never
            // call it, but the unused import is ~10 bytes and keeps the index space
            // identical across all kernel shapes. The call $notify EMIT is gated on
            // enableYieldEscape (added in Step 4); Step 1 only adds the declaration.
            data.ExtraImportCount = 1;

            // NOTE: Function index assignment for multi-block helpers is done in
            // WasmKernelFunctionGenerator.AssignHelperFunctionIndices(), called at the
            // start of GenerateCode(). This is because CreateKernelCodeGenerator runs
            // BEFORE CreateFunctionCodeGenerator (ILGPU compilation order), so
            // data.HelperMethods is empty at this point.

            KernelGenerator = gen;
            return gen;
        }

        protected override CompiledKernel CreateKernel(
            EntryPoint entryPoint,
            CompiledKernel.KernelInfo? kernelInfo,
            StringBuilder builder,
            WasmCodeGenerator.GeneratorArgs data)
        {
            var kernelGen = KernelGenerator!;

            // Build the Wasm module
            var moduleBuilder = new WasmModuleBuilder();

            // Import shared memory. The module's declared import maximum MUST match (or
            // exceed) the host's WebAssembly.Memory `maximum` value, otherwise
            // WebAssembly.instantiate fails with "memory import has a larger maximum size N
            // than the module's declared maximum M" - the imported memory's max must be <=
            // the import's declared max per the spec. Threading the same
            // WasmBackendOptions.MaxLinearMemoryPages value (default 16384 / 1 GiB,
            // configurable up to 65536 / 4 GiB) keeps both ends in sync. Default
            // SharedArrayBuffer reservation budget on Chrome accommodates 16384 pages
            // without RangeError; consumers that opt into a larger cap accept that risk.
            moduleBuilder.ImportSharedMemory("env", "memory", 1, (uint)Options.MaxLinearMemoryPages);

            // Import math functions from JavaScript Math object
            var mathImports = new Dictionary<string, uint>();

            // Add unary math type: (f64) -> f64
            int unaryTypeIdx = moduleBuilder.AddFuncType(
                new byte[] { WasmOpCodes.F64 },
                new byte[] { WasmOpCodes.F64 });

            // Add binary math type: (f64, f64) -> f64
            int binaryTypeIdx = moduleBuilder.AddFuncType(
                new byte[] { WasmOpCodes.F64, WasmOpCodes.F64 },
                new byte[] { WasmOpCodes.F64 });

            foreach (var name in UnaryMathFuncs)
            {
                int idx = moduleBuilder.ImportFunction("Math", name, unaryTypeIdx);
                mathImports[name] = (uint)idx;
            }

            foreach (var name in BinaryMathFuncs)
            {
                int idx = moduleBuilder.ImportFunction("Math", name, binaryTypeIdx);
                mathImports[name] = (uint)idx;
            }

            // Variant C Step 1 (Trip 2026-05-27): env.notify import shim. Type =
            // (i32 byteAddr, i32 count) -> i32 (woken count). The JS side (WorkerPool.cs
            // WasmBootstrapScript) supplies a function that calls Atomics.notify on an
            // Int32Array view over the shared SAB. Spec-correct, per syg's clarification
            // on tc39/ecma262 #3800 (see _wasm_fork/NOTES_ecma262_3800_syg.md). Declared
            // AFTER all math imports so math indices in CreateKernelCodeGenerator's
            // mathImports dict (assigned 0..N-1 there) stay valid; notify takes index N.
            // The kernel function comes next and gets index N+1.
            // The matching ExtraImportCount=1 bump lives in CreateKernelCodeGenerator
            // (above) so AssignHelperFunctionIndices computes helper indices correctly.
            // Step 1 only DECLARES; the call $notify emit comes in Step 2 (gated on
            // enableYieldEscape per Step 4). Currently this is an unused import.
            int notifyTypeIdx = moduleBuilder.AddFuncType(
                new byte[] { WasmOpCodes.I32, WasmOpCodes.I32 },
                new byte[] { WasmOpCodes.I32 });
            int notifyFuncIdx = moduleBuilder.ImportFunction("env", "notify", notifyTypeIdx);

            // Pass math imports to the code generator
            kernelGen.MathImports = mathImports;

            // Add function type for the kernel.
            // Phase-mode kernels return i32 (0=done, 1=yielded at barrier).
            // Non-phase-mode kernels also return i32 for signature consistency (always returns 0).
            var paramTypes = kernelGen.GetParamTypes();
            int typeIdx = moduleBuilder.AddFuncType(paramTypes, new byte[] { WasmOpCodes.I32 });

            // Add kernel function (index = importFuncCount + 0)
            int funcIdx = moduleBuilder.AddFunction(typeIdx);

            // Export as "kernel"
            moduleBuilder.ExportFunction("kernel", funcIdx);

            // Set kernel function body (defined function index 0)
            moduleBuilder.SetFunctionBody(0, kernelGen._locals, kernelGen.Code.ToArray());

            // Generate helper function bodies for multi-block helpers
            int definedFuncIndex = 1; // 0 = kernel
            int maxSharedMemorySize = data.SharedMemorySize;

            foreach (var helperMethod in data.HelperFunctionOrder)
            {
                var helperAllocas = data.HelperMethods[helperMethod];
                var helperGen = new WasmKernelFunctionGenerator(data, helperMethod, helperAllocas);
                var result = helperGen.GenerateAsHelper(
                    kernelGen.SharedAllocaOffsets,
                    kernelGen.SharedAllocaMetadata,
                    kernelGen.SharedMemorySizeValue,
                    mathImports);

                // Add helper function type.
                // Option E: helpers always return their natural result type.
                // The yield flag is communicated via scratch[0], not the return value.
                var helperResultTypes = result.ResultTypes;
                int helperTypeIdx = moduleBuilder.AddFuncType(result.ParamTypes, helperResultTypes);

                // Add helper function (index must match pre-assigned index)
                int helperFuncIdx = moduleBuilder.AddFunction(helperTypeIdx);
                int expectedIdx = data.HelperFunctionIndices[helperMethod];
                if (helperFuncIdx != expectedIdx)
                {
                    if (VerboseLogging) Log($"Wasm: WARNING: Helper '{helperMethod.Name}' funcIdx mismatch: got {helperFuncIdx}, expected {expectedIdx}");
                }

                // Set helper function body
                moduleBuilder.SetFunctionBody(definedFuncIndex, result.Locals, result.Code);
                definedFuncIndex++;

                // Track max shared memory (helpers may allocate Broadcast slots)
                if (result.SharedMemorySize > maxSharedMemorySize)
                    maxSharedMemorySize = result.SharedMemorySize;

                // Helper scratch is already included in ScratchPerThread via the kernel's
                // _helperScratchCumulativeOffset (extended into _scratchNextOffset).
                // Just ensure alignment.
                data.ScratchPerThread = (data.ScratchPerThread + 7) & ~7;

                if (VerboseLogging) Log($"[Wasm-Helper] '{helperMethod.Name}' funcIdx={helperFuncIdx}, params={result.ParamTypes.Length}, locals={result.Locals.Count}, code={result.Code.Length}b, barriers={result.BarrierCount}, resultTypes=[{string.Join(",", helperResultTypes.Select(t => $"0x{t:X2}"))}], phaseMode={data.PhaseCount > 1}");
            }

            // Update shared memory size to account for helper Broadcast slots
            data.SharedMemorySize = maxSharedMemorySize;

            // Add phase dispatcher for barrier kernels.
            // Moves the thread/phase loop from JS into Wasm, eliminating ~1M JS-Wasm
            // boundary crossings per dispatch for large sorts (260K elements).
            if (data.HasBarriers)
            {
                bool enableYieldEscape = Options.EnableYieldEscape ?? false;
                GeneratePhaseDispatcher(moduleBuilder, funcIdx, notifyFuncIdx, enableYieldEscape, paramTypes, definedFuncIndex);
                definedFuncIndex++;
            }

            // Emit binary
            var wasmBinary = moduleBuilder.Emit();

            // TEMP: removed debug dump

            // Dump Wasm binary to file for debugging (desktop only)
            if (WasmDumpPath != null && !OperatingSystem.IsBrowser())
            {
                try
                {
                    Directory.CreateDirectory(WasmDumpPath);
                    var name = $"kernel_{wasmBinary.Length}";
                    File.WriteAllBytes(Path.Combine(WasmDumpPath, $"{name}.wasm"), wasmBinary);
                }
                catch { }
            }

            // Record compilation info for diagnostics
            var info = $"Kernel params={paramTypes.Length} (userParams={data.ParamInfos.Count}), locals={kernelGen._locals.Count}, code={kernelGen.Code.Count}b, helpers={data.HelperFunctionOrder.Count}, sharedMem={data.SharedMemorySize}, barriers={data.BarrierCount}, hasBarriers={data.HasBarriers}, dynSharedElemSize={data.DynamicSharedElementSize}, scratchPerThread={data.ScratchPerThread}";
            if (VerboseLogging) Log($"[Wasm-Final] spt={data.ScratchPerThread} barriers={data.BarrierCount} phases={data.PhaseCount} helpers={data.HelperFunctionOrder.Count}");
            if (VerboseLogging)
            {
                Log($"--- GENERATED WASM BINARY ({wasmBinary.Length} bytes) ---");
                Log(info);
                Log("---");
            }
            // Only accumulate kernel info and binaries when debug dump is active.
            // These static lists grow unbounded and cause memory pressure over long sessions.
            if (WasmDumpPath != null || OnKernelCompiled != null)
            {
                AllKernelInfos.Add(info);
                AllWasmBinaries.Add(wasmBinary);
            }
            LastWasmBinary = wasmBinary;
            try { OnKernelCompiled?.Invoke($"kernel_{AllWasmBinaries.Count}", wasmBinary, info); } catch { }

            // Snapshot the codegen's trace on the static so tests + the dispatcher's
            // diag string can inspect it. Used to investigate trace gaps where the
            // codegen's TraceToParameter fails to identify the kernel's actual buffer
            // write — see _DevComms/SpawnDev.ILGPU/geordi-to-team-wasm-copy-out-race-2026-05-03.md.
            LastWrittenParamIndices = new HashSet<int>(kernelGen.WrittenParamIndices);
            LastStoreTargetTrace = new List<string>(kernelGen.StoreTargetTrace);
            if (VerboseLogging)
            {
                Log($"[Wasm-CopyOutTrace] writtenParams=[{string.Join(",", kernelGen.WrittenParamIndices)}] storeCount={kernelGen.StoreTargetTrace.Count}");
                for (int ti = 0; ti < kernelGen.StoreTargetTrace.Count && ti < 32; ti++)
                    Log($"[Wasm-CopyOutTrace]   #{ti}: {kernelGen.StoreTargetTrace[ti]}");
            }

            return new WasmCompiledKernel(
                Context,
                entryPoint,
                wasmBinary,
                data.ParamInfos.Count,
                data.ParamInfos,
                data.SharedMemorySize,
                data.BarrierCount,
                data.HasBarriers,
                data.DynamicSharedElementSize,
                data.ScratchPerThread,
                data.PhaseCount,
                kernelGen.WrittenParamIndices,
                kernelGen.StoreTargetTrace);
        }

        /// <summary>
        /// Generates a phase dispatcher function that runs the thread/phase loop
        /// entirely in Wasm. Eliminates JS-Wasm boundary crossings per phase.
        /// Dispatcher params: (threadStart, threadEnd, numGroups, groupSize,
        ///   gridDimX, gridDimY, scratchBase, scratchPerThread,
        ///   sharedMemBase, barrierBase, dynamicSharedLen, zeroRegionSize, ...userArgs)
        /// </summary>
        private void GeneratePhaseDispatcher(
            WasmModuleBuilder moduleBuilder,
            int kernelFuncIdx,
            int notifyFuncIdx,
            bool enableYieldEscape,
            byte[] kernelParamTypes,
            int definedFuncIndex)
        {
            // Dispatcher params: 11 system + N user (same user params as kernel)
            // Kernel params: 10 system (globalIdx..phase) + N user
            int kernelSystemParams = 10; // globalIdx, dimX, dimY, scratch, groupDimX, tid, sharedMem, barrier, dynShared, phase
            int userParamCount = kernelParamTypes.Length - kernelSystemParams;

            // Dispatcher system params
            var dispParamTypes = new List<byte>();
            dispParamTypes.Add(WasmOpCodes.I32); // 0: threadStart
            dispParamTypes.Add(WasmOpCodes.I32); // 1: threadEnd
            dispParamTypes.Add(WasmOpCodes.I32); // 2: numGroups
            dispParamTypes.Add(WasmOpCodes.I32); // 3: groupSize
            dispParamTypes.Add(WasmOpCodes.I32); // 4: gridDimX
            dispParamTypes.Add(WasmOpCodes.I32); // 5: gridDimY
            dispParamTypes.Add(WasmOpCodes.I32); // 6: scratchBase
            dispParamTypes.Add(WasmOpCodes.I32); // 7: scratchPerThread
            dispParamTypes.Add(WasmOpCodes.I32); // 8: sharedMemBase
            dispParamTypes.Add(WasmOpCodes.I32); // 9: barrierBase
            dispParamTypes.Add(WasmOpCodes.I32); // 10: dynamicSharedLen
            dispParamTypes.Add(WasmOpCodes.I32); // 11: zeroRegionSize (shared mem + barrier counters, for zeroing between groups)
            dispParamTypes.Add(WasmOpCodes.I32); // 12: workerCount (for inter-worker barriers)
            dispParamTypes.Add(WasmOpCodes.I32); // 13: fenceBase (for inter-worker atomic barriers)
            dispParamTypes.Add(WasmOpCodes.I32); // 14: yieldStateAddr (per-worker 16-byte buffer for spin-yield save/restore)
            dispParamTypes.Add(WasmOpCodes.I32); // 15: resumeMode (0=fresh, 1=resume from saved state at yieldStateAddr)
            int dispSystemParams = 16;

            // Add user params (same types as kernel's user params)
            for (int i = kernelSystemParams; i < kernelParamTypes.Length; i++)
                dispParamTypes.Add(kernelParamTypes[i]);

            int dispTypeIdx = moduleBuilder.AddFuncType(dispParamTypes.ToArray(), Array.Empty<byte>());
            int dispFuncIdx = moduleBuilder.AddFunction(dispTypeIdx);
            moduleBuilder.ExportFunction("dispatcher", dispFuncIdx);

            // Locals: g, phase, tid, anyYielded, r, zeroIdx, savedGen, arrived, spinCount, resumed,
            //         groupResume (11 i32)
            var locals = new List<WasmLocal>
            {
                new WasmLocal { Type = WasmOpCodes.I32, Count = 11 }
            };
            uint pG = (uint)dispParamTypes.Count;         // local index for g
            uint pPhase = pG + 1;
            uint pTid = pG + 2;
            uint pAnyYielded = pG + 3;
            uint pR = pG + 4;
            uint pZeroIdx = pG + 5;
            uint pSavedGen = pG + 6;
            uint pArrived = pG + 7;
            uint pSpinCount = pG + 8;     // counter for phase AND group barrier spin iterations
            uint pResumed = pG + 9;       // 1 if re-entered after a PHASE-barrier spin-yield (yieldFlag=1)
            uint pGroupResume = pG + 10;  // 1 if re-entered after a GROUP-barrier spin-yield (yieldFlag=2)

            // Yield-on-spin threshold. Pure spin runs ~5ns/iteration, so 1M = ~5ms before yielding to JS.
            // Tuning rationale (revised 2026-04-28 after Data's single-tab regression):
            //   100K (~500us) was too aggressive - a worker descheduled by an OS timeslice (~15ms on
            //   Windows) gets re-scheduled to find OTHER workers have all spun past 100K and yielded
            //   pointlessly, paying yield round-trips for what would have been a sub-ms wait. 1M (~5ms)
            //   stays UNDER the OS timeslice so a single timeslice's worth of waiting doesn't trigger
            //   yields, but yields fire promptly once we cross "real starvation" territory (multi-
            //   timeslice waits typical of CPU oversub).
            const int YIELD_SPIN_THRESHOLD = 1_000_000;
            // yieldStateAddr layout (16 bytes per worker):
            //   offset 0: yieldFlag  (i32) — 0 = normal exit; 1 = yielded at PHASE barrier;
            //                                2 = yielded at GROUP barrier (selects the resume path
            //                                + which gen slot JS parks on)
            //   offset 4: savedG     (i32) — group index at yield
            //   offset 8: savedPhase (i32) — phase index at yield (phase-barrier yield only)
            //   offset 12: savedGen  (i32) — generation value the spin loop was waiting on
            //                                (phase gen for yieldFlag=1, group gen for yieldFlag=2)

            var code = new List<byte>();

            if (enableYieldEscape)
            {
                // === SPIN-YIELD PROLOGUE === (Variant C path)
                // If resumeMode != 0, we were re-dispatched after yielding mid-phase-barrier-spin.
                // Restore (g, phase, savedGen) from yieldStateAddr; set pResumed=1 so the phase
                // loop body knows to skip the tid loop + arrival++ (already done before yield)
                // and jump straight to the spin loop with the saved savedGen.
                // If resumeMode == 0, fresh dispatch: g=0, pResumed=0.
                WasmModuleBuilder.EmitLocalGet(code, 15); // resumeMode
                code.Add(WasmOpCodes.I32Eqz);
                code.Add(WasmOpCodes.If);
                code.Add(WasmOpCodes.Void);
                // Fresh start: g = 0, resumed = 0, groupResume = 0
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pG);
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pResumed);
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pGroupResume);
                code.Add(WasmOpCodes.Else);
                // Resume: g = load(yieldStateAddr + 4). The yieldFlag (offset 0) selects which barrier
                // we yielded at: 1 = PHASE barrier (resume into the phase loop spin, as before);
                // 2 = GROUP barrier (skip the phase loop + group arrival, resume into the group spin).
                // (phase + savedGen are loaded inside the loop_g body so they apply to the right iteration.)
                WasmModuleBuilder.EmitLocalGet(code, 14); // yieldStateAddr
                code.Add(WasmOpCodes.I32Load);
                code.Add(0x02); code.Add(0x04); // align=2, offset=4 (savedG)
                WasmModuleBuilder.EmitLocalSet(code, pG);
                // if (load(yieldStateAddr+0 [yieldFlag]) == 2) group-resume; else phase-resume
                WasmModuleBuilder.EmitLocalGet(code, 14); // yieldStateAddr
                code.Add(WasmOpCodes.I32Load);
                code.Add(0x02); code.Add(0x00); // align=2, offset=0 (yieldFlag)
                WasmModuleBuilder.EmitI32Const(code, 2);
                code.Add(WasmOpCodes.I32Eq);
                code.Add(WasmOpCodes.If);
                code.Add(WasmOpCodes.Void);
                // GROUP-barrier resume: pResumed=0 (don't re-enter phase loop), pGroupResume=1
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pResumed);
                WasmModuleBuilder.EmitI32Const(code, 1);
                WasmModuleBuilder.EmitLocalSet(code, pGroupResume);
                code.Add(WasmOpCodes.Else);
                // PHASE-barrier resume: pResumed=1, pGroupResume=0
                WasmModuleBuilder.EmitI32Const(code, 1);
                WasmModuleBuilder.EmitLocalSet(code, pResumed);
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pGroupResume);
                code.Add(WasmOpCodes.End); // end if (yieldFlag == 2)
                code.Add(WasmOpCodes.End); // end if (resumeMode)
            }
            else
            {
                // === PURE-SPIN PROLOGUE === (v4.8.0 baseline path)
                // No resume support: dispatcher always runs to completion. resumeMode (param 15),
                // pResumed, pGroupResume locals are unused. Just initialize g=0.
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pG);
            }

            // block $exit_g
            code.Add(WasmOpCodes.Block);
            code.Add(WasmOpCodes.Void);
            // loop $loop_g
            code.Add(WasmOpCodes.Loop);
            code.Add(WasmOpCodes.Void);

            // br_if $exit_g (g >= numGroups)
            WasmModuleBuilder.EmitLocalGet(code, pG);
            WasmModuleBuilder.EmitLocalGet(code, 2); // numGroups
            code.Add(WasmOpCodes.I32GeU);
            code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(code, 1); // break to $exit_g

            if (enableYieldEscape)
            {
                // GROUP-RESUME SKIP: on a group-barrier resume (pGroupResume=1) this worker already
                // ran all phases of group g, did the group zeroing, and arrived at the group barrier
                // before it yielded — so skip the entire phase loop + zeroing and fall straight to the
                // group barrier (which restores its savedGen below). Fresh / phase-resume runs this block.
                WasmModuleBuilder.EmitLocalGet(code, pGroupResume);
                code.Add(WasmOpCodes.I32Eqz);
                code.Add(WasmOpCodes.If);
                code.Add(WasmOpCodes.Void);

                // phase init: if resumed, use saved phase; else 0
                // (after the first resumed iteration, pResumed is cleared so subsequent phases
                // use phase=0 as normal)
                WasmModuleBuilder.EmitLocalGet(code, pResumed);
                code.Add(WasmOpCodes.If);
                code.Add(WasmOpCodes.Void);
                // Resume: phase = load(yieldStateAddr + 8)
                WasmModuleBuilder.EmitLocalGet(code, 14); // yieldStateAddr
                code.Add(WasmOpCodes.I32Load);
                code.Add(0x02); code.Add(0x08); // align=2, offset=8 (savedPhase)
                WasmModuleBuilder.EmitLocalSet(code, pPhase);
                code.Add(WasmOpCodes.Else);
                // Fresh: phase = 0
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pPhase);
                code.Add(WasmOpCodes.End); // end if
            }
            else
            {
                // Pure-spin: no group-resume wrapper, no phase-resume branching. phase = 0.
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pPhase);
            }

            // block $exit_phase
            code.Add(WasmOpCodes.Block);
            code.Add(WasmOpCodes.Void);
            // loop $loop_phase
            code.Add(WasmOpCodes.Loop);
            code.Add(WasmOpCodes.Void);

            // === FRESH FLOW vs RESUMED FLOW ===
            // On a fresh dispatch (pResumed=0), run the tid loop + barrier setup + arrival++.
            // On a resume (pResumed=1), the tid loop + arrival++ already ran before the yield;
            // skip them. Just load savedGen from the yield buffer and synthesize arrived=0 so
            // the if (arrived == workerCount) check below routes us straight to the spin path.
            // This entire wrapper is closed below right after the arrival++ stores pArrived.
            if (enableYieldEscape)
            {
                WasmModuleBuilder.EmitLocalGet(code, pResumed);
                code.Add(WasmOpCodes.I32Eqz);
                code.Add(WasmOpCodes.If);
                code.Add(WasmOpCodes.Void);
            }
            // ---- FRESH FLOW (always executed when gate is off; when on, executed when pResumed == 0) ----

            // anyYielded = 0
            WasmModuleBuilder.EmitI32Const(code, 0);
            WasmModuleBuilder.EmitLocalSet(code, pAnyYielded);

            // tid = threadStart
            WasmModuleBuilder.EmitLocalGet(code, 0); // threadStart
            WasmModuleBuilder.EmitLocalSet(code, pTid);

            // block $exit_tid
            code.Add(WasmOpCodes.Block);
            code.Add(WasmOpCodes.Void);
            // loop $loop_tid
            code.Add(WasmOpCodes.Loop);
            code.Add(WasmOpCodes.Void);

            // br_if $exit_tid (tid >= threadEnd)
            WasmModuleBuilder.EmitLocalGet(code, pTid);
            WasmModuleBuilder.EmitLocalGet(code, 1); // threadEnd
            code.Add(WasmOpCodes.I32GeU);
            code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(code, 1); // break to $exit_tid

            // Push kernel args: globalIdx = g * groupSize + tid
            WasmModuleBuilder.EmitLocalGet(code, pG);
            WasmModuleBuilder.EmitLocalGet(code, 3); // groupSize
            code.Add(WasmOpCodes.I32Mul);
            WasmModuleBuilder.EmitLocalGet(code, pTid);
            code.Add(WasmOpCodes.I32Add);
            // gridDimX
            WasmModuleBuilder.EmitLocalGet(code, 4);
            // gridDimY
            WasmModuleBuilder.EmitLocalGet(code, 5);
            // myScratch = scratchBase + tid * scratchPerThread
            WasmModuleBuilder.EmitLocalGet(code, 6); // scratchBase
            WasmModuleBuilder.EmitLocalGet(code, pTid);
            WasmModuleBuilder.EmitLocalGet(code, 7); // scratchPerThread
            code.Add(WasmOpCodes.I32Mul);
            code.Add(WasmOpCodes.I32Add);
            // groupDimX = groupSize
            WasmModuleBuilder.EmitLocalGet(code, 3);
            // threadIdX = tid
            WasmModuleBuilder.EmitLocalGet(code, pTid);
            // sharedMemBase
            WasmModuleBuilder.EmitLocalGet(code, 8);
            // barrierBase
            WasmModuleBuilder.EmitLocalGet(code, 9);
            // dynamicSharedLen
            WasmModuleBuilder.EmitLocalGet(code, 10);
            // phase
            WasmModuleBuilder.EmitLocalGet(code, pPhase);
            // user args (pass through from dispatcher params)
            for (int i = 0; i < userParamCount; i++)
                WasmModuleBuilder.EmitLocalGet(code, (uint)(dispSystemParams + i));

            // call kernel
            code.Add(WasmOpCodes.Call);
            WasmModuleBuilder.EmitU32Leb128(code, (uint)kernelFuncIdx);
            WasmModuleBuilder.EmitLocalSet(code, pR);

            // if (r === 1) anyYielded = 1
            WasmModuleBuilder.EmitLocalGet(code, pR);
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.I32Eq);
            code.Add(WasmOpCodes.If);
            code.Add(WasmOpCodes.Void);
            WasmModuleBuilder.EmitI32Const(code, 1);
            WasmModuleBuilder.EmitLocalSet(code, pAnyYielded);
            code.Add(WasmOpCodes.End); // end if

            // tid++
            WasmModuleBuilder.EmitLocalGet(code, pTid);
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(code, pTid);
            code.Add(WasmOpCodes.Br);
            WasmModuleBuilder.EmitU32Leb128(code, 0); // continue $loop_tid

            code.Add(WasmOpCodes.End); // end loop $loop_tid
            code.Add(WasmOpCodes.End); // end block $exit_tid

            // Inter-worker phase barrier + global yield check.
            // For workerCount=1: simple check. For workerCount>1: Wasm atomic barrier.
            // fenceBase layout: [0]=arrival counter, [4]=generation, [8]=global yield count, [12]=exit flag

            // Fence: flush non-atomic shared memory writes from this phase
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.AtomicFence);
            code.Add(0x00);

            // Add this worker's yield count to global yield counter (atomic)
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase param
            WasmModuleBuilder.EmitLocalGet(code, pAnyYielded);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicRmwAdd);
            code.Add(0x02); code.Add(0x08); // align=2, offset=8 (global yield counter)
            code.Add(WasmOpCodes.Drop);

            // Save current generation
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicLoad);
            code.Add(0x02); code.Add(0x04); // align=2, offset=4 (generation)
            WasmModuleBuilder.EmitLocalSet(code, pSavedGen);

            // Atomically increment arrival counter
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicRmwAdd);
            code.Add(0x02); code.Add(0x00); // align=2, offset=0 (arrival counter)
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(code, pArrived);

            // ---- end FRESH FLOW ----
            if (enableYieldEscape)
            {
                code.Add(WasmOpCodes.Else);
                // ---- RESUMED FLOW (executed when pResumed == 1) ----
                // savedGen = load(yieldStateAddr + 12)
                WasmModuleBuilder.EmitLocalGet(code, 14); // yieldStateAddr
                code.Add(WasmOpCodes.I32Load);
                code.Add(0x02); code.Add(0x0C); // align=2, offset=12 (saved savedGen)
                WasmModuleBuilder.EmitLocalSet(code, pSavedGen);
                // arrived = 0 (force the else / spin path on the workerCount check below)
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pArrived);
                // ---- end RESUMED FLOW ----
                code.Add(WasmOpCodes.End); // end if (FRESH vs RESUMED)
            }

            // if (arrived == workerCount) — last worker
            WasmModuleBuilder.EmitLocalGet(code, pArrived);
            WasmModuleBuilder.EmitLocalGet(code, 12); // workerCount param
            code.Add(WasmOpCodes.I32Eq);
            code.Add(WasmOpCodes.If);
            code.Add(WasmOpCodes.Void);

            // Last worker: check global yield count
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicLoad);
            code.Add(0x02); code.Add(0x08); // offset=8 (global yield count)
            code.Add(WasmOpCodes.I32Eqz);
            // Store exit flag: 1 if no yields, 0 if yields remain
            WasmModuleBuilder.EmitLocalSet(code, pAnyYielded); // reuse as temp
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            WasmModuleBuilder.EmitLocalGet(code, pAnyYielded);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicStore);
            code.Add(0x02); code.Add(0x0C); // offset=12 (exit flag)

            // Per Data 2026-04-25: fence here so the exit-flag store is fully published
            // BEFORE any subsequent atomic ops or the gen bump. The existing pre-notify
            // fence at line 808 covers the resets; THIS fence covers the exit flag write
            // specifically, since waiters read it after wait32-wakeup at a different addr.
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.AtomicFence);
            code.Add(0x00);

            // Reset arrival counter and global yield count
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            WasmModuleBuilder.EmitI32Const(code, 0);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicStore);
            code.Add(0x02); code.Add(0x00); // offset=0 (arrival counter)
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            WasmModuleBuilder.EmitI32Const(code, 0);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicStore);
            code.Add(0x02); code.Add(0x08); // offset=8 (global yield count)

            // Fence before notify: ensure all writes visible to waking workers
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.AtomicFence);
            code.Add(0x00);

            // PURE SPIN PHASE BARRIER (v4.8.0 baseline). This is the barrier that
            // actually serializes phase-mode kernels like RadixSort (the in-kernel
            // EmitBarrier path is bypassed in phase mode). Wait/notify variants race
            // in the V8 wasm context — re-confirmed 2026-05-24 on current Chrome +
            // current backend: with WasmBackend.UseWaitNotifyBarriers ON, large sorts
            // fail with sort-order violations / duplicate values (1.4M: 1067, 500K:
            // 187) while small sorts pass. It's a V8 linear-memory wait/notify ordering
            // bug (chromium#490434403 family), reproduced even though this dispatcher
            // has only ~38 locals — so it is NOT the April "275-local spill" theory and
            // is NOT fixable in our codegen. Pure spin avoids the buggy futex path and
            // is correct. CPU cost is bounded: cross-worker wait window per phase is
            // <1ms typical. The wait/notify branch below stays behind the (default-off)
            // flag as a re-test harness. Full log: Plans/wasm-waitnotify-still-races-2026-05-24.md.
            //
            // Last worker: bump gen via atomic.store.
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.I32Add);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicStore);
            code.Add(0x02); code.Add(0x04); // offset=4 (generation)

            if (UseWaitNotifyBarriers)
            {
                // notify(fenceBase+4, int.MaxValue) — wake all phase-barrier sleepers.
                WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
                WasmModuleBuilder.EmitI32Const(code, int.MaxValue);
                code.Add(WasmOpCodes.AtomicPrefix);
                WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.MemoryAtomicNotify);
                code.Add(0x02); code.Add(0x04); // align=2, offset=4 (generation)
                code.Add(WasmOpCodes.Drop); // discard woken-count
            }

            if (enableYieldEscape)
            {
                // Variant C Step 2 (Trip 2026-05-27): JS-side notify shim. The wasm-side
                // memory.atomic.notify above is gated off by default (V8 race); ours uses
                // the env.notify import which calls Atomics.notify(view, addr>>2, count).
                // Spec-conformant Atomics.wait(Infinity) waiters require explicit notify -
                // see _wasm_fork/NOTES_ecma262_3800_syg.md. fenceBase+4 = phase gen address.
                // count = int.MaxValue (positive) - same value the wasm-side notify branch
                // uses. Spec also accepts negative-as-infinity, but several engines have
                // historically had bugs around the negative form; the positive max is the
                // safe choice. Drop the woken-count return - we don't need it.
                WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
                WasmModuleBuilder.EmitI32Const(code, 4);
                code.Add(WasmOpCodes.I32Add);             // fenceBase + 4 (phase gen byte addr)
                WasmModuleBuilder.EmitI32Const(code, int.MaxValue); // count = wake-all
                WasmModuleBuilder.EmitCall(code, (uint)notifyFuncIdx);
                code.Add(WasmOpCodes.Drop);
            }

            code.Add(WasmOpCodes.Else);

            if (UseWaitNotifyBarriers)
            {
                // === WAIT/NOTIFY phase waiter: sleep until gen advances ===
                // Block $exit { Loop $spin {
                //   if (load(gen) != savedGen) br $exit
                //   wait32(gen, savedGen, 1ms)   // self-heals if a notify is missed
                //   drop; br $spin } }
                // No yield-to-JS: wait32 OS-parks the worker, so spin-starvation can't occur.
                code.Add(WasmOpCodes.Block);
                code.Add(WasmOpCodes.Void);
                code.Add(WasmOpCodes.Loop);
                code.Add(WasmOpCodes.Void);
                // if (load(gen) != savedGen) br $exit (depth 1)
                WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
                code.Add(WasmOpCodes.AtomicPrefix);
                WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicLoad);
                code.Add(0x02); code.Add(0x04); // offset=4 (generation)
                WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
                code.Add(WasmOpCodes.I32Ne);
                code.Add(WasmOpCodes.BrIf);
                WasmModuleBuilder.EmitU32Leb128(code, 1); // → $exit
                // wait32(fenceBase+4, savedGen, 1_000_000 ns = 1ms)
                WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
                WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
                WasmModuleBuilder.EmitI64Const(code, 1_000_000);
                code.Add(WasmOpCodes.AtomicPrefix);
                WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.MemoryAtomicWait32);
                code.Add(0x02); code.Add(0x04); // align=2, offset=4
                code.Add(WasmOpCodes.Drop); // discard wait result
                code.Add(WasmOpCodes.Br);
                WasmModuleBuilder.EmitU32Leb128(code, 0); // → $spin (re-check)
                code.Add(WasmOpCodes.End); // end loop $spin
                code.Add(WasmOpCodes.End); // end block $exit
                code.Add(WasmOpCodes.End); // end if (arrived == workerCount)
            }
            else
            {

            // Other workers: spin-wait. Variant C path adds a yield-to-JS escape after
            // YIELD_SPIN_THRESHOLD iters when enableYieldEscape is on so the worker can park
            // via Atomics.wait(Infinity) and avoid spin-starvation under CPU oversub. The
            // last-arriving worker calls env.notify (after the gen-bump) to wake parked
            // waiters. Pure-spin path (gate off) just spins until gen advances - relies on
            // OS scheduler running every worker within the spin window (default config of
            // WorkerCount <= hwConcurrency-2 leaves headroom that makes this reliable).

            if (enableYieldEscape)
            {
                // spinCount = 0 (before entering spin block)
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pSpinCount);
            }

            code.Add(WasmOpCodes.Block); // $spin_exit
            code.Add(WasmOpCodes.Void);
            code.Add(WasmOpCodes.Loop); // $spin_loop
            code.Add(WasmOpCodes.Void);
            // curGen = atomic.load(gen); if (curGen != savedGen) break $spin_exit
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicLoad);
            code.Add(0x02); code.Add(0x04); // offset=4 (generation)
            WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
            code.Add(WasmOpCodes.I32Ne);
            code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(code, 1); // break (gen changed)

            if (enableYieldEscape)
            {
                // spinCount++
                WasmModuleBuilder.EmitLocalGet(code, pSpinCount);
                WasmModuleBuilder.EmitI32Const(code, 1);
                code.Add(WasmOpCodes.I32Add);
                WasmModuleBuilder.EmitLocalSet(code, pSpinCount);
                // if (spinCount > YIELD_SPIN_THRESHOLD) { save state + return }
                WasmModuleBuilder.EmitLocalGet(code, pSpinCount);
                WasmModuleBuilder.EmitI32Const(code, YIELD_SPIN_THRESHOLD);
                code.Add(WasmOpCodes.I32GtU);
                code.Add(WasmOpCodes.If);
                code.Add(WasmOpCodes.Void);
                // ---- YIELD: persist state to yieldStateAddr, then exit dispatcher ----
                // yieldStateAddr[0] = 1 (yieldFlag)
                WasmModuleBuilder.EmitLocalGet(code, 14); // yieldStateAddr
                WasmModuleBuilder.EmitI32Const(code, 1);
                WasmModuleBuilder.EmitStore(code, WasmOpCodes.I32Store, 2, 0);
                // yieldStateAddr[4] = g
                WasmModuleBuilder.EmitLocalGet(code, 14);
                WasmModuleBuilder.EmitLocalGet(code, pG);
                WasmModuleBuilder.EmitStore(code, WasmOpCodes.I32Store, 2, 4);
                // yieldStateAddr[8] = phase
                WasmModuleBuilder.EmitLocalGet(code, 14);
                WasmModuleBuilder.EmitLocalGet(code, pPhase);
                WasmModuleBuilder.EmitStore(code, WasmOpCodes.I32Store, 2, 8);
                // yieldStateAddr[12] = savedGen
                WasmModuleBuilder.EmitLocalGet(code, 14);
                WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
                WasmModuleBuilder.EmitStore(code, WasmOpCodes.I32Store, 2, 12);
                // EXIT THE FUNCTION (return) -- leaves yieldFlag=1 in the buffer for JS to see.
                code.Add(WasmOpCodes.Return);
                code.Add(WasmOpCodes.End); // end yield-if
            }

            // Continue spin
            code.Add(WasmOpCodes.Br);
            WasmModuleBuilder.EmitU32Leb128(code, 0); // continue $spin_loop
            code.Add(WasmOpCodes.End); // end loop $spin_loop
            code.Add(WasmOpCodes.End); // end block $spin_exit

            code.Add(WasmOpCodes.End); // end if (arrived == workerCount)
            } // end else (pure-spin phase waiter)

            if (enableYieldEscape)
            {
                // Past the barrier: clear pResumed so subsequent phase iterations of THIS dispatch
                // take the FRESH FLOW (need to do their own arrival++, gen-load, etc.).
                // Only the FIRST iteration after a yield-resume needs to skip that work.
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pResumed);
            }

            // Acquire fence: matches EmitBarrier (WasmKernelFunctionGenerator.cs:3924).
            // Without this, non-atomic kernel writes from the just-completed phase
            // are not guaranteed visible after the seq_cst load chain via gen.
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.AtomicFence);
            code.Add(0x00);

            // All workers: check exit flag
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicLoad);
            code.Add(0x02); code.Add(0x0C); // offset=12 (exit flag)
            code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(code, 1); // break to $exit_phase if exit=1

            // phase++
            WasmModuleBuilder.EmitLocalGet(code, pPhase);
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(code, pPhase);
            code.Add(WasmOpCodes.Br);
            WasmModuleBuilder.EmitU32Leb128(code, 0); // continue $loop_phase

            code.Add(WasmOpCodes.End); // end loop $loop_phase
            code.Add(WasmOpCodes.End); // end block $exit_phase

            // Zero shared memory AND barrier counters between groups.
            // Only first worker (threadStart == 0) zeroes to avoid races.
            // Other workers skip to the group barrier which ensures visibility.
            WasmModuleBuilder.EmitLocalGet(code, 0); // threadStart param
            WasmModuleBuilder.EmitI32Const(code, 0);
            code.Add(WasmOpCodes.I32Eq);
            code.Add(WasmOpCodes.If);
            code.Add(WasmOpCodes.Void);
            WasmModuleBuilder.EmitI32Const(code, 0);
            WasmModuleBuilder.EmitLocalSet(code, pZeroIdx);
            code.Add(WasmOpCodes.Block);
            code.Add(WasmOpCodes.Void);
            code.Add(WasmOpCodes.Loop);
            code.Add(WasmOpCodes.Void);
            // br_if exit (zeroIdx >= zeroRegionSize)
            WasmModuleBuilder.EmitLocalGet(code, pZeroIdx);
            WasmModuleBuilder.EmitLocalGet(code, 11); // zeroRegionSize param
            code.Add(WasmOpCodes.I32GeU);
            code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(code, 1); // break to exit block
            // i32.atomic.store(sharedMemBase + zeroIdx, 0) — atomic for multi-worker visibility
            WasmModuleBuilder.EmitLocalGet(code, 8); // sharedMemBase
            WasmModuleBuilder.EmitLocalGet(code, pZeroIdx);
            code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitI32Const(code, 0);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicStore);
            code.Add(0x02); code.Add(0x00); // align=2, offset=0
            // zeroIdx += 4
            WasmModuleBuilder.EmitLocalGet(code, pZeroIdx);
            WasmModuleBuilder.EmitI32Const(code, 4);
            code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(code, pZeroIdx);
            code.Add(WasmOpCodes.Br);
            WasmModuleBuilder.EmitU32Leb128(code, 0); // continue loop
            code.Add(WasmOpCodes.End); // end loop
            code.Add(WasmOpCodes.End); // end block
            code.Add(WasmOpCodes.End); // end if (threadStart == 0)

            if (enableYieldEscape)
            {
                code.Add(WasmOpCodes.End); // end if (pGroupResume == 0) — skip phase loop + zeroing on group-resume
            }

            // Inter-worker group barrier: all workers must finish current group
            // (including shared memory zeroing) before any starts the next group.
            // Uses fenceBase + 16 for the group barrier (separate from phase barrier at +0).
            // Variant C path: on a GROUP-barrier resume this worker already saved its group gen +
            // arrived (RmwAdd+16) before yielding — re-doing the arrival would double-count and
            // desync the barrier. So fresh/phase-resume → save gen + arrive; group-resume → restore
            // savedGen + force arrived=0 (waiter spin path). Pure-spin path: never yields here,
            // always fresh, always saves gen + arrives.
            if (enableYieldEscape)
            {
                WasmModuleBuilder.EmitLocalGet(code, pGroupResume);
                code.Add(WasmOpCodes.I32Eqz);
                code.Add(WasmOpCodes.If);
                code.Add(WasmOpCodes.Void);
            }
            // Save generation
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicLoad);
            code.Add(0x02); code.Add(0x14); // align=2, offset=20 (group gen at fenceBase+20)
            WasmModuleBuilder.EmitLocalSet(code, pSavedGen);
            // Arrive
            WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicRmwAdd);
            code.Add(0x02); code.Add(0x10); // offset=16 (group arrival at fenceBase+16)
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(code, pArrived);
            if (enableYieldEscape)
            {
                code.Add(WasmOpCodes.Else);
                // GROUP-barrier resume: restore savedGen (yieldStateAddr+12), force arrived=0 → waiter path.
                WasmModuleBuilder.EmitLocalGet(code, 14); // yieldStateAddr
                code.Add(WasmOpCodes.I32Load);
                code.Add(0x02); code.Add(0x0C); // align=2, offset=12 (saved group savedGen)
                WasmModuleBuilder.EmitLocalSet(code, pSavedGen);
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pArrived);
                code.Add(WasmOpCodes.End); // end if (pGroupResume == 0) — group arrival
            }
            // If last worker
            WasmModuleBuilder.EmitLocalGet(code, pArrived);
            WasmModuleBuilder.EmitLocalGet(code, 12); // workerCount
            code.Add(WasmOpCodes.I32Eq);
            code.Add(WasmOpCodes.If);
            code.Add(WasmOpCodes.Void);
            // PURE SPIN GROUP BARRIER (v4.8.0 baseline, matches phase barrier).
            // Last worker: reset arrival, reset exit flag for next group's phase loop, bump group gen.
            WasmModuleBuilder.EmitLocalGet(code, 13);
            WasmModuleBuilder.EmitI32Const(code, 0);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicStore);
            code.Add(0x02); code.Add(0x10); // offset=16
            WasmModuleBuilder.EmitLocalGet(code, 13);
            WasmModuleBuilder.EmitI32Const(code, 0);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicStore);
            code.Add(0x02); code.Add(0x0C); // offset=12 (exit flag)
            // Release fence before bumping the group generation: ensure ALL of the
            // last group's writes (non-atomic kernel data writes + shared-memory zeroing
            // + the exit-flag/arrival resets above) are visible to waking workers BEFORE
            // they observe the advanced group gen. This mirrors the phase-barrier producer's
            // fence at the gen store (see "Fence before notify" above, ~line 970). Without
            // it, V8's wasm linear-memory ordering (chromium#490434403 family) lets a waiter
            // see the bumped group gen via seq_cst load yet read stale group data — the source
            // of intermittent sort-order violations on large multi-group RadixSorts. The phase
            // barrier had this fence; the group barrier was missing it (asymmetry fixed 2026-05-25).
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.AtomicFence);
            code.Add(0x00);
            WasmModuleBuilder.EmitLocalGet(code, 13);
            WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.I32Add);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicStore);
            code.Add(0x02); code.Add(0x14); // offset=20

            if (UseWaitNotifyBarriers)
            {
                // notify(fenceBase+20, int.MaxValue) — wake all group-barrier sleepers.
                WasmModuleBuilder.EmitLocalGet(code, 13);
                WasmModuleBuilder.EmitI32Const(code, int.MaxValue);
                code.Add(WasmOpCodes.AtomicPrefix);
                WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.MemoryAtomicNotify);
                code.Add(0x02); code.Add(0x14); // align=2, offset=20 (group gen)
                code.Add(WasmOpCodes.Drop);
            }

            if (enableYieldEscape)
            {
                // Variant C Step 2 (Trip 2026-05-27): JS-side notify for group-gen waiters.
                // Same rationale as the phase-gen notify above. fenceBase+20 = group gen
                // address; int.MaxValue = wake all.
                WasmModuleBuilder.EmitLocalGet(code, 13); // fenceBase
                WasmModuleBuilder.EmitI32Const(code, 20);
                code.Add(WasmOpCodes.I32Add);             // fenceBase + 20 (group gen byte addr)
                WasmModuleBuilder.EmitI32Const(code, int.MaxValue); // count = wake-all
                WasmModuleBuilder.EmitCall(code, (uint)notifyFuncIdx);
                code.Add(WasmOpCodes.Drop);
            }

            code.Add(WasmOpCodes.Else);
            if (UseWaitNotifyBarriers)
            {
                // === WAIT/NOTIFY group waiter: sleep until group gen advances ===
                code.Add(WasmOpCodes.Block);
                code.Add(WasmOpCodes.Void);
                code.Add(WasmOpCodes.Loop);
                code.Add(WasmOpCodes.Void);
                WasmModuleBuilder.EmitLocalGet(code, 13);
                code.Add(WasmOpCodes.AtomicPrefix);
                WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicLoad);
                code.Add(0x02); code.Add(0x14); // offset=20
                WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
                code.Add(WasmOpCodes.I32Ne);
                code.Add(WasmOpCodes.BrIf);
                WasmModuleBuilder.EmitU32Leb128(code, 1); // → $exit (gen changed)
                // wait32(fenceBase+20, savedGen, 1ms)
                WasmModuleBuilder.EmitLocalGet(code, 13);
                WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
                WasmModuleBuilder.EmitI64Const(code, 1_000_000);
                code.Add(WasmOpCodes.AtomicPrefix);
                WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.MemoryAtomicWait32);
                code.Add(0x02); code.Add(0x14); // align=2, offset=20
                code.Add(WasmOpCodes.Drop);
                code.Add(WasmOpCodes.Br);
                WasmModuleBuilder.EmitU32Leb128(code, 0); // → $spin (re-check)
                code.Add(WasmOpCodes.End); // end loop
                code.Add(WasmOpCodes.End); // end block
                code.Add(WasmOpCodes.End); // end if (group barrier)
            }
            else
            {
            // Other workers: spin-wait for group generation to advance. Variant C path adds the
            // yield-to-JS-after-threshold escape (mirrors phase waiter). Pure-spin path just spins.
            if (enableYieldEscape)
            {
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pSpinCount);
            }
            code.Add(WasmOpCodes.Block);
            code.Add(WasmOpCodes.Void);
            code.Add(WasmOpCodes.Loop);
            code.Add(WasmOpCodes.Void);
            WasmModuleBuilder.EmitLocalGet(code, 13);
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.I32AtomicLoad);
            code.Add(0x02); code.Add(0x14); // offset=20
            WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
            code.Add(WasmOpCodes.I32Ne);
            code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(code, 1); // break (gen changed)
            if (enableYieldEscape)
            {
                // spinCount++
                WasmModuleBuilder.EmitLocalGet(code, pSpinCount);
                WasmModuleBuilder.EmitI32Const(code, 1);
                code.Add(WasmOpCodes.I32Add);
                WasmModuleBuilder.EmitLocalSet(code, pSpinCount);
                // if (spinCount > YIELD_SPIN_THRESHOLD) { save group state (yieldFlag=2) + return }
                WasmModuleBuilder.EmitLocalGet(code, pSpinCount);
                WasmModuleBuilder.EmitI32Const(code, YIELD_SPIN_THRESHOLD);
                code.Add(WasmOpCodes.I32GtU);
                code.Add(WasmOpCodes.If);
                code.Add(WasmOpCodes.Void);
                // yieldStateAddr[0] = 2 (GROUP-barrier yieldFlag — distinct from the phase barrier's 1)
                WasmModuleBuilder.EmitLocalGet(code, 14);
                WasmModuleBuilder.EmitI32Const(code, 2);
                WasmModuleBuilder.EmitStore(code, WasmOpCodes.I32Store, 2, 0);
                // yieldStateAddr[4] = g
                WasmModuleBuilder.EmitLocalGet(code, 14);
                WasmModuleBuilder.EmitLocalGet(code, pG);
                WasmModuleBuilder.EmitStore(code, WasmOpCodes.I32Store, 2, 4);
                // yieldStateAddr[12] = savedGen (the group gen this waiter is blocked on)
                WasmModuleBuilder.EmitLocalGet(code, 14);
                WasmModuleBuilder.EmitLocalGet(code, pSavedGen);
                WasmModuleBuilder.EmitStore(code, WasmOpCodes.I32Store, 2, 12);
                code.Add(WasmOpCodes.Return);
                code.Add(WasmOpCodes.End); // end yield-if
            }
            code.Add(WasmOpCodes.Br);
            WasmModuleBuilder.EmitU32Leb128(code, 0); // continue spin
            code.Add(WasmOpCodes.End); // end loop
            code.Add(WasmOpCodes.End); // end block
            code.Add(WasmOpCodes.End); // end if (group barrier)
            } // end else (pure-spin group waiter)

            // After group barrier: ALL workers fence + reset phase state for next group.
            // atomic.fence ensures visibility of the last worker's exit flag reset.
            code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(code, WasmOpCodes.AtomicFence);
            code.Add(0x00); // fence ordering byte

            if (enableYieldEscape)
            {
                // Past the group barrier: clear pGroupResume so the NEXT group iteration takes the
                // fresh flow (runs its phase loop + arrives normally). Only the FIRST group after a
                // group-barrier resume skips that work.
                WasmModuleBuilder.EmitI32Const(code, 0);
                WasmModuleBuilder.EmitLocalSet(code, pGroupResume);
            }

            // g++
            WasmModuleBuilder.EmitLocalGet(code, pG);
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(code, pG);
            code.Add(WasmOpCodes.Br);
            WasmModuleBuilder.EmitU32Leb128(code, 0); // continue $loop_g

            code.Add(WasmOpCodes.End); // end loop $loop_g
            code.Add(WasmOpCodes.End); // end block $exit_g

            // Normal-exit path: clear yieldFlag in the per-worker yield buffer so JS sees
            // "dispatcher completed all work, no re-dispatch needed". The yield-mid-spin paths
            // (phase + group barrier) RETURN before reaching here, so they leave yieldFlag=1
            // (phase) or 2 (group) for JS to see and re-dispatch. ALWAYS emitted (even when
            // gate is off and dispatcher never sets yieldFlag): defends against cross-dispatch
            // state leaks if the yieldStateAddr region is reused without explicit zeroing.
            WasmModuleBuilder.EmitLocalGet(code, 14); // yieldStateAddr
            WasmModuleBuilder.EmitI32Const(code, 0);
            WasmModuleBuilder.EmitStore(code, WasmOpCodes.I32Store, 2, 0);

            moduleBuilder.SetFunctionBody(definedFuncIndex, locals, code.ToArray());

            if (VerboseLogging) Log($"[Wasm-Dispatcher] Added phase dispatcher: funcIdx={dispFuncIdx}, params={dispParamTypes.Count} (system={dispSystemParams}, user={userParamCount}), code={code.Count}b");
        }

        #endregion
    }

    /// <summary>
    /// Intrinsic handler delegate type for the Wasm backend.
    /// </summary>
    public delegate void WasmIntrinsicHandler(
        WasmBackend backend,
        WasmCodeGenerator codeGenerator,
        Value value);

    /// <summary>
    /// Backend options for Wasm.
    /// </summary>
    public class WasmBackendOptions
    {
        /// <summary>
        /// Number of Web Workers to use for parallel dispatch.
        /// Defaults to <c>Math.Max(2, navigator.hardwareConcurrency - 2)</c>,
        /// leaving 2 hardware threads free for the browser UI, Mono runtime,
        /// and OS. The pure-spin phase barrier needs the OS scheduler to run
        /// every worker within the spin window; over-saturating the CPU
        /// (e.g. equal-to-hardwareConcurrency workers + multi-tab oversub)
        /// can cause one worker to be descheduled long enough that other
        /// workers spin past the YIELD_SPIN_THRESHOLD and yield to JS, then
        /// re-dispatch and spin again, losing throughput. Leaving headroom
        /// keeps the descheduling window short.
        /// </summary>
        public int WorkerCount { get; set; } = Math.Max(2, WasmILGPUDevice.GetHardwareConcurrency() - 2);

        /// <summary>
        /// Variant C contention-safe barrier path (Trip 2026-05-27). When <c>true</c>, the
        /// phase dispatcher emits the full spin + yield-to-JS + producer-side
        /// <c>env.notify</c> path so a stalled worker parks in JS via
        /// <c>Atomics.wait(Infinity)</c> until the last-arriving worker wakes it on the
        /// gen-bump. When <c>false</c>, the dispatcher emits pure spin (no yield, no
        /// notify, no resume-mode handling) - byte-identical to the v4.8.0 baseline hot
        /// path.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Default (left <c>null</c>): <see cref="WasmAccelerator.Create(Context, WasmBackendOptions)"/>
        /// resolves to <c>true</c>. Variant C is the correct default because external CPU
        /// contention (other tabs, games like Fallout 76, parallel test runs) can stall
        /// any worker regardless of <c>WorkerCount</c>, and pure spin livelocks when that
        /// happens (Data 2026-04-28: pure spin "0 iters in 30 min" at 2-tab oversub).
        /// Variant C survives external contention by parking stalled workers in JS instead
        /// of burning CPU. Healthy-machine overhead is small (~500ns per barrier for the
        /// producer's notify call when no waiters are parked).
        /// </para>
        /// <para>
        /// Set explicit <c>false</c> only when (a) you know the machine is healthy AND
        /// nothing else is competing for CPU AND (b) you want byte-equivalent v4.8.0
        /// pure-spin for micro-benchmarks where every nanosecond counts. Set explicit
        /// <c>true</c> to make the choice unambiguous in test fixtures.
        /// </para>
        /// </remarks>
        public bool? EnableYieldEscape { get; set; } = null;

        /// <summary>
        /// Maximum SharedArrayBuffer-backed <c>WebAssembly.Memory</c> size in 64 KiB pages.
        /// Default <c>16384</c> (1 GiB), the conservative ceiling that fits within Chrome's
        /// per-renderer SharedArrayBuffer reservation budget without contending with other
        /// tabs. Browsers support up to <c>65536</c> pages (4 GiB) for shared memory; raise
        /// only when the consumer's working set genuinely exceeds 1 GiB and the host has
        /// the headroom (large-model ML inference is the canonical case — see
        /// <c>SpawnDev.ILGPU.ML</c>'s DA3-Small graph executor). Note: the cached memory's
        /// <c>maximum</c> is fixed at instantiation; raising this option after the
        /// accelerator has already allocated and reused the cached memory has no effect.
        /// </summary>
        public int MaxLinearMemoryPages { get; set; } = 16384;
    }

    /// <summary>
    /// Argument mapper for Wasm kernel parameters.
    /// </summary>
    public class WasmArgumentMapper : ArgumentMapper
    {
        public WasmArgumentMapper(Context context) : base(context) { }

        protected override Type MapViewType(Type viewType, Type elementType)
        {
            return viewType;
        }

        protected override void MapViewInstance<TILEmitter, TSource, TTarget>(
            in TILEmitter emitter,
            Type viewType,
            in TSource source,
            in TTarget target)
        {
            // View mapping handled separately for Wasm
        }
    }


    /// <summary>
    /// Capability context for Wasm backend.
    /// </summary>
    public class WasmCapabilityContext : CapabilityContext
    {
        public WasmCapabilityContext() : base()
        {
            // Half (Float16) is emulated via f32 promotion in the Wasm codegen.
            // BasicValueType.Float16 maps to WasmOpCodes.F32 with 2-byte element size.
            Float16 = true;
        }
    }
}
