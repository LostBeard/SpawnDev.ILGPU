// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: WebGPUBackend.cs
//
// WebGPU/WGSL backend for ILGPU using CodeGeneratorBackend pattern.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Backends.EntryPoints;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Intrinsics;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using System.Reflection;
using System.Text;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// Diagnostic categories for WebGPU backend logging.
    /// Use with <see cref="WebGPUBackend.DiagnosticFlags"/> to enable per-category output.
    /// </summary>
    [Flags]
    public enum WGSLDiagnostics
    {
        None = 0,
        SharedMemory = 1 << 0,
        Uniformity = 1 << 1,
        Inlining = 1 << 2,
        Bindings = 1 << 3,
        Dispatch = 1 << 4,
        Compilation = 1 << 5,
        All = SharedMemory | Uniformity | Inlining | Bindings | Dispatch | Compilation,
    }

    /// <summary>
    /// A compiled WGSL shader entry in the registry, keyed by kernel name.
    /// Provides structural metadata derived from the generated WGSL source.
    /// </summary>
    public record WGSLEntry(
        string KernelName,
        string Source,
        int WorkgroupSize,
        int SharedMemoryBytes,
        int BindingCount,
        int LineCount,
        int EmulationFunctionCount,
        string[] SharedMemoryVars,
        bool UsesBarriers,
        bool UsesAtomics,
        bool UsesSubgroups,
        bool UniformityTransformApplied,
        bool UsesI64Emulation,
        bool UsesF64Emulation,
        DateTime Timestamp);

    /// <summary>
    /// WebGPU/WGSL backend for ILGPU.
    /// Compiles ILGPU IR to WGSL shader code using the CodeGeneratorBackend pattern.
    /// </summary>
    public class WebGPUBackend : CodeGeneratorBackend<
        WGSLIntrinsic.Handler,
        WGSLCodeGenerator.GeneratorArgs,
        WGSLCodeGenerator,
        StringBuilder>
    {

        /// <summary>
        /// The backend type constant for WebGPU.
        /// </summary>
        public const BackendType BackendTypeWebGPU = BackendType.WebGPU; // Ensure this matches ILGPU's BackendType enum for WebGPU (if defined)

        #region Pre-compiled Regex Patterns

        // @workgroup_size(x[,y[,z]]) extraction
        private static readonly System.Text.RegularExpressions.Regex s_workgroupSizePattern =
            new(@"@workgroup_size\((\d+)(?:\s*,\s*(\d+))?(?:\s*,\s*(\d+))?\)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // @workgroup_size(x) — first dimension only
        private static readonly System.Text.RegularExpressions.Regex s_workgroupSizeSimplePattern =
            new(@"@workgroup_size\((\d+)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        // var<workgroup> shared memory declarations
        private static readonly System.Text.RegularExpressions.Regex s_sharedMemoryPattern =
            new(@"var<workgroup>\s+\w+\s*:\s*array<\w+,\s*(\d+)>",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        #endregion

        /// <summary>
        /// Controls whether verbose logging is enabled. Set to true to enable console output.
        /// Setting this to true also sets <see cref="DiagnosticFlags"/> to <see cref="WGSLDiagnostics.All"/>.
        /// </summary>
        public static bool VerboseLogging
        {
            get => DiagnosticFlags != WGSLDiagnostics.None;
            set => DiagnosticFlags = value ? WGSLDiagnostics.All : WGSLDiagnostics.None;
        }

        /// <summary>
        /// Per-category diagnostic flags. Set individual flags to enable focused logging.
        /// </summary>
        public static WGSLDiagnostics DiagnosticFlags { get; set; } = WGSLDiagnostics.None;

        /// <summary>
        /// Stores the last generated WGSL source for debugging.
        /// </summary>
        public static string? LastGeneratedWGSL { get; set; }

        /// <summary>
        /// Named registry of all compiled WGSL shaders, keyed by kernel name.
        /// Replaces the old flat list for instant lookup.
        /// </summary>
        public static Dictionary<string, WGSLEntry> WGSLRegistry { get; } = new();

        /// <summary>
        /// Flat list of all generated WGSL sources (backward-compatible with demo UI).
        /// </summary>
        public static List<string> AllGeneratedWGSL { get; set; } = new List<string>();

        /// <summary>
        /// When true, emits IR StringValue nodes as WGSL comments (e.g. "// String: index out of range").
        /// Default false — these comments appear ~42 times per kernel and add noise.
        /// </summary>
        public static bool EmitDebugStrings { get; set; } = false;

        /// <summary>
        /// Enables shader pipeline caching. Disable for debugging shader compilation issues.
        /// When enabled, compiled shaders are cached and reused across kernel invocations.
        /// </summary>
        public static bool EnableShaderCaching { get; set; } = true;

        /// <summary>
        /// Enables reflection metadata caching for parameter types.
        /// When enabled, PropertyInfo/FieldInfo lookups are cached to avoid per-call reflection.
        /// </summary>
        public static bool EnableReflectionCaching { get; set; } = true;

        /// <summary>
        /// Enables scalar buffer pooling.
        /// WARNING: Disabled by default because WebGPU Queue.Submit is asynchronous.
        /// Pooled buffers may be reused before the GPU finishes reading from them.
        /// Enable only if you implement explicit synchronization.
        /// </summary>
        public static bool EnableBufferPooling { get; set; } = false;

        /// <summary>
        /// When true, kernel dispatches with an identical (pipeline + data-buffer bindings)
        /// signature reuse a cached <c>GPUBindGroup</c> instead of recreating and disposing one
        /// every dispatch. The cache entry owns a stable scalar/lock buffer set whose 256-byte
        /// contents are rewritten on each hit, so correctness is unaffected - a bind group binds
        /// buffers, not their contents. Default OFF.
        ///
        /// Opt in for fixed-shape workloads (e.g. ML fixed-shape decode), where the same kernels
        /// re-dispatch over the same buffers every step: the hit rate approaches 100% after the
        /// first step and eliminates the per-dispatch <c>createBindGroup</c> + <c>destroy</c>
        /// churn that dominates the per-step GPU floor. Safe reuse assumes the consumer
        /// <c>Synchronize</c>s between successive dispatches of the same signature (the fixed-shape
        /// decode loop syncs every step), so an owned scalar buffer is never rewritten while a
        /// prior dispatch still reads it. Call <see cref="WebGPUAccelerator.ClearBindGroupCache"/>
        /// when the backing buffer set changes (e.g. generation teardown) to release cached groups
        /// and their owned buffers.
        /// </summary>
        public static bool EnableBindGroupCaching { get; set; } = false;

        /// <summary>
        /// Diagnostic (default OFF). When true, the WebGPU backend accumulates the wall-time spent in
        /// BOTH GPU-wait surfaces, so a profiled step accounts for ALL GPU-wait rather than just one slice:
        /// <list type="bullet">
        /// <item><description><see cref="WebGPUAcceleratorExtensions.SynchronizeAsync"/>'s
        /// <c>queue.OnSubmittedWorkDone()</c> drain → <see cref="ProfileSyncWaitMs"/> / <see cref="ProfileSyncWaitCount"/>.</description></item>
        /// <item><description>The GPU→CPU readback <c>staging.MapAsync(Read)</c> wait inside
        /// <see cref="WebGPUBuffer{T}"/>'s <c>CopyToHostAsync</c> family → <see cref="ProfileReadbackWaitMs"/> / <see cref="ProfileReadbackWaitCount"/>.</description></item>
        /// </list>
        /// These two surfaces are distinct: when a decode's GPU-wait hides in logits/shape readbacks
        /// (the <c>mapAsync</c> is queued behind prior work) it lands in <see cref="ProfileReadbackWaitMs"/> and
        /// reads ~0 in <see cref="ProfileSyncWaitMs"/>. Capturing both lets a consumer split a step's wall-time
        /// into total-GPU-wait vs .NET-side build/submit and have the slices SUM TO THE MEASURED TOTAL — naming
        /// a bottleneck from only one surface is the partial-profile trap. Reset with
        /// <see cref="ResetDispatchProfiling"/> before the step you want to measure.
        /// </summary>
        public static bool EnableDispatchProfiling { get; set; } = false;

        /// <summary>Cumulative ms spent in <c>queue.OnSubmittedWorkDone()</c> (the sync-drain GPU-wait surface) while <see cref="EnableDispatchProfiling"/> is on.</summary>
        public static double ProfileSyncWaitMs;

        /// <summary>Number of <c>OnSubmittedWorkDone()</c> GPU drains while <see cref="EnableDispatchProfiling"/> is on.</summary>
        public static long ProfileSyncWaitCount;

        /// <summary>Cumulative ms spent in the GPU→CPU readback <c>staging.MapAsync(Read)</c> wait (the OTHER GPU-wait
        /// surface, distinct from <see cref="ProfileSyncWaitMs"/>) while <see cref="EnableDispatchProfiling"/> is on.</summary>
        public static double ProfileReadbackWaitMs;

        /// <summary>Number of GPU→CPU readback <c>MapAsync(Read)</c> waits while <see cref="EnableDispatchProfiling"/> is on.</summary>
        public static long ProfileReadbackWaitCount;

        // ── CPU-prologue phase timers (the per-dispatch .NET build/submit work in RunKernel) ──
        // The GPU-wait surfaces above measure where the GPU blocks; these measure where the CPU
        // spends time BUILDING each dispatch. Their sum across a step is the directly-measured CPU
        // prologue cost — the slice that shows up as the (forward − GPU-wait − readback) RESIDUAL in
        // a consumer's step timing. Splitting it into phases turns that residual into measured slices
        // and points at which phase to optimize (e.g. fixed-shape decode re-runs a byte-identical
        // prologue every step). All accumulate only while <see cref="EnableDispatchProfiling"/> is on.

        /// <summary>Cumulative ms in the per-dispatch SHADER-RESOLVE phase (override-constant build,
        /// runtime <c>@workgroup_size</c> regex patch, and <c>GetOrCreateComputeShader</c> lookup) — O(WGSL length),
        /// constant for a fixed (kernel + config). While <see cref="EnableDispatchProfiling"/> is on.</summary>
        public static double ProfileCpuShaderResolveMs;

        /// <summary>Cumulative ms in the per-dispatch ARG-PREP phase (arg flatten/expand, per-view reflection,
        /// scalar packing, bind-group ENTRY build, and storage-aliasing check) — O(args/bindings), but NOT the
        /// <c>CreateBindGroup</c> call itself (that is <see cref="ProfileCpuBindGroupMs"/>). While <see cref="EnableDispatchProfiling"/> is on.</summary>
        public static double ProfileCpuArgBuildMs;

        /// <summary>Cumulative ms in the per-dispatch BIND-GROUP-RESOLVE phase (the cache-hit lookup or
        /// <c>device.CreateBindGroup</c> + workgroup-count math) — splits the JS-interop bind-group cost out of
        /// <see cref="ProfileCpuArgBuildMs"/> so the CPU arg-processing vs the GPU object creation are separated.
        /// While <see cref="EnableDispatchProfiling"/> is on.</summary>
        public static double ProfileCpuBindGroupMs;

        /// <summary>Cumulative ms in the per-dispatch ENCODE phase (compute-pass encode + dispatch-record bookkeeping).
        /// While <see cref="EnableDispatchProfiling"/> is on.</summary>
        public static double ProfileCpuEncodeMs;

        /// <summary>Number of dispatches that recorded CPU-prologue phase timings while <see cref="EnableDispatchProfiling"/> is on.</summary>
        public static long ProfileCpuDispatchCount;

        /// <summary>Resets ALL dispatch-profiling counters — the sync-drain (<see cref="ProfileSyncWaitMs"/>,
        /// <see cref="ProfileSyncWaitCount"/>) and readback (<see cref="ProfileReadbackWaitMs"/>,
        /// <see cref="ProfileReadbackWaitCount"/>) GPU-wait surfaces AND the CPU-prologue phase timers
        /// (<see cref="ProfileCpuShaderResolveMs"/>, <see cref="ProfileCpuArgBuildMs"/>,
        /// <see cref="ProfileCpuEncodeMs"/>, <see cref="ProfileCpuDispatchCount"/>).</summary>
        public static void ResetDispatchProfiling()
        {
            ProfileSyncWaitMs = 0;
            ProfileSyncWaitCount = 0;
            ProfileReadbackWaitMs = 0;
            ProfileReadbackWaitCount = 0;
            ProfileCpuShaderResolveMs = 0;
            ProfileCpuArgBuildMs = 0;
            ProfileCpuBindGroupMs = 0;
            ProfileCpuEncodeMs = 0;
            ProfileCpuDispatchCount = 0;
        }

        /// <summary>
        /// When set, every compiled shader is auto-written to {WGSLDumpPath}/{KernelName}.wgsl.
        /// Desktop only — ignored in Blazor WASM (use console.log instead).
        /// </summary>
        public static string? WGSLDumpPath { get; set; }

        /// <summary>
        /// Callback invoked whenever a WGSL shader is compiled.
        /// Parameters: (kernelName, wgslSource, entry).
        /// Set by ShaderDebugService to auto-dump shaders to a user-selected folder.
        /// </summary>
        public static Action<string, string, WGSLEntry>? OnShaderCompiled { get; set; }

        /// <summary>
        /// When true, runs structural validation on generated WGSL before caching.
        /// Catches codegen bugs early (missing entry point, undeclared shared memory,
        /// undefined emulation functions, workgroup_size inconsistencies).
        /// Default true — validation is cheap relative to GPU shader compilation.
        /// </summary>
        public static bool EnableWGSLValidation { get; set; } = true;

        /// <summary>
        /// Writes a message to the console. Caller MUST check <see cref="VerboseLogging"/>
        /// or <see cref="DiagnosticFlags"/> BEFORE constructing the message string
        /// to avoid allocating interpolated strings when logging is disabled.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static void Log(string message) => Console.WriteLine(message);

        // Pre-compiled patterns for WGSL validation
        private static readonly System.Text.RegularExpressions.Regex s_fnNamePattern =
            new(@"fn\s+(\w+)\s*\(",
                System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex s_sharedVarDeclPattern =
            new(@"var<workgroup>\s+(\w+)\s*:",
                System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex s_sharedVarRefPattern =
            new(@"(?<!var<workgroup>\s+)(shared_\d+)\s*\[",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Validates generated WGSL for structural correctness. Catches codegen bugs
        /// that would otherwise surface as cryptic GPU errors or silent failures.
        /// </summary>
        internal static void ValidateWGSL(string wgslSource, string kernelName)
        {
            if (!EnableWGSLValidation) return;

            var errors = new List<string>();

            // 1. Entry point must exist
            if (!wgslSource.Contains("fn main("))
                errors.Add("Missing entry point: 'fn main(' not found");

            // 2. @workgroup_size must exist
            if (!wgslSource.Contains("@workgroup_size("))
                errors.Add("Missing @workgroup_size attribute");

            // 3. Shared memory vars referenced but not declared
            var declaredShared = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match m in s_sharedVarDeclPattern.Matches(wgslSource))
                declaredShared.Add(m.Groups[1].Value);
            foreach (System.Text.RegularExpressions.Match m in s_sharedVarRefPattern.Matches(wgslSource))
            {
                var refName = m.Groups[1].Value;
                if (!declaredShared.Contains(refName))
                    errors.Add($"Shared memory '{refName}' referenced but not declared");
            }

            // 4. Emulation functions called but not defined
            // Only check if emulation types are used
            if (wgslSource.Contains("emu_i64") || wgslSource.Contains("emu_u64"))
            {
                var definedFns = new HashSet<string>();
                foreach (System.Text.RegularExpressions.Match m in s_fnNamePattern.Matches(wgslSource))
                    definedFns.Add(m.Groups[1].Value);

                // Check common emulation functions that are called
                string[] emuFunctions = { "i64_from_i32", "i64_to_i32", "u64_from_u32", "u64_to_u32",
                    "i64_add", "i64_sub", "i64_mul", "i64_lt", "i64_gt", "i64_le", "i64_ge", "i64_eq",
                    "i64_ne", "i64_shl", "i64_shr", "i64_and", "i64_or", "i64_xor", "i64_neg",
                    "i64_min", "i64_max" };
                foreach (var fn in emuFunctions)
                {
                    if (wgslSource.Contains(fn + "(") && !definedFns.Contains(fn))
                        errors.Add($"Emulation function '{fn}' called but not defined");
                }
            }
            if (wgslSource.Contains("emu_f64") || wgslSource.Contains("f64_"))
            {
                var definedFns = new HashSet<string>();
                foreach (System.Text.RegularExpressions.Match m in s_fnNamePattern.Matches(wgslSource))
                    definedFns.Add(m.Groups[1].Value);

                string[] emuFunctions = { "f64_from_f32", "f64_to_f32", "f64_add", "f64_sub",
                    "f64_mul", "f64_div", "f64_neg", "f64_abs", "f64_lt", "f64_gt", "f64_le",
                    "f64_ge", "f64_eq", "f64_ne", "f64_from_ieee754_bits", "f64_to_ieee754_bits" };
                foreach (var fn in emuFunctions)
                {
                    if (wgslSource.Contains(fn + "(") && !definedFns.Contains(fn))
                        errors.Add($"Emulation function '{fn}' called but not defined");
                }
            }

            if (errors.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append($"WGSL validation failed for kernel '{kernelName}':");
                foreach (var e in errors)
                    sb.Append($"\n  - {e}");
                var message = sb.ToString();
                if (VerboseLogging) Log($"[WGSLValidation] {message}");
                throw new InvalidOperationException(message);
            }
        }

        #region Instance

        /// <summary>
        /// The configuration options for this backend instance.
        /// </summary>
        public WebGPUBackendOptions Options { get; }

        /// <summary>
        /// Gets the set of WebGPU features enabled on the device.
        /// The WGSL code generator can use this to conditionally emit feature-gated code.
        /// </summary>
        public HashSet<string> EnabledFeatures { get; }

        /// <summary>
        /// Effective F64 emulation mode for this backend. Defaults to <c>Options.F64Emulation</c>
        /// at construction time and can be promoted at runtime via <see cref="WebGPUAccelerator.F64Mode"/>
        /// for callers that need to switch between Dekker and Ozaki without recreating the
        /// backend (e.g. <c>AcceleratorRequirements.RequiresFloat64Strict</c> applied after
        /// the accelerator already exists). Codegen reads this property, so changing it
        /// will affect kernels compiled AFTER the change. Cached compiled kernels still
        /// reflect the mode in effect when they were generated; callers that depend on
        /// re-compilation after a mode flip should clear the kernel cache.
        /// </summary>
        public F64EmulationMode F64Mode { get; set; }

        /// <summary>True if f64 emulation is enabled (Dekker or Ozaki).</summary>
        internal bool EnableF64Emulation => F64Mode != F64EmulationMode.Disabled;

        /// <summary>True if using Ozaki quad-float f64 emulation.</summary>
        internal bool UseOzakiF64Emulation => F64Mode == F64EmulationMode.Ozaki;

        /// <summary>Always true — i64 emulation is required by ILGPU IR.</summary>
        internal bool EnableI64Emulation => true;

        /// <summary>
        /// Test-only switch. When set to <c>true</c> before the WebGPU device is
        /// created, <c>shader-f16</c> is never requested from the adapter and
        /// <see cref="HasShaderF16"/> returns <c>false</c> regardless of the device's
        /// enabled feature set. Forces the WGSL f16 emulation codegen path even on
        /// adapters that expose native <c>shader-f16</c>. Pairs with
        /// <see cref="WebGPUCapabilityContext.Float16Native"/> so consumers can tell
        /// whether they're on the native or emulated path.
        /// Default: <c>false</c>.
        /// </summary>
        public static bool ForceEmulatedF16 { get; set; } = false;

        /// <summary>
        /// Returns true if shader-f16 is enabled AND the test override
        /// <see cref="ForceEmulatedF16"/> is not set. False forces the emulation
        /// codegen path (WGSL <c>_f16_to_f32</c> / <c>_f32_to_f16</c> helpers).
        /// </summary>
        public bool HasShaderF16 => !ForceEmulatedF16 && EnabledFeatures.Contains("shader-f16");

        /// <summary>Returns true if subgroups are enabled and not force-disabled by options.</summary>
        public bool HasSubgroups => !Options.ForceDisableSubgroups && EnabledFeatures.Contains("subgroups");

        /// <summary>Returns true if timestamp queries are enabled.</summary>
        public bool HasTimestampQuery => EnabledFeatures.Contains("timestamp-query");

        /// <summary>
        /// The device's maximum invocations per workgroup. Set by the accelerator after
        /// initialization. Used as the default @workgroup_size for explicitly grouped
        /// kernels that don't specify one via KernelSpecialization.
        /// </summary>
        public int? DefaultMaxWorkgroupSize { get; set; }

        /// <summary>
        /// Creates a new WebGPU backend with default options.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        public WebGPUBackend(Context context)
            : this(context, WebGPUBackendOptions.Default, new HashSet<string>())
        {
        }

        /// <summary>
        /// Creates a new WebGPU backend with the specified options.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="options">The backend configuration options.</param>
        public WebGPUBackend(Context context, WebGPUBackendOptions options)
            : this(context, options, new HashSet<string>())
        {
        }

        /// <summary>
        /// Creates a new WebGPU backend with the specified options and enabled device features.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <param name="options">The backend configuration options.</param>
        /// <param name="enabledFeatures">The set of WebGPU features enabled on the device.</param>
        public WebGPUBackend(Context context, WebGPUBackendOptions options, HashSet<string> enabledFeatures)
            : base(
                  context,
                  new WebGPUCapabilityContext(),
                  BackendTypeWebGPU,
                  new WebGPUArgumentMapper(context))
        {
            Options = options ?? WebGPUBackendOptions.Default;
            F64Mode = Options.F64Emulation;
            EnabledFeatures = enabledFeatures ?? new HashSet<string>();

            InitIntrinsicProvider();
            RegisterMathIntrinsics();

            InitializeKernelTransformers(builder =>
            {
                // Add any WebGPU-specific transformers
            });

            // Hard reference for bundling
            _ = typeof(XMath);
        }


        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated argument mapper.
        /// </summary>
        public new WebGPUArgumentMapper ArgumentMapper =>
            base.ArgumentMapper as WebGPUArgumentMapper;


        #endregion

        #region Methods

        /// <summary>
        /// Gets the intrinsic manager via reflection as it is internal.
        /// </summary>
        private static IntrinsicImplementationManager GetIntrinsicManager(Context context)
        {
            var prop = typeof(Context).GetProperty("IntrinsicManager", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (prop == null) throw new InvalidOperationException("Could not find IntrinsicManager property on Context.");
            var manager = (IntrinsicImplementationManager)prop.GetValue(context);
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

                var containers = (Array)containersField.GetValue(manager);
                int webGpuIndex = (int)BackendTypeWebGPU;

                // Check if we need to resize or initialize
                if (webGpuIndex >= containers.Length || containers.GetValue(webGpuIndex) == null)
                {
                    // We need to fix it.
                    // Get BackendContainer type
                    var containerType = mgrType.GetNestedType("BackendContainer", BindingFlags.NonPublic);
                    var createMethod = containerType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public);

                    if (webGpuIndex >= containers.Length)
                    {
                        if (VerboseLogging) Log($"WebGPU: Resizing IntrinsicManager containers from {containers.Length} to {webGpuIndex + 1}");
                        var newContainers = Array.CreateInstance(containerType, webGpuIndex + 1);
                        Array.Copy(containers, newContainers, containers.Length);
                        containers = newContainers;
                        containersField.SetValue(manager, containers);
                    }

                    // Initialize the slot
                    // Since it's a struct, it might be "not null" but default initialized (null matchers)
                    // We can check if matchers field is null to confirm initialization is needed?
                    // Actually, let's just create a new one and set it, to be safe.
                    // But array of structs... setting the value boxes it.
                    // Array.SetValue works for structs.

                    var newContainer = createMethod.Invoke(null, null);
                    containers.SetValue(newContainer, webGpuIndex);
                    if (VerboseLogging) Log("WebGPU: Initialized BackendContainer for WebGPU.");
                }
                else
                {
                    // Check if matchers are initialized (it's a struct, so the array entry isn't null, but its fields might be)
                    var containerType = mgrType.GetNestedType("BackendContainer", BindingFlags.NonPublic);
                    var container = containers.GetValue(webGpuIndex);
                    var matchersField = containerType.GetField("matchers", BindingFlags.Instance | BindingFlags.NonPublic);
                    var matchers = matchersField.GetValue(container);
                    if (matchers == null)
                    {
                        if (VerboseLogging) Log("WebGPU: BackendContainer found but uninitialized. Re-initializing.");
                        var createMethod = containerType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public);
                        var newContainer = createMethod.Invoke(null, null);
                        containers.SetValue(newContainer, webGpuIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                if (VerboseLogging) Log($"WebGPU: Error fixing IntrinsicManager: {ex}");
            }
        }

        private void RegisterIntrinsic(MethodInfo method, WGSLIntrinsic.Handler handler)
        {
            if (method == null)
            {
                if (VerboseLogging) Log("WebGPU: Skipping invalid intrinsic method (null)");
                return;
            }
            if (VerboseLogging) Log($"WebGPU: Registering Intrinsic: {method.DeclaringType.Name}.{method.Name}");
            GetIntrinsicManager(Context).RegisterMethod(
                method,
                new WebGPUIntrinsic(
                    handler.Method,
                    IntrinsicImplementationMode.GenerateCode));
        }

        private void RegisterIntrinsic(Type type, string methodName, WGSLIntrinsic.Handler handler)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == methodName);

            bool found = false;
            foreach (var method in methods)
            {
                found = true;
                RegisterIntrinsic(method, handler);
            }
            if (!found)
            {
                if (VerboseLogging) Log($"WebGPU: Intrinsic not found: {type.Name}.{methodName}");
            }
        }

        private void RegisterRedirect(MethodInfo original, MethodInfo target)
        {
            if (original == null || target == null) return;
            if (VerboseLogging) Log($"WebGPU: Redirecting {original.DeclaringType.Name}.{original.Name} -> {target.DeclaringType.Name}.{target.Name}");
            GetIntrinsicManager(Context).RegisterMethod(
                original,
                new WebGPUIntrinsic(
                    target,
                    IntrinsicImplementationMode.Redirect));
        }

        private void RegisterMathIntrinsics()
        {
            var t = typeof(WebGPUIntrinsics);

            // Helpers to register: 1. Redirect Original -> Wrapper, 2. Handler for Wrapper
            void Reg(MethodInfo original, MethodInfo wrapper, WGSLIntrinsic.Handler handler)
            {
                if (original == null) return;
                // EXPERIMENT: Direct Registration to bypass potentially broken Redirect
                RegisterRedirect(original, wrapper);
                RegisterIntrinsic(wrapper, handler);
            }

            void RegAll(Type type, string name, WGSLIntrinsic.Handler handler)
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
                        else
                        {
                            continue;
                        }
                    }

                    var pTypes = target.GetParameters().Select(p => p.ParameterType).ToArray();

                    // FIX: Use GetMethod with types to avoid AmbiguousMatchException
                    var wrapper = t.GetMethod(
                        name,
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        pTypes, // Explicitly pass parameter types
                        null);

                    if (wrapper != null)
                    {
                        if (VerboseLogging) Log($"WebGPU: Mapping {type.Name}.{name}({string.Join(",", pTypes.Select(pt => pt.Name))}) to {t.Name}.{name}");
                        Reg(target, wrapper, handler);
                    }
                    else
                    {
                        // Debug log for missing wrapper
                        var pTypeNames = string.Join(", ", pTypes.Select(pt => pt.Name));
                        if (VerboseLogging) Log($"WebGPU: Missing wrapper for {type.Name}.{name}({pTypeNames})");
                    }
                }
            }

            // Unary
            RegAll(typeof(Math), "Abs", WGSLCodeGenerator.GenerateAbs);
            RegAll(typeof(MathF), "Abs", WGSLCodeGenerator.GenerateAbs);

            RegAll(typeof(Math), "Sign", WGSLCodeGenerator.GenerateSign);
            RegAll(typeof(MathF), "Sign", WGSLCodeGenerator.GenerateSign);

            RegAll(typeof(Math), "Round", WGSLCodeGenerator.GenerateRound);
            RegAll(typeof(MathF), "Round", WGSLCodeGenerator.GenerateRound);

            RegAll(typeof(Math), "Truncate", WGSLCodeGenerator.GenerateTruncate);
            RegAll(typeof(MathF), "Truncate", WGSLCodeGenerator.GenerateTruncate);

            // Binary
            RegAll(typeof(Math), "Atan2", WGSLCodeGenerator.GenerateAtan2);
            RegAll(typeof(MathF), "Atan2", WGSLCodeGenerator.GenerateAtan2);

            RegAll(typeof(Math), "Max", WGSLCodeGenerator.GenerateMax);
            RegAll(typeof(MathF), "Max", WGSLCodeGenerator.GenerateMax);

            RegAll(typeof(Math), "Min", WGSLCodeGenerator.GenerateMin);
            RegAll(typeof(MathF), "Min", WGSLCodeGenerator.GenerateMin);

            RegAll(typeof(Math), "Pow", WGSLCodeGenerator.GeneratePow);
            RegAll(typeof(MathF), "Pow", WGSLCodeGenerator.GeneratePow);

            // Ternary
            RegAll(typeof(Math), "Max", WGSLCodeGenerator.GenerateClamp); // Using GenerateClamp just to match handler signature, but target is Max?
                                                                          // Wait, RegAll finds 'Max' method in 'WebGPUIntrinsics'. I want to map Math.Clamp -> WebGPUIntrinsics.Max

            // Manual Redirect for test
            var mClamp = typeof(MathF).GetMethod("Clamp", new[] { typeof(float), typeof(float), typeof(float) });
            var wMax = typeof(WebGPUIntrinsics).GetMethod("Max", new[] { typeof(float), typeof(float) });
            // Signature mismatch... Max takes 2 args. 

            // Revert to standard RegAll for Clamp, but I will modify RegAll to force a different target.

            // Actually, keep standard RegAll. I suspect FixIntrinsicManager.
            RegAll(typeof(Math), "Clamp", WGSLCodeGenerator.GenerateClamp);
            RegAll(typeof(MathF), "Clamp", WGSLCodeGenerator.GenerateClamp);

            RegAll(typeof(Math), "FusedMultiplyAdd", WGSLCodeGenerator.GenerateFusedMultiplyAdd);
            RegAll(typeof(MathF), "FusedMultiplyAdd", WGSLCodeGenerator.GenerateFusedMultiplyAdd);

            // Register IntrinsicMath methods - these are the targets of RemappedIntrinsics
            // Math.Abs(int) -> IntrinsicMath.Abs(int) during compilation, so we need handlers for IntrinsicMath
            RegAll(typeof(IntrinsicMath), "Abs", WGSLCodeGenerator.GenerateAbs);
            RegAll(typeof(IntrinsicMath), "Min", WGSLCodeGenerator.GenerateMin);
            RegAll(typeof(IntrinsicMath), "Max", WGSLCodeGenerator.GenerateMax);

            // Register XMath functions from ILGPU.Algorithms
            try
            {
                var xmathType = Type.GetType("ILGPU.Algorithms.XMath, ILGPU.Algorithms");
                if (xmathType != null)
                {
                    RegAll(xmathType, "Rsqrt", WGSLCodeGenerator.GenerateRsqrt);
                    RegAll(xmathType, "Rcp", WGSLCodeGenerator.GenerateRcp);
                    if (VerboseLogging) Log("WebGPU: Registered XMath intrinsics (Rsqrt, Rcp)");
                }
                else
                {
                    if (VerboseLogging) Log("WebGPU: XMath type not found - ILGPU.Algorithms may not be loaded");
                }
            }
            catch (Exception ex)
            {
                if (VerboseLogging) Log($"WebGPU: Error registering XMath intrinsics: {ex.Message}");
            }

            // Register ILGPU.Algorithms group/warp reduction intrinsics.
            // These override the default IL fallback (PTXWarpExtensions butterfly shuffle)
            // with WGSL subgroup intrinsics (subgroupMax, subgroupMin, subgroupAdd, etc.)
            // The open generic method definition is registered so all TReduction specializations
            // (MaxInt32, MinInt32, AddInt32, etc.) are handled by a single registration.
            try
            {
                var groupExtType = Type.GetType("ILGPU.Algorithms.GroupExtensions, ILGPU.Algorithms");
                var warpExtType = Type.GetType("ILGPU.Algorithms.WarpExtensions, ILGPU.Algorithms");
                const BindingFlags algorithmFlags = BindingFlags.Public | BindingFlags.Static;

                if (groupExtType != null)
                {
                    var groupReduce = groupExtType.GetMethod("Reduce", algorithmFlags);
                    var groupAllReduce = groupExtType.GetMethod("AllReduce", algorithmFlags);

                    if (groupReduce != null)
                    {
                        RegisterIntrinsic(groupReduce, WGSLCodeGenerator.GenerateGroupReduce);
                        if (VerboseLogging) Log("WebGPU: Registered GroupExtensions.Reduce intrinsic");
                    }
                    if (groupAllReduce != null)
                    {
                        RegisterIntrinsic(groupAllReduce, WGSLCodeGenerator.GenerateGroupReduce);
                        if (VerboseLogging) Log("WebGPU: Registered GroupExtensions.AllReduce intrinsic");
                    }
                }
                else
                {
                    if (VerboseLogging) Log("WebGPU: GroupExtensions type not found - ILGPU.Algorithms may not be loaded");
                }

                if (warpExtType != null)
                {
                    var warpReduce = warpExtType.GetMethod("Reduce", algorithmFlags);
                    var warpAllReduce = warpExtType.GetMethod("AllReduce", algorithmFlags);

                    if (warpReduce != null)
                    {
                        RegisterIntrinsic(warpReduce, WGSLCodeGenerator.GenerateWarpReduce);
                        if (VerboseLogging) Log("WebGPU: Registered WarpExtensions.Reduce intrinsic");
                    }
                    if (warpAllReduce != null)
                    {
                        RegisterIntrinsic(warpAllReduce, WGSLCodeGenerator.GenerateWarpReduce);
                        if (VerboseLogging) Log("WebGPU: Registered WarpExtensions.AllReduce intrinsic");
                    }
                }
                else
                {
                    if (VerboseLogging) Log("WebGPU: WarpExtensions type not found - ILGPU.Algorithms may not be loaded");
                }

                // Also register the IL-level implementations that ILGPU inlines when compiling
                // ILGroupExtensions.AllReduce and PTXWarpExtensions.Reduce. Without these, ILGPU
                // compiles the C# IL bodies — which use Warp.IsFirstLane compiled as GroupIndexValue
                // (local_id == 0) instead of LaneIdxValue (subgroup_invocation_id == 0), causing
                // atomicMax to only fire for thread 0 and silently dropping all but the first subgroup.
                const BindingFlags internalFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

                var ilGroupExtType = Type.GetType("ILGPU.Algorithms.IL.ILGroupExtensions, ILGPU.Algorithms");
                if (ilGroupExtType != null)
                {
                    var ilGroupReduce = ilGroupExtType.GetMethod("Reduce", internalFlags);
                    var ilGroupAllReduce = ilGroupExtType.GetMethod("AllReduce", internalFlags);
                    if (ilGroupReduce != null)
                    {
                        RegisterIntrinsic(ilGroupReduce, WGSLCodeGenerator.GenerateGroupAllReduce);
                        if (VerboseLogging) Log("WebGPU: Registered ILGroupExtensions.Reduce intrinsic");
                    }
                    if (ilGroupAllReduce != null)
                    {
                        RegisterIntrinsic(ilGroupAllReduce, WGSLCodeGenerator.GenerateGroupAllReduce);
                        if (VerboseLogging) Log("WebGPU: Registered ILGroupExtensions.AllReduce intrinsic");
                    }
                }

                var ilWarpExtType = Type.GetType("ILGPU.Algorithms.IL.ILWarpExtensions, ILGPU.Algorithms");
                if (ilWarpExtType != null)
                {
                    var ilWarpReduce = ilWarpExtType.GetMethod("Reduce", internalFlags);
                    var ilWarpAllReduce = ilWarpExtType.GetMethod("AllReduce", internalFlags);
                    if (ilWarpReduce != null)
                    {
                        RegisterIntrinsic(ilWarpReduce, WGSLCodeGenerator.GenerateWarpReduce);
                        if (VerboseLogging) Log("WebGPU: Registered ILWarpExtensions.Reduce intrinsic");
                    }
                    if (ilWarpAllReduce != null)
                    {
                        RegisterIntrinsic(ilWarpAllReduce, WGSLCodeGenerator.GenerateWarpReduce);
                        if (VerboseLogging) Log("WebGPU: Registered ILWarpExtensions.AllReduce intrinsic");
                    }
                }

                var ptxWarpExtType = Type.GetType("ILGPU.Algorithms.PTX.PTXWarpExtensions, ILGPU.Algorithms");
                if (ptxWarpExtType != null)
                {
                    var ptxWarpReduce = ptxWarpExtType.GetMethod("Reduce", internalFlags);
                    var ptxWarpAllReduce = ptxWarpExtType.GetMethod("AllReduce", internalFlags);
                    if (ptxWarpReduce != null)
                    {
                        RegisterIntrinsic(ptxWarpReduce, WGSLCodeGenerator.GenerateWarpReduce);
                        if (VerboseLogging) Log("WebGPU: Registered PTXWarpExtensions.Reduce intrinsic");
                    }
                    if (ptxWarpAllReduce != null)
                    {
                        RegisterIntrinsic(ptxWarpAllReduce, WGSLCodeGenerator.GenerateWarpReduce);
                        if (VerboseLogging) Log("WebGPU: Registered PTXWarpExtensions.AllReduce intrinsic");
                    }
                }

            }
            catch (Exception ex)
            {
                if (VerboseLogging) Log($"WebGPU: Error registering group/warp reduction intrinsics: {ex.Message}");
            }

            // Register Half ↔ float conversion intrinsics unconditionally.
            // The default HalfExtensions.ConvertHalfToFloat / ConvertFloatToHalf use
            // static lookup tables that cannot be accessed from GPU code. When inlined,
            // all table reads return zero, producing incorrect results. The generators
            // below handle both modes: native WGSL f32()/f16() when shader-f16 is
            // available, or an f32 pass-through / _f16_to_f32(_f32_to_f16()) round-trip
            // in emulated mode where Half locals are already represented as f32.
            RegisterIntrinsic(
                typeof(HalfExtensions),
                nameof(HalfExtensions.ConvertHalfToFloat),
                WGSLCodeGenerator.GenerateConvertHalfToFloat);
            RegisterIntrinsic(
                typeof(HalfExtensions),
                nameof(HalfExtensions.ConvertFloatToHalf),
                WGSLCodeGenerator.GenerateConvertFloatToHalf);
        }


        /// <summary>
        /// Creates a new entry point.
        /// </summary>
        protected override EntryPoint CreateEntryPoint(
            in EntryPointDescription entry,
            in BackendContext backendContext,
            in KernelSpecialization specialization) =>
            new EntryPoint(
                entry,
                backendContext.SharedMemorySpecification,
                specialization);

        /// <summary>
        /// Creates the main kernel builder and initializes generator args.
        /// </summary>
        protected override StringBuilder CreateKernelBuilder(
            EntryPoint entryPoint,
            in BackendContext backendContext,
            in KernelSpecialization specialization,
            out WGSLCodeGenerator.GeneratorArgs data)
        {
            var builder = new StringBuilder();

            builder.AppendLine("//");
            builder.Append("// Generated by SpawnDev.ILGPU.WebGPU v");
            builder.AppendLine(Context.Version);
            builder.AppendLine("//");
            builder.AppendLine();

            var typeGenerator = new WGSLTypeGenerator(this, Context.TypeContext);

            // For explicitly grouped kernels, use the specialization's MaxNumThreadsPerGroup
            // if available. Do NOT fall back to DefaultMaxWorkgroupSize (the device's max,
            // e.g. 1024) — that would bake a too-large @workgroup_size into the WGSL, causing
            // Group.DimX to return the wrong value and spawning more threads than the dispatch
            // intends. The default path in GetWorkgroupSize() (64 for 1D, etc.) is safe.
            int? maxWorkgroupSize = specialization.MaxNumThreadsPerGroup;
            if (VerboseLogging)
                Log($"[CreateKernelBuilder] spec.MaxNumThreads={specialization.MaxNumThreadsPerGroup}, IsExplicitlyGrouped={entryPoint.IsExplicitlyGrouped}, DefaultMax={DefaultMaxWorkgroupSize}, final={maxWorkgroupSize}");

            data = new WGSLCodeGenerator.GeneratorArgs(
                this,
                typeGenerator,
                entryPoint,
                backendContext.SharedAllocations,
                backendContext.DynamicSharedAllocations,
                maxWorkgroupSize);

            return builder;
        }

        /// <summary>
        /// Creates a function-code generator for helper methods.
        /// </summary>
        protected override WGSLCodeGenerator CreateFunctionCodeGenerator(
            Method method,
            Allocas allocas,
            WGSLCodeGenerator.GeneratorArgs data)
        {
            // rc.16 work-in-progress: re-activating the rc.14 fn-definition
            // emission path with body-codegen fixes for cross-scope identifier
            // resolution. Methods marked [MethodImpl(NoInlining)] (no IR Inline
            // flag) get emitted as standalone WGSL `fn` definitions; methods
            // with the Inline flag stay on the codegen-time inline path.
            // Tracks `Plans/rc16-fn-def-codegen-harden.md`.
            if (method.HasFlags(MethodFlags.Inline))
                data.HelperMethods[method] = allocas;
            else
                // Track non-Inline methods so the kernel's IR-scan passes
                // (SetEmulationFlags, ScanForSubgroupAndBroadcastUsage) see
                // 64-bit ops / barriers / broadcasts that live in their bodies.
                // Without this, the kernel emits calls to i64_eq / i64_ge etc.
                // but the emulation library isn't pulled in -> WGSL validator
                // fails with "function called but not defined" (Tuvok's
                // 2026-05-05 fn-definition path bug). rc.16 Bug D follow-up.
                data.NonInlineMethods.Add(method);
            return new WGSLFunctionGenerator(data, method, allocas);
        }

        /// <summary>
        /// Creates a kernel-code generator for the main entry point.
        /// </summary>
        protected override WGSLCodeGenerator CreateKernelCodeGenerator(
            in AllocaKindInformation sharedAllocations,
            Method method,
            Allocas allocas,
            WGSLCodeGenerator.GeneratorArgs data) =>
            new WGSLKernelFunctionGenerator(data, method, allocas);

        /// <summary>
        /// Creates the final compiled kernel.
        /// </summary>
        protected override CompiledKernel CreateKernel(
            EntryPoint entryPoint,
            CompiledKernel.KernelInfo? kernelInfo,
            StringBuilder builder,
            WGSLCodeGenerator.GeneratorArgs data)
        {
            var wgslSource = builder.ToString();

            // Resolve the deferred enable subgroups placeholder.
            // The placeholder was emitted in GenerateHeader() because the IR pre-scan
            // may flag subgroup usage for nodes that are later handled via shared-memory
            // emulation (e.g., 64-bit reductions). Now that the full WGSL is assembled,
            // check if there are actual subgroup builtins in the shader body.
            if (wgslSource.Contains("/*__WGSL_ENABLE_SUBGROUPS_PLACEHOLDER__*/"))
            {
                bool needsSubgroups = wgslSource.Contains("subgroup_size") ||
                                      wgslSource.Contains("subgroup_invocation_id") ||
                                      wgslSource.Contains("subgroupShuffle") ||
                                      wgslSource.Contains("subgroupAdd") ||
                                      wgslSource.Contains("subgroupMax") ||
                                      wgslSource.Contains("subgroupMin") ||
                                      wgslSource.Contains("subgroupBroadcastFirst") ||
                                      wgslSource.Contains("subgroup_id");
                if (needsSubgroups)
                {
                    wgslSource = wgslSource.Replace("/*__WGSL_ENABLE_SUBGROUPS_PLACEHOLDER__*/", "enable subgroups;");
                }
                else
                {
                    wgslSource = wgslSource.Replace("/*__WGSL_ENABLE_SUBGROUPS_PLACEHOLDER__*/\r\n", "");
                    wgslSource = wgslSource.Replace("/*__WGSL_ENABLE_SUBGROUPS_PLACEHOLDER__*/\n", "");
                    wgslSource = wgslSource.Replace("/*__WGSL_ENABLE_SUBGROUPS_PLACEHOLDER__*/", "");
                }
            }

            // Resolve the const workgroup_size placeholder.
            // The placeholder was emitted in GenerateModuleHeader() before shared memory
            // allocations were known. Now extract the actual workgroup size from the
            // @compute @workgroup_size(...) annotation and use it for the const declaration.
            if (wgslSource.Contains("/*__WGSL_CONST_WORKGROUP_SIZE_PLACEHOLDER__*/"))
            {
                string wgConst = "const workgroup_size : vec3<u32> = vec3<u32>(64u, 1u, 1u);";
                var wgMatch = s_workgroupSizePattern.Match(wgslSource);
                if (wgMatch.Success)
                {
                    string x = wgMatch.Groups[1].Value;
                    string y = wgMatch.Groups[2].Success ? wgMatch.Groups[2].Value : "1";
                    string z = wgMatch.Groups[3].Success ? wgMatch.Groups[3].Value : "1";
                    wgConst = $"const workgroup_size : vec3<u32> = vec3<u32>({x}u, {y}u, {z}u);";
                }
                wgslSource = wgslSource.Replace("/*__WGSL_CONST_WORKGROUP_SIZE_PLACEHOLDER__*/", wgConst);
            }

            // Prepend kernel method name as a comment for identification
            var methodName = entryPoint.MethodInfo?.Name ?? "unknown";
            wgslSource = $"// Kernel: {methodName}\n" + wgslSource;

            // Run structural validation before caching
            ValidateWGSL(wgslSource, methodName);

            LastGeneratedWGSL = wgslSource;
            AllGeneratedWGSL.Add(wgslSource);

            // Populate named WGSL registry with structural metadata
            int regWorkgroupSize = 0;
            var regWgMatch = s_workgroupSizeSimplePattern.Match(wgslSource);
            if (regWgMatch.Success)
                regWorkgroupSize = int.Parse(regWgMatch.Groups[1].Value);
            int regBindingCount = data.ExpectedBindingCountHolder.Count > 0
                ? data.ExpectedBindingCountHolder[0] : 0;
            int regSharedBytes = 0;
            var sharedVarNames = new List<string>();
            foreach (var m in s_sharedMemoryPattern.Matches(wgslSource)
                .Cast<System.Text.RegularExpressions.Match>())
            {
                regSharedBytes += int.Parse(m.Groups[1].Value) * 4; // each element is 4 bytes
            }
            foreach (var m in s_sharedVarDeclPattern.Matches(wgslSource)
                .Cast<System.Text.RegularExpressions.Match>())
            {
                sharedVarNames.Add(m.Groups[1].Value);
            }
            // Count emulation functions defined in this shader
            int emuFnCount = 0;
            foreach (var m in s_fnNamePattern.Matches(wgslSource)
                .Cast<System.Text.RegularExpressions.Match>())
            {
                var fn = m.Groups[1].Value;
                if (fn.StartsWith("i64_") || fn.StartsWith("u64_") || fn.StartsWith("f64_"))
                    emuFnCount++;
            }
            WGSLRegistry[methodName] = new WGSLEntry(
                KernelName: methodName,
                Source: wgslSource,
                WorkgroupSize: regWorkgroupSize,
                SharedMemoryBytes: regSharedBytes,
                BindingCount: regBindingCount,
                LineCount: wgslSource.Split('\n').Length,
                EmulationFunctionCount: emuFnCount,
                SharedMemoryVars: sharedVarNames.ToArray(),
                UsesBarriers: wgslSource.Contains("workgroupBarrier()"),
                UsesAtomics: wgslSource.Contains("atomicAdd(") || wgslSource.Contains("atomicMax(") ||
                    wgslSource.Contains("atomicMin(") || wgslSource.Contains("atomicStore(") ||
                    wgslSource.Contains("atomicLoad(") || wgslSource.Contains("atomicCompareExchangeWeak(") ||
                    wgslSource.Contains("atomicAnd(") || wgslSource.Contains("atomicOr(") ||
                    wgslSource.Contains("atomicXor(") || wgslSource.Contains("atomicExchange("),
                UsesSubgroups: wgslSource.Contains("enable subgroups;"),
                UniformityTransformApplied: wgslSource.Contains("_uf_group_iter") || wgslSource.Contains("_uf_tile_iter"),
                UsesI64Emulation: wgslSource.Contains("emu_i64"),
                UsesF64Emulation: wgslSource.Contains("emu_f64"),
                Timestamp: DateTime.UtcNow);

            // Auto-dump WGSL to file if configured (desktop only)
            if (WGSLDumpPath != null && !OperatingSystem.IsBrowser())
            {
                try
                {
                    Directory.CreateDirectory(WGSLDumpPath);
                    File.WriteAllText(Path.Combine(WGSLDumpPath, $"{methodName}.wgsl"), wgslSource);
                }
                catch { /* best-effort dump */ }
            }

            // Fire callback for browser shader debug (ShaderDebugService)
            try { OnShaderCompiled?.Invoke(methodName, wgslSource, WGSLRegistry[methodName]); }
            catch (Exception ex) { if (VerboseLogging) Log($"[WebGPU] OnShaderCompiled subscriber error: {ex.Message}"); }

            if (VerboseLogging)
            {
                Log($"--- GENERATED WGSL ({methodName}) ---");
                Log(wgslSource);
                Log("-----------------------");
            }

            return new WebGPUCompiledKernel(
                Context,
                entryPoint,
                wgslSource,
                data.DynamicSharedOverrides.Count > 0 ? data.DynamicSharedOverrides : null,
                data.ScalarPackingManifest.Count > 0 ? data.ScalarPackingManifest : null,
                data.ExpectedBindingCountHolder.Count > 0 ? data.ExpectedBindingCountHolder[0] : 0,
                data.I64SpinlockParamIndices.Count > 0 ? data.I64SpinlockParamIndices : null,
                data.CoalesceManifest.Count > 0 ? data.CoalesceManifest : null);
        }

        #endregion
    }

    /// <summary>
    /// Represents a compiled WebGPU/WGSL kernel.
    /// </summary>
    public class WebGPUCompiledKernel : CompiledKernel
    {
        /// <summary>
        /// Gets the generated WGSL source code.
        /// </summary>
        public string WGSLSource { get; }

        /// <summary>
        /// Gets the dynamic shared memory override constant metadata.
        /// Empty list if no dynamic shared memory is used.
        /// </summary>
        public IReadOnlyList<DynamicSharedOverrideInfo> DynamicSharedOverrides { get; }

        /// <summary>
        /// Returns true if this kernel uses dynamic shared memory.
        /// </summary>
        public bool HasDynamicSharedMemory => DynamicSharedOverrides.Count > 0;

        /// <summary>
        /// Gets the scalar packing manifest describing how scalar params are packed into a single buffer.
        /// Empty list if no scalar packing is used (e.g., no scalar params).
        /// </summary>
        public IReadOnlyList<ScalarPackingEntry> ScalarPackingManifest { get; }

        /// <summary>
        /// Returns true if this kernel uses scalar parameter packing.
        /// </summary>
        public bool HasScalarPacking => ScalarPackingManifest.Count > 0;

        /// <summary>
        /// The number of @group(0) bindings emitted in the WGSL. Used to validate
        /// that the bind group entry count matches the shader layout.
        /// </summary>
        public int ExpectedBindingCount { get; }

        /// <summary>
        /// Spinlock-companion-buffer keys for i64/f64 Min/Max/Exchange atomics.
        /// Key: (ParamIdx, FieldIdx). FieldIdx=-1 for direct view params;
        /// FieldIdx>=0 for body-struct view fields. The dispatch code auto-allocates one
        /// lock buffer per entry and binds it (in OrderBy order, matching WGSL emission).
        /// </summary>
        public IReadOnlySet<(int ParamIdx, int FieldIdx)> I64SpinlockParamIndices { get; }

        /// <summary>
        /// Returns true if this kernel needs spinlock buffers for i64 atomics.
        /// </summary>
        public bool HasI64Spinlocks => I64SpinlockParamIndices.Count > 0;

        /// <summary>
        /// Manifest of body-struct ArrayView fields that share coalesced storage-buffer
        /// bindings. Empty when the kernel fits within the device's binding limit
        /// without coalescing. Each entry says "fields F1, F2, ... of body-struct
        /// param N share binding @binding(K)"; the dispatcher allocates one GPU buffer
        /// per entry, concatenates each member's data, and binds it once.
        /// </summary>
        public IReadOnlyList<CoalesceGroupEntry> CoalesceManifest { get; }

        /// <summary>Returns true if any coalesce group is active for this kernel.</summary>
        public bool HasCoalesceGroups => CoalesceManifest.Count > 0;

        /// <summary>
        /// Creates a new compiled WebGPU kernel.
        /// </summary>
        public WebGPUCompiledKernel(
            Context context,
            EntryPoint entryPoint,
            string wgslSource,
            IReadOnlyList<DynamicSharedOverrideInfo>? dynamicSharedOverrides = null,
            IReadOnlyList<ScalarPackingEntry>? scalarPackingManifest = null,
            int expectedBindingCount = 0,
            IReadOnlySet<(int ParamIdx, int FieldIdx)>? i64SpinlockParamIndices = null,
            IReadOnlyList<CoalesceGroupEntry>? coalesceManifest = null)
            : base(context, entryPoint, null)
        {
            WGSLSource = wgslSource;
            DynamicSharedOverrides = dynamicSharedOverrides ?? Array.Empty<DynamicSharedOverrideInfo>();
            ScalarPackingManifest = scalarPackingManifest ?? Array.Empty<ScalarPackingEntry>();
            ExpectedBindingCount = expectedBindingCount;
            I64SpinlockParamIndices = i64SpinlockParamIndices ?? new HashSet<(int, int)>();
            CoalesceManifest = coalesceManifest ?? Array.Empty<CoalesceGroupEntry>();
        }
    }

    /// <summary>
    /// Argument mapper for WebGPU kernel parameters.
    /// </summary>
    public class WebGPUArgumentMapper : ArgumentMapper
    {
        /// <summary>
        /// Creates a new WebGPU argument mapper.
        /// </summary>
        public WebGPUArgumentMapper(Context context) : base(context) { }

        /// <summary>
        /// Maps a view type to its WebGPU equivalent.
        /// </summary>
        protected override System.Type MapViewType(System.Type viewType, System.Type elementType)
        {
            return viewType;
        }

        /// <summary>
        /// Maps a view instance for kernel invocation.
        /// </summary>
        protected override void MapViewInstance<TILEmitter, TSource, TTarget>(
            in TILEmitter emitter,
            System.Type viewType,
            in TSource source,
            in TTarget target)
        {
            // View mapping is handled separately for WebGPU
        }
    }

    /// <summary>
    /// Capability context for WebGPU backend.
    /// </summary>
    public class WebGPUCapabilityContext : CapabilityContext
    {
        /// <summary>
        /// Creates a new WebGPU capability context.
        /// </summary>
        public WebGPUCapabilityContext() : base()
        {
        }
    }

    /// <summary>
    /// Describes a coalesced storage-buffer binding shared by multiple body-struct
    /// ArrayView fields of the same element type. Produced by
    /// <c>WGSLKernelFunctionGenerator.DecideCoalesceGroups</c> when a kernel would
    /// otherwise exceed <c>maxStorageBuffersPerShaderStage</c>; consumed by
    /// <c>WebGPUAccelerator</c> at dispatch time to allocate one shared GPU buffer
    /// per group, copy each member's data into it, and bind ONCE instead of N times.
    /// </summary>
    public class CoalesceGroupEntry
    {
        /// <summary>
        /// IR parameter index of the body-struct holding the coalesced fields when
        /// <see cref="IsDirectParam"/> is false. -1 (unused) when <see cref="IsDirectParam"/> is true.
        /// </summary>
        public int BodyStructParamIndex { get; set; }

        /// <summary>
        /// True when this group coalesces same-typed direct ArrayView kernel parameters
        /// (rather than body-struct view fields). When true, <see cref="MemberDirectParamIndices"/>
        /// holds the IR parameter indices of the grouped direct params and
        /// <see cref="BodyStructParamIndex"/> / <see cref="MemberFieldIndices"/> are unused.
        /// (rc.16 direct-param coalesce, 2026-05-05.)
        /// </summary>
        public bool IsDirectParam { get; set; } = false;

        /// <summary>
        /// True when this is a SUB-WORD direct-param coalesce group (Int8/UInt8/Int16/
        /// UInt16/Float16). The shared binding storage class is `array&lt;atomic&lt;u32&gt;&gt;`
        /// (matches existing sub-word direct param storage); per-member offsets stored in
        /// `_scalar_params` are ELEMENT counts (byte_offset / SubWordElementByteSize), not
        /// u32-slot offsets. Used by the dispatcher to choose the right offset formula.
        /// (rc.16 direct-param coalesce v2 sub-word, 2026-05-05.)
        /// </summary>
        public bool IsSubWord { get; set; } = false;

        /// <summary>
        /// Element byte size for sub-word groups (1 for Int8/UInt8; 2 for Int16/UInt16/Float16).
        /// 0 when <see cref="IsSubWord"/> is false.
        /// </summary>
        public int SubWordElementByteSize { get; set; } = 0;

        /// <summary>
        /// Element-type key identifying which fields share this binding.
        /// One of: "i32", "u32", "f32", "u32_emu_i64", "u32_emu_f64".
        /// </summary>
        public string ElementTypeKey { get; set; } = "";

        /// <summary>The WGSL identifier for the shared binding, e.g. "param1_i32_coalesced".</summary>
        public string BindingName { get; set; } = "";

        /// <summary>The @binding(N) slot occupied by this group's coalesced buffer.</summary>
        public int BindingIndex { get; set; } = -1;

        /// <summary>WGSL element type of the binding (i32/u32/f32 — emu_64 always rides as u32).</summary>
        public string BindingWgslType { get; set; } = "u32";

        /// <summary>1 for i32/f32/u32; 2 for emu_i64/emu_f64 (each logical element occupies 2 u32 slots).</summary>
        public int ElementWordsPerSlot { get; set; } = 1;

        /// <summary>
        /// Body-struct field indices that participate in this group, in concatenation order.
        /// The runtime appends each field's GPU buffer data into the shared buffer in this order.
        /// Used when <see cref="IsDirectParam"/> is false.
        /// </summary>
        public List<int> MemberFieldIndices { get; set; } = new();

        /// <summary>
        /// Direct ArrayView parameter indices (in IR Method.Parameters space) participating in this
        /// group, in concatenation order. Used when <see cref="IsDirectParam"/> is true.
        /// (rc.16 direct-param coalesce, 2026-05-05.)
        /// </summary>
        public List<int> MemberDirectParamIndices { get; set; } = new();
    }

    /// <summary>
    /// Describes how a single scalar kernel parameter is packed into the shared scalar buffer.
    /// </summary>
    public class ScalarPackingEntry
    {
        /// <summary>The kernel parameter index (matches ILGPU EntryPoint.Parameters).</summary>
        public int ParamIndex { get; set; }

        /// <summary>Byte offset within the packed buffer where this scalar starts.</summary>
        public int ByteOffset { get; set; }

        /// <summary>Size in bytes of this scalar (4 for i32/f32/u32, 8 for emulated f64/i64).</summary>
        public int ByteSize { get; set; }

        /// <summary>The WGSL type string for unpacking (e.g., "i32", "f32", "u32").</summary>
        public string WgslType { get; set; } = "i32";

        /// <summary>True if this is an emulated 64-bit float parameter.</summary>
        public bool IsEmulatedF64 { get; set; }

        /// <summary>True if this is an emulated 64-bit integer parameter.</summary>
        public bool IsEmulatedI64 { get; set; }

        /// <summary>True if this is a struct parameter.</summary>
        public bool IsStruct { get; set; }

        /// <summary>
        /// True if this entry represents the element offset of a buffer view binding.
        /// When true, the runtime packs contiguous.Index (element count) at this slot
        /// so the WGSL shader can offset array accesses for sub-views.
        /// </summary>
        public bool IsViewOffset { get; set; }

        /// <summary>
        /// For IsViewOffset entries: the binding index of the associated buffer.
        /// This lets the runtime correlate the packed scalar slot with the correct buffer binding.
        /// </summary>
        public int ViewBindingIndex { get; set; } = -1;

        /// <summary>
        /// True if this entry represents the element COUNT of a packed-struct body-struct view.
        /// The runtime packs the actual element count (contiguous.Length) here so the WGSL
        /// length field reads the true count instead of trying to derive it from arrayLength().
        /// </summary>
        public bool IsViewCount { get; set; }

        /// <summary>
        /// For IsViewCount entries: the binding index of the associated buffer.
        /// </summary>
        public int ViewCountBindingIndex { get; set; } = -1;

        /// <summary>
        /// True if this entry represents the element offset of a body-struct view field that
        /// participates in a coalesced storage-buffer binding. Unlike IsViewOffset, the value
        /// is NOT a sub-view's element offset of its parent buffer — it is the field's offset
        /// within the SHARED coalesced buffer, computed by the runtime from the running sum
        /// of preceding member field lengths in the same group.
        /// </summary>
        public bool IsCoalesceFieldOffset { get; set; }

        /// <summary>For IsCoalesceFieldOffset entries: the body-struct param index (IR param index).</summary>
        public int CoalesceBodyStructParamIndex { get; set; } = -1;

        /// <summary>For IsCoalesceFieldOffset entries: the body-struct field index within the struct.</summary>
        public int CoalesceFieldIndex { get; set; } = -1;

        /// <summary>Number of u32 slots this scalar occupies (1 for 4-byte, 2 for 8-byte).</summary>
        public int SlotCount => (ByteSize + 3) / 4;
    }
}
