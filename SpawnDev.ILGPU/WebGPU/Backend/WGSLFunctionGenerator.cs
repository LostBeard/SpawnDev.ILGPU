// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: WGSLFunctionGenerator.cs
//
// WGSL function generator for helper device functions.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;
using System;
using System.Collections.Generic;
using System.Text;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// Function generator for helper device functions (non-kernel methods).
    /// </summary>
    internal sealed class WGSLFunctionGenerator : WGSLCodeGenerator
    {
        #region Constants

        /// <summary>
        /// Methods with these flags will be skipped during code generation.
        /// </summary>
        private const MethodFlags MethodFlagsToSkip =
            MethodFlags.External |
            MethodFlags.Intrinsic;

        #endregion

        #region Instance

        private readonly GeneratorArgs _args;

        // Sub-word state for fn-param ArrayView codegen (rc.16 Bug D phase 2,
        // 2026-05-05). Mirrors WGSLKernelFunctionGenerator's _subWord* dictionaries
        // but keyed by helper-local Parameter.Index instead of kernel-binding index.
        // Populated in ScanHelperSubWordParams() called from GenerateCode() before
        // body emission. Consumed by the LEA / Load / Store overrides below.
        private readonly Dictionary<int, int> _subWordParams = new();           // paramIdx -> elemSize (1 byte / 2 short / 2 emu-f16)
        private readonly HashSet<int> _subWordUnsignedParams = new();           // byte / ushort
        private readonly HashSet<int> _subWordFloat16Params = new();            // emulated f16 (no shader-f16)
        private readonly HashSet<int> _subWordBFloat16Params = new();           // emulated bf16 (always - no native WGSL bf16)
        private readonly Dictionary<string, int> _subWordLEAVars = new();       // LEA target Variable name -> paramIdx
        private readonly Dictionary<int, string> _paramIndexToLocalVar = new(); // paramIdx -> "v_X" (the helper's `let v_X : ptr<...> = p_Y;` local name)

        // Coalesce-aware fn-param tracking (local.15+): closes Tuvok L44455 walker bug
        // where N coalesced ArrayView args passed to a NoInlining helper produce N
        // aliased ptr args at the WGSL call site. The helper's signature collapses
        // each coalesced group into 1 leader ptr + N per-member offset i32 args.
        // _coalesceParamInfo: paramIdx -> (helperLocalLeaderPtrName, offsetArgName, entry)
        //   Used by GenerateCode(LoadElementAddress) and the call-site emit to find
        //   the leader ptr + offset for each coalesced helper param.
        // _coalesceGroupLeaderPtrNames: leaderBindingName -> WGSL ptr arg name for the group
        //   The first member of each group gets the ptr arg; non-leader members reuse
        //   the leader's ptr name via this dict.
        private readonly Dictionary<int, (string leaderPtr, string offsetArg, HelperParamCoalesceEntry entry)> _coalesceParamInfo = new();
        private readonly Dictionary<string, string> _coalesceGroupLeaderPtrNames = new();

        // Emulated 64-bit storage view params (ArrayView<long>/<ulong>/<double>).
        // Helper signature for these uses `array<u32>` (raw bits, mirroring kernel
        // binding) rather than `array<emu_i64>` / `array<emu_f64>`. LEA / Load /
        // Store overrides consult these sets to emit the stride=2 raw u32 access
        // pattern instead of direct element indexing.
        // Closes Tuvok's 2026-05-05 walker WGSL L44452 type mismatch.
        private readonly HashSet<int> _emuI64Params = new();    // ArrayView<long>/<ulong>
        private readonly HashSet<int> _emuF64Params = new();    // ArrayView<double>
        // LEA target Variable name -> (paramIdx, isF64). Populated when LEA on an
        // emu-64 view is emitted; consumed by Load / Store to know which raw-bits
        // base_idx to dereference.
        private readonly Dictionary<string, (int paramIdx, bool isF64)> _emu64LEAVars = new();

        // Cross-block FieldAddress inline substitution map. Helper bodies emitted as
        // multi-block switch/case state machines define LoadFieldAddress in one case
        // and use it in others; WGSL `let` is case-scoped, so a use in case N of a
        // `let` declared in case M < N is "unresolved value". The kernel pre-hoists
        // primitives + tracks LEA via _crossBlockPointerExprs; field addresses here
        // get the same treatment via a registered "deref form" expression that the
        // Load / Store overrides substitute into the rhs / lhs.
        // (rc.16 Bug D phase 2 follow-up, 2026-05-05.)
        private readonly Dictionary<string, string> _fieldAddressDerefExpr = new(); // varName -> "(*src).fieldName"

        // Helper-side deferred declarations for sub-word LEA targets. Like the kernel-side
        // `_deferredVarDeclarations`, this collects `var v_X : i32;` lines that need to be
        // emitted at the helper fn-body top (function scope) so cross-block uses resolve
        // correctly. Sub-word LEA emit appends here when the helper has multiple WGSL
        // blocks (nested if/else/loop within the fn body); the body is generated then
        // `Builder.Insert` injects the deferred decls at the saved fn-body start position.
        // Tuvok's local.9 walker WGSL `unresolved value 'v_359'` (Bug D phase 7 follow-up,
        // 2026-05-05).
        private readonly List<string> _helperDeferredVarDeclarations = new();

        /// <summary>
        /// Creates a new WGSL function generator.
        /// </summary>
        public WGSLFunctionGenerator(
            in GeneratorArgs args,
            Method method,
            Allocas allocas)
            : base(args, method, allocas)
        {
            _args = args;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Returns the WGSL parameter-position type for a ViewType.
        /// Address space is read from the ViewType: `Shared` → `workgroup`,
        /// `Local` → `function`, `Global`/`Generic` → `storage`. Sub-word element
        /// types (Int8/Int16/Float16-emulated) on storage backing lower to
        /// `ptr&lt;storage, array&lt;atomic&lt;u32&gt;&gt;, read_write&gt;`; full-word lower to
        /// `ptr&lt;{addrSpace}, array&lt;{elem}&gt;, read_write&gt;`. Mirrors the kernel-side
        /// SetupParameterBindings sub-word detection (rc.16 fn-def codegen Bug D
        /// + 2026-05-05 phase 6 — shared-memory address space propagation).
        /// </summary>
        internal static string GetWgslViewParamType(
            ViewType viewType,
            WGSLTypeGenerator typeGenerator,
            bool hasShaderF16,
            MemoryAddressSpace? observedAddressSpace = null)
        {
            bool isSubWord = false;
            bool isEmuI64 = false;
            bool isEmuF64 = false;
            if (viewType.ElementType is PrimitiveType prim)
            {
                switch (prim.BasicValueType)
                {
                    case BasicValueType.Int8:
                    case BasicValueType.Int16:
                        isSubWord = true;
                        break;
                    case BasicValueType.Float16:
                        if (!hasShaderF16) isSubWord = true;
                        break;
                    case BasicValueType.Int64:
                        isEmuI64 = true;
                        break;
                    case BasicValueType.Float64:
                        isEmuF64 = true;
                        break;
                }
            }

            // Map ILGPU MemoryAddressSpace to WGSL storage class. The address
            // space comes from one of two sources:
            //   1. observedAddressSpace — call-site-observed address space
            //      from the kernel's pre-body scan (Bug D phase 7). Used when
            //      ILGPU's IR didn't propagate `Shared` / `Local` into the helper's
            //      parameter type. Takes precedence when present.
            //   2. viewType.AddressSpace — IR-level address space on the param
            //      type. Fallback for the common storage-buffer case where the
            //      IR has the right address space already.
            // Generic / Global / unobserved → "storage" (typical kernel param).
            var effectiveAddressSpace = observedAddressSpace ?? viewType.AddressSpace;
            string addrSpace = effectiveAddressSpace switch
            {
                MemoryAddressSpace.Shared => "workgroup",
                MemoryAddressSpace.Local => "function",
                _ => "storage",
            };

            // Sub-word packing into atomic<u32> is the storage-buffer pattern
            // (CPU-side packing + atomicLoad/atomicAnd/atomicOr in WGSL). For
            // workgroup-memory sub-word views we'd need a different layout
            // (workgroup vars don't share host-side packing). Until a test
            // surfaces that case, route sub-word through the storage form.
            if (isSubWord && addrSpace == "storage")
                return "ptr<storage, array<atomic<u32>>, read_write>";

            // Emulated 64-bit element types (Int64/UInt64/Float64) on storage
            // backing must mirror the kernel's `array<u32>` raw-bits binding
            // (see WGSLKernelFunctionGenerator.GenerateHeader line 1899-1903).
            // The kernel binds `array<u32>` so it can spinlock-protected access
            // each u32 half independently for atomic emu-64 ops; the helper
            // signature must match or Tint rejects the call site as a
            // ptr<array<u32>> vs ptr<array<emu_i64>> type mismatch.
            // Closes Tuvok's 2026-05-05 walker `EncodeFrameBody_*` arg type
            // mismatch at WGSL L44452 where arg N expected `array<vec2<u32>>`
            // but kernel passed `array<u32>`. Helper-side LEA + Load + Store
            // for these params use raw u32 stride=2 (lo+hi) access — see
            // GenerateCode(LoadElementAddress) / Load / Store overrides.
            if ((isEmuI64 || isEmuF64) && addrSpace == "storage")
                return "ptr<storage, array<u32>, read_write>";

            string elemTypeName = typeGenerator[viewType.ElementType];
            // Non-storage address spaces use `read_write` access mode; WGSL
            // does not parameterize ptr<workgroup,T> with an explicit access.
            // Keep the trailing `, read_write` only for storage.
            return addrSpace == "storage"
                ? $"ptr<storage, array<{elemTypeName}>, read_write>"
                : $"ptr<{addrSpace}, array<{elemTypeName}>>";
        }

        /// <summary>
        /// Generates a header stub for the current method.
        ///
        /// Coalesce-aware emit (local.15+): when the caller's pre-body scan recorded
        /// `HelperParamCoalesceInfo` for params that share a coalesced kernel binding,
        /// the signature collapses each coalesced group into ONE leader ptr arg + N
        /// per-member offset i32 args. The non-leader members of the group don't get
        /// their own ptr arg (just their offset arg), avoiding WGSL's "invalid aliased
        /// pointer argument" error. Closes Tuvok's L44455 walker bug.
        /// </summary>
        private void GenerateHeaderStub(StringBuilder builder)
        {
            builder.Append("fn ");
            builder.Append(GetMethodName(Method));
            builder.Append("(");

            // Emit parameters
            bool first = true;
            // Track which leader-binding ptrs have already been emitted (one per group).
            var emittedLeaderPtrs = new HashSet<string>();

            void Emit(string s)
            {
                if (!first) builder.Append(", ");
                first = false;
                builder.Append(s);
            }

            foreach (var param in Method.Parameters)
            {
                // Coalesce-aware path: this param is a member of a direct-param coalesce
                // group. Emit (leader ptr if first member of group) + per-member offset i32.
                if (param.ParameterType is ViewType
                    && _args.HelperParamCoalesceInfo.TryGetValue((Method, param.Index), out var coalEntry))
                {
                    if (coalEntry.IsLeader && !emittedLeaderPtrs.Contains(coalEntry.LeaderBindingName))
                    {
                        // Emit the LEADER ptr arg ONCE for the group. The leader's name
                        // pattern is `p_<paramId>_leader_ptr` so the helper body's LEA
                        // override can find it via the same naming convention.
                        emittedLeaderPtrs.Add(coalEntry.LeaderBindingName);
                        // Storage class is "storage" for direct-param coalesce (always backed
                        // by a kernel storage binding). Element type from the group entry.
                        string leaderType = $"ptr<storage, array<{coalEntry.LeaderBindingWgslElementType}>, read_write>";
                        Emit($"p_{param.Id}_leader : {leaderType}");
                    }
                    // Always emit the per-member offset arg (i32 element index into the leader).
                    Emit($"p_{param.Id}_offset : i32");
                    continue;
                }

                string paramType;
                if (param.ParameterType is ViewType viewType)
                {
                    // Bug D fix (2026-05-05): ArrayView<T> as a fn-param emits as
                    // a real WGSL pointer to the kernel's storage binding, not the
                    // bare element type. Without this fix, helpers calling
                    // sub-word LEA on a buf param fail because `var v_X : i32 = p_X;`
                    // produces an i32 local that can't be indexed as an array.
                    //
                    // Bug D phase 7 (2026-05-05): consult the call-site observation
                    // table when ILGPU's IR didn't propagate the right address space
                    // into the helper's parameter type. Without this, a workgroup
                    // ArrayView passed from the kernel emits as `ptr<storage, ...>`
                    // and Tint rejects the type mismatch at the call site.
                    var observedAS = _args.HelperParamAddressSpaces.TryGetValue(
                        (Method, param.Index), out var asValue) ? asValue : (MemoryAddressSpace?)null;
                    paramType = GetWgslViewParamType(
                        viewType,
                        TypeGenerator,
                        Backend.HasShaderF16,
                        observedAS);
                }
                else
                {
                    paramType = TypeGenerator[param.ParameterType];
                }
                Emit($"p_{param.Id} : {paramType}");
            }

            builder.Append(")");

            // WGSL grammar: void-returning fns omit the return-type clause entirely.
            // `fn name(...) -> void { ... }` is rejected by the validator with
            // "unresolved type 'void'"; correct form is `fn name(...) { ... }`.
            if (!Method.ReturnType.IsVoidType)
            {
                builder.Append(" -> ");
                builder.Append(TypeGenerator[Method.ReturnType]);
            }
        }

        /// <summary>
        /// Gets the WGSL function name for the given method.
        /// Used by both the function generator (to emit the fn definition) and the
        /// kernel generator (to emit the call site) so the names match.
        /// </summary>
        internal static string GetMethodName(Method method)
        {
            var handleName = method.Handle.Name;
            if (method.HasFlags(MethodFlags.External))
            {
                return handleName;
            }
            else
            {
                // Constructor names in MSIL start with a dot
                return handleName.StartsWith(".")
                    ? handleName.Substring(1) + "_" + method.Id
                    : handleName + "_" + method.Id;
            }
        }

        /// <summary>
        /// Emits module-scope shared memory declarations.
        /// 
        /// CRITICAL: Shared memory Alloca nodes from the kernel's IR are referenced by helper
        /// functions but are NOT in the helper function's Allocas.SharedAllocations. They must
        /// be declared as var&lt;workgroup&gt; at module scope before the function body runs.
        /// We use GeneratorArgs.SharedMemoryVarNames (pre-populated from backendContext) to
        /// emit the declarations and pre-register them in valueVariables.
        /// </summary>
        public override void GenerateHeader(StringBuilder builder)
        {
            // Pre-register shared memory variable names so Load() can find them.
            // NOTE: The actual var<workgroup> declarations are emitted by the
            // KERNEL generator's GenerateHeader() — we must NOT duplicate them here.
            // The Load() interception in the base class handles reference-inequality
            // between the Alloca instance here and the one in the function body.
            foreach (var kvp in _args.SharedMemoryVarNames)
            {
                var allocaNode = kvp.Key;
                var (varName, declaration) = kvp.Value;

                // Register in valueVariables for this specific Alloca instance
                var wgslType = TypeGenerator[allocaNode.Type];
                var sharedVar = new Variable(varName, wgslType);
                valueVariables[allocaNode] = sharedVar;
                declaredVariables.Add(varName);
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[DIAG-FUNC-SHARED-REG] Registered (no emit): {varName} for method {Method.Handle.Name}_{Method.Id}");
            }
        }

        /// <summary>
        /// Generates WGSL code for the function body.
        /// </summary>
        public override void GenerateCode()
        {
            if (Method.HasFlags(MethodFlagsToSkip))
                return;

            // Skip code generation for helpers that will be inlined by the kernel generator.
            // The kernel's GenerateCode(MethodCall) handles inlining these methods directly
            // at the call site. Emitting them here as standalone functions would place their
            // code at module scope (outside fn main), causing WGSL validation errors.
            if (_args.HelperMethods.ContainsKey(Method))
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-FuncGen] Skipping code gen for inlined helper: {Method.Handle.Name}_{Method.Id}");
                return;
            }

            // Pre-scan parameters for sub-word ArrayView types so the LEA / Load /
            // Store overrides know which params need atomic-load + shift / mask
            // emission. Must run before body codegen sees any LoadElementAddress.
            // (rc.16 fn-def-codegen Bug D phase 2, 2026-05-05).
            ScanHelperSubWordParams();

            // Declare function and parameters
            GenerateHeaderStub(Builder);
            Builder.AppendLine(" {");
            PushIndent();

            // Bind parameters to local variables
            foreach (var param in Method.Parameters)
            {
                // Coalesce-aware binding (local.15+): for params in a coalesced group,
                // bind the leader ptr alias (per group, only first member's leaderPtr arg
                // is in the signature) and record the per-member offset arg name. The LEA
                // override below uses these to emit `&(*leaderPtr)[offsetArg + idxExpr]`.
                if (param.ParameterType is ViewType
                    && _args.HelperParamCoalesceInfo.TryGetValue((Method, param.Index), out var coalEntry))
                {
                    var coalVariable = Allocate(param);
                    // For the LEADER param, `p_{param.Id}_leader` is the WGSL ptr-arg name.
                    // For NON-LEADER members, the leader is referenced by the FIRST member's
                    // p_{leaderId}_leader name. We need to track the leader's id per group.
                    string leaderPtrName;
                    if (coalEntry.IsLeader)
                    {
                        leaderPtrName = $"p_{param.Id}_leader";
                        _coalesceGroupLeaderPtrNames[coalEntry.LeaderBindingName] = leaderPtrName;
                    }
                    else
                    {
                        // Look up the leader ptr name (set when we processed the leader earlier).
                        leaderPtrName = _coalesceGroupLeaderPtrNames.TryGetValue(coalEntry.LeaderBindingName, out var name)
                            ? name
                            : $"p_{param.Id}_leader"; // fallback (shouldn't happen with proper group ordering)
                    }
                    string offsetArgName = $"p_{param.Id}_offset";
                    _coalesceParamInfo[param.Index] = (leaderPtrName, offsetArgName, coalEntry);
                    // Bind variable.Name to the local helper alias for the leader ptr.
                    // Loads on `parameter[i]` will go through the LEA override below.
                    AppendLine($"// param {param.Index} (coalesced; leader={leaderPtrName}, offset={offsetArgName})");
                    declaredVariables.Add(coalVariable.Name);
                    continue;
                }

                var variable = Allocate(param);
                // Bug D fix (2026-05-05): ViewType params are emitted as ptr<storage, ...>
                // in the fn signature (see GenerateHeaderStub), so the binding must use
                // `let` (pointers can't be `var`). The Variable's Type comes from
                // TypeGenerator[ViewType] which returns the element type (i32) - we have
                // to override here to keep the binding consistent with the signature.
                if (param.ParameterType is ViewType viewType)
                {
                    var observedAS = _args.HelperParamAddressSpaces.TryGetValue(
                        (Method, param.Index), out var asValue) ? asValue : (MemoryAddressSpace?)null;
                    string ptrType = GetWgslViewParamType(
                        viewType,
                        TypeGenerator,
                        Backend.HasShaderF16,
                        observedAS);
                    AppendLine($"let {variable.Name} : {ptrType} = p_{param.Id};");
                    // Remember the helper-local var name for this param so the
                    // sub-word Load / Store overrides can emit `(*v_X)[wordIdx]`.
                    _paramIndexToLocalVar[param.Index] = variable.Name;
                    // Track emu-64 ArrayView params so LEA / Load / Store emit
                    // raw u32 stride=2 access matching the kernel's `array<u32>`
                    // binding (closes Tuvok L44452 type mismatch).
                    if (viewType.ElementType is PrimitiveType emuPrim)
                    {
                        if (emuPrim.BasicValueType == BasicValueType.Int64)
                            _emuI64Params.Add(param.Index);
                        else if (emuPrim.BasicValueType == BasicValueType.Float64)
                            _emuF64Params.Add(param.Index);
                    }
                    continue;
                }
                // WGSL: pointer types (ptr<workgroup, ...>, ptr<storage, ...>, ptr<function, ...>)
                // are not constructible and cannot be used with 'var'. Use 'let' instead.
                if (variable.Type.StartsWith("ptr<"))
                    AppendLine($"let {variable.Name} = p_{param.Id};");
                else
                    AppendLine($"var {variable.Name} : {variable.Type} = p_{param.Id};");
            }

            // Save the position right after parameter binding — sub-word LEA hoisted
            // declarations (Bug D phase 7 follow-up, 2026-05-05) get inserted here so
            // they're visible to every nested block of the helper body.
            int helperBodyStartPosition = Builder.Length;

            // Generate body
            GenerateCodeInternal();

            // Inject hoisted sub-word LEA `var v_X : i32;` declarations at the helper
            // body root so cross-block uses (nested if/else/loop in WGSL) can resolve.
            if (_helperDeferredVarDeclarations.Count > 0)
            {
                var deferred = string.Join(Environment.NewLine, _helperDeferredVarDeclarations) + Environment.NewLine;
                Builder.Insert(helperBodyStartPosition, deferred);
            }

            PopIndent();
            Builder.AppendLine("}");
        }

        /// <summary>
        /// Pre-scans helper parameters for sub-word ArrayView element types.
        /// Mirrors WGSLKernelFunctionGenerator.PreScanEmulatedParameters but
        /// for non-kernel methods: helpers have no KernelParamOffset and no
        /// EntryPoint.Parameters list, so we read the .NET parameter types
        /// off Method.Source.GetParameters() to detect signed-vs-unsigned.
        /// (rc.16 fn-def-codegen Bug D phase 2, 2026-05-05).
        /// </summary>
        private void ScanHelperSubWordParams()
        {
            System.Reflection.ParameterInfo[]? clrParams = null;
            try
            {
                clrParams = Method.Source?.GetParameters();
            }
            catch
            {
                // Defensive: some IR methods may have no .NET source (synthetic).
                clrParams = null;
            }

            foreach (var param in Method.Parameters)
            {
                if (param.ParameterType is not ViewType viewType) continue;
                if (viewType.ElementType is not PrimitiveType prim) continue;

                Type? clrElement = null;
                if (clrParams != null && param.Index >= 0 && param.Index < clrParams.Length)
                {
                    var clrParamType = clrParams[param.Index].ParameterType;
                    // Strip ByRef wrapper for `ref ArrayView<T>` style params.
                    if (clrParamType.IsByRef) clrParamType = clrParamType.GetElementType()!;
                    if (clrParamType.IsGenericType)
                    {
                        var genArgs = clrParamType.GetGenericArguments();
                        if (genArgs.Length > 0) clrElement = genArgs[0];
                    }
                }

                switch (prim.BasicValueType)
                {
                    case BasicValueType.Int8:
                        _subWordParams[param.Index] = 1;
                        if (clrElement == typeof(byte))
                            _subWordUnsignedParams.Add(param.Index);
                        break;
                    case BasicValueType.Int16:
                        _subWordParams[param.Index] = 2;
                        if (clrElement == typeof(ushort))
                            _subWordUnsignedParams.Add(param.Index);
                        break;
                    case BasicValueType.Float16:
                        if (!Backend.HasShaderF16)
                        {
                            _subWordParams[param.Index] = 2;
                            _subWordFloat16Params.Add(param.Index);
                        }
                        break;
                    case BasicValueType.BFloat16:
                        // bfloat16 is always emulated (no native WGSL bf16) - 2-byte sub-word.
                        _subWordParams[param.Index] = 2;
                        _subWordBFloat16Params.Add(param.Index);
                        break;
                }
            }
        }

        /// <summary>
        /// LoadElementAddress on a helper's ArrayView parameter. The base class
        /// emits `&amp;source[idx]` which is wrong here because the helper's
        /// `source` is `ptr&lt;storage, array&lt;...&gt;, read_write&gt;` (from the
        /// fn signature) - WGSL requires `(*ptr)[idx]`. For sub-word element
        /// types we instead store the i32 offset and defer the atomic-load + shift +
        /// mask to GenerateCode(Load), mirroring the kernel's sub-word path.
        /// (rc.16 Bug D phase 2, 2026-05-05.)
        /// </summary>
        public override void GenerateCode(LoadElementAddress value)
        {
            if (value.Source.Resolve() is Parameter param
                && param.ParameterType is ViewType)
            {
                var target = Load(value);
                var offset = Load(value.Offset);

                // When i64 emulation is active, Int64 indices become emu_i64 (vec2<u32>)
                // which cannot be used as WGSL array indices NOR assigned to `var i32`.
                // Wrap with i64_to_i32() — mirrors the kernel-side LEA path
                // (WGSLKernelFunctionGenerator.GenerateCode(LoadElementAddress) line 4445).
                // Surfaced 2026-05-05 by Tuvok's AV1 walker on local.6: helper read
                // `consts[cdfBase + N]` where `cdfBase` is `long`, so the offset arrived
                // as emu_i64. The unwrapped `var v_X : i32 = v_Y;` emit failed Tint
                // validation with "cannot initialize 'var' of type 'i32' with value of
                // type 'vec2<u32>'".
                bool offsetIsEmulatedI64 = Backend.EnableI64Emulation
                    && value.Offset.Resolve().BasicValueType == BasicValueType.Int64;
                string offsetExpr = offsetIsEmulatedI64 ? $"i64_to_i32({offset})" : $"{offset}";

                // Coalesce-aware LEA (local.15+): if this param was bound via the
                // coalesced helper-sig path (1 leader ptr + N offset args per group),
                // the LEA must use `&(*leaderPtr)[offsetArg + idxExpr]` so each member's
                // accesses go through the SHARED leader binding with the per-member
                // offset baked in. Sub-word coalesce uses element-offset semantics —
                // the offsetArg is the byte/short element-index offset, idxExpr is the
                // element index within that view; subsequent sub-word Load codegen will
                // divide by elemsPerWord to get the u32 word index.
                if (_coalesceParamInfo.TryGetValue(param.Index, out var coalInfo))
                {
                    if (coalInfo.entry.IsSubWord)
                    {
                        // Sub-word: store (offsetArg + element-idx) for the Load chain to
                        // consume. Mirror the helper-side sub-word LEA path's deferred-decl
                        // hoisting for cross-block uses.
                        _subWordLEAVars[target.Name] = param.Index;
                        // Track that this sub-word param uses the coalesced leader binding
                        // (so sub-word Load codegen reads from `(*leaderPtr)` instead of
                        // `(*p_X)`). Map paramIdx -> leaderPtr name in the existing
                        // _paramIndexToLocalVar dict.
                        _paramIndexToLocalVar[param.Index] = coalInfo.leaderPtr;
                        bool helperIsMultiBlock = Method.Blocks.Count > 1;
                        string composedOffset = $"({coalInfo.offsetArg} + {offsetExpr})";
                        if (helperIsMultiBlock && declaredVariables.Add(target.Name))
                        {
                            _helperDeferredVarDeclarations.Add($"    var {target.Name} : i32;");
                            AppendLine($"{target.Name} = {composedOffset};");
                        }
                        else if (helperIsMultiBlock)
                        {
                            AppendLine($"{target.Name} = {composedOffset};");
                        }
                        else
                        {
                            AppendLine($"var {target.Name} : i32 = {composedOffset};");
                        }
                        return;
                    }
                    // Full-word coalesced (i32/u32/f32 via v1): emit a ptr alias
                    // pointing at the leader's element at `offsetArg + idxExpr`.
                    AppendLine($"let {target.Name} = &(*{coalInfo.leaderPtr})[{coalInfo.offsetArg} + {offsetExpr}];");
                    return;
                }

                // Emu-64 (Int64/UInt64/Float64) ArrayView fn-param: raw u32 storage
                // (stride=2). Mirror the kernel-side LEA pattern at
                // `WGSLKernelFunctionGenerator.cs:4789-4794`. Store the u32 base
                // index (= elementIdx * 2) in a fn-scope `let` and emit `&p_X` as
                // the binding alias. Load/Store overrides reconstruct emu_i64 /
                // emu_f64 from two u32 reads at base_idx and base_idx+1.
                // Closes Tuvok L44452 fn-def call-site type mismatch (helper sig
                // is `array<u32>` matching kernel binding; need stride-2 access).
                bool isEmu64 = _emuI64Params.Contains(param.Index)
                    || _emuF64Params.Contains(param.Index);
                if (isEmu64)
                {
                    bool isF64 = _emuF64Params.Contains(param.Index);
                    _emu64LEAVars[target.Name] = (param.Index, isF64);
                    AppendLine($"let {target.Name}_base_idx = i32({offsetExpr}) * 2;");
                    AppendLine($"let {target.Name} = p_{param.Id};");
                    return;
                }

                if (_subWordParams.ContainsKey(param.Index))
                {
                    // Sub-word: store the element offset; Load / Store will compute
                    // word index + bit shift from this i32 at the use site.
                    //
                    // Cross-block fix (Bug D phase 7 follow-up, Tuvok's local.9 walker
                    // `unresolved value 'v_359'` at WGSL L19170, 2026-05-05): if the
                    // helper has multiple IR blocks (i.e. nested if/else/loop in the
                    // emitted WGSL fn body), `var v_X : i32 = ...;` is block-scoped and
                    // a use in a sibling block fails Tint validation. Hoist the var
                    // declaration to the helper fn body root via deferred-decl insertion
                    // and emit only the assignment locally.
                    _subWordLEAVars[target.Name] = param.Index;
                    bool helperIsMultiBlock = Method.Blocks.Count > 1;
                    if (helperIsMultiBlock && declaredVariables.Add(target.Name))
                    {
                        _helperDeferredVarDeclarations.Add($"    var {target.Name} : i32;");
                        AppendLine($"{target.Name} = {offsetExpr};");
                    }
                    else if (helperIsMultiBlock)
                    {
                        // Already pre-declared via prior call site; just assign locally.
                        AppendLine($"{target.Name} = {offsetExpr};");
                    }
                    else
                    {
                        // Single-block helper: combined declaration + assignment.
                        AppendLine($"var {target.Name} : i32 = {offsetExpr};");
                    }
                    return;
                }

                // Full-word: helper's local source is a ptr, so we must deref before
                // indexing. WGSL: `&((*ptr)[idx])` produces ptr<storage, T>.
                var sourceVar = Load(value.Source);
                AppendLine($"let {target.Name} = &(*{sourceVar})[{offsetExpr}];");
                return;
            }

            base.GenerateCode(value);
        }

        /// <summary>
        /// LoadFieldAddress on a struct in a helper body. Mirrors the base class
        /// emit (`let v_X = &amp;(*src).fieldN;`) but ALSO registers the dereffed
        /// form so Load/Store in other switch-case blocks can substitute the
        /// expression directly instead of referencing the case-scoped `let`.
        /// (rc.16 Bug D phase 2 follow-up, 2026-05-05.)
        /// </summary>
        public override void GenerateCode(LoadFieldAddress value)
        {
            var target = Load(value);
            var source = Load(value.Source);

            string fieldName = $"field_{value.FieldSpan.Index}";
            if (IsIndexType(value.Source.Type))
            {
                fieldName = value.FieldSpan.Index switch
                {
                    0 => "x",
                    1 => "y",
                    2 => "z",
                    _ => fieldName
                };
            }

            // Build dereffed form (no `&`). Local-alloca sources are var-typed so
            // `source.field` works directly; pointer sources need `(*source).field`.
            string derefExpr = _localAllocaVarNames.Contains(source.Name)
                ? $"{source}.{fieldName}"
                : $"(*{source}).{fieldName}";

            // Register inline-substitution form. Load/Store overrides below check
            // this dictionary FIRST so cross-block uses bypass the case-scoped let.
            _fieldAddressDerefExpr[target.Name] = derefExpr;

            AppendIndent();
            Builder.Append("let ");
            Builder.Append(target.Name);
            Builder.Append(" = &");
            Builder.Append(derefExpr);
            Builder.Append(";");
            Builder.AppendLine();
        }

        /// <summary>
        /// Load on a helper's ArrayView parameter or its sub-word LEA result.
        /// Two cases: (1) Loading the parameter itself ("aliasing" the view) emits
        /// a `let` that copies the local pointer; (2) Loading from a sub-word
        /// LEA target emits the atomic-load + shift + mask + sign-extend chain
        /// using the helper's local pointer name as the binding.
        /// Mirrors WGSLKernelFunctionGenerator.GenerateCode(Load) sub-word path
        /// at lines 4560-4618.
        /// </summary>
        public override void GenerateCode(global::ILGPU.IR.Values.Load loadVal)
        {
            // Case (1): Load(viewParam) returns a pointer alias.
            if (loadVal.Source.Resolve() is Parameter param
                && param.Type is ViewType)
            {
                var target = Load(loadVal);
                var sourceVar = Load(loadVal.Source);
                // The helper's source is already a ptr<storage, ...>; aliasing it
                // is just `let target = sourceVar;`. Downstream code that does
                // `*target` or `(*target)[idx]` works because target IS the ptr.
                AppendLine($"let {target.Name} = {sourceVar};");
                return;
            }

            var source = Load(loadVal.Source);

            // Cross-block field address: substitute the dereffed expression so we
            // never reference a case-scoped `let` from another case.
            if (_fieldAddressDerefExpr.TryGetValue(source.ToString(), out var fieldDeref))
            {
                var target = Load(loadVal);
                Declare(target);
                AppendLine($"{target.Name} = {fieldDeref};");
                return;
            }

            // Emu-64 LEA result: read two u32 slots and reconstruct.
            // Mirror the kernel-side path at WGSLKernelFunctionGenerator
            // GenerateCode(Load) line 4995-5020. The LEA stored the u32 base
            // index in `{source}_base_idx`; helper-local binding alias is
            // `source` (ptr<storage, array<u32>, read_write>).
            if (_emu64LEAVars.TryGetValue(source.ToString(), out var emuInfo))
            {
                var target = Load(loadVal);
                string baseIdxVar = $"{source}_base_idx";
                string lo = $"(*{source})[u32({baseIdxVar})]";
                string hi = $"(*{source})[u32({baseIdxVar}) + 1u]";
                if (emuInfo.isF64)
                {
                    Declare(target);
                    AppendLine($"{target} = f64_from_ieee754_bits({lo}, {hi});");
                }
                else
                {
                    Declare(target);
                    AppendLine($"{target} = emu_i64({lo}, {hi});");
                }
                return;
            }

            // Case (2): Sub-word LEA result -> extract from packed atomic<u32>.
            if (_subWordLEAVars.TryGetValue(source.ToString(), out var subWordParamIdx))
            {
                var target = Load(loadVal);
                var idx = source.ToString();
                var elemSize = _subWordParams[subWordParamIdx];
                // Helper-local binding form: `(*v_X)` where v_X is the helper's
                // ptr alias for the param. The kernel uses `paramN` (a global
                // var<storage, ...>); here we deref the local ptr.
                string subWordBinding = _paramIndexToLocalVar.TryGetValue(subWordParamIdx, out var local)
                    ? $"(*{local})"
                    : $"param{subWordParamIdx}"; // defensive fallback (shouldn't happen)

                string extractExpr;
                if (elemSize == 1)
                {
                    var wordIdx = $"(u32({idx}) / 4u)";
                    var shift = $"((u32({idx}) % 4u) * 8u)";
                    var rawByte = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFu)";
                    if (_subWordUnsignedParams.Contains(subWordParamIdx))
                        extractExpr = $"i32({rawByte})";                                    // byte: zero-extend
                    else
                        extractExpr = $"select(i32({rawByte}), (i32({rawByte}) - 256), ({rawByte}) >= 128u)"; // sbyte: sign-extend
                }
                else if (_subWordFloat16Params.Contains(subWordParamIdx))
                {
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    var rawExpr = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFFFu)";
                    extractExpr = $"_f16_to_f32({rawExpr})";
                }
                else if (_subWordBFloat16Params.Contains(subWordParamIdx))
                {
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    var rawExpr = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFFFu)";
                    extractExpr = $"_bf16_to_f32({rawExpr})";
                }
                else if (_subWordUnsignedParams.Contains(subWordParamIdx))
                {
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    var rawExpr = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFFFu)";
                    extractExpr = $"i32({rawExpr})"; // ushort: zero-extend
                }
                else
                {
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    var rawExpr = $"((u32(atomicLoad(&{subWordBinding}[{wordIdx}])) >> {shift}) & 0xFFFFu)";
                    extractExpr = $"select(i32({rawExpr}), (i32({rawExpr}) - 65536), ({rawExpr}) >= 32768u)";
                }

                Declare(target);
                AppendLine($"{target.Name} = {extractExpr};");
                return;
            }

            base.GenerateCode(loadVal);
        }

        /// <summary>
        /// Store into a helper's sub-word LEA target. Atomic RMW on the packed
        /// u32 word: clear the target bit-range with atomicAnd, set the new value
        /// with atomicOr. Mirrors WGSLKernelFunctionGenerator.GenerateCode(Store)
        /// sub-word path at lines 4747-4791.
        /// </summary>
        public override void GenerateCode(global::ILGPU.IR.Values.Store storeVal)
        {
            var address = Load(storeVal.Target);

            // Cross-block field address: substitute the dereffed expression on the
            // lhs so we never write through a case-scoped `let` from another case.
            if (_fieldAddressDerefExpr.TryGetValue(address.ToString(), out var fieldDeref))
            {
                var val = Load(storeVal.Value);
                AppendLine($"{fieldDeref} = {val};");
                return;
            }

            // Emu-64 LEA target: split emu_i64 / emu_f64 into two u32 stores.
            // Mirrors WGSLKernelFunctionGenerator.GenerateCode(Store) emu_64
            // path at lines 5167-5200.
            if (_emu64LEAVars.TryGetValue(address.ToString(), out var emuStoreInfo))
            {
                var val = Load(storeVal.Value);
                string baseIdxVar = $"{address}_base_idx";
                if (emuStoreInfo.isF64)
                {
                    AppendLine($"let _bits_{address} = f64_to_ieee754_bits({val});");
                    AppendLine($"(*{address})[u32({baseIdxVar})] = _bits_{address}.x;");
                    AppendLine($"(*{address})[u32({baseIdxVar}) + 1u] = _bits_{address}.y;");
                }
                else
                {
                    AppendLine($"(*{address})[u32({baseIdxVar})] = {val}.x;");
                    AppendLine($"(*{address})[u32({baseIdxVar}) + 1u] = {val}.y;");
                }
                return;
            }

            if (_subWordLEAVars.TryGetValue(address.ToString(), out var storeParamIdx))
            {
                var val = Load(storeVal.Value);
                var idx = address.ToString();
                var elemSize = _subWordParams[storeParamIdx];
                string subWordBinding = _paramIndexToLocalVar.TryGetValue(storeParamIdx, out var local)
                    ? $"(*{local})"
                    : $"param{storeParamIdx}";

                if (elemSize == 1)
                {
                    var wordIdx = $"(u32({idx}) / 4u)";
                    var shift = $"((u32({idx}) % 4u) * 8u)";
                    AppendLine($"atomicAnd(&{subWordBinding}[{wordIdx}], ~(0xFFu << {shift}));");
                    AppendLine($"atomicOr(&{subWordBinding}[{wordIdx}], ((u32({val}) & 0xFFu) << {shift}));");
                }
                else if (_subWordFloat16Params.Contains(storeParamIdx))
                {
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    AppendLine($"atomicAnd(&{subWordBinding}[{wordIdx}], ~(0xFFFFu << {shift}));");
                    AppendLine($"atomicOr(&{subWordBinding}[{wordIdx}], ((_f32_to_f16({val}) & 0xFFFFu) << {shift}));");
                }
                else if (_subWordBFloat16Params.Contains(storeParamIdx))
                {
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    AppendLine($"atomicAnd(&{subWordBinding}[{wordIdx}], ~(0xFFFFu << {shift}));");
                    AppendLine($"atomicOr(&{subWordBinding}[{wordIdx}], ((_f32_to_bf16({val}) & 0xFFFFu) << {shift}));");
                }
                else // Int16 / UInt16
                {
                    var wordIdx = $"(u32({idx}) / 2u)";
                    var shift = $"((u32({idx}) % 2u) * 16u)";
                    AppendLine($"atomicAnd(&{subWordBinding}[{wordIdx}], ~(0xFFFFu << {shift}));");
                    AppendLine($"atomicOr(&{subWordBinding}[{wordIdx}], ((u32({val}) & 0xFFFFu) << {shift}));");
                }
                return;
            }

            base.GenerateCode(storeVal);
        }

        /// <summary>
        /// AddressSpaceCast on a helper-local alloca. Mirrors
        /// WGSLKernelFunctionGenerator.GenerateCode(AddressSpaceCast): when the
        /// cast source is a function-scope `var` (in _localAllocaVarNames) and
        /// the cast target is a pointer type, register the cast result Variable
        /// with name `&amp;v_source` so call sites that pass it as a `ref`-style
        /// arg pick up the correct pointer expression directly. Without this,
        /// the base class emits `let v_X = v_source;` (a struct copy) and the
        /// downstream fn call gets a value where it expects ptr - WGSL fails
        /// with "type mismatch for argument 1".
        /// (rc.16 Bug D phase 3 follow-up, 2026-05-05.)
        /// </summary>
        public override void GenerateCode(AddressSpaceCast value)
        {
            var source = Load(value.Value);
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
            base.GenerateCode(value);
        }

        /// <summary>
        /// MethodCall from inside a helper body. Without this override the base
        /// class falls to "Unmapped fallback" emitting `target = i32(0);` for any
        /// helper-to-helper call. We mirror WGSLKernelFunctionGenerator's
        /// non-inline branch: methods with implementation that aren't intrinsic
        /// or external get a real WGSL fn call to the standalone fn definition.
        /// (rc.16 Bug D phase 3, 2026-05-05.)
        ///
        /// The Inline-into-helper case (target is in HelperMethods) is intentionally
        /// not handled here yet - that requires the kernel's full inline machinery
        /// (200+ lines: alloca setup, structured CFG, varCounter offset, scoped
        /// declaredVariables snapshot). Codec helpers in the rc.16 test set are
        /// all NoInlining so this case does not fire. If a future test mixes Inline
        /// helpers called from NoInlining helpers, extend this branch.
        /// </summary>
        public override void GenerateCode(MethodCall methodCall)
        {
            var targetMethod = methodCall.Target;

            // Inline-into-helper case: not supported (yet). Fall through to base
            // and let the "Unmapped fallback" diagnostic surface; if the IR places
            // an Inline helper inside a NoInlining helper, we want a clear signal,
            // not silently-wrong code.
            if (_args.HelperMethods.ContainsKey(targetMethod))
            {
                base.GenerateCode(methodCall);
                return;
            }

            // Real fn call: target has a body and isn't intrinsic / external,
            // matching the kernel's EmitNonInlinedMethodCall branch.
            if (targetMethod.HasImplementation
                && !targetMethod.HasFlags(MethodFlags.External)
                && !targetMethod.HasFlags(MethodFlags.Intrinsic))
            {
                var fnName = GetMethodName(targetMethod);

                var argStrs = new List<string>(methodCall.Count);
                for (int i = 0; i < methodCall.Count; i++)
                {
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
                return;
            }

            base.GenerateCode(methodCall);
        }

        #endregion
    }
}
