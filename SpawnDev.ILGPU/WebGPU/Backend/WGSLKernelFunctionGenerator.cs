// ---------------------------------------------------------------------------------------
//                                  SpawnDev.ILGPU.WebGPU
//                         Copyright (c) 2024 SpawnDev Project
//
// File: WGSLKernelFunctionGenerator.cs
// Force Update
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends.EntryPoints;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Analyses.ControlFlowDirection;
using global::ILGPU.IR.Analyses.TraversalOrders;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;
using System.Linq;
using System.Text;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    internal sealed class WGSLKernelFunctionGenerator : WGSLCodeGenerator
    {
        #region Pre-compiled Regex Patterns

        // Let-to-var hoisting for inlined helper functions (matches v_N and v_N_suffix names)
        private static readonly System.Text.RegularExpressions.Regex s_inlineLetPattern =
            new(@"^(\s*)let\s+(v_\d+(?:_\w+)?)\s*=\s*(.+);",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

        // Var declaration hoisting to function scope
        private static readonly System.Text.RegularExpressions.Regex s_varHoistPattern =
            new(@"^(\s*)var\s+(v_\d+\w*)\s*:\s*([^;=]+?)\s*(?:=\s*(.+?))?\s*;\s*$",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

        #endregion

        #region Instance

        private HashSet<int> _atomicParameters = new HashSet<int>();
        // Subset of _atomicParameters where the element type is float (f32/f16).
        // WGSL does not support atomic<f32>, so these are declared as atomic<u32> (raw bits)
        // and emulated via a CAS loop in WGSLCodeGenerator.GenerateCode(GenericAtomic).
        private HashSet<int> _floatAtomicParameters = new HashSet<int>();
        // Subset of _atomicParameters where the atomic has unsigned semantics.
        // WGSL's atomicMin/atomicMax on i32 do signed comparisons, so unsigned
        // reductions need atomic<u32> buffers and u32 values.
        private HashSet<int> _unsignedAtomicParameters = new HashSet<int>();
        // Params (or body-struct view fields) that need spinlock companion buffers for
        // i64/f64 Min/Max/Exchange atomics. These require both halves of the emulated
        // 64-bit value to update atomically, which WGSL can't do without a lock.
        // Key: (ParamIdx, FieldIdx). FieldIdx=-1 for direct view params; FieldIdx>=0
        // for a view field of a body struct parameter at that field index.
        private HashSet<(int ParamIdx, int FieldIdx)> _i64SpinlockParams = new HashSet<(int, int)>();
        // Maps spinlock key -> lock buffer binding name (set during GenerateHeader /
        // pre-populated in the constructor so body generation can resolve the name first).
        private Dictionary<(int ParamIdx, int FieldIdx), string> _lockBufferNames = new Dictionary<(int, int), string>();
        private HashSet<Value> _hoistedPrimitives = new HashSet<Value>();
        // Tracks WGSL variable names that hold pointer aliases (e.g., "v_27_f0" which is 'let v_27_f0 = &param1_f0;').
        // The post-processor checks this to avoid converting 'let v_28 = v_27_f0;' to 'v_28 = v_27_f0;'
        // which would be a type error (can't assign a pointer to a 'var' variable in WGSL).
        private HashSet<string> _viewPointerVarNames = new HashSet<string>();
        private HashSet<int> _emulatedF64Params = new HashSet<int>();
        private HashSet<int> _emulatedI64Params = new HashSet<int>();
        // Tracks synthetic uniformity counter variables already declared in this kernel.
        // Prevents WGSL "redeclaration" errors when multiple loops need the same counter.
        private HashSet<string> _declaredSyntheticCounters = new HashSet<string>();
        private List<DynamicSharedOverrideInfo> _dynamicSharedOverrides = new List<DynamicSharedOverrideInfo>();
        private bool _usesBroadcast = false;
        private string _broadcastType = "i32"; // WGSL type for _broadcast_temp (set by ScanForSubgroupAndBroadcastUsage)
        private bool _usesSubgroups = false;
        private bool _usesBarriers = false;
        // Set to true by GenerateGroupAllReduce so the module-scope atomic<i32> workgroup var is emitted.
        // The static handler sets this via the public property to signal the need for _grp_reduce_i32.
        private bool _usesGroupReduce = false;
        /// <summary>Set to true by GenerateGroupAllReduce to request emission of <c>var&lt;workgroup&gt; _grp_reduce_i32: atomic&lt;i32&gt;</c>.</summary>
        public override bool UsesGroupReduce { get => _usesGroupReduce; set => _usesGroupReduce = value; }
        // Tracks view params with sub-word element types (1=byte, 2=short/half).
        // WGSL declares these as array<atomic<u32>> but element access must extract sub-word values.
        private Dictionary<int, int> _subWordParams = new Dictionary<int, int>(); // paramIndex -> elementByteSize
        // Float16 sub-word params need f16-to-f32 conversion instead of sign extension
        private HashSet<int> _subWordFloat16Params = new HashSet<int>();
        private HashSet<int> _subWordBFloat16Params = new HashSet<int>();
        // Unsigned sub-word params need zero-extension instead of sign extension
        private HashSet<int> _subWordUnsignedParams = new HashSet<int>();
        // Maps LEA variable names to their sub-word param index (for Load/Store extraction)
        private Dictionary<string, int> _subWordLEAVars = new Dictionary<string, int>();
        // For body-struct view-field LEAs, the WGSL binding name is `param{outerN}_f{fieldIdx}`,
        // not `param{N}`. The sub-word Load/Store codegen (which assumes `param{idx}`) needs the
        // explicit binding name. Populated alongside _subWordLEAVars when we hit a body-struct
        // sub-word view field LEA; the Load/Store paths look up this map and use the value as
        // the binding identifier.
        private Dictionary<int, string> _subWordBodyStructBindingNames = new Dictionary<int, string>();
        // Maps variable names to their emulation info (param index, body-struct field index, is emu_f64).
        // FieldIdx=-1 for direct view-param access; FieldIdx>=0 for body-struct view-field access.
        // The spinlock lookup uses (ParamIndex, FieldIdx) as the key so body-struct view fields
        // get their own lock buffer separate from direct-param locks.
        private Dictionary<string, (int ParamIndex, int FieldIdx, bool IsF64)> _emulatedVarMappings = new Dictionary<string, (int, int, bool)>();
        // Maps cross-block pointer variable names to inline expressions (e.g. "param1[v_3_idx]")
        // This fixes WGSL scoping: pointers declared in one switch case are not visible in another.
        private Dictionary<string, string> _crossBlockPointerExprs = new Dictionary<string, string>();
        // Set of LoadElementAddress Values that cross block boundaries
        private HashSet<Value> _crossBlockPointers = new HashSet<Value>();
        // Tracks `let` bindings already emitted (e.g. shared memory pointers from NewView).
        // SEPARATE from declaredVariables to avoid poisoning Declare() — see NV1080 audit.
        private readonly HashSet<string> _emittedLetBindings = new HashSet<string>();
        // Counter for unique inlining namespaces — each inlining gets a unique offset
        // to avoid variable name collisions with kernel-scope declarations.
        private int _inlineCounter = 0;
        // Scalar packing manifest — populated during GenerateHeader(), used by SetupParameterBindings()
        private List<ScalarPackingEntry> _scalarManifest = new List<ScalarPackingEntry>();
        // View offset scalar slots — maps param.Index → slot index in _scalar_params
        // where the element offset for that buffer binding is stored.
        // Used by GetField to read ArrayView Field 1 (Index) from _scalar_params instead of hardcoding 0.
        private Dictionary<int, int> _viewOffsetScalarSlots = new Dictionary<int, int>();
        // Tracked during body generation: set true when any _scalar_params[ reference is emitted.
        // Avoids expensive Builder.ToString().Contains() call in GenerateHeader().
        private bool _bodyReferencesScalarParams = false;
        private CFG<ReversePostOrder, Forwards> _cfg;
        private Dominators<Backwards> _postDominators;
        private Loops<ReversePostOrder, Forwards> _loops;
        private GeneratorArgs _generatorArgs;

        // --- Body Struct Decomposition ---
        // A "body struct" is a struct parameter (e.g. IGridStrideKernelBody implementations like
        // ReductionImplementation) that contains ArrayView fields mixed with scalar fields.
        // WGSL cannot represent such a struct as a single buffer binding, so we decompose it:
        // - Each view field gets its own storage buffer binding: param{N}_f{fieldIdx}
        // - Scalar fields are packed into _scalar_params
        //
        // _bodyStructParams: paramIndex → list of field info
        // _bodyStructAtomicFields: (paramIndex, fieldIndex) → true if this view field is atomic
        // _bodyStructAtomicFloatFields: (paramIndex, fieldIndex) → true if float atomic
        private Dictionary<int, List<BodyStructFieldInfo>> _bodyStructParams = new Dictionary<int, List<BodyStructFieldInfo>>();
        // _bodyStructOutputFields: (paramIndex, fieldIndex) → true if this view field
        // is the target of any Store / GenericAtomic in the kernel or any helper it
        // calls. Output fields must be excluded from coalesce because the host doesn't
        // copy back from the coalesced buffer to the original output ArrayView's GPU
        // storage.
        private HashSet<(int paramIdx, int fieldIdx)> _bodyStructOutputFields = new HashSet<(int, int)>();
        private HashSet<(int paramIdx, int fieldIdx)> _bodyStructAtomicFields = new HashSet<(int, int)>();
        private HashSet<(int paramIdx, int fieldIdx)> _bodyStructAtomicFloatFields = new HashSet<(int, int)>();
        private HashSet<(int paramIdx, int fieldIdx)> _bodyStructUnsignedAtomicFields = new HashSet<(int, int)>();
        // Subset of _bodyStructAtomicFields where the element type is emulated 64-bit (emu_i64, emu_u64, emu_f64).
        // WGSL doesn't support atomic on vec2<u32>, so these buffers use plain storage and
        // the reduction's final write uses a plain store instead of an atomic operation.
        private HashSet<(int paramIdx, int fieldIdx)> _bodyStructEmulated64AtomicFields = new HashSet<(int, int)>();

        // Deferred var declarations for structured multi-block code gen.
        // In WGSL, 'var' inside if/else/loop is block-scoped, so declaring inside
        // one branch makes the variable invisible to sibling branches. We collect
        // declarations here and insert them at function scope after code gen.
        private readonly List<string> _deferredVarDeclarations = new List<string>();
        private bool _useDeferredDeclarations = false;
        // Set to true during GenerateCode() for scan/sort kernels to enable
        // block-level WGSL comments and structured code gen tracing.
        private bool _isScanKernel = false;
        // Unique-per-loop sentinel id for the two-pass uniform-break resolution (GenerateLoopConstruct).
        private int _ufBreakSentinelCounter = 0;

        /// <summary>
        /// Describes one field of a decomposed body struct parameter.
        /// </summary>
        private sealed class BodyStructFieldInfo
        {
            public int FieldIndex { get; init; }
            public TypeNode FieldType { get; init; } = null!;
            public bool IsView { get; init; }   // true → gets its own buffer binding
            public bool IsScalar { get; init; } // true → packed into _scalar_params OR is view metadata
            // For view fields: the WGSL binding name (e.g. "param1_f0")
            public string BindingName { get; set; } = "";
            // For scalar fields: the slot index in _scalar_params (-1 if view metadata)
            public int ScalarSlot { get; set; } = -1;
            // True if this scalar field is metadata of a preceding view field (e.g. ArrayView1D.Length)
            // These fields use arrayLength(&bindingName) instead of _scalar_params
            public bool IsViewMetadata { get; set; } = false;
            // For IsViewMetadata fields: the binding name of the associated view field
            public string AssociatedViewBindingName { get; set; } = "";
            // True if this is a length field (Int64 following a view), false if flag (Int8 following a view)
            public bool IsLengthField { get; set; } = false;
            // For view fields: the scalar slot index for the view offset (-1 if none)
            // Populated during GenerateHeader so LoadElementAddress can inject _scalar_params[slot]
            public int ViewOffsetSlot { get; set; } = -1;
            // For packed-struct view fields: the scalar slot index for the element count (-1 if none)
            // Used by the length field generator to read the true element count from the CPU.
            public int ViewCountSlot { get; set; } = -1;
        }
        // Coalesce-group manifest: one entry per group of view fields that share a
        // storage-buffer binding. Exported to the runtime via WebGPUCompiledKernel.
        // The data type is the PUBLIC CoalesceGroupEntry on SpawnDev.ILGPU.WebGPU.Backend so
        // it can flow through CompiledKernel to the runtime accelerator. Each entry is either
        // a body-struct group (IsDirectParam=false; MemberFieldIndices used) or a direct-param
        // group (IsDirectParam=true; MemberDirectParamIndices used). Same dispatch path
        // concatenates members and binds once.
        // Maintained as a side-car (not on BodyStructFieldInfo) because GenerateHeader rebuilds
        // _bodyStructParams; side-car data survives that rebuild.
        private List<CoalesceGroupEntry> _coalesceGroups = new();
        // Lookup: (paramIdx, fieldIdx) → which group this body-struct field belongs to.
        private Dictionary<(int paramIdx, int fieldIdx), CoalesceGroupEntry> _coalesceMembership = new();
        // Subset: (paramIdx, fieldIdx) keys that are body-struct LEADERS — only leaders emit a @binding declaration.
        private HashSet<(int paramIdx, int fieldIdx)> _coalesceLeaders = new();

        // Direct-param coalesce — same pattern keyed by IR parameter index instead of (paramIdx, fieldIdx).
        // (rc.16 direct-param coalesce, 2026-05-05.)
        private Dictionary<int, CoalesceGroupEntry> _directParamCoalesceMembership = new();
        private HashSet<int> _directParamCoalesceLeaders = new();
        // Direct-param outputs (kernel writes to them); excluded from coalesce.
        // Populated by ScanForBodyStructOutputs alongside _bodyStructOutputFields.
        private HashSet<int> _directParamOutputs = new HashSet<int>();
        // Per-direct-param scalar slot holding the u32-slot offset within the coalesced buffer.
        // Set during DecideCoalesceGroups → consumed by GenerateHeader (allocates the scalar slot)
        // and LoadElementAddress emit (which references _scalar_params[slot] in the address expression).
        private Dictionary<int, int> _directParamCoalesceViewOffsetSlots = new();
        // _bodyStructFieldVars: (paramIndex, fieldIndex) → WGSL variable name
        // Used by GetField code generation to redirect field accesses to the appropriate binding.
        private Dictionary<(int, int), string> _bodyStructFieldVars = new Dictionary<(int, int), string>();

        // --- Packed Struct Support ---
        // When a view's element type is a struct containing emulated 64-bit fields (emu_f64, emu_i64, emu_u64),
        // the WGSL struct's std430 alignment may differ from the CPU struct's layout. For example:
        //   RadixSortPair<double, int> on CPU: 8 (double) + 4 (int) = 12 bytes
        //   In WGSL with Ozaki emu_f64=vec4<f32>: stride = roundup(16, 16+4) = 32 bytes  ← mismatch!
        // Fix: bind such struct arrays as array<u32> using the CPU field layout (packed), so the shader
        // uses the same byte offsets as the host allocator.
        private struct PackedStructFieldInfo
        {
            public string WgslType;      // e.g. "emu_f64", "emu_i64", "i32", "f32", etc.
            public int U32Offset;        // offset in u32 units from element start
            public int U32Count;         // number of u32s this field occupies
            public bool IsEmuF64;        // field maps to emu_f64 (f64_to/from_ieee754_bits needed)
            public bool IsEmuI64OrU64;   // field maps to emu_i64 or emu_u64
        }
        // Maps param index → packed field layout (for regular view params with struct+emu element type)
        private Dictionary<int, List<PackedStructFieldInfo>> _packedStructLayouts = new();
        // Maps (bsParamIdx, fieldIdx) → packed field layout (for body-struct view fields with struct+emu element type)
        private Dictionary<(int, int), List<PackedStructFieldInfo>> _packedStructBSFieldLayouts = new();
        // Maps LEA result variable name → (bufferBindingName, fieldLayout)
        // Populated during LoadElementAddress, consumed by Load and Store.
        private Dictionary<string, (string BindingName, List<PackedStructFieldInfo> Layout)> _packedStructLEAVars = new();

        /// <summary>
        /// Names of LoadElementAddress result pointers that index a bool-remapped shared
        /// (workgroup) array — i.e. <c>&amp;shared_N[i]</c> where shared_N is declared
        /// <c>array&lt;i32, N&gt;</c> in place of the illegal <c>array&lt;bool, N&gt;</c>
        /// (see SharedMemoryResolver.BoolRemappedVars). Load/Store of these pointers convert
        /// between WGSL <c>bool</c> and the stored <c>i32</c> 0/1 representation.
        /// </summary>
        private readonly HashSet<string> _boolSharedPtrs = new();

        /// <summary>
        /// True when <paramref name="ptr"/> is an IR pointer into shared (workgroup) memory
        /// whose element type is <c>bool</c>. Such arrays are declared <c>array&lt;i32,N&gt;</c>
        /// (WGSL forbids workgroup bool), so Load/Store must convert i32 0/1 ↔ WGSL bool. This
        /// is the IR-type-based detection that survives pointer casts (PointerCast/AddressSpaceCast),
        /// which the name-based <see cref="_boolSharedPtrs"/> set does not always track.
        /// </summary>
        private bool IsBoolSharedPointer(Value ptr)
        {
            return ptr.Type is global::ILGPU.IR.Types.PointerType pt
                && pt.AddressSpace == MemoryAddressSpace.Shared
                && TypeGenerator[pt.ElementType] == "bool";
        }
        // Maps LoadFieldAddress result var name → (bindingName, field info) for NON-emulated scalar fields
        // (emu_f64/emu_i64 fields from LoadFieldAddress are registered in _emulatedVarMappings instead)
        private Dictionary<string, (string BindingName, PackedStructFieldInfo FieldInfo)> _packedScalarFieldPtrs = new();

        public WGSLKernelFunctionGenerator(
            in GeneratorArgs args,
            Method method,
            Allocas allocas)
            : base(args, method, allocas)
        {
            _generatorArgs = args;
            EntryPoint = args.EntryPoint;
            DynamicSharedAllocations = args.DynamicSharedAllocations;
            _cfg = method.Blocks.CreateCFG();
            _postDominators = _cfg.Blocks.CreatePostDominators();
            _loops = _cfg.CreateLoops();

            ScanForAtomicUsage();
            // Pre-populate lock buffer names immediately after scan, BEFORE body generation.
            // GenerateHeader creates these names too (for WGSL emission), but body generation
            // runs first and needs the names to exist when it encounters GenericAtomic nodes.
            foreach (var key in _i64SpinlockParams)
            {
                _lockBufferNames[key] = key.FieldIdx >= 0
                    ? $"_lock_bs{key.ParamIdx}_f{key.FieldIdx}"
                    : $"_lock_param{key.ParamIdx}";
            }
            // CRITICAL: ScanBodyStructParams must run before body generation
            // because SetupParameterBindings (called during body generation) needs _bodyStructParams.
            // GenerateHeader runs AFTER body generation, so we cannot rely on GenerateHeader
            // to populate _bodyStructParams.
            ScanBodyStructParams();
            // Identify body-struct view fields that are kernel OUTPUTS (target of any
            // Store / GenericAtomic in the kernel or any helper it calls). Output
            // fields must NOT be coalesced — coalesce concatenates input data into a
            // shared buffer and binds once; output writes within the kernel land in
            // that shared buffer, NOT in the original output ArrayView's GPU storage.
            // Without this exclusion, Tests23_RegisterHeavyBody_UnitExtent_NoLaunchFailure
            // returns 0 instead of 5194 — kernel writes Out[0]=sum into the coalesced
            // input buffer instead of into bOut's actual storage.
            ScanForBodyStructOutputs();
            // After scan, decide whether this kernel exceeds the device's binding limit and
            // needs coalescing. Mutates BodyStructFieldInfo.BindingName + IsCoalesceMember/Leader
            // so the rest of the codegen path emits one binding per group instead of one per field.
            DecideCoalesceGroups();

            // Bug D phase 7 (2026-05-05): scan kernel IR for MethodCall nodes and record
            // observed address spaces for each ViewType arg passed to a NoInlining helper.
            // Helpers are emitted in PARALLEL with the kernel via WGSLFunctionGenerator;
            // ILGPU IR doesn't propagate `AddressSpace.Shared` into helper parameter types
            // at our optimization level, so the helper's signature defaults to
            // `ptr<storage, ...>` even when the kernel passes a workgroup pointer
            // (Naga rejects the type mismatch at the call site). This scan runs in the
            // kernel constructor (sequential, before parallel GenerateCode) and writes
            // into the shared `_args.HelperParamAddressSpaces` dict — the helper's
            // GenerateHeaderStub reads from it to override the WGSL ptr type.
            ScanCallSiteAddressSpaces();

            if (!EntryPoint.IsExplicitlyGrouped && method.Parameters.Count > 0)
            {
                var indexParam = method.Parameters[0];
                foreach (var block in method.Blocks)
                {
                    foreach (var entry in block)
                    {
                        if (entry.Value is Store store && store.Value == indexParam)
                        {
                            _indexParameterAddress = store.Target;
                            goto FoundIndexParam;
                        }
                    }
                }
            FoundIndexParam:;
            }
        }

        private void ScanForAtomicUsage()
        {
            // Scan the kernel's own blocks for atomic operations
            ScanBlocksForAtomics(Method.Blocks, paramIndex => paramIndex);

            // Also scan called helper functions recursively
            var visited = new HashSet<global::ILGPU.IR.Method>();
            ScanMethodCallsRecursive(Method, paramIndex => paramIndex, visited);
        }

        /// <summary>
        /// Walk all Store / GenericAtomic ops in the kernel (and all helper methods it
        /// calls) and identify body-struct view fields that are the WRITE TARGET. These
        /// fields cannot participate in coalesce — the coalesce path concatenates input
        /// data into a shared buffer at host-time and binds it once; output writes
        /// within the kernel land in that shared buffer rather than in the original
        /// output ArrayView's GPU storage. The host doesn't copy back, so the output
        /// buffer stays at zero. Surfaced 2026-05-05 by Tests23_RegisterHeavyBody —
        /// 12 ArrayView<int> fields plus an Out field, all coalesced together, kernel
        /// writes Out[0]=sum into the shared buffer instead of bOut.
        /// </summary>
        /// <summary>
        /// Scans the kernel IR (and recursively any reachable NoInlining helpers) for
        /// `MethodCall` nodes and records the observed `MemoryAddressSpace` for each
        /// `ViewType` argument passed to non-inlined helpers. Populates
        /// <c>_args.HelperParamAddressSpaces[(callee, paramIdx)]</c> when the arg's
        /// IR Type is `AddressSpaceType.Shared` / `Local` (i.e. workgroup or function
        /// memory). The helper's `GenerateHeaderStub` reads this dict to override the
        /// WGSL ptr storage class — without it, ILGPU's IR doesn't propagate
        /// `Shared` into the helper's parameter type and the signature defaults to
        /// `ptr&lt;storage, ...&gt;` which Naga rejects when the caller passes
        /// `ptr&lt;workgroup, ...&gt;`. (Bug D phase 7, 2026-05-05.)
        /// </summary>
        private void ScanCallSiteAddressSpaces()
        {
            var visited = new HashSet<global::ILGPU.IR.Method>();
            ScanCallSiteAddressSpacesInternal(Method, visited);
        }

        private void ScanCallSiteAddressSpacesInternal(
            global::ILGPU.IR.Method m,
            HashSet<global::ILGPU.IR.Method> visited)
        {
            if (!visited.Add(m)) return;
            foreach (var block in m.Blocks)
            {
                foreach (var entry in block)
                {
                    if (entry.Value is not MethodCall call) continue;
                    var callee = call.Target;
                    // Inline helpers don't get a fn-def signature — only NoInline
                    // helpers need the address space override.
                    if (_generatorArgs.HelperMethods.ContainsKey(callee)) continue;
                    if (callee.HasFlags(global::ILGPU.IR.MethodFlags.External)
                        || callee.HasFlags(global::ILGPU.IR.MethodFlags.Intrinsic))
                        continue;

                    // Coalesce-aware helper sig (local.15+): track which arg positions
                    // share a coalesced leader binding so the helper can collapse same-group
                    // ptr args into 1 leader + N offset args. Builds a per-call group map
                    // first, then promotes each (callee, argIdx) entry into the shared dict.
                    var perCallGroups = new Dictionary<string, List<int>>(); // groupKey -> [argIdx1, argIdx2, ...]
                    var perCallEntryStaging = new Dictionary<int, HelperParamCoalesceEntry>();

                    for (int argIdx = 0; argIdx < call.Count; argIdx++)
                    {
                        var arg = call.Nodes[argIdx].Resolve();
                        if (arg.Type is global::ILGPU.IR.Types.AddressSpaceType ast)
                        {
                            var argAddrSpace = ast.AddressSpace;
                            // Only record non-Generic / non-Global — those are the
                            // cases where the helper signature default ("storage")
                            // would mismatch the caller's actual ptr type. Generic
                            // and Global both map to "storage" in WGSL so the default
                            // is correct.
                            if (argAddrSpace == global::ILGPU.IR.MemoryAddressSpace.Shared
                                || argAddrSpace == global::ILGPU.IR.MemoryAddressSpace.Local)
                            {
                                var key = (callee, argIdx);
                                // First-write-wins; if multiple call sites pass different
                                // address spaces for the same param, the first observed
                                // wins (subsequent calls with mismatched address space
                                // would need helper monomorphization, deferred to v8).
                                if (!_generatorArgs.HelperParamAddressSpaces.ContainsKey(key))
                                    _generatorArgs.HelperParamAddressSpaces[key] = argAddrSpace;
                            }
                        }

                        // Coalesce-aware sig scan. Resolve the arg to a kernel Parameter (if any).
                        // If the parameter is a member of a direct-param coalesce group, stage
                        // a HelperParamCoalesceEntry. After the loop, designate the FIRST member
                        // per group as the leader (gets the leader ptr arg) and copy all entries
                        // into _generatorArgs.HelperParamCoalesceInfo.
                        var argParam = ResolveToParameter(arg);
                        if (argParam != null
                            && _directParamCoalesceMembership.TryGetValue(argParam.Index, out var dpGroup))
                        {
                            int voSlot = _viewOffsetScalarSlots.TryGetValue(argParam.Index, out var slot) ? slot : -1;
                            // Element WGSL type for the leader binding (e.g. "atomic<u32>" for sub-word,
                            // "i32"/"u32"/"f32" for v1).
                            string leaderElemWgslType = dpGroup.BindingWgslType;
                            perCallEntryStaging[argIdx] = new HelperParamCoalesceEntry
                            {
                                GroupKey = dpGroup.BindingName,
                                LeaderBindingName = dpGroup.BindingName,
                                LeaderBindingWgslElementType = leaderElemWgslType,
                                IsLeader = false, // promoted below
                                OffsetScalarSlot = voSlot,
                                IsSubWord = dpGroup.IsSubWord,
                                SubWordElementByteSize = dpGroup.SubWordElementByteSize,
                            };
                            if (!perCallGroups.TryGetValue(dpGroup.BindingName, out var groupList))
                            {
                                groupList = new List<int>();
                                perCallGroups[dpGroup.BindingName] = groupList;
                            }
                            groupList.Add(argIdx);
                        }
                    }

                    // Designate the first arg in each group as the leader and promote all
                    // staged entries into the shared HelperParamCoalesceInfo dict.
                    foreach (var kvp in perCallGroups)
                    {
                        if (kvp.Value.Count == 0) continue;
                        int leaderArgIdx = kvp.Value[0];
                        perCallEntryStaging[leaderArgIdx].IsLeader = true;
                    }
                    foreach (var coalEntry in perCallEntryStaging)
                    {
                        var coalKey = (callee, coalEntry.Key);
                        if (!_generatorArgs.HelperParamCoalesceInfo.ContainsKey(coalKey))
                            _generatorArgs.HelperParamCoalesceInfo[coalKey] = coalEntry.Value;
                    }
                    // Recurse into the callee's body so transitive calls are also
                    // scanned (a kernel-side helper passing the workgroup view to
                    // another helper still needs the second-level signature override).
                    ScanCallSiteAddressSpacesInternal(callee, visited);
                }
            }
        }

        private void ScanForBodyStructOutputs()
        {
            ScanBlocksForBodyStructOutputs(Method.Blocks);

            var visited = new HashSet<global::ILGPU.IR.Method>();
            ScanMethodCallsForBodyStructOutputs(Method, visited);
        }

        private void ScanBlocksForBodyStructOutputs(IEnumerable<global::ILGPU.IR.BasicBlock> blocks)
        {
            foreach (var block in blocks)
            {
                foreach (var entry in block)
                {
                    Value? target = null;
                    if (entry.Value is global::ILGPU.IR.Values.Store store)
                        target = store.Target;
                    else if (entry.Value is global::ILGPU.IR.Values.GenericAtomic atomic)
                        target = atomic.Target;
                    else if (entry.Value is global::ILGPU.IR.Values.AtomicCAS cas)
                        target = cas.Target;
                    if (target == null) continue;

                    if (ResolveToParameterWithFieldChain(target, out var param, out var fieldIdx)
                        && param != null)
                    {
                        if (fieldIdx >= 0
                            && _bodyStructParams.TryGetValue(param.Index, out var bsFields)
                            && fieldIdx < bsFields.Count
                            && bsFields[fieldIdx].IsView)
                        {
                            _bodyStructOutputFields.Add((param.Index, fieldIdx));
                        }
                        else if (fieldIdx < 0)
                        {
                            // Direct ArrayView parameter that is the target of a write.
                            // Excluded from direct-param coalesce by DecideCoalesceGroups
                            // because the runtime concatenation path doesn't write back to
                            // each member's individual GPU buffer (mirrors body-struct
                            // output exclusion, rc.27). (rc.16 direct-param coalesce,
                            // 2026-05-05.)
                            _directParamOutputs.Add(param.Index);
                        }
                    }
                }
            }
        }

        private void ScanMethodCallsForBodyStructOutputs(
            global::ILGPU.IR.Method method,
            HashSet<global::ILGPU.IR.Method> visited)
        {
            if (!visited.Add(method)) return;

            // Scan blocks of this method
            ScanBlocksForBodyStructOutputs(method.Blocks);

            foreach (var block in method.Blocks)
            {
                foreach (var entry in block)
                {
                    if (entry.Value is global::ILGPU.IR.Values.MethodCall methodCall)
                        ScanMethodCallsForBodyStructOutputs(methodCall.Target, visited);
                }
            }
        }

        /// <summary>
        /// Recursively walks MethodCall nodes in a method, scanning each called helper
        /// for atomic operations and mapping helper parameters back to kernel parameters.
        /// Handles multi-level call chains (kernel -> helper -> sub-helper -> atomic).
        /// </summary>
        private void ScanMethodCallsRecursive(
            global::ILGPU.IR.Method method,
            Func<int, int> parentParamMapper,
            HashSet<global::ILGPU.IR.Method> visited)
        {
            if (!visited.Add(method)) return; // avoid cycles

            foreach (var block in method.Blocks)
            {
                foreach (var entry in block)
                {
                    if (entry.Value is global::ILGPU.IR.Values.MethodCall methodCall)
                    {
                        var targetMethod = methodCall.Target;

                        // Build mapping: target method param index -> kernel param index
                        // MethodCall arguments correspond to the target method's parameters
                        var paramMap = new Dictionary<int, int>();
                        int targetParamOffset = 0;
                        var targetParams = targetMethod.Parameters.ToArray();

                        for (int argIdx = 0; argIdx < methodCall.Count && argIdx < targetParams.Length; argIdx++)
                        {
                            var arg = methodCall[argIdx];
                            int targetParamIdx = targetParams[argIdx].Index;

                            // Try to resolve the argument back to a parameter in the parent method
                            if (ResolveToParameterWithFieldChain(arg, out var parentParam, out _))
                            {
                                // Map through parent's mapper to get kernel param index
                                int kernelIdx = parentParamMapper(parentParam!.Index);
                                if (kernelIdx >= 0)
                                    paramMap[targetParamIdx] = kernelIdx;
                            }
                        }

                        if (paramMap.Count > 0)
                        {
                            Func<int, int> childMapper = helperParamIdx =>
                                paramMap.TryGetValue(helperParamIdx, out var kernelIdx) ? kernelIdx : -1;

                            // Scan this helper's blocks for atomics
                            ScanBlocksForAtomics(targetMethod.Blocks, childMapper);

                            // Recurse into sub-helpers
                            ScanMethodCallsRecursive(targetMethod, childMapper, visited);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Scans IR blocks for atomic operations and records which parameters need
        /// special handling (float atomics, unsigned atomics, i64/f64 spinlocks).
        /// The paramMapper converts a parameter index from the scanned method's scope
        /// to the kernel's parameter index. For the kernel itself, this is identity.
        /// For helper functions, it maps through the call site arguments.
        /// </summary>
        private void ScanBlocksForAtomics(
            IEnumerable<global::ILGPU.IR.BasicBlock> blocks,
            Func<int, int> paramMapper)
        {
            foreach (var block in blocks)
            {
                foreach (var entry in block)
                {
                    if (entry.Value is global::ILGPU.IR.Values.GenericAtomic atomic)
                    {
                        // Resolve the atomic target to a parameter, tracking GetField chain
                        if (ResolveToParameterWithFieldChain(atomic.Target, out var param, out var fieldIdx))
                        {
                            int kernelParamIdx = paramMapper(param!.Index);
                            if (kernelParamIdx < 0) continue; // couldn't map to kernel param

                            // Emulated 64-bit Min/Max/Exchange atomics need a spinlock companion
                            // buffer regardless of whether the target is a direct param or a
                            // body-struct view field — both end up doing dual-u32 CAS loops.
                            var valType = TypeGenerator[atomic.Value.Type];
                            bool isEmu64 = valType == "emu_i64" || valType == "emu_u64" || valType == "emu_f64";
                            bool needsLock = atomic.Kind == global::ILGPU.IR.Values.AtomicKind.Min
                                || atomic.Kind == global::ILGPU.IR.Values.AtomicKind.Max
                                || atomic.Kind == global::ILGPU.IR.Values.AtomicKind.Exchange
                                || (valType == "emu_f64" && atomic.Kind == global::ILGPU.IR.Values.AtomicKind.Add);

                            if (fieldIdx >= 0)
                            {
                                // Atomic on a field of a body struct - track the specific field
                                _bodyStructAtomicFields.Add((kernelParamIdx, fieldIdx));
                                var elemType = atomic.Value.Type;
                                if (elemType is global::ILGPU.IR.Types.PrimitiveType pt &&
                                    (pt.BasicValueType == global::ILGPU.BasicValueType.Float32 ||
                                     pt.BasicValueType == global::ILGPU.BasicValueType.Float16))
                                    _bodyStructAtomicFloatFields.Add((kernelParamIdx, fieldIdx));
                                if (atomic.IsUnsigned)
                                    _bodyStructUnsignedAtomicFields.Add((kernelParamIdx, fieldIdx));
                                if (isEmu64 && needsLock)
                                    _i64SpinlockParams.Add((kernelParamIdx, fieldIdx));
                            }
                            else
                            {
                                // Atomic directly on a parameter
                                _atomicParameters.Add(kernelParamIdx);
                                var elemType = atomic.Value.Type;
                                if (elemType is global::ILGPU.IR.Types.PrimitiveType pt &&
                                    (pt.BasicValueType == global::ILGPU.BasicValueType.Float32 ||
                                     pt.BasicValueType == global::ILGPU.BasicValueType.Float16))
                                    _floatAtomicParameters.Add(kernelParamIdx);
                                if (atomic.IsUnsigned)
                                    _unsignedAtomicParameters.Add(kernelParamIdx);
                                if (isEmu64 && needsLock)
                                    _i64SpinlockParams.Add((kernelParamIdx, -1));
                            }
                        }
                    }
                    else if (entry.Value is global::ILGPU.IR.Values.AtomicCAS cas)
                    {
                        if (ResolveToParameterWithFieldChain(cas.Target, out var param, out var fieldIdx))
                        {
                            int kernelParamIdx = paramMapper(param!.Index);
                            if (kernelParamIdx < 0) continue;

                            if (fieldIdx >= 0)
                                _bodyStructAtomicFields.Add((kernelParamIdx, fieldIdx));
                            else
                                _atomicParameters.Add(kernelParamIdx);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Pre-populates _bodyStructParams by classifying struct parameters as body structs.
        /// MUST be called from the constructor, before body generation and SetupParameterBindings.
        /// GenerateHeader runs AFTER body generation in ILGPU's pipeline, so we cannot rely on
        /// GenerateHeader to populate _bodyStructParams. We do the field classification here so
        /// SetupParameterBindings can correctly handle body struct parameters.
        /// </summary>
        private void ScanBodyStructParams()
        {
            int paramOffset = KernelParamOffset;
            // Track global scalar slot offset across all params (as GenerateHeader does)
            int globalScalarSlotOffset = 0;

            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;

                var elementType = GetBufferElementType(param.ParameterType);
                var wgslType = TypeGenerator[elementType];
                bool isEmuF64 = Backend.EnableF64Emulation && wgslType == "emu_f64";
                bool isEmuI64 = Backend.EnableI64Emulation && (wgslType == "emu_i64" || wgslType == "emu_u64");

                // Body struct detection: structs containing view fields (e.g., ReductionImplementation,
                // InitializerImplementation) must be decomposed into individual bindings.
                //
                // Challenge: ArrayView2D/3D are also structs with view fields but should NOT be decomposed.
                // They use the IsMultiDim / view binding path which matches the runtime's binding layout.
                //
                // Solution: For grid-stride kernels (KernelParamOffset == 0), ALL struct params with view
                // fields are body structs — grid-stride kernels never receive raw ArrayView2D/3D params.
                // For stream kernels (KernelParamOffset > 0), use IsMultiDim to distinguish view wrappers.
                IsMultiDim(param.ParameterType, out var earlyIsMultiDimInit, out _, out _, out _, out _);
                if (param.ParameterType is global::ILGPU.IR.Types.StructureType bodyStructType
                    && IsBodyStruct(bodyStructType)
                    && (KernelParamOffset == 0 || !earlyIsMultiDimInit))
                {
                    var fieldInfos = new List<BodyStructFieldInfo>();
                    string lastViewBindingName = "";
                    int viewMetadataPhase = 0; // 0=idle, 1=after view ptr, 2=after Int64 (expect optional Int8)

                    for (int fi = 0; fi < bodyStructType.NumFields; fi++)
                    {
                        var fieldType = bodyStructType.Fields[fi];
                        bool fieldIsView = IsViewFieldType(fieldType);
                        // Capture before any reset
                        string capturedViewBindingName = lastViewBindingName;

                        // State machine for view metadata detection (see GenerateHeader for full comments)
                        bool isViewMetadata = false;
                        bool isLengthField = false;
                        if (fieldIsView)
                        {
                            viewMetadataPhase = 1;
                        }
                        else if (viewMetadataPhase == 1)
                        {
                            string typeStr = fieldType.ToString();
                            // Only mark as view-length metadata if:
                            // 1. The type is Int64 (view lengths are always Int64), AND
                            // 2. This is NOT the last field in the struct.
                            // Reason: ILGPU's DCE can remove the Output view's Length field when it's
                            // never accessed (e.g., Finish() only writes to Output[0], never reads Output.Length).
                            // When DCE removes Output.Length, the ReducedValue field (e.g., long/ulong) becomes
                            // the field immediately after the Output view ptr — and ReducedValue can also be
                            // Int64! With no next field following it, we can reliably distinguish:
                            // - If last field: it's ReducedValue (user scalar), NOT Output.length
                            // - If not last field: it's the view length (some field follows it)
                            bool isLastField = (fi == bodyStructType.NumFields - 1);
                            if (typeStr.Contains("Int64") && !isLastField)
                            {
                                isViewMetadata = true;
                                isLengthField = true;
                                viewMetadataPhase = 2;
                            }
                            else
                            {
                                viewMetadataPhase = 0;
                                lastViewBindingName = "";
                            }
                        }
                        else if (viewMetadataPhase == 2)
                        {
                            string typeStr = fieldType.ToString();
                            if (typeStr.Contains("Int8"))
                            {
                                isViewMetadata = true;
                                isLengthField = false;
                                viewMetadataPhase = 0;
                                lastViewBindingName = "";
                            }
                            else
                            {
                                viewMetadataPhase = 0;
                                lastViewBindingName = "";
                            }
                        }

                        var info = new BodyStructFieldInfo
                        {
                            FieldIndex = fi,
                            FieldType = fieldType,
                            IsView = fieldIsView,
                            IsScalar = !fieldIsView,
                            IsViewMetadata = isViewMetadata,
                            IsLengthField = isLengthField,
                            BindingName = fieldIsView ? $"param{param.Index}_f{fi}" : "",
                            AssociatedViewBindingName = isViewMetadata ? capturedViewBindingName : "",
                        };
                        fieldInfos.Add(info);

                        if (fieldIsView)
                        {
                            lastViewBindingName = $"param{param.Index}_f{fi}";

                            // Detect packed struct: view field element type is struct with emu field(s)
                            var bsViewElemType = GetBufferElementType(fieldType);
                            if (bsViewElemType is StructureType bsViewStructType)
                            {
                                var bsPsLayout = ComputePackedLayout(bsViewStructType);
                                if (bsPsLayout != null)
                                    _packedStructBSFieldLayouts[(param.Index, fi)] = bsPsLayout;
                            }
                        }
                        else if (!isViewMetadata)
                        {
                            // User scalar field: assign scalar slot
                            var scalarElemType = GetBufferElementType(fieldType);
                            var scalarWgslType = TypeGenerator[scalarElemType];
                            bool fieldIsEmuF64 = Backend.EnableF64Emulation && scalarWgslType == "emu_f64";
                            bool fieldIsEmuI64 = Backend.EnableI64Emulation && (scalarWgslType == "emu_i64" || scalarWgslType == "emu_u64");
                            int slotCount = (fieldIsEmuF64 || fieldIsEmuI64) ? 2 : 1;
                            info.ScalarSlot = globalScalarSlotOffset;
                            globalScalarSlotOffset += slotCount;
                            lastViewBindingName = "";
                        }
                    }
                    _bodyStructParams[param.Index] = fieldInfos;
                }
                else
                {
                    // Non-body-struct param: track if it's a packed scalar
                    IsMultiDim(param.ParameterType, out _, out var isView, out _, out _, out _);
                    bool isStruct = param.ParameterType is global::ILGPU.IR.Types.StructureType && !isView;
                    bool isAtomic = _atomicParameters.Contains(param.Index);
                    bool ownBinding = isView || isStruct || isAtomic;
                    if (!ownBinding)
                    {
                        // This is a packed scalar — advance the slot offset
                        int slotCount = (isEmuF64 || isEmuI64) ? 2 : 1;
                        globalScalarSlotOffset += slotCount;
                    }
                }
            }

            // --- Pre-compute view offset scalar slots ---
            // GenerateHeader (which runs AFTER body generation) will add one scalar slot per
            // buffer/view param for the element offset. We must pre-compute these slot indices
            // here so the body's GetField handler (Field 1) can emit _scalar_params[slot]
            // instead of hardcoded 0. If the dict is empty during body gen, the auto-layout
            // drops the unused _scalar_params binding → bind group layout mismatch.
            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;

                var elemType = GetBufferElementType(param.ParameterType);
                var wType = TypeGenerator[elemType];
                bool emuF64 = Backend.EnableF64Emulation && wType == "emu_f64";
                bool emuI64 = Backend.EnableI64Emulation && (wType == "emu_i64" || wType == "emu_u64");

                IsMultiDim(param.ParameterType, out _, out var isView, out _, out _, out _);
                bool isStruct = param.ParameterType is global::ILGPU.IR.Types.StructureType && !isView;
                bool isAtomic = _atomicParameters.Contains(param.Index);
                bool ownBinding = isView || isStruct || isAtomic;

                // Body structs are handled separately (their view fields get bindings too)
                IsMultiDim(param.ParameterType, out var isMultiDimCheck, out _, out _, out _, out _);
                bool isBodyStruct = param.ParameterType is global::ILGPU.IR.Types.StructureType bsType2
                    && IsBodyStruct(bsType2)
                    && (KernelParamOffset == 0 || !isMultiDimCheck);

                if (isBodyStruct)
                {
                    // Body struct view fields also need view offset slots.
                    // Allocate a slot for each view field and pre-populate ViewOffsetSlot
                    // so LoadElementAddress (during body generation) can inject the offset.
                    // For packed-struct view fields, also reserve a ViewCountSlot.
                    if (_bodyStructParams.TryGetValue(param.Index, out var bsFields))
                    {
                        foreach (var fieldInfo in bsFields)
                        {
                            if (fieldInfo.IsView)
                            {
                                fieldInfo.ViewOffsetSlot = globalScalarSlotOffset;
                                // Use synthetic param index matching GenerateHeader's encoding
                                int syntheticIdx = (param.Index + 1) * 1000 + fieldInfo.FieldIndex;
                                _viewOffsetScalarSlots[syntheticIdx] = globalScalarSlotOffset;
                                globalScalarSlotOffset++;

                                // Packed-struct view fields also get a count slot (must match GenerateHeader order)
                                if (_packedStructBSFieldLayouts.ContainsKey((param.Index, fieldInfo.FieldIndex)))
                                {
                                    fieldInfo.ViewCountSlot = globalScalarSlotOffset;
                                    // Propagate to associated metadata fields (length field, etc.)
                                    string viewBindName = $"param{param.Index}_f{fieldInfo.FieldIndex}";
                                    foreach (var metaField in bsFields)
                                    {
                                        if (metaField.IsViewMetadata && metaField.AssociatedViewBindingName == viewBindName)
                                            metaField.ViewCountSlot = globalScalarSlotOffset;
                                    }
                                    globalScalarSlotOffset++;
                                }
                            }
                        }
                    }
                    continue;
                }

                if (ownBinding && isView)
                {
                    // This param gets a buffer binding → reserve a view offset slot
                    _viewOffsetScalarSlots[param.Index] = globalScalarSlotOffset;
                    globalScalarSlotOffset++;

                    // Detect packed struct: view element type is a struct with emu-field(s)
                    var psElemType = GetBufferElementType(param.ParameterType);
                    if (psElemType is StructureType psStructType)
                    {
                        var psLayout = ComputePackedLayout(psStructType);
                        if (psLayout != null)
                            _packedStructLayouts[param.Index] = psLayout;
                    }
                }
            }
        }

        /// <summary>
        /// Decides whether this kernel exceeds WebGPU's maxStorageBuffersPerShaderStage limit
        /// and, if so, coalesces eligible body-struct ArrayView fields into shared bindings
        /// grouped by element type. Sets <see cref="_coalesceGroups"/>, <see cref="_coalesceMembership"/>,
        /// and <see cref="_coalesceLeaders"/>; mutates <see cref="BodyStructFieldInfo.BindingName"/>
        /// for every member so downstream codegen sites emit the shared binding identifier.
        ///
        /// Eligibility — a field qualifies for coalescing when ALL of:
        /// 1. It is an ArrayView field of a body struct (IsView).
        /// 2. It is NOT atomic (atomic bindings need atomic&lt;T&gt; type, can't share with non-atomic).
        /// 3. It is NOT sub-word (i8/i16/Half emulated with packed atomic&lt;u32&gt; layout).
        /// 4. It is NOT a packed-struct view (uses CPU-layout u32 packing — different stride per group).
        /// 5. Element type is i32, u32, f32, emu_i64, emu_u64, or emu_f64 — primitives only.
        ///
        /// Trigger threshold: WebGPU spec minimum maxStorageBuffersPerShaderStage = 8; Chrome
        /// default = 10. We trigger when raw bindings would exceed 10 so kernels under that
        /// limit keep their current shape (no runtime concat overhead). Coalesces the LARGEST
        /// candidate groups first so the trigger shuts off as soon as we drop under the limit.
        /// </summary>
        private void DecideCoalesceGroups()
        {
            // Body-struct coalesce + direct-param coalesce share this method. Either
            // path can fire even when the other has no candidates. (rc.16 direct-param
            // coalesce, 2026-05-05.)
            const int CoalesceTriggerThreshold = 10;

            // Estimate raw binding count had we NOT coalesced. Mirrors the structure of
            // GenerateHeader's emit path. Conservative — overcounts is fine, undercount is not.
            int paramOffset = KernelParamOffset;
            int rawBindings = 0;
            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;
                IsMultiDim(param.ParameterType, out var isMultiDim, out var isView, out _, out var is2D, out var is3D);
                bool isStruct = param.ParameterType is global::ILGPU.IR.Types.StructureType && !isView;
                bool isAtomic = _atomicParameters.Contains(param.Index);

                if (_bodyStructParams.TryGetValue(param.Index, out var bsFields))
                {
                    foreach (var bf in bsFields)
                        if (bf.IsView) rawBindings++;
                }
                else if (isView || isStruct || isAtomic)
                {
                    rawBindings++;
                    if (is2D || is3D) rawBindings++; // stride buffer
                }
            }
            rawBindings += 1;                       // _scalar_params buffer (assume present once any scalar/view-offset slots exist)
            rawBindings += _i64SpinlockParams.Count; // i64 spinlock companion buffers

            if (rawBindings <= CoalesceTriggerThreshold) return;

            // Build candidate groups (paramIdx + element-type key) and pick which to coalesce.
            // Sort candidates by member count descending — coalescing the largest groups first
            // reduces binding count fastest.
            var candidates = new List<(int paramIdx, string typeKey, string wgslType, int stride, List<BodyStructFieldInfo> members)>();
            foreach (var kvp in _bodyStructParams)
            {
                int paramIdx = kvp.Key;
                var bsFields = kvp.Value;
                var buckets = new Dictionary<string, (string wgslType, int stride, List<BodyStructFieldInfo> members)>();
                foreach (var bf in bsFields)
                {
                    if (!bf.IsView) continue;
                    if (_bodyStructAtomicFields.Contains((paramIdx, bf.FieldIndex))) continue;
                    // Output body-struct view fields MUST be excluded from coalesce.
                    // See ScanForBodyStructOutputs comment.
                    if (_bodyStructOutputFields.Contains((paramIdx, bf.FieldIndex))) continue;
                    int synthIdx = (paramIdx + 1) * 1000 + bf.FieldIndex;
                    if (_subWordParams.ContainsKey(synthIdx)) continue;
                    if (_packedStructBSFieldLayouts.ContainsKey((paramIdx, bf.FieldIndex))) continue;

                    var elemType = GetBufferElementType(bf.FieldType);

                    // Sub-word body-struct fields (Int8/Int16/UInt8/UInt16/Half) MUST be
                    // excluded from coalesce. Their backing storage is `array<atomic<u32>>`
                    // (packed sub-word — needs RMW semantics for thread-safe stores). The
                    // coalesce path emits a plain `array<i32>` (non-atomic) binding which
                    // would mismatch the per-load `atomicLoad(&binding[idx])` emit produced
                    // for sub-word reads → WGSL "no matching call to atomicLoad(ptr<storage,
                    // i32, read_write>)". `_subWordParams` is populated during GenerateHeader
                    // which runs AFTER `DecideCoalesceGroups`, so the dict-based check above
                    // never matches body-struct sub-word fields. Detect them here directly
                    // from the IR element type. Surfaced 2026-05-04 by Tuvok's SilkBodyStructShape
                    // (6 short + 9 int) — the int-coalesce bucket caught the shorts because
                    // BasicValueType.Int16 maps to "i32" in TypeGenerator (WGSL has no i16).
                    if (elemType is PrimitiveType subWordCheck)
                    {
                        var bvt = subWordCheck.BasicValueType;
                        if (bvt == BasicValueType.Int8 || bvt == BasicValueType.Int16
                            || bvt == BasicValueType.Float16)
                            continue;
                    }

                    var wgslElem = TypeGenerator[elemType];
                    bool isEmuF64Field = Backend.EnableF64Emulation && wgslElem == "emu_f64";
                    bool isEmuI64Field = Backend.EnableI64Emulation && (wgslElem == "emu_i64" || wgslElem == "emu_u64");
                    string typeKey;
                    string bindingType;
                    int stride;
                    if (isEmuF64Field)      { typeKey = "u32_emu_f64"; bindingType = "u32"; stride = 2; }
                    else if (isEmuI64Field) { typeKey = "u32_emu_i64"; bindingType = "u32"; stride = 2; }
                    else if (wgslElem == "i32" || wgslElem == "u32" || wgslElem == "f32")
                    {
                        typeKey = wgslElem; bindingType = wgslElem; stride = 1;
                    }
                    else
                    {
                        continue; // unsupported element type for coalesce in v1
                    }

                    if (!buckets.TryGetValue(typeKey, out var bucket))
                    {
                        bucket = (bindingType, stride, new List<BodyStructFieldInfo>());
                        buckets[typeKey] = bucket;
                    }
                    bucket.members.Add(bf);
                }
                foreach (var bucketKvp in buckets)
                {
                    var bucket = bucketKvp.Value;
                    if (bucket.members.Count < 2) continue; // only coalesce when 2+ siblings would share
                    candidates.Add((paramIdx, bucketKvp.Key, bucket.wgslType, bucket.stride, bucket.members));
                }
            }

            // Sort candidates by potential savings (member count - 1 = bindings eliminated) descending.
            candidates.Sort((a, b) => (b.members.Count - 1).CompareTo(a.members.Count - 1));

            int currentBindings = rawBindings;
            foreach (var cand in candidates)
            {
                if (currentBindings <= CoalesceTriggerThreshold) break;
                string bindingName = $"param{cand.paramIdx}_{cand.typeKey.Replace("_", "")}_coalesced";
                var group = new CoalesceGroupEntry
                {
                    BodyStructParamIndex = cand.paramIdx,
                    ElementTypeKey = cand.typeKey,
                    BindingName = bindingName,
                    ElementWordsPerSlot = cand.stride,
                    BindingWgslType = cand.wgslType,
                };
                bool first = true;
                foreach (var bf in cand.members)
                {
                    bf.BindingName = bindingName;
                    group.MemberFieldIndices.Add(bf.FieldIndex);
                    _coalesceMembership[(cand.paramIdx, bf.FieldIndex)] = group;
                    if (first) { _coalesceLeaders.Add((cand.paramIdx, bf.FieldIndex)); first = false; }
                }
                _coalesceGroups.Add(group);
                // Each coalesced group eliminates (members - 1) bindings.
                currentBindings -= (cand.members.Count - 1);

                if (WebGPUBackend.VerboseLogging)
                    WebGPUBackend.Log($"[WGSL-Coalesce] Grouped {cand.members.Count} fields of param{cand.paramIdx} ({cand.typeKey}) into '{bindingName}' — bindings now {currentBindings}");
            }

            // ---------------------- Direct-param coalesce ----------------------
            // After body-struct coalesce decisions, scan direct ArrayView kernel
            // parameters and group same-typed input-only params into shared bindings.
            // Eligibility — a direct param qualifies when ALL of:
            //   1. It is an ArrayView / View-type param (single AddressSpaceType, not multi-dim).
            //   2. It is NOT atomic.
            //   3. It is NOT emulated 64-bit (Int64/Float64) for v1/v2 — those need stride=2 emit
            //      that the existing direct-param LEA path doesn't yet thread through.
            //   4. The kernel does NOT WRITE to it (per _directParamOutputs from
            //      ScanForBodyStructOutputs).
            //   5. Its ViewType is not multi-dim (ArrayView2D/3D need stride buffers).
            //
            // v1 (rc.16 2026-05-05): i32/u32/f32 — single binding type per group, stride=1.
            // v2 (rc.16 2026-05-05): sub-word (Int8/UInt8/Int16/UInt16/Float16) — shared
            //   `array<atomic<u32>>` binding per element-byte-size+sign, with each member's
            //   element offset stored in `_scalar_params[voSlot]`. The Load/Store path uses
            //   the leader's BindingName via `_subWordBodyStructBindingNames` (same dict that
            //   serves body-struct sub-word coalesce members).
            //
            // We trigger only when raw bindings still exceed the threshold after
            // body-struct coalesce. Mirrors the body-struct path: largest savings first.
            if (currentBindings <= CoalesceTriggerThreshold) return;

            var directCandidates = new List<(string typeKey, string wgslType, int stride, bool isSubWord, int subWordElemSize, bool subWordIsFloat16, List<global::ILGPU.IR.Values.Parameter> members)>();
            var directBuckets = new Dictionary<string, (string wgslType, int stride, bool isSubWord, int subWordElemSize, bool subWordIsFloat16, List<global::ILGPU.IR.Values.Parameter> members)>();
            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;
                // Skip body-struct params (already handled above).
                if (_bodyStructParams.ContainsKey(param.Index)) continue;
                IsMultiDim(param.ParameterType, out var isMultiDim, out var isViewParam, out _, out var is2D, out var is3D);
                if (!isViewParam || is2D || is3D) continue;
                if (_atomicParameters.Contains(param.Index)) continue;
                if (_directParamOutputs.Contains(param.Index)) continue;

                var elemType = GetBufferElementType(param.ParameterType);
                if (elemType is not PrimitiveType ept) continue;
                // Emu-64 still excluded (needs stride=2 emit).
                if (ept.BasicValueType == BasicValueType.Int64
                    || ept.BasicValueType == BasicValueType.Float64) continue;

                string typeKey;
                string bindingType;
                int stride;
                bool isSubWord = false;
                int subWordElemSize = 0;
                bool subWordIsFloat16 = false;

                // Check sub-word FIRST — TypeGenerator maps Int8/Int16 to "i32" and "f16"
                // emulated maps to "f32", so falling through to wgslElem checks would
                // mis-route sub-word ArrayViews into the i32/f32 path. v2 routes them to
                // the sub-word path via BasicValueType + emulation flag.
                switch (ept.BasicValueType)
                {
                    case BasicValueType.Int8:
                        typeKey = "swi8"; subWordElemSize = 1; isSubWord = true;
                        bindingType = "atomic<u32>"; stride = 1;
                        break;
                    case BasicValueType.Int16:
                        typeKey = "swi16"; subWordElemSize = 2; isSubWord = true;
                        bindingType = "atomic<u32>"; stride = 1;
                        break;
                    case BasicValueType.Float16:
                        // Native f16 (shader-f16 enabled) uses `array<f16>` storage and
                        // would bucket like a non-sub-word type (`array<f16>` is a valid
                        // WGSL storage type). We don't currently bucket f16 native — fall
                        // through to the wgslElem check below where it'll skip (TypeGenerator
                        // returns "f16" not in our v1 set). Emulated f16 (the common path
                        // when shader-f16 isn't supported) goes through atomic<u32> packed
                        // storage like Int8/Int16.
                        if (!Backend.HasShaderF16)
                        {
                            typeKey = "swf16"; subWordElemSize = 2; isSubWord = true;
                            subWordIsFloat16 = true;
                            bindingType = "atomic<u32>"; stride = 1;
                            break;
                        }
                        continue; // native f16 not supported in v2 yet
                    case BasicValueType.BFloat16:
                        // bfloat16: always emulated (no native WGSL bf16). Reuse the 2-byte
                        // sub-word storage classification; the "swbf16" typeKey keeps it in its
                        // own coalesce bucket so the Add below routes it to _subWordBFloat16Params.
                        typeKey = "swbf16"; subWordElemSize = 2; isSubWord = true;
                        subWordIsFloat16 = true;
                        bindingType = "atomic<u32>"; stride = 1;
                        break;
                    default:
                        {
                            var wgslElem = TypeGenerator[elemType];
                            if (wgslElem == "i32" || wgslElem == "u32" || wgslElem == "f32")
                            {
                                // v1 path — same WGSL type per group, no atomic, plain `array<i32|u32|f32>`.
                                typeKey = wgslElem; bindingType = wgslElem; stride = 1;
                            }
                            else
                            {
                                continue; // unsupported element type
                            }
                            break;
                        }
                }

                if (!directBuckets.TryGetValue(typeKey, out var bucket))
                {
                    bucket = (bindingType, stride, isSubWord, subWordElemSize, subWordIsFloat16, new List<global::ILGPU.IR.Values.Parameter>());
                    directBuckets[typeKey] = bucket;
                }
                bucket.members.Add(param);
            }
            // BasicValueType doesn't have separate UInt8/UInt16 cases — sub-word unsigned ArrayViews
            // surface as Int8/Int16 + a sign flag elsewhere. The existing sub-word direct-param
            // machinery (_subWordParams + _subWordFloat16Params) keys by IR param index, so
            // signedness is handled in Load codegen via the IR's target type, not by the bucket.
            // For coalesce purposes Int8/UInt8 share `swi8` bucket since storage layout is identical
            // (1 byte per element packed into atomic<u32>). Same for Int16/UInt16 → `swi16`.
            foreach (var bucketKvp in directBuckets)
            {
                var bucket = bucketKvp.Value;
                if (bucket.members.Count < 2) continue;
                directCandidates.Add((bucketKvp.Key, bucket.wgslType, bucket.stride, bucket.isSubWord, bucket.subWordElemSize, bucket.subWordIsFloat16, bucket.members));
            }
            directCandidates.Sort((a, b) => (b.members.Count - 1).CompareTo(a.members.Count - 1));

            foreach (var cand in directCandidates)
            {
                if (currentBindings <= CoalesceTriggerThreshold) break;
                string bindingName = $"param_direct_{cand.typeKey.Replace("_", "")}_coalesced";
                var group = new CoalesceGroupEntry
                {
                    BodyStructParamIndex = -1,
                    IsDirectParam = true,
                    IsSubWord = cand.isSubWord,
                    SubWordElementByteSize = cand.subWordElemSize,
                    ElementTypeKey = cand.typeKey,
                    BindingName = bindingName,
                    ElementWordsPerSlot = cand.stride,
                    BindingWgslType = cand.wgslType,
                };
                bool first = true;
                foreach (var p in cand.members)
                {
                    group.MemberDirectParamIndices.Add(p.Index);
                    _directParamCoalesceMembership[p.Index] = group;
                    if (first) { _directParamCoalesceLeaders.Add(p.Index); first = false; }

                    // For sub-word groups, register each member with the existing sub-word
                    // direct-param machinery so Load/Store finds the leader's binding name
                    // and the correct element byte size. _subWordFloat16Params tags Float16
                    // members for the _f16_to_f32 / _f32_to_f16 conversion path.
                    if (cand.isSubWord)
                    {
                        _subWordParams[p.Index] = cand.subWordElemSize;
                        if (cand.subWordIsFloat16)
                        {
                            // "swbf16" bucket = bfloat16 (different conversion helper at load/store).
                            if (cand.typeKey == "swbf16") _subWordBFloat16Params.Add(p.Index);
                            else _subWordFloat16Params.Add(p.Index);
                        }
                        _subWordBodyStructBindingNames[p.Index] = bindingName;
                    }
                }
                _coalesceGroups.Add(group);
                currentBindings -= (cand.members.Count - 1);

                if (WebGPUBackend.VerboseLogging)
                    WebGPUBackend.Log($"[WGSL-Coalesce] Grouped {cand.members.Count} direct {(cand.isSubWord ? "sub-word " : "")}params [{string.Join(",", cand.members.ConvertAll(p => p.Index))}] ({cand.typeKey}) into '{bindingName}' — bindings now {currentBindings}");
            }
        }

        /// <summary>
        /// Computes the packed u32 layout for a struct type whose fields include emulated 64-bit types.
        /// Returns null if the struct does not contain any emulated 64-bit fields, or if any field
        /// has an unsupported type that cannot be packed into u32 slots.
        /// </summary>
        private List<PackedStructFieldInfo>? ComputePackedLayout(StructureType structType)
        {
            var fields = new List<PackedStructFieldInfo>();
            int u32Offset = 0;
            bool hasEmuField = false;

            foreach (var fieldType in structType.Fields)
            {
                string wgslType = TypeGenerator[fieldType];
                bool isEmuF64 = Backend.EnableF64Emulation && wgslType == "emu_f64";
                bool isEmuI64u64 = Backend.EnableI64Emulation &&
                                   (wgslType == "emu_i64" || wgslType == "emu_u64");
                int u32Count;

                if (isEmuF64 || isEmuI64u64)
                {
                    u32Count = 2;
                    hasEmuField = true;
                }
                else if (wgslType == "i32" || wgslType == "u32" || wgslType == "f32" ||
                         wgslType == "f16" || wgslType == "i16" || wgslType == "u16" ||
                         wgslType == "i8"  || wgslType == "u8"  || wgslType == "bool")
                {
                    u32Count = 1;
                }
                else
                {
                    // Nested struct or unsupported type — skip packed treatment for safety
                    return null;
                }

                fields.Add(new PackedStructFieldInfo
                {
                    WgslType = wgslType,
                    U32Offset = u32Offset,
                    U32Count = u32Count,
                    IsEmuF64 = isEmuF64,
                    IsEmuI64OrU64 = isEmuI64u64,
                });
                u32Offset += u32Count;
            }

            return hasEmuField ? fields : null;
        }


        /// <summary>
        /// Resolves a value to a parameter, also tracking if the access goes through a GetField
        /// on a body struct. Returns true if resolved, with fieldIdx=-1 for direct param access
        /// or fieldIdx>=0 for a field of a body struct.
        /// </summary>
        private bool ResolveToParameterWithFieldChain(Value target, out global::ILGPU.IR.Values.Parameter? param, out int fieldIdx)
        {
            param = null;
            fieldIdx = -1;
            var current = target;
            int lastFieldIdx = -1;
            while (current != null)
            {
                if (current is global::ILGPU.IR.Values.Parameter p)
                {
                    param = p;
                    fieldIdx = lastFieldIdx;
                    return true;
                }
                if (current is global::ILGPU.IR.Values.LoadElementAddress lea)
                {
                    current = lea.Source;
                    continue;
                }
                if (current is global::ILGPU.IR.Values.GetField gf)
                {
                    lastFieldIdx = gf.FieldSpan.Index;
                    current = gf.ObjectValue;
                    continue;
                }
                if (current is global::ILGPU.IR.Values.AddressSpaceCast cast)
                {
                    current = cast.Value;
                    continue;
                }
                break;
            }
            // Fallback: use original ResolveToParameter
            param = ResolveToParameter(target) as global::ILGPU.IR.Values.Parameter;
            return param != null;
        }

        /// <summary>
        /// Returns true if the given StructureType is a "body struct" — i.e., it contains
        /// at least one view field (ViewType or struct with View as first field).
        /// Body structs (like ReductionImplementation, InitializerImplementation) cannot be
        /// represented as a single WGSL buffer binding and must be decomposed.
        /// </summary>
        private bool IsBodyStruct(global::ILGPU.IR.Types.StructureType st)
        {
            for (int i = 0; i < st.NumFields; i++)
            {
                var field = st.Fields[i];
                if (IsViewFieldType(field)) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the given type is a view type (ViewType or struct whose first field is a view).
        /// </summary>
        private bool IsViewFieldType(TypeNode type)
        {
            if (type is ViewType) return true;
            if (type is global::ILGPU.IR.Types.StructureType st)
            {
                if (st.NumFields > 0 && (st.Fields[0] is ViewType || st.Fields[0].ToString().Contains("View")))
                    return true;
            }
            if (type.ToString().Contains("View")) return true;
            return false;
        }

        #endregion

        #region Properties

        public EntryPoint EntryPoint { get; }
        public AllocaKindInformation DynamicSharedAllocations { get; }

        /// <summary>
        /// Returns the list of dynamic shared memory override constant info generated during code emission.
        /// </summary>
        public IReadOnlyList<DynamicSharedOverrideInfo> DynamicSharedOverrides => _dynamicSharedOverrides;

        private Value? _indexParameterAddress;

        // Returns 1 if the first IR parameter is an index that should be skipped
        // (for bindings/scalar packing), 0 if it's a user-supplied parameter.
        //
        // For auto-grouped kernels (IndexType = Index1D/2D/3D/LongIndex*),
        // ILGPU always puts the index as Method.Parameters[0] and it should be skipped.
        // Note: Index2D/3D are StructureType in IR (they have X,Y,Z fields).
        //
        // For KernelConfig (stream/explicitly-grouped kernels), the first IR param
        // may or may not be an index. LoadStreamKernel<Index1D, ...> puts Index1D
        // (PrimitiveType) at param 0. LoadStreamKernel<ArrayView<T>> puts a ViewType.
        // We only skip primitives (actual index types), not views or structs.
        private int KernelParamOffset
        {
            get
            {
                if (EntryPoint.IndexType == IndexType.None) return 0;

                // For auto-grouped kernels, the first param is always the index — skip it.
                // This covers Index1D (PrimitiveType), Index2D/3D (StructureType), and LongIndex* types.
                if (EntryPoint.IndexType != IndexType.KernelConfig) return 1;

                // KernelConfig: explicitly grouped / grid-stride kernels.
                // ILGPU's KernelIndexParameterOffset is 0 for KernelConfig, meaning ALL
                // params are in EntryPoint.Parameters[]. However, stream kernels loaded
                // via LoadStreamKernel<Index1D, ...> have Index1D as Parameters[0] —
                // this is the thread index and should be skipped (mapped to global_id.x).
                //
                // Grid-stride kernels (e.g. reduce, initialize) use LongIndex1D as their
                // first param — this is the padded data count, NOT a thread index.
                // It must NOT be skipped; it needs a buffer binding.
                //
                // Distinction: Index1D/2D/3D → stream kernel index → skip (offset=1)
                //              LongIndex1D/2D/3D → grid-stride data count → don't skip (offset=0)
                //
                // This mirrors the runtime's runtimeIndexSkip logic in WebGPUAccelerator.cs.
                if (EntryPoint.Parameters.Count > 0)
                {
                    var firstParamType = EntryPoint.Parameters[0];
                    if (firstParamType == typeof(Index1D) || firstParamType == typeof(Index2D) || firstParamType == typeof(Index3D))
                        return 1;
                }
                return 0;
            }
        }


        #endregion

        #region Methods

        /// <summary>
        /// Returns true if the atomic target pointer resolves to a parameter that was
        /// detected as float-typed during ScanForAtomicUsage. Used by the base class
        /// GenerateCode(GenericAtomic) to select the CAS-loop emulation path.
        /// </summary>
        protected override bool IsFloatAtomicTarget(global::ILGPU.IR.Value target)
        {
            // Check direct parameter float atomics
            if (ResolveToParameter(target) is global::ILGPU.IR.Values.Parameter param
                && _floatAtomicParameters.Contains(param.Index))
                return true;

            // Check body struct float atomic fields (e.g. ReductionImplementation.Output view)
            if (ResolveToParameterWithFieldChain(target, out var bsParam, out var fieldIdx)
                && fieldIdx >= 0
                && _bodyStructAtomicFloatFields.Contains((bsParam!.Index, fieldIdx)))
                return true;

            return false;
        }

        /// <summary>
        /// Returns true if the atomic target pointer resolves to a parameter that was
        /// detected as unsigned during ScanForAtomicUsage. Used by the base class
        /// GenerateCode(GenericAtomic) to use atomic&lt;u32&gt; operations for unsigned semantics.
        /// </summary>
        protected override bool IsUnsignedAtomicTarget(global::ILGPU.IR.Value target)
        {
            // Check direct parameter unsigned atomics
            if (ResolveToParameter(target) is global::ILGPU.IR.Values.Parameter param
                && _unsignedAtomicParameters.Contains(param.Index))
                return true;

            // Check body struct unsigned atomic fields
            if (ResolveToParameterWithFieldChain(target, out var bsParam, out var fieldIdx)
                && fieldIdx >= 0
                && _bodyStructUnsignedAtomicFields.Contains((bsParam!.Index, fieldIdx)))
                return true;

            return false;
        }

        /// <summary>
        /// Returns true if the atomic target pointer resolves to a parameter with an emulated
        /// 64-bit element type (emu_i64, emu_u64, emu_f64). WGSL doesn't support atomic
        /// operations on vec2&lt;u32&gt;, so these must use plain storage and a direct store.
        /// </summary>
        protected override bool IsEmulated64BitAtomicTarget(global::ILGPU.IR.Value target)
        {
            // Check direct parameter emulated 64-bit atomics
            if (ResolveToParameter(target) is global::ILGPU.IR.Values.Parameter param
                && (_emulatedI64Params.Contains(param.Index) || _emulatedF64Params.Contains(param.Index))
                && _atomicParameters.Contains(param.Index))
                return true;

            // Check body struct emulated 64-bit atomic fields
            if (ResolveToParameterWithFieldChain(target, out var bsParam, out var fieldIdx)
                && fieldIdx >= 0
                && _bodyStructEmulated64AtomicFields.Contains((bsParam!.Index, fieldIdx)))
                return true;

            return false;
        }

        private TypeNode UnwrapType(TypeNode type)
        {
            var current = type;
            while (current != null)
            {
                if (current is global::ILGPU.IR.Types.PointerType ptr)
                {
                    current = ptr.ElementType;
                    continue;
                }
                return current;
            }
            return type;
        }

        private bool IsViewStructure(TypeNode type)
        {
            if (type is ViewType) return true;
            if (type is StructureType st)
            {
                if (st.ToString().Contains("View")) return true;
                if (st.NumFields >= 2 && st.Fields[0] is PointerType) return true;
            }
            return false;
        }

        public override void GenerateHeader(StringBuilder builder)
        {
            // ── Shader header comment ──
            // Emitted at the top of every generated shader for debugging. Use the SOURCE method name
            // (stable), NOT the IR Method.Name - the latter carries the IRContext's per-context
            // ordinal (e.g. "_41" live vs "_2" offline) which makes a precompiled artifact's bytes
            // depend on compile-order history and breaks offline==runtime determinism (Seven, 2026-06-11).
            builder.AppendLine($"// Kernel: {EntryPoint.MethodInfo?.Name ?? Method.Name}");
            builder.AppendLine($"// WorkgroupSize: {GetWorkgroupSize()}");
            if (_generatorArgs.SharedMemoryResolver.Count > 0)
            {
                builder.AppendLine($"// SharedMemory: {string.Join(", ", _generatorArgs.SharedMemoryResolver.GetVariableNames())}");
            }
            if (TypeGenerator.KernelUsesI64 == true || TypeGenerator.KernelUsesF64 == true)
            {
                var emuParts = new List<string>();
                if (TypeGenerator.KernelUsesI64 == true) emuParts.Add("i64");
                if (TypeGenerator.KernelUsesF64 == true) emuParts.Add("f64");
                builder.AppendLine($"// Emulation: {string.Join(", ", emuParts)}");
            }
            builder.AppendLine();

            // Pre-scan for Broadcast and subgroup usage
            ScanForSubgroupAndBroadcastUsage();

            // Emit WGSL enable directives at the very top of the module
            // These must appear before any other declarations
            // For subgroups: ALWAYS emit a placeholder when the backend supports subgroups.
            // The IR pre-scan may miss subgroup usage that gets injected during body generation
            // (e.g., algorithm helper functions like reduce that emit subgroupAdd).
            // The placeholder is resolved AFTER full body generation by WebGPUBackend.Compile(),
            // which checks if the final WGSL actually contains subgroup builtins.
            if (Backend.HasSubgroups)
                builder.AppendLine("/*__WGSL_ENABLE_SUBGROUPS_PLACEHOLDER__*/");
            if (Backend.HasShaderF16)
                builder.AppendLine("enable f16;");
            if (Backend.HasSubgroups || Backend.HasShaderF16)
                builder.AppendLine();

            // Use the emulation flags already set by SetEmulationFlags() in GenerateCode().
            // GenerateCode() runs BEFORE GenerateHeader() (CodeGeneratorBackend.Compile order),
            // and it scans both the kernel's IR AND helper methods' IR for Int64/Float64.
            // This ensures the emulation library inclusion matches the body's emulation usage.
            bool needsI64Emulation = TypeGenerator.KernelUsesI64 == true;
            bool needsF64Emulation = TypeGenerator.KernelUsesF64 == true;
            // Float16 emulation is needed whenever any parameter was registered as a
            // sub-word Float16 param (populated only when !Backend.HasShaderF16). Load/store
            // call-sites emit `_f16_to_f32` / `_f32_to_f16` from WGSLEmulationLibrary.F16Functions.
            // The FloatAsIntCast / IntAsFloatCast paths also emit these helpers when the IR
            // type is Float16 but the WGSL type is "f32" (emulated path), which happens in
            // kernels that handle RadixSortPair<Half, int> - the Half lives in a struct field,
            // not a direct parameter, so _subWordFloat16Params stays empty. Track that case
            // via _kernelReferencesF16Helpers set at emit time.
            bool needsF16Emulation = _subWordFloat16Params.Count > 0 || _kernelReferencesF16Helpers;
            bool needsBF16Emulation = _subWordBFloat16Params.Count > 0 || _kernelReferencesBF16Helpers;

            // Emit minimal emulation library: only functions actually used by the kernel body
            if (needsF64Emulation || needsI64Emulation || needsF16Emulation || needsBF16Emulation)
            {
                builder.AppendLine("// ============ Emulation Library ============");
                builder.AppendLine(WGSLEmulationLibrary.GetMinimalEmulationLibrary(
                    needsF64Emulation,
                    Backend.UseOzakiF64Emulation,
                    needsI64Emulation,
                    needsF16Emulation,
                    Builder.ToString(),
                    includeBF16: needsBF16Emulation));
            }

            // F32 Inf/NaN literal helpers - always available because:
            //   1. They're tiny (3 functions, ~6 lines).
            //   2. Trimming them based on kernel body grep is fragile because
            //      the codegen may emit them from FormatFloat in places we
            //      can't easily detect (constants used as default values,
            //      identity-property emission, etc.).
            //   3. They MUST be functions (not const-bitcast literals)
            //      because WGSL's const-evaluator rejects expressions that
            //      yield non-finite values - `bitcast<f32>(0x7F800000u)`
            //      would const-eval to +Inf and trip "Invalid ComputePipeline".
            //   4. Function calls aren't const-evaluated, so the bitcast
            //      runs at runtime where producing +Inf / -Inf / NaN is
            //      explicitly permitted.
            builder.AppendLine("// ============ F32 Special Value Helpers ============");
            builder.AppendLine(WGSLEmulationLibrary.F32SpecialValueFunctions);

            // Emit hoisted i64 constants (deduplicated across kernel + helper bodies)
            if (_baseArgs.I64Constants.Count > 0)
            {
                builder.AppendLine("// ============ Hoisted i64 Constants ============");
                foreach (var (pair, name) in _baseArgs.I64Constants)
                    builder.AppendLine(
                        $"const {name} : vec2<u32> = vec2<u32>({pair.Lo}u, {pair.Hi}u);");
                builder.AppendLine();
            }

            // For auto-grouped (implicitly grouped) kernels, emit an override constant for the
            // user dimension. WebGPU dispatches ceil(userDim/workgroupSize) workgroups, which
            // often exceeds the actual data size. Without a range check, excess threads execute
            // and WGSL's clamped array indexing causes OOB writes to overwrite the last valid
            // element. The override constant is set per-dispatch in WebGPUAccelerator.RunKernel().
            if (!EntryPoint.IsExplicitlyGrouped && KernelParamOffset > 0)
            {
                builder.AppendLine("override _ilgpu_user_dim : u32 = 4294967295u;");
                builder.AppendLine();
            }

            // Emit struct definitions (may reference emu_i64/emu_f64 from the library above)
            TypeGenerator.GenerateTypeDefinitions(builder);

            int bindingIdx = 0;
            int paramOffset = KernelParamOffset;

            // --- SCALAR PACKING ---
            // Instead of emitting one @binding per param, we pack all non-view, non-struct
            // scalar params into a single array<u32> storage buffer. This drastically reduces
            // the binding count, staying within WebGPU's maxStorageBuffersPerShaderStage limit.
            //
            // Phase 1: Classify params and emit view bindings. Collect scalar packing info.
            // Phase 2: Emit single packed scalar binding (if any).

            var scalarManifest = new List<ScalarPackingEntry>();
            int scalarSlotOffset = 0; // current u32 slot index in packed buffer
            // Track view (buffer) params and their binding indices for view offset entries
            var viewParamBindingIndices = new List<(int paramIndex, int bindingIndex)>();

            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;

                var elementType = GetBufferElementType(param.ParameterType);
                var wgslType = TypeGenerator[elementType];

                // Debug info
                builder.AppendLine($"// Param {param.Index}: {param.ParameterType} (Element: {elementType})");

                // --- BODY STRUCT DECOMPOSITION ---
                // We must check for body structs (e.g. ReductionImplementation<T>, InitializerImplementation<T>)
                // that contain view fields mixed with scalar fields.
                // These get decomposed into individual bindings.
                //
                // Challenge: ArrayView2D/3D are also StructureTypes with a view field, but they
                // should NOT be treated as body structs. They are handled by the IsMultiDim / view
                // binding path, which matches the runtime's binding layout.
                //
                // Solution: For grid-stride kernels (KernelParamOffset == 0), ALL struct params with
                // view fields are body structs — grid-stride kernels never receive raw view params.
                // For stream kernels (KernelParamOffset > 0), use IsMultiDim to exclude view wrappers.
                IsMultiDim(param.ParameterType, out var earlyIsMultiDim, out var earlyIsView, out var earlyIs1D, out var earlyIs2D, out var earlyIs3D);
                if (param.ParameterType is global::ILGPU.IR.Types.StructureType earlyBodyStructType
                    && IsBodyStruct(earlyBodyStructType)
                    && (KernelParamOffset == 0 || !earlyIsMultiDim))
                {
                    var fieldInfos = new List<BodyStructFieldInfo>();
                    string lastViewBindingName = "";
                    int viewMetadataPhase = 0; // 0=idle, 1=after view ptr (expect Int64), 2=after Int64 (expect optional Int8)

                    for (int fi = 0; fi < earlyBodyStructType.NumFields; fi++)
                    {
                        var fieldType = earlyBodyStructType.Fields[fi];
                        bool fieldIsView = IsViewFieldType(fieldType);
                        if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-Field] param.Index={param.Index} fi={fi} type={fieldType} IsView={fieldIsView} phase={viewMetadataPhase} lastViewName='{lastViewBindingName}'");
                        // Capture lastViewBindingName BEFORE any potential reset in the state machine below,
                        // so AssociatedViewBindingName is correctly set for metadata fields.
                        string capturedViewBindingName = lastViewBindingName;

                        // Detect view metadata: Int64 or Int8 fields immediately following a view field
                        // These are internal fields of ArrayView1D (Length, flag) — not user scalars
                        //
                        // ArrayView1D<T, Dense> IR structure: [ptr(ViewType), Int64(length), Int8(valid_flag)]
                        // ArrayView<T>          IR structure: [ptr(ViewType), Int64(length)] (no flag)
                        //
                        // We use a state machine (viewMetadataPhase) to track position in this sequence:
                        //   0 = no view being tracked (reset state)
                        //   1 = saw a view ptr; expecting Int64 length next
                        //   2 = saw the Int64 length; expecting optional Int8 flag next
                        // Any field outside expected sequence → reset to 0.
                        //
                        // CRITICAL: This prevents ReducedValue:long (Int64) from being misclassified
                        // as a second view's length metadata when it immediately follows a view's
                        // Int64 length field (which occurs in ArrayView<T> with no Int8 flag).
                        bool isViewMetadata = false;
                        bool isLengthField = false;
                        if (fieldIsView)
                        {
                            // View pointer encountered — next field should be Int64 length
                            viewMetadataPhase = 1;
                        }
                        else if (viewMetadataPhase == 1)
                        {
                            string typeStr = fieldType.ToString();
                            // Only mark as view-length metadata if:
                            // 1. The type is Int64 (view lengths are always Int64), AND
                            // 2. This is NOT the last field in the struct.
                            // Reason: ILGPU's DCE can remove the Output view's Length field when it's
                            // never accessed. When DCE removes Output.Length, ReducedValue (e.g., long/ulong)
                            // becomes the field immediately after the Output view ptr — and it's also Int64!
                            // If there's no next field, this must be ReducedValue, not a view length.
                            bool isLastField = (fi == earlyBodyStructType.NumFields - 1);
                            if (typeStr.Contains("Int64") && !isLastField)
                            {
                                isViewMetadata = true;
                                isLengthField = true;
                                viewMetadataPhase = 2; // Now expecting optional Int8 flag
                            }
                            else
                            {
                                // Unexpected type where length should be, OR last field — reset
                                viewMetadataPhase = 0;
                                lastViewBindingName = "";
                            }
                        }
                        else if (viewMetadataPhase == 2)
                        {
                            // Just after Int64 length: expecting optional Int8 flag
                            string typeStr = fieldType.ToString();
                            if (typeStr.Contains("Int8"))
                            {
                                isViewMetadata = true;
                                isLengthField = false;
                                viewMetadataPhase = 0; // Int8 is the last metadata field — reset
                                lastViewBindingName = "";
                            }
                            else
                            {
                                // No Int8 flag (e.g., ArrayView<T> vs ArrayView1D) — this is a user scalar
                                // Reset phase and treat this field normally (user scalar)
                                viewMetadataPhase = 0;
                                lastViewBindingName = "";
                            }
                        }
                        else
                        {
                            // Not in metadata tracking (viewMetadataPhase == 0) — user scalar or another view
                            // lastViewBindingName is already "" here; no action needed
                        }

                        var info = new BodyStructFieldInfo
                        {
                            FieldIndex = fi,
                            FieldType = fieldType,
                            IsView = fieldIsView,
                            IsScalar = !fieldIsView,
                            IsViewMetadata = isViewMetadata,
                            IsLengthField = isLengthField,
                            AssociatedViewBindingName = isViewMetadata ? capturedViewBindingName : "",
                        };
                        fieldInfos.Add(info);

                        if (fieldIsView)
                        {
                            // View field: create a separate buffer binding (or share a coalesced one).
                            // If DecideCoalesceGroups marked this field as a coalesce member, use the
                            // group's shared binding name. Only the LEADER emits a @binding decl;
                            // non-leaders skip emission and reuse the leader's binding index.
                            bool isCoalesceMember = _coalesceMembership.TryGetValue((param.Index, fi), out var coalesceGroup);
                            bool isCoalesceLeader = isCoalesceMember && _coalesceLeaders.Contains((param.Index, fi));
                            string bindingName = isCoalesceMember ? coalesceGroup!.BindingName : $"param{param.Index}_f{fi}";
                            info.BindingName = bindingName;
                            lastViewBindingName = bindingName;

                            // Determine element type for this view field
                            var viewElemType = GetBufferElementType(fieldType);
                            var viewWgslType = TypeGenerator[viewElemType];

                            // Check if this view field is atomic
                            bool fieldIsAtomic = _bodyStructAtomicFields.Contains((param.Index, fi));
                            bool fieldIsFloatAtomic = _bodyStructAtomicFloatFields.Contains((param.Index, fi));
                            bool fieldIsUnsignedAtomic = _bodyStructUnsignedAtomicFields.Contains((param.Index, fi));

                            string bindingWgslType = viewWgslType;
                            bool is64Emu = (Backend.EnableF64Emulation && viewWgslType == "emu_f64") ||
                                           (Backend.EnableI64Emulation && (viewWgslType == "emu_i64" || viewWgslType == "emu_u64"));

                            // Track sub-word view fields inside body structs
                            bool isSubWordField = false;
                            if (viewElemType is PrimitiveType bsPt)
                            {
                                int bsElemSize = 0;
                                if (bsPt.BasicValueType == BasicValueType.Int8)
                                    bsElemSize = 1;
                                else if (bsPt.BasicValueType == BasicValueType.Int16)
                                    bsElemSize = 2;
                                else if (bsPt.BasicValueType == BasicValueType.Float16 && !Backend.HasShaderF16)
                                    bsElemSize = 2;
                                if (bsElemSize > 0)
                                {
                                    // Use syntheticViewParamIdx as key since body struct fields
                                    // don't have their own param.Index
                                    int synthIdx = (param.Index + 1) * 1000 + fi;
                                    _subWordParams[synthIdx] = bsElemSize;
                                    if (bsPt.BasicValueType == BasicValueType.Float16 && !Backend.HasShaderF16)
                                        _subWordFloat16Params.Add(synthIdx);
                                    isSubWordField = true;
                                }
                            }

                            if (is64Emu)
                            {
                                bindingWgslType = "u32";
                            }
                            else if (_packedStructBSFieldLayouts.ContainsKey((param.Index, fi)))
                            {
                                // Packed struct view field: bind as array<u32> using CPU layout packing
                                bindingWgslType = "u32";
                            }
                            else if (isSubWordField)
                            {
                                // Sub-word view field: atomic<u32> for thread-safe RMW
                                bindingWgslType = "atomic<u32>";
                            }

                            if (fieldIsAtomic)
                            {
                                if (is64Emu)
                                {
                                    // Emulated 64-bit atomic body-struct field: stored as two u32
                                    // words per element. The per-word atomic ops (atomicLoad,
                                    // atomicStore, atomicCompareExchangeWeak, atomicAdd-on-hi)
                                    // used by the Min/Max/Exchange spinlock and Add CAS loop
                                    // require the binding type to be atomic<u32>.
                                    bindingWgslType = "atomic<u32>";
                                    _bodyStructEmulated64AtomicFields.Add((param.Index, fi));
                                }
                                else if (fieldIsFloatAtomic || fieldIsUnsignedAtomic)
                                    bindingWgslType = "atomic<u32>";
                                else
                                    bindingWgslType = $"atomic<{viewWgslType}>";
                            }

                            // Coalesced binding emission:
                            //   LEADER (or non-coalesced):   emit @binding decl + advance bindingIdx
                            //   NON-LEADER coalesce member:  share leader's bindingIdx — DO NOT emit decl + DO NOT advance
                            // The viewParamBindingIndices entry is recorded for EVERY field (leader and non-leader)
                            // because each field still needs its own per-field offset slot in _scalar_params.
                            // Non-leader members map their synthetic param index to the LEADER's binding index.
                            int syntheticViewParamIdx = (param.Index + 1) * 1000 + fi;
                            int recordedBindingIdx;
                            if (isCoalesceMember && !isCoalesceLeader)
                            {
                                // Map to the leader's binding index; the group's BindingIndex was set when
                                // the leader was emitted (always processed first because we walk fields in IR order).
                                builder.AppendLine($"// Body struct field {fi}: {fieldType} (view, coalesced into {bindingName})");
                                recordedBindingIdx = coalesceGroup!.BindingIndex;
                            }
                            else
                            {
                                if (isCoalesceLeader)
                                {
                                    builder.AppendLine($"// Body struct field {fi}: {fieldType} (view, COALESCE LEADER for {bindingName})");
                                    // Coalesce binding type override: use the group's chosen WGSL type so
                                    // mixed sources (e.g. emu_i64 stored as u32) keep a single declaration.
                                    bindingWgslType = coalesceGroup!.BindingWgslType;
                                    coalesceGroup.BindingIndex = bindingIdx;
                                }
                                else
                                {
                                    builder.AppendLine($"// Body struct field {fi}: {fieldType} (view)");
                                }
                                builder.AppendLine($"@group(0) @binding({bindingIdx}) var<storage, read_write> {bindingName} : array<{bindingWgslType}>;");
                                recordedBindingIdx = bindingIdx;
                                bindingIdx++;
                            }
                            viewParamBindingIndices.Add((syntheticViewParamIdx, recordedBindingIdx));
                        }
                        else if (isViewMetadata)
                        {
                            // View metadata field: NOT packed into _scalar_params
                            // Will use arrayLength(&bindingName) or 0 in SetupParameterBindings
                            builder.AppendLine($"// Body struct field {fi}: {fieldType} (view metadata, {(isLengthField ? "length" : "flag")})");
                            // Don't reset lastViewBindingName — there may be more metadata fields
                        }
                        else
                        {
                            // User scalar field: pack into _scalar_params
                            lastViewBindingName = ""; // Reset view tracking
                            var scalarElemType = GetBufferElementType(fieldType);
                            var scalarWgslType = TypeGenerator[scalarElemType];

                            bool isEmuF64 = Backend.EnableF64Emulation && scalarWgslType == "emu_f64";
                            bool isEmuI64 = Backend.EnableI64Emulation && (scalarWgslType == "emu_i64" || scalarWgslType == "emu_u64");
                            int slotCount = (isEmuF64 || isEmuI64) ? 2 : 1;

                            info.ScalarSlot = scalarSlotOffset;

                            // Use a synthetic param index for the scalar manifest entry
                            // We encode it as (paramIndex + 1) * 1000 + fieldIndex to make it unique
                            // IMPORTANT: use (param.Index + 1) so that grid-stride kernels with
                            // param.Index == 0 still produce a synthetic >= 1000, which is how the
                            // runtime decoder distinguishes body-struct scalars from real params.
                            int syntheticParamIdx = (param.Index + 1) * 1000 + fi;
                            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-BodyStruct] param.Index={param.Index}, fi={fi}, syntheticParamIdx={syntheticParamIdx}, paramOffset={paramOffset}, wgslType={scalarWgslType}");
                            var entry = new ScalarPackingEntry
                            {
                                ParamIndex = syntheticParamIdx,
                                ByteOffset = scalarSlotOffset * 4,
                                ByteSize = slotCount * 4,
                                WgslType = scalarWgslType,
                                IsEmulatedF64 = isEmuF64,
                                IsEmulatedI64 = isEmuI64,
                                IsStruct = false,
                            };
                            scalarManifest.Add(entry);
                            builder.AppendLine($"// Body struct field {fi}: {fieldType} (user scalar, slot {scalarSlotOffset})");
                            scalarSlotOffset += slotCount;
                        }
                    }
                    _bodyStructParams[param.Index] = fieldInfos;
                    continue; // Skip normal binding logic for this param
                }

                // Determine if this is a view, multidim view, struct, or primitive scalar
                IsMultiDim(param.ParameterType, out var isMultiDim, out var isView, out var is1DView, out var is2DView, out var is3DView);

                bool isStruct = param.ParameterType is global::ILGPU.IR.Types.StructureType && !isView;
                bool isAtomic = _atomicParameters.Contains(param.Index);

                // Check emulation
                bool isEmulatedF64 = Backend.EnableF64Emulation && wgslType == "emu_f64";
                bool isEmulatedI64 = Backend.EnableI64Emulation && (wgslType == "emu_i64" || wgslType == "emu_u64");

                // If parameter is a view, check its element type for emulation tracking
                if (isView)
                {
                    var paramElemType = GetBufferElementType(param.ParameterType);
                    string elemWgslType = TypeGenerator[paramElemType];
                    if (Backend.EnableF64Emulation && elemWgslType == "emu_f64")
                        isEmulatedF64 = true;
                    if (Backend.EnableI64Emulation && (elemWgslType == "emu_i64" || elemWgslType == "emu_u64"))
                        isEmulatedI64 = true;
                    // Track sub-word element views: WGSL has no 8/16-bit types,
                    // buffer is array<u32>, element access needs sub-word extraction
                    if (paramElemType is PrimitiveType pt)
                    {
                        // BasicValueType: Int8 covers both sbyte/byte, Int16 covers both short/ushort
                        // (ILGPU IR doesn't distinguish signed/unsigned at storage level)
                        switch (pt.BasicValueType)
                        {
                            case BasicValueType.Int8:
                                _subWordParams[param.Index] = 1; // 1 byte per element
                                break;
                            case BasicValueType.Int16:
                                _subWordParams[param.Index] = 2; // 2 bytes per element
                                break;
                            case BasicValueType.Float16:
                                if (!Backend.HasShaderF16)
                                {
                                    _subWordParams[param.Index] = 2; // emulated f16 = 2 bytes
                                    _subWordFloat16Params.Add(param.Index);
                                }
                                break;
                            case BasicValueType.BFloat16:
                                _subWordParams[param.Index] = 2; // bfloat16 always emulated = 2 bytes
                                _subWordBFloat16Params.Add(param.Index);
                                break;
                        }
                    }
                }

                if (isEmulatedF64)
                    _emulatedF64Params.Add(param.Index);
                else if (isEmulatedI64)
                    _emulatedI64Params.Add(param.Index);

                // DECISION: Does this param get its own binding, or is it packed?
                bool ownBinding = isView || isStruct || isAtomic;

                if (ownBinding)
                {
                    // --- OWN BINDING (views, structs, atomics) ---
                    // NOTE: read-only optimization (var<storage, read>) is disabled because
                    // algorithm kernels bind SubViews of the same temp buffer to multiple params.
                    // WebGPU rejects mixed read-only/read-write usage on the same buffer in one
                    // compute pass. Enabling this requires runtime buffer aliasing checks.
                    string accessMode = "read_write";
                    string bindingWgslType = wgslType;

                    if (isEmulatedF64 || isEmulatedI64)
                    {
                        bindingWgslType = "u32"; // Raw bits storage
                    }
                    else if (_packedStructLayouts.ContainsKey(param.Index))
                    {
                        bindingWgslType = "u32"; // Packed struct: CPU-layout u32 packing
                    }
                    else if (_subWordParams.ContainsKey(param.Index))
                    {
                        // Sub-word: packed 8/16-bit values in u32 words.
                        // Must be atomic<u32> because multiple threads may write to different
                        // sub-word slots within the same u32 word (data race on RMW).
                        bindingWgslType = "atomic<u32>";
                    }

                    if (isAtomic)
                    {
                        // WGSL only supports atomic<i32> and atomic<u32>.
                        // Float atomics (f32/f16) must be stored as atomic<u32> (raw bits)
                        // and emulated via a CAS loop.
                        // Unsigned atomics also need atomic<u32> for correct min/max semantics.
                        if (_floatAtomicParameters.Contains(param.Index) ||
                            _unsignedAtomicParameters.Contains(param.Index))
                            bindingWgslType = "atomic<u32>";
                        else
                            bindingWgslType = $"atomic<{bindingWgslType}>";
                    }

                    // Direct-param coalesce: same-typed input-only direct ArrayView
                    // params may share a single binding. The leader emits the @binding
                    // decl with the group's shared name; non-leaders skip the decl and
                    // record their viewParamBindingIndices entry pointing to the leader's
                    // binding index. Each member's own LoadElementAddress emit uses the
                    // shared binding name (substitution happens in the LEA codegen via
                    // _directParamCoalesceMembership). (rc.16 direct-param coalesce, 2026-05-05.)
                    if (_directParamCoalesceMembership.TryGetValue(param.Index, out var dpGroup))
                    {
                        bool isDpLeader = _directParamCoalesceLeaders.Contains(param.Index);
                        if (isDpLeader)
                        {
                            // Override binding type to the group's chosen WGSL type.
                            bindingWgslType = dpGroup.BindingWgslType;
                            dpGroup.BindingIndex = bindingIdx;
                            builder.AppendLine($"// Param {param.Index}: direct ArrayView (COALESCE LEADER for {dpGroup.BindingName})");
                            // Use `read_write` storage class for consistency with body-struct
                            // coalesce leaders (line 1688) and direct view bindings
                            // (`accessMode` hardcoded to `read_write` because algorithm kernels
                            // alias SubViews — same rationale applies to coalesced bindings).
                            // WGSL `atomic<u32>` requires `read_write`; sub-word coalesce groups
                            // bind `array<atomic<u32>>` and would fail validation under `read`.
                            builder.AppendLine($"@group(0) @binding({bindingIdx}) var<storage, read_write> {dpGroup.BindingName} : array<{bindingWgslType}>;");
                            viewParamBindingIndices.Add((param.Index, bindingIdx));
                            bindingIdx++;
                        }
                        else
                        {
                            // Non-leader: share the leader's binding index. The leader is
                            // always processed first (we walk Method.Parameters in IR order
                            // and DecideCoalesceGroups assigns the lowest-index member as
                            // leader). Group.BindingIndex is set when the leader emits.
                            builder.AppendLine($"// Param {param.Index}: direct ArrayView (coalesced into {dpGroup.BindingName})");
                            viewParamBindingIndices.Add((param.Index, dpGroup.BindingIndex));
                        }
                        // Stride buffer for 2D/3D views — direct-param coalesce v1 excludes
                        // multi-dim views (DecideCoalesceGroups gates on !is2D && !is3D), so
                        // no stride buffer emission needed here.
                        continue;
                    }

                    var bindingDecl = $"@group(0) @binding({bindingIdx}) var<storage, {accessMode}> param{param.Index} : array<{bindingWgslType}>;";
                    builder.AppendLine(bindingDecl);
                    viewParamBindingIndices.Add((param.Index, bindingIdx));
                    bindingIdx++;

                    // Stride buffer for 2D/3D views
                    if (isView && isMultiDim && (is2DView || is3DView))
                    {
                        var strideDecl = $"@group(0) @binding({bindingIdx}) var<storage, read> param{param.Index}_stride : array<i32>;";
                        builder.AppendLine(strideDecl);
                        bindingIdx++;
                    }
                }

                else
                {
                    // --- PACKED SCALAR ---
                    // Determine how many u32 slots this scalar needs
                    int slotCount = 1; // default: 4 bytes
                    int byteSize = 4;
                    string scalarWgslType = wgslType;

                    if (isEmulatedF64)
                    {
                        slotCount = 2;
                        byteSize = 8;
                        scalarWgslType = "emu_f64";
                    }
                    else if (isEmulatedI64)
                    {
                        slotCount = 2;
                        byteSize = 8;
                        scalarWgslType = wgslType; // emu_i64 or emu_u64
                    }

                    var entry = new ScalarPackingEntry
                    {
                        ParamIndex = param.Index,
                        ByteOffset = scalarSlotOffset * 4,
                        ByteSize = byteSize,
                        WgslType = scalarWgslType,
                        IsEmulatedF64 = isEmulatedF64,
                        IsEmulatedI64 = isEmulatedI64,
                        IsStruct = false,
                    };
                    scalarManifest.Add(entry);
                    builder.AppendLine($"// -> Packed scalar at slot {scalarSlotOffset} ({slotCount} slots, type: {scalarWgslType})");

                    scalarSlotOffset += slotCount;
                }
            }

            // --- View Offset Entries ---
            // For each buffer binding, add a scalar packing entry to store the element offset.
            // This supports sub-views: the runtime packs contiguous.Index (element count) 
            // and the WGSL reads it from _scalar_params instead of hardcoding offset to 0.
            foreach (var (viewParamIndex, viewBindIdx) in viewParamBindingIndices)
            {
                // Detect coalesce-member view fields. For these, the offset is NOT a
                // sub-view's element offset of the bound buffer — it is the member's
                // offset within the SHARED coalesced buffer. Runtime computes it from
                // the running sum of preceding members' lengths.
                bool isCoalesceMemberOffset = false;
                int coalesceParamIdx = -1, coalesceFieldIdx = -1;
                if (viewParamIndex >= 1000)
                {
                    // Body-struct member: synthetic param index encoding.
                    int rp = (viewParamIndex / 1000) - 1;
                    int fi = viewParamIndex % 1000;
                    if (_coalesceMembership.ContainsKey((rp, fi)))
                    {
                        isCoalesceMemberOffset = true;
                        coalesceParamIdx = rp;
                        coalesceFieldIdx = fi;
                    }
                }
                else if (_directParamCoalesceMembership.ContainsKey(viewParamIndex))
                {
                    // Direct-param coalesce member: viewParamIndex IS the IR param index
                    // directly. Treat the same as body-struct coalesce — the runtime
                    // fills _scalar_params[slot] with the member's u32-slot offset
                    // within the shared coalesced buffer. (rc.16 direct-param coalesce.)
                    isCoalesceMemberOffset = true;
                    coalesceParamIdx = viewParamIndex; // re-used to mean the direct param index
                    coalesceFieldIdx = -1;             // sentinel: direct-param mode
                }

                var viewOffsetEntry = new ScalarPackingEntry
                {
                    ParamIndex = viewParamIndex,
                    ByteOffset = scalarSlotOffset * 4,
                    ByteSize = 4,
                    WgslType = "i32",
                    IsViewOffset = !isCoalesceMemberOffset,
                    IsCoalesceFieldOffset = isCoalesceMemberOffset,
                    ViewBindingIndex = viewBindIdx,
                    CoalesceBodyStructParamIndex = coalesceParamIdx,
                    CoalesceFieldIndex = coalesceFieldIdx,
                };
                scalarManifest.Add(viewOffsetEntry);
                _viewOffsetScalarSlots[viewParamIndex] = scalarSlotOffset;
                builder.AppendLine($"// View offset for param{viewParamIndex} at scalar slot {scalarSlotOffset}{(isCoalesceMemberOffset ? " (coalesce member)" : "")}");

                // If this is a body struct view field (synthetic param index >= 1000),
                // propagate the slot to BodyStructFieldInfo so LoadElementAddress can use it.
                if (viewParamIndex >= 1000)
                {
                    int realParamIndex = (viewParamIndex / 1000) - 1;
                    int fieldIndex = viewParamIndex % 1000;
                    if (_bodyStructParams.TryGetValue(realParamIndex, out var bsFields)
                        && fieldIndex < bsFields.Count)
                    {
                        bsFields[fieldIndex].ViewOffsetSlot = scalarSlotOffset;
                    }
                }
                scalarSlotOffset++;

                // For packed-struct body-struct view fields, also add an element COUNT slot.
                // arrayLength() returns CPU-allocation-size/4 u32s, not the logical element count,
                // because CPU element size != GPU packed element size. So we send the true count
                // from the CPU in a dedicated scalar slot.
                if (viewParamIndex >= 1000)
                {
                    int realParamIndex2 = (viewParamIndex / 1000) - 1;
                    int fieldIndex2 = viewParamIndex % 1000;
                    if (_packedStructBSFieldLayouts.ContainsKey((realParamIndex2, fieldIndex2)))
                    {
                        var countEntry = new ScalarPackingEntry
                        {
                            ParamIndex = viewParamIndex,
                            ByteOffset = scalarSlotOffset * 4,
                            ByteSize = 4,
                            WgslType = "u32",
                            IsViewCount = true,
                            ViewCountBindingIndex = viewBindIdx,
                        };
                        scalarManifest.Add(countEntry);
                        builder.AppendLine($"// View element count for param{viewParamIndex} (packed struct) at scalar slot {scalarSlotOffset}");

                        if (_bodyStructParams.TryGetValue(realParamIndex2, out var bsFields2)
                            && fieldIndex2 < bsFields2.Count)
                        {
                            bsFields2[fieldIndex2].ViewCountSlot = scalarSlotOffset;
                            // Propagate to associated metadata (length) fields
                            string viewBindName2 = $"param{realParamIndex2}_f{fieldIndex2}";
                            foreach (var metaField2 in bsFields2)
                            {
                                if (metaField2.IsViewMetadata && metaField2.AssociatedViewBindingName == viewBindName2)
                                    metaField2.ViewCountSlot = scalarSlotOffset;
                            }
                        }
                        scalarSlotOffset++;
                    }
                }
                // (v1) No per-member ViewCount slot for direct-param coalesce members.
                // Pre-body slot allocation in ScanBodyStructParams reserves 1 slot per direct
                // view param (the offset slot). Adding a count slot here would advance
                // GenerateHeader's scalarSlotOffset by 2 per member while pre-body advances
                // by 1, drifting all subsequent members' slot indices. A correct fix needs
                // count-slot pre-allocation in ScanBodyStructParams, but DecideCoalesceGroups
                // runs AFTER ScanBodyStructParams so coalesce membership is unknown there.
                // For v1, GetViewLength on a coalesce member falls back to arrayLength()
                // which returns the shared binding's element count (sum across members) —
                // wrong, but kernels using direct-param coalesce typically don't read
                // view.Length on the inputs. Tracked for v2.
            }

            // Emit the single packed scalar binding (if any scalars were packed)
            // GenerateHeader runs AFTER body generation, so Builder already contains
            // the full function body. If _scalar_params[ is never referenced in the
            // body, Chrome's WebGPU auto-layout will strip the unused binding from the
            // bind group layout, causing a mismatch when the runtime creates entries
            // for it. Skip the declaration entirely to keep layout and bind group in sync.
            if (scalarManifest.Count > 0)
            {
                if (_bodyReferencesScalarParams)
                {
                    builder.AppendLine($"@group(0) @binding({bindingIdx}) var<storage, read> _scalar_params : array<u32>;");
                    bindingIdx++;

                    // Store manifest in generator args for the compiled kernel
                    foreach (var entry in scalarManifest)
                        _generatorArgs.ScalarPackingManifest.Add(entry);

                    foreach (var entry in scalarManifest)
                        if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-Manifest] ParamIndex={entry.ParamIndex}, ByteOffset={entry.ByteOffset}, ByteSize={entry.ByteSize}, WgslType={entry.WgslType}");
                }
                else
                {
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log("[WGSL] Skipping unused _scalar_params binding — body does not reference it");
                }
            }

            // Emit spinlock companion buffers for i64/f64 Min/Max/Exchange atomics.
            // One lock buffer per (ParamIdx, FieldIdx) pair. One atomic<u32> per emulated-64 element.
            // Emission order must match runtime's OrderBy in WebGPUAccelerator.
            foreach (var key in _i64SpinlockParams.OrderBy(x => x.ParamIdx).ThenBy(x => x.FieldIdx))
            {
                var lockName = key.FieldIdx >= 0
                    ? $"_lock_bs{key.ParamIdx}_f{key.FieldIdx}"
                    : $"_lock_param{key.ParamIdx}";
                _lockBufferNames[key] = lockName;
                builder.AppendLine($"@group(0) @binding({bindingIdx}) var<storage, read_write> {lockName} : array<atomic<u32>>;");
                if (key.FieldIdx >= 0)
                    builder.AppendLine($"// Spinlock buffer for i64/f64 Min/Max/Exchange on body-struct param{key.ParamIdx} field{key.FieldIdx}");
                else
                    builder.AppendLine($"// Spinlock buffer for i64/f64 Min/Max/Exchange on param{key.ParamIdx}");
                bindingIdx++;
                _generatorArgs.I64SpinlockParamIndices.Add(key);
            }

            // Record expected binding count for runtime validation (bind group must match WGSL layout)
            _generatorArgs.ExpectedBindingCountHolder[0] = bindingIdx;

            // Export coalesce manifest so the runtime knows which body-struct view fields
            // share which storage-buffer binding. The dispatcher concatenates each member's
            // GPU buffer data into the shared binding's GPU buffer at dispatch time.
            foreach (var grp in _coalesceGroups)
                _generatorArgs.CoalesceManifest.Add(grp);

            // Store manifest locally for SetupParameterBindings()
            _scalarManifest = scalarManifest;

            builder.AppendLine();

            // Emit shared memory allocations (static) via SharedMemoryResolver.
            // Uses SharedMemoryResolver (from backendContext.SharedAllocations) instead of
            // Allocas.SharedAllocations because the kernel's own Allocas may be EMPTY
            // when shared memory is allocated inside helper functions (e.g., reduction).
            var resolver = _generatorArgs.SharedMemoryResolver;
            resolver.EmitStaticDeclarations(builder, TypeGenerator, (allocaNode, varName, wgslType) =>
            {
                valueVariables[allocaNode] = new Variable(varName, wgslType);
                declaredVariables.Add(varName);
            });

            // Register kernel's own shared allocas (may be different object instances).
            resolver.RegisterKernelAllocas(
                Allocas.SharedAllocations,
                TypeGenerator,
                (allocaNode, varName, wgslType) =>
                {
                    valueVariables[allocaNode] = new Variable(varName, wgslType);
                },
                (name, allocaValue) => valueVariables.Any(kv => kv.Value.Name == name && kv.Key != allocaValue));

            // Emit dynamic shared memory allocations using pipeline-overridable constants
            resolver.EmitDynamicDeclarations(
                DynamicSharedAllocations,
                builder,
                TypeGenerator,
                loadVariable: (alloca) => Load(alloca).Name,
                markDeclared: (name) => declaredVariables.Add(name));

            // Copy dynamic overrides to the local list and generator args
            foreach (var overrideInfo in resolver.DynamicOverrides)
            {
                _dynamicSharedOverrides.Add(overrideInfo);
                _generatorArgs.DynamicSharedOverrides.Add(overrideInfo);
            }

            // Emit synthetic workgroup variables (broadcast, shuffle, group reduce)
            SharedMemoryResolver.EmitSyntheticWorkgroupVariables(
                new SyntheticWorkgroupConfig
                {
                    UsesBroadcast = _usesBroadcast,
                    BroadcastType = _broadcastType,
                    UsesWarpShuffleEmulation = UsesWarpShuffleEmulation,
                    UsesSubgroupsWithoutHardwareSupport = _usesSubgroups && !Backend.HasSubgroups,
                    UsesGroupReduce = _usesGroupReduce
                },
                builder);

            // NOTE: ALL builtins use 'let' bindings inside main() instead of var<private>.
            // var<private> is treated as "possibly non-uniform" by WGSL's uniformity analysis,
            // which breaks workgroupBarrier() calls in kernels branching on these values.
            // Since all helper functions are inlined into main(), let bindings are accessible
            // everywhere. 'let' preserves uniformity from @builtin params (for group_id,
            // num_workgroups) and gives the validator better tracking for local_id etc.
            // workgroup_size MUST be a const (not var<private>) for WGSL uniformity analysis.
            // var<private> is considered non-uniform, which breaks workgroupBarrier() calls.
            // Use the actual group size from KernelSpecialization when available.
            // Emit a placeholder for const workgroup_size. The actual value is resolved in
            // GenerateCode() AFTER shared memory allocations are known (needed for inference).
            // Both @workgroup_size and the const workgroup_size variable MUST agree.
            builder.AppendLine("/*__WGSL_CONST_WORKGROUP_SIZE_PLACEHOLDER__*/");

            builder.AppendLine();
        }

        /// <summary>
        /// Scans the method for Broadcast, subgroup, and barrier IR nodes to determine what features are needed.
        /// Sets _usesBroadcast (for shared memory temp), _usesSubgroups (for enable subgroups directive),
        /// and _usesBarriers (for uniformity-sensitive range check decisions).
        /// Called early in GenerateCode() so flags are available before SetupIndexVariables().
        /// </summary>
        private void ScanForSubgroupAndBroadcastUsage()
        {
            _usesBroadcast = false;
            _usesSubgroups = false;
            _usesBarriers = false;

            // Scan the kernel method, all inlined helper methods, AND all non-inlined
            // (standalone fn def) helpers for subgroup/barrier usage. Inlined helpers
            // become part of the main shader so their IR affects uniformity directly;
            // non-inlined helpers also affect feature flags because the kernel still
            // calls them and any subgroup/broadcast use inside them must be visible
            // to enable-subgroups + uniformity decisions. (rc.16 Bug D follow-up.)
            var methodsToScan = new List<Method> { Method };
            foreach (var (helperMethod, _) in _generatorArgs.HelperMethods)
                methodsToScan.Add(helperMethod);
            foreach (var nonInlineMethod in _generatorArgs.NonInlineMethods)
                methodsToScan.Add(nonInlineMethod);

            foreach (var method in methodsToScan)
            {
                foreach (var block in method.Blocks)
                {
                    foreach (var entry in block)
                    {
                        switch (entry.Value)
                        {
                            case global::ILGPU.IR.Values.Broadcast broadcast:
                                if (broadcast.Kind == global::ILGPU.IR.Values.BroadcastKind.GroupLevel)
                                {
                                    _usesBroadcast = true;
                                    _broadcastType = TypeGenerator[broadcast.Type];
                                }
                                else
                                    _usesSubgroups = true; // warp-level broadcast uses subgroupBroadcastFirst
                                break;
                            case global::ILGPU.IR.Values.WarpShuffle:
                            case global::ILGPU.IR.Values.SubWarpShuffle:
                            case global::ILGPU.IR.Values.LaneIdxValue:
                            case global::ILGPU.IR.Values.WarpSizeValue:
                                _usesSubgroups = true;
                                break;
                            case global::ILGPU.IR.Values.Barrier:
                            case global::ILGPU.IR.Values.PredicateBarrier:
                            case global::ILGPU.IR.Values.MemoryBarrier:
                                _usesBarriers = true;
                                break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Override MethodCall to inline helper functions (e.g., scan/reduce algorithm helpers)
        /// directly into the kernel. Helper functions that use workgroupBarrier() cannot be
        /// called as separate WGSL functions because WGSL requires all threads to reach barriers
        /// uniformly. Calling a barrier-using function from conditional code causes GPU deadlock.
        /// 
        /// This mirrors the Wasm backend's inlining approach: instead of emitting a function call,
        /// we walk the helper method's IR blocks and emit their code inline at the call site.
        /// </summary>
        public override void GenerateCode(MethodCall methodCall)
        {
            var targetMethod = methodCall.Target;

            // Check if this is a helper function we should inline
            if (_generatorArgs.HelperMethods.TryGetValue(targetMethod, out var helperAllocas))
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-Inline] Inlining helper: {targetMethod.Name} (id={targetMethod.Id}, {targetMethod.Parameters.Count} params, {methodCall.Nodes.Length} args)");

                // Step 1: Map call arguments to helper's parameters in valueVariables.
                // This ensures that when the inlined code calls Load(param), it resolves
                // to the caller's argument variable.
                for (int i = 0; i < targetMethod.Parameters.Count && i < methodCall.Nodes.Length; i++)
                {
                    var param = targetMethod.Parameters[i];
                    var arg = methodCall.Nodes[i].Resolve();

                    // Load the argument to get its Variable, then register the parameter
                    // to point to the same Variable
                    var argVar = Load(arg);
                    valueVariables[param] = argVar;
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-Inline]   param[{i}] '{param}' -> {argVar.Name}");
                }

                // Step 2: Allocate result variable if the call has a non-void return
                Variable? resultVar = null;
                if (!methodCall.Type.IsVoidType)
                {
                    resultVar = Load(methodCall);
                    Declare(resultVar);
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-Inline]   result -> {resultVar.Name}");
                }

                // Step 2.5: Set up the helper's local allocations (accumulators, loop counters, etc.)
                // Without this, the helper's local variables would not be registered in valueVariables,
                // causing the inlined code to create fresh variables instead of using the correct ones.
                if (helperAllocas.LocalAllocations.Length > 0)
                {
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-Inline]   Setting up {helperAllocas.LocalAllocations.Length} local allocations");
                    SetupAllocations(helperAllocas.LocalAllocations, MemoryAddressSpace.Local);
                }

                // Step 2.6: Register helper's shared memory alloca references via SharedMemoryResolver.
                // The helper's IR may reference shared Alloca nodes that are different object
                // instances from those in the resolver. Pre-register them here so that
                // Load() finds the correct module-scope variable without relying on the fallback.
                _generatorArgs.SharedMemoryResolver.RegisterHelperAllocas(
                    helperAllocas.SharedAllocations,
                    TypeGenerator,
                    (allocaNode, varName, wgslType) =>
                    {
                        valueVariables[allocaNode] = new Variable(varName, wgslType);
                    });

                // Step 3: Walk the helper's IR blocks and emit their code inline.
                // We handle single-block helpers directly, and multi-block helpers
                // using the same GenerateStructuredCode approach as the kernel.
                var blocks = targetMethod.Blocks;
                var blockList = blocks.ToList();

                // CRITICAL: Disable state machine mode during inlining.
                // When IsStateMachineActive=true, Declare() defers vars to VariableBuilder.
                // But the kernel's hoisting pass already flushed VariableBuilder, so new vars
                // added during inlining would be lost (never emitted in WGSL).
                // Setting to false makes Declare() emit 'var' declarations inline.
                var savedStateMachineActive = IsStateMachineActive;
                IsStateMachineActive = false;

                // CRITICAL FIX: Save declaredVariables state before inlining.
                // When a helper is inlined multiple times in the same kernel, the { } block-
                // scoped `var` declarations from the first inlining are invisible to later blocks.
                // But declaredVariables persists across inlinings, so Declare() skips the `var`
                // on the 2nd+ inlining → "unresolved value" errors (v_199, v_64, v_195).
                // Fix: snapshot declaredVariables before each inlining and restore after,
                // so scoped `var` declarations can be re-emitted in subsequent inlinings.
                var savedDeclaredVariables = new HashSet<string>(declaredVariables);
                var savedEmittedLetBindings = new HashSet<string>(_emittedLetBindings);

                // CRITICAL FIX: Offset varCounter to avoid namespace collisions.
                // The kernel's hoisting pass pre-declares vars at function scope (e.g. var v_214 : bool).
                // If the inlined helper's Allocate() generates the same name (v_214) for an i32,
                // the function-scope bool declaration is visible inside the { } block → type mismatch.
                // Fix: jump varCounter to a high offset so inlined vars get unique names.
                var savedVarCounter = varCounter;
                _inlineCounter++;
                varCounter = 10000 * _inlineCounter;

                // Also remove helper's IR values from valueVariables so Load() allocates
                // fresh variable names for each inlining (avoids name collisions in { } scopes).
                foreach (var block in blockList)
                    foreach (var valueEntry in block)
                        valueVariables.Remove(valueEntry.Value);

                AppendLine($"// --- BEGIN inlined helper: {targetMethod.Name} ---");
                AppendLine("{"); // Block-scope all let bindings to prevent redeclaration

                if (blockList.Count <= 1)
                {
                    // Single block: emit values inline
                    if (blockList.Count == 1)
                    {
                        var block = blockList[0];
                        foreach (var valueEntry in block)
                        {
                            GenerateCodeFor(valueEntry.Value);
                        }
                        // Handle terminator separately (not in enumeration)
                        if (block.Terminator is ReturnTerminator ret)
                        {
                            if (resultVar != null && !ret.IsVoidReturn)
                            {
                                var retVal = Load(ret.ReturnValue.Resolve());
                                AppendLine($"{resultVar} = {retVal};");
                            }
                            // Don't emit the actual return — we're inlining, not returning
                        }
                    }
                }
                else
                {
                    // Multi-block helper: use post-dominator-based structured code generation.
                    // This maintains uniform control flow for barriers.
                    if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-Inline] Multi-block helper with {blockList.Count} blocks");

                    // Save the current Method context — GenerateStructuredCode uses Method
                    // internally, but we need to process the helper's blocks, not the kernel's.
                    // Since GenerateStructuredCode takes explicit block parameters, we
                    // generate code by walking blocks directly with the same approach.
                    InlineMultiBlockHelper(targetMethod, blockList, resultVar);
                }

                AppendLine("}"); // End block scope for inlined helper
                AppendLine($"// --- END inlined helper: {targetMethod.Name} ---");

                // Restore state machine mode
                IsStateMachineActive = savedStateMachineActive;

                // Restore declaredVariables and _emittedLetBindings so the next inlining
                // of the same helper can re-emit { }-scoped var/let declarations.
                // Both are readonly, so we remove entries added during inlining.
                declaredVariables.ExceptWith(declaredVariables.Except(savedDeclaredVariables).ToList());
                _emittedLetBindings.ExceptWith(_emittedLetBindings.Except(savedEmittedLetBindings).ToList());

                // Restore varCounter so the kernel's own code gen continues from
                // where it left off, without gaps from the inlined variable names.
                varCounter = savedVarCounter;
                return;
            }

            // rc.16 work-in-progress: re-activating the rc.14 fn-call branch.
            // For methods that the IR Inliner skipped (NoInlining / body-size
            // cap) and that aren't intrinsics/external, emit a real WGSL fn
            // call to the standalone fn definition produced by
            // WGSLFunctionGenerator. Tracks `Plans/rc16-fn-def-codegen-harden.md`.
            if (targetMethod.HasImplementation
                && !targetMethod.HasFlags(MethodFlags.External)
                && !targetMethod.HasFlags(MethodFlags.Intrinsic))
            {
                EmitNonInlinedMethodCall(methodCall);
                return;
            }

            // Fall through to base class for non-helper calls (intrinsics, math, etc.)
            base.GenerateCode(methodCall);
        }

        /// <summary>
        /// Emits a real WGSL function call for a method whose body lives in a
        /// module-scope `fn` definition (not inlined into the kernel).
        ///
        /// The fn name is derived via <see cref="WGSLFunctionGenerator.GetMethodName"/>
        /// so call sites match the definition emitted by the function generator.
        /// Arguments are passed positionally by their already-computed Variable
        /// names; void-return calls emit a bare statement, value-return calls
        /// emit `let target = fn_name(args...);`.
        /// </summary>
        private void EmitNonInlinedMethodCall(MethodCall methodCall)
        {
            var fnName = WGSLFunctionGenerator.GetMethodName(methodCall.Target);
            var callee = methodCall.Target;

            var argStrs = new List<string>(methodCall.Count);
            // Coalesce-aware emit (local.15+): when args reference coalesced kernel
            // params, the helper's signature collapses each group into 1 leader ptr +
            // N per-member offsets. Emit the leader binding ref ONCE per group, then
            // emit the per-member offset for every coalesced arg. Non-coalesced args
            // emit normally.
            var emittedLeaderGroups = new HashSet<string>();
            for (int i = 0; i < methodCall.Count; i++)
            {
                var argResolved = methodCall.Nodes[i].Resolve();
                var argParam = ResolveToParameter(argResolved);
                if (argParam != null
                    && _generatorArgs.HelperParamCoalesceInfo.TryGetValue((callee, i), out var coalEntry))
                {
                    // Emit leader binding ref ONCE per group.
                    if (coalEntry.IsLeader && !emittedLeaderGroups.Contains(coalEntry.LeaderBindingName))
                    {
                        emittedLeaderGroups.Add(coalEntry.LeaderBindingName);
                        argStrs.Add($"&{coalEntry.LeaderBindingName}");
                        _bodyReferencesScalarParams = true;
                    }
                    // Always emit per-member offset (i32 element index into the leader).
                    int voSlot = coalEntry.OffsetScalarSlot;
                    if (voSlot >= 0)
                    {
                        argStrs.Add($"i32(_scalar_params[{voSlot}])");
                        _bodyReferencesScalarParams = true;
                    }
                    else
                    {
                        // No offset slot recorded — defensive fallback to 0.
                        argStrs.Add("0");
                    }
                    continue;
                }

                var argVar = Load(methodCall[i]);
                argStrs.Add(argVar.ToString());
            }
            string argList = string.Join(", ", argStrs);

            if (methodCall.Type.IsVoidType)
            {
                AppendLine($"{fnName}({argList});");
                return;
            }

            var target = Load(methodCall);
            Declare(target);
            AppendLine($"{target} = {fnName}({argList});");
        }

        /// <summary>
        /// Inline a multi-block helper method using STRUCTURED control flow.
        /// 
        /// CRITICAL: We CANNOT use a loop/switch state machine here because it violates
        /// WGSL's barrier uniformity requirement. Different threads would reach
        /// workgroupBarrier() at different loop iterations (e.g., thread 0 doing a 
        /// sequential reduction loop vs threads 1-63 skipping to the final barrier).
        /// 
        /// Instead, we use the same recursive structured code generation approach as 
        /// the kernel's GenerateStructuredCode: build post-dominators for the helper's
        /// CFG, then recursively emit if/else for IfBranch terminators, 
        /// loop { } for back-edges, and flat code for UnconditionalBranch.
        /// This ensures barriers appear at the SAME textual depth for ALL threads.
        /// </summary>
        private void InlineMultiBlockHelper(Method helperMethod, List<BasicBlock> blockList, Variable? resultVar)
        {
            // Build post-dominators from the helper's blocks to find merge points
            var helperCfg = helperMethod.Blocks.CreateCFG();
            var helperPostDom = helperCfg.Blocks.CreatePostDominators();
            
            // Track which blocks have been visited to avoid double-emission
            var helperVisited = new HashSet<BasicBlock>();
            
            // Track loop headers: a back-edge from B→A where A dominates B means A is a loop header
            var loopHeaders = new HashSet<BasicBlock>();
            foreach (var block in blockList)
            {
                if (block.Terminator is UnconditionalBranch ub && blockList.IndexOf(ub.Target) <= blockList.IndexOf(block))
                    loopHeaders.Add(ub.Target);
                if (block.Terminator is IfBranch ifb)
                {
                    if (blockList.IndexOf(ifb.TrueTarget) <= blockList.IndexOf(block))
                        loopHeaders.Add(ifb.TrueTarget);
                    if (blockList.IndexOf(ifb.FalseTarget) <= blockList.IndexOf(block))
                        loopHeaders.Add(ifb.FalseTarget);
                }
            }
            
            // Pre-declare ALL phi variables from the helper's blocks.
            // CRITICAL: Phi variables are shared across branches (e.g., used in both
            // if and else sides of a structured if/else). If we rely on lazy declaration
            // inside PushPhiValues, the var may be declared inside one branch scope
            // but referenced from the other branch or after the merge — a WGSL scoping
            // violation. Pre-declaring them here ensures they're at the correct outer scope.
            foreach (var block in blockList)
            {
                foreach (var valueEntry in block)
                {
                    if (valueEntry.Value is PhiValue phi)
                    {
                        var phiVar = Load(phi);
                        Declare(phiVar);
                    }
                    else break; // Phis are always at the start of a block
                }
            }
            
            // Recursively emit structured code starting from the entry block
            InlineStructuredBlock(blockList[0], null, helperPostDom, helperVisited, loopHeaders, resultVar);
        }
        
        /// <summary>
        /// Recursively generates structured WGSL code for an inlined helper block.
        /// This mirrors the kernel's GenerateStructuredCode pattern.
        /// </summary>
        /// <summary>
        /// Stack of loop exit targets. When inside a loop, any unconditional branch
        /// to the loop's exit block should emit "break;" instead of recursing.
        /// Pushed when entering a loop header, popped when exiting.
        /// </summary>
        private readonly Stack<BasicBlock> _loopExitStack = new();

        /// <summary>
        /// The current loop's header exit target. Set by GenerateLoopConstruct so that
        /// EmitIntermediateBlocksToExit can distinguish body-break-specific blocks from
        /// the shared merge block. Body-break exit chain blocks whose code is NOT the
        /// header exit target get emitted inside the break scope and marked visited.
        /// </summary>
        private BasicBlock? _currentLoopHeaderExitTarget;

        private void InlineStructuredBlock(
            BasicBlock current,
            BasicBlock? stop,
            global::ILGPU.IR.Analyses.Dominators<global::ILGPU.IR.Analyses.ControlFlowDirection.Backwards> postDom,
            HashSet<BasicBlock> visited,
            HashSet<BasicBlock> loopHeaders,
            Variable? resultVar)
        {
            if (current == null || current == stop || visited.Contains(current)) return;
            visited.Add(current);
            
            // If this is a loop header, wrap in a WGSL loop
            bool isLoopHeader = loopHeaders.Contains(current);
            if (isLoopHeader)
            {
                AppendLine("loop {");
                PushIndent();
            }
            
            // Emit the block's non-terminator values
            foreach (var valueEntry in current)
            {
                GenerateCodeFor(valueEntry.Value);
            }
            
            // Handle the block's terminator
            var terminator = current.Terminator;
            
            if (terminator is ReturnTerminator ret)
            {
                if (resultVar != null && !ret.IsVoidReturn)
                {
                    var retVal = Load(ret.ReturnValue.Resolve());
                    AppendLine($"{resultVar} = {retVal};");
                }
                // No code needed after return — structured flow naturally ends
                if (isLoopHeader)
                {
                    AppendLine("break;");
                    PopIndent();
                    AppendLine("}");
                }
            }
            else if (terminator is UnconditionalBranch ub)
            {
                PushPhiValues(ub);

                // Check if this branch exits a loop (targets a loop exit block).
                // This handles "break from inside if-block within a loop" — the PHI
                // values were pushed above, now we need to emit the actual "break;".
                bool isLoopExit = _loopExitStack.Count > 0 && _loopExitStack.Contains(ub.Target);

                if (isLoopExit)
                {
                    // Branch exits the enclosing loop — emit break
                    AppendLine("break;");
                    if (isLoopHeader)
                    {
                        PopIndent();
                        AppendLine("}");
                    }
                }
                else if (visited.Contains(ub.Target))
                {
                    // Back-edge: continue the loop (implicit in WGSL loop)
                    AppendLine("// loop continue (back-edge)");
                    if (isLoopHeader)
                    {
                        PopIndent();
                        AppendLine("}");
                    }
                }
                else
                {
                    // Forward edge: continue structured flow
                    if (isLoopHeader)
                    {
                        PopIndent();
                        AppendLine("}");
                    }
                    InlineStructuredBlock(ub.Target, stop, postDom, visited, loopHeaders, resultVar);
                }
            }
            else if (terminator is IfBranch ifb)
            {
                var trueTarget = ifb.TrueTarget;
                var falseTarget = ifb.FalseTarget;
                
                // Find the merge point where both branches converge
                var merge = postDom.GetImmediateDominator(current);
                
                var cond = Load(ifb.Condition.Resolve());
                
                // LOOP HEADER with IfBranch: one branch is the loop body (back-edge),
                // the other exits the loop. Emit as: if(!exitCond){break;} bodyCode;
                if (isLoopHeader)
                {
                    bool trueIsBackEdge = visited.Contains(trueTarget) || loopHeaders.Contains(trueTarget);
                    bool falseIsBackEdge = visited.Contains(falseTarget) || loopHeaders.Contains(falseTarget);
                    
                    BasicBlock bodyTarget, exitTarget;
                    string breakCond;
                    
                    if (!trueIsBackEdge && falseIsBackEdge)
                    {
                        // True exits, false continues → break if condition is true
                        bodyTarget = falseTarget;
                        exitTarget = trueTarget;
                        breakCond = $"{cond}";
                    }
                    else if (trueIsBackEdge && !falseIsBackEdge)
                    {
                        // False exits, true continues → break if condition is false
                        bodyTarget = trueTarget;
                        exitTarget = falseTarget;
                        breakCond = $"!{cond}";
                    }
                    else
                    {
                        // Both or neither are back-edges (unusual) — figure out by
                        // checking which target has already been visited
                        // Default: treat true as body
                        bodyTarget = trueTarget;
                        exitTarget = falseTarget;
                        breakCond = $"!{cond}";
                    }
                    
                    // Emit break condition
                    PushPhiValues(ifb, exitTarget);
                    AppendLine($"if ({breakCond}) {{ break; }}");
                    
                    // Emit phi values for loop body
                    PushPhiValues(ifb, bodyTarget);
                    
                    // Track loop exit so inner unconditional branches to it emit "break;"
                    _loopExitStack.Push(exitTarget);

                    // Emit loop body (recurse into body target, stop at this loop header)
                    if (!visited.Contains(bodyTarget))
                    {
                        InlineStructuredBlock(bodyTarget, current, postDom, visited, loopHeaders, resultVar);
                    }

                    _loopExitStack.Pop();

                    // Close the loop
                    PopIndent();
                    AppendLine("}"); // end loop { }
                    
                    // Emit exit path code AFTER the loop
                    if (!visited.Contains(exitTarget))
                    {
                        InlineStructuredBlock(exitTarget, stop, postDom, visited, loopHeaders, resultVar);
                    }
                    
                    // Continue from merge point (if different from exit target)
                    if (merge != null && merge != stop && !visited.Contains(merge))
                    {
                        InlineStructuredBlock(merge, stop, postDom, visited, loopHeaders, resultVar);
                    }
                }
                else
                {
                    // Non-loop IfBranch: standard structured if/else
                    // Save visited state before true branch for short-circuit patterns
                    var visitedBeforeTrueBranch = new HashSet<BasicBlock>(visited);
                    
                    // Push phi values for true branch
                    PushPhiValues(ifb, trueTarget);
                    AppendLine($"if ({cond}) {{");
                    PushIndent();
                    
                    // Check if true target is a back-edge
                    if (visited.Contains(trueTarget))
                    {
                        AppendLine("// loop continue (true branch back-edge)");
                    }
                    else
                    {
                        InlineStructuredBlock(trueTarget, merge, postDom, visited, loopHeaders, resultVar);
                    }
                    
                    PopIndent();
                    AppendLine("} else {");
                    PushIndent();
                    
                    // Push phi values for false branch
                    PushPhiValues(ifb, falseTarget);
                    
                    // Restore visited state for false branch
                    visited.IntersectWith(visitedBeforeTrueBranch);
                    
                    // Check if false target is a back-edge
                    if (visited.Contains(falseTarget))
                    {
                        AppendLine("// loop continue (false branch back-edge)");
                    }
                    else
                    {
                        InlineStructuredBlock(falseTarget, merge, postDom, visited, loopHeaders, resultVar);
                    }
                    
                    PopIndent();
                    AppendLine("}");
                    
                    // Continue from merge point
                    if (merge != null && merge != stop)
                    {
                        InlineStructuredBlock(merge, stop, postDom, visited, loopHeaders, resultVar);
                    }
                }
            }
            else if (terminator is SwitchBranch sb)
            {
                var selector = Load(sb.Condition.Resolve());
                var merge = postDom.GetImmediateDominator(current);
                
                AppendLine($"switch (i32({selector})) {{");
                PushIndent();
                
                for (int i = 0; i < sb.NumCasesWithoutDefault; i++)
                {
                    AppendLine($"case {i}: {{");
                    PushIndent();
                    var caseTarget = sb.GetCaseTarget(i);
                    if (!visited.Contains(caseTarget))
                    {
                        InlineStructuredBlock(caseTarget, merge, postDom, visited, loopHeaders, resultVar);
                    }
                    PopIndent();
                    AppendLine("}");
                }
                
                AppendLine("default: {");
                PushIndent();
                if (!visited.Contains(sb.DefaultBlock))
                {
                    InlineStructuredBlock(sb.DefaultBlock, merge, postDom, visited, loopHeaders, resultVar);
                }
                PopIndent();
                AppendLine("}");
                
                PopIndent();
                AppendLine("}");
                
                if (isLoopHeader)
                {
                    PopIndent();
                    AppendLine("}");
                }
                
                if (merge != null && merge != stop)
                {
                    InlineStructuredBlock(merge, stop, postDom, visited, loopHeaders, resultVar);
                }
            }
            else if (terminator != null)
            {
                // Fallback
                GenerateCodeFor(terminator);
                if (isLoopHeader)
                {
                    PopIndent();
                    AppendLine("}");
                }
            }
        }

        /// <summary>
        /// Push phi values for a branch terminator (unconditional branch).
        /// Phi values in ILGPU represent SSA merge points — when branching to a block,
        /// we assign the branch's source values to the phi destination variables.
        /// </summary>
        private void PushPhiValues(UnconditionalBranch branch)
        {
            var targetBlock = branch.Target;
            foreach (var valueEntry in targetBlock)
            {
                if (valueEntry.Value is PhiValue phi)
                {
                    // Find the source value for this phi from the current block
                    for (int i = 0; i < phi.Nodes.Length; i++)
                    {
                        if (phi.Sources[i] == branch.BasicBlock)
                        {
                            var srcValue = phi.Nodes[i].Resolve();
                            var phiVar = Load(phi);
                            Declare(phiVar); // Ensure phi var is declared (Allocate doesn't call Declare)
                            var srcVar = Load(srcValue);
                            AppendLine($"{phiVar} = {srcVar};");
                        }
                    }
                }
                else break; // Phis are always at the start of a block
            }
        }

        /// <summary>
        /// Push phi values for a conditional branch (IfBranch) to a specific target block.
        /// </summary>
        private void PushPhiValues(IfBranch branch, BasicBlock targetBlock)
        {
            foreach (var valueEntry in targetBlock)
            {
                if (valueEntry.Value is PhiValue phi)
                {
                    for (int i = 0; i < phi.Nodes.Length; i++)
                    {
                        if (phi.Sources[i] == branch.BasicBlock)
                        {
                            var srcValue = phi.Nodes[i].Resolve();
                            var phiVar = Load(phi);
                            Declare(phiVar); // Ensure phi var is declared (Allocate doesn't call Declare)
                            var srcVar = Load(srcValue);
                            AppendLine($"{phiVar} = {srcVar};");
                        }
                    }
                }
                else break;
            }
        }

        public override void GenerateCode()
        {
            // CRITICAL: Set i64/f64 emulation flags BEFORE body generation.
            // GenerateCode() runs before GenerateHeader() (see CodeGeneratorBackend.Compile).
            // Without this, KernelUsesI64 is null during body generation, causing the
            // TypeGenerator to fall back to Backend.EnableI64Emulation (true),
            // emitting emu_i64 types and i64_from_i32 calls. But GenerateHeader() later
            // sets KernelUsesI64=false (if the kernel's own IR has no Int64) and omits the
            // emulation library — producing unresolved call targets in the WGSL.
            // Fix: scan kernel AND helper methods' IR here so body and header are consistent.
            SetEmulationFlags();

            // Pre-scan for subgroup, broadcast, and barrier usage BEFORE SetupIndexVariables().
            // The range check (early return for excess threads) breaks WGSL static uniformity
            // analysis for kernels using subgroups or barriers, so we need these flags set first.
            ScanForSubgroupAndBroadcastUsage();

            var _kernelMethodName = Method.Handle.Name ?? "unknown";
            _isScanKernel = _kernelMethodName.Contains("MultiPassScan") || _kernelMethodName.Contains("SingleGroupScan")
                         || _kernelMethodName.Contains("RadixSort");

            string workgroupSize = GetWorkgroupSize();

            // --- BODY-FIRST GENERATION ---
            // We need to know if the body uses subgroup builtins BEFORE emitting the signature.
            // The IR pre-scan (_usesSubgroups) may miss inlined methods. Instead, generate the
            // body into a temporary StringBuilder, check if it references subgroup_size or
            // subgroup_invocation_id, then emit the correct function signature.

            // Save the current builder position so we can inject the signature before the body.
            int signatureInsertPosition = Builder.Length;

            // Emit a placeholder for the signature (we'll replace it after generating the body).
            // We use a unique sentinel that we'll replace with the real signature.
            const string signatureSentinel = "/*__WGSL_SIGNATURE_PLACEHOLDER__*/";
            Builder.AppendLine(signatureSentinel);

            PushIndent();

            // workgroup_size is a module-scope const — no runtime assignment needed.

            // 0. Pre-scan parameters to identify emulated 64-bit types
            // This MUST happen before hoisting so that Load/Store know which buffers are emulated
            PreScanEmulatedParameters();

            // 1. Scan and declare hoisted variables
            HoistCrossBlockVariables();

            // 2. Setup standard bindings
            SetupIndexVariables();
            SetupParameterBindings();

            // FIX: Set up kernel's own local allocations (e.g., Alloca for `out` parameters).
            // These are NOT handled by HoistCrossBlockVariables (skips pointers) and NOT
            // handled by the inlining code (which only sets up the HELPER's local allocations).
            // Without this, alloca-backed variables (like `v_22` for `out ScanBoundaries<T>`)
            // are used but never declared, causing WGSL compilation errors.
            SetupAllocations(Allocas.LocalAllocations, MemoryAddressSpace.Local);

            // 3. START CONTROL FLOW
            // Save position before code generation for post-processing
            int codeGenStartPosition = Builder.Length;

            if (Method.Blocks.Count == 1)
            {
                GenerateCodeInternal();
            }
            else if (_loops.Count == 0) // No Loops -> Acyclic
            {
                // Enable deferred declarations: in WGSL, 'var' inside if/else/loop is
                // block-scoped. Variables first encountered in one branch would be invisible
                // to sibling branches. Deferring declarations to function scope fixes this.
                _useDeferredDeclarations = true;
                _deferredVarDeclarations.Clear();
                var pd = global::ILGPU.IR.Analyses.Dominators.CreatePostDominators(Method.Blocks);
                GenerateStructuredCode(Method.EntryBlock, null, pd);
                _useDeferredDeclarations = false;
                // Insert deferred declarations at the start of code gen (function scope)
                if (_deferredVarDeclarations.Count > 0)
                {
                    var deferred = string.Join(Environment.NewLine, _deferredVarDeclarations) + Environment.NewLine;
                    Builder.Insert(codeGenStartPosition, deferred);
                    codeGenStartPosition += deferred.Length;
                }
            }
            else
            {
                // Enable deferred declarations (same rationale as acyclic path above)
                _useDeferredDeclarations = true;
                _deferredVarDeclarations.Clear();
                var pd = global::ILGPU.IR.Analyses.Dominators.CreatePostDominators(Method.Blocks);
                GenerateStructuredCode(Method.EntryBlock, null, pd);
                _useDeferredDeclarations = false;
                if (_deferredVarDeclarations.Count > 0)
                {
                    var deferred = string.Join(Environment.NewLine, _deferredVarDeclarations) + Environment.NewLine;
                    Builder.Insert(codeGenStartPosition, deferred);
                    codeGenStartPosition += deferred.Length;
                }
            }

            // CRITICAL: Post-process to fix variable scoping issues.
            // HoistCrossBlockVariables() declares cross-block vars at function scope, but its
            // analysis may miss variables used by terminators in other blocks (e.g., CompareValue
            // used as IfBranch condition). Code generators also emit "let v_N = expr;" which creates
            // block-scoped bindings that shadow hoisted vars. Fix: unconditionally convert ALL
            // "let v_N = expr;" to assignments, and add missing var declarations at function scope.
            // Pointer references (&param, &temp_) must remain as 'let' since they're immutable bindings.
            if (Method.Blocks.Count > 1)
            {
                string generatedCode = Builder.ToString(codeGenStartPosition, Builder.Length - codeGenStartPosition);
                var missingDeclarations = new List<string>(); // var declarations to add
                var hoistedLetDeclarations = new List<string>(); // pointer-alias lets to hoist
                // Name -> WGSL type for resolving arithmetic operand types in the missing-decl path.
                // Seeded with the ALREADY-EMITTED `var v_N : type;` declarations (the hoisted
                // inlined-helper temps - these are inlining-generated, NOT kernel IR values, so they
                // are absent from valueVariables), then extended as we declare each missing-decl temp.
                // This lets a LATER temp's arithmetic inherit the type of an EARLIER hoisted OR
                // missing-decl operand (the inlined-helper temps chain: v_a = acc+bias (f32);
                // v_b = v_a * v_c (f32); ...). Without it, the 2nd+ link falls back to the i32 default.
                var missingDeclInferredTypes = new Dictionary<string, string>(System.StringComparer.Ordinal);
                foreach (System.Text.RegularExpressions.Match dm in
                    System.Text.RegularExpressions.Regex.Matches(generatedCode, @"\bvar\s+(v_\w+)\s*:\s*([\w<>]+)\s*;"))
                {
                    missingDeclInferredTypes[dm.Groups[1].Value] = dm.Groups[2].Value;
                }
                bool anyReplacements = false;
                string processed = s_inlineLetPattern.Replace(generatedCode, match =>
                {
                    string indent = match.Groups[1].Value;
                    string varName = match.Groups[2].Value;
                    string expr = match.Groups[3].Value;

                    // Pointer aliases (expressions starting with '&') can't be declared as 'var'
                    // in WGSL — they must remain as 'let'. But 'let' has block scope, so if they're
                    // inside a case block and used from other case blocks, we must hoist them to
                    // function scope. ONLY hoist simple pointer aliases like &v_67 (references to
                    // module-scope shared memory). DO NOT hoist indexed expressions like &v_66[v_82]
                    // since those depend on block-local variables.
                    if (expr.TrimStart().StartsWith("&") || _viewPointerVarNames.Contains(expr.Trim()))
                    {
                        var trimmed = expr.TrimStart();
                        if (_viewPointerVarNames.Contains(expr.Trim()))
                        {
                            // The RHS is a pointer alias variable (e.g. v_27_f0, which is 'let v_27_f0 = &param1_f0;').
                            // WGSL cannot assign pointers to 'var' variables — keep as 'let' and hoist to function scope.
                            hoistedLetDeclarations.Add($"    let {varName} = {trimmed};");
                            _viewPointerVarNames.Add(varName); // Also track v_28 as a pointer alias
                            anyReplacements = true;
                            return ""; // Remove from case block; will be emitted at function scope
                        }
                        // Simple pointer alias: &identifier (no brackets, no dots, no operators)
                        // e.g. &v_67, &param0 — these reference module-scope or function-scope vars
                        bool isSimpleRef = IsAmpersandWordChars(trimmed);
                        if (isSimpleRef)
                        {
                            hoistedLetDeclarations.Add($"    let {varName} = {trimmed};");
                            anyReplacements = true;
                            return ""; // Remove from case block; will be emitted at function scope
                        }
                        // Complex pointer expression (e.g. &v_66[v_82]) — keep as-is in block
                        return match.Value;
                    }

                    anyReplacements = true;

                    // If this variable wasn't already declared by HoistCrossBlockVariables,
                    // we need to add a var declaration at function scope
                    if (!declaredVariables.Contains(varName))
                    {
                        // Infer type from expression patterns
                        string inferredType = InferWgslType(expr);

                        // Try valueVariables lookup for more accurate type
                        foreach (var kvp in valueVariables)
                        {
                            if (kvp.Value.Name == varName)
                            {
                                inferredType = kvp.Value.Type;
                                break;
                            }
                        }

                        // RHS-lookup fallback: when `expr` is a bare identifier naming
                        // another tracked variable, inherit THAT variable's type instead
                        // of guessing from expression shape. This fixes the case where
                        // `let v_X = v_alloca;` aliases a LocalMemory `array<T, N>` alloca
                        // through a ViewType IR value. TypeGenerator maps ViewType to its
                        // element type (i32), which is wrong for `var` declarations that
                        // will then be indexed via `&v_X[i]`. Propagating the underlying
                        // alloca's array type preserves indexability. Without this, the
                        // subsequent `*&v_X[i] = ...;` stores write into a scalar and get
                        // silently discarded (Tuvok's 2026-04-24 VP9 iDCT 8x8 report).
                        var exprTrimmed = expr.Trim();
                        if (IsSimpleIdentifier(exprTrimmed))
                        {
                            foreach (var kvp in valueVariables)
                            {
                                if (kvp.Value.Name == exprTrimmed)
                                {
                                    inferredType = kvp.Value.Type;
                                    break;
                                }
                            }
                        }

                        // Binary-arithmetic operand-type lookup: InferWgslType defaults
                        // arithmetic (A + B, A * B, ...) to i32 from expression SHAPE alone,
                        // which is wrong for float/u32 operands. WGSL requires both operands of
                        // an arithmetic op to share the result type, so inherit the type of a
                        // tracked non-i32 operand. Fixes inlined-helper temps like FusedActivate's
                        // `v = acc + bias` (f32+f32) being declared i32 and rejected by Tint
                        // ("cannot assign f32 to i32"). Bug #3 (FusedRegBlockedLinearActivation),
                        // 2026-06-09.
                        if (inferredType == "i32"
                            && TryInferArithmeticOperandType(exprTrimmed, missingDeclInferredTypes, out var arithOperandType))
                        {
                            inferredType = arithOperandType;
                        }
                        // Record this temp's resolved type so a later missing-decl temp that does
                        // arithmetic on it (the inlined-helper chain) can inherit it.
                        missingDeclInferredTypes[varName] = inferredType;

                        // WGSL: pointer types are not constructible and can't be declared
                        // as 'var'. They will be bound as 'let' at their point of use.
                        if (!inferredType.StartsWith("ptr<"))
                        {
                            if (WebGPU.Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[DIAG-MISSING-DECL] Adding var {varName} : {inferredType}");
                            missingDeclarations.Add($"    var {varName} : {inferredType};");
                        }
                        else
                        {
                            if (WebGPU.Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[DIAG-MISSING-SKIP] Skipping ptr var {varName} : {inferredType}");
                        }
                        declaredVariables.Add(varName);
                    }

                    // Convert "let v_N = expr;" to "v_N = expr;" (assignment only)
                    return $"{indent}{varName} = {expr};";
                });

                if (anyReplacements)
                {
                    Builder.Remove(codeGenStartPosition, Builder.Length - codeGenStartPosition);

                    // Insert missing var declarations at the function scope (before generated code)
                    if (missingDeclarations.Count > 0)
                    {
                        foreach (var decl in missingDeclarations)
                            Builder.AppendLine(decl);
                    }

                    // Insert hoisted pointer-alias let declarations at function scope
                    if (hoistedLetDeclarations.Count > 0)
                    {
                        foreach (var decl in hoistedLetDeclarations)
                            Builder.AppendLine(decl);
                    }

                    Builder.Append(processed);
                }

                // Note: Barrier extraction post-processing is no longer needed.
                // With structured loop generation, barriers are emitted directly
                // in the loop body at the correct scope, ensuring uniform control flow.

            }

            // PHASE 3: Hoist ALL 'var v_N' declarations to function scope.
            //
            // WGSL block-scopes 'var' declarations: a variable declared inside an
            // if/else/loop body is invisible to sibling scopes. The structured code
            // generator can emit the same IR block in multiple WGSL scopes (via
            // UnvisitSharedTargets), and separate IR PrimitiveValue nodes for the
            // same constant get the same variable name via Load() caching. This
            // causes "unresolved value" errors when one scope's 'var' is referenced
            // from a sibling scope.
            //
            // Fix: unconditionally move every 'var v_N' declaration to function
            // scope. Only v_N-prefixed variables are touched — local arrays, structs,
            // and other naming conventions are left in place.
            if (Method.Blocks.Count > 1)
            {
                string code = Builder.ToString(codeGenStartPosition, Builder.Length - codeGenStartPosition);

                var hoistedVars = new Dictionary<string, string>();

                string hoisted = s_varHoistPattern.Replace(code, match =>
                {
                    string indent = match.Groups[1].Value;
                    string varName = match.Groups[2].Value;
                    string varType = match.Groups[3].Value.Trim();
                    bool hasInit = match.Groups[4].Success && match.Groups[4].Value.Length > 0;
                    string initExpr = hasInit ? match.Groups[4].Value : null;

                    if (!hoistedVars.ContainsKey(varName))
                        hoistedVars[varName] = varType;

                    if (hasInit)
                        return $"{indent}{varName} = {initExpr};";

                    return "";
                });

                // Bug #3 fixpoint: a temp declared `i32` but assigned binary ARITHMETIC whose
                // operand is non-i32 must take the operand's type (WGSL requires both arithmetic
                // operands to share the result type). The string-heuristic that typed missing-decl
                // temps can't see operand types across the inlined-helper f32 chain (acc+bias ->
                // v_b = v_a * v_c -> ...). Here the COMPLETE name->type map exists, so resolve to a
                // fixpoint over the assignments. Monotonic (i32 -> non-i32 only) => terminates.
                {
                    // COMPLETE operand-type map: hoistedVars (the code-window decls just scanned)
                    // PLUS the HoistCrossBlockVariables declarations emitted BEFORE
                    // codeGenStartPosition (absent from `code`, hence absent from hoistedVars - this
                    // is exactly why the f32 operand v_10015 was invisible). Corrections update both
                    // maps so the chain resolves in any order.
                    var allKnownTypes = new Dictionary<string, string>(hoistedVars, System.StringComparer.Ordinal);
                    foreach (System.Text.RegularExpressions.Match pm in
                        s_varHoistPattern.Matches(Builder.ToString(0, codeGenStartPosition)))
                    {
                        allKnownTypes[pm.Groups[2].Value] = pm.Groups[3].Value.Trim();
                    }
                    var s_assignRe = new System.Text.RegularExpressions.Regex(
                        @"(?m)^\s*(v_\w+)\s*=\s*([^;]+);");
                    var arithOps = new[] { " + ", " - ", " * ", " / ", " % " };
                    bool changed = true;
                    while (changed)
                    {
                        changed = false;
                        foreach (System.Text.RegularExpressions.Match am in s_assignRe.Matches(hoisted))
                        {
                            var lhs = am.Groups[1].Value;
                            if (!hoistedVars.TryGetValue(lhs, out var lhsType) || lhsType != "i32")
                                continue;
                            var rhs = am.Groups[2].Value;
                            if (rhs.Contains("==") || rhs.Contains("i32(")) continue;
                            if (!(rhs.Contains(" + ") || rhs.Contains(" - ") || rhs.Contains(" * ")
                                  || rhs.Contains(" / ") || rhs.Contains(" % ")))
                                continue;
                            foreach (var op in rhs.Split(arithOps, System.StringSplitOptions.RemoveEmptyEntries))
                            {
                                var tok = op.Trim().Trim('(', ')').Trim();
                                if (allKnownTypes.TryGetValue(tok, out var ot)
                                    && ot != "i32" && ot != "bool" && !ot.StartsWith("ptr<"))
                                {
                                    hoistedVars[lhs] = ot;
                                    allKnownTypes[lhs] = ot;
                                    changed = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (hoistedVars.Count > 0)
                {
                    Builder.Remove(codeGenStartPosition, Builder.Length - codeGenStartPosition);
                    foreach (var kvp in hoistedVars)
                        Builder.AppendLine($"    var {kvp.Key} : {kvp.Value};");
                    Builder.Append(hoisted);
                }
            }

            // PHASE 4: Dead variable elimination.
            // Remove hoisted "var v_N : type;" pre-declarations that are never
            // referenced elsewhere in the function body. Hoisting pre-declares
            // variables for all cross-block values, but structured code generation
            // may eliminate the code paths that use some of them.
            // IMPORTANT: Only target "var v_" (hoisted pre-declarations). Never
            // remove "let v_" lines — those are active computations whose removal
            // can break binding layout detection and eliminate needed values.
            {
                string fullBody = Builder.ToString(signatureInsertPosition,
                    Builder.Length - signatureInsertPosition);
                var lines = fullBody.Split('\n');
                int removedCount = 0;

                for (int li = 0; li < lines.Length; li++)
                {
                    if (lines[li] == null) continue;
                    var trimmed = lines[li].TrimStart();
                    string varName = null;

                    // Match: "var v_N :" or "var v_N_suffix :"
                    if (trimmed.StartsWith("var v_"))
                    {
                        int nameEnd = trimmed.IndexOfAny(new[] { ' ', ':' }, 4);
                        if (nameEnd > 4)
                            varName = trimmed.Substring(4, nameEnd - 4);
                    }

                    if (varName == null) continue;
                    string fullVarName = varName;

                    // Check if this variable appears on any other line (word-boundary match)
                    bool referenced = false;
                    for (int lj = 0; lj < lines.Length; lj++)
                    {
                        if (lj == li || lines[lj] == null) continue;
                        if (ContainsWordBoundary(lines[lj], fullVarName))
                        {
                            referenced = true;
                            break;
                        }
                    }

                    if (!referenced)
                    {
                        lines[li] = null; // Mark for removal
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    Builder.Remove(signatureInsertPosition, Builder.Length - signatureInsertPosition);
                    for (int li = 0; li < lines.Length; li++)
                    {
                        if (lines[li] != null)
                        {
                            if (li > 0 && Builder.Length > signatureInsertPosition)
                                Builder.Append('\n');
                            Builder.Append(lines[li]);
                        }
                    }
                    if (WebGPUBackend.VerboseLogging)
                        WebGPUBackend.Log($"[DeadVarElim] Removed {removedCount} unused declarations");
                }
            }

            PopIndent();
            Builder.AppendLine("}"); // End Function

            // --- NOW BUILD THE REAL SIGNATURE ---
            // Check the generated body to see if it actually uses subgroup builtins.
            // This is more reliable than the IR pre-scan which may miss inlined methods.
            string bodyText = Builder.ToString(signatureInsertPosition + signatureSentinel.Length + Environment.NewLine.Length,
                Builder.Length - signatureInsertPosition - signatureSentinel.Length - Environment.NewLine.Length);
            bool bodyUsesSubgroups = Backend.HasSubgroups &&
                (bodyText.Contains("subgroup_size") || bodyText.Contains("subgroup_invocation_id") ||
                 bodyText.Contains("subgroupShuffle") || bodyText.Contains("subgroupAdd") ||
                 bodyText.Contains("subgroupBroadcastFirst") || bodyText.Contains("subgroup_id"));

            // Determine which subgroup builtins to include in the entry point signature.
            // @builtin(subgroup_id) requires 'chromium_experimental_subgroup_matrix' which
            // is NOT part of the standard 'subgroups' extension. We compute subgroup_id
            // from available builtins instead: local_invocation_index / subgroup_size.
            bool bodyNeedsSubgroups = bodyUsesSubgroups || _usesGroupReduce;

            // Marker (parsed by WebGPUCompiledKernel.UsesGridIdxZ) so the dispatch path knows whether the
            // Z dimension is FREE for an X-workgroup-overflow fold. A kernel that reads Grid.IdxZ owns Z, so
            // its X-overflow must NOT be folded into Z. A 2D kernel (Grid.IdxX/IdxY only) leaves Z free.
            string gridIdxZMarker = KernelUsesGridIdxZ() ? "// @uses_grid_idx_z\n" : "";
            // Rename entry point builtins to _ep_xxx to avoid clashing with module-scope var<private>
            string signature = gridIdxZMarker + $"@compute @workgroup_size({workgroupSize})\n" +
                "fn main(@builtin(global_invocation_id) _ep_global_id : vec3<u32>, " +
                "@builtin(local_invocation_id) _ep_local_id : vec3<u32>, " +
                "@builtin(workgroup_id) _ep_group_id : vec3<u32>, " +
                "@builtin(num_workgroups) _ep_num_workgroups : vec3<u32>, " +
                "@builtin(local_invocation_index) _ep_local_index : u32" +
                (bodyNeedsSubgroups ? ", @builtin(subgroup_invocation_id) subgroup_invocation_id : u32, @builtin(subgroup_size) subgroup_size : u32" : "") +
                ") {";

            string builtinCopies = "\n" +
                "    // All builtins use 'let' (not var<private>) for WGSL uniformity analysis.\n" +
                "    // 'let' preserves uniformity tracking from @builtin parameters.\n" +
                "    let global_id = _ep_global_id;\n" +
                "    let local_id = _ep_local_id;\n" +
                "    let group_id = _ep_group_id;\n" +
                "    let num_workgroups = _ep_num_workgroups;\n" +
                "    let local_index = _ep_local_index;\n" +
                "    // Seed the runtime-zero used by F32 special-value helpers from a\n" +
                "    // @builtin value so Naga's const-evaluator cannot fold it. Required\n" +
                "    // because `bitcast<f32>(const_u32_with_inf_bit_pattern)` is rejected\n" +
                "    // at shader creation. See WGSLEmulationLibrary.F32SpecialValueFunctions.\n" +
                "    _ilgpu_runtime_zero = local_id.x ^ local_id.x;\n" +
                (bodyNeedsSubgroups ? "    let subgroup_id = _ep_local_index / subgroup_size;\n" : "");

            // Replace the sentinel with the real signature + builtin copies
            string fullOutput = Builder.ToString(signatureInsertPosition, Builder.Length - signatureInsertPosition);
            fullOutput = fullOutput.Replace(signatureSentinel, signature + builtinCopies);

            Builder.Remove(signatureInsertPosition, Builder.Length - signatureInsertPosition);
            Builder.Append(fullOutput);
            // NOTE: The enable subgroups placeholder is resolved in WebGPUBackend.CreateKernel()
            // because the header is written to the upstream builder, not this.Builder.

            // Diagnostic WGSL dump (enable when debugging specific kernels)
            // if (_kernelMethodName.Contains("SomeKernel"))
            // {
            //     var escaped = fullOutput.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
            //     Console.WriteLine($"WGSL_DUMP[{_kernelMethodName}|loops={_loops.Count}]:{escaped}:END_WGSL_DUMP");
            // }
        }
        private string GetPrefix(Value value)
        {
            // If the variable was hoisted to the top of main(), it's already a 'var'
            // and we must use a direct assignment (no prefix).
            // Otherwise, we use 'let ' to declare it locally.
            return _hoistedPrimitives.Contains(value) ? "" : "let ";
        }

        /// <summary>
        /// Checks if <paramref name="text"/> contains <paramref name="word"/> as a
        /// whole word (surrounded by non-word characters or string boundaries).
        /// Used by dead variable elimination to avoid false positives like v_1 matching v_10.
        /// </summary>
        private static bool ContainsWordBoundary(string text, string word)
        {
            int pos = 0;
            while (pos <= text.Length - word.Length)
            {
                int idx = text.IndexOf(word, pos, StringComparison.Ordinal);
                if (idx < 0) return false;

                bool startOk = idx == 0 || !IsWordChar(text[idx - 1]);
                bool endOk = idx + word.Length >= text.Length || !IsWordChar(text[idx + word.Length]);
                if (startOk && endOk) return true;

                pos = idx + 1;
            }
            return false;
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        // FixGridStrideLoopUniformity — DELETED (Phase 0.4)
        // The regex-based post-processing pass has been replaced by direct
        // uniform break emission in GenerateLoopConstruct() with proper
        // tile loop vs grid-stride loop classification.
        // See: ClassifyLoopType(), TryRemoveGroupIndex(), FindPhiInitValue()


        /// <summary>
        /// Infers the WGSL type from an expression string for post-processing var declarations.
        /// </summary>
        /// <summary>
        /// True when <paramref name="s"/> looks like a bare WGSL identifier (ASCII
        /// letters, digits, underscore; must start with letter or underscore). Used by
        /// the post-codegen missing-declaration pass to decide when to look up the
        /// RHS of `let v_X = expr;` in valueVariables to inherit its type (instead of
        /// heuristically inferring from expression shape).
        /// </summary>
        private static bool IsSimpleIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char first = s[0];
            if (!char.IsLetter(first) && first != '_') return false;
            for (int i = 1; i < s.Length; i++)
            {
                char c = s[i];
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return true;
        }

        /// <summary>
        /// For a binary ARITHMETIC expression (<c>A + B</c>, <c>A * B</c>, ...) in the
        /// missing-declaration fallback, returns the WGSL result type by inheriting the type
        /// of the first tracked operand variable that isn't the <c>i32</c> default. WGSL
        /// requires both operands of an arithmetic op to share the result type, so any tracked
        /// operand's type IS the result type. Used to override <see cref="InferWgslType"/>'s
        /// shape-only <c>i32</c> guess for float/u32 arithmetic (e.g. FusedActivate's
        /// <c>v = acc + bias</c>). Returns false when no non-i32 operand can be resolved.
        /// </summary>
        private bool TryInferArithmeticOperandType(
            string expr, Dictionary<string, string> localTypes, out string type)
        {
            type = "i32";
            if (!(expr.Contains(" + ") || expr.Contains(" - ") || expr.Contains(" * ")
                || expr.Contains(" / ") || expr.Contains(" % ")))
                return false;
            var operands = expr.Split(
                new[] { " + ", " - ", " * ", " / ", " % " },
                System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in operands)
            {
                var tok = raw.Trim().Trim('(', ')').Trim();
                if (!IsSimpleIdentifier(tok)) continue;
                // Earlier missing-decl temp in the same inlined-helper chain (e.g. the f32 acc+bias
                // temp feeding `v_b = v_a * v_c`). Checked first so the chain resolves in source order.
                if (localTypes != null && localTypes.TryGetValue(tok, out var lt) && lt != "i32")
                {
                    type = lt;
                    return true;
                }
                foreach (var kvp in valueVariables)
                {
                    if (kvp.Value.Name == tok)
                    {
                        if (kvp.Value.Type != "i32")
                        {
                            type = kvp.Value.Type;
                            return true;
                        }
                        break;
                    }
                }
            }
            return false;
        }

        private static string InferWgslType(string expr)
        {
            // Comparison operators and emulated comparison functions produce bool
            if (expr.Contains("f64_lt") || expr.Contains("f64_le") || expr.Contains("f64_gt") ||
                expr.Contains("f64_ge") || expr.Contains("f64_eq") || expr.Contains("f64_ne") ||
                expr.Contains("i64_lt") || expr.Contains("i64_le") || expr.Contains("i64_gt") ||
                expr.Contains("i64_ge") || expr.Contains("i64_eq") || expr.Contains("i64_ne") ||
                expr.Contains("u64_lt") || expr.Contains("u64_le") || expr.Contains("u64_gt") ||
                expr.Contains("u64_ge"))
                return "bool";
            // Comparison operators produce bool
            if (expr.Contains(" != ") || expr.Contains(" == ") || expr.Contains(" >= ") ||
                expr.Contains(" <= ") || expr.Contains(" > ") || expr.Contains(" < "))
                return "bool";
            // Bitwise ops: result type depends on operand types.
            // If wrapped in select() (from emit-site bool→i32 conversion), result is i32.
            // Otherwise, in MISSING-DECL path (inlined helpers), bool & bool is more common
            // than integer bitwise AND (which is handled by IR-aware hoisting path).
            if (expr.Contains(" | ") || expr.Contains(" & ") || expr.Contains(" ^ "))
            {
                if (expr.Contains("select("))
                    return "i32";
                return "bool";
            }
            // Arithmetic operators return integers
            if (expr.Contains(" * ") || expr.Contains(" / ") || expr.Contains(" % ") ||
                expr.Contains(" + ") || expr.Contains(" - "))
                return "i32";
            // emu_f64 emulation functions return vec2<f32>
            if (expr.Contains("f64_from_f32") || expr.Contains("f64_add") || expr.Contains("f64_sub") ||
                expr.Contains("f64_mul") || expr.Contains("f64_div") || expr.Contains("f64_neg") ||
                expr.Contains("f64_from_ieee754") || expr.Contains("emu_f64(") ||
                expr.Contains("f64_abs") || expr.Contains("f64_min") || expr.Contains("f64_max"))
                return "vec2<f32>";
            // emu_i64 emulation functions return vec2<u32>
            if (expr.Contains("i64_from_i32") || expr.Contains("u64_from_u32") ||
                expr.Contains("i64_add") || expr.Contains("i64_sub") || expr.Contains("i64_mul") ||
                expr.Contains("u64_mul") || expr.Contains("i64_neg") || expr.Contains("i64_abs") ||
                expr.Contains("i64_and") || expr.Contains("i64_or") || expr.Contains("i64_xor") ||
                expr.Contains("i64_shl") || expr.Contains("i64_shr") || expr.Contains("u64_shr") ||
                expr.Contains("i64_not") || expr.Contains("emu_i64(") || expr.Contains("emu_u64("))
                return "vec2<u32>";
            // Extraction functions return scalars
            if (expr.Contains("f64_to_f32") || expr.Contains("f32("))
                return "f32";
            if (expr.Contains("i64_to_i32"))
                return "i32";
            if (expr.Contains("u64_to_u32"))
                return "u32";
            if (expr.Contains("i32("))
                return "i32";
            if (expr.Contains("u32("))
                return "u32";
            // Default to i32 (most common for unlisted arithmetic patterns)
            return "i32";
        }

        private string GetWorkgroupSize()
        {
            // If the kernel was compiled with a specific MaxWorkgroupSize
            // (from KernelSpecialization.MaxNumThreadsPerGroup, passed via
            // SpecializedValue<int>(groupDim)), use that exact value.
            // This is critical for explicitly grouped kernels where the
            // shared memory layout and ExclusiveScan thread count depend
            // on the compiled group size matching @workgroup_size.
            if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
            {
                WebGPUBackend.Log($"[WorkgroupSize] MaxWorkgroupSize={_generatorArgs.MaxWorkgroupSize}, IsExplicitlyGrouped={EntryPoint.IsExplicitlyGrouped}, IndexType={EntryPoint.IndexType}");
                WebGPUBackend.Log($"[WorkgroupSize] SharedAllocations count={_generatorArgs.SharedAllocations.Length}");
                foreach (var alloca in _generatorArgs.SharedAllocations)
                {
                    WebGPUBackend.Log($"[WorkgroupSize]   SharedAlloc: type={alloca.ElementType}, arraySize={alloca.ArraySize}");
                }
            }

            if (_generatorArgs.MaxWorkgroupSize.HasValue)
            {
                int wgSize = _generatorArgs.MaxWorkgroupSize.Value;
                if (WebGPU.Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WorkgroupSize] Using MaxWorkgroupSize={wgSize}");
                return EntryPoint.IndexType switch
                {
                    IndexType.Index1D => $"{wgSize}",
                    IndexType.Index2D => $"{(int)Math.Sqrt(wgSize)}, {(int)Math.Sqrt(wgSize)}",
                    IndexType.Index3D => $"{(int)Math.Cbrt(wgSize)}, {(int)Math.Cbrt(wgSize)}, {(int)Math.Cbrt(wgSize)}",
                    _ => $"{wgSize}"
                };
            }

            // For explicitly grouped kernels with shared memory, try to infer the workgroup
            // size from the largest shared memory allocation. ILGPU's algorithms (like RadixSort)
            // typically size shared memory proportional to groupSize * factor, and the allocation
            // size is known at compile time from the inlined SpecializedValue constant.
            if (EntryPoint.IsExplicitlyGrouped && _generatorArgs.SharedAllocations.Length > 0)
            {
                // Find the largest shared allocation
                long maxSharedElements = _generatorArgs.SharedAllocations.Allocas.Max(a => a.ArraySize);
                // Cap at the device's actual MaxNumThreadsPerGroup to avoid emitting a
                // @workgroup_size larger than what the device supports.  Without this cap
                // the heuristic could pick 512 or 1024 from shared-memory sizes while the
                // device (and the algorithm's dispatch) only supports 256, causing
                // Group.DimX to return the wrong value and corrupting all index math
                // (TileInfo, grid-stride loops, etc.).
                int maxAllowedWorkgroupSize = _generatorArgs.Backend.DefaultMaxWorkgroupSize ?? 1024;
                if (WebGPU.Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WorkgroupSize] ExplicitlyGrouped with shared memory. Max shared elements={maxSharedElements}, deviceMax={maxAllowedWorkgroupSize}");

                // Common ILGPU patterns: shared memory = groupSize * N where N is a small factor (1, 2, 4, ..., 64)
                // Try to find the largest power-of-2 factor that gives a reasonable groupSize (32..deviceMax)
                for (int factor = 1; factor <= 64; factor *= 2)
                {
                    long candidateGroupSize = maxSharedElements / factor;
                    if (candidateGroupSize >= 32 && candidateGroupSize <= maxAllowedWorkgroupSize && (candidateGroupSize & (candidateGroupSize - 1)) == 0)
                    {
                        if (WebGPU.Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WorkgroupSize] Inferred groupSize={candidateGroupSize} from shared[{maxSharedElements}]/factor={factor}, deviceMax={maxAllowedWorkgroupSize}");
                        return EntryPoint.IndexType switch
                        {
                            IndexType.Index1D => $"{candidateGroupSize}",
                            _ => $"{candidateGroupSize}"
                        };
                    }
                }
            }

            if (WebGPU.Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WorkgroupSize] Using default fallback (64)");
            // Default fallback for auto-grouped kernels where no specialization is provided
            return EntryPoint.IndexType switch
            {
                IndexType.Index1D => "64",
                IndexType.Index2D => "8, 8",
                IndexType.Index3D => "4, 4, 4",
                _ => "64"
            };
        }

        /// <summary>
        /// Enumerates every helper method that contributes IR to the final WGSL
        /// module: inline helpers (HelperMethods) and standalone-fn helpers
        /// (NonInlineMethods). Used by all scan passes that need to see the
        /// full call-graph IR. (rc.16 Bug D follow-up, 2026-05-05.)
        /// </summary>
        private IEnumerable<Method> EnumerateAllHelperMethods()
        {
            foreach (var (helperMethod, _) in _generatorArgs.HelperMethods)
                yield return helperMethod;
            foreach (var nonInlineMethod in _generatorArgs.NonInlineMethods)
                yield return nonInlineMethod;
        }

        /// <summary>
        /// Sets TypeGenerator.KernelUsesI64 and KernelUsesF64 by scanning the kernel's
        /// IR AND all helper methods' IR for Int64/Float64 types.
        /// Must be called at the start of GenerateCode() (before body generation) so that
        /// the TypeGenerator maps 64-bit types consistently during both body and header.
        /// </summary>
        private void SetEmulationFlags()
        {
            bool containsI64 = ContainsBasicValueType(BasicValueType.Int64);
            bool containsF64 = ContainsBasicValueType(BasicValueType.Float64);

            // Also scan helper methods' IR — inlined helpers (e.g., ExclusiveScan) may
            // introduce Int64/Float64 operations (like Index3D.Size overflow checks) that
            // are invisible to the kernel's own IR scan. Non-inlined helpers (standalone
            // fn defs) emit their bodies into the same WGSL module, so any 64-bit op in
            // those bodies also requires the emulation library — without this, the kernel
            // emits calls to i64_eq / i64_ge / f64_add / etc. but the corresponding fn
            // definitions are missing and the WGSL validator fails. (Tuvok's 2026-05-05
            // fn-definition path bug; rc.16 Bug D follow-up.)
            //
            // The helper scan must mirror ContainsBasicValueType's full coverage:
            //   (a) helper Method.Parameters — `long` / `double` / `ArrayView<long>`-style
            //       params are Int64/Float64 in IR but appear ONLY in Method.Parameters,
            //       not in any block's value list.
            //   (b) PrimitiveType result types in the helper's blocks (direct hits).
            //   (c) Pointer<Int64> / View<Int64> / Struct{Int64 ...} types via
            //       TypeContainsBasicValue, so an Alloca/LEA/SubViewValue whose Type wraps
            //       Int64 also flips the flag even when the inlined Load was eliminated.
            if (!containsI64 || !containsF64)
            {
                var bothFound = false;
                foreach (var helperMethod in EnumerateAllHelperMethods())
                {
                    // (a) parameter types
                    foreach (var param in helperMethod.Parameters)
                    {
                        if (!containsI64 && TypeContainsBasicValue(param.ParameterType, BasicValueType.Int64, new HashSet<TypeNode>()))
                            containsI64 = true;
                        if (!containsF64 && TypeContainsBasicValue(param.ParameterType, BasicValueType.Float64, new HashSet<TypeNode>()))
                            containsF64 = true;
                        if (containsI64 && containsF64) { bothFound = true; break; }
                    }
                    if (bothFound) break;
                    // (b) + (c) value types in blocks
                    foreach (var block in helperMethod.Blocks)
                    {
                        foreach (var valueEntry in block)
                        {
                            var vt = valueEntry.Value.Type;
                            if (!containsI64 && TypeContainsBasicValue(vt, BasicValueType.Int64, new HashSet<TypeNode>()))
                                containsI64 = true;
                            if (!containsF64 && TypeContainsBasicValue(vt, BasicValueType.Float64, new HashSet<TypeNode>()))
                                containsF64 = true;
                            if (containsI64 && containsF64) { bothFound = true; break; }
                        }
                        if (bothFound) break;
                    }
                    if (bothFound) break;
                }
            }

            TypeGenerator.KernelUsesI64 = Backend.EnableI64Emulation && containsI64;
            TypeGenerator.KernelUsesF64 = Backend.EnableF64Emulation && containsF64;
        }

        /// <summary>
        /// Checks whether ANY value in the kernel IR (parameters AND internal values)
        /// contains the specified BasicValueType.
        /// This scans the full IR because intermediate values (e.g. Grid.GlobalIndex.X → Int64,
        /// loop counters, comparisons) generate 64-bit IR operations that require the
        /// emulation library even when no kernel parameter uses 64-bit types.
        /// </summary>
        private bool ContainsBasicValueType(BasicValueType targetType)
        {
            // First check parameters
            int paramOffset = KernelParamOffset;
            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;
                if (TypeContainsBasicValue(param.ParameterType, targetType, new HashSet<TypeNode>()))
                    return true;
            }
            // Then check ALL values in all blocks (catches intermediate Int64/Float64 from
            // Grid.GlobalIndex.X, loop counters, comparison results, etc.)
            foreach (var block in Method.Blocks)
            {
                foreach (var value in block)
                {
                    if (value.Value.Type is PrimitiveType pt && pt.BasicValueType == targetType)
                        return true;
                }
            }
            return false;
        }

        private bool TypeContainsBasicValue(TypeNode type, BasicValueType targetType, HashSet<TypeNode> visited)
        {
            if (type == null || !visited.Add(type)) return false;
            if (type is PrimitiveType pt)
                return pt.BasicValueType == targetType;
            if (type is ViewType vt)
                return TypeContainsBasicValue(vt.ElementType, targetType, visited);
            if (type is global::ILGPU.IR.Types.PointerType ptr)
                return TypeContainsBasicValue(ptr.ElementType, targetType, visited);
            if (type is StructureType st)
            {
                foreach (var field in st.Fields)
                {
                    if (TypeContainsBasicValue(field, targetType, visited))
                        return true;
                }
            }
            return false;
        }

        private TypeNode GetBufferElementType(TypeNode type)
        {
            var current = type;
            while (current != null)
            {
                if (current is ViewType viewType) return viewType.ElementType;
                if (current is global::ILGPU.IR.Types.PointerType ptrType) return ptrType.ElementType;
                if (current is global::ILGPU.IR.Types.StructureType structType)
                {
                    // Refined Logic:
                    // 1. If this is a Wrapper Struct (like ArrayView2D/3D), its first field is the underlying View.
                    //    We MUST drill down to get the primitive element type (e.g., float).
                    // 2. If this is a Data Struct (user-defined struct), it is the element type itself.
                    //    We MUST return it as-is so we get 'array<MyStruct>'.

                    if (structType.NumFields > 0 && (structType.Fields[0] is ViewType || structType.Fields[0].ToString().Contains("View")))
                    {
                        current = structType.Fields[0];
                        continue;
                    }

                    return structType;
                }
                break;
            }
            return current; // Return whatever we resolved to
        }
        // Add this to your Properties region
        private HashSet<Value> _hoistedIndexFields = new HashSet<Value>();

        private void SetupIndexVariables()
        {
            // If the kernel is explicitly grouped (Shared Memory / Advanced), the user handles indices via Group.* intrinsics
            // However, we still need to map the main Kernel Index parameter if it exists.

            if (Method.Parameters.Count == 0) return;

            var indexParam = Method.Parameters[0];

            // Only map if strictly implicit OR if we detected an IndexType
            if (KernelParamOffset == 0) return;

            var indexVar = Allocate(indexParam);
            _hoistedIndexFields.Clear();

            // Auto-grouped range check: for implicitly grouped kernels, the dispatch may
            // launch more threads than data elements. Without a range check, WGSL's clamped
            // array indexing causes excess threads to overwrite the last valid element.
            //
            // However, we MUST skip the range check if the kernel uses subgroup operations
            // (subgroupShuffle, subgroupBroadcastFirst), group broadcast, or group reduce.
            // The early `return` introduces a non-uniform control flow path, and WGSL's
            // STATIC uniformity analysis will reject any subsequent subgroup/barrier calls
            // — even if the branch is never taken at runtime.
            // For such kernels, the user is responsible for ensuring the element count is a
            // multiple of the workgroup size, or the kernel must handle excess threads itself.
            bool kernelUsesUniformOps = _usesSubgroups || _usesBroadcast || _usesBarriers || _usesGroupReduce;
            bool needsRangeCheck = !EntryPoint.IsExplicitlyGrouped && !kernelUsesUniformOps;

            // 1D Kernel
            if (EntryPoint.IndexType == IndexType.Index1D)
            {
                // Map to global linear index, accounting for 2D dispatch fallback.
                // When the element count exceeds 65535×64, WebGPUAccelerator spills into
                // workY (workX=65535, workY=ceil(total/65535)). The full linear index is:
                //   (group_id.x + group_id.y * num_workgroups.x) * workgroup_size.x + local_index
                // When workY=1, group_id.y=0 → identical to the original 1D formula.
                AppendLine($"var {indexVar.Name} : i32 = i32(local_index + (group_id.x + group_id.y * num_workgroups.x) * workgroup_size.x);");
                if (needsRangeCheck)
                    AppendLine($"if (u32({indexVar.Name}) >= _ilgpu_user_dim) {{ return; }}");
            }
            // 2D Kernel
            else if (EntryPoint.IndexType == IndexType.Index2D)
            {
                // Map global_id.xy to vec2<i32>
                AppendLine($"var {indexVar.Name} : vec2<i32> = vec2<i32>(i32(global_id.x), i32(global_id.y));");

                // Handle struct field access (index.X, index.Y) by pre-calculating them
                // This prevents "GetField" later from trying to access a struct field on a vec2
                foreach (var use in indexParam.Uses)
                {
                    if (use.Target is global::ILGPU.IR.Values.GetField gf)
                    {
                        var componentVar = Allocate(gf);
                        _hoistedIndexFields.Add(gf);
                        declaredVariables.Add(componentVar.Name); // Ensure it's marked as declared

                        string comp = gf.FieldSpan.Index == 0 ? "x" : "y";
                        AppendLine($"var {componentVar.Name} : i32 = {indexVar.Name}.{comp};"); // Use vector component
                    }
                }
            }
            // 3D Kernel
            else if (EntryPoint.IndexType == IndexType.Index3D)
            {
                // Map global_id.xyz to vec3<i32>
                AppendLine($"var {indexVar.Name} : vec3<i32> = vec3<i32>(i32(global_id.x), i32(global_id.y), i32(global_id.z));");

                foreach (var use in indexParam.Uses)
                {
                    if (use.Target is global::ILGPU.IR.Values.GetField gf)
                    {
                        var componentVar = Allocate(gf);
                        _hoistedIndexFields.Add(gf);
                        declaredVariables.Add(componentVar.Name);

                        string comp = gf.FieldSpan.Index == 0 ? "x" : (gf.FieldSpan.Index == 1 ? "y" : "z");
                        AppendLine($"var {componentVar.Name} : i32 = {indexVar.Name}.{comp};");
                    }
                }
            }
            // LongIndex1D Kernel (grid-stride kernels, e.g. from LoadGridStrideKernel/accelerator.Reduce)
            // LongIndex1D is an emulated i64 (vec2<u32>) representing the 64-bit thread index.
            else if (EntryPoint.IndexType == IndexType.LongIndex1D)
            {
                // Map to emulated i64: pack the 32-bit global linear index into the low word.
                // Accounts for 2D dispatch fallback (same reasoning as Index1D above).
                AppendLine($"var {indexVar.Name} : vec2<u32> = vec2<u32>(u32(local_index) + (u32(group_id.x) + u32(group_id.y) * u32(num_workgroups.x)) * u32(workgroup_size.x), 0u); // LongIndex1D (emu_i64)");
                if (needsRangeCheck)
                    AppendLine($"if ({indexVar.Name}.x >= _ilgpu_user_dim) {{ return; }}");
            }
            // LongIndex2D Kernel
            else if (EntryPoint.IndexType == IndexType.LongIndex2D)
            {
                // Map to pair of emulated i64 (vec2<u32> per dimension)
                // This is rare - map to a struct with two emu_i64 fields for X and Y
                AppendLine($"var {indexVar.Name}_x : vec2<u32> = vec2<u32>(u32(global_id.x), 0u); // LongIndex2D X");
                AppendLine($"var {indexVar.Name}_y : vec2<u32> = vec2<u32>(u32(global_id.y), 0u); // LongIndex2D Y");
                // Note: LongIndex2D field accesses may still need handling in GetField
            }
            // LongIndex3D Kernel
            else if (EntryPoint.IndexType == IndexType.LongIndex3D)
            {
                AppendLine($"var {indexVar.Name}_x : vec2<u32> = vec2<u32>(u32(global_id.x), 0u); // LongIndex3D X");
                AppendLine($"var {indexVar.Name}_y : vec2<u32> = vec2<u32>(u32(global_id.y), 0u); // LongIndex3D Y");
                AppendLine($"var {indexVar.Name}_z : vec2<u32> = vec2<u32>(u32(global_id.z), 0u); // LongIndex3D Z");
            }
            else
            {
                // Fallback: Default to 1D if we skipped the param but didn't match specific types
                // This protects against weird IndexType states
                AppendLine($"var {indexVar.Name} : i32 = i32(global_id.x); // Fallback mapping (IndexType={EntryPoint.IndexType})");
            }
        }


        // ─────────────────────────────────────────────────────────────
        //  Grid.IdxX / Grid.DimX linearization for 2D dispatch fallback
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the kernel is logically 1D (auto-grouped Index1D, LongIndex1D,
        /// or explicitly grouped). For these kernels, when the workgroup count exceeds 65535
        /// and the accelerator falls back to 2D dispatch, Grid.IdxX and Grid.DimX must
        /// return linearized values so algorithms see a flat 1D workgroup namespace.
        /// True 2D/3D kernels (Index2D, Index3D, etc.) use actual multi-dimensional grids
        /// and should NOT be linearized.
        /// </summary>
        private bool? _shouldLinearizeGridX;

        /// <summary>
        /// Determines whether Grid.IdxX should use linearized 2D→1D mapping.
        /// Returns false for explicitly 2D/3D index types AND for LoadStreamKernel
        /// kernels that access Grid.IdxY or Grid.IdxZ (indicating true multi-dimensional
        /// grid semantics, not a 1D fallback split).
        /// </summary>
        private bool ShouldLinearizeGridX()
        {
            if (_shouldLinearizeGridX.HasValue)
                return _shouldLinearizeGridX.Value;

            _shouldLinearizeGridX = EntryPoint.IndexType switch
            {
                IndexType.Index2D => false,
                IndexType.Index3D => false,
                _ => !KernelUsesMultiDimensionalGrid(), // Check IR for Grid.IdxY/IdxZ usage
            };
            return _shouldLinearizeGridX.Value;
        }

        /// <summary>
        /// Scans the kernel IR to check if Grid.IdxY or Grid.IdxZ is accessed.
        /// If so, the kernel uses true multi-dimensional grid semantics and
        /// Grid.IdxX should NOT be linearized.
        /// </summary>
        private bool KernelUsesMultiDimensionalGrid()
        {
            foreach (var block in Method.Blocks)
            {
                foreach (var entry in block)
                {
                    if (entry.Value is GridIndexValue gridIdx &&
                        gridIdx.Dimension != DeviceConstantDimension3D.X)
                        return true;
                }
            }
            return false;
        }

        private bool? _kernelUsesGridIdxZ;
        /// <summary>
        /// Scans the kernel IR for Grid.IdxZ access. A 2D-grid kernel (uses Grid.IdxY but NOT
        /// Grid.IdxZ) leaves the Z dispatch dimension FREE, so the accelerator can fold an
        /// X-workgroup-count overflow (> maxComputeWorkgroupsPerDimension) into Z. When that
        /// happens, Grid.IdxX must reconstruct as group_id.x + group_id.z*num_workgroups.x and
        /// Grid.DimX as num_workgroups.x*num_workgroups.z. A true 3D-grid kernel (uses Grid.IdxZ)
        /// has no free Z dim, so the fold isn't applied and Grid.IdxX stays group_id.x.
        /// </summary>
        private bool KernelUsesGridIdxZ()
        {
            if (_kernelUsesGridIdxZ.HasValue)
                return _kernelUsesGridIdxZ.Value;
            bool found = EntryPoint.IndexType == IndexType.Index3D;
            if (!found)
                foreach (var block in Method.Blocks)
                    foreach (var entry in block)
                        if (entry.Value is GridIndexValue g && g.Dimension == DeviceConstantDimension3D.Z)
                        { found = true; break; }
            _kernelUsesGridIdxZ = found;
            return found;
        }

        /// <summary>
        /// Override: Grid.IdxX returns the linearized workgroup index for 1D kernels.
        /// When 2D dispatch fallback is used (workY > 1), group_id.x alone is wrong;
        /// the full linear index is group_id.x + group_id.y * num_workgroups.x.
        /// For 1D dispatch (workY=1), group_id.y=0 so this is a no-op.
        /// </summary>
        public override void GenerateCode(GridIndexValue value)
        {
            var target = Load(value);
            Declare(target);

            if (value.Dimension == DeviceConstantDimension3D.X && ShouldLinearizeGridX())
            {
                // 1D kernel: X overflow folds into Y → reconstruct the flat group index.
                AppendLine($"{target} = i32(group_id.x + group_id.y * num_workgroups.x);");
            }
            else if (value.Dimension == DeviceConstantDimension3D.X && !KernelUsesGridIdxZ())
            {
                // 2D kernel (uses Grid.IdxY, Z free): X overflow folds into Z → reconstruct via z.
                // Backward-compatible: when not folded, num_workgroups.z=1 and group_id.z=0 → group_id.x.
                AppendLine($"{target} = i32(group_id.x + group_id.z * num_workgroups.x);");
            }
            else
            {
                string dim = value.Dimension switch
                {
                    DeviceConstantDimension3D.X => "x",
                    DeviceConstantDimension3D.Y => "y",
                    DeviceConstantDimension3D.Z => "z",
                    _ => "x"
                };
                AppendLine($"{target} = i32(group_id.{dim});");
            }
        }

        /// <summary>
        /// Override: Grid.DimX returns the total workgroup count for 1D kernels.
        /// When 2D dispatch fallback splits workgroups into (workX, workY),
        /// the total is num_workgroups.x * num_workgroups.y (not just num_workgroups.x).
        /// For 1D dispatch (workY=1), this is num_workgroups.x * 1 = same result.
        /// </summary>
        public override void GenerateCode(GridDimensionValue value)
        {
            var target = Load(value);
            Declare(target);

            if (value.Dimension == DeviceConstantDimension3D.X && ShouldLinearizeGridX())
            {
                // 1D kernel: X folded into Y → logical Grid.DimX = num_workgroups.x * num_workgroups.y.
                AppendLine($"{target} = i32(num_workgroups.x * num_workgroups.y);");
            }
            else if (value.Dimension == DeviceConstantDimension3D.X && !KernelUsesGridIdxZ())
            {
                // 2D kernel: X folded into Z (exact fold X*Z=logicalX) → Grid.DimX = num_workgroups.x*num_workgroups.z.
                // Backward-compatible: num_workgroups.z=1 when not folded.
                AppendLine($"{target} = i32(num_workgroups.x * num_workgroups.z);");
            }
            else
            {
                string dim = value.Dimension switch
                {
                    DeviceConstantDimension3D.X => "x",
                    DeviceConstantDimension3D.Y => "y",
                    DeviceConstantDimension3D.Z => "z",
                    _ => "x"
                };
                AppendLine($"{target} = i32(num_workgroups.{dim});");
            }
        }

        /// <summary>
        /// Group.DimX override that clamps X-dim to min(workgroup_size.x, _ilgpu_user_dim)
        /// for auto-grouped kernels with an extent override constant. Closes
        /// `Tests23_GroupDimX_Clamps_To_Extent_OnUnitDispatch` — the IL emits a clamp
        /// pre-fix on CUDA / OpenCL, and WebGPU should honor the same semantic. The
        /// WGSL @workgroup_size attribute is fixed at compile time (= customGroupSize),
        /// so we can't change the actual workgroup size per-dispatch; we instead expose
        /// the clamped value to user code via Group.DimX. The OOB range check at the
        /// top of main() already prevents excess threads from running, so this just
        /// makes the user-visible Group.DimX consistent with the IL clamp.
        ///
        /// Y / Z dims and explicitly-grouped kernels fall through to the base behavior.
        /// </summary>
        public override void GenerateCode(GroupDimensionValue value)
        {
            // Only override the X dim, and only for auto-grouped kernels where
            // `_ilgpu_user_dim` is declared (matches the condition in GenerateHeader).
            if (value.Dimension == DeviceConstantDimension3D.X
                && !EntryPoint.IsExplicitlyGrouped
                && KernelParamOffset > 0)
            {
                var target = Load(value);
                Declare(target);
                AppendLine($"{target} = i32(min(workgroup_size.x, _ilgpu_user_dim));");
                return;
            }
            base.GenerateCode(value);
        }

        /// <summary>
        /// Pre-scans parameters to identify which buffers use emulated 64-bit types.
        /// This MUST run before HoistCrossBlockVariables and body generation so that
        /// LoadElementAddress and Load/Store can check if a param needs emulation.
        /// </summary>
        private void PreScanEmulatedParameters()
        {
            int paramOffset = KernelParamOffset;
            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;

                var elementType = GetBufferElementType(param.ParameterType);
                var wgslType = TypeGenerator[elementType];

                if (Backend.EnableF64Emulation && wgslType == "emu_f64")
                {
                    _emulatedF64Params.Add(param.Index);
                }
                else if (Backend.EnableI64Emulation && (wgslType == "emu_i64" || wgslType == "emu_u64"))
                {
                    _emulatedI64Params.Add(param.Index);
                }

                // Pre-scan sub-word element types so SetupParameterBindings and
                // GenerateHeader can both detect them before body generation runs.
                IsMultiDim(param.ParameterType, out _, out var isView, out _, out _, out _);
                if (isView && elementType is PrimitiveType preScanPt)
                {
                    switch (preScanPt.BasicValueType)
                    {
                        case BasicValueType.Int8:
                            _subWordParams[param.Index] = 1;
                            // Detect unsigned (byte) via CLR param type
                            {
                                int userIdx8 = param.Index - KernelParamOffset;
                                if (userIdx8 >= 0 && userIdx8 < EntryPoint.Parameters.Count)
                                {
                                    var clrType8 = EntryPoint.Parameters[userIdx8];
                                    if (clrType8.IsGenericType)
                                    {
                                        var genArgs8 = clrType8.GetGenericArguments();
                                        if (genArgs8.Length > 0 && genArgs8[0] == typeof(byte))
                                            _subWordUnsignedParams.Add(param.Index);
                                    }
                                }
                            }
                            break;
                        case BasicValueType.Int16:
                            _subWordParams[param.Index] = 2;
                            // Detect unsigned (ushort) via CLR param type from EntryPoint.Parameters.
                            // EntryPoint.Parameters[N] is the CLR Type of user param N (0-based).
                            // For stream kernels with KernelParamOffset=1, IR param.Index=1 maps to
                            // user param 0 (the first user param after the auto-generated Index1D).
                            {
                                int userIdx = param.Index - KernelParamOffset;
                                if (userIdx >= 0 && userIdx < EntryPoint.Parameters.Count)
                                {
                                    var clrParamType = EntryPoint.Parameters[userIdx];
                                    // Check if this is ArrayView<ushort> or similar generic with ushort element
                                    if (clrParamType.IsGenericType)
                                    {
                                        var genArgs = clrParamType.GetGenericArguments();
                                        if (genArgs.Length > 0 && genArgs[0] == typeof(ushort))
                                            _subWordUnsignedParams.Add(param.Index);
                                    }
                                }
                            }
                            break;
                        case BasicValueType.Float16:
                            if (!Backend.HasShaderF16)
                            {
                                _subWordParams[param.Index] = 2;
                                _subWordFloat16Params.Add(param.Index);
                            }
                            break;
                        case BasicValueType.BFloat16:
                            _subWordParams[param.Index] = 2;
                            _subWordBFloat16Params.Add(param.Index);
                            break;
                    }
                }
            }
        }

        private void HoistCrossBlockVariables()
        {
            var defBlocks = new Dictionary<Value, BasicBlock>();
            _hoistedPrimitives.Clear(); // Initialize the restored set
            _crossBlockPointers.Clear();
            _crossBlockPointerExprs.Clear();

            foreach (var block in Method.Blocks)
                foreach (var value in block)
                    defBlocks[value.Value] = block;

            foreach (var block in Method.Blocks)
            {
                foreach (var value in block)
                {
                    // CRITICAL: Skip NewView to prevent hoisting pointer logic
                    if (value.Value is global::ILGPU.IR.Values.NewView) continue;

                    if (value.Value is PhiValue)
                    {
                        _hoistedPrimitives.Add(value.Value);
                    }

                    // FIX: Hoist UnaryArithmeticValue and BinaryArithmeticValue results
                    // These are math intrinsics (Abs, Min, Max, etc.) that may be reused
                    // across branches in structured control flow, causing scope issues
                    if (value.Value is global::ILGPU.IR.Values.UnaryArithmeticValue ||
                        value.Value is global::ILGPU.IR.Values.BinaryArithmeticValue)
                    {
                        if (!value.Value.Type.IsPointerType && !value.Value.Type.IsVoidType)
                        {
                            _hoistedPrimitives.Add(value.Value);
                        }
                    }

                    foreach (var use in value.Value.Uses)
                    {
                        if (defBlocks.TryGetValue(use.Target, out var defBlock) && defBlock != block)
                        {
                            // CRITICAL FIX: Skip hoisting values whose IR type is ViewType.
                            // ViewType values (ArrayView references extracted via GetField from
                            // parameter structs) will be aliased to buffer bindings by
                            // GenerateCode(LoadElementAddress) which emits "let v_X = &paramY[offset];".
                            // Hoisting them as "var v_X : <element_type> = 0;" causes a redeclaration
                            // conflict because TypeGenerator maps ViewType to its element type.
                            bool isViewType = value.Value.Type is global::ILGPU.IR.Types.ViewType;

                            if (!value.Value.Type.IsPointerType && !value.Value.Type.IsVoidType && !isViewType)
                            {
                                _hoistedPrimitives.Add(value.Value);
                            }
                            // Track cross-block LoadElementAddress pointers for inline substitution
                            else if (value.Value.Type.IsPointerType && value.Value is LoadElementAddress)
                            {
                                _crossBlockPointers.Add(value.Value);
                            }
                        }
                    }
                }
            }

            foreach (var val in _hoistedPrimitives)
            {
                var variable = Allocate(val);
                var wgslType = TypeGenerator[val.Type];
                var basicType = val.Type.BasicValueType;


                string init = " = 0";
                if (basicType == BasicValueType.Int1) init = " = false";
                else if (basicType == BasicValueType.Float16 || basicType == BasicValueType.Float32) init = " = 0.0";
                else if (basicType == BasicValueType.Float64)
                {
                    // emu_f64 is emulated as vec2<f32> when emulation is enabled
                    if (wgslType == "emu_f64")
                    {
                        if (Backend.UseOzakiF64Emulation)
                            init = " = emu_f64(0.0, 0.0, 0.0, 0.0)";
                        else
                            init = " = emu_f64(0.0, 0.0)";
                    }
                    else
                        init = " = 0.0";
                }
                else if (basicType == BasicValueType.Int64)
                {
                    // emu_i64 is emulated as vec2<u32> when emulation is enabled
                    if (wgslType == "emu_i64" || wgslType == "emu_u64")
                        init = " = emu_i64(0u, 0u)";
                    else
                        init = " = 0";
                }

                // WGSL: pointer types are not constructible — skip 'var' declaration for them.
                // The IR-level IsPointerType filter earlier may not catch all cases because some
                // IR types (StructureType containing pointers) are mapped to ptr<> by TypeGenerator.
                if (wgslType.StartsWith("ptr<"))
                {
                    if (WebGPU.Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[DIAG-HOIST-SKIP] Skipping ptr type hoist for {variable.Name} : {wgslType}, IR type={val.Type}, kind={val.GetType().Name}");
                    continue;
                }

                if (WebGPU.Backend.WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[DIAG-HOIST-EMIT] Emitting hoisted var {variable.Name} : {wgslType}, IR type={val.Type}, kind={val.GetType().Name}");
                AppendLine($"var {variable.Name} : {wgslType}{init};");
                declaredVariables.Add(variable.Name);
            }
        }

        void IsMultiDim(TypeNode ParameterType, out bool isMultiDim, out bool isView, out bool is1DView, out bool is2DView, out bool is3DView)
        {
            is1DView = false;
            is2DView = false;
            is3DView = false;
            isMultiDim = false;
            isView = false;

            // 0. Direct ViewType Check — for stream kernels where the IR type is View<T> directly
            if (ParameterType is ViewType)
            {
                isView = true;
                is1DView = true;
                return;
            }

            string typeName = ParameterType.ToString();

            // 1. Direct String Check (works for C# types if available, but unreliable for IR strings)
            bool isStringView = typeName.Contains("ArrayView");

            // 2. Structural Check on ParameterType (The Wrapper Struct)
            if (ParameterType is global::ILGPU.IR.Types.StructureType st)
            {
                // Check if it looks like an ArrayView wrapper (Field 0 is View)
                if (st.NumFields > 0 && (st.Fields[0] is ViewType || st.Fields[0].ToString().Contains("View")))
                {
                    // Discriminate "single ArrayView wrapper" from "body struct with multiple
                    // ArrayView fields". A 1D ArrayView wrapper has 3 fields with exactly one
                    // ViewType (field 0); 2D has 4 fields with one ViewType; 3D has 6 fields
                    // with one ViewType. A body struct with 2 ArrayView1D fields ALSO has 6
                    // fields (2 × {BaseView, Extent, Stride}) but with TWO ViewType fields.
                    // Count actual ViewType fields to disambiguate. Surfaced 2026-05-04 by
                    // Tests23_TwoShortBodyStructDense — was being treated as a 3D ArrayView
                    // (false isView=true, false is3DView=true) because NumFields==6.
                    int viewFieldCount = 0;
                    for (int fi = 0; fi < st.NumFields; fi++)
                    {
                        if (st.Fields[fi] is ViewType || st.Fields[fi].ToString().Contains("View"))
                            viewFieldCount++;
                    }
                    if (viewFieldCount > 1)
                    {
                        // Multi-view body struct — leave isView=false; the body-struct
                        // codegen path will handle each field individually.
                        return;
                    }

                    isView = true;

                    if (st.NumFields == 6 || st.NumFields == 4) // 3D (6 fields usually), 2D (4 fields)
                    {
                        isMultiDim = true;
                        if (st.NumFields == 6) is3DView = true;
                        else is2DView = true;
                    }
                    else if (st.NumFields == 3)
                    {
                        is1DView = true;
                        isMultiDim = false; // 1D doesn't need stride buffers, so we treat as "not multi dim" for stride purposes
                    }
                }
            }

            // 3. Fallback: Combine String check with Structural properties
            if (!isView && isStringView)
            {
                isView = true;
                if (typeName.Contains("2D") || typeName.Contains("3D")) isMultiDim = true;
                if (typeName.Contains("3D")) is3DView = true;
                else if (typeName.Contains("2D")) is2DView = true;
                else if (typeName.Contains("1D")) is1DView = true;
            }
        }

        private void SetupParameterBindings()
        {
            // NOTE: GenerateHeader() runs AFTER body generation in ILGPU's pipeline,
            // so _scalarManifest is not yet populated. We must replicate the same
            // classification logic here to determine which params are packed scalars.
            // Both this method and GenerateHeader iterate Method.Parameters in the same
            // order, so the slot offsets will match.

            int paramOffset = KernelParamOffset;

            // First pass: classify all params and compute slot offsets for packed scalars
            var packedScalarSlots = new Dictionary<int, (int slot, int slotCount, string wgslType, bool isEmuF64, bool isEmuI64)>();
            int scalarSlotOffset = 0;

            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;

                // Body struct params: their user-scalar fields CONSUME packed-scalar slots
                // (ScanBodyStructParams assigned fieldInfo.ScalarSlot for each). If we just
                // `continue` here we leave scalarSlotOffset stale and any TRAILING packed
                // scalar gets the WRONG slot index in its `_scalar_params[N]` emit — the
                // emitted index falls in the body struct's slot range, reading D0/D1/etc
                // values instead of the actual scalar input. Repro:
                // TensorViewStructParam_HelperPattern_1DOutput_Indexes_Correctly (two
                // TensorView-shaped body structs followed by float/float/int trailing
                // scalars - scaleA/scaleB/mode all read body-struct dim slots and
                // produced garbage on WebGPU + WebGL).
                //
                // Advance scalarSlotOffset by the body struct's user-scalar consumption
                // (skipping view + view-metadata fields) so the trailing-scalar slot math
                // matches GenerateHeader.
                if (_bodyStructParams.TryGetValue(param.Index, out var bsFields))
                {
                    foreach (var bf in bsFields)
                    {
                        if (bf.IsView || bf.IsViewMetadata) continue;
                        var bsScalarElemType = GetBufferElementType(bf.FieldType);
                        var bsWgslType = TypeGenerator[bsScalarElemType];
                        bool bsIsEmuF64 = Backend.EnableF64Emulation && bsWgslType == "emu_f64";
                        bool bsIsEmuI64 = Backend.EnableI64Emulation && (bsWgslType == "emu_i64" || bsWgslType == "emu_u64");
                        scalarSlotOffset += (bsIsEmuF64 || bsIsEmuI64) ? 2 : 1;
                    }
                    continue;
                }

                IsMultiDim(param.ParameterType, out var isMultiDim, out var isView, out var is1DView, out var is2DView, out var is3DView);
                bool isStruct = param.ParameterType is global::ILGPU.IR.Types.StructureType && !isView;
                bool isAtomic = _atomicParameters.Contains(param.Index);

                // Does this param get its own binding? (same logic as GenerateHeader)
                bool ownBinding = isView || isStruct || isAtomic;

                if (!ownBinding)
                {
                    // This is a packed scalar
                    var elementType = GetBufferElementType(param.ParameterType);
                    var wgslType = TypeGenerator[elementType];

                    bool isEmuF64 = Backend.EnableF64Emulation && wgslType == "emu_f64";
                    bool isEmuI64 = Backend.EnableI64Emulation && (wgslType == "emu_i64" || wgslType == "emu_u64");

                    int slotCount = (isEmuF64 || isEmuI64) ? 2 : 1;
                    packedScalarSlots[param.Index] = (scalarSlotOffset, slotCount, wgslType, isEmuF64, isEmuI64);
                    scalarSlotOffset += slotCount;
                }
            }

            // Second pass: emit variable declarations
            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;
                var variable = Allocate(param);

                IsMultiDim(param.ParameterType, out var isMultiDim, out var isView, out var is1DView, out var is2DView, out var is3DView);

                // --- BODY STRUCT: create individual field variables ---
                if (_bodyStructParams.TryGetValue(param.Index, out var fieldInfos))
                {
                    // The param variable itself is allocated but not used directly.
                    // Instead, create individual variables for each field.
                    for (int fi = 0; fi < fieldInfos.Count; fi++)
                    {
                        var fieldInfo = fieldInfos[fi];
                        string fieldVarName = $"{variable.Name}_f{fi}";
                        _bodyStructFieldVars[(param.Index, fi)] = fieldVarName;
                        declaredVariables.Add(fieldVarName);

                        if (fieldInfo.IsView)
                        {
                            // View field: create a pointer to the view buffer (a 'let' pointer alias)
                            // Track the var name as a pointer alias so the post-processor won't
                            // convert 'let v_28 = v_27_f0;' to 'v_28 = v_27_f0;' (pointer→i32 error).
                            _viewPointerVarNames.Add(fieldVarName);
                            AppendLine($"let {fieldVarName} = &{fieldInfo.BindingName};");
                        }
                        else if (fieldInfo.IsViewMetadata)
                        {
                            // View metadata field: use arrayLength() for length, 0 for flags
                            if (fieldInfo.IsLengthField)
                            {
                                // Length field: use arrayLength(&bindingName) cast to i32
                                // The IR type is Int64, but we use i32 since WGSL doesn't have i64 natively
                                var scalarElemType = GetBufferElementType(fieldInfo.FieldType);
                                var scalarWgslType = TypeGenerator[scalarElemType];
                                bool isEmuI64 = Backend.EnableI64Emulation && (scalarWgslType == "emu_i64" || scalarWgslType == "emu_u64");

                                // For packed-struct view fields, the CPU sends the true element count
                                // in a dedicated scalar slot (ViewCountSlot). Use that instead of
                                // arrayLength(), because arrayLength() returns CPU-allocation-bytes/4 u32s
                                // which does NOT equal element_count when CPU_element_size != GPU_packed_size.
                                // E.g. RadixSortPair<double,int>: CPU=16 bytes=4 u32s, GPU-packed=3 u32s.
                                // So arrayLength()/3 = (count*4)/3 ≠ count. Use _scalar_params[slot] instead.
                                string lengthExpr;
                                if (fieldInfo.ViewCountSlot >= 0)
                                {
                                    lengthExpr = $"i32(_scalar_params[{fieldInfo.ViewCountSlot}])";
                                }
                                else
                                {
                                    lengthExpr = $"i32(arrayLength(&{fieldInfo.AssociatedViewBindingName}))";
                                }

                                if (isEmuI64)
                                    AppendLine($"var {fieldVarName} = i64_from_i32({lengthExpr});");
                                else
                                    AppendLine($"var {fieldVarName} = {lengthExpr};");
                            }
                            else
                            {
                                // Flag field: use 0 (unused in WGSL)
                                AppendLine($"var {fieldVarName} = 0;");
                            }
                        }
                        else
                        {
                            // User scalar field: read from _scalar_params
                            _bodyReferencesScalarParams = true;
                            int slot = fieldInfo.ScalarSlot;
                            var scalarElemType = GetBufferElementType(fieldInfo.FieldType);
                            var scalarWgslType = TypeGenerator[scalarElemType];
                            bool isEmuF64 = Backend.EnableF64Emulation && scalarWgslType == "emu_f64";
                            bool isEmuI64 = Backend.EnableI64Emulation && (scalarWgslType == "emu_i64" || scalarWgslType == "emu_u64");

                            if (isEmuF64)
                                AppendLine($"var {fieldVarName} = f64_from_ieee754_bits(_scalar_params[{slot}], _scalar_params[{slot + 1}]);");
                            else if (isEmuI64)
                                // Use the correct type constructor (emu_i64 or emu_u64) so the WGSL type
                                // tracker assigns the right signedness for subsequent comparisons.
                                // Using emu_i64 for ulong would cause unsigned comparisons (u64_lt) to fall
                                // back to signed (i64_lt), breaking MinUInt64 (0xFFFF... as signed = -1).
                                AppendLine($"var {fieldVarName} = {scalarWgslType}(_scalar_params[{slot}], _scalar_params[{slot + 1}]);");
                            else if (scalarWgslType == "u32")
                                AppendLine($"var {fieldVarName} = _scalar_params[{slot}];");
                            else if (scalarWgslType == "i32")
                                AppendLine($"var {fieldVarName} = bitcast<i32>(_scalar_params[{slot}]);");
                            else if (scalarWgslType == "f32")
                                AppendLine($"var {fieldVarName} = bitcast<f32>(_scalar_params[{slot}]);");
                            else
                                AppendLine($"var {fieldVarName} = {BitcastFromU32($"_scalar_params[{slot}]", scalarWgslType)};");
                        }
                    }
                    continue; // Skip normal variable declaration for this param
                }


                // Check if this param is packed
                if (packedScalarSlots.TryGetValue(param.Index, out var packInfo))
                {
                    // --- PACKED SCALAR: read from _scalar_params buffer ---
                    _bodyReferencesScalarParams = true;
                    int slot = packInfo.slot;

                    if (packInfo.isEmuF64)
                    {
                        AppendLine($"var {variable.Name} = f64_from_ieee754_bits(_scalar_params[{slot}], _scalar_params[{slot + 1}]);");
                    }
                    else if (packInfo.isEmuI64)
                    {
                        AppendLine($"var {variable.Name} = {packInfo.wgslType}(_scalar_params[{slot}], _scalar_params[{slot + 1}]);");
                    }
                    else
                    {
                        // Standard scalar: bitcast from u32 to the target type
                        string wgslType = packInfo.wgslType;
                        if (wgslType == "u32")
                        {
                            AppendLine($"var {variable.Name} = _scalar_params[{slot}];");
                        }
                        else if (wgslType == "i32")
                        {
                            AppendLine($"var {variable.Name} = bitcast<i32>(_scalar_params[{slot}]);");
                        }
                        else if (wgslType == "f32")
                        {
                            AppendLine($"var {variable.Name} = bitcast<f32>(_scalar_params[{slot}]);");
                        }
                        else
                        {
                            AppendLine($"var {variable.Name} = {BitcastFromU32($"_scalar_params[{slot}]", wgslType)};");
                        }
                    }
                    continue;
                }

                // --- NON-PACKED: original binding logic ---
                if (!isView)
                {
                    bool isAtomic = _atomicParameters.Contains(param.Index);
                    bool isStruct = param.ParameterType is global::ILGPU.IR.Types.StructureType;

                    if (isAtomic)
                    {
                        AppendLine($"let {variable.Name} = &param{param.Index};");
                    }
                    else if (isStruct)
                    {
                        if (isMultiDim)
                        {
                            AppendLine($"let {variable.Name} = &param{param.Index};");
                        }
                        else
                        {
                            AppendLine($"let {variable.Name} = &param{param.Index}[0];");
                        }
                    }
                    else
                    {
                        // Non-packed scalar that has its own binding (shouldn't normally happen,
                        // but kept for safety/fallback)
                        if (_emulatedF64Params.Contains(param.Index))
                        {
                            AppendLine($"var {variable.Name} = f64_from_ieee754_bits(param{param.Index}[0], param{param.Index}[1]);");
                        }
                        else if (_emulatedI64Params.Contains(param.Index))
                        {
                            var emuType = TypeGenerator[GetBufferElementType(param.ParameterType)];
                            AppendLine($"var {variable.Name} = {emuType}(param{param.Index}[0], param{param.Index}[1]);");
                        }
                        else
                        {
                            AppendLine($"var {variable.Name} = param{param.Index}[0];");
                        }
                    }
                }
                else
                {
                    // Emit a `let` alias to the kernel binding so call sites of
                    // NoInlining helpers can pass the pointer as an argument
                    // (rc.16 fn-def-codegen Bug D, phase 4 — 2026-05-05).
                    //
                    // For full-word ArrayViews this is the existing behavior. For
                    // sub-word ArrayViews (`array<atomic<u32>>` storage) the
                    // earlier guard skipped emission because the inline-only sub-
                    // word LEA path emits `&param{N}[idx]` directly and didn't
                    // need the alias. With ptr-typed fn params landing in
                    // WGSLFunctionGenerator.GenerateHeaderStub, kernels need the
                    // alias variable available so it can be passed by name to
                    // helpers — emit it for sub-word too. The alias is just a
                    // pointer; if no helper consumes it, the var is harmless.
                    //
                    // Direct-param coalesce member: there is no `param{N}` binding
                    // for non-leaders (the leader emitted the shared binding under
                    // a different name). Alias the leader's binding so the variable
                    // is at least declared. The LEA path applies the per-member
                    // offset, so any LEA-based access is correct. Helper calls that
                    // pass the alias by value would lose the per-member offset; v1
                    // direct-param coalesce excludes such kernels (NoInlining helpers
                    // taking ArrayView are handled by separate fn-def codegen).
                    if (_directParamCoalesceMembership.TryGetValue(param.Index, out var dpAliasGroup))
                    {
                        AppendLine($"let {variable.Name} = &{dpAliasGroup.BindingName};");
                    }
                    else
                    {
                        AppendLine($"let {variable.Name} = &param{param.Index};");
                    }

                    if (isView && isMultiDim && (is2DView || is3DView))
                    {
                        AppendLine($"let {variable.Name}_stride = &param{param.Index}_stride;");
                    }
                }
            }
        }

        public override void GenerateCode(global::ILGPU.IR.Values.NewView value)
        {
            var target = Load(value);
            var source = Load(value.Pointer);

            // GUARD: Prevent duplicate `let` emissions when the same NewView is processed
            // multiple times (e.g., shared memory pointers in helpers inlined multiple times).
            // WGSL `let` bindings cannot be redeclared — emitting the same `let v_200 = &shared_1;`
            // twice causes a compilation error.
            // CRITICAL: Use _emittedLetBindings NOT declaredVariables — adding to declaredVariables
            // poisons Declare() and prevents var emissions for same-named variables.
            if (_emittedLetBindings.Contains(target.Name))
                return;

            // NewView result is strictly a pointer (reference) in WGSL
            string refPrefix = "";
            if (value.Pointer.Type is global::ILGPU.IR.Types.PointerType ptrType &&
                ptrType.AddressSpace == MemoryAddressSpace.Shared)
            {
                refPrefix = "&";
            }

            // Local-alloca source: when NewView wraps a function-scope alloca that
            // SetupAllocations declared as `var v_X : T`, we must take the address
            // (`&v_X`) instead of copying by value. Without this, `let v_alias = v_X`
            // produces a scalar copy and downstream `LoadElementAddress` emits
            // `&v_alias[idx]` which WGSL rejects with "cannot index type 'T'".
            // Mirrors the existing shared-memory branch above.
            if (string.IsNullOrEmpty(refPrefix)
                && _localAllocaVarNames.Contains(source.Name))
            {
                refPrefix = "&";
            }

            // We use 'let' to alias the pointer, ensuring we don't copy the array
            // Optimization: If source is already a pointer (likely), we might not need &
            // But for Shared Memory (var<workgroup> arr), it's treated as value, so we need &

            _emittedLetBindings.Add(target.Name);

            if (IsStateMachineActive)
            {
                // Hoist let declarations to function scope (VariableBuilder) so they
                // are accessible across all case blocks in the state machine.
                // Without this, the let has block scope inside a case and becomes
                // "unresolved value" in other case blocks that reference it.
                VariableBuilder.AppendLine($"    let {target.Name} = {refPrefix}{source};");
            }
            else
            {
                AppendLine($"let {target.Name} = {refPrefix}{source};");
            }
        }

        public override void GenerateCode(DynamicMemoryLengthValue value)
        {
            var target = Load(value);
            Declare(target);

            // Use the override constant for dynamic shared memory length
            // The override constant is declared as u32 but DynamicMemoryLengthValue is i32, so cast
            if (_dynamicSharedOverrides.Count > 0)
            {
                // For now, all dynamic shared allocations share the same size from SharedMemoryConfig
                // Use the first (and typically only) override constant
                var overrideName = _dynamicSharedOverrides[0].ConstantName;
                AppendLine($"{target} = i32({overrideName}); // dynamic memory length from override constant");
            }
            else
            {
                // Fallback: no dynamic shared memory overrides registered
                AppendLine($"{target} = 0; // dynamic memory length (no override available)");
            }
        }

        public override void GenerateCode(Parameter parameter) { }

        public override void GenerateCode(LoadElementAddress value)
        {
            var target = Load(value);
            var offset = Load(value.Offset);

            // When i64 emulation is active, Int64 indices become emu_i64 (vec2<u32>)
            // which cannot be used as WGSL array indices. Wrap with i64_to_i32().
            bool offsetIsEmulatedI64 = Backend.EnableI64Emulation
                && value.Offset.Resolve().BasicValueType == BasicValueType.Int64;
            string offsetExpr = offsetIsEmulatedI64 ? $"i64_to_i32({offset})" : $"{offset}";

            // SubView chain walk: if value.Source resolves through one or more
            // SubViewValue nodes (`view.SubView(offset, length)`), the SubView IR
            // node aliases its source view's pointer (see WGSLCodeGenerator
            // GenerateCode(SubViewValue)) and the actual offset is accumulated
            // here at the use site. Walk the chain, accumulate every SubView's
            // offset into offsetExpr, and emit `let target = &underlyingSource[
            // (svOffset1 + svOffset2 + ... + leaOffset)];`. This bypasses the
            // body-struct / sub-word / packed-struct branches below — those paths
            // are for direct kernel-parameter view access; SubView results in
            // codecs kernels are always derived from a plain ArrayView<T> source.
            // If a future codecs kernel calls `someBodyStructField.SubView(...)`,
            // extend this walk to delegate back into the special-case paths after
            // resolving to the underlying source.
            var effectiveSourceValue = value.Source.Resolve();
            bool wentThroughSubView = false;
            while (effectiveSourceValue is global::ILGPU.IR.Values.SubViewValue sv)
            {
                wentThroughSubView = true;
                var svOffsetVar = Load(sv.Offset);
                bool svOffsetIsEmuI64 = Backend.EnableI64Emulation
                    && sv.Offset.Resolve().BasicValueType == BasicValueType.Int64;
                string svOffsetExpr = svOffsetIsEmuI64 ? $"i64_to_i32({svOffsetVar})" : $"{svOffsetVar}";
                offsetExpr = $"(({svOffsetExpr}) + ({offsetExpr}))";
                effectiveSourceValue = sv.Source.Resolve();
            }
            if (wentThroughSubView)
            {
                var effectiveSource = Load(effectiveSourceValue);
                AppendLine($"let {target.Name} = &{effectiveSource.Name}[{offsetExpr}];");
                return;
            }

            // --- BODY STRUCT VIEW FIELD ACCESS ---
            // When LoadElementAddress is called on a GetField result from a body struct parameter,
            // redirect to the correct per-field binding (e.g. param1_f0[offset]).
            // This handles: GetField(bodyStruct, viewFieldIdx) → LoadElementAddress(viewField, offset)
            if (value.Source.Resolve() is global::ILGPU.IR.Values.GetField gf
                && gf.ObjectValue.Resolve() is global::ILGPU.IR.Values.Parameter bsParam
                && _bodyStructParams.TryGetValue(bsParam.Index, out var bsFields))
            {
                int fieldIdx = gf.FieldSpan.Index;
                if (fieldIdx < bsFields.Count && bsFields[fieldIdx].IsView)
                {
                    string bindingName = bsFields[fieldIdx].BindingName;
                    var elemType = GetBufferElementType(bsFields[fieldIdx].FieldType);
                    string fieldTypeStr = TypeGenerator[elemType];
                    
                    bool isEmuF64Field = Backend.EnableF64Emulation && fieldTypeStr == "emu_f64";
                    bool isEmuI64Field = Backend.EnableI64Emulation && (fieldTypeStr == "emu_i64" || fieldTypeStr == "emu_u64");

                    if (isEmuF64Field || isEmuI64Field)
                    {
                        _emulatedVarMappings[target.Name] = (bsParam.Index, fieldIdx, isEmuF64Field);

                        AppendIndent();
                        if (bsFields[fieldIdx].ViewOffsetSlot >= 0)
                        {
                            _bodyReferencesScalarParams = true;
                            int voSlotEmu = bsFields[fieldIdx].ViewOffsetSlot;
                            Builder.Append($"let {target.Name}_base_idx = i32(_scalar_params[{voSlotEmu}]) + i32({offsetExpr}) * 2;");
                        }
                        else
                        {
                            Builder.Append($"let {target.Name}_base_idx = i32({offsetExpr}) * 2;");
                        }
                        Builder.AppendLine();
                        
                        AppendIndent();
                        Builder.Append($"let {target.Name} = &{bindingName};");
                        Builder.AppendLine();
                        return;
                    }

                    // Check for packed struct body-struct view field
                    if (_packedStructBSFieldLayouts.TryGetValue((bsParam.Index, fieldIdx), out var bsPsLayout))
                    {
                        int psStrideU32s = bsPsLayout.Sum(f => f.U32Count);
                        AppendIndent();
                        if (bsFields[fieldIdx].ViewOffsetSlot >= 0)
                        {
                            _bodyReferencesScalarParams = true;
                            int voSlotBS = bsFields[fieldIdx].ViewOffsetSlot;
                            Builder.Append($"let {target.Name}_base_idx = i32(_scalar_params[{voSlotBS}]) + i32({offsetExpr}) * {psStrideU32s};");
                        }
                        else
                        {
                            Builder.Append($"let {target.Name}_base_idx = i32({offsetExpr}) * {psStrideU32s};");
                        }
                        Builder.AppendLine();
                        AppendIndent();
                        Builder.Append($"let {target.Name} = &{bindingName};");
                        Builder.AppendLine();
                        _packedStructLEAVars[target.Name] = (bindingName, bsPsLayout);
                        return;
                    }

                    // --- View offset adjustment for body struct view fields ---
                    // Body struct view fields may be sub-views with non-zero aligned offsets.
                    // The element offset is packed into _scalar_params by the runtime.
                    string adjustedOffsetExprBS = offsetExpr;
                    if (bsFields[fieldIdx].ViewOffsetSlot >= 0)
                    {
                        _bodyReferencesScalarParams = true;
                        int voSlot = bsFields[fieldIdx].ViewOffsetSlot;
                        adjustedOffsetExprBS = $"(i32(_scalar_params[{voSlot}]) + {offsetExpr})";
                    }

                    // SUB-WORD body-struct view field: backing storage is array<atomic<u32>>,
                    // a direct &binding[index] pointer is not valid for Load/Store of i8/i16/Half.
                    // Register the same way the direct view-param path does so the Load/Store
                    // codegen unpacks via shift/mask (and _f16_to_f32 for Half). This is what
                    // lets RadixSortPair<Half, int>.Key go through the Half buffer correctly:
                    // before this hook, the body-struct Half load emitted plain `*&atomic<u32>`
                    // which silently mis-decoded the value (Chrome treated it as bit-stuffed
                    // f32) and the Half radix sort produced wrong output.
                    if (elemType is PrimitiveType bsLeaPt)
                    {
                        int bsLeaElemSize = 0;
                        bool bsLeaIsFloat16 = false;
                        bool bsLeaIsBFloat16 = false;
                        switch (bsLeaPt.BasicValueType)
                        {
                            case BasicValueType.Int8: bsLeaElemSize = 1; break;
                            case BasicValueType.Int16: bsLeaElemSize = 2; break;
                            case BasicValueType.Float16:
                                if (!Backend.HasShaderF16) { bsLeaElemSize = 2; bsLeaIsFloat16 = true; }
                                break;
                            case BasicValueType.BFloat16:
                                bsLeaElemSize = 2; bsLeaIsBFloat16 = true; // always emulated
                                break;
                        }
                        if (bsLeaElemSize > 0)
                        {
                            int synthIdx = (bsParam.Index + 1) * 1000 + fieldIdx;
                            // Make sure registration is in place even if the GenerateHeader path
                            // didn't reach here (defensive: register just-in-time).
                            if (!_subWordParams.ContainsKey(synthIdx))
                                _subWordParams[synthIdx] = bsLeaElemSize;
                            if (bsLeaIsFloat16)
                                _subWordFloat16Params.Add(synthIdx);
                            if (bsLeaIsBFloat16)
                                _subWordBFloat16Params.Add(synthIdx);
                            _subWordBodyStructBindingNames[synthIdx] = bindingName;
                            _subWordLEAVars[target.Name] = synthIdx;
                            // Sub-word LEA stores the i32 element index, not a pointer (matches
                            // the direct view-param path emit). The Load/Store codegen will
                            // compute the u32 word index and bit shift from this offset.
                            //
                            // Cross-block hoist (Bug D phase 7 follow-up, 2026-05-05):
                            // mirror the direct-param sub-word LEA path — when the LEA value
                            // has cross-block uses AND we're in deferred-decl mode, hoist the
                            // var decl to function scope to avoid "unresolved value 'v_X'"
                            // when the use is in a sibling WGSL block.
                            bool isCrossBlockBsSubWord = _crossBlockPointers.Contains(value)
                                && _useDeferredDeclarations;
                            if (isCrossBlockBsSubWord && declaredVariables.Add(target.Name))
                            {
                                _deferredVarDeclarations.Add($"    var {target.Name} : i32;");
                                AppendLine($"{target.Name} = {adjustedOffsetExprBS};");
                            }
                            else if (isCrossBlockBsSubWord)
                            {
                                AppendLine($"{target.Name} = {adjustedOffsetExprBS};");
                            }
                            else
                            {
                                AppendLine($"var {target.Name} : i32 = {adjustedOffsetExprBS};");
                            }
                            return;
                        }
                    }

                    if (_crossBlockPointers.Contains(value))
                    {
                        _crossBlockPointerExprs[target.Name] = $"{bindingName}[{adjustedOffsetExprBS}]";
                        AppendLine($"let {target.Name} = &{bindingName}[{adjustedOffsetExprBS}];");
                    }
                    else
                    {
                        AppendLine($"let {target.Name} = &{bindingName}[{adjustedOffsetExprBS}];");
                    }
                    return;
                }
            }
            if (ResolveToParameter(value.Source) is global::ILGPU.IR.Values.Parameter param)
            {
                int paramOffset = KernelParamOffset;
                if (param.Index >= paramOffset)
                {
                    // Direct-param coalesce member (NON-sub-word): redirect LEA to the
                    // leader's shared binding name. The runtime fills _scalar_params[voSlot]
                    // with this member's u32-element offset within the shared coalesced buffer.
                    // For SUB-WORD coalesce members (IsSubWord=true), we fall through to the
                    // existing sub-word LEA path below — _subWordBodyStructBindingNames was
                    // populated for those members in DecideCoalesceGroups so the sub-word
                    // Load/Store codegen finds the leader's binding name automatically; the
                    // adjustedOffsetExpr already includes _scalar_params[voSlot] addition.
                    // (rc.16 direct-param coalesce, 2026-05-05.)
                    if (_directParamCoalesceMembership.TryGetValue(param.Index, out var dpGroup)
                        && !dpGroup.IsSubWord)
                    {
                        _bodyReferencesScalarParams = true;
                        int dpVoSlot = _viewOffsetScalarSlots[param.Index];
                        string dpAdjustedOffsetExpr = $"(i32(_scalar_params[{dpVoSlot}]) + {offsetExpr})";

                        if (_crossBlockPointers.Contains(value))
                        {
                            _crossBlockPointerExprs[target.Name] = $"{dpGroup.BindingName}[{dpAdjustedOffsetExpr}]";
                        }
                        AppendIndent();
                        Builder.Append($"let {target.Name} = &{dpGroup.BindingName}[{dpAdjustedOffsetExpr}];");
                        Builder.AppendLine();
                        return;
                    }

                    // --- View offset adjustment ---
                    // With offset=0 binding, the buffer starts at byte 0 (not sub-view start).
                    // ILGPU IR assumes LoadElementAddress addresses relative to the sub-view.
                    // The u32 padding offset from _scalar_params is added OUTSIDE the stride
                    // multiplication: base_idx = u32Offset + i * stride.
                    // This is correct because u32Offset = padding/4 (raw u32 distance from
                    // binding start to view start), independent of element or packed stride.
                    bool hasViewOffset = _viewOffsetScalarSlots.TryGetValue(param.Index, out int voSlot);
                    if (hasViewOffset) _bodyReferencesScalarParams = true;
                    string adjustedOffsetExpr = hasViewOffset
                        ? $"(i32(_scalar_params[{voSlot}]) + {offsetExpr})"
                        : offsetExpr;

                    // Check if this is an emulated 64-bit buffer
                    if (_emulatedF64Params.Contains(param.Index) || _emulatedI64Params.Contains(param.Index))
                    {
                        bool isF64 = _emulatedF64Params.Contains(param.Index);
                        // Register for Load/Store to know this needs conversion. FieldIdx=-1 means direct view param.
                        _emulatedVarMappings[target.Name] = (param.Index, -1, isF64);

                        // base_idx = u32Offset + i * 2  (u32Offset already in scalar params as padding/4)
                        AppendIndent();
                        if (hasViewOffset)
                            Builder.Append($"let {target.Name}_base_idx = i32(_scalar_params[{voSlot}]) + i32({offsetExpr}) * 2;");
                        else
                            Builder.Append($"let {target.Name}_base_idx = i32({offsetExpr}) * 2;");
                        Builder.AppendLine();
                        AppendIndent();
                        Builder.Append($"let {target.Name} = &param{param.Index};");
                        Builder.AppendLine();
                        return;
                    }

                    // Check if this is a packed struct buffer (struct with emu fields, CPU-layout packed)
                    if (_packedStructLayouts.TryGetValue(param.Index, out var psLayout))
                    {
                        int psStrideU32s = psLayout.Sum(f => f.U32Count);
                        // base_idx = u32Offset + i * stride  (u32Offset already in scalar params as padding/4)
                        AppendIndent();
                        if (hasViewOffset)
                            Builder.Append($"let {target.Name}_base_idx = i32(_scalar_params[{voSlot}]) + i32({offsetExpr}) * {psStrideU32s};");
                        else
                            Builder.Append($"let {target.Name}_base_idx = i32({offsetExpr}) * {psStrideU32s};");
                        Builder.AppendLine();
                        AppendIndent();
                        Builder.Append($"let {target.Name} = &param{param.Index};");
                        Builder.AppendLine();
                        _packedStructLEAVars[target.Name] = ($"param{param.Index}", psLayout);
                        return;
                    }

                    // SUB-WORD VIEW FIX:
                    // WGSL has no 8/16-bit types — buffer is array<u32>. For sub-word views,
                    // we can't take a pointer to a sub-word within a u32 word. Instead, store the
                    // element index and extract the sub-word value in the Load codegen.
                    // Safety net: if registration was skipped (e.g. body-struct continue),
                    // detect sub-word from param type directly.
                    if (!_subWordParams.TryGetValue(param.Index, out var elemSize))
                    {
                        var leaElemType = GetBufferElementType(param.ParameterType);
                        if (leaElemType is PrimitiveType leaPt)
                        {
                            if (leaPt.BasicValueType == BasicValueType.Int8)
                                elemSize = 1;
                            else if (leaPt.BasicValueType == BasicValueType.Int16)
                                elemSize = 2;
                            else if (leaPt.BasicValueType == BasicValueType.Float16 && !Backend.HasShaderF16)
                            {
                                elemSize = 2;
                                _subWordFloat16Params.Add(param.Index);
                            }
                            else if (leaPt.BasicValueType == BasicValueType.BFloat16)
                            {
                                elemSize = 2;
                                _subWordBFloat16Params.Add(param.Index);
                            }
                            if (elemSize > 0)
                                _subWordParams[param.Index] = elemSize; // register for Load/Store
                        }
                    }
                    if (elemSize > 0)
                    {
                        _subWordLEAVars[target.Name] = param.Index;
                        // SUB-WORD LEA STORES AN i32 OFFSET, NOT A POINTER.
                        //
                        // Cross-block fix (Tuvok local.8 PMT, 2026-05-05):
                        // when the LEA result is used in a different basic block from
                        // where it's defined (added to _crossBlockPointers by
                        // HoistCrossBlockVariables), the block-local `var v_X : i32 = ...`
                        // declaration is out of scope at the use site — Naga rejects with
                        // "unresolved value 'v_X'". Hoist the var declaration to function
                        // scope (deferred-decl list) so it's visible everywhere, and emit
                        // only the assignment in the current block.
                        bool isCrossBlockSubWord = _crossBlockPointers.Contains(value)
                            && _useDeferredDeclarations;
                        AppendIndent();
                        if (isCrossBlockSubWord && declaredVariables.Add(target.Name))
                        {
                            // Pre-declare at function scope; emit assignment locally.
                            _deferredVarDeclarations.Add($"    var {target.Name} : i32;");
                            Builder.Append($"{target.Name} = {adjustedOffsetExpr};");
                        }
                        else if (isCrossBlockSubWord)
                        {
                            // Already in declaredVariables (pre-declared). Just assign.
                            Builder.Append($"{target.Name} = {adjustedOffsetExpr};");
                        }
                        else
                        {
                            // Single-block use: combined declaration + assignment.
                            // 'var : i32' (not 'let') lets the post-processing pick it up
                            // as i32 — `let` would trigger the let→var converter which
                            // inspects valueVariables.Type (ptr<...> for a LEA target) and
                            // skips the var declaration (ptr types aren't constructible),
                            // leaving v_N used without any declaration.
                            Builder.Append($"var {target.Name} : i32 = {adjustedOffsetExpr};");
                        }
                        Builder.AppendLine();
                        if (_crossBlockPointers.Contains(value))
                        {
                            // Cross-block sub-word ptr expr — must reference the actual bound
                            // identifier. For sub-word direct-param coalesce members,
                            // _subWordBodyStructBindingNames maps to the leader's BindingName;
                            // fall back to `param{N}` only when no coalesce membership exists.
                            // Defense-in-depth alongside the pre-decl above; some code paths
                            // (e.g. Load via _crossBlockPointerExprs) substitute the inline
                            // expression directly without referencing the var name.
                            string swBindRef = _subWordBodyStructBindingNames.TryGetValue(param.Index, out var swCoalBindName)
                                ? swCoalBindName : $"param{param.Index}";
                            if (elemSize == 1) // byte
                                _crossBlockPointerExprs[target.Name] = $"((u32(atomicLoad(&{swBindRef}[(u32({target.Name}) / 4u)])) >> ((u32({target.Name}) % 4u) * 8u)) & 0xFFu)";
                            else // short (2 bytes)
                                _crossBlockPointerExprs[target.Name] = $"((u32(atomicLoad(&{swBindRef}[(u32({target.Name}) / 2u)])) >> ((u32({target.Name}) % 2u) * 16u)) & 0xFFFFu)";
                        }
                        return;
                    }

                    // CROSS-BLOCK POINTER FIX:
                    // If this LoadElementAddress is used in a different switch-case block,
                    // its `let v_X = &paramN[offset]` will be out of scope at the use site.
                    // Instead, register an inline expression so Load/Store can substitute it.
                    if (_crossBlockPointers.Contains(value))
                    {
                        // Register the inline array access expression
                        _crossBlockPointerExprs[target.Name] = $"param{param.Index}[{adjustedOffsetExpr}]";
                        // Still emit a local declaration for same-block uses
                        AppendIndent();
                        Builder.Append($"let {target.Name} = &param{param.Index}[{adjustedOffsetExpr}];");
                        Builder.AppendLine();
                        return;
                    }

                    AppendIndent();
                    Builder.Append($"let {target.Name} = &param{param.Index}[{adjustedOffsetExpr}];");
                    Builder.AppendLine();
                    return;
                }
            }

            var sourceVal = Load(value.Source);
            if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
            {
                var _leaKernelName = Method.Handle.Name ?? "";
                if (_leaKernelName.Contains("MultiPassScan") || _leaKernelName.Contains("SingleGroupScan"))
                    WebGPUBackend.Log($"[DIAG-LEA] Fallback LEA: target={target.Name}, source={sourceVal.Name} (type={sourceVal.Type}), IR source type={value.Source.Type}, IR source kind={value.Source.Resolve().GetType().Name}, isNewView={value.Source.Resolve() is NewView}, isPointerType={value.Source.Type.IsPointerType}");
            }

            AppendIndent();
            Builder.Append($"let {target.Name} = ");

            // POINTER DEREFERENCE LOGIC
            // If the source comes from NewView, it's a pointer (let v_3 = &v_4).
            // To access element at offset: (*ptr)[offset] -> &(*ptr)[offset] gives the address.
            // NOTE: `value.Source is NewView` doesn't pattern-match through ValueReference;
            // use `Resolve()` for the IR walk-back. The original check `is NewView` was
            // broken (always false on a ValueReference); the second condition
            // `IsPointerType` covered the common case but missed the
            // NewView-wrapping-scalarized-alloca shape, leaving WGSL with `&v[idx]`
            // on a scalar i32. Resolved version fires correctly for both shapes -
            // SetupAllocations now declares the alloca as `array<T, 1>` for
            // user-array allocations and the NewView fix above emits `&v_alloca`
            // (yielding `ptr<function, array<T, 1>>`), so `&(*v)[idx]` produces
            // the right `ptr<function, T>` for the element address.
            if (value.Source.Resolve() is global::ILGPU.IR.Values.NewView || value.Source.Type.IsPointerType)
            {
                Builder.Append($"&(*{sourceVal})[{offsetExpr}];");
            }
            else
            {
                Builder.Append($"&{sourceVal}[{offsetExpr}];");
            }
            Builder.AppendLine();

            // If this pointer indexes a bool element in shared (workgroup) memory, the backing
            // array was remapped to array<i32,N> (WGSL forbids workgroup bool), so remember the
            // pointer and have Load/Store convert the i32 0/1 storage to/from WGSL bool. Detect
            // from the IR pointer type (address space + element) — robust regardless of whether
            // the source was the shared var directly or a NewView alias of it.
            if (value.Type is global::ILGPU.IR.Types.PointerType leaBoolPtr
                && leaBoolPtr.AddressSpace == MemoryAddressSpace.Shared
                && TypeGenerator[leaBoolPtr.ElementType] == "bool")
                _boolSharedPtrs.Add(target.Name);
        }

        public override void GenerateCode(global::ILGPU.IR.Values.Load loadVal)
        {
            var target = Load(loadVal);
            var source = Load(loadVal.Source);

            // Bool-remapped shared array: storage is i32 (0/1). The IR load value is usually
            // already i32 (ILGPU lowers bool memory through ints), in which case a plain
            // `target = *ptr` is correct. Only when the load's own WGSL type is `bool` do we
            // convert (`*ptr != 0i`) — otherwise we'd assign a bool to an i32 target. The
            // matching Store mirrors this.
            if (_boolSharedPtrs.Contains(source.Name) || IsBoolSharedPointer(loadVal.Source))
            {
                Declare(target);
                if (TypeGenerator[loadVal.Type] == "bool")
                    AppendLine($"{target} = (*{source}) != 0i;");
                else
                    AppendLine($"{target} = *{source};");
                return;
            }

            // Special handling for loading an ArrayView parameter (which is a struct).
            // We cannot 'load' the struct from the buffer binding (which is array<T>).
            // Instead, we treat the 'loaded' value as a pointer/alias to the binding.

            // CRITICAL FIX: Only alias if the source IS the parameter node itself.
            // Do NOT alias if the source is a derived pointer (e.g. LoadElementAddress),
            // as that would prevent loading actual data values.
            // Note: loadVal.Source is a ValueReference struct. To pattern match against Value types (classes),
            // we must use .Resolve() to get the underlying Value object.
            if (loadVal.Source.Resolve() is global::ILGPU.IR.Values.Parameter param &&
                param.Type is global::ILGPU.IR.Types.ViewType)
            {
                // Verify index (skip implicit kernel index if any)
                int paramOffset = KernelParamOffset;
                if (param.Index >= paramOffset)
                {
                    // Direct-param coalesce member: redirect alias to leader's binding.
                    // For non-coalesced direct params, alias the param's own binding.
                    // (rc.16 direct-param coalesce — handle Load(Parameter ViewType) pattern.)
                    if (_directParamCoalesceMembership.TryGetValue(param.Index, out var dpLoadAliasGroup))
                    {
                        AppendLine($"let {target} = &{dpLoadAliasGroup.BindingName};");
                    }
                    else
                    {
                        AppendLine($"let {target} = &param{param.Index};");
                    }
                    return;
                }
            }

            // Check for emulated 64-bit buffer access
            if (_emulatedVarMappings.TryGetValue(source.ToString(), out var emulInfo))
            {
                // This is loading from an emulated buffer - need to read 2 u32 values and convert
                string baseIdxVar = $"{source}_base_idx";
                // If the buffer is atomic (mixed atomic + non-atomic access), use atomicLoad
                bool isAtomicBuf = IsAtomicPointer(loadVal.Source);
                string lo = isAtomicBuf
                    ? $"atomicLoad(&(*{source})[u32({baseIdxVar})])"
                    : $"(*{source})[u32({baseIdxVar})]";
                string hi = isAtomicBuf
                    ? $"atomicLoad(&(*{source})[u32({baseIdxVar}) + 1u])"
                    : $"(*{source})[u32({baseIdxVar}) + 1u]";
                if (emulInfo.IsF64)
                {
                    // emu_f64: convert IEEE 754 bits to double-float
                    Declare(target);
                    AppendLine($"{target} = f64_from_ieee754_bits({lo}, {hi});");
                }
                else
                {
                    // emu_i64: just combine two u32 into vec2<u32>
                    Declare(target);
                    AppendLine($"{target} = emu_i64({lo}, {hi});");
                }
                return;
            }

            // Check for packed struct scalar field pointer (from LoadFieldAddress on a packed struct)
            if (_packedScalarFieldPtrs.TryGetValue(source.ToString(), out var psScalarLoad))
            {
                string baseIdxVar = $"{source}_base_idx";
                string elemRef = $"(*{source})[u32({baseIdxVar})]";
                string fieldExpr = psScalarLoad.FieldInfo.WgslType switch
                {
                    "i32" => $"bitcast<i32>({elemRef})",
                    "f32" => $"bitcast<f32>({elemRef})",
                    "bool" => $"bool({elemRef})",
                    _ => elemRef  // u32 or other
                };
                Declare(target);
                AppendLine($"{target} = {fieldExpr};");
                return;
            }

            // Check for packed struct load (struct with emu fields, stored as CPU-layout u32 array)
            if (_packedStructLEAVars.TryGetValue(source.ToString(), out var psLoadInfo))
            {
                string baseIdxVar = $"{source}_base_idx";
                Declare(target);
                for (int fi = 0; fi < psLoadInfo.Layout.Count; fi++)
                {
                    var field = psLoadInfo.Layout[fi];
                    string fieldRef = $"(*{source})[u32({baseIdxVar}) + {field.U32Offset}u]";
                    string fieldRef1 = $"(*{source})[u32({baseIdxVar}) + {field.U32Offset + 1}u]";
                    string fieldExpr;
                    if (field.IsEmuF64)
                        fieldExpr = $"f64_from_ieee754_bits({fieldRef}, {fieldRef1})";
                    else if (field.IsEmuI64OrU64)
                        fieldExpr = $"{field.WgslType}({fieldRef}, {fieldRef1})";
                    else if (field.WgslType == "f32")
                        fieldExpr = $"bitcast<f32>({fieldRef})";
                    else if (field.WgslType == "i32")
                        fieldExpr = $"bitcast<i32>({fieldRef})";
                    else
                        fieldExpr = fieldRef; // u32 or other: raw u32
                    AppendLine($"{target}.field_{fi} = {fieldExpr};");
                }
                return;
            }

            // SUB-WORD VIEW FIX: Extract sub-word value from packed atomic<u32> word
            if (_subWordLEAVars.TryGetValue(source.ToString(), out var subWordParamIdx))
            {
                var idx = source.ToString();
                var elemSize = _subWordParams[subWordParamIdx];
                // Body-struct view fields use a synthetic param index whose backing
                // binding name is `param{outerN}_f{fieldIdx}`, not `param{N}`.
                string subWordBinding = _subWordBodyStructBindingNames.TryGetValue(subWordParamIdx, out var bsName)
                    ? bsName
                    : $"param{subWordParamIdx}";
                string extractExpr;
                if (elemSize == 1)
                {
                    // Byte extraction: 4 bytes per atomic<u32> word
                    var wordIdx = $"(u32({idx}) / 4u)";
                    var shift = $"((u32({idx}) % 4u) * 8u)";
                    var rawByte = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFu)";
                    if (_subWordUnsignedParams.Contains(subWordParamIdx))
                        extractExpr = $"i32({rawByte})"; // byte: zero-extend (0-255)
                    else
                        extractExpr = $"select(i32({rawByte}), (i32({rawByte}) - 256), ({rawByte}) >= 128u)"; // sbyte: sign-extend
                }
                else if (_subWordFloat16Params.Contains(subWordParamIdx))
                {
                    // Float16 extraction: 2 halves per atomic<u32> word, call _f16_to_f32 helper.
                    // Helper from WGSLEmulationLibrary.F16Functions handles all cases correctly:
                    // signed zero, denormals (flushed), normal, Inf, NaN. Previous inline code
                    // mishandled exp==0 denormals and exp==31 Inf/NaN (produced huge finites).
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    var rawExpr = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFFFu)";
                    extractExpr = $"_f16_to_f32({rawExpr})";
                }
                else if (_subWordBFloat16Params.Contains(subWordParamIdx))
                {
                    // bfloat16 extraction: 2 bf16 per atomic<u32> word, call _bf16_to_f32 helper
                    // (same 2-byte sub-word layout as Float16, different conversion).
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    var rawExpr = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFFFu)";
                    extractExpr = $"_bf16_to_f32({rawExpr})";
                }
                else if (_subWordUnsignedParams.Contains(subWordParamIdx))
                {
                    // UInt16 extraction: 2 ushorts per atomic<u32> word, zero-extension
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    var rawExpr = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFFFu)";
                    extractExpr = $"i32({rawExpr})"; // zero-extend: & 0xFFFF already ensures 0-65535
                }
                else
                {
                    // Int16 extraction: 2 shorts per atomic<u32> word, with sign extension
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    var rawExpr = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFFFu)";
                    // Sign-extend: if bit 15 is set, extend to full i32 negative
                    extractExpr = $"select(i32({rawExpr}), (i32({rawExpr}) - 65536), ({rawExpr}) >= 32768u)";
                }
                if (_hoistedPrimitives.Contains(loadVal))
                    AppendLine($"{target} = {extractExpr};");
                else
                {
                    Declare(target);
                    AppendLine($"{target} = {extractExpr};");
                }
                return;
            }

            // CROSS-BLOCK POINTER FIX: Use inline expression instead of out-of-scope pointer
            if (_crossBlockPointerExprs.TryGetValue(source.ToString(), out var inlineExpr))
            {
                if (_hoistedPrimitives.Contains(loadVal))
                {
                    AppendLine($"{target} = {inlineExpr};");
                }
                else
                {
                    Declare(target);
                    AppendLine($"{target} = {inlineExpr};");
                }
                return;
            }

            // Local alloca variables are WGSL 'var' value types, not pointers — no dereference.
            bool isAllocaVar = _localAllocaVarNames.Contains(source.Name);
            // Atomic buffers require atomicLoad instead of *ptr dereference.
            // When a buffer has both atomic ops (Atomic.And) and non-atomic reads,
            // the buffer is declared array<atomic<T>> and ALL reads must use atomicLoad.
            bool isAtomic = !isAllocaVar && IsAtomicPointer(loadVal.Source);
            string rhs = isAllocaVar ? $"{source}" : isAtomic ? $"atomicLoad({source})" : $"*{source}";

            // Check if we already declared this at the top
            if (_hoistedPrimitives.Contains(loadVal))
            {
                AppendLine($"{target} = {rhs};");
            }
            else
            {
                // Fallback to your stable declaration logic
                Declare(target);
                AppendLine($"{target} = {rhs};");
            }
        }

        public override void GenerateCode(global::ILGPU.IR.Values.Store storeVal)
        {
            var address = Load(storeVal.Target);
            var val = Load(storeVal.Value);

            // Bool-remapped shared array: storage is i32 (0/1). ILGPU already lowers the bool
            // value to an i32 (0/1) before the store (e.g. `select(0,1,cond)` / `i32(1)` for
            // `true`), so a plain `*ptr = val` (i32→i32) is correct — do NOT wrap in another
            // select (that would be `select(i32,i32,i32)`, which WGSL rejects). The matching
            // Load converts the i32 storage back to a WGSL bool. We still short-circuit here so
            // the value isn't misrouted through the emulated-64/packed branches below.
            if (_boolSharedPtrs.Contains(address.Name) || IsBoolSharedPointer(storeVal.Target))
            {
                if (TypeGenerator[storeVal.Value.Type] == "bool")
                    AppendLine($"*{address} = select(0i, 1i, {val});");
                else
                    AppendLine($"*{address} = {val};");
                return;
            }

            // Check for emulated 64-bit buffer store (Parameter arrays registered earlier)
            if (_emulatedVarMappings.TryGetValue(address.ToString(), out var emulInfo))
            {
                string baseIdxVar = $"{address}_base_idx";
                // If the buffer is atomic (mixed atomic + non-atomic access), use atomicStore
                bool isAtomicBuf = IsAtomicPointer(storeVal.Target);
                if (emulInfo.IsF64)
                {
                    // emu_f64: convert double-float back to IEEE 754 bits
                    AppendLine($"let _bits_{address} = f64_to_ieee754_bits({val});");
                    if (isAtomicBuf)
                    {
                        AppendLine($"atomicStore(&(*{address})[u32({baseIdxVar})], _bits_{address}.x);");
                        AppendLine($"atomicStore(&(*{address})[u32({baseIdxVar}) + 1u], _bits_{address}.y);");
                    }
                    else
                    {
                        AppendLine($"(*{address})[u32({baseIdxVar})] = _bits_{address}.x;");
                        AppendLine($"(*{address})[u32({baseIdxVar}) + 1u] = _bits_{address}.y;");
                    }
                }
                else
                {
                    // emu_i64: split vec2<u32> into two u32 values
                    if (isAtomicBuf)
                    {
                        AppendLine($"atomicStore(&(*{address})[u32({baseIdxVar})], {val}.x);");
                        AppendLine($"atomicStore(&(*{address})[u32({baseIdxVar}) + 1u], {val}.y);");
                    }
                    else
                    {
                        AppendLine($"(*{address})[u32({baseIdxVar})] = {val}.x;");
                        AppendLine($"(*{address})[u32({baseIdxVar}) + 1u] = {val}.y;");
                    }
                }
                return;
            }

            // Check for packed struct scalar field pointer store (from LoadFieldAddress on a packed struct)
            if (_packedScalarFieldPtrs.TryGetValue(address.ToString(), out var psScalarStore))
            {
                string baseIdxVar = $"{address}_base_idx";
                string storeExpr = psScalarStore.FieldInfo.WgslType switch
                {
                    "i32" => $"bitcast<u32>({val})",
                    "f32" => $"bitcast<u32>({val})",
                    "bool" => $"u32({val})",
                    _ => $"{val}"  // u32 or other
                };
                AppendLine($"(*{address})[u32({baseIdxVar})] = {storeExpr};");
                return;
            }

            // Check for packed struct store (struct with emu fields, stored as CPU-layout u32 array)
            if (_packedStructLEAVars.TryGetValue(address.ToString(), out var psStoreInfo))
            {
                string baseIdxVar = $"{address}_base_idx";
                for (int fi = 0; fi < psStoreInfo.Layout.Count; fi++)
                {
                    var field = psStoreInfo.Layout[fi];
                    string slotRef = $"(*{address})[u32({baseIdxVar}) + {field.U32Offset}u]";
                    string slotRef1 = $"(*{address})[u32({baseIdxVar}) + {field.U32Offset + 1}u]";
                    if (field.IsEmuF64)
                    {
                        string bitsVar = $"_ps_bits_{address}_{fi}";
                        AppendLine($"let {bitsVar} = f64_to_ieee754_bits({val}.field_{fi});");
                        AppendLine($"{slotRef} = {bitsVar}.x;");
                        AppendLine($"{slotRef1} = {bitsVar}.y;");
                    }
                    else if (field.IsEmuI64OrU64)
                    {
                        AppendLine($"{slotRef} = {val}.field_{fi}.x;");
                        AppendLine($"{slotRef1} = {val}.field_{fi}.y;");
                    }
                    else if (field.WgslType == "f32" || field.WgslType == "i32")
                    {
                        AppendLine($"{slotRef} = bitcast<u32>({val}.field_{fi});");
                    }
                    else
                    {
                        AppendLine($"{slotRef} = {val}.field_{fi};"); // u32 or other: direct
                    }
                }
                return;
            }

            // SUB-WORD STORE: atomic read-modify-write on packed atomic<u32> word.
            // Multiple threads may target different sub-word slots in the same u32 word.
            // Non-atomic RMW causes data races (one thread's write overwrites the other's).
            // Fix: atomicAnd to clear target bits, atomicOr to set new value.
            // This is safe because each thread operates on non-overlapping bit ranges.
            if (_subWordLEAVars.TryGetValue(address.ToString(), out var storeParamIdx))
            {
                var idx = address.ToString();
                var elemSize = _subWordParams[storeParamIdx];
                // Body-struct view fields use a synthetic param index whose backing binding
                // name is `param{outerN}_f{fieldIdx}`, not `param{N}`. See _subWordBodyStructBindingNames.
                string subWordBinding = _subWordBodyStructBindingNames.TryGetValue(storeParamIdx, out var bsName)
                    ? bsName
                    : $"param{storeParamIdx}";
                if (elemSize == 1)
                {
                    // Byte store: atomic RMW on atomic<u32> word (4 bytes per word)
                    // WGSL requires explicit parenthesization for mixed-precedence operators
                    var wordIdx = $"(u32({idx}) / 4u)";
                    var shift = $"((u32({idx}) % 4u) * 8u)";
                    AppendLine($"atomicAnd(&{subWordBinding}[{wordIdx}], ~(0xFFu << {shift}));");
                    AppendLine($"atomicOr(&{subWordBinding}[{wordIdx}], ((u32({val}) & 0xFFu) << {shift}));");
                }
                else if (_subWordFloat16Params.Contains(storeParamIdx))
                {
                    // Float16 store: call _f32_to_f16 helper for the conversion, then pack
                    // into the atomic<u32> word via RMW. Helper from WGSLEmulationLibrary
                    // handles underflow (flush to zero), overflow (clamp exp to 31 with
                    // mantissa preserved so NaN stays NaN), and normal range. Previous
                    // inline store zeroed the mantissa on overflow, losing NaN.
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    AppendLine($"atomicAnd(&{subWordBinding}[{wordIdx}], ~(0xFFFFu << {shift}));");
                    AppendLine($"atomicOr(&{subWordBinding}[{wordIdx}], ((_f32_to_f16({val}) & 0xFFFFu) << {shift}));");
                }
                else if (_subWordBFloat16Params.Contains(storeParamIdx))
                {
                    // bfloat16 store: _f32_to_bf16 conversion, then pack into the atomic<u32>
                    // word via RMW (same 2-byte sub-word layout as Float16).
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    AppendLine($"atomicAnd(&{subWordBinding}[{wordIdx}], ~(0xFFFFu << {shift}));");
                    AppendLine($"atomicOr(&{subWordBinding}[{wordIdx}], ((_f32_to_bf16({val}) & 0xFFFFu) << {shift}));");
                }
                else // elemSize == 2, Int16/UInt16
                {
                    // Short store: atomic RMW on atomic<u32> word (2 shorts per word)
                    // WGSL requires explicit parenthesization for mixed-precedence operators
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    AppendLine($"atomicAnd(&{subWordBinding}[{wordIdx}], ~(0xFFFFu << {shift}));");
                    AppendLine($"atomicOr(&{subWordBinding}[{wordIdx}], ((u32({val}) & 0xFFFFu) << {shift}));");
                }
                return;
            }

            // CROSS-BLOCK POINTER FIX: Use inline expression instead of out-of-scope pointer
            if (_crossBlockPointerExprs.TryGetValue(address.ToString(), out var inlineExpr))
            {
                AppendLine($"{inlineExpr} = {val};");
                return;
            }

            // Local alloca variables are WGSL 'var' value types, not pointers — no dereference.
            if (_localAllocaVarNames.Contains(address.Name))
                AppendLine($"{address} = {val};");
            else if (IsAtomicPointer(storeVal.Target))
                AppendLine($"atomicStore({address}, {val});");
            else
                AppendLine($"*{address} = {val};");
        }

        public override void GenerateCode(global::ILGPU.IR.Values.GenericAtomic value)
        {
            string ptrStr = Load(value.Target).ToString();

            if (_emulatedVarMappings.TryGetValue(ptrStr, out var emulInfo))
            {
                 var target = Load(value);
                 var val = Load(value.Value);
                 Declare(target);

                 string valWgslType = TypeGenerator[value.Value.Type];
                 
                 string prefix = valWgslType switch
                 {
                     "emu_f64" => "f64",
                     "emu_u64" => "u64",
                     "emu_i64" => value.IsUnsigned ? "u64" : "i64",
                     _ => value.IsUnsigned ? "u64" : "i64"
                 };

                 string emuOp = value.Kind switch
                 {
                     global::ILGPU.IR.Values.AtomicKind.Max => $"{prefix}_max",
                     global::ILGPU.IR.Values.AtomicKind.Min => $"{prefix}_min",
                     global::ILGPU.IR.Values.AtomicKind.Add => $"{prefix}_add",
                     global::ILGPU.IR.Values.AtomicKind.Exchange => null,
                     _ => $"{prefix}_add"
                 };

                 string baseIdxVar = $"{ptrStr}_base_idx";

                 if (emuOp != null)
                 {
                     string oldVar = $"_emu64_old_{target.Name}";
                     if (valWgslType == "emu_f64")
                     {
                         // f64 atomics: stored as two u32 words (IEEE-754 bits).
                         // Min/Max/Exchange/Add all need spinlock (both words must be consistent).
                         bool needsF64Lock = value.Kind == global::ILGPU.IR.Values.AtomicKind.Min
                             || value.Kind == global::ILGPU.IR.Values.AtomicKind.Max
                             || value.Kind == global::ILGPU.IR.Values.AtomicKind.Exchange
                             || value.Kind == global::ILGPU.IR.Values.AtomicKind.Add;
                         if (needsF64Lock)
                         {
                             var lockKeyF64 = (emulInfo.ParamIndex, emulInfo.FieldIdx);
                             if (!_lockBufferNames.TryGetValue(lockKeyF64, out var lockNameF64))
                                 throw new System.InvalidOperationException($"No lock buffer for param{emulInfo.ParamIndex} field{emulInfo.FieldIdx} - ScanForAtomicUsage should have detected f64 {value.Kind}");
                             string lockIdxF64 = $"u32({baseIdxVar}) / 2u";
                             string oldLoF64 = $"_sl_old_lo_{target.Name}";
                             string oldHiF64 = $"_sl_old_hi_{target.Name}";

                             AppendLine($"// f64 {value.Kind}: spinlock on {lockNameF64}");
                             AppendLine($"loop {{");
                             PushIndent();
                             AppendLine($"let _sl_cas_{target.Name} = atomicCompareExchangeWeak(&{lockNameF64}[{lockIdxF64}], 0u, 1u);");
                             AppendLine($"if (_sl_cas_{target.Name}.exchanged) {{");
                             PushIndent();
                             // Critical section: read raw bits, convert to f64, compare, write if needed
                             AppendLine($"let {oldLoF64} = atomicLoad(&(*{ptrStr})[u32({baseIdxVar})]);");
                             AppendLine($"let {oldHiF64} = atomicLoad(&(*{ptrStr})[u32({baseIdxVar}) + 1u]);");
                             AppendLine($"let _sl_old_f64_{target.Name} = f64_from_ieee754_bits({oldLoF64}, {oldHiF64});");
                             if (value.Kind == global::ILGPU.IR.Values.AtomicKind.Exchange)
                             {
                                 string ieeeBitsExch = $"_sl_ieee_{target.Name}";
                                 AppendLine($"let {ieeeBitsExch} = f64_to_ieee754_bits({val});");
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar})], {ieeeBitsExch}.x);");
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar}) + 1u], {ieeeBitsExch}.y);");
                             }
                             else if (value.Kind == global::ILGPU.IR.Values.AtomicKind.Add)
                             {
                                 // Add: compute new = f64_add(old, val), store unconditionally
                                 string ieeeBitsNew = $"_sl_ieee_{target.Name}";
                                 AppendLine($"let _sl_new_f64_{target.Name} = f64_add(_sl_old_f64_{target.Name}, {val});");
                                 AppendLine($"let {ieeeBitsNew} = f64_to_ieee754_bits(_sl_new_f64_{target.Name});");
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar})], {ieeeBitsNew}.x);");
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar}) + 1u], {ieeeBitsNew}.y);");
                             }
                             else
                             {
                                 // Min/Max: compare using f64 emulation, conditionally write
                                 string cmpFnF64 = value.Kind == global::ILGPU.IR.Values.AtomicKind.Max ? "f64_gt" : "f64_lt";
                                 AppendLine($"if ({cmpFnF64}({val}, _sl_old_f64_{target.Name})) {{");
                                 PushIndent();
                                 string ieeeBitsNew = $"_sl_ieee_{target.Name}";
                                 AppendLine($"let {ieeeBitsNew} = f64_to_ieee754_bits({val});");
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar})], {ieeeBitsNew}.x);");
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar}) + 1u], {ieeeBitsNew}.y);");
                                 PopIndent();
                                 AppendLine($"}}");
                             }
                             AppendLine($"{target} = _sl_old_f64_{target.Name};");
                             AppendLine($"atomicStore(&{lockNameF64}[{lockIdxF64}], 0u);");
                             AppendLine($"break;");
                             PopIndent();
                             AppendLine($"}}");
                             PopIndent();
                             AppendLine($"}}");
                         }
                     }
                     else
                     {
                         // emu_i64 / emu_u64
                         // Check if this is a bitwise op (And/Or/Xor) - these can use
                         // independent i32 atomics on lo/hi halves (no carry between halves)
                         bool isBitwiseOp = value.Kind == global::ILGPU.IR.Values.AtomicKind.And
                             || value.Kind == global::ILGPU.IR.Values.AtomicKind.Or
                             || value.Kind == global::ILGPU.IR.Values.AtomicKind.Xor;

                         if (isBitwiseOp)
                         {
                             // Bitwise atomics on emulated i64: two independent i32 atomics
                             // No carry between halves - each bit is independent
                             string atomicOp = value.Kind switch
                             {
                                 global::ILGPU.IR.Values.AtomicKind.And => "atomicAnd",
                                 global::ILGPU.IR.Values.AtomicKind.Or => "atomicOr",
                                 global::ILGPU.IR.Values.AtomicKind.Xor => "atomicXor",
                                 _ => "atomicAnd" // unreachable
                             };
                             string oldLoVar = $"_emu64_old_lo_{target.Name}";
                             string oldHiVar = $"_emu64_old_hi_{target.Name}";
                             AppendLine($"let {oldLoVar} = {atomicOp}(&(*{ptrStr})[u32({baseIdxVar})], {val}.x);");
                             AppendLine($"let {oldHiVar} = {atomicOp}(&(*{ptrStr})[u32({baseIdxVar}) + 1u], {val}.y);");
                             AppendLine($"{target} = {valWgslType}({oldLoVar}, {oldHiVar});");
                         }
                         else if (value.Kind == global::ILGPU.IR.Values.AtomicKind.Add)
                         {
                             // i64 atomicAdd: lock-free CAS loop on lo half + atomicAdd on hi half.
                             // CAS on lo serializes low-half updates. atomicAdd on hi is commutative,
                             // so concurrent carries produce the correct result regardless of order.
                             string oldLoVar = $"_emu64_old_lo_{target.Name}";
                             string newLoVar = $"_emu64_new_lo_{target.Name}";
                             string carryVar = $"_emu64_carry_{target.Name}";
                             string casResVar = $"_emu64_cas_{target.Name}";
                             AppendLine($"// i64 atomicAdd: CAS-lo + atomicAdd-hi (lock-free)");
                             AppendLine($"var {oldLoVar} = atomicLoad(&(*{ptrStr})[u32({baseIdxVar})]);");
                             AppendLine($"loop {{");
                             PushIndent();
                             AppendLine($"let {newLoVar} = {oldLoVar} + {val}.x;");
                             AppendLine($"let {carryVar} = select(0u, 1u, {newLoVar} < {oldLoVar});");
                             AppendLine($"let {casResVar} = atomicCompareExchangeWeak(&(*{ptrStr})[u32({baseIdxVar})], {oldLoVar}, {newLoVar});");
                             AppendLine($"if ({casResVar}.exchanged) {{");
                             PushIndent();
                             AppendLine($"let _old_hi_{target.Name} = atomicAdd(&(*{ptrStr})[u32({baseIdxVar}) + 1u], {val}.y + {carryVar});");
                             AppendLine($"{target} = {valWgslType}({oldLoVar}, _old_hi_{target.Name});");
                             AppendLine($"break;");
                             PopIndent();
                             AppendLine($"}}");
                             AppendLine($"{oldLoVar} = {casResVar}.old_value;");
                             PopIndent();
                             AppendLine($"}}");
                         }
                         else
                         {
                             // i64 Min/Max/Exchange: spinlock on companion lock buffer.
                             // Both halves must be read/written atomically. A per-element
                             // spinlock serializes access to the i64 value.
                             var lockKey = (emulInfo.ParamIndex, emulInfo.FieldIdx);
                             if (!_lockBufferNames.TryGetValue(lockKey, out var lockName))
                                 throw new System.InvalidOperationException($"No lock buffer for param{emulInfo.ParamIndex} field{emulInfo.FieldIdx} - ScanForAtomicUsage should have detected i64 {value.Kind}");
                             string lockIdx = $"u32({baseIdxVar}) / 2u"; // one lock per i64 element (2 u32s)
                             string oldLoVar2 = $"_sl_old_lo_{target.Name}";
                             string oldHiVar2 = $"_sl_old_hi_{target.Name}";
                             string prefix2 = valWgslType == "emu_u64" || value.IsUnsigned ? "u64" : "i64";

                             AppendLine($"// i64 {value.Kind}: spinlock on {lockName}");
                             AppendLine($"loop {{");
                             PushIndent();
                             AppendLine($"let _sl_cas_{target.Name} = atomicCompareExchangeWeak(&{lockName}[{lockIdx}], 0u, 1u);");
                             AppendLine($"if (_sl_cas_{target.Name}.exchanged) {{");
                             PushIndent();
                             // Critical section: read both halves, compute, write if needed
                             AppendLine($"let {oldLoVar2} = atomicLoad(&(*{ptrStr})[u32({baseIdxVar})]);");
                             AppendLine($"let {oldHiVar2} = atomicLoad(&(*{ptrStr})[u32({baseIdxVar}) + 1u]);");
                             AppendLine($"let _sl_old_{target.Name} = {valWgslType}({oldLoVar2}, {oldHiVar2});");
                             if (value.Kind == global::ILGPU.IR.Values.AtomicKind.Exchange)
                             {
                                 // Exchange: unconditionally write new value
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar})], {val}.x);");
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar}) + 1u], {val}.y);");
                             }
                             else
                             {
                                 // Min/Max: compare and conditionally write
                                 string cmpFn = value.Kind == global::ILGPU.IR.Values.AtomicKind.Max
                                     ? $"{prefix2}_gt" : $"{prefix2}_lt";
                                 AppendLine($"if ({cmpFn}({val}, _sl_old_{target.Name})) {{");
                                 PushIndent();
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar})], {val}.x);");
                                 AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar}) + 1u], {val}.y);");
                                 PopIndent();
                                 AppendLine($"}}");
                             }
                             AppendLine($"{target} = _sl_old_{target.Name};");
                             // Release lock
                             AppendLine($"atomicStore(&{lockName}[{lockIdx}], 0u);");
                             AppendLine($"break;");
                             PopIndent();
                             AppendLine($"}}");
                             PopIndent();
                             AppendLine($"}}");
                         }
                     }
                 }
                 else
                 {
                     // Exchange on i64/f64: uses the same spinlock pattern
                     var lockKey2 = (emulInfo.ParamIndex, emulInfo.FieldIdx);
                     if (!_lockBufferNames.TryGetValue(lockKey2, out var lockName2))
                         throw new System.InvalidOperationException($"No lock buffer for param{emulInfo.ParamIndex} field{emulInfo.FieldIdx} - ScanForAtomicUsage should have detected i64 Exchange");
                     string lockIdx2 = $"u32({baseIdxVar}) / 2u";
                     string oldLoVar3 = $"_sl_old_lo_{target.Name}";
                     string oldHiVar3 = $"_sl_old_hi_{target.Name}";

                     AppendLine($"// i64 Exchange: spinlock on {lockName2}");
                     AppendLine($"loop {{");
                     PushIndent();
                     AppendLine($"let _sl_cas_{target.Name} = atomicCompareExchangeWeak(&{lockName2}[{lockIdx2}], 0u, 1u);");
                     AppendLine($"if (_sl_cas_{target.Name}.exchanged) {{");
                     PushIndent();
                     AppendLine($"let {oldLoVar3} = atomicLoad(&(*{ptrStr})[u32({baseIdxVar})]);");
                     AppendLine($"let {oldHiVar3} = atomicLoad(&(*{ptrStr})[u32({baseIdxVar}) + 1u]);");
                     AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar})], {val}.x);");
                     AppendLine($"atomicStore(&(*{ptrStr})[u32({baseIdxVar}) + 1u], {val}.y);");
                     AppendLine($"{target} = {valWgslType}({oldLoVar3}, {oldHiVar3});");
                     AppendLine($"atomicStore(&{lockName2}[{lockIdx2}], 0u);");
                     AppendLine($"break;");
                     PopIndent();
                     AppendLine($"}}");
                     PopIndent();
                     AppendLine($"}}");
                 }
                 return;
            }
            
            base.GenerateCode(value);
        }



        public override void GenerateCode(BinaryArithmeticValue value)
        {
            var target = Load(value);
            var left = Load(value.Left);
            var right = Load(value.Right);
            string prefix = GetPrefix(value);

            // Check if this is an emulated 64-bit operation
            var leftType = TypeGenerator[value.Left.Type];
            var rightType = TypeGenerator[value.Right.Type];
            bool isEmulatedF64 = Backend.EnableF64Emulation && (leftType == "emu_f64" || rightType == "emu_f64");
            bool isEmulatedI64 = Backend.EnableI64Emulation && (leftType == "emu_i64" || leftType == "emu_u64" || rightType == "emu_i64" || rightType == "emu_u64");
            
            // Fallback: check BasicValueType when emulation options are enabled.
            // TypeGenerator may not return "emu_i64" for IR intermediate values (e.g., reduce accumulators).
            if (!isEmulatedF64 && !isEmulatedI64)
            {
                if (Backend.EnableI64Emulation && value.BasicValueType == BasicValueType.Int64)
                {
                    isEmulatedI64 = true;
                    leftType = value.IsUnsigned ? "emu_u64" : "emu_i64";
                }
                else if (Backend.EnableF64Emulation && value.BasicValueType == BasicValueType.Float64)
                {
                    isEmulatedF64 = true;
                    leftType = "emu_f64";
                }
            }

            if (isEmulatedF64)
            {
                // Use emu_f64 emulation functions
                string? emulFunc = value.Kind switch
                {
                    BinaryArithmeticKind.Add => "f64_add",
                    BinaryArithmeticKind.Sub => "f64_sub",
                    BinaryArithmeticKind.Mul => "f64_mul",
                    BinaryArithmeticKind.Div => "f64_div",
                    BinaryArithmeticKind.Min => "f64_min",
                    BinaryArithmeticKind.Max => "f64_max",
                    _ => null
                };

                if (emulFunc != null)
                {
                    AppendLine($"{prefix}{target} = {emulFunc}({left}, {right});");
                    return;
                }
                // Fall through to standard ops for unsupported kinds
            }

            if (isEmulatedI64)
            {
                // Use emu_i64 emulation functions
                // NOTE: TypeGenerator returns "emu_i64" for BOTH long AND ulong, because ILGPU's
                // BasicValueType enum has no UInt64 member (only Int64). Signedness is tracked
                // by the arithmetic type, so we must also check value.IsUnsigned.
                bool isUnsigned = leftType == "emu_u64" || rightType == "emu_u64" || value.IsUnsigned;
                string? emulFunc = value.Kind switch
                {
                    BinaryArithmeticKind.Add => "i64_add",
                    BinaryArithmeticKind.Sub => "i64_sub",
                    BinaryArithmeticKind.Mul => isUnsigned ? "u64_mul" : "i64_mul",
                    BinaryArithmeticKind.And => "i64_and",
                    BinaryArithmeticKind.Or => "i64_or",
                    BinaryArithmeticKind.Xor => "i64_xor",
                    BinaryArithmeticKind.Shl => "i64_shl",
                    BinaryArithmeticKind.Shr => isUnsigned ? "u64_shr" : "i64_shr",
                    BinaryArithmeticKind.Min => isUnsigned ? "u64_min" : "i64_min",
                    BinaryArithmeticKind.Max => isUnsigned ? "u64_max" : "i64_max",
                    _ => null
                };

                if (emulFunc != null)
                {
                    if (value.Kind == BinaryArithmeticKind.Shl || value.Kind == BinaryArithmeticKind.Shr)
                    {
                        // Shift amount should be u32
                        AppendLine($"{prefix}{target} = {emulFunc}({left}, u32({right}));");
                    }
                    else
                    {
                        AppendLine($"{prefix}{target} = {emulFunc}({left}, {right});");
                    }
                    return;
                }
                // Fall through to standard ops for unsupported kinds
            }

            // Standard (non-emulated) path
            if (value.Kind == BinaryArithmeticKind.PowF)
            {
                // WGSL pow(x, y) is undefined for x < 0 and returns NaN.
                // Runtime-safe guard: always use pow(abs(x), y), then apply sign
                // correction for odd integer exponents. Uses abs(y) % 2.0 instead of
                // y % 2.0 because WGSL % is IEEE remainder (can be negative for neg y)
                // while we need the "is odd" test to work for negative exponents too.
                // Matches the WebGL GLSLCodeGenerator fix (rc.21, mod(y,2.0)>=1 path).
                AppendLine($"{prefix}{target} = pow(abs({left}), {right}) * select(1.0, -1.0, ({left} < 0.0) && ((abs({right}) % 2.0) >= 1.0));");
                return;
            }
            if (value.Kind == BinaryArithmeticKind.Min || value.Kind == BinaryArithmeticKind.Max || value.Kind == BinaryArithmeticKind.Atan2F)
            {
                string func = value.Kind switch
                {
                    BinaryArithmeticKind.Min => "min",
                    BinaryArithmeticKind.Max => "max",
                    BinaryArithmeticKind.Atan2F => "atan2",
                    _ => "min"
                };
                // For unsigned integer min/max, WGSL's min()/max() are type-overloaded:
                // min(i32, i32) is signed, min(u32, u32) is unsigned.
                // When ILGPU flags the operation as unsigned, bitcast operands to u32
                // and the result back to i32.
                if (value.IsUnsigned && value.BasicValueType == BasicValueType.Int32
                    && (value.Kind == BinaryArithmeticKind.Min || value.Kind == BinaryArithmeticKind.Max))
                {
                    AppendLine($"{prefix}{target} = bitcast<i32>({func}(bitcast<u32>({left}), bitcast<u32>({right})));");
                }
                else
                {
                    AppendLine($"{prefix}{target} = {func}({left}, {right});");
                }
                return;
            }

            // BinaryLogF: log(left) / log(right)
            if (value.Kind == BinaryArithmeticKind.BinaryLogF)
            {
                AppendLine($"{prefix}{target} = log({left}) / log({right});");
                return;
            }

            // CopySign: WGSL has no copysign built-in.
            if (value.Kind == BinaryArithmeticKind.CopySignF)
            {
                AppendLine($"{prefix}{target} = select(-abs({left}), abs({left}), {right} >= 0.0);");
                return;
            }

            // When AND/OR/XOR operates on bool (Int1) operands, WGSL returns bool.
            // If the IR result type is Int32, we must convert: select(0, 1, bool_expr)
            if ((value.Kind == BinaryArithmeticKind.And ||
                 value.Kind == BinaryArithmeticKind.Or ||
                 value.Kind == BinaryArithmeticKind.Xor) &&
                value.Left.BasicValueType == BasicValueType.Int1 &&
                value.Right.BasicValueType == BasicValueType.Int1 &&
                value.BasicValueType != BasicValueType.Int1)
            {
                var boolOp = GetArithmeticOp(value.Kind);
                AppendLine($"{prefix}{target} = select(0, 1, {left} {boolOp} {right});");
                return;
            }

            var op = GetArithmeticOp(value.Kind);

            if (value.Kind == BinaryArithmeticKind.Shl || value.Kind == BinaryArithmeticKind.Shr)
            {
                // WGSL `i32 >> u32` is arithmetic (sign-extending) shift; `i32 << u32`
                // pushing into the sign bit can also trigger validator UB. ILGPU stores
                // uint as i32 (BasicValueType has no UInt32). For unsigned shift right,
                // we MUST bitcast through u32 to get logical (zero-fill) shift; otherwise
                // a high-bit-set uint like 0x80000000 shifts to 0xFFFF0000 instead of
                // 0x00010000 — silently wrong.
                //
                // Tuvok's OpusRangeDecoderGpu_DecodeBitLogP_LogP15Mixed surfaced this on
                // 2026-05-04: post-Init Normalize, libopus state.Rng = 0x80000000, then
                // `s = r >> 15` returned 0xFFFF0000, flipping the first-bit decode result.
                //
                // For Shl, always cast through u32 to avoid signed-shift UB on high bits.
                bool isInt32 = value.Left.BasicValueType == BasicValueType.Int32;
                if (isInt32 && (value.IsUnsigned || value.Kind == BinaryArithmeticKind.Shl))
                {
                    AppendLine($"{prefix}{target} = bitcast<i32>(bitcast<u32>({left}) {op} u32({right}));");
                }
                else
                {
                    AppendLine($"{prefix}{target} = {left} {op} u32({right});");
                }
            }
            else if ((value.Kind == BinaryArithmeticKind.Div || value.Kind == BinaryArithmeticKind.Rem)
                && value.IsUnsigned
                && value.Left.BasicValueType == BasicValueType.Int32
                && !leftType.StartsWith("vec") && !rightType.StartsWith("vec"))
            {
                // Same bitcast pattern as Shr. WGSL `i32 / i32` and `i32 % i32` are
                // SIGNED div/rem; for high-bit-set i32-as-uint operands they produce
                // the wrong result. Tuvok's `OpusRangeDecoderGpu_DecodeUint_*` on
                // WebGPU 2026-05-04: post-Init `state.Rng = 0x80000000` divided by
                // `ft = 6u` should give `0x15555555` (unsigned), not `0xEAAAAAAB`
                // (signed two's complement). Bitcast through u32 fixes both Div and
                // Rem for any high-bit-set uint operand.
                AppendLine($"{prefix}{target} = bitcast<i32>(bitcast<u32>({left}) {op} bitcast<u32>({right}));");
            }
            else
                AppendLine($"{prefix}{target} = {left} {op} {right};");
        }


        private void PushPhiValues(BasicBlock targetBlock, BasicBlock sourceBlock)
        {
            foreach (var value in targetBlock)
            {
                if (value.Value is PhiValue phi)
                {
                    var targetVar = Load(phi);
                    Declare(targetVar); // Ensure phi var is declared (Allocate doesn't call Declare)
                    bool matched = false;
                    // PhiValue in ILGPU implements a collection of (SourceBlock, Value) pairs
                    for (int i = 0; i < phi.Count; i++)
                    {
                        if (phi.Sources[i] == sourceBlock)
                        {
                            var sourceVal = Load(phi[i]);
                            AppendLine($"{targetVar} = {sourceVal};");
                            matched = true;
                        }
                    }
                    if (WebGPU.Backend.WebGPUBackend.VerboseLogging && !matched)
                    {
                        var allSources = new List<string>();
                        for (int i = 0; i < phi.Count; i++)
                            allSources.Add($"B{phi.Sources[i].Id}");
                        WebGPUBackend.Log($"[DIAG-PHI] WARNING: No match for phi {targetVar} in B{targetBlock.Id} from source B{sourceBlock.Id}. Phi sources: [{string.Join(", ", allSources)}]");
                    }
                }
            }
        }

        /// <summary>
        /// Pushes PHI values transitively through intermediate blocks that sit between
        /// a loop break point and the final merge/exit block.
        ///
        /// When ILGPU generates IR for a loop body break (e.g., from C# `hitT = t; steps = i; break;`),
        /// it may insert intermediate basic blocks between the break source and the final merge
        /// block. These intermediate blocks have unconditional branches and PHI nodes that
        /// carry the assigned values forward.
        ///
        /// CRITICAL: This method must NOT add blocks to the visited set or emit code for them.
        /// The exit/merge blocks will be emitted by GenerateStructuredCodeRecursive AFTER the
        /// loop construct. If we mark them visited here, the post-loop code (conditionals, stores)
        /// will never be generated, causing the "grey screen" bug.
        /// </summary>
        private void PushPhiValuesTransitive(BasicBlock exitTarget, BasicBlock sourceBlock,
            Loops<ReversePostOrder, Forwards>.Node currentLoop, HashSet<BasicBlock> visited)
        {
            // Push any PHIs in the immediate exit target
            PushPhiValues(exitTarget, sourceBlock);

            // Follow the chain of unconditional branches through intermediate blocks.
            // Only push PHI values — do NOT emit code or mark blocks as visited.
            // The post-loop blocks will be emitted naturally after the loop construct.
            //
            // IMPORTANT: Stop when we reach a block outside the current loop's PARENT.
            // For nested loops, the break clause must NOT push PHI values for ancestor
            // loop headers — those PHIs reference increment variables that haven't been
            // computed yet (they're in code blocks after the child loop). The ancestor
            // PHIs will be pushed by the natural code flow after the loop construct.
            var current = exitTarget;
            int maxDepth = 8; // Safety limit
            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (current.Terminator is global::ILGPU.IR.Values.UnconditionalBranch uBranch)
                {
                    var nextBlock = uBranch.Target;
                    // Stop if nextBlock is outside the current loop — its PHIs belong
                    // to an ancestor loop and will be handled by post-loop code emission.
                    if (!currentLoop.Contains(nextBlock))
                        break;
                    PushPhiValues(nextBlock, current);
                    current = nextBlock;
                }
                else
                {
                    break; // Not an unconditional branch, stop tracing
                }
            }
        }
        public override void GenerateCode(UnaryArithmeticValue value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            string prefix = GetPrefix(value);

            // Emu_f64 source needs different intrinsic codegen: the f32 syntax
            // (e.g. `source != 0.0`) doesn't compile against vec2<f32>/vec4<f32>.
            // IsInfF / IsNaNF route through helpers that read the high f32 lane
            // (which carries IEEE-correct Inf/NaN encoding for both Dekker and
            // Ozaki - see WGSLEmulationLibrary.f64_from_ieee754_bits Inf/NaN
            // branches that preserve sign in the .x lane).
            string sourceType = TypeGenerator[value.Value.Type];
            bool isEmuF64Source = Backend.EnableF64Emulation && sourceType == "emu_f64";
            if (isEmuF64Source)
            {
                string? f64Func = value.Kind switch
                {
                    UnaryArithmeticKind.IsNaNF => $"f64_is_nan({source})",
                    UnaryArithmeticKind.IsInfF => $"f64_is_inf({source})",
                    _ => null
                };
                if (f64Func != null)
                {
                    AppendLine($"{prefix}{target} = {f64Func};");
                    return;
                }
            }

            // Handle math intrinsics that need function calls
            string? funcCall = value.Kind switch
            {
                UnaryArithmeticKind.Abs => $"abs({source})",
                UnaryArithmeticKind.SinF => $"sin({source})",
                UnaryArithmeticKind.CosF => $"cos({source})",
                UnaryArithmeticKind.TanF => $"tan({source})",
                UnaryArithmeticKind.AsinF => $"asin({source})",
                UnaryArithmeticKind.AcosF => $"acos({source})",
                UnaryArithmeticKind.AtanF => $"atan({source})",
                UnaryArithmeticKind.SinhF => $"sinh({source})",
                UnaryArithmeticKind.CoshF => $"cosh({source})",
                UnaryArithmeticKind.TanhF => $"tanh({source})",
                UnaryArithmeticKind.ExpF => $"exp({source})",
                UnaryArithmeticKind.Exp2F => $"exp2({source})",
                UnaryArithmeticKind.LogF => $"log({source})",
                UnaryArithmeticKind.Log2F => $"log2({source})",
                UnaryArithmeticKind.SqrtF => $"sqrt({source})",
                UnaryArithmeticKind.RsqrtF => $"1.0 / sqrt({source})",
                UnaryArithmeticKind.RcpF => $"1.0 / {source}",
                UnaryArithmeticKind.FloorF => $"floor({source})",
                UnaryArithmeticKind.CeilingF => $"ceil({source})",
                UnaryArithmeticKind.Log10F => $"(log({source}) / 2.302585093)",
                // IsNaN on f32: NaN is the only value where x != x
                UnaryArithmeticKind.IsNaNF => sourceType == "emu_f64" ? $"f64_is_nan({source})" : $"({source} != {source})",
                // IsInf on f32: infinity-detect via (x != 0.0 && x == x * 2.0)
                // - x != 0.0 excludes 0
                // - x == x * 2.0 only +/-Inf satisfies (any finite x doubles)
                // - x == x excludes NaN (since NaN != NaN)
                UnaryArithmeticKind.IsInfF => sourceType == "emu_f64" ? $"f64_is_inf({source})" : $"({source} != 0.0 && {source} == {source} * 2.0 && {source} == {source})",
                _ => null
            };

            if (funcCall != null)
            {
                AppendLine($"{prefix}{target} = {funcCall};");
                return;
            }

            // Handle emulated emu_i64/emu_u64 negation (sourceType already
            // computed at top of method for the IsInf/IsNaN emu_f64 routing)
            if (Backend.EnableI64Emulation && (sourceType == "emu_i64" || sourceType == "emu_u64") && value.Kind == UnaryArithmeticKind.Neg)
            {
                AppendLine($"{prefix}{target} = i64_neg({source});");
                return;
            }

            // Handle simple unary operators
            string op = value.Kind switch
            {
                UnaryArithmeticKind.Neg => "-",
                UnaryArithmeticKind.Not => TypeGenerator[value.Value.Type] == "bool" ? "!" : "~",
                _ => ""
            };

            if (!string.IsNullOrEmpty(op))
            {
                AppendLine($"{prefix}{target} = {op}({source});");
            }
            else
            {
                // Fallback for unsupported operations
                AppendLine($"// [WGSL] Unhandled UnaryArithmeticKind: {value.Kind}");
                AppendLine($"{prefix}{target} = {source};");
            }
        }
        public override void GenerateCode(UnconditionalBranch branch)
        {
            // 1. Move Phi data (v_11 = v_19, etc.)
            PushPhiValues(branch.Target, branch.BasicBlock);

            // 2. Force the State Machine transition
            var targetIndex = GetBlockIndex(branch.Target);
            AppendIndent();
            Builder.AppendLine($"current_block = {targetIndex};");

            // Must emit control flow to reach the state machine loop header.
            // Without this, code inside nested loops (for/while) falls through
            // instead of transitioning to the target block. This was the root
            // cause of 'return' inside for-loops not exiting the kernel.
            //
            // For barrier kernels: use 'continue' to reach the barrier at the
            // loop header (uniform control flow requirement).
            // For non-barrier kernels: 'continue' is also correct and safe.
            AppendIndent();
            Builder.AppendLine("continue;");
        }
        private new void GenerateCodeInternal()
        {
            // Optimization for single-block (Linear)
            if (Method.Blocks.Count == 1)
            {
                GenerateBlockCode(Method.Blocks.First());
                return;
            }

            // Structured Control Flow (Acyclic Multi-block)
            if (_loops.Count == 0)
            {
                _visitedBlocks.Clear();
                GenerateStructuredCode(Method.EntryBlock, null);
                return;
            }

            // Fallback: State Machine (Cyclic)
            // Note: Barriers inside loops will likely fail validation or execution uniformity quirks
            foreach (var block in Method.Blocks)
            {
                AppendIndent();
                Builder.AppendLine($"case {GetBlockIndex(block)}: {{");
                PushIndent();

                GenerateBlockCode(block);

                PopIndent();
                AppendIndent();
                Builder.AppendLine("}");
            }
        }

        private HashSet<BasicBlock> _visitedBlocks = new HashSet<BasicBlock>();

        private void GenerateBlockCode(BasicBlock block)
        {
            foreach (var value in block)
            {
                if (value.Value is TerminatorValue) continue;

                this.GenerateCodeFor(value.Value);
            }

            // Handle terminator explicitly ONLY if we are in state machine or single block
            // For structured code, the recursion handles branching logic, but we might need to emit 'return'
            if (!(_loops.Count == 0 && Method.Blocks.Count > 1))
            {
                // State Machine / Single Block
                if (block.Terminator is UnconditionalBranch ub) GenerateCode(ub);
                else if (block.Terminator is IfBranch ib) GenerateCode(ib);
                else if (block.Terminator is SwitchBranch sb) GenerateCode(sb); // Add Switch support
                else if (block.Terminator is ReturnTerminator rt2) GenerateCode(rt2);
                else if (block.Terminator != null) this.GenerateCodeFor(block.Terminator);
            }
        }

        private void GenerateStructuredCode(BasicBlock current, BasicBlock? stop)
        {
            if (current == null || current == stop || _visitedBlocks.Contains(current)) return;
            _visitedBlocks.Add(current);

            // 1. Emit instructions (excluding terminator)
            GenerateBlockCode(current);

            // 2. Handle Control Flow
            var terminator = current.Terminator;

            if (terminator is UnconditionalBranch ub)
            {
                // Linear flow -> Recurse to target
                PushPhiValues(ub.Target, current);
                GenerateStructuredCode(ub.Target, stop);
            }
            else if (terminator is IfBranch ib)
            {
                // Divergence point
                var trueTarget = ib.TrueTarget;
                var falseTarget = ib.FalseTarget;
                var merge = _postDominators.GetImmediateDominator(current);

                // Resolve Phi nodes for outgoing branches
                PushPhiValues(trueTarget, current);

                // SHORT-CIRCUIT FIX: Save visited state before true branch so that
                // blocks visited during the true path can be re-visited by the false
                // path. This is essential for || short-circuit patterns where both
                // branches converge on a shared body block (e.g. if (a || b) { body }).
                var visitedBeforeTrueBranch = new HashSet<BasicBlock>(_visitedBlocks);

                var cond = Load(ib.Condition);
                AppendLine($"if ({cond}) {{");
                PushIndent();
                GenerateStructuredCode(trueTarget, merge);
                PopIndent();
                AppendLine("} else {");
                PushIndent();
                PushPhiValues(falseTarget, current);
                // Restore visited state: remove blocks that were only visited during
                // the true branch, so the false branch can reach shared targets.
                _visitedBlocks.IntersectWith(visitedBeforeTrueBranch);
                GenerateStructuredCode(falseTarget, merge);
                PopIndent();
                AppendLine("}");

                // Continue from merge point
                if (merge != null && merge != stop)
                {
                    GenerateStructuredCode(merge, stop);
                }
            }
            else if (terminator is SwitchBranch sb)
            {
                // Switch support for structured flow
                var selector = Load(sb.Condition);
                AppendLine($"switch ({selector}) {{");

                var merge = _postDominators.GetImmediateDominator(current);

                for (int i = 0; i < sb.NumCasesWithoutDefault; i++)
                {
                    AppendLine($"case {i}: {{");
                    PushIndent();
                    PushPhiValues(sb.GetCaseTarget(i), current);
                    GenerateStructuredCode(sb.GetCaseTarget(i), merge);
                    AppendLine("break;");
                    PopIndent();
                    AppendLine("}");
                }

                AppendLine("default: {");
                PushIndent();
                PushPhiValues(sb.DefaultBlock, current);
                GenerateStructuredCode(sb.DefaultBlock, merge);
                AppendLine("break;");
                PopIndent();
                AppendLine("}");
                AppendLine("}");

                if (merge != null && merge != stop)
                    GenerateStructuredCode(merge, stop);
            }
            else if (terminator is ReturnTerminator rt)
            {
                // Already handled in GenerateBlockCode for the return statement itself
            }
        }
        public override void GenerateCode(IfBranch branch)
        {
            var condition = Load(branch.Condition);
            var trueIndex = GetBlockIndex(branch.TrueTarget);
            var falseIndex = GetBlockIndex(branch.FalseTarget);

            AppendIndent();
            Builder.AppendLine($"if ({condition}) {{");
            PushIndent();
            PushPhiValues(branch.TrueTarget, branch.BasicBlock);
            AppendIndent();
            Builder.AppendLine($"current_block = {trueIndex};");
            PopIndent();

            AppendIndent();
            Builder.AppendLine("} else {");
            PushIndent();
            PushPhiValues(branch.FalseTarget, branch.BasicBlock);
            AppendIndent();
            Builder.AppendLine($"current_block = {falseIndex};");
            PopIndent();

            AppendIndent();
            Builder.AppendLine("}");
            // Note: NO continue; here — same rationale as UnconditionalBranch.
            // Control flows to after the switch where barriers are placed.
        }
        // 3. Handles 'return' to exit the kernel
        public override void GenerateCode(ReturnTerminator returnTerminator)
        {
            // For kernels WITH barriers, we can't use 'return;' directly because it
            // would skip the workgroupBarrier() after the switch, violating WGSL's
            // uniform control flow. Use current_block = -1 + continue to jump back
            // to the loop header where the exit check handles termination.
            //
            // For kernels WITHOUT barriers (the common case), 'return;' is safe and
            // correct. The previous approach of just setting current_block = -1
            // without break/continue/return caused execution to fall through to
            // subsequent code in the same block (e.g., return inside a for loop
            // would continue past the loop to code that should be unreachable).
            bool hasBarriers = _usesBarriers;

            if (hasBarriers && _loops.Count > 0 && Method.Blocks.Count > 1)
            {
                // Barrier kernel: signal exit, continue to loop header for barrier-safe exit
                AppendLine("current_block = -1;");
                AppendLine("continue;");
            }
            else
            {
                // No barriers or single block: direct return is safe
                AppendLine("return;");
            }
        }

        public override void GenerateCode(CompareValue value)
        {
            var target = Load(value);
            var left = Load(value.Left);
            var right = Load(value.Right);
            var op = GetCompareOp(value.Kind);

            string prefix = GetPrefix(value);

            // Fix: Handle Vector vs Scalar comparison (e.g. vec2 >= i32)
            string leftType = TypeGenerator[value.Left.Type];
            string rightType = TypeGenerator[value.Right.Type];

            // Check for emulated 64-bit compare
            bool isEmulatedF64 = Backend.EnableF64Emulation && (leftType == "emu_f64" || rightType == "emu_f64");
            bool isEmulatedI64 = Backend.EnableI64Emulation && (leftType == "emu_i64" || leftType == "emu_u64" || rightType == "emu_i64" || rightType == "emu_u64");

            if (isEmulatedF64)
            {
                string? emulFunc = value.Kind switch
                {
                    CompareKind.LessThan => "f64_lt",
                    CompareKind.LessEqual => "f64_le",
                    CompareKind.GreaterThan => "f64_gt",
                    CompareKind.GreaterEqual => "f64_ge",
                    CompareKind.Equal => "f64_eq",
                    CompareKind.NotEqual => "f64_ne",
                    _ => null
                };

                if (emulFunc != null)
                {
                    // IEEE 754 unordered compare: NaN operand forces result TRUE
                    // (except NotEqual which is already TRUE for NaN under ordered).
                    // ILGPU's IR negation pass rewrites `clt + brfalse` as
                    // `cge + brtrue` and toggles UnsignedOrUnordered to keep the
                    // semantics. Backends that ignore the flag set bits for NaN
                    // (DoubleNaNComparisonTest 2026-04-29).
                    if (value.IsUnsignedOrUnordered && value.Kind != CompareKind.NotEqual)
                    {
                        AppendLine(
                            $"{prefix}{target} = (_f32_is_nan_bits({left}.x) || _f32_is_nan_bits({right}.x)) || {emulFunc}({left}, {right});");
                    }
                    else
                    {
                        AppendLine($"{prefix}{target} = {emulFunc}({left}, {right});");
                    }
                    return;
                }
            }

            if (isEmulatedI64)
            {
                // NOTE: TypeGenerator returns "emu_i64" for BOTH long AND ulong, because ILGPU's
                // BasicValueType enum has no UInt64 member. Must also check value.IsUnsignedOrUnordered.
                bool isUnsigned = leftType == "emu_u64" || rightType == "emu_u64" || value.IsUnsignedOrUnordered;
                string? emulFunc = value.Kind switch
                {
                    CompareKind.LessThan => isUnsigned ? "u64_lt" : "i64_lt",
                    CompareKind.LessEqual => isUnsigned ? "u64_le" : "i64_le",
                    CompareKind.GreaterThan => isUnsigned ? "u64_gt" : "i64_gt",
                    CompareKind.GreaterEqual => isUnsigned ? "u64_ge" : "i64_ge",
                    CompareKind.Equal => "i64_eq",
                    CompareKind.NotEqual => "i64_ne",
                    _ => null
                };

                if (emulFunc != null)
                {
                    AppendLine($"{prefix}{target} = {emulFunc}({left}, {right});");
                    return;
                }
            }

            // Standard comparison:
            bool leftIsVec = leftType.StartsWith("vec");
            bool rightIsVec = rightType.StartsWith("vec");

            // For unsigned integer comparisons, WGSL operators are type-sensitive:
            // i32 < i32 is signed, u32 < u32 is unsigned.
            // When ILGPU flags the comparison as unsigned, bitcast operands to u32.
            bool needsUnsignedCast = value.IsUnsignedOrUnordered
                && value.Left.BasicValueType == BasicValueType.Int32
                && !leftIsVec && !rightIsVec
                && value.Kind != CompareKind.Equal
                && value.Kind != CompareKind.NotEqual;

            bool isFloatScalar = (value.Left.BasicValueType == BasicValueType.Float32
                    || value.Left.BasicValueType == BasicValueType.Float16)
                && !leftIsVec && !rightIsVec;

            // Three cases for f32 NaN safety:
            //   1. Unordered LT/LE/GT/GE: emit `is_nan(l) || is_nan(r) || (l op r)`
            //      to force TRUE for NaN. Required because ILGPU's IR negates
            //      `clt+brfalse` to `cge+brtrue [Unordered]`.
            //   2. Equal/NotEqual on f32 (ordered or unordered): Naga / Chrome
            //      WGSL backend has a long-standing bug where bit-identical NaN
            //      operands compare equal. Always apply explicit NaN guard for
            //      IEEE-strict semantics. Equal returns FALSE for NaN, NotEqual
            //      returns TRUE.
            // Diagnosed against FloatNaNComparisonTest 2026-04-29.
            bool isNativeFloatUnordered = isFloatScalar && value.IsUnsignedOrUnordered
                && value.Kind != CompareKind.NotEqual
                && value.Kind != CompareKind.Equal;
            bool isNativeFloatEqualLike = isFloatScalar
                && (value.Kind == CompareKind.Equal || value.Kind == CompareKind.NotEqual);

            if (needsUnsignedCast)
            {
                AppendLine($"{prefix}{target} = bitcast<u32>({left}) {op} bitcast<u32>({right});");
            }
            else if (isNativeFloatUnordered)
            {
                // IEEE 754 NaN bit pattern: exponent all 1s AND mantissa nonzero.
                // Bit-pattern detect (`val != val` may be optimised away by the
                // shader compiler; bitcast survives optimisation).
                string LIsNaN = WGSLCodeGenerator.WgslIsNaNExprPublic($"{left}", leftType);
                string RIsNaN = WGSLCodeGenerator.WgslIsNaNExprPublic($"{right}", rightType);
                AppendLine($"{prefix}{target} = ({LIsNaN} || {RIsNaN} || ({left} {op} {right}));");
            }
            else if (isNativeFloatEqualLike)
            {
                // Equal: IEEE result is FALSE for NaN regardless of ordered or
                // unordered. NotEqual: IEEE result is TRUE for NaN regardless.
                // Naga compares NaN bit patterns directly so explicit NaN guard
                // is required for both flag states.
                string LIsNaN = WGSLCodeGenerator.WgslIsNaNExprPublic($"{left}", leftType);
                string RIsNaN = WGSLCodeGenerator.WgslIsNaNExprPublic($"{right}", rightType);
                if (value.Kind == CompareKind.Equal)
                    AppendLine($"{prefix}{target} = (!({LIsNaN}) && !({RIsNaN}) && ({left} == {right}));");
                else // NotEqual
                    AppendLine($"{prefix}{target} = ({LIsNaN} || {RIsNaN} || ({left} != {right}));");
            }
            else if (leftIsVec && !rightIsVec)
            {
                // vec op scalar -> all(vec op vec(scalar))
                // Use vector type of the vector operand to splat the scalar
                AppendLine($"{prefix}{target} = all({left} {op} {leftType}({right}));");
            }
            else if (!leftIsVec && rightIsVec)
            {
                // scalar op vec -> all(vec(scalar) op vec)
                AppendLine($"{prefix}{target} = all({rightType}({left}) {op} {right});");
            }
            else
            {
                AppendLine($"{prefix}{target} = {left} {op} {right};");
            }
        }
        public override void GenerateCode(global::ILGPU.IR.Values.PointerCast value)
        {
            var src = Load(value.Value);
            if (_boolSharedPtrs.Contains(src.Name))
            {
                // Alias the cast result directly to the source pointer rather than emitting
                // the base `v_target = v_source; // ptrCast`. WGSL pointers are not
                // var-declarable (Declare() skips ptr types), so that assignment targets an
                // undeclared value ("unresolved value 'v_N'") and the shader fails to compile.
                // Aliasing avoids the bogus declaration AND carries the bool-remapped-shared
                // tag straight through to the eventual Load/Store (which convert i32 0/1 ↔ bool).
                valueVariables[value] = src;
                return;
            }
            base.GenerateCode(value);
        }

        public override void GenerateCode(AddressSpaceCast value)
        {
            // When the source is a kernel-side local alloca, the kernel emits
            // it as `var v_X : i32;` (a function-scope value, not a pointer).
            // To produce a real WGSL pointer for the cast target — needed when
            // the cast feeds a fn-def call expecting `ptr<function, T>` — we
            // must take the address with `&v_X`.
            //
            // Earlier shape was `let v_target = &v_source;` (one extra `let`
            // per cast). For Tuvok's iDCT 16x16 with 32 helper calls × 16
            // ref-int outputs = 512 addrSpaceCast emissions, this added ~50KB
            // of WGSL and pushed Tint validator past the 30s timeout for
            // ZeroCoefficients. Now: alias the cast result directly to the
            // expression `&v_source` and let the call emit pick it up inline.
            // No separate `let` line per cast → 50KB saved per kernel.
            var source = Load(value.Value);
            // Bool-remapped shared pointer: alias the cast result to the source pointer (same
            // reasoning as the PointerCast override) so we neither emit a bogus undeclared-pointer
            // assignment nor lose the bool-remapped-shared tag the Load/Store rely on.
            if (_boolSharedPtrs.Contains(source.Name))
            {
                valueVariables[value] = source;
                return;
            }
            if (value.Type is AddressSpaceType
                && _localAllocaVarNames.Contains(source.Name))
            {
                var ptrType = TypeGenerator[value.Type];
                if (ptrType.StartsWith("ptr<"))
                {
                    valueVariables[value] = new Variable($"&{source}", ptrType);
                    return;
                }
            }
            // Fall through to base for non-alloca / non-ptr cases.
            base.GenerateCode(value);
        }

        public override void GenerateCode(ConvertValue value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            var targetType = TypeGenerator[value.Type];

            string prefix = GetPrefix(value);

            // Fix: Handle Vector to Scalar conversion (e.g. i32(vec2)) which WGSL forbids
            var sourceType = TypeGenerator[value.Value.Type];

            bool isVectorSource = sourceType.StartsWith("vec");
            bool isScalarTarget = !targetType.StartsWith("vec") && !targetType.StartsWith("mat") && !targetType.StartsWith("array")
                                  && targetType != "emu_f64" && targetType != "emu_i64" && targetType != "emu_u64";

            // Detect unsigned source conversion (e.g. uint → float)
            bool isSourceUnsigned = (value.Flags & ConvertFlags.SourceUnsigned) == ConvertFlags.SourceUnsigned;

            // Emulated type detection
            bool isEmulatedF64Target = Backend.EnableF64Emulation && targetType == "emu_f64";
            bool isEmulatedI64Target = Backend.EnableI64Emulation && (targetType == "emu_i64" || targetType == "emu_u64");
            bool isEmulatedF64Source = Backend.EnableF64Emulation && sourceType == "emu_f64";
            bool isEmulatedI64Source = Backend.EnableI64Emulation && (sourceType == "emu_i64" || sourceType == "emu_u64");

            // ---- TARGET is emulated emu_f64 ----
            if (isEmulatedF64Target)
            {
                if (isEmulatedF64Source)
                {
                    // emu_f64 → emu_f64: just assign
                    AppendLine($"{prefix}{target} = {source};");
                }
                else if (isEmulatedI64Source)
                {
                    // emu_i64 → emu_f64: extract i32 then convert through f32
                    AppendLine($"{prefix}{target} = f64_from_f32(f32(i64_to_i32({source})));");
                }
                else if (isVectorSource)
                {
                    // vec → emu_f64: extract .x component
                    AppendLine($"{prefix}{target} = f64_from_f32(f32({source}.x));");
                }
                else if (sourceType == "f32")
                {
                    AppendLine($"{prefix}{target} = f64_from_f32({source});");
                }
                else if (isSourceUnsigned && sourceType == "i32")
                {
                    // unsigned int → emu_f64: bitcast to u32 first to preserve unsigned value
                    AppendLine($"{prefix}{target} = f64_from_f32(f32(bitcast<u32>({source})));");
                }
                else
                {
                    // Integer or other scalar → emu_f64: convert to f32 first
                    AppendLine($"{prefix}{target} = f64_from_f32(f32({source}));");
                }
                return;
            }

            // ---- TARGET is emulated emu_i64/emu_u64 ----
            if (isEmulatedI64Target)
            {
                if (isEmulatedI64Source)
                {
                    // emu_i64 → emu_i64 (or emu_u64 → emu_u64): just assign
                    AppendLine($"{prefix}{target} = {source};");
                }
                else if (isEmulatedF64Source)
                {
                    // emu_f64 → emu_i64: extract f32, cast to i32, then widen
                    AppendLine($"{prefix}{target} = i64_from_i32(i32(f64_to_f32({source})));");
                }
                else if (isVectorSource)
                {
                    // Generic vec → emu_i64: extract .x, cast to i32, then widen
                    if (targetType == "emu_u64")
                        AppendLine($"{prefix}{target} = u64_from_u32(u32({source}.x));");
                    else
                        AppendLine($"{prefix}{target} = i64_from_i32(i32({source}.x));");
                }
                else if (sourceType == "i32")
                {
                    AppendLine($"{prefix}{target} = i64_from_i32({source});");
                }
                else if (sourceType == "u32")
                {
                    if (targetType == "emu_u64")
                        AppendLine($"{prefix}{target} = u64_from_u32({source});");
                    else
                        AppendLine($"{prefix}{target} = i64_from_i32(i32({source}));");
                }
                else if (sourceType == "f32")
                {
                    // f32 → emu_i64: cast to i32 first
                    AppendLine($"{prefix}{target} = i64_from_i32(i32({source}));");
                }
                else
                {
                    // Other scalar → emu_i64: cast to i32 first
                    AppendLine($"{prefix}{target} = i64_from_i32(i32({source}));");
                }
                return;
            }

            // ---- SOURCE is emulated emu_f64, target is scalar ----
            if (isEmulatedF64Source && isScalarTarget)
            {
                if (targetType == "f32")
                {
                    AppendLine($"{prefix}{target} = f64_to_f32({source});");
                }
                else
                {
                    // emu_f64 → i32/u32/etc: extract to f32, then cast
                    AppendLine($"{prefix}{target} = {targetType}(f64_to_f32({source}));");
                }
                return;
            }

            // ---- SOURCE is emulated emu_i64/emu_u64, target is scalar ----
            if (isEmulatedI64Source && isScalarTarget)
            {
                if (targetType == "i32")
                {
                    AppendLine($"{prefix}{target} = i64_to_i32({source});");
                }
                else if (targetType == "u32")
                {
                    AppendLine($"{prefix}{target} = u64_to_u32({source});");
                }
                else if (targetType == "f32")
                {
                    // emu_i64 → f32: extract low word as i32, then cast to f32
                    AppendLine($"{prefix}{target} = f32(i64_to_i32({source}));");
                }
                else
                {
                    // emu_i64 → other scalar: extract low word and cast
                    AppendLine($"{prefix}{target} = {targetType}(i64_to_i32({source}));");
                }
                return;
            }

            // ---- Generic vector → scalar (non-emulated) ----
            if (isVectorSource && isScalarTarget)
            {
                AppendLine($"{prefix}{target} = {targetType}({source}.x);");
                return;
            }

            // ---- Unsigned int → float: must bitcast to u32 first to preserve unsigned value ----
            if (isSourceUnsigned && sourceType == "i32" && targetType == "f32")
            {
                AppendLine($"{prefix}{target} = f32(bitcast<u32>({source}));");
                return;
            }

            // ---- Standard scalar → scalar ----
            // Sub-word narrowing for Int16 / Int8 targets baked into the
            // cast expression: WGSL has no native i16/i8, so `i32(int_val)`
            // is identity. Without explicit narrowing, `(short)((x + (1<<13)) >> 14)`
            // butterfly patterns leave high bits intact (Tuvok's iDCT 16x16
            // residual). Combine into one expression because `let v_X = ...`
            // is immutable. Mirrors the base WGSL handler.
            string castExpr = $"{targetType}({source})";
            if (targetType == "i32")
            {
                bool isTargetUnsigned = (value.Flags & ConvertFlags.TargetUnsigned) == ConvertFlags.TargetUnsigned;
                var dstBasicType = value.Type.BasicValueType;
                // WGSL `extractBits` built-in: signed extract sign-extends.
                // Single intrinsic call vs shift chain - smaller WGSL, faster
                // validator. See base WGSLCodeGenerator handler for details.
                if (dstBasicType == BasicValueType.Int16)
                    castExpr = isTargetUnsigned ? $"({castExpr} & 0xFFFFi)" : $"extractBits({castExpr}, 0u, 16u)";
                else if (dstBasicType == BasicValueType.Int8)
                    castExpr = isTargetUnsigned ? $"({castExpr} & 0xFFi)" : $"extractBits({castExpr}, 0u, 8u)";
                else
                {
                    // Widening from a SUB-WORD source (Int16/Int8) to a wider int (i32). The source
                    // lives in an i32 but may be zero-extended - it came from an unsigned sub-word
                    // load, or from a `(short)`/`(sbyte)` signedness reinterpret that the core IR
                    // ELIDES (short and ushort share BasicValueType.Int16). C# sign-extends
                    // short->int and zero-extends ushort->int; SourceUnsigned carries which. Re-
                    // extend the low bits so the high bits are correct. Concretely PopArithmeticArgs
                    // promotes the `(short)` operand of `(short)Interop.FloatAsInt(half) >> 15`
                    // (AscendingHalf's ones-complement mask) to i32 via THIS convert; without the
                    // re-extension `>> 15` saw a zero-extended value and returned 0 instead of
                    // 0xFFFF for negative Halves. extractBits on a signed i32 sign-extends.
                    // Idempotent for already-extended values; desktop backends never reach here.
                    bool isWidenSrcUnsigned = (value.Flags & ConvertFlags.SourceUnsigned) == ConvertFlags.SourceUnsigned;
                    var srcBasicType = value.Value.BasicValueType;
                    if (srcBasicType == BasicValueType.Int16)
                        castExpr = isWidenSrcUnsigned ? $"({castExpr} & 0xFFFFi)" : $"extractBits({castExpr}, 0u, 16u)";
                    else if (srcBasicType == BasicValueType.Int8)
                        castExpr = isWidenSrcUnsigned ? $"({castExpr} & 0xFFi)" : $"extractBits({castExpr}, 0u, 8u)";
                }
            }
            AppendLine($"{prefix}{target} = {castExpr};");
        }

        public override void GenerateCode(global::ILGPU.IR.Values.GetField value)
        {
            // 1. Safety check: If this is a kernel index component (X, Y, Z) 
            // already hoisted in SetupIndexVariables, skip it.
            if (_hoistedIndexFields.Contains(value)) return;

            // 1b. Body struct field access: redirect to the per-field variable.
            // When a body struct parameter (e.g. ReductionImplementation) is accessed,
            // we redirect GetField to the individual field variable created in SetupParameterBindings.
            if (value.ObjectValue.Resolve() is global::ILGPU.IR.Values.Parameter bodyParam
                && _bodyStructParams.ContainsKey(bodyParam.Index))
            {
                int fieldIdx = value.FieldSpan.Index;
                if (_bodyStructFieldVars.TryGetValue((bodyParam.Index, fieldIdx), out var fieldVarName)
                    && fieldIdx < _bodyStructParams[bodyParam.Index].Count)
                {
                    var fieldInfo = _bodyStructParams[bodyParam.Index][fieldIdx];

                    if (fieldInfo.IsView)
                    {
                        // View field: the fieldVarName is a pointer (&param1_f0).
                        // WGSL does NOT support assigning pointers to 'var' variables — only 'let' bindings.
                        // If this GetField result was hoisted (declared as 'var v_28 : i32'), we CANNOT
                        // assign a pointer to it. This would cause a WGSL validation error.
                        // Instead, skip the assignment — the 'var v_28' stays at 0 (unused placeholder).
                        // The actual buffer access is handled by LoadElementAddress, which detects
                        // body struct view field accesses through the IR chain and uses param{N}_f{M}[idx]
                        // directly, without needing the v_28 intermediate variable.
                        if (!_hoistedPrimitives.Contains(value))
                        {
                            var target = Load(value);
                            AppendLine($"let {target} = {fieldVarName};");
                        }
                        // If hoisted: skip the assignment to avoid pointer→i32 type error.
                        return;
                    }
                    else
                    {
                        // Scalar or metadata field: the fieldVarName is a value (i32, etc.).
                        // Safe to assign to a hoisted var.
                        var target = Load(value);
                        string prefix = _hoistedPrimitives.Contains(value) ? "" : "let ";
                        AppendLine($"{prefix}{target} = {fieldVarName};");
                        return;
                    }
                }
            }

            // 2. Identify if the object being accessed is DIRECTLY a Parameter (likely an ArrayView)
            // CRITICAL: Use Resolve() NOT ResolveToParameter() here. ResolveToParameter walks
            // through Load→LEA→GetField chains and would resolve a struct loaded from a
            // decomposed view buffer back to the original parameter. That causes
            // GetField(loadedStruct, 0) to emit &param0[0] instead of v_173.field_0.
            if (value.ObjectValue.Resolve() is global::ILGPU.IR.Values.Parameter param)
            {
                int paramOffset = KernelParamOffset;
                if (param.Index >= paramOffset)
                {
                    IsMultiDim(param.ParameterType, out var isMultiDim, out var isView, out var is1DView, out var is2DView, out var is3DView);

                    if (isView)
                    {
                        var target = Load(value);
                        var rawType = UnwrapType(param.ParameterType);

                        // ROBUST TYPE DETECTION
                        string typeName = param.ParameterType.ToString();

                        if (rawType is global::ILGPU.IR.Types.StructureType st)
                        {


                            // Field 0 is always the actual pointer to the data.
                            // Direct-param coalesce member (NON-sub-word): redirect to the shared
                            // binding + per-member offset slot. Field-0 access on a coalesce member
                            // returns the pointer to THIS member's data within the shared array.
                            // Sub-word coalesce members fall through to the default emit (which
                            // would reference an undeclared `param{N}` and surface as a clear
                            // WGSL compile error if a kernel ever tries to take field-0 of a
                            // sub-word coalesce member — sub-word access patterns go through LEA,
                            // not field-0, so this should not trip in practice).
                            if (value.FieldSpan.Index == 0)
                            {
                                if (_directParamCoalesceMembership.TryGetValue(param.Index, out var dpFld0Group)
                                    && !dpFld0Group.IsSubWord)
                                {
                                    int dpFld0VoSlot = _viewOffsetScalarSlots[param.Index];
                                    _bodyReferencesScalarParams = true;
                                    AppendLine($"let {target} = &{dpFld0Group.BindingName}[i32(_scalar_params[{dpFld0VoSlot}])];");
                                    return;
                                }
                                AppendLine($"let {target} = &param{param.Index}[0];");
                                return;
                            }

                            // Metadata handling (Length and Strides)
                            if (isMultiDim && rawType is StructureType st1)
                            {
                                // Direct-param coalesce member: arrayLength on the SHARED binding
                                // returns the sum of all members' u32 counts (wrong for view.Length
                                // queries on a coalesce member, but compiles cleanly). v1 doesn't
                                // allocate a per-member count slot; tracked for v2.
                                string totalLen = $"i32(arrayLength(&{(_directParamCoalesceMembership.TryGetValue(param.Index, out var dpLenMdGroup) ? dpLenMdGroup.BindingName : $"param{param.Index}")}))";
                                // For packed struct params, arrayLength() returns u32 count — divide by stride
                                if (_packedStructLayouts.TryGetValue(param.Index, out var psLenLayout2))
                                {
                                    int psStride2 = psLenLayout2.Sum(f => f.U32Count);
                                    totalLen = $"({totalLen} / {psStride2})";
                                }
                                // For sub-word params, arrayLength() returns atomic<u32> count
                                if (_subWordParams.TryGetValue(param.Index, out var swMdElemSize))
                                {
                                    int elemsPerWord2 = 4 / swMdElemSize;
                                    totalLen = $"({totalLen} * {elemsPerWord2})";
                                }

                                // Check hoisting to prevent shadowing
                                string prefix = _hoistedPrimitives.Contains(value) ? "" : "let ";

                                // Helper: wrap i32 expression with i64_from_i32 when target expects emu_i64
                                var targetWgslType = TypeGenerator[value.Type];
                                bool needsI64Wrap = Backend.EnableI64Emulation &&
                                    (targetWgslType == "emu_i64" || targetWgslType == "emu_u64");
                                string WrapI32(string expr) => needsI64Wrap ? $"i64_from_i32({expr})" : expr;

                                if (is3DView)
                                {
                                    var width = $"param{param.Index}_stride[0]";
                                    var height = $"param{param.Index}_stride[1]";
                                    var depth = $"param{param.Index}_stride[2]";

                                    // Flattened Access:
                                    // 1: Width
                                    // 2: Height
                                    // 3: Depth
                                    // 4: StrideY (Width)
                                    // 5: StrideZ (Width*Height)

                                    switch (value.FieldSpan.Index)
                                    {
                                        case 1: AppendLine($"{prefix}{target} = {WrapI32(width)};"); return;
                                        case 2: AppendLine($"{prefix}{target} = {WrapI32(height)};"); return;
                                        case 3: AppendLine($"{prefix}{target} = {WrapI32(depth)};"); return;
                                        case 4: AppendLine($"{prefix}{target} = {WrapI32(width)};"); return;
                                        case 5: AppendLine($"{prefix}{target} = {WrapI32($"{width} * {height}")};"); return;
                                    }
                                }
                                else if (is2DView)
                                {
                                    var width = $"param{param.Index}_stride[0]";
                                    var height = $"param{param.Index}_stride[1]";

                                    // Flattened Access:
                                    // 1: Width
                                    // 2: Height
                                    // 3: StrideY (Width)

                                    switch (value.FieldSpan.Index)
                                    {
                                        case 1: AppendLine($"{prefix}{target} = {WrapI32(width)};"); return;
                                        case 2: AppendLine($"{prefix}{target} = {WrapI32(height)};"); return;
                                        case 3: AppendLine($"{prefix}{target} = {WrapI32(width)};"); return;
                                    }
                                }
                                else if (is1DView)
                                {
                                    // For 1D, we verify if mapped to standard structure
                                    // 1D ArrayView is (View, Index, Length) or (View, Length)?
                                    // If base View, it has (Buffer, Index, Length).
                                    // Usually accessed: Field 2 (Length).

                                    switch (value.FieldSpan.Index)
                                    {
                                        case 1:
                                        {
                                            // Index/Offset: read from packed scalars if a view offset slot exists.
                                            // The runtime packs the element offset (contiguous.Index) at this slot
                                            // so sub-views correctly offset array accesses.
                                            if (_viewOffsetScalarSlots.TryGetValue(param.Index, out int viewOffSlot))
                                            {
                                                _bodyReferencesScalarParams = true;
                                                AppendLine($"{prefix}{target} = i32(_scalar_params[{viewOffSlot}]);");
                                            }
                                            else
                                                AppendLine($"{prefix}{target} = {WrapI32("0")};");
                                            return;
                                        }
                                        case 2: AppendLine($"{prefix}{target} = {WrapI32(totalLen)};"); return; // Length
                                    }
                                }

                                // Fallback for unknown length access
                                AppendLine($"{prefix}{target} = {WrapI32(totalLen)};");
                                return;
                            }
                        }
                    }
                }
            }

            // 3. Special handling for Kernel Index Parameter (X, Y components)
            if (ResolveToParameter(value.ObjectValue) is global::ILGPU.IR.Values.Parameter kernelParam)
            {
                int paramOffset = KernelParamOffset;
                if (kernelParam.Index < paramOffset)
                {
                    var target = Load(value);
                    var source = Load(value.ObjectValue);
                    string prefix = _hoistedPrimitives.Contains(value) ? "" : "let ";

                    if (EntryPoint.IndexType == IndexType.Index2D)
                    {
                        // Standard: Field 0 is X, Field 1 is Y
                        string comp = value.FieldSpan.Index == 0 ? "x" : "y";
                        AppendLine($"{prefix}{target} = {source}.{comp};");
                        return;
                    }
                    else if (EntryPoint.IndexType == IndexType.Index3D)
                    {
                        string comp = value.FieldSpan.Index == 0 ? "x" : (value.FieldSpan.Index == 1 ? "y" : "z");
                        AppendLine($"{prefix}{target} = {source}.{comp};");
                        return;
                    }
                    else if (EntryPoint.IndexType == IndexType.Index1D)
                    {
                        if (value.FieldSpan.Index == 0) AppendLine($"{prefix}{target} = {source};");
                        else AppendLine($"{prefix}{target} = 0;");
                        return;
                    }
                }
            }

            // 4. Standard Field Access (not a View or Kernel Index)
            var standardTarget = Load(value);
            var standardSource = Load(value.ObjectValue);

            // Check hoisting for the final result
            string finalPrefix = _hoistedPrimitives.Contains(value) ? "" : "let ";

            if (value.Type.IsPointerType)
            {
                AppendLine($"{finalPrefix}{standardTarget} = &({standardSource}).field_{value.FieldSpan.Index};");
            }
            else
            {
                // WGSL supports vector/struct swizzles/access
                // If source is a vec2/vec3 and we want field 0/1, use x/y/z
                // BUT ILGPU IR 'Index2D' is a struct, so it might be treating it as such
                // If we forced Index2D to be vec2<i32>, we need .x/.y access.
                // However, standard structs use struct members.
                // WE MUST CHECK TYPE.

                string fieldAccess = $".field_{value.FieldSpan.Index}";

                // Heuristic: If source is a vector type string, use x/y/z/w
                var typeStr = TypeGenerator[value.ObjectValue.Type];
                if (typeStr.Contains("vec2"))
                {
                    fieldAccess = value.FieldSpan.Index == 0 ? ".x" : ".y";
                }

                AppendLine($"{finalPrefix}{standardTarget} = {standardSource}{fieldAccess};");
            }
        }

        public override void GenerateCode(global::ILGPU.IR.Values.SetField value)
        {
            var target = Load(value.ObjectValue);
            var val = Load(value.Value);
            // Directly update the field of the hoisted variable
            // Note: This relies on 'target' being a mutable 'var' (hoisted primitive)
            AppendLine($"{target}.field_{value.FieldSpan.Index} = {val};");

            // Define the result value to maintain connectivity for downstream users (like Phi)
            // Since we mutated 'target' in place, the result 'value' is logically equivalent to 'target'
            // Use Declare() which checks declaredVariables to avoid duplicate declarations
            // when the variable was already hoisted by the phi pre-scan pass.
            var res = Allocate(value);
            Declare(res);
            AppendLine($"{res.Name} = {target};");
        }

        private static string GetArithmeticOp(BinaryArithmeticKind kind)
        {
            switch (kind)
            {
                case BinaryArithmeticKind.Add: return "+";
                case BinaryArithmeticKind.Sub: return "-";
                case BinaryArithmeticKind.Mul: return "*";
                case BinaryArithmeticKind.Div: return "/";
                case BinaryArithmeticKind.Rem: return "%";
                case BinaryArithmeticKind.And: return "&";
                case BinaryArithmeticKind.Or: return "|";
                case BinaryArithmeticKind.Xor: return "^";
                case BinaryArithmeticKind.Shl: return "<<";
                case BinaryArithmeticKind.Shr: return ">>";
                default: throw new NotSupportedException($"Binary op {kind} not supported.");
            }
        }
        private static string GetCompareOp(CompareKind kind)
        {
            switch (kind)
            {
                case CompareKind.Equal: return "==";
                case CompareKind.NotEqual: return "!=";
                case CompareKind.LessThan: return "<";
                case CompareKind.LessEqual: return "<=";
                case CompareKind.GreaterThan: return ">";
                case CompareKind.GreaterEqual: return ">=";
                default: throw new NotSupportedException($"Compare op {kind} not supported.");
            }
        }
        public override void GenerateCode(LoadFieldAddress value)
        {
            if (ResolveToParameter(value.Source) is global::ILGPU.IR.Values.Parameter param)
            {
                int paramOffset = KernelParamOffset;
                if (param.Index < paramOffset)
                {
                    var target = Load(value);
                    var source = Load(value.Source);
                    if (EntryPoint.IndexType == IndexType.Index2D)
                    {
                        string comp = value.FieldSpan.Index == 0 ? "x" : "y";
                        AppendLine($"var temp_{target.Name} : i32 = {source}.{comp};");
                        AppendLine($"let {target} = &temp_{target.Name};");
                        return;
                    }
                }
            }
            // Handle field address of a packed struct element pointer.
            // Without this, the default codegen emits (*v_X).field_N on an array<u32> binding → WGSL type error.
            {
                var sourceVar = Load(value.Source);
                if (_packedStructLEAVars.TryGetValue(sourceVar.ToString(), out var psLFA))
                {
                    int fieldIdx = value.FieldSpan.Index;
                    var field = psLFA.Layout[fieldIdx];
                    var target = Load(value);
                    string sourceBaseIdx = $"{sourceVar}_base_idx";
                    AppendLine($"let {target.Name}_base_idx = {sourceBaseIdx} + {field.U32Offset};");
                    AppendLine($"let {target.Name} = &{psLFA.BindingName};");
                    if (field.IsEmuF64)
                        _emulatedVarMappings[target.Name] = (-1, -1, true);
                    else if (field.IsEmuI64OrU64)
                        _emulatedVarMappings[target.Name] = (-1, -1, false);
                    else
                        _packedScalarFieldPtrs[target.Name] = (psLFA.BindingName, field);
                    return;
                }
            }
            base.GenerateCode(value);
        }





        protected override void Declare(Variable variable)
        {
            if (_useDeferredDeclarations)
            {
                if (declaredVariables.Contains(variable.Name)) return;
                declaredVariables.Add(variable.Name);
                if (variable.Type.StartsWith("ptr<")) return;
                _deferredVarDeclarations.Add($"    var {variable.Name} : {variable.Type};");
                return;
            }
            base.Declare(variable);
        }

        public override void GenerateCode(PhiValue phiValue)
        {
            // Phi values are handled at the branch level in this state-machine model.
            // We don't emit code here, but the values will be pulled by the branching logic.
        }

        private global::ILGPU.IR.Values.Parameter? ResolveToParameter(global::ILGPU.IR.Value value)
        {
            if (value is global::ILGPU.IR.Values.Parameter p) return p;
            if (value is GetField gf) return ResolveToParameter(gf.ObjectValue);

            if (value is global::ILGPU.IR.Values.Load load) return ResolveToParameter(load.Source);
            if (value is LoadElementAddress lea) return ResolveToParameter(lea.Source);
            if (value is LoadFieldAddress lfa) return ResolveToParameter(lfa.Source);
            if (value is global::ILGPU.IR.Values.NewView nv) return ResolveToParameter(nv.Pointer);

            if (_indexParameterAddress != null && value == _indexParameterAddress)
            {
                return Method.Parameters[0];
            }

            return null;
        }

        /// <summary>
        /// Kernel-specific GetViewLength: resolves the view back to a kernel parameter
        /// and emits i32(arrayLength(&paramN)) for storage buffer bindings.
        /// </summary>
        public override void GenerateCode(global::ILGPU.IR.Values.GetViewLength value)
        {
            var target = Load(value);

            // Try to resolve the view to a kernel parameter
            if (ResolveToParameter(value.View) is global::ILGPU.IR.Values.Parameter param
                && param.Index >= KernelParamOffset)
            {
                string prefix = _hoistedPrimitives.Contains(value) ? "" : "let ";
                string lengthExpr;

                // Direct-param coalesce member: arrayLength() returns the SHARED binding's
                // u32 count (sum across members), not this member's logical element count.
                // v1 doesn't allocate per-member count slots — kernels using direct-param
                // coalesce that read view.Length on coalesced inputs will get the wrong
                // answer. Tracked for v2; the AV1 walker doesn't read view.Length on its
                // coalesce-eligible inputs.
                lengthExpr = $"i32(arrayLength(&{(_directParamCoalesceMembership.TryGetValue(param.Index, out var dpLenGroup) ? dpLenGroup.BindingName : $"param{param.Index}")}))";

                // For packed struct params, arrayLength() returns u32 count — divide by stride to get logical element count
                if (_packedStructLayouts.TryGetValue(param.Index, out var psLenLayout))
                {
                    int psStridelen = psLenLayout.Sum(f => f.U32Count);
                    lengthExpr = $"({lengthExpr} / {psStridelen})";
                }

                // For sub-word params, arrayLength() returns atomic<u32> count.
                // Logical element count = physical * (4 / elementByteSize).
                if (_subWordParams.TryGetValue(param.Index, out var swElemSize))
                {
                    int elemsPerWord = 4 / swElemSize; // 4 for Int8, 2 for Int16/Float16
                    lengthExpr = $"({lengthExpr} * {elemsPerWord})";
                }

                // Handle emulated i64 case
                var targetWgslType = TypeGenerator[value.Type];
                if (Backend.EnableI64Emulation &&
                    (targetWgslType == "emu_i64" || targetWgslType == "emu_u64"))
                {
                    lengthExpr = $"i64_from_i32({lengthExpr})";
                }

                AppendLine($"{prefix}{target} = {lengthExpr};");
                return;
            }

            // Fallback to base implementation
            base.GenerateCode(value);
        }

        protected override bool IsAtomicPointer(Value ptr)
        {
            if (ResolveToParameter(ptr) is global::ILGPU.IR.Values.Parameter param)
            {
                return _atomicParameters.Contains(param.Index);
            }
            return base.IsAtomicPointer(ptr);
        }

        #endregion


        private void GenerateBasicBlockCode(BasicBlock block)
        {
            foreach (var value in block)
            {
                // Skip Terminators (handled by Structured Logic)
                if (value.Value is global::ILGPU.IR.Values.TerminatorValue) continue;
                GenerateCodeFor(value.Value);
            }
        }

        private void GenerateStructuredCode(BasicBlock block, BasicBlock? stopBlock, global::ILGPU.IR.Analyses.Dominators<global::ILGPU.IR.Analyses.ControlFlowDirection.Backwards> pd)
        {
            var visited = new HashSet<BasicBlock>(new BasicBlock.Comparer());

            GenerateStructuredCodeRecursive(block, stopBlock, pd, visited);
        }

        private void GenerateStructuredCodeRecursive(BasicBlock? block, BasicBlock? stopBlock, global::ILGPU.IR.Analyses.Dominators<global::ILGPU.IR.Analyses.ControlFlowDirection.Backwards> pd, HashSet<BasicBlock> visited, Loops<ReversePostOrder, Forwards>.Node? currentLoop = null)
        {
            while (block != null && block != stopBlock)
            {
                // Check if this block is a loop header that we're entering for the first time
                Loops<ReversePostOrder, Forwards>.Node? loopNode = null;
                foreach (var loop in _loops)
                {
                    foreach (var header in loop.Headers)
                    {
                        if (header == block)
                        {
                            loopNode = loop;
                            break;
                        }
                    }
                    if (loopNode != null) break;
                }

                if (loopNode != null && !visited.Contains(block))
                {
                    // Entering a loop — emit a WGSL loop {} construct
                    visited.Add(block);
                    var headerExitBlock = GenerateLoopConstruct(block, loopNode, stopBlock, pd, visited);

                    // After the loop, continue with the header's exit target.
                    // IMPORTANT: Use the header's exit target (returned by GenerateLoopConstruct),
                    // NOT loopNode.Exits[0]. The Exits array may contain body-break-specific
                    // intermediate blocks (e.g., blocks with `flagged = true` assignments) that
                    // should NOT be emitted after the loop — their code belongs exclusively to
                    // the body break path and was already handled inside the loop construct.
                    // Using the header's exit target ensures we start from the correct post-loop
                    // continuation point (typically the merge block shared by all exit paths).
                    if (headerExitBlock != null || loopNode.Exits.Length > 0)
                    {
                        // Prefer the header's exit target; fall back to Exits[0] for
                        // unconditional-branch headers or other edge cases.
                        block = headerExitBlock ?? loopNode.Exits[0];
                        if (WebGPU.Backend.WebGPUBackend.VerboseLogging && _isScanKernel)
                            WebGPUBackend.Log($"[CODEGEN] Post-loop exit: start=B{block.Id} stopBlock=B{stopBlock?.Id}");
                        // Skip pass-through blocks in the exit chain, but never
                        // skip past the stopBlock — the caller owns that boundary.
                        int skipLimit = 10;
                        while (block != null && block != stopBlock && skipLimit-- > 0)
                        {
                            if (block.Terminator is global::ILGPU.IR.Values.UnconditionalBranch exitUBranch
                                && !HasNonPhiInstructions(block))
                            {
                                // Pure pass-through block — mark visited and skip
                                if (WebGPU.Backend.WebGPUBackend.VerboseLogging && _isScanKernel)
                                    WebGPUBackend.Log($"[CODEGEN]   Skip pass-through B{block.Id} → B{exitUBranch.Target.Id}");
                                visited.Add(block);
                                block = exitUBranch.Target;
                            }
                            else
                            {
                                break; // Found a block with real code or conditional branch
                            }
                        }
                    }
                    else
                    {
                        block = null;
                    }
                    continue;
                }

                // Back-edge check: if this is a loop header we've already visited,
                // this is a back-edge — the loop handles re-iteration implicitly.
                if (visited.Contains(block))
                {
                    // If we're inside a loop and this is the header, push phi values for the
                    // back-edge and let the loop iterate naturally.
                    break;
                }
                visited.Add(block);

                // If we're inside a loop and this block is OUTSIDE the loop,
                // we shouldn't be here. This shouldn't happen if stopBlock is set correctly.
                if (currentLoop != null && !currentLoop.Contains(block))
                {
                    break;
                }

                // Emit Block Body — with diagnostic annotations for scan kernels
                if (WebGPU.Backend.WebGPUBackend.VerboseLogging && _isScanKernel)
                {
                    var termDesc = block.Terminator switch
                    {
                        global::ILGPU.IR.Values.IfBranch ib => $"IfBranch(T=B{ib.TrueTarget.Id},F=B{ib.FalseTarget.Id})",
                        global::ILGPU.IR.Values.UnconditionalBranch ub => $"UBranch(B{ub.Target.Id})",
                        global::ILGPU.IR.Values.ReturnTerminator _ => "Return",
                        _ => block.Terminator?.GetType().Name ?? "?"
                    };
                    string loopInfo = currentLoop != null ? $" inLoop" : "";
                    string stopInfo = stopBlock != null ? $" stop=B{stopBlock.Id}" : "";
                    AppendLine($"// BB_{block.Id} ({termDesc}{loopInfo}{stopInfo})");
                    WebGPUBackend.Log($"[CODEGEN] Process BB_{block.Id} {termDesc}{loopInfo}{stopInfo}");
                }
                GenerateBasicBlockCode(block);

                var terminator = block.Terminator;

                if (terminator is global::ILGPU.IR.Values.IfBranch branch)
                {
                    var trueTarget = branch.TrueTarget;
                    var falseTarget = branch.FalseTarget;

                    // Check if we're inside a loop and one target exits the loop
                    if (currentLoop != null)
                    {
                        // DEBUG IL FIX: Use transitive exit detection.
                        // In Debug builds, Roslyn inserts intermediate blocks between
                        // branches and the actual loop exit. We follow unconditional
                        // branch chains to detect indirect exits.
                        bool trueExitsLoop = ExitsLoopTransitively(trueTarget, currentLoop, out var trueExitBlock);
                        bool falseExitsLoop = ExitsLoopTransitively(falseTarget, currentLoop, out var falseExitBlock);
                        bool trueIsBackEdge = IsLoopHeader(trueTarget, currentLoop);
                        bool falseIsBackEdge = IsLoopHeader(falseTarget, currentLoop);

                        // Case: if (cond) break/return; — true exits loop (directly or transitively)
                        if (trueExitsLoop && !falseExitsLoop)
                        {
                            AppendLine($"if ({Load(branch.Condition)}) {{");
                            PushIndent();
                            EmitIntermediateBlocksToExit(trueTarget, trueExitBlock, block, currentLoop, visited);
                            // If the exit leads to a return, emit return; not break;
                            // break only exits the loop; return exits the kernel.
                            if (IsReturnExit(trueExitBlock ?? trueTarget))
                                AppendLine("return;");
                            else
                                AppendLine("break;");
                            PopIndent();
                            AppendLine("}");
                            PushPhiValues(falseTarget, block);
                            block = falseTarget;
                            continue;
                        }
                        // Case: if (!cond) break/return; — false exits loop (directly or transitively)
                        else if (falseExitsLoop && !trueExitsLoop)
                        {
                            AppendLine($"if (!{Load(branch.Condition)}) {{");
                            PushIndent();
                            EmitIntermediateBlocksToExit(falseTarget, falseExitBlock, block, currentLoop, visited);
                            if (IsReturnExit(falseExitBlock ?? falseTarget))
                                AppendLine("return;");
                            else
                                AppendLine("break;");
                            PopIndent();
                            AppendLine("}");
                            PushPhiValues(trueTarget, block);
                            block = trueTarget;
                            continue;
                        }
                        // Case: if (cond) continue; — true goes back to header
                        else if (trueIsBackEdge && !falseIsBackEdge)
                        {
                            AppendLine($"if ({Load(branch.Condition)}) {{");
                            PushIndent();
                            PushPhiValues(trueTarget, block);
                            AppendLine("continue;");
                            PopIndent();
                            AppendLine("}");
                            PushPhiValues(falseTarget, block);
                            block = falseTarget;
                            continue;
                        }
                        // Case: if (!cond) continue; — false goes back to header
                        else if (falseIsBackEdge && !trueIsBackEdge)
                        {
                            AppendLine($"if (!{Load(branch.Condition)}) {{");
                            PushIndent();
                            PushPhiValues(falseTarget, block);
                            AppendLine("continue;");
                            PopIndent();
                            AppendLine("}");
                            PushPhiValues(trueTarget, block);
                            block = trueTarget;
                            continue;
                        }
                    }

                    // Standard if/else handling (no loop break/continue)
                    BasicBlock? mergeNode = pd.GetImmediateDominator(block);
                    if (mergeNode == block) mergeNode = null;

                    // If the post-dominator-computed merge node is outside the current
                    // loop, clamp it to stopBlock. The post-dominator tree is computed
                    // over the full CFG and can cross loop boundaries. If unclamped,
                    // the recursive if/else calls would process blocks beyond the loop,
                    // then set block=mergeNode (outside), causing the outer while loop's
                    // currentLoop.Contains check to break — skipping remaining loop body.
                    if (currentLoop != null && mergeNode != null && !currentLoop.Contains(mergeNode))
                    {
                        if (WebGPU.Backend.WebGPUBackend.VerboseLogging && _isScanKernel)
                            WebGPUBackend.Log($"[CODEGEN] CLAMP mergeNode B{mergeNode.Id} (outside loop) → searching for in-loop merge");

                        // FIX: When the post-dominator is outside the loop (due to a break-exit
                        // path), find the actual in-loop convergence point. If one branch target
                        // is a back-edge block (reaches the loop header through a UB chain),
                        // it's the in-loop merge — both branches converge there before back-edging.
                        // Without this, the continuation code (sum += val; i++) gets placed inside
                        // one branch and the other branch's non-exit paths can't reach it.
                        BasicBlock? inLoopMerge = null;
                        if (currentLoop.Contains(trueTarget) && ReachesHeaderThroughUBChain(trueTarget, currentLoop))
                            inLoopMerge = trueTarget;
                        else if (currentLoop.Contains(falseTarget) && ReachesHeaderThroughUBChain(falseTarget, currentLoop))
                            inLoopMerge = falseTarget;

                        mergeNode = inLoopMerge ?? stopBlock;
                    }

                    if (WebGPU.Backend.WebGPUBackend.VerboseLogging && _isScanKernel)
                    {
                        AppendLine($"// IF from BB_{block.Id}: merge=BB_{mergeNode?.Id.ToString() ?? "null"} true=BB_{trueTarget.Id} false=BB_{falseTarget.Id}");
                        WebGPUBackend.Log($"[CODEGEN] IF BB_{block.Id}: merge=B{mergeNode?.Id.ToString() ?? "null"} true=B{trueTarget.Id} false=B{falseTarget.Id}");
                    }

                    if (falseTarget == mergeNode)
                    {
                        AppendLine($"if ({Load(branch.Condition)}) {{");
                        PushIndent();
                        PushPhiValues(trueTarget, block);
                        GenerateStructuredCodeRecursive(trueTarget, mergeNode, pd, visited, currentLoop);
                        PopIndent();
                        // When falseTarget == merge, the false path goes directly to merge.
                        // We must still push the merge's phi values for this path, otherwise
                        // variables that should retain their pre-branch values will stay at
                        // their hoisted defaults (e.g., 0.0 instead of pixel coordinates).
                        AppendLine("} else {");
                        PushIndent();
                        PushPhiValues(mergeNode, block);
                        PopIndent();
                        AppendLine("}");
                    }
                    else if (trueTarget == mergeNode)
                    {
                        AppendLine($"if (!{Load(branch.Condition)}) {{");
                        PushIndent();
                        PushPhiValues(falseTarget, block);
                        GenerateStructuredCodeRecursive(falseTarget, mergeNode, pd, visited, currentLoop);
                        PopIndent();
                        // When trueTarget == merge, the true path goes directly to merge.
                        // Push the merge's phi values for the true-path (condition was true
                        // but negated above, so this else is the "original true" path).
                        AppendLine("} else {");
                        PushIndent();
                        PushPhiValues(mergeNode, block);
                        PopIndent();
                        AppendLine("}");
                    }
                    else
                    {
                        // SHORT-CIRCUIT FIX: For || short-circuit patterns where both
                        // branches converge on a shared body block (e.g. if (a || b) { body }),
                        // we need to allow the false branch to re-visit blocks that were
                        // visited by the true branch. However, we must NOT blindly restore
                        // all visited state, as that would cause duplicate code emission in
                        // normal if-else branches (breaking the f64 emulation library etc).
                        //
                        // Strategy: After the true branch, check if the false target has a
                        // direct branch to the same block as trueTarget. If so, un-visit
                        // that shared target so the false path can emit it too.
                        var visitedBeforeTrueBranch = new HashSet<BasicBlock>(visited);

                        AppendLine($"if ({Load(branch.Condition)}) {{");
                        PushIndent();
                        PushPhiValues(trueTarget, block);
                        GenerateStructuredCodeRecursive(trueTarget, mergeNode, pd, visited, currentLoop);
                        PopIndent();
                        AppendLine("} else {");
                        PushIndent();
                        PushPhiValues(falseTarget, block);
                        // Targeted restore: only un-visit blocks that are shared convergence
                        // targets reachable from the false branch. This handles || patterns
                        // without duplicating code in normal if-else branches.
                        UnvisitSharedTargets(falseTarget, visitedBeforeTrueBranch, visited);
                        GenerateStructuredCodeRecursive(falseTarget, mergeNode, pd, visited, currentLoop);
                        PopIndent();
                        AppendLine("}");
                    }

                    if (WebGPU.Backend.WebGPUBackend.VerboseLogging && _isScanKernel && mergeNode != null)
                        AppendLine($"// MERGE → BB_{mergeNode.Id}");
                    block = mergeNode;
                }
                else if (terminator is global::ILGPU.IR.Values.UnconditionalBranch uBranch)
                {
                    PushPhiValues(uBranch.Target, block);

                    // Check if this is a back-edge to the loop header (implicit continue)
                    if (currentLoop != null && IsLoopHeader(uBranch.Target, currentLoop))
                    {
                        // Back-edge: phi values pushed, loop continues naturally
                        block = null;
                    }
                    else
                    {
                        block = uBranch.Target;
                    }
                }
                else if (terminator is global::ILGPU.IR.Values.ReturnTerminator)
                {
                    AppendLine("return;");
                    block = null;
                }
                else
                {
                    block = null;
                }
            }
        }

        /// <summary>
        /// For || short-circuit patterns: un-visit blocks that are shared convergence
        /// targets between the true and false branches of an if-else. This walks the
        /// false target's CFG (following unconditional branches and if-branch targets)
        /// to find blocks that were newly visited by the true branch. Only those blocks
        /// are removed from the visited set, avoiding duplicate code emission in normal
        /// if-else branches.
        /// </summary>
        private void UnvisitSharedTargets(BasicBlock falseTarget, HashSet<BasicBlock> visitedBefore, HashSet<BasicBlock> visited)
        {
            // Collect blocks newly visited by the true branch
            var newlyVisited = new HashSet<BasicBlock>(visited, new BasicBlock.Comparer());
            newlyVisited.ExceptWith(visitedBefore);
            if (newlyVisited.Count == 0) return;

            // Walk the false path to find reachable blocks that were also visited
            // by the true path. Only walk a few steps to find immediate convergence
            // (the || pattern typically converges within 1-2 blocks).
            var toCheck = new Queue<BasicBlock>();
            toCheck.Enqueue(falseTarget);
            var checked_ = new HashSet<BasicBlock>(new BasicBlock.Comparer());

            while (toCheck.Count > 0)
            {
                var bb = toCheck.Dequeue();
                if (!checked_.Add(bb)) continue;

                // If this block was newly visited by the true branch, un-visit it
                // so the false branch can re-emit it
                if (newlyVisited.Contains(bb))
                {
                    visited.Remove(bb);
                    // Continue checking successors — the shared body might chain
                    // to more shared blocks via unconditional branches
                }

                // Only follow edges from blocks we haven't un-visited yet or that
                // are the starting block (to find its successors)
                var term = bb.Terminator;
                if (term is global::ILGPU.IR.Values.IfBranch ib)
                {
                    if (newlyVisited.Contains(ib.TrueTarget))
                        toCheck.Enqueue(ib.TrueTarget);
                    if (newlyVisited.Contains(ib.FalseTarget))
                        toCheck.Enqueue(ib.FalseTarget);
                }
                else if (term is global::ILGPU.IR.Values.UnconditionalBranch ub)
                {
                    if (newlyVisited.Contains(ub.Target))
                        toCheck.Enqueue(ub.Target);
                }
            }
        }

        /// <summary>
        /// Checks if a block is a header of the given loop.
        /// </summary>
        private static bool IsLoopHeader(BasicBlock block, Loops<ReversePostOrder, Forwards>.Node loopNode)
        {
            foreach (var header in loopNode.Headers)
            {
                if (header == block) return true;
            }
            return false;
        }

        /// <summary>
        /// DEBUG IL FIX: Checks if a block contains any non-PHI, non-terminator instructions.
        /// Pure pass-through blocks (containing only PHI values and a terminator) can be
        /// skipped in post-loop exit chain processing since their PHI values were already
        /// handled by the loop's break paths.
        /// </summary>
        /// <summary>
        /// Check if a block reaches a loop header through an unconditional branch chain.
        /// This identifies blocks that are the loop's natural continuation point (back-edge
        /// source), which serves as the in-loop merge point for if-else blocks.
        /// </summary>
        private static bool ReachesHeaderThroughUBChain(BasicBlock block, Loops<ReversePostOrder, Forwards>.Node loop)
        {
            var current = block;
            for (int i = 0; i < 10; i++)
            {
                if (current.Terminator is global::ILGPU.IR.Values.UnconditionalBranch ub)
                {
                    foreach (var header in loop.Headers)
                    {
                        if (ub.Target == header) return true;
                    }
                    if (!loop.Contains(ub.Target)) return false;
                    current = ub.Target;
                }
                else return false; // IfBranch or other — not a simple back-edge chain
            }
            return false;
        }

        private static bool HasNonPhiInstructions(BasicBlock block)
        {
            foreach (var value in block)
            {
                if (value.Value is global::ILGPU.IR.Values.TerminatorValue) continue;
                if (value.Value is PhiValue) continue;
                return true; // Found a real instruction
            }
            return false;
        }

        /// <summary>
        /// DEBUG IL FIX: Checks whether a block exits the loop, either directly
        /// or transitively through a chain of unconditional branches.
        /// 
        /// In Debug builds, Roslyn inserts intermediate basic blocks between
        /// control flow decisions and their actual targets. These blocks contain
        /// only unconditional branches (and sometimes PHI assignments). This
        /// method follows such chains to determine if the eventual destination
        /// is outside the loop.
        /// </summary>
        /// <param name="target">The immediate branch target to check.</param>
        /// <param name="loop">The current loop node.</param>
        /// <param name="exitBlock">The actual exit block (outside the loop), or the target itself if it directly exits.</param>
        /// <returns>True if the target eventually exits the loop.</returns>
        private static bool ExitsLoopTransitively(
            BasicBlock target,
            Loops<ReversePostOrder, Forwards>.Node loop,
            out BasicBlock exitBlock)
        {
            // Fast path: direct exit
            if (!loop.Contains(target))
            {
                exitBlock = target;
                return true;
            }

            // Follow unconditional branch chains through intermediate blocks
            var current = target;
            int maxDepth = 10; // Safety limit to prevent infinite loops
            for (int i = 0; i < maxDepth; i++)
            {
                // Only follow unconditional branches (intermediate pass-through blocks)
                if (current.Terminator is global::ILGPU.IR.Values.UnconditionalBranch uBranch)
                {
                    var next = uBranch.Target;
                    if (!loop.Contains(next))
                    {
                        // Found the exit!
                        exitBlock = next;
                        return true;
                    }
                    current = next;
                }
                else
                {
                    // Hit a conditional branch or other terminator — not a simple chain
                    break;
                }
            }

            exitBlock = target;
            return false;
        }

        /// <summary>
        /// Checks if a block (or chain of unconditional branches from it) leads DIRECTLY
        /// to a ReturnTerminator without passing through any blocks that contain real code.
        /// Only follows empty pass-through blocks (PHI-only + unconditional branch/return).
        /// A block with non-PHI instructions is post-loop code, not a direct return path -
        /// even if it ends with ReturnTerminator.
        /// </summary>
        private static bool IsReturnExit(BasicBlock block)
        {
            int limit = 10;
            var current = block;
            while (current != null && limit-- > 0)
            {
                // A block with real instructions is post-loop code, not a direct return.
                // This catches the case where break exits to a block that has post-loop
                // logic (second loop, assignments, stores) before eventually returning.
                if (HasNonPhiInstructions(current))
                    return false;
                if (current.Terminator is global::ILGPU.IR.Values.ReturnTerminator)
                    return true;
                if (current.Terminator is global::ILGPU.IR.Values.UnconditionalBranch ub)
                    current = ub.Target;
                else
                    break;
            }
            return false;
        }

        /// <summary>
        /// DEBUG IL FIX: Emits intermediate blocks' instructions and PHI values
        /// along the chain from startBlock to exitBlock.
        /// 
        /// When Debug IL inserts intermediate blocks between a branch and the
        /// loop exit, those blocks may contain PHI assignments that carry values
        /// (like hitT, steps) forward. This method walks the chain, emitting
        /// each block's instructions and pushing PHI values, then finally pushes
        /// PHI values for the exit block itself.
        /// </summary>
        private void EmitIntermediateBlocksToExit(
            BasicBlock startBlock,
            BasicBlock exitBlock,
            BasicBlock sourceBlock,
            Loops<ReversePostOrder, Forwards>.Node currentLoop,
            HashSet<BasicBlock> visited)
        {
            if (!currentLoop.Contains(startBlock))
            {
                // startBlock IS outside the loop — it's in the exit chain.
                // Push PHIs from the source block into this block.
                PushPhiValues(startBlock, sourceBlock);

                // FIX: If this block is NOT the header's exit target (i.e., it's a
                // body-break-specific intermediate block), emit its code inside the
                // break scope and mark it visited. This ensures assignments like
                // `flagged = true` execute only when the break is taken, not
                // unconditionally after the loop.
                // If this IS the header's exit target, skip — it's the shared merge
                // block that should be emitted naturally after the loop.
                if (_currentLoopHeaderExitTarget != null
                    && startBlock != _currentLoopHeaderExitTarget
                    && HasNonPhiInstructions(startBlock))
                {
                    GenerateBasicBlockCode(startBlock);
                    visited.Add(startBlock);
                }

                // Follow the exit chain transitively to find merge-point PHIs.
                // The exit block may be a pass-through (no PHIs) that leads to a
                // MERGE block where the actual PHIs live (multiple exit paths converge).
                // IMPORTANT: Stop at loop headers of parent loops — pushing PHIs there
                // would overwrite parent loop counter values (triple-nested loop regression).
                var exitChain = startBlock;
                int maxChain = 8;
                for (int depth = 0; depth < maxChain; depth++)
                {
                    if (exitChain.Terminator is global::ILGPU.IR.Values.UnconditionalBranch exitUB)
                    {
                        // Stop if the next block is a loop header — it belongs to a
                        // parent loop whose PHIs will be handled by its own code generation.
                        bool isParentLoopHeader = false;
                        foreach (var loop in _loops)
                        {
                            foreach (var header in loop.Headers)
                            {
                                if (header == exitUB.Target) { isParentLoopHeader = true; break; }
                            }
                            if (isParentLoopHeader) break;
                        }
                        if (isParentLoopHeader) break;

                        PushPhiValues(exitUB.Target, exitChain);
                        exitChain = exitUB.Target;
                    }
                    else break;
                }
                return;
            }

            // Walk through intermediate blocks, emitting their code and PHI values
            var current = startBlock;
            var prevBlock = sourceBlock;
            int maxDepth = 10;
            for (int i = 0; i < maxDepth; i++)
            {
                // Push PHI values from the previous block into this one
                PushPhiValues(current, prevBlock);
                // Mark visited so we don't re-emit
                visited.Add(current);
                // Emit the block's instructions (excluding terminator)
                GenerateBasicBlockCode(current);

                if (current.Terminator is global::ILGPU.IR.Values.UnconditionalBranch uBranch)
                {
                    var next = uBranch.Target;
                    if (!currentLoop.Contains(next))
                    {
                        // Reached the exit — push PHI values transitively
                        PushPhiValuesTransitive(next, current, currentLoop, visited);
                        return;
                    }
                    prevBlock = current;
                    current = next;
                }
                else
                {
                    // Shouldn't happen given ExitsLoopTransitively passed, but be safe
                    break;
                }
            }
        }

        /// <summary>
        /// Generates a WGSL loop {} construct for a natural loop detected by ILGPU's loop analysis.
        /// The loop header block is emitted first, followed by a break condition check,
        /// then the loop body blocks. This ensures barriers are in uniform control flow.
        /// </summary>
        /// <summary>
        /// Returns the header's exit target block (the first block outside the loop
        /// that the header's normal exit leads to). The caller should use this for
        /// post-loop continuation instead of loopNode.Exits[0], which may point to
        /// a body-break-specific intermediate block.
        /// </summary>
        private BasicBlock? GenerateLoopConstruct(
            BasicBlock headerBlock,
            Loops<ReversePostOrder, Forwards>.Node loopNode,
            BasicBlock? outerStopBlock,
            global::ILGPU.IR.Analyses.Dominators<global::ILGPU.IR.Analyses.ControlFlowDirection.Backwards> pd,
            HashSet<BasicBlock> visited)
        {
            // ═══════════════════════════════════════════════════════════════════
            // IR-Level Uniformity Analysis
            // ═══════════════════════════════════════════════════════════════════
            // Analyze the loop to detect grid-stride patterns and emit semantic
            // markers that the WGSL post-processor can use for precise uniformity
            // transforms. This replaces fragile regex-based detection.
            //
            // Grid-stride loops have paired phi counters:
            //   - Thread counter: initialized from GroupIndexValue (local_id) → NON-UNIFORM
            //   - Group counter: initialized from GridIndexValue (group_id) → UNIFORM
            //
            // By emitting markers with variable names, the post-processor knows
            // exactly which variable to use for the uniform break condition.
            // ═══════════════════════════════════════════════════════════════════

            bool loopHasIRBarriers = UniformityAnalyzer.LoopContainsBarrier(loopNode);

            // ═══════════════════════════════════════════════════════════════════
            // IR-Level Uniformity Analysis — Direct Emission
            // ═══════════════════════════════════════════════════════════════════
            // Classify each phi in the loop header by tracing to its builtin source.
            // Store the results so we can use them at the break emission point to
            // emit a uniform break condition directly — no post-processing needed.
            // ═══════════════════════════════════════════════════════════════════
            
            // Maps phi Value → classification result
            var phiClassifications = new Dictionary<Value, Backend.BuiltinTraceResult>();
            // Maps phi Value → emitted WGSL variable name
            var phiVarNames = new Dictionary<Value, string>();
            // Track identified group/thread counters
            Value groupCounterPhi = null;
            Value threadCounterPhi = null;
            string groupCounterName = null;
            string threadCounterName = null;
            
            if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                WebGPUBackend.Log($"[UniformityDirect] Analyzing loop header block {headerBlock.Id} ({headerBlock.Count} values, IR barriers={loopHasIRBarriers})");
            
            foreach (var valueEntry in headerBlock)
            {
                if (valueEntry.Value is PhiValue phi)
                {
                    string varName = Load(phi).ToString();
                    
                    if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                        WebGPUBackend.Log($"[UniformityDirect]   Found phi: {varName} with {phi.Nodes.Length} nodes");
                    
                    // Combine ALL phi node results before classifying.
                    var phiResult = BuiltinTraceResult.Unknown;
                    for (int phiIdx = 0; phiIdx < phi.Nodes.Length; phiIdx++)
                    {
                        var resolved = phi.Nodes[phiIdx].Resolve();
                        if (resolved == phi) continue;
                        var traceResult = UniformityAnalyzer.TraceToBuiltinSource(resolved, 8);
                        
                        if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[UniformityDirect]     Node[{phiIdx}] type={resolved.GetType().Name} -> {traceResult}");
                        
                        phiResult = UniformityAnalyzer.CombineTraceResults(phiResult, traceResult);
                    }
                    
                    if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                        WebGPUBackend.Log($"[UniformityDirect]   Phi combined result: {phiResult}");
                    
                    phiClassifications[phi] = phiResult;
                    phiVarNames[phi] = varName;
                    
                    if (phiResult == BuiltinTraceResult.GridIndex ||
                        phiResult == BuiltinTraceResult.GridDimension)
                    {
                        groupCounterPhi = phi;
                        groupCounterName = varName;
                    }
                    else if (phiResult == BuiltinTraceResult.GroupIndex ||
                             phiResult == BuiltinTraceResult.MixedNonUniform)
                    {
                        threadCounterPhi = phi;
                        threadCounterName = varName;
                    }
                }
            }
            
            if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                WebGPUBackend.Log($"[UniformityDirect] Result: group={groupCounterName ?? "null"}, thread={threadCounterName ?? "null"}");

            // ─── Classify loop type by analyzing the counter increment ───
            // Tile loops (step = GroupDimension / workgroup_size) have uniform
            // iteration counts — all threads break at the same iteration. No
            // synthetic counter needed.
            // Grid-stride loops (step involves GridDimension) DO need the
            // synthetic counter when the loop contains barriers.
            LoopType loopType = LoopType.Unknown;
            if (threadCounterPhi is PhiValue tcPhi)
            {
                loopType = UniformityAnalyzer.ClassifyLoopType(tcPhi, loopNode);
                if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                    WebGPUBackend.Log($"[UniformityDirect] Loop classified as: {loopType}");
            }

            // ─── Pre-loop analysis: determine if synthetic uniform counter is needed ───
            // We must analyze the break condition BEFORE emitting `loop {` so we can
            // emit the synthetic counter initialization before the loop opens.
            //
            // For TILE LOOPS: use a uniform counter derived from the phi's init with
            // GroupIndex (local_invocation_id) stripped. The counter tracks the same
            // iteration pattern as thread 0, and all threads break at the same iteration.
            //
            // For GRID-STRIDE LOOPS: use the existing group_id-based counter.
            bool needsSyntheticGroupCounter = false;
            bool isTileLoopCounter = false;
            string syntheticGroupCounterVar = null;

            var terminator = headerBlock.Terminator;
            if (terminator is global::ILGPU.IR.Values.IfBranch preCheckBranch &&
                threadCounterName != null && groupCounterName == null)
            {
                var condValue = preCheckBranch.Condition.Resolve();
                if (condValue is global::ILGPU.IR.Values.CompareValue preCompare)
                {
                    string preLeftVar = Load(preCompare.Left).ToString();
                    string preRightVar = Load(preCompare.Right).ToString();
                    bool leftIsThread = (preLeftVar == threadCounterName);
                    bool rightIsThread = (preRightVar == threadCounterName);

                    if (leftIsThread || rightIsThread)
                    {
                        string threadType = threadCounterPhi != null ? TypeGenerator[threadCounterPhi.Type] : null;
                        if (threadType == "i32" || threadType == "u32")
                        {
                            bool usedTileCounter = false;

                            if (loopType == LoopType.TileLoop && threadCounterPhi is PhiValue tilePhi)
                            {
                                // ── Tile loop: build uniform counter from phi init ──
                                // The phi init is e.g. base + Group.IdxX + Group.DimX.
                                // Strip GroupIndex to get: base + Group.DimX (uniform).
                                // This tracks thread 0's counter value at each iteration.
                                var initValue = UniformityAnalyzer.FindPhiInitValue(tilePhi, loopNode);
                                string uniformInit = initValue != null
                                    ? UniformityAnalyzer.TryRemoveGroupIndex(initValue, v => Load(v).ToString())
                                    : null;

                                if (uniformInit != null && uniformInit != "")
                                {
                                    needsSyntheticGroupCounter = true;
                                    isTileLoopCounter = true;
                                    syntheticGroupCounterVar = "_uf_tile_iter";
                                    if (_declaredSyntheticCounters.Add(syntheticGroupCounterVar))
                                        AppendLine($"var {syntheticGroupCounterVar} : i32 = {uniformInit};");
                                    else
                                        AppendLine($"{syntheticGroupCounterVar} = {uniformInit};");
                                    usedTileCounter = true;

                                    if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                                        WebGPUBackend.Log($"[UniformityDirect] TILE LOOP counter: var {syntheticGroupCounterVar} = {uniformInit}");
                                }
                                else if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                                {
                                    WebGPUBackend.Log($"[UniformityDirect] TILE LOOP: failed to decompose init, falling back to grid-stride counter");
                                }
                            }

                            if (!usedTileCounter)
                            {
                                // ── Grid-stride / Unknown / tile fallback: existing behavior ──
                                needsSyntheticGroupCounter = true;
                                syntheticGroupCounterVar = "_uf_group_iter";
                                if (_declaredSyntheticCounters.Add(syntheticGroupCounterVar))
                                {
                                    if (ShouldLinearizeGridX())
                                        AppendLine($"var {syntheticGroupCounterVar} : i32 = i32(group_id.x + group_id.y * num_workgroups.x);");
                                    else
                                        AppendLine($"var {syntheticGroupCounterVar} : i32 = i32(group_id.x);");
                                }
                                else
                                {
                                    if (ShouldLinearizeGridX())
                                        AppendLine($"{syntheticGroupCounterVar} = i32(group_id.x + group_id.y * num_workgroups.x);");
                                    else
                                        AppendLine($"{syntheticGroupCounterVar} = i32(group_id.x);");
                                }

                                if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                                    WebGPUBackend.Log($"[UniformityDirect] SYNTHETIC counter: var {syntheticGroupCounterVar} (linearized={ShouldLinearizeGridX()})");
                            }
                        }
                    }
                }
            }
            
            if (WebGPU.Backend.WebGPUBackend.VerboseLogging && _isScanKernel)
            {
                var exitIds = string.Join(",", loopNode.Exits.Select(e => $"B{e.Id}"));
                AppendLine($"// LOOP header=BB_{headerBlock.Id} exits=[{exitIds}]");
                WebGPUBackend.Log($"[CODEGEN] LOOP header=B{headerBlock.Id} exits=[{exitIds}] outerStop=B{outerStopBlock?.Id}");
            }
            AppendLine("loop {");
            PushIndent();

            // ─── Two-pass uniform-break (GEMV grid-stride fix) ───
            // The synthetic uniform break + group/tile counter (init above, increment below) are
            // emitted UNCONDITIONALLY for a thread-dependent loop, but whether the loop's break
            // actually USES the uniform form is decided AFTER the body is emitted: only a loop that
            // contains a barrier needs uniform control flow. We can't know that pre-emission —
            // cooperative-op barriers (Group/Warp Scan/Reduce) are produced by intrinsic HANDLERS at
            // codegen time, not as IR — so we emit a sentinel for the break condition now and patch it
            // from the EMITTED body text once the loop is closed (workgroupBarrier()/storageBarrier()
            // are real text by then, incl. inlined-helper barriers). Barrier-free loops (e.g. a
            // cooperative GEMV's K-tile loop, barriers in the post-loop reduction) keep their natural
            // thread-dependent break instead of being conflated with the group grid-stride counter.
            int _ufBodyStartPos = Builder.Length;
            string _ufBreakSentinel = null;
            string _ufUniformBreak = null;
            string _ufNaturalBreak = null;

            // Emit the header block's body (instructions)
            GenerateBasicBlockCode(headerBlock);

            // Track the header's exit target for post-loop continuation.
            BasicBlock? headerExitTarget = null;

            if (terminator is global::ILGPU.IR.Values.IfBranch headerBranch)
            {
                var trueTarget = headerBranch.TrueTarget;
                var falseTarget = headerBranch.FalseTarget;

                // DEBUG IL FIX: Use transitive exit detection for loop headers too.
                bool trueIsExit = ExitsLoopTransitively(trueTarget, loopNode, out var trueHeaderExit);
                bool falseIsExit = ExitsLoopTransitively(falseTarget, loopNode, out var falseHeaderExit);

                // The header's exit target is the first block outside the loop
                // that the header's normal exit leads to. Used for post-loop
                // continuation (not body-break-specific intermediate blocks).
                headerExitTarget = trueIsExit ? trueTarget : (falseIsExit ? falseTarget : null);
                _currentLoopHeaderExitTarget = headerExitTarget;

                // ─── Uniform break emission ─────────────────────────────────
                // If the break condition uses a non-uniform counter, emit a
                // uniform replacement directly. This avoids the need for
                // post-processing regex.
                // For tile loops: use the tile counter with DIRECT comparison
                // (no * workgroup_size.x scaling — the tile counter already
                // tracks thread 0's absolute position).
                // For grid-stride loops: use the group counter * workgroup_size.
                string breakConditionExpr = null;
                if ((trueIsExit || falseIsExit) && threadCounterName != null)
                {
                    // Check if the condition traces through a comparison
                    // involving the thread counter phi
                    var condValue = headerBranch.Condition.Resolve();
                    if (condValue is global::ILGPU.IR.Values.CompareValue compare)
                    {
                        // Check if either operand traces to a phi we classified
                        string leftVar = Load(compare.Left).ToString();
                        string rightVar = Load(compare.Right).ToString();
                        string compOp = GetCompareOp(compare.Kind);
                        
                        // Determine which side is the thread counter and which is the limit
                        bool leftIsThread = (leftVar == threadCounterName);
                        bool rightIsThread = (rightVar == threadCounterName);
                        
                        if (leftIsThread || rightIsThread)
                        {
                            string limitVar = leftIsThread ? rightVar : leftVar;
                            string uniformExpr = null;

                            if (isTileLoopCounter && syntheticGroupCounterVar != null)
                            {
                                // ── Tile loop: direct comparison ──
                                // The tile counter tracks thread 0's absolute position,
                                // so compare directly against the limit (no scaling).
                                if (leftIsThread)
                                    uniformExpr = $"{syntheticGroupCounterVar} {compOp} {limitVar}";
                                else
                                    uniformExpr = $"{limitVar} {compOp} {syntheticGroupCounterVar}";
                            }
                            else if (groupCounterName != null)
                            {
                                // Has a separate uniform group counter
                                // Use: (groupCounter * workgroup_size) op limit
                                if (leftIsThread)
                                    uniformExpr = $"({groupCounterName} * i32(workgroup_size.x)) {compOp} {limitVar}";
                                else
                                    uniformExpr = $"{limitVar} {compOp} ({groupCounterName} * i32(workgroup_size.x))";
                            }
                            else
                            {
                                // No separate group counter — inject a SYNTHETIC one.
                                // We emit a new variable that uses ONLY uniform builtins
                                // (group_id, num_workgroups, workgroup_size) so the WGSL
                                // validator sees no local_id in the break condition.
                                // GUARD: Only valid for scalar i32 counters (not vec2/vec3)
                                string threadType = threadCounterPhi != null ? TypeGenerator[threadCounterPhi.Type] : null;
                                bool isScalarInt = threadType == "i32" || threadType == "u32";

                                if (isScalarInt)
                                {
                                    // We need the synthetic counter — mark for init and increment
                                    needsSyntheticGroupCounter = true;
                                    syntheticGroupCounterVar = "_uf_group_iter";
                                    if (leftIsThread)
                                        uniformExpr = $"({syntheticGroupCounterVar} * i32(workgroup_size.x)) {compOp} {limitVar}";
                                    else
                                        uniformExpr = $"{limitVar} {compOp} ({syntheticGroupCounterVar} * i32(workgroup_size.x))";
                                }
                                else if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                                {
                                    WebGPUBackend.Log($"[UniformityDirect] SKIP: thread counter {threadCounterName} is {threadType ?? "unknown"}, not scalar i32");
                                }
                            }
                            
                            if (uniformExpr != null)
                            {
                                string origCondVar = Load(headerBranch.Condition).ToString();
                                breakConditionExpr = $"_uf_break_{origCondVar}";
                                AppendLine($"let {breakConditionExpr} = {uniformExpr};");
                                
                                if (WebGPU.Backend.WebGPUBackend.VerboseLogging)
                                    WebGPUBackend.Log($"[UniformityDirect] EMIT uniform break: let {breakConditionExpr} = {uniformExpr}");
                            }
                        }
                    }
                }

                if (trueIsExit)
                {
                    // True branch exits → break when condition is true
                    string _natCond = Load(headerBranch.Condition).ToString();
                    string condExpr;
                    if (breakConditionExpr != null)
                    {
                        // Defer uniform-vs-natural to the post-body barrier scan (see two-pass note).
                        _ufBreakSentinel = $"/*__UFBRK{_ufBreakSentinelCounter++}__*/";
                        _ufUniformBreak = breakConditionExpr;
                        _ufNaturalBreak = _natCond;
                        condExpr = _ufBreakSentinel;
                    }
                    else condExpr = _natCond;
                    AppendLine($"if ({condExpr}) {{");
                    PushIndent();
                    EmitIntermediateBlocksToExit(trueTarget, trueHeaderExit, headerBlock, loopNode, visited);
                    AppendLine("break;");
                    PopIndent();
                    AppendLine("}");

                    // False branch is the loop body
                    PushPhiValues(falseTarget, headerBlock);
                    GenerateStructuredCodeRecursive(falseTarget, headerBlock, pd, visited, loopNode);
                }
                else if (falseIsExit)
                {
                    // False branch exits → break when condition is false
                    string _natCond = $"!{Load(headerBranch.Condition)}";
                    string condExpr;
                    if (breakConditionExpr != null)
                    {
                        _ufBreakSentinel = $"/*__UFBRK{_ufBreakSentinelCounter++}__*/";
                        _ufUniformBreak = $"!{breakConditionExpr}";
                        _ufNaturalBreak = _natCond;
                        condExpr = _ufBreakSentinel;
                    }
                    else condExpr = _natCond;
                    AppendLine($"if ({condExpr}) {{");
                    PushIndent();
                    EmitIntermediateBlocksToExit(falseTarget, falseHeaderExit, headerBlock, loopNode, visited);
                    AppendLine("break;");
                    PopIndent();
                    AppendLine("}");

                    // True branch is the loop body
                    PushPhiValues(trueTarget, headerBlock);
                    GenerateStructuredCodeRecursive(trueTarget, headerBlock, pd, visited, loopNode);
                }
                else
                {
                    // Both branches stay in the loop — emit if/else inside loop
                    AppendLine($"if ({Load(headerBranch.Condition)}) {{");
                    PushIndent();
                    PushPhiValues(trueTarget, headerBlock);
                    GenerateStructuredCodeRecursive(trueTarget, headerBlock, pd, visited, loopNode);
                    PopIndent();
                    AppendLine("} else {");
                    PushIndent();
                    PushPhiValues(falseTarget, headerBlock);
                    GenerateStructuredCodeRecursive(falseTarget, headerBlock, pd, visited, loopNode);
                    PopIndent();
                    AppendLine("}");
                }
            }
            else if (terminator is global::ILGPU.IR.Values.UnconditionalBranch uBranch)
            {
                // Unconditional branch in header — emit body, loop continues implicitly
                PushPhiValues(uBranch.Target, headerBlock);
                GenerateStructuredCodeRecursive(uBranch.Target, headerBlock, pd, visited, loopNode);
            }

            // If we used a synthetic counter, emit its increment
            // at the end of the loop body (before PopIndent/closing brace).
            if (needsSyntheticGroupCounter && syntheticGroupCounterVar != null)
            {
                if (isTileLoopCounter)
                {
                    // Tile loop: step by workgroup_size (same as the actual loop step)
                    AppendLine($"{syntheticGroupCounterVar} = {syntheticGroupCounterVar} + i32(workgroup_size.x);");
                }
                else
                {
                    // Grid-stride: step by num_workgroups
                    if (ShouldLinearizeGridX())
                        AppendLine($"{syntheticGroupCounterVar} = {syntheticGroupCounterVar} + i32(num_workgroups.x * num_workgroups.y);");
                    else
                        AppendLine($"{syntheticGroupCounterVar} = {syntheticGroupCounterVar} + i32(num_workgroups.x);");
                }
            }

            PopIndent();
            AppendLine("}");

            // ─── Two-pass uniform-break resolution ───
            // Body fully emitted. Patch the deferred break sentinel from the ACTUAL emitted body:
            // a loop that contains a barrier (workgroupBarrier/storageBarrier — incl. ones from
            // inlined cooperative-op helpers, which are real text by now) keeps the synthetic UNIFORM
            // break (so the barrier stays in uniform control flow); a barrier-free loop reverts to its
            // NATURAL thread-dependent break (fixes the GEMV K-loop conflation). The unconditionally
            // emitted synthetic counter/let are harmless (written-but-unread) when the natural break wins.
            if (_ufBreakSentinel != null)
            {
                string _ufBody = Builder.ToString(_ufBodyStartPos, Builder.Length - _ufBodyStartPos);
                bool _ufLoopHasBarrier = _ufBody.Contains("workgroupBarrier(", StringComparison.Ordinal)
                    || _ufBody.Contains("storageBarrier(", StringComparison.Ordinal);
                Builder.Replace(_ufBreakSentinel, _ufLoopHasBarrier ? _ufUniformBreak : _ufNaturalBreak);
            }

            _currentLoopHeaderExitTarget = null;
            return headerExitTarget;
        }


        // Uniformity analysis methods extracted to UniformityAnalyzer.cs (Phase 1.3)

    }

}
