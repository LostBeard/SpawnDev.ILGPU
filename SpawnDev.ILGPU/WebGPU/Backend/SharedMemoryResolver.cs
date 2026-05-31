// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: SharedMemoryResolver.cs
//
// Manages shared memory (var<workgroup>) allocation, matching, and WGSL emission.
// Extracted from WGSLKernelFunctionGenerator and WGSLCodeGenerator (Phase 1.1).
//
// Responsibilities:
// 1. Pre-populate deterministic shared memory variable names from backend context
// 2. Match IR Alloca nodes to pre-registered variables (handling object identity mismatches)
// 3. Emit WGSL var<workgroup> declarations (static, dynamic, synthetic)
// 4. Register matched allocas into the code generator's valueVariables dictionary
//
// The core challenge: ILGPU IR may produce DIFFERENT Alloca object instances for the
// same logical shared allocation (kernel vs. helper function, or backendContext vs.
// method-local Allocas). This class uses a two-pass matching strategy:
//   Pass 1: Match by element type + array size (strict, handles multiple allocations)
//   Pass 2: Fall back to first unassigned entry (single-allocation case)
// ---------------------------------------------------------------------------------------

using System.Text;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Values;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// Describes a pipeline-overridable constant for dynamic shared memory sizing.
    /// Moved from WGSLKernelFunctionGenerator to SharedMemoryResolver (Phase 1.1).
    /// </summary>
    public readonly struct DynamicSharedOverrideInfo
    {
        /// <summary>The WGSL override constant name (e.g. "DYNAMIC_SHARED_SIZE_0").</summary>
        public string ConstantName { get; }

        /// <summary>The WGSL variable name for the shared memory array.</summary>
        public string VariableName { get; }

        /// <summary>The allocation index within ILGPU's dynamic shared allocation list.</summary>
        public int AllocaIndex { get; }

        /// <summary>The size of one element in bytes.</summary>
        public int ElementSize { get; }

        public DynamicSharedOverrideInfo(string constantName, string variableName, int allocaIndex, int elementSize)
        {
            ConstantName = constantName;
            VariableName = variableName;
            AllocaIndex = allocaIndex;
            ElementSize = elementSize;
        }
    }

    /// <summary>
    /// Configuration for synthetic workgroup variables (broadcast, shuffle, group reduce).
    /// Set by the kernel generator after scanning IR for feature usage.
    /// </summary>
    public struct SyntheticWorkgroupConfig
    {
        /// <summary>Whether a broadcast temp variable is needed.</summary>
        public bool UsesBroadcast;

        /// <summary>WGSL type for broadcast temp (e.g., "i32", "f32").</summary>
        public string BroadcastType;

        /// <summary>Whether warp shuffle emulation via shared memory is needed.</summary>
        public bool UsesWarpShuffleEmulation;

        /// <summary>Whether subgroups are used but not available (requires emulation).</summary>
        public bool UsesSubgroupsWithoutHardwareSupport;

        /// <summary>Whether group-level reduction atomics are needed.</summary>
        public bool UsesGroupReduce;
    }

    /// <summary>
    /// Manages shared memory (var&lt;workgroup&gt;) allocation and resolution for WGSL code generation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shared memory in WGSL is declared at module scope as <c>var&lt;workgroup&gt;</c> variables.
    /// The ILGPU IR represents these as <c>Alloca</c> nodes with <c>MemoryAddressSpace.Shared</c>.
    /// </para>
    /// <para>
    /// The mapping from IR Alloca → WGSL variable is complicated by the fact that the same
    /// logical allocation may appear as different object instances in different contexts:
    /// the backend context's <c>SharedAllocations</c>, the kernel method's <c>Allocas</c>,
    /// and each helper method's <c>Allocas</c> may all have distinct <c>Alloca</c> objects
    /// for the same shared buffer.
    /// </para>
    /// </remarks>
    public sealed class SharedMemoryResolver
    {
        /// <summary>
        /// Maps pre-registered Alloca IR values (from backendContext.SharedAllocations) to their
        /// deterministic WGSL variable names and full declarations.
        /// Key: Alloca Value from backend context
        /// Value: (VarName e.g. "shared_0", Declaration e.g. "var&lt;workgroup&gt; shared_0 : array&lt;u32, 256&gt;;")
        /// </summary>
        private readonly Dictionary<Value, (string Name, string Declaration)> _entries;

        /// <summary>
        /// Dynamic shared memory override info collected during emission.
        /// Uses the existing DynamicSharedOverrideInfo struct from WGSLKernelFunctionGenerator.
        /// </summary>
        private readonly List<DynamicSharedOverrideInfo> _dynamicOverrides = new();

        /// <summary>
        /// WGSL variable names of shared (workgroup) arrays whose IR element type is
        /// <c>bool</c> but which are declared as <c>array&lt;i32, N&gt;</c> instead.
        /// WGSL forbids <c>bool</c> in the <c>workgroup</c> address space (bool has no
        /// defined size/footprint), so a <c>var&lt;workgroup&gt; array&lt;bool, N&gt;</c>
        /// declaration fails Tint validation — the whole pipeline then silently fails to
        /// create and the kernel never runs (e.g. ILGPU.Algorithms.UniqueKernel's
        /// <c>SharedMemory.Allocate&lt;bool&gt;</c>). These arrays store 0/1 as i32; the
        /// kernel generator's LoadElementAddress/Load/Store convert at each access site.
        /// </summary>
        public HashSet<string> BoolRemappedVars { get; } = new();

        /// <summary>
        /// Creates a SharedMemoryResolver pre-populated with shared allocations from the backend context.
        /// </summary>
        /// <param name="sharedAllocations">Static shared memory allocations from the backend context.</param>
        /// <param name="typeGenerator">WGSL type generator for element type mapping.</param>
        public SharedMemoryResolver(
            AllocaKindInformation sharedAllocations,
            WGSLTypeGenerator typeGenerator)
        {
            _entries = new Dictionary<Value, (string Name, string Declaration)>();
            int sharedIdx = 0;
            foreach (var alloca in sharedAllocations)
            {
                var name = $"shared_{sharedIdx}";
                var wgslType = typeGenerator[alloca.ElementType];
                // bool is illegal in the workgroup address space — store as i32 (0/1) and
                // convert at the access sites (see BoolRemappedVars docs above).
                if (wgslType == "bool")
                {
                    wgslType = "i32";
                    BoolRemappedVars.Add(name);
                }
                int entryCount = (int)alloca.ArraySize;
                var declaration = $"var<workgroup> {name} : array<{wgslType}, {entryCount}>;";
                _entries[alloca.Alloca] = (name, declaration);
                sharedIdx++;
            }
        }

        /// <summary>
        /// Creates a SharedMemoryResolver from a pre-built entries dictionary.
        /// Used when GeneratorArgs already has SharedMemoryVarNames populated.
        /// </summary>
        internal SharedMemoryResolver(Dictionary<Value, (string Name, string Declaration)> entries)
        {
            _entries = entries;
        }

        /// <summary>
        /// Gets the underlying entries dictionary. Used during the transition period
        /// where GeneratorArgs.SharedMemoryVarNames is still referenced directly.
        /// </summary>
        internal Dictionary<Value, (string Name, string Declaration)> Entries => _entries;

        /// <summary>
        /// Gets the number of static shared memory entries.
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Gets the dynamic shared memory override info collected during emission.
        /// </summary>
        public IReadOnlyList<DynamicSharedOverrideInfo> DynamicOverrides => _dynamicOverrides;

        /// <summary>
        /// Tries to resolve an IR Alloca value to its pre-registered WGSL variable name.
        /// Uses a two-pass matching strategy to handle object identity mismatches.
        /// </summary>
        /// <param name="alloca">The Alloca IR value to resolve.</param>
        /// <param name="registeredNames">
        /// Set of variable names already registered in valueVariables for OTHER alloca instances.
        /// Used to skip entries that are already claimed by a different Alloca.
        /// </param>
        /// <param name="varName">The resolved WGSL variable name, if found.</param>
        /// <returns>True if the alloca was resolved to a shared memory variable.</returns>
        public bool TryResolve(
            Alloca alloca,
            Func<string, Value?, bool> isNameClaimedByOther,
            out string varName)
        {
            varName = "";

            // Direct lookup: exact object reference match
            if (_entries.TryGetValue(alloca, out var entry))
            {
                varName = entry.Name;
                return true;
            }

            // Two-pass matching for object identity mismatches
            string allocaElemStr = alloca.AllocaType.ToString();
            long allocaSize = alloca.ArrayLength.Resolve() is PrimitiveValue pv
                ? pv.Int64Value : -1;

            // Pass 1: Match by element type + array size (strict)
            foreach (var kvp in _entries)
            {
                var (name, declaration) = kvp.Value;
                if (isNameClaimedByOther(name, alloca))
                    continue;
                if (kvp.Key is Alloca candidate
                    && candidate.AllocaType.ToString() == allocaElemStr
                    && candidate.ArrayLength.Resolve() is PrimitiveValue cpv
                    && cpv.Int64Value == allocaSize)
                {
                    varName = name;
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log(
                        $"[SharedMemoryResolver] Matched alloca (hash={alloca.GetHashCode()}, " +
                        $"type={allocaElemStr}, size={allocaSize}) to '{name}' by type+size");
                    return true;
                }
            }

            // Pass 2: Fall back to first unassigned entry (single-allocation case)
            foreach (var kvp in _entries)
            {
                var (name, declaration) = kvp.Value;
                if (isNameClaimedByOther(name, alloca))
                    continue;
                varName = name;
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log(
                    $"[SharedMemoryResolver] Fallback: redirected alloca (hash={alloca.GetHashCode()}) to '{name}'");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Emits all static shared memory var&lt;workgroup&gt; declarations and registers
        /// them in the code generator's valueVariables dictionary.
        /// </summary>
        /// <param name="builder">StringBuilder for WGSL output.</param>
        /// <param name="typeGenerator">Type generator for WGSL type names.</param>
        /// <param name="registerVariable">
        /// Callback to register (Alloca Value, variable name, WGSL type) in valueVariables + declaredVariables.
        /// </param>
        public void EmitStaticDeclarations(
            StringBuilder builder,
            WGSLTypeGenerator typeGenerator,
            Action<Value, string, string> registerVariable)
        {
            foreach (var kvp in _entries)
            {
                var allocaNode = kvp.Key;
                var (varName, declaration) = kvp.Value;

                var wgslType = typeGenerator[allocaNode.Type];
                registerVariable(allocaNode, varName, wgslType);

                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log(
                    $"[SharedMemoryResolver] Emitting: name={varName}, alloca hash={allocaNode.GetHashCode()}");

                builder.AppendLine(declaration);
            }
        }

        /// <summary>
        /// Registers a kernel's own shared allocations (which may be different object instances
        /// from those in the pre-registered entries). Uses the two-pass matching strategy.
        /// </summary>
        /// <param name="kernelSharedAllocations">The kernel method's shared allocations.</param>
        /// <param name="typeGenerator">Type generator for WGSL type names.</param>
        /// <param name="registerVariable">
        /// Callback to register (Alloca Value, variable name, WGSL type) in valueVariables.
        /// </param>
        /// <param name="isNameClaimedByOther">
        /// Predicate: returns true if the given variable name is already registered
        /// for a DIFFERENT Alloca in valueVariables.
        /// </param>
        public void RegisterKernelAllocas(
            AllocaKindInformation kernelSharedAllocations,
            WGSLTypeGenerator typeGenerator,
            Action<Value, string, string> registerVariable,
            Func<string, Value?, bool> isNameClaimedByOther)
        {
            foreach (var alloca in kernelSharedAllocations)
            {
                // Direct lookup first
                if (_entries.TryGetValue(alloca.Alloca, out var preAssigned))
                {
                    var wgslType = typeGenerator[alloca.Alloca.Type];
                    registerVariable(alloca.Alloca, preAssigned.Name, wgslType);
                    continue;
                }

                // Reference mismatch: match by element type + array size
                long allocaArraySize = alloca.ArraySize;
                string allocaElemStr = alloca.ElementType.ToString();
                foreach (var kvp in _entries)
                {
                    if (kvp.Key is not Alloca candidateAlloca) continue;
                    if (candidateAlloca.AllocaType.ToString() != allocaElemStr) continue;
                    if (candidateAlloca.ArrayLength.Resolve() is not PrimitiveValue pv
                        || pv.Int64Value != allocaArraySize)
                        continue;
                    if (isNameClaimedByOther(kvp.Value.Name, alloca.Alloca))
                        continue;

                    var wgslType = typeGenerator[alloca.Alloca.Type];
                    registerVariable(alloca.Alloca, kvp.Value.Name, wgslType);
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log(
                        $"[SharedMemoryResolver] Matched kernel alloca (hash={alloca.Alloca.GetHashCode()}, " +
                        $"size={allocaArraySize}) to '{kvp.Value.Name}' by type+size");
                    break;
                }
            }
        }

        /// <summary>
        /// Registers a helper function's shared memory allocas before inlining.
        /// Tries direct lookup only — relies on Load()'s TryResolve fallback for mismatches.
        /// </summary>
        /// <param name="helperSharedAllocations">The helper method's shared allocations.</param>
        /// <param name="typeGenerator">Type generator for WGSL type names.</param>
        /// <param name="registerVariable">
        /// Callback to register (Alloca Value, variable name, WGSL type) in valueVariables.
        /// </param>
        public void RegisterHelperAllocas(
            AllocaKindInformation helperSharedAllocations,
            WGSLTypeGenerator typeGenerator,
            Action<Value, string, string> registerVariable)
        {
            foreach (var sharedAlloca in helperSharedAllocations)
            {
                if (_entries.TryGetValue(sharedAlloca.Alloca, out var preAssigned))
                {
                    var wgslType = typeGenerator[sharedAlloca.Alloca.Type];
                    registerVariable(sharedAlloca.Alloca, preAssigned.Name, wgslType);
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log(
                        $"[SharedMemoryResolver] Registered helper alloca -> {preAssigned.Name}");
                }
                else
                {
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log(
                        $"[SharedMemoryResolver] WARNING: helper alloca not found " +
                        $"(hash={sharedAlloca.Alloca.GetHashCode()})");
                }
            }
        }

        /// <summary>
        /// Emits dynamic shared memory declarations with pipeline-overridable constants.
        /// </summary>
        /// <param name="dynamicAllocations">Dynamic shared allocations from the backend context.</param>
        /// <param name="builder">StringBuilder for WGSL output.</param>
        /// <param name="typeGenerator">Type generator for WGSL type names.</param>
        /// <param name="loadVariable">
        /// Callback to allocate/load a variable for the alloca and register it.
        /// Returns the variable name.
        /// </param>
        /// <param name="markDeclared">Callback to mark a variable name as declared.</param>
        public void EmitDynamicDeclarations(
            AllocaKindInformation dynamicAllocations,
            StringBuilder builder,
            WGSLTypeGenerator typeGenerator,
            Func<Alloca, string> loadVariable,
            Action<string> markDeclared)
        {
            if (dynamicAllocations.Length == 0)
                return;

            _dynamicOverrides.Clear();
            builder.AppendLine();
            builder.AppendLine("// Dynamic shared memory (sized via pipeline override constants)");

            foreach (var alloca in dynamicAllocations)
            {
                var varName = loadVariable(alloca.Alloca);
                markDeclared(varName);

                var wgslType = typeGenerator[alloca.ElementType];
                int elementSize = alloca.ElementSize;
                string overrideConstName = $"DYNAMIC_SHARED_SIZE_{alloca.Index}";

                builder.AppendLine($"override {overrideConstName} : u32 = 1u;");
                builder.AppendLine($"var<workgroup> {varName} : array<{wgslType}, {overrideConstName}>;");

                var overrideInfo = new DynamicSharedOverrideInfo(
                    constantName: overrideConstName,
                    variableName: varName,
                    allocaIndex: alloca.Index,
                    elementSize: elementSize);
                _dynamicOverrides.Add(overrideInfo);
            }
        }

        /// <summary>
        /// Emits synthetic workgroup variables (broadcast temp, shuffle buffer, group reduce atomics).
        /// </summary>
        /// <param name="config">Configuration describing which synthetic variables are needed.</param>
        /// <param name="builder">StringBuilder for WGSL output.</param>
        public static void EmitSyntheticWorkgroupVariables(
            in SyntheticWorkgroupConfig config,
            StringBuilder builder)
        {
            if (config.UsesBroadcast)
            {
                builder.AppendLine();
                builder.AppendLine("// Workgroup broadcast shared memory");
                builder.AppendLine($"var<workgroup> _broadcast_temp : {config.BroadcastType};");
            }

            if (config.UsesSubgroupsWithoutHardwareSupport || config.UsesWarpShuffleEmulation)
            {
                builder.AppendLine();
                builder.AppendLine("// Warp shuffle emulation shared memory (256 u32 slots = 4 per thread for Ozaki vec4<f32> emulated types)");
                builder.AppendLine("var<workgroup> _warp_shuffle_buf : array<u32, 256>;");
            }

            if (config.UsesGroupReduce)
            {
                builder.AppendLine();
                builder.AppendLine("// Group-level reduction shared memory (for GenerateGroupAllReduce)");
                builder.AppendLine("var<workgroup> _grp_reduce_i32 : atomic<i32>;");
                builder.AppendLine("var<workgroup> _grp_reduce_u32 : atomic<u32>;");
                builder.AppendLine("var<workgroup> _grp_sg_results : array<u32, 64>; // per-subgroup partial results (2 slots per sg for 64-bit types)");
            }
        }

        /// <summary>
        /// Gets shared memory variable names for diagnostic output (shader header comments).
        /// </summary>
        /// <returns>List of variable names in declaration order.</returns>
        public IEnumerable<string> GetVariableNames()
        {
            foreach (var kvp in _entries)
                yield return kvp.Value.Name;
        }
    }
}
