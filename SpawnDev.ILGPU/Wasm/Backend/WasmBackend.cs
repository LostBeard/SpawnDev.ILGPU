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
                GeneratePhaseDispatcher(moduleBuilder, funcIdx, paramTypes, definedFuncIndex);
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

            // Locals: g, phase, tid, anyYielded, r, zeroIdx, savedGen, arrived, spinCount, resumed (10 i32)
            var locals = new List<WasmLocal>
            {
                new WasmLocal { Type = WasmOpCodes.I32, Count = 10 }
            };
            uint pG = (uint)dispParamTypes.Count;         // local index for g
            uint pPhase = pG + 1;
            uint pTid = pG + 2;
            uint pAnyYielded = pG + 3;
            uint pR = pG + 4;
            uint pZeroIdx = pG + 5;
            uint pSavedGen = pG + 6;
            uint pArrived = pG + 7;
            uint pSpinCount = pG + 8;     // counter for phase barrier spin iterations
            uint pResumed = pG + 9;       // 1 if dispatcher was re-entered after a spin-yield, 0 otherwise

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
            //   offset 0: yieldFlag  (i32) — 1 if dispatcher returned mid-spin, 0 if normal exit
            //   offset 4: savedG     (i32) — group index at yield
            //   offset 8: savedPhase (i32) — phase index at yield
            //   offset 12: savedGen  (i32) — generation value the spin loop was waiting on

            var code = new List<byte>();

            // === SPIN-YIELD PROLOGUE ===
            // If resumeMode != 0, we were re-dispatched after yielding mid-phase-barrier-spin.
            // Restore (g, phase, savedGen) from yieldStateAddr; set pResumed=1 so the phase
            // loop body knows to skip the tid loop + arrival++ (already done before yield)
            // and jump straight to the spin loop with the saved savedGen.
            // If resumeMode == 0, fresh dispatch: g=0, pResumed=0.
            WasmModuleBuilder.EmitLocalGet(code, 15); // resumeMode
            code.Add(WasmOpCodes.I32Eqz);
            code.Add(WasmOpCodes.If);
            code.Add(WasmOpCodes.Void);
            // Fresh start: g = 0, resumed = 0
            WasmModuleBuilder.EmitI32Const(code, 0);
            WasmModuleBuilder.EmitLocalSet(code, pG);
            WasmModuleBuilder.EmitI32Const(code, 0);
            WasmModuleBuilder.EmitLocalSet(code, pResumed);
            code.Add(WasmOpCodes.Else);
            // Resume: g = load(yieldStateAddr + 4), resumed = 1
            // (phase + savedGen are loaded inside the loop_g body so they apply to the right iteration)
            WasmModuleBuilder.EmitLocalGet(code, 14); // yieldStateAddr
            code.Add(WasmOpCodes.I32Load);
            code.Add(0x02); code.Add(0x04); // align=2, offset=4 (savedG)
            WasmModuleBuilder.EmitLocalSet(code, pG);
            WasmModuleBuilder.EmitI32Const(code, 1);
            WasmModuleBuilder.EmitLocalSet(code, pResumed);
            code.Add(WasmOpCodes.End); // end if

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
            WasmModuleBuilder.EmitLocalGet(code, pResumed);
            code.Add(WasmOpCodes.I32Eqz);
            code.Add(WasmOpCodes.If);
            code.Add(WasmOpCodes.Void);
            // ---- FRESH FLOW (executed when pResumed == 0) ----

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

            // Other workers: spin-wait with yield-to-JS after threshold.
            // Pure spin (atomic.load only) avoids V8's broken wasm wait/notify path entirely
            // (V8 14.7 FutexEmulation race - see Wasm/Notes/wait-notify-race-investigation-2026-04-26.md).
            // The yield-to-JS after THRESHOLD spin iterations prevents OS scheduler starvation
            // when host is CPU-oversubscribed: under simultaneous-start 2-tab oversub, pure spin
            // alone starved indefinitely (Data 2026-04-28: 0 iters in 30 min). With yield, workers
            // periodically save state + return; JS re-dispatches them after a microtask boundary,
            // giving the OS a chance to schedule the descheduled last-arriver.

            // spinCount = 0 (before entering spin block)
            WasmModuleBuilder.EmitI32Const(code, 0);
            WasmModuleBuilder.EmitLocalSet(code, pSpinCount);

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

            // spinCount++
            WasmModuleBuilder.EmitLocalGet(code, pSpinCount);
            WasmModuleBuilder.EmitI32Const(code, 1);
            code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(code, pSpinCount);
            // if (spinCount > YIELD_SPIN_THRESHOLD) { save state + br $exit_g }
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
            // EXIT THE FUNCTION (return) -- this leaves yieldFlag=1 in the buffer for JS to see.
            // Cannot use `br $exit_g` here: that would fall through to the yieldFlag=0 store
            // emitted right after end of $exit_g (which is the normal-exit clear), wiping out
            // our yieldFlag=1 and causing JS to think the dispatcher completed normally.
            code.Add(WasmOpCodes.Return);
            code.Add(WasmOpCodes.End); // end yield-if

            // Continue spin
            code.Add(WasmOpCodes.Br);
            WasmModuleBuilder.EmitU32Leb128(code, 0); // continue $spin_loop
            code.Add(WasmOpCodes.End); // end loop $spin_loop
            code.Add(WasmOpCodes.End); // end block $spin_exit

            code.Add(WasmOpCodes.End); // end if (arrived == workerCount)
            } // end else (pure-spin phase waiter)

            // Past the barrier: clear pResumed so subsequent phase iterations of THIS dispatch
            // take the FRESH FLOW (need to do their own arrival++, gen-load, etc.).
            // Only the FIRST iteration after a yield-resume needs to skip that work.
            WasmModuleBuilder.EmitI32Const(code, 0);
            WasmModuleBuilder.EmitLocalSet(code, pResumed);

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

            // Inter-worker group barrier: all workers must finish current group
            // (including shared memory zeroing) before any starts the next group.
            // Uses fenceBase + 16 for the group barrier (separate from phase barrier at +0).
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
            // Other workers: pure spin-wait for group generation to advance.
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
            // "dispatcher completed all work, no re-dispatch needed". The yield-mid-spin
            // path branches directly to $exit_g WITHOUT going through here, so it leaves
            // yieldFlag=1 (the value it stored before the br).
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
