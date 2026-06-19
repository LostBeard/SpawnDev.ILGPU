// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: WGSLCodeGenerator.cs
//
// Base WGSL code generator implementing IBackendCodeGenerator for WebGPU backend.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Backends.EntryPoints;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;
using System.Reflection;
using System.Text;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// Base class for WGSL code generation. Generates WGSL compute shader source
    /// from ILGPU IR values by implementing the IBackendCodeGenerator interface.
    /// </summary>
    public abstract partial class WGSLCodeGenerator : IBackendCodeGenerator<StringBuilder>
    {
        #region Pre-compiled Regex Patterns

        // Let-to-var hoisting pattern for switch/case block scope fix
        private static readonly System.Text.RegularExpressions.Regex s_letHoistPattern =
            new(@"^(\s*)let\s+(v_\d+)\s*=\s*(.+);",
                System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Checks if string is all word characters (letters, digits, underscore). Empty returns false.</summary>
        protected static bool IsAllWordChars(string s)
        {
            if (s.Length == 0) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return true;
        }

        /// <summary>Checks if string matches pattern &amp;\w+ (ampersand followed by word chars only).</summary>
        protected static bool IsAmpersandWordChars(string s)
        {
            return s.Length > 1 && s[0] == '&' && IsAllWordChars(s.AsSpan(1));
        }

        private static bool IsAllWordChars(ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return s.Length > 0;
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Generation arguments for WGSL code generator construction.
        /// </summary>
        public readonly struct GeneratorArgs
        {
            /// <summary>
            /// Creates new generator args.
            /// </summary>
            public GeneratorArgs(
                WebGPUBackend backend,
                WGSLTypeGenerator typeGenerator,
                EntryPoint entryPoint,
                AllocaKindInformation sharedAllocations,
                AllocaKindInformation dynamicSharedAllocations,
                int? maxWorkgroupSize = null)
            {
                Backend = backend;
                TypeGenerator = typeGenerator;
                EntryPoint = entryPoint;
                SharedAllocations = sharedAllocations;
                DynamicSharedAllocations = dynamicSharedAllocations;
                MaxWorkgroupSize = maxWorkgroupSize;
                DynamicSharedOverrides = new List<DynamicSharedOverrideInfo>();
                ScalarPackingManifest = new List<ScalarPackingEntry>();
                HelperMethods = new Dictionary<Method, Allocas>();
                NonInlineMethods = new List<Method>();

                // Create SharedMemoryResolver with deterministic names from backend context.
                // This MUST happen before helper function generators run (which is
                // before the kernel generator), so both can use the same names.
                SharedMemoryResolver = new SharedMemoryResolver(sharedAllocations, typeGenerator);
            }

            /// <summary>The parent backend.</summary>
            public WebGPUBackend Backend { get; }

            /// <summary>The type generator.</summary>
            public WGSLTypeGenerator TypeGenerator { get; }

            /// <summary>The kernel entry point.</summary>
            public EntryPoint EntryPoint { get; }

            /// <summary>Shared memory allocations.</summary>
            public AllocaKindInformation SharedAllocations { get; }

            /// <summary>Dynamic shared memory allocations.</summary>
            public AllocaKindInformation DynamicSharedAllocations { get; }

            /// <summary>
            /// The maximum workgroup size for this kernel, derived from KernelSpecialization.
            /// When set, this overrides the hardcoded default workgroup sizes (64 for 1D, etc.)
            /// to match the actual group dimension used during kernel compilation.
            /// This is critical for explicitly grouped kernels where SpecializedValue<int>(groupDim)
            /// triggers recompilation with a specific group size that must match @workgroup_size.
            /// </summary>
            public int? MaxWorkgroupSize { get; }

            /// <summary>
            /// Populated by the kernel code generator during GenerateHeader() with
            /// the WGSL override constant names for dynamic shared memory.
            /// </summary>
            public List<DynamicSharedOverrideInfo> DynamicSharedOverrides { get; }

            /// <summary>
            /// Populated by the kernel code generator during GenerateHeader() with
            /// the scalar parameter packing layout for this kernel.
            /// </summary>
            public List<ScalarPackingEntry> ScalarPackingManifest { get; }

            /// <summary>
            /// Populated by the kernel code generator at the end of GenerateHeader() with
            /// the total number of @group(0) bindings emitted. Used at runtime to validate
            /// that the bind group entry count matches the WGSL layout. List with 1 element (mutable in readonly struct).
            /// </summary>
            public List<int> ExpectedBindingCountHolder { get; } = new() { 0 };

            /// <summary>
            /// Spinlock-companion-buffer keys for i64/f64 Min/Max/Exchange atomics.
            /// Key: (ParamIdx, FieldIdx). FieldIdx=-1 for direct view params;
            /// FieldIdx>=0 for body-struct view fields. Populated during ScanForAtomicUsage,
            /// used by dispatch to allocate lock buffers (one per key, in OrderBy order).
            /// </summary>
            public HashSet<(int ParamIdx, int FieldIdx)> I64SpinlockParamIndices { get; } = new();

            /// <summary>
            /// Populated by the kernel code generator during GenerateHeader() with the
            /// coalesce-group manifest (body-struct ArrayView fields that share a binding).
            /// Empty when no coalescing was needed.
            /// </summary>
            public List<CoalesceGroupEntry> CoalesceManifest { get; } = new();

            /// <summary>
            /// Manages shared memory allocation, matching, and WGSL emission.
            /// Replaces the previous SharedMemoryVarNames dictionary with a proper
            /// encapsulation of the two-pass matching logic.
            /// </summary>
            public SharedMemoryResolver SharedMemoryResolver { get; }

            /// <summary>
            /// Maps shared memory Alloca IR values to their module-scope WGSL variable names
            /// and full declarations. Delegates to SharedMemoryResolver.Entries for backward
            /// compatibility during the transition.
            /// </summary>
            public Dictionary<Value, (string Name, string Declaration)> SharedMemoryVarNames =>
                SharedMemoryResolver.Entries;

            /// <summary>
            /// Maps helper methods to their allocas for inlining at call sites.
            /// Populated by WebGPUBackend.CreateFunctionCodeGenerator() for methods
            /// flagged Inline. Methods without Inline (e.g. NoInlining) go into
            /// NonInlineMethods instead.
            /// </summary>
            public Dictionary<Method, Allocas> HelperMethods { get; }

            /// <summary>
            /// Methods emitted as standalone WGSL `fn` definitions (NoInlining or
            /// Inliner-rejected). Populated by WebGPUBackend.CreateFunctionCodeGenerator().
            /// Used by SetEmulationFlags / ScanForSubgroupAndBroadcastUsage so they see
            /// 64-bit ops, barriers, and broadcasts that live in non-inlined helpers
            /// (otherwise their emulation library / subgroup feature flags miss them
            /// and the emitted WGSL calls i64_ge / i64_eq with no definitions —
            /// rc.16 fn-def codegen Bug D follow-up, 2026-05-05).
            /// </summary>
            public List<Method> NonInlineMethods { get; }

            /// <summary>
            /// Tracks emulated i64/u64 constant values used during body generation.
            /// Maps (lo, hi) u32 pairs to module-scope const names (e.g., _c_i64_0).
            /// Populated by GenerateCode(PrimitiveValue), consumed by GenerateHeader()
            /// to emit const declarations. Shared across kernel and helper generators.
            /// </summary>
            public Dictionary<(uint Lo, uint Hi), string> I64Constants { get; } = new();

            /// <summary>
            /// Per-helper-method, per-parameter-index OBSERVED address space at call sites.
            /// Populated by WGSLKernelFunctionGenerator's pre-body scan of MethodCall nodes
            /// (kernel constructor, runs sequentially before parallel GenerateCode). Consumed
            /// by WGSLFunctionGenerator.GetWgslViewParamType to override the WGSL storage
            /// class for ViewType params — necessary because ILGPU's IR doesn't propagate
            /// `AddressSpace.Shared` into helper parameter types at our optimization level.
            /// (Bug D phase 7, 2026-05-05.)
            /// </summary>
            public Dictionary<(Method method, int paramIdx), MemoryAddressSpace> HelperParamAddressSpaces { get; } = new();

            /// <summary>
            /// Per-helper-method, per-parameter-index COALESCE membership info. Populated by
            /// WGSLKernelFunctionGenerator's pre-body scan of MethodCall nodes when an arg
            /// resolves to a kernel parameter that's a member of a direct-param coalesce
            /// group (`_directParamCoalesceMembership`). Consumed by WGSLFunctionGenerator
            /// at signature emit + LEA emit time.
            ///
            /// The fix: when N coalesced ArrayView params are passed positionally to a
            /// NoInlining helper, each "let v_X = &leader_binding;" alias points to the
            /// SAME WGSL ptr — Tint rejects with "invalid aliased pointer argument" or
            /// (more subtly) the helper reads from the same slot N times because the
            /// kernel-side per-member offset isn't passed across the call.
            ///
            /// Fix shape: helper signature collapses N coalesced same-group args into 1
            /// LEADER ptr + N i32 offset args. The kernel call site emits the leader ptr
            /// ONCE and the per-arg offset values per member. The helper's LEA on each
            /// coalesced param uses `&(*leaderPtr)[offsetArg + idxExpr]` instead of
            /// `&(*p_X)[idxExpr]`. (Local.15+ helper-sig coalesce-aware emit, 2026-05-05.)
            /// </summary>
            public Dictionary<(Method method, int paramIdx), HelperParamCoalesceEntry> HelperParamCoalesceInfo { get; } = new();
        }

        /// <summary>
        /// Coalesce metadata for a single helper parameter index. See
        /// <see cref="GeneratorArgs.HelperParamCoalesceInfo"/>.
        /// </summary>
        public class HelperParamCoalesceEntry
        {
            /// <summary>Group key — same for all params that share a coalesced leader binding.</summary>
            public string GroupKey { get; set; } = string.Empty;
            /// <summary>Leader binding name in the kernel (e.g. "param_direct_swi8_coalesced").</summary>
            public string LeaderBindingName { get; set; } = string.Empty;
            /// <summary>Leader binding's WGSL element type (e.g. "atomic&lt;u32&gt;").</summary>
            public string LeaderBindingWgslElementType { get; set; } = string.Empty;
            /// <summary>True when this param is the FIRST member of its group (gets the leader ptr arg).</summary>
            public bool IsLeader { get; set; }
            /// <summary>Per-member offset scalar slot in `_scalar_params`. The kernel call site emits the offset value from this slot.</summary>
            public int OffsetScalarSlot { get; set; }
            /// <summary>True when the coalesced group is sub-word (atomic&lt;u32&gt; backing). Affects Load codegen.</summary>
            public bool IsSubWord { get; set; }
            /// <summary>Sub-word element byte size (1 or 2). Used by Load codegen for atomic shift/mask.</summary>
            public int SubWordElementByteSize { get; set; }
        }

        /// <summary>
        /// Represents a variable in WGSL code.
        /// </summary>
        /// <summary>
        /// Represents a variable in WGSL code.
        /// </summary>
        public class Variable
        {
            public Variable(string name, string type)
            {
                Name = name;
                Type = type;
            }

            public string Name { get; }
            public string Type { get; }

            public override string ToString() => Name;
        }

        #endregion

        #region Instance

        protected int varCounter = 0;
        protected int labelCounter = 0;
        protected readonly Dictionary<Value, Variable> valueVariables = new();
        private readonly Dictionary<BasicBlock, string> blockLabels = new();
        protected readonly GeneratorArgs _baseArgs;

        // Local array support (NewArray + LoadArrayElementAddress)
        protected readonly Dictionary<string, string> _allocaArrayNames = new();
        protected int _localArrayCounter = 0;

        // Track local alloca variable names (declared as WGSL 'var', NOT pointers).
        // Store/Load/LoadFieldAddress must NOT use pointer dereference (*) on these.
        protected readonly HashSet<string> _localAllocaVarNames = new();

        // Flag to tracking if we are generating code within the state machine loop
        protected bool IsStateMachineActive { get; set; } = false;

        /// <summary>
        /// Set by <see cref="GenerateCode(FloatAsIntCast)"/> or
        /// <see cref="GenerateCode(IntAsFloatCast)"/> when the emulated-Half
        /// round-trip path emits <c>_f32_to_f16</c> or <c>_f16_to_f32</c>.
        /// Read by <c>WGSLKernelFunctionGenerator.GenerateHeader</c> to include
        /// the F16 helpers in the emulation library even when no kernel parameter
        /// was registered as a sub-word Float16 (e.g. kernels that operate on
        /// <c>RadixSortPair&lt;Half, int&gt;</c> see Half values as struct
        /// fields, not direct Half buffer params - but still need the helpers
        /// for correct bit extraction).
        /// </summary>
        protected bool _kernelReferencesF16Helpers { get; set; } = false;
        protected bool _kernelReferencesBF16Helpers { get; set; } = false;
        protected bool _kernelReferencesFP8Helpers { get; set; } = false;
        protected bool _kernelReferencesFP4Helpers { get; set; } = false;

        private StringBuilder prefixBuilder = new StringBuilder();
        private StringBuilder suffixBuilder = new StringBuilder();

        /// <summary>
        /// Constructs a new WGSL code generator.
        /// </summary>
        protected WGSLCodeGenerator(in GeneratorArgs args, Method method, Allocas allocas)
        {
            Backend = args.Backend;
            TypeGenerator = args.TypeGenerator;
            Method = method;
            Allocas = allocas;
            _baseArgs = args;

            Builder = prefixBuilder;
        }

        #endregion

        #region Properties

        /// <summary>The parent backend.</summary>
        public WebGPUBackend Backend { get; }

        /// <summary>The type generator.</summary>
        public WGSLTypeGenerator TypeGenerator { get; }

        /// <summary>The current method being generated.</summary>
        public Method Method { get; }

        /// <summary>All local allocas.</summary>
        public Allocas Allocas { get; }

        /// <summary>The current string builder.</summary>
        public StringBuilder Builder { get; protected set; }

        /// <summary>Builder for variable declarations.</summary>
        public StringBuilder VariableBuilder { get; } = new StringBuilder();

        /// <summary>The current intrinsic provider for code-generation purposes.</summary>
        public global::ILGPU.IR.Intrinsics.IntrinsicImplementationProvider<WGSLIntrinsic.Handler> ImplementationProvider => Backend.IntrinsicProvider;

        /// <summary>Current indentation level.</summary>
        protected int IndentLevel { get; set; } = 0;

        /// <summary>
        /// Returns true if the 'subgroups' WebGPU feature is enabled on the device.
        /// When true, native subgroup intrinsics (subgroupShuffle, subgroup_invocation_id, etc.) are used.
        /// When false, workgroup shared-memory emulation is used instead.
        /// </summary>
        protected bool HasSubgroups => Backend.HasSubgroups;

        /// <summary>
        /// When set to true, the kernel generator emits the group-reduce workgroup variables
        /// (atomic&lt;i32&gt;, atomic&lt;u32&gt;, per-subgroup result array) at module scope.
        /// Set by GenerateGroupAllReduce during body generation so the header knows to include them.
        /// </summary>
        public virtual bool UsesGroupReduce { get; set; } = false;

        /// <summary>
        /// When set to true, the kernel generator emits the warp shuffle emulation shared memory buffer
        /// (_warp_shuffle_buf) at module scope. Set by the Reduce handler when subgroups are unavailable
        /// and a workgroup-level shared-memory reduction is needed.
        /// </summary>
        public virtual bool UsesWarpShuffleEmulation { get; set; } = false;

        #endregion

        #region IBackendCodeGenerator

        /// <summary>
        /// Generates a function declaration header.
        /// </summary>
        public abstract void GenerateHeader(StringBuilder builder);

        /// <summary>
        /// Generates the function body code.
        /// </summary>
        public abstract void GenerateCode();

        /// <summary>
        /// Generates constant declarations (not used for WGSL).
        /// </summary>
        public void GenerateConstants(StringBuilder builder) { }

        /// <summary>
        /// Merges the generated code into the main builder.
        /// </summary>
        public void Merge(StringBuilder builder) => builder.Append(Builder);

        #endregion

        #region Variable Management

        /// <summary>
        /// Allocates a variable for a value.
        /// </summary>
        protected Variable Allocate(Value value)
        {
            var name = $"v_{varCounter++}";
            var type = TypeGenerator[value.Type];
            if (type.StartsWith("ptr<"))
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[DIAG-ALLOCATE] Allocated {name} with ptr type '{type}' for IR value type={value.Type} kind={value.GetType().Name} hash={value.GetHashCode()}");
            var variable = new Variable(name, type);
            valueVariables[value] = variable;
            return variable;
        }

        /// <summary>
        /// Allocates a variable for a type.
        /// </summary>
        protected Variable AllocateType(TypeNode type)
        {
            var name = $"v_{varCounter++}";
            var wgslType = TypeGenerator[type];
            return new Variable(name, wgslType);
        }

        /// <summary>
        /// Gets the variable for a value, allocating if necessary.
        /// </summary>
        public Variable Load(Value value)
        {
            if (!valueVariables.TryGetValue(value, out var variable))
            {
                // CRITICAL: Intercept shared-address-space Alloca values.
                // The Alloca node from backendContext.SharedAllocations (stored in
                // SharedMemoryResolver) may be a DIFFERENT object instance from the one
                // referenced in the function body's IR. We detect shared Alloca values
                // by type and redirect them to the pre-registered module-scope variable.
                if (value is Alloca alloca && alloca.AddressSpace == MemoryAddressSpace.Shared)
                {
                    if (_baseArgs.SharedMemoryResolver.TryResolve(
                        alloca,
                        (name, allocaValue) => valueVariables.Any(kv => kv.Value.Name == name && kv.Key != allocaValue),
                        out var resolvedName))
                    {
                        var wgslType = TypeGenerator[value.Type];
                        var sharedVar = new Variable(resolvedName, wgslType);
                        valueVariables[value] = sharedVar;
                        return sharedVar;
                    }
                }
                variable = Allocate(value);
            }
            return variable;
        }

        public Variable LoadIntrinsicValue(Value value) => Load(value);

        /// <summary>
        /// Binds a value to a variable.
        /// </summary>
        protected void Bind(Value value, Variable variable)
        {
            valueVariables[value] = variable;
        }

        protected readonly HashSet<string> declaredVariables = new();

        /// <summary>
        /// Declares a variable in the current scope.
        /// </summary>
        protected virtual void Declare(Variable variable)
        {
            if (declaredVariables.Contains(variable.Name)) return;
            declaredVariables.Add(variable.Name);

            // WGSL: pointer types (ptr<workgroup, ...>, ptr<storage, ...>, ptr<function, ...>)
            // are not constructible and cannot be declared with 'var'. Skip declaration — these
            // are bound via 'let' at the point of assignment or come from function parameters.
            if (variable.Type.StartsWith("ptr<"))
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[DIAG-DECLARE] Skipping ptr type declaration for {variable.Name} : {variable.Type}");
                return;
            }

            if (IsStateMachineActive)
            {
                // When in state machine mode, defer declarations to function scope
                // instead of emitting them inside case blocks where they'd be scoped.
                VariableBuilder.Append("    var ");
                VariableBuilder.Append(variable.Name);
                VariableBuilder.Append(" : ");
                VariableBuilder.Append(variable.Type);
                VariableBuilder.AppendLine(";");
            }
            else
            {
                AppendIndent();
                Builder.Append("var ");
                Builder.Append(variable.Name);
                Builder.Append(" : ");
                Builder.Append(variable.Type);
                Builder.AppendLine(";");
            }
        }

        #endregion

        #region Label Management

        /// <summary>
        /// Declares a new label for a block.
        /// </summary>
        protected string DeclareLabel()
        {
            return $"L_{Method.Id}_{labelCounter++}";
        }

        /// <summary>
        /// Gets or creates a label for a block.
        /// </summary>
        protected string GetBlockLabel(BasicBlock block)
        {
            if (!blockLabels.TryGetValue(block, out var label))
            {
                label = DeclareLabel();
                blockLabels[block] = label;
            }
            return label;
        }

        /// <summary>
        /// Marks a label in the output.
        /// </summary>
        protected void MarkLabel(string label)
        {
            // WGSL doesn't have labels like C, use block IDs in switch
        }

        #endregion

        #region Code Emission Helpers

        /// <summary>
        /// Appends indentation to the builder.
        /// </summary>
        protected void AppendIndent()
        {
            for (int i = 0; i < IndentLevel; i++)
                Builder.Append("    ");
        }

        /// <summary>
        /// Increases indentation.
        /// </summary>
        protected void PushIndent() => IndentLevel++;

        /// <summary>
        /// Decreases indentation.
        /// </summary>
        protected void PopIndent() => IndentLevel--;

        /// <summary>
        /// Appends a line with indentation.
        /// </summary>
        protected void AppendLine(string line)
        {
            AppendIndent();
            Builder.AppendLine(line);
        }

        /// <summary>
        /// Appends a line without indentation.
        /// </summary>
        protected void AppendLineRaw(string line)
        {
            Builder.AppendLine(line);
        }

        /// <summary>
        /// Begins a function body.
        /// </summary>
        protected void BeginFunctionBody()
        {
            Builder.AppendLine("{");
            PushIndent();
        }

        /// <summary>
        /// Finishes a function body.
        /// </summary>
        protected void FinishFunctionBody()
        {
            PopIndent();
            Builder.AppendLine("}");
        }

        #endregion

        #region IR Traversal

        /// <summary>
        /// Generates code for all blocks in the method.
        /// </summary>
        protected void GenerateCodeInternal()
        {
            var blocks = Method.Blocks;

            // Setup local allocations
            SetupAllocations(Allocas.LocalAllocations, MemoryAddressSpace.Local);

            bool hasReturnValue = !Method.ReturnType.IsVoidType;
            string returnType = hasReturnValue ? TypeGenerator[Method.ReturnType] : "void";

            // Simple case: single block, no control flow
            if (blocks.Count == 1)
            {
                IsStateMachineActive = false;
                var theBlock = blocks.First();
                foreach (var valueEntry in theBlock)
                {
                    GenerateCodeFor(valueEntry.Value);
                }
                // Emit the block's terminator (e.g. ReturnTerminator). The
                // BasicBlock enumerator yields only the value list and does
                // NOT include the terminator (BasicBlock.cs:241 iterates
                // basicBlock.values; Terminator is stored separately). For
                // single-block methods we must emit it explicitly here, or
                // WGSL falls off the end of the function and a non-void
                // function silently returns 0 (the default). The multi-
                // block path below is safe because it has its own
                // `return _ilgpu_return_val;` safety net at function end.
                if (theBlock.Terminator != null)
                    GenerateCodeFor(theBlock.Terminator);
                return;
            }

            // Multiple blocks: use loop/switch pattern
            IsStateMachineActive = true;

            // Declare return variable if needed
            if (hasReturnValue)
            {
                // Initialize to zero/default to avoid WGSL "variable used before assignment" errors
                // though logic should ensure assignment before exit.
                string zeroVal = returnType.StartsWith("bool") ? "false" : (returnType.StartsWith("f") ? "0.0" : "0");
                if (returnType.StartsWith("vec")) zeroVal = $"{returnType}()"; // zero init vector
                // emu_i64/emu_u64/emu_f64 are type aliases for vec types — use the alias as constructor
                // which automatically matches the correct component count (vec2 or vec4 depending on mode)
                if (returnType == "emu_i64" || returnType == "emu_u64" || returnType == "emu_f64") zeroVal = $"{returnType}()";

                AppendLine($"var _ilgpu_return_val : {returnType} = {zeroVal};");
            }

            // Save position before loop - deferred variable declarations will be inserted here
            int deferredInsertPosition = Builder.Length;

            AppendLine("var current_block : i32 = 0;");
            AppendLine("loop {");
            PushIndent();
            AppendLine("switch (current_block) {");
            PushIndent();

            int blockIndex = 0;
            foreach (var block in blocks)
            {
                AppendLine($"case {blockIndex}: {{");
                PushIndent();

                foreach (var valueEntry in block)
                {
                    GenerateCodeFor(valueEntry.Value);
                }

                PopIndent();
                AppendLine("}");
                blockIndex++;
            }

            AppendLine("default: { break; }");
            PopIndent();
            AppendLine("}"); // end switch

            AppendLine("if (current_block == -1) { break; }");

            PopIndent();
            AppendLine("}"); // end loop

            // CRITICAL: Post-process to hoist ALL variable declarations to function scope.
            // Some code generators bypass Declare() and write "let v_N = expr;" directly.
            // In WGSL, 'let' inside a 'case' block is scoped to that block, causing
            // "unresolved value" errors when that variable is used in another block.
            // We convert these to assignments and add var declarations at function scope.
            {
                string loopCode = Builder.ToString(deferredInsertPosition, Builder.Length - deferredInsertPosition);
                var hoistedVars = new Dictionary<string, string>(); // name -> inferred type
                string processed = s_letHoistPattern.Replace(loopCode, match =>
                {
                    string indent = match.Groups[1].Value;
                    string varName = match.Groups[2].Value;
                    string expr = match.Groups[3].Value;

                    // Infer type from the expression for the var declaration
                    string inferredType = "bool"; // default for comparisons
                    // Pointer aliases — expressions starting with '&' create references
                    // that can't be declared as 'var'. Handle based on complexity:
                    // - Simple refs (&identifier): hoist to function scope
                    // - Complex refs (&identifier[index]): keep in block (index may be block-local)
                    if (expr.TrimStart().StartsWith("&"))
                    {
                        var trimmed = expr.TrimStart();
                        if (IsAmpersandWordChars(trimmed))
                        {
                            VariableBuilder.AppendLine($"    let {varName} = {trimmed};");
                            return ""; // Remove from case block; hoisted to function scope
                        }
                        // Complex pointer ref — keep as 'let' in the block
                        return match.Value;
                    }
                    else if (expr.Contains("f64_from_f32") || expr.Contains("f64_add") || expr.Contains("f64_sub") || expr.Contains("f64_mul") || expr.Contains("f64_div") ||
                             expr.Contains("f64_neg") || expr.Contains("f64_from_ieee754") || expr.Contains("emu_f64(") ||
                             expr.Contains("f64_abs") || expr.Contains("f64_min") || expr.Contains("f64_max"))
                    {
                        inferredType = "emu_f64"; // vec2<f32> alias
                    }
                    else if (expr.Contains("i64_from_i32") || expr.Contains("u64_from_u32") ||
                             expr.Contains("i64_add") || expr.Contains("i64_sub") || expr.Contains("i64_mul") ||
                             expr.Contains("u64_mul") || expr.Contains("i64_neg") || expr.Contains("i64_abs") ||
                             expr.Contains("i64_and") || expr.Contains("i64_or") || expr.Contains("i64_xor") ||
                             expr.Contains("i64_shl") || expr.Contains("i64_shr") || expr.Contains("u64_shr") ||
                             expr.Contains("i64_not") || expr.Contains("emu_i64(") || expr.Contains("emu_u64("))
                    {
                        inferredType = "emu_i64"; // vec2<u32> alias
                    }
                    else if (expr.Contains("f64_to_f32") || expr.Contains("f32("))
                    {
                        inferredType = "f32";
                    }
                    else if (expr.Contains("i64_to_i32"))
                    {
                        inferredType = "i32";
                    }
                    else if (expr.Contains("u64_to_u32"))
                    {
                        inferredType = "u32";
                    }
                    else if (expr.Contains("i32("))
                    {
                        inferredType = "i32";
                    }
                    else if (expr.Contains("u32("))
                    {
                        inferredType = "u32";
                    }
                    else
                    {
                        // Try to look up type from valueVariables
                        foreach (var kvp in valueVariables)
                        {
                            if (kvp.Value.Name == varName)
                            {
                                inferredType = kvp.Value.Type;
                                break;
                            }
                        }
                    }

                    // WGSL: pointer types are not constructible and can't be declared as 'var'.
                    // Hoist simple identifier pointers; keep complex expressions in block.
                    if (inferredType.StartsWith("ptr<"))
                    {
                        bool isSimpleIdent = IsAllWordChars(expr.Trim());
                        if (isSimpleIdent)
                        {
                            VariableBuilder.AppendLine($"    let {varName} = {expr.Trim()};");
                            return ""; // Remove from case block; hoisted to function scope
                        }
                        return match.Value; // Keep complex pointer expressions in block
                    }

                    if (!hoistedVars.ContainsKey(varName))
                    {
                        hoistedVars[varName] = inferredType;
                    }

                    // Convert "let v_N = expr;" to "v_N = expr;" (assignment only)
                    return $"{indent}{varName} = {expr};";
                });

                // SECOND PASS: substitute uses of complex-pointer-LEA `let` bindings.
                //
                // The first pass keeps `let v_X = &(*v_Y)[idx];` as block-scoped because the
                // index might reference a block-local variable. But when v_X is dereferenced
                // in a sibling case (`v_R = *v_X;` or `*v_X = v_W;`), Tint reports
                // "unresolved value 'v_X'" — the let died with its case scope.
                //
                // Fix: text-substitute `*v_X` with `(*v_Y)[idx]` everywhere in the helper
                // body, then drop the original let. The substitution is sound because:
                //   - v_Y is either a fn parameter (always visible) or a function-scope alias.
                //   - idx references function-scope hoisted vars (already in VariableBuilder).
                // If a future case introduces an idx that references a block-local var, the
                // resulting WGSL will fail at compile time with a more specific error and we
                // can refine the fix at that point.
                {
                    var ptrLetPattern = new System.Text.RegularExpressions.Regex(
                        @"^\s*let\s+(v_\d+)\s*=\s*&\s*(.+?);\s*$",
                        System.Text.RegularExpressions.RegexOptions.Multiline);

                    var ptrLets = new Dictionary<string, string>(); // varName -> deref expression
                    foreach (System.Text.RegularExpressions.Match m in ptrLetPattern.Matches(processed))
                    {
                        var name = m.Groups[1].Value;
                        var deref = m.Groups[2].Value.Trim();
                        if (!ptrLets.ContainsKey(name))
                            ptrLets[name] = deref;
                    }

                    foreach (var kvp in ptrLets)
                    {
                        string varName = kvp.Key;
                        string derefExpr = kvp.Value;
                        // `*v_X` -> `(<derefExpr>)`. Wrap in parens so subsequent operators
                        // (e.g., `*v_X + 1`) bind correctly. We deliberately leave the
                        // original `let v_X = &expr;` in place — keeps any non-deref use
                        // (e.g., passing v_X as a pointer arg) resolving inside its own case.
                        // Cross-case `*v_X` uses now substitute to the deref expression
                        // directly and no longer depend on the block-scoped let.
                        var derefUsePattern = new System.Text.RegularExpressions.Regex(
                            @"\*" + System.Text.RegularExpressions.Regex.Escape(varName) + @"(?![A-Za-z0-9_])");
                        processed = derefUsePattern.Replace(processed, "(" + derefExpr + ")");
                    }
                }

                // Rebuild the code: replace the loop portion with processed version
                Builder.Remove(deferredInsertPosition, Builder.Length - deferredInsertPosition);
                Builder.Append(processed);

                // Add hoisted var declarations to VariableBuilder
                foreach (var kvp in hoistedVars)
                {
                    VariableBuilder.AppendLine($"    var {kvp.Key} : {kvp.Value};");
                }
            }

            // Insert all deferred variable declarations at the function scope
            // (before the loop). This ensures all variables are accessible from any case block.
            if (VariableBuilder.Length > 0)
            {
                Builder.Insert(deferredInsertPosition, VariableBuilder.ToString());
            }

            // Emit final return
            if (hasReturnValue)
            {
                AppendLine("return _ilgpu_return_val;");
            }
            else
            {
                AppendLine("return;");
            }
        }

        /// <summary>
        /// Gets the block index for control flow.
        /// </summary>
        protected int GetBlockIndex(BasicBlock block)
        {
            int index = 0;
            foreach (var b in Method.Blocks)
            {
                if (b == block) return index;
                index++;
            }
            return -1;
        }

        /// <summary>
        /// Sets up local/shared memory allocations.
        /// </summary>
        protected void SetupAllocations(AllocaKindInformation allocas, MemoryAddressSpace addressSpace)
        {
            foreach (var allocaInfo in allocas)
            {
                var variable = Allocate(allocaInfo.Alloca);
                var elementType = TypeGenerator[allocaInfo.ElementType];

                // Track this variable as a local alloca value type (not a pointer).
                // Store/Load/LoadFieldAddress must NOT dereference these with '*'.
                _localAllocaVarNames.Add(variable.Name);

                // `AllocaKindInformation.IsArray` returns false when ArraySize == 1
                // (it's defined as `ArraySize > 1`), but the IR distinguishes between
                // "scalar local" (Alloca.IsSimpleAllocation) and "array of length 1"
                // (Alloca.IsArrayAllocation with primitive value 1) - the latter is
                // what `LocalMemory.Allocate<T>(1)` produces. Downstream codegen for
                // `LoadArrayElementAddress` always emits `&v[idx]`, which is invalid
                // WGSL when v is a scalar i32 ("cannot index type 'i32'"). Use the
                // IR's IsArrayAllocation to preserve the array shape even at N=1.
                //
                // Additional case: ILGPU IR can scalarize `LocalMemory.Allocate<T>(1)`
                // so that both IsArray and IsArrayAllocation return false (the
                // ArrayLength gets optimized to non-primitive). The signal that
                // distinguishes "user-array, scalarized" from "compiler-scratch
                // scalar" is whether the alloca has a NewView consumer - LocalMemory
                // returns an ArrayView through NewView; compiler-generated scratch
                // (helper out-params, etc.) doesn't. Declare the user-array case
                // as `array<T, 1>` so the existing `&v[idx]` LEA codegen + Store/Load
                // pipelines work without per-call special-casing. Mirrors the
                // GLSL SetupAllocations logic.
                bool hasNewViewConsumer = false;
                foreach (var use in allocaInfo.Alloca.Uses)
                {
                    if (use.Resolve() is global::ILGPU.IR.Values.NewView)
                    {
                        hasNewViewConsumer = true;
                        break;
                    }
                }

                bool emitAsArray =
                    allocaInfo.IsArray
                    || allocaInfo.Alloca.IsArrayAllocation(out _)
                    || hasNewViewConsumer;

                if (emitAsArray)
                {
                    // The WGSL declaration carries the full array type; keep the tracked
                    // variable type in sync so downstream passes that look up variable
                    // types by name (e.g. the missing-declaration RHS-lookup in
                    // WGSLKernelFunctionGenerator) retrieve `array<T, N>` rather than
                    // the default `TypeGenerator[PointerType]` which maps to the element
                    // scalar. Without this, alias values `let v_X = v_alloca;` get
                    // declared as scalar vars and subsequent `&v_X[i]` indexing silently
                    // produces wrong output or undeclared identifiers on WebGL.
                    // Fixes the core 2026-04-24 LocalMemory<int>(64) bug reported by
                    // Tuvok for VP9 iDCT 8x8, and the 2026-04-25
                    // NoHelperOutLikeAllocaTest WGSL "cannot index type 'i32'" bug.
                    var arrayWgslType = $"array<{elementType}, {allocaInfo.ArraySize}>";
                    valueVariables[allocaInfo.Alloca] = new Variable(variable.Name, arrayWgslType);
                    AppendLine($"var {variable.Name} : {arrayWgslType};");
                }
                else
                {
                    AppendLine($"var {variable.Name} : {elementType};");
                }
            }
        }

        #endregion

        #region Value Visitors - Dispatch

        /// <summary>
        /// Generates code for a value using the visitor pattern.
        /// </summary>
        protected void GenerateCodeFor(Value value)
        {
            // Skip void values that have no observable side effects. The
            // exclusions list contains every void-typed IR node whose handler
            // emits code: terminators, side-effecting memory ops, and any
            // method call (void-returning helpers can still write through
            // ref/out pointer params and must be visited so the call site is
            // emitted; this was the rc.16 fn-def-emission gap).
            if (value.Type.IsVoidType &&
                !(value is TerminatorValue) &&
                !(value is Store) &&
                !(value is MemoryBarrier) &&
                !(value is global::ILGPU.IR.Values.Barrier) &&
                !(value is PredicateBarrier) &&
                !(value is MethodCall))
                return;

            // Debug logging to trace instruction generation
            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL] Generating code for: {value.GetType().FullName} - {value}");

            // Handle inaccessible Throw instruction (likely internal)
            // Use Contains("Throw") to be safer
            if (value.GetType().Name.Contains("Throw"))
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL] HANDLING THROW: {value}");
                GenerateThrow(value);
                return;
            }

            // Check for intrinsic implementation
            if (ImplementationProvider.TryGetCodeGenerator(value, out var intrinsicCodeGenerator))
            {
                intrinsicCodeGenerator(Backend, this, value);
                return;
            }

            switch (value)
            {
                // Parameters
                case global::ILGPU.IR.Values.Parameter p:
                    GenerateCode(p);
                    break;

                case global::ILGPU.IR.Values.MethodCall v:
                    GenerateCode(v);
                    break;

                // Arithmetic
                case global::ILGPU.IR.Values.BinaryArithmeticValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.UnaryArithmeticValue v:
                    GenerateUnOp(v);
                    break;
                case global::ILGPU.IR.Values.TernaryArithmeticValue v:
                    GenerateCode(v);
                    break;

                // Comparisons
                case global::ILGPU.IR.Values.CompareValue v:
                    GenerateCode(v);
                    break;

                // Conversions
                case global::ILGPU.IR.Values.ConvertValue v:
                    GenerateCode(v);
                    break;

                // Memory Operations
                case global::ILGPU.IR.Values.Load v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.Store v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.LoadElementAddress v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.LoadFieldAddress v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.Alloca v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.NewArray v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.LoadArrayElementAddress v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.NewView v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.GetViewLength v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.SubViewValue v:
                    GenerateCode(v);
                    break;

                // Constants
                case global::ILGPU.IR.Values.PrimitiveValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.NullValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.StringValue v:
                    GenerateCode(v);
                    break;

                // Phi
                case global::ILGPU.IR.Values.PhiValue v:
                    GenerateCode(v);
                    break;

                // Structures
                case global::ILGPU.IR.Values.StructureValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.GetField v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.SetField v:
                    GenerateCode(v);
                    break;

                // Device Constants
                case global::ILGPU.IR.Values.GridIndexValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.GroupIndexValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.GridDimensionValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.GroupDimensionValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.WarpSizeValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.LaneIdxValue v:
                    GenerateCode(v);
                    break;


                // Control Flow
                case global::ILGPU.IR.Values.ReturnTerminator v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.UnconditionalBranch v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.IfBranch v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.SwitchBranch v:
                    GenerateCode(v);
                    break;



                // Casts
                case global::ILGPU.IR.Values.IntAsPointerCast v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.PointerAsIntCast v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.PointerCast v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.AddressSpaceCast v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.FloatAsIntCast v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.IntAsFloatCast v:
                    GenerateCode(v);
                    break;


                // Atomics & Barriers
                case global::ILGPU.IR.Values.GenericAtomic v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.AtomicCAS v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.MemoryBarrier v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.Barrier v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.PredicateBarrier v:
                    GenerateCode(v);
                    break;

                // Warp Operations
                case global::ILGPU.IR.Values.Broadcast v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.WarpShuffle v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.SubWarpShuffle v:
                    GenerateCode(v);
                    break;

                // Debug/IO
                case global::ILGPU.IR.Values.DebugAssertOperation v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.WriteToOutput v:
                    GenerateCode(v);
                    break;

                // Other
                case global::ILGPU.IR.Values.Predicate v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.DynamicMemoryLengthValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.AlignTo v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.AsAligned v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.LanguageEmitValue v:
                    GenerateCode(v);
                    break;

                default:
                    throw new NotSupportedException(
                        $"WebGPU backend: unhandled IR value type '{value.GetType().Name}' " +
                        $"in method '{Method?.Name ?? "unknown"}'. " +
                        $"This IR node type must be implemented or added as an explicit case.");
            }
        }

        #endregion

        #region Value Visitors - Implementation

        // Parameters
        public virtual void GenerateCode(Parameter parameter)
        {
            // Parameters are typically handled in the kernel generator
        }

        // Arithmetic
        public virtual void GenerateCode(BinaryArithmeticValue value)
        {
            var target = Load(value);
            var left = Load(value.Left);
            var right = Load(value.Right);

            string op = value.Kind switch
            {
                BinaryArithmeticKind.Add => "+",
                BinaryArithmeticKind.Sub => "-",
                BinaryArithmeticKind.Mul => "*",
                BinaryArithmeticKind.Div => "/",
                BinaryArithmeticKind.And => "&",
                BinaryArithmeticKind.Or => "|",
                BinaryArithmeticKind.Xor => "^",
                BinaryArithmeticKind.Shl => "<<",
                BinaryArithmeticKind.Shr => ">>",
                BinaryArithmeticKind.Min => "min",
                BinaryArithmeticKind.Max => "max",
                BinaryArithmeticKind.Rem when TypeGenerator[value.Left.Type].StartsWith("f") => "frem",
                BinaryArithmeticKind.Rem => "%",
                BinaryArithmeticKind.PowF => "pow_func",
                BinaryArithmeticKind.Atan2F => "atan2_func",
                BinaryArithmeticKind.BinaryLogF => "binarylog_func",
                BinaryArithmeticKind.CopySignF => "copysign_func",
                _ => "+"
            };

            Declare(target);

            if (op == "frem")
            {
                AppendLine($"{target} = {left} - {right} * trunc({left} / {right});");
                return;
            }

            // emu_i64 / emu_u64 dispatch — base path is shared with helpers
            // (WGSLFunctionGenerator inherits this). Without this dispatch, a
            // helper that does `long a + long b` would emit `vec2<u32> + vec2<u32>`
            // which is component-wise (no carry propagation) and produces wrong
            // results. Mirrors WGSLKernelFunctionGenerator.GenerateCode at line 5397+
            // for: Add, Sub, Mul, And, Or, Xor — Shl/Shr handled in their own branch
            // below; Min/Max handled in their own branch above.
            // (Bug D follow-up, 2026-05-05.)
            if (Backend.EnableI64Emulation
                && (TypeGenerator[value.Left.Type] == "emu_i64" || TypeGenerator[value.Left.Type] == "emu_u64"
                    || TypeGenerator[value.Right.Type] == "emu_i64" || TypeGenerator[value.Right.Type] == "emu_u64"
                    || value.BasicValueType == BasicValueType.Int64))
            {
                bool isUnsignedI64 = TypeGenerator[value.Left.Type] == "emu_u64"
                    || TypeGenerator[value.Right.Type] == "emu_u64"
                    || value.IsUnsigned;
                string? emulI64Func = value.Kind switch
                {
                    BinaryArithmeticKind.Add => "i64_add",
                    BinaryArithmeticKind.Sub => "i64_sub",
                    BinaryArithmeticKind.Mul => isUnsignedI64 ? "u64_mul" : "i64_mul",
                    BinaryArithmeticKind.And => "i64_and",
                    BinaryArithmeticKind.Or => "i64_or",
                    BinaryArithmeticKind.Xor => "i64_xor",
                    _ => null
                };
                if (emulI64Func != null)
                {
                    AppendLine($"{target} = {emulI64Func}({left}, {right});");
                    return;
                }
                // Other kinds (Shl/Shr/Min/Max/Div/Rem) fall through to their
                // dedicated branches below, which already handle emu_i64 LHS.
            }

            // Handle pointer arithmetic (e.g., &(*ptr)[offset])
            if (value.Kind == BinaryArithmeticKind.Add)
            {
                if (value.Left.Type.IsPointerType)
                {
                    AppendLine($"{target} = &(*{left})[{right}];");
                    return;
                }
                else if (value.Right.Type.IsPointerType)
                {
                    AppendLine($"{target} = &(*{right})[{left}];");
                    return;
                }
            }

            if (value.Kind == BinaryArithmeticKind.Min || value.Kind == BinaryArithmeticKind.Max)
            {
                // Check for emulated 64-bit types first.
                // WGSL's min()/max() are component-wise on vec2, which is WRONG for 64-bit values.
                // Use the emulation functions (i64_max, u64_max, f64_max, etc.) instead.
                //
                // Two detection paths:
                // 1. TypeGenerator lookup → "emu_i64" / "emu_u64" / "emu_f64"
                // 2. BasicValueType fallback → Int64/Float64 when emulation is enabled
                //    This catches cases where IR intermediate values may not map correctly via TypeGenerator.
                var leftType = TypeGenerator[value.Left.Type];
                bool isEmu64 = leftType == "emu_i64" || leftType == "emu_u64" || leftType == "emu_f64";
                
                // Fallback: check BasicValueType when emulation options are enabled
                if (!isEmu64 && Backend.EnableI64Emulation && 
                    (value.BasicValueType == BasicValueType.Int64))
                {
                    isEmu64 = true;
                    leftType = value.IsUnsigned ? "emu_u64" : "emu_i64";
                }
                else if (!isEmu64 && Backend.EnableF64Emulation && 
                    value.BasicValueType == BasicValueType.Float64)
                {
                    isEmu64 = true;
                    leftType = "emu_f64";
                }
                
                if (isEmu64)
                {
                    string prefix = leftType switch
                    {
                        "emu_f64" => "f64",
                        "emu_u64" => "u64",
                        "emu_i64" => "i64",
                        _ => "i64"
                    };
                    string emuOp = value.Kind == BinaryArithmeticKind.Max ? "max" : "min";
                    AppendLine($"{target} = {prefix}_{emuOp}({left}, {right});");
                }
                // For unsigned integer min/max, WGSL's min()/max() are type-overloaded:
                // min(i32, i32) is signed, min(u32, u32) is unsigned.
                // When ILGPU flags the operation as unsigned, bitcast operands to u32
                // and the result back to i32.
                else if (value.IsUnsigned && value.BasicValueType == BasicValueType.Int32)
                {
                    AppendLine($"{target} = bitcast<i32>({op}(bitcast<u32>({left}), bitcast<u32>({right})));");
                }
                else
                {
                    AppendLine($"{target} = {op}({left}, {right});");
                }
            }
            else if (op == "pow_func")
            {
                // WGSL pow(x,y) is undefined for x<0; same negative-base guard as
                // WGSLKernelFunctionGenerator and GLSLCodeGenerator (rc.21 fix).
                AppendLine($"{target} = pow(abs({left}), {right}) * select(1.0, -1.0, ({left} < 0.0) && ((abs({right}) % 2.0) >= 1.0));");
            }
            else if (op == "atan2_func")
            {
                AppendLine($"{target} = atan2({left}, {right});");
            }
            else if (op == "binarylog_func")
            {
                AppendLine($"{target} = log({left}) / log({right});");
            }
            else if (op == "copysign_func")
            {
                // WGSL has no copysign builtin; emulate: sign(right) * abs(left)
                AppendLine($"{target} = sign({right}) * abs({left});");
            }
            else if (value.Kind == BinaryArithmeticKind.Shl || value.Kind == BinaryArithmeticKind.Shr)
            {
                // WGSL shift operators require the right operand to be u32 -
                // `i32 << i32` fails to compile with "no matching overload".
                // Wrap in u32(...). Mirrors the kernel-side handler at
                // WGSLKernelFunctionGenerator line 4778-4779; needed here for
                // the helper fn-def emission path (which uses this base
                // handler unmodified). Triggered by `i * 2` (Tuvok's iDCT
                // 16x16 helper has dozens of shifts in the butterfly).
                //
                // For emu_i64/emu_u64 LHS, WGSL has no `vec2<u32> >> u32` overload —
                // WGSL only supports `T >> u32` where T is i32/u32, or `vecN<T> >> vecN<u32>`.
                // Use the i64/u64 emulation library helpers instead.
                // Surfaced 2026-05-05 by Tuvok's AV1 walker on local.7: helper
                // `cdfBase >> 1` (where cdfBase is `long`) emitted
                // `vec2<u32> >> u32(...)` which Tint rejected. (Bug D follow-up.)
                string leftShType = TypeGenerator[value.Left.Type];
                if (Backend.EnableI64Emulation && (leftShType == "emu_i64" || leftShType == "emu_u64"))
                {
                    string shHelper = value.Kind == BinaryArithmeticKind.Shl
                        ? "i64_shl"
                        : (leftShType == "emu_u64" || value.IsUnsigned ? "u64_shr" : "i64_shr");
                    AppendLine($"{target} = {shHelper}({left}, u32({right}));");
                }
                else
                {
                    AppendLine($"{target} = {left} {op} u32({right});");
                }
            }
            else
            {
                AppendLine($"{target} = {left} {op} {right};");
            }
        }

        public virtual void GenerateCode(UnaryArithmeticValue value)
        {
            GenerateUnOp(value);
        }

        private void GenerateUnOp(UnaryArithmeticValue value)
        {
            var target = Load(value);
            var operand = Load(value.Value);
            Declare(target);

            // Handle emulated emu_i64/emu_u64 negation
            var operandType = TypeGenerator[value.Value.Type];
            if (Backend.EnableI64Emulation && (operandType == "emu_i64" || operandType == "emu_u64") && value.Kind == UnaryArithmeticKind.Neg)
            {
                AppendLine($"{target} = i64_neg({operand});");
                return;
            }

            string result = value.Kind switch
            {
                UnaryArithmeticKind.Neg => $"-{operand}",
                UnaryArithmeticKind.Not => TypeGenerator[value.Value.Type] == "bool" ? $"!{operand}" : $"~{operand}",

                // Math Intrinsics (Float)
                UnaryArithmeticKind.Abs => $"abs({operand})",
                UnaryArithmeticKind.SinF => $"sin({operand})",
                UnaryArithmeticKind.CosF => $"cos({operand})",
                UnaryArithmeticKind.TanF => $"tan({operand})",
                UnaryArithmeticKind.AsinF => $"asin({operand})",
                UnaryArithmeticKind.AcosF => $"acos({operand})",
                UnaryArithmeticKind.AtanF => $"atan({operand})",
                UnaryArithmeticKind.SinhF => $"sinh({operand})",
                UnaryArithmeticKind.CoshF => $"cosh({operand})",
                UnaryArithmeticKind.TanhF => $"tanh({operand})",
                UnaryArithmeticKind.ExpF => $"exp({operand})",
                UnaryArithmeticKind.Exp2F => $"exp2({operand})",
                UnaryArithmeticKind.LogF => $"log({operand})",
                UnaryArithmeticKind.Log2F => $"log2({operand})",
                UnaryArithmeticKind.SqrtF => $"sqrt({operand})",
                UnaryArithmeticKind.RsqrtF => $"1.0 / sqrt({operand})", // Manual inverseSqrt
                UnaryArithmeticKind.RcpF => $"1.0 / {operand}",
                UnaryArithmeticKind.FloorF => $"floor({operand})",
                UnaryArithmeticKind.CeilingF => $"ceil({operand})",
                UnaryArithmeticKind.Log10F => $"(log({operand}) / 2.302585093)",
                // Bit-level NaN/Inf detection for f32 — GPU shader compilers may
                // optimize away `val != val` or flush NaN in comparisons.
                // f32 NaN: exponent=0xFF, mantissa!=0. f32 Inf: exponent=0xFF, mantissa==0.
                // emu_f64 routes to f64_is_nan / f64_is_inf helpers (vec2/vec4
                // comparisons would return vec2<bool>/vec4<bool>, not bool).
                UnaryArithmeticKind.IsNaNF => operandType == "f32"
                    ? $"((bitcast<u32>({operand}) & 0x7F800000u) == 0x7F800000u && (bitcast<u32>({operand}) & 0x007FFFFFu) != 0u)"
                    : operandType == "f16"
                        // Native f16: pack into vec2<f16> for valid bitcast<u32>; bottom 16 bits = f16 bits.
                        // f16 NaN: exponent (bits 14-10) all 1s = 0x7C00, mantissa (bits 9-0) nonzero.
                        ? $"((bitcast<u32>(vec2<f16>({operand}, 0.0h)) & 0x7C00u) == 0x7C00u && (bitcast<u32>(vec2<f16>({operand}, 0.0h)) & 0x03FFu) != 0u)"
                        : operandType == "emu_f64"
                            ? $"f64_is_nan({operand})"
                            : $"({operand} != {operand})",
                UnaryArithmeticKind.IsInfF => operandType == "f32"
                    ? $"((bitcast<u32>({operand}) & 0x7FFFFFFFu) == 0x7F800000u)"
                    : operandType == "f16"
                        // f16 Inf: exponent all 1s AND mantissa zero = bits 14-0 == 0x7C00 (sign-stripped).
                        ? $"((bitcast<u32>(vec2<f16>({operand}, 0.0h)) & 0x7FFFu) == 0x7C00u)"
                        : operandType == "emu_f64"
                            ? $"f64_is_inf({operand})"
                            : $"(abs({operand}) == (1.0 / 0.0))",
                UnaryArithmeticKind.IsFinF => operandType == "f32"
                    ? $"((bitcast<u32>({operand}) & 0x7F800000u) != 0x7F800000u)"
                    : operandType == "f16"
                        // f16 Finite: exponent != all 1s.
                        ? $"((bitcast<u32>(vec2<f16>({operand}, 0.0h)) & 0x7C00u) != 0x7C00u)"
                        : operandType == "emu_f64"
                            ? $"(!f64_is_nan({operand}) && !f64_is_inf({operand}))"
                            : $"(({operand} == {operand}) && (abs({operand}) != (1.0 / 0.0)))",

                // Bit Operations
                UnaryArithmeticKind.PopC => $"i32(countOneBits({operand}))",
                UnaryArithmeticKind.CLZ => $"i32(countLeadingZeros({operand}))",
                UnaryArithmeticKind.CTZ => $"i32(countTrailingZeros({operand}))",

                _ => "DEBUG_MISSING"
            };

            if (result == "DEBUG_MISSING")
            {
                AppendLine($"// [WGSL] Unhandled UnaryArithmeticKind: {value.Kind}");
                result = $"unhandled_unary({operand})";
            }

            AppendLine($"{target} = {result};");
        }

        public virtual void GenerateCode(TernaryArithmeticValue value)
        {
            var target = Load(value);
            var first = Load(value.First);
            var second = Load(value.Second);
            var third = Load(value.Third);
            Declare(target);

            string result = value.Kind switch
            {
                TernaryArithmeticKind.MultiplyAdd => $"fma({first}, {second}, {third})",
                _ => $"({first} * {second} + {third})"
            };

            AppendLine($"{target} = {result};");
        }

        // Comparisons
        public virtual void GenerateCode(CompareValue value)
        {
            var target = Load(value);
            var left = Load(value.Left);
            var right = Load(value.Right);
            Declare(target);

            string op = value.Kind switch
            {
                CompareKind.Equal => "==",
                CompareKind.NotEqual => "!=",
                CompareKind.LessThan => "<",
                CompareKind.LessEqual => "<=",
                CompareKind.GreaterThan => ">",
                CompareKind.GreaterEqual => ">=",
                _ => "=="
            };

            // Check for emulated 64-bit operands
            string leftType = TypeGenerator[value.Left.Type];
            string rightType = TypeGenerator[value.Right.Type];
            bool isEmulatedI64 = (leftType == "emu_i64" || leftType == "emu_u64" ||
                                  rightType == "emu_i64" || rightType == "emu_u64");

            if (isEmulatedI64)
            {
                // Use 64-bit emulation comparison functions instead of raw operators
                // (raw >= on vec2<u32> returns vec2<bool>, not bool)
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
                    AppendLine($"{target} = {emulFunc}({left}, {right});");
                    return;
                }
            }

            bool isEmulatedF64 = (leftType == "emu_f64" || rightType == "emu_f64");
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
                    if (value.IsUnsignedOrUnordered && value.Kind != CompareKind.NotEqual)
                    {
                        AppendLine(
                            $"{target} = (_f32_is_nan_bits({left}.x) || _f32_is_nan_bits({right}.x)) || {emulFunc}({left}, {right});");
                    }
                    else
                    {
                        AppendLine($"{target} = {emulFunc}({left}, {right});");
                    }
                    return;
                }
            }

            // For unsigned integer comparisons, WGSL operators are type-sensitive:
            // i32 < i32 is signed, u32 < u32 is unsigned.
            // When ILGPU flags the comparison as unsigned, bitcast operands to u32.
            bool needsUnsignedCast = value.IsUnsignedOrUnordered
                && value.Left.BasicValueType == BasicValueType.Int32
                && value.Kind != CompareKind.Equal
                && value.Kind != CompareKind.NotEqual;

            // f32 NaN safety - mirror of the kernel-function-generator override.
            // Helper functions still use this base class generator, so any
            // helper using f32 compare also needs the same NaN-safe codegen.
            bool isFloatScalar = (value.Left.BasicValueType == BasicValueType.Float32
                    || value.Left.BasicValueType == BasicValueType.Float16)
                && !leftType.StartsWith("vec") && !rightType.StartsWith("vec");
            bool isNativeFloatUnordered = isFloatScalar && value.IsUnsignedOrUnordered
                && value.Kind != CompareKind.NotEqual
                && value.Kind != CompareKind.Equal;
            bool isNativeFloatEqualLike = isFloatScalar
                && (value.Kind == CompareKind.Equal || value.Kind == CompareKind.NotEqual);

            if (needsUnsignedCast)
            {
                AppendLine($"{target} = bitcast<u32>({left}) {op} bitcast<u32>({right});");
            }
            else if (isNativeFloatUnordered)
            {
                string LIsNaN = WgslIsNaNExpr($"{left}", leftType);
                string RIsNaN = WgslIsNaNExpr($"{right}", rightType);
                AppendLine($"{target} = ({LIsNaN} || {RIsNaN} || ({left} {op} {right}));");
            }
            else if (isNativeFloatEqualLike)
            {
                string LIsNaN = WgslIsNaNExpr($"{left}", leftType);
                string RIsNaN = WgslIsNaNExpr($"{right}", rightType);
                if (value.Kind == CompareKind.Equal)
                    AppendLine($"{target} = (!({LIsNaN}) && !({RIsNaN}) && ({left} == {right}));");
                else
                    AppendLine($"{target} = ({LIsNaN} || {RIsNaN} || ({left} != {right}));");
            }
            else
            {
                AppendLine($"{target} = {left} {op} {right};");
            }
        }

        /// <summary>
        /// Emits an IEEE 754 NaN bit-pattern test for a WGSL float scalar. For f32,
        /// `bitcast&lt;u32&gt;(x)` works directly. For native f16, the operand must be
        /// packed via `vec2&lt;f16&gt;(x, 0.0h)` first because `bitcast&lt;u32&gt;(f16)`
        /// is not valid WGSL (Tint rejects with "no matching call to bitcast&lt;u32&gt;(f16)";
        /// see candidates list - only `vec2&lt;f16&gt; -&gt; u32` is valid).
        /// f16 NaN: exponent bits 14-10 all 1s = 0x7C00, mantissa bits 9-0 nonzero (0x03FF mask).
        /// f32 NaN: exponent bits 30-23 all 1s = 0x7F800000, mantissa bits 22-0 nonzero (0x007FFFFF mask).
        /// </summary>
        private static string WgslIsNaNExpr(string operand, string wgslType)
        {
            if (wgslType == "f16")
            {
                return $"((bitcast<u32>(vec2<f16>({operand}, 0.0h)) & 0x7C00u) == 0x7C00u && "
                    + $"(bitcast<u32>(vec2<f16>({operand}, 0.0h)) & 0x03FFu) != 0u)";
            }
            // f32 path. Also reached for emulated-f16 (locals are f32 in that mode) -
            // bits represent the f32 promotion of the original f16 value, so the f32
            // NaN pattern is correct after promotion.
            return $"((bitcast<u32>({operand}) & 0x7F800000u) == 0x7F800000u && "
                + $"(bitcast<u32>({operand}) & 0x007FFFFFu) != 0u)";
        }

        /// <summary>Public proxy of <see cref="WgslIsNaNExpr"/> for use by partial-class /
        /// derived generators (e.g. WGSLKernelFunctionGenerator) that need the same NaN
        /// bit-pattern emit.</summary>
        internal static string WgslIsNaNExprPublic(string operand, string wgslType)
            => WgslIsNaNExpr(operand, wgslType);

        // Conversions
        public virtual void GenerateCode(ConvertValue value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            var targetType = TypeGenerator[value.Type];
            var sourceType = TypeGenerator[value.Value.Type];
            Declare(target);

            // Handle emulated 64-bit type conversions.
            // emu_i64/emu_u64 are type aliases for vec2<u32> (lo, hi).
            bool targetIsEmu64 = targetType == "emu_i64" || targetType == "emu_u64";
            bool sourceIsEmu64 = sourceType == "emu_i64" || sourceType == "emu_u64";

            if (targetIsEmu64 && !sourceIsEmu64)
            {
                // Widening: i32/u32/f32 → emu_i64/emu_u64
                // Convert source to u32, then zero-extend to vec2<u32>(lo, 0u)
                AppendLine($"{target} = vec2<u32>(u32({source}), 0u);");
            }
            else if (!targetIsEmu64 && sourceIsEmu64)
            {
                // Narrowing: emu_i64/emu_u64 → i32/u32/f32
                // Take low word (.x) and cast to target type
                AppendLine($"{target} = {targetType}({source}.x);");
            }
            else if (targetType == "emu_f64" && !sourceIsEmu64)
            {
                // emu_f64 widening from f32: use vec2<u32> with bitcast
                AppendLine($"{target} = vec2<u32>(bitcast<u32>(f32({source})), 0u);");
            }
            else
            {
                // WGSL has no native i16/i8 types, so `i32(int_value)` is
                // identity when both source and target lower to i32. Without
                // explicit narrowing, `(short)((x + (1 << 13)) >> 14)` patterns
                // in butterfly arithmetic (Tuvok's Vp9Idct16x16Kernel) leave
                // high bits intact when intermediates are just above short
                // range - producing small bit-exact divergence vs CPU oracle
                // on Random/Batched inputs. Mirrors rc.14 Wasm `i32.extend16_s`.
                // Combine cast + narrowing into one expression because helper
                // bodies use `let v_X = ...;` (immutable, cannot reassign).
                string castExpr = $"{targetType}({source})";
                if (targetType == "i32")
                {
                    bool isTargetUnsigned = (value.Flags & ConvertFlags.TargetUnsigned) == ConvertFlags.TargetUnsigned;
                    var dstBasicType = value.Type.BasicValueType;
                    if (dstBasicType == BasicValueType.Int16)
                        castExpr = isTargetUnsigned ? $"({castExpr} & 0xFFFFi)" : $"extractBits({castExpr}, 0u, 16u)";
                    else if (dstBasicType == BasicValueType.Int8)
                        castExpr = isTargetUnsigned ? $"({castExpr} & 0xFFi)" : $"extractBits({castExpr}, 0u, 8u)";
                    else
                    {
                        // Widening from a SUB-WORD source (Int16/Int8) to a wider int: re-extend the
                        // low bits per SourceUnsigned. The source may be zero-extended from an
                        // unsigned sub-word load or a `(short)`/`(sbyte)` reinterpret the core IR
                        // elided (short/ushort share BasicValueType.Int16). Mirrors the
                        // WGSLKernelFunctionGenerator override; see that comment for the radix case.
                        bool isSourceUnsigned = (value.Flags & ConvertFlags.SourceUnsigned) == ConvertFlags.SourceUnsigned;
                        var srcBasicType = value.Value.BasicValueType;
                        if (srcBasicType == BasicValueType.Int16)
                            castExpr = isSourceUnsigned ? $"({castExpr} & 0xFFFFi)" : $"extractBits({castExpr}, 0u, 16u)";
                        else if (srcBasicType == BasicValueType.Int8)
                            castExpr = isSourceUnsigned ? $"({castExpr} & 0xFFi)" : $"extractBits({castExpr}, 0u, 8u)";
                    }
                }
                AppendLine($"{target} = {castExpr};");
            }
        }

        // Memory Operations
        public virtual void GenerateCode(global::ILGPU.IR.Values.Load loadVal)
        {
            var target = Load(loadVal);
            var source = Load(loadVal.Source);
            Declare(target);

            var targetType = TypeGenerator[loadVal.Type];
            string sourceType = targetType;
            bool isAtomic = IsAtomicPointer(loadVal.Source);

            if (loadVal.Source.Type is global::ILGPU.IR.Types.PointerType ptrType)
            {
                var elemType = ptrType.ElementType;
                sourceType = TypeGenerator[elemType];
            }

            // Local alloca variables are WGSL 'var' value types, not pointers — no dereference needed.
            bool isAllocaVar = _localAllocaVarNames.Contains(source.Name);

            if (isAtomic)
            {
                AppendLine($"{target} = atomicLoad({source});");
            }
            else if (isAllocaVar)
            {
                AppendLine($"{target} = {source};");
            }
            else if (targetType != sourceType)
            {
                AppendLine($"{target} = bitcast<{targetType}>(*{source});");
            }
            else
            {
                AppendLine($"{target} = *{source};");
            }
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.Store storeVal)
        {
            var address = Load(storeVal.Target);
            var val = Load(storeVal.Value);

            bool isAtomic = IsAtomicPointer(storeVal.Target);

            // Detect emu_f64 store: buffer stores IEEE-754 bytes, must convert from Dekker.
            string targetElemType = "";
            if (storeVal.Target.Type is global::ILGPU.IR.Types.PointerType storePtrType)
                targetElemType = TypeGenerator[storePtrType.ElementType];

            // Local alloca variables are WGSL 'var' value types, not pointers — no dereference needed.
            bool isAllocaVar = _localAllocaVarNames.Contains(address.Name);

            if (isAtomic)
            {
                AppendLine($"atomicStore({address}, {val});");
            }
            else if (isAllocaVar)
            {
                AppendLine($"{address} = {val};");
            }
            else
            {
                AppendLine($"*{address} = {val};");
            }
        }

        protected virtual bool IsAtomicPointer(Value ptr)
        {
            if (ptr.Type is global::ILGPU.IR.Types.PointerType ptrType)
            {
                var elemTypeStr = TypeGenerator[ptrType.ElementType];
                if (elemTypeStr.Contains("atomic")) return true;
                var elemType = ptrType.ElementType;
                if (elemType.ToString().Contains("Index1D") || elemType.ToString().Contains("LongIndex1D"))
                    return true;
            }
            return false;
        }

        public virtual void GenerateCode(LoadElementAddress value)
        {
            var target = Load(value);
            var source = Load(value.Source);
            var offset = Load(value.Offset);
            AppendIndent();
            Builder.Append("let ");
            Builder.Append(target.Name);
            Builder.Append(" = ");
            Builder.Append($"&{source}[{offset}];");
            Builder.AppendLine();
        }

        /// <summary>
        /// Generates code for a SubViewValue (`view.SubView(offset, length)`).
        /// </summary>
        /// <remarks>
        /// SubView is a logical alias of its source view: the runtime semantics are
        /// "same backing buffer, indexes shifted by <c>offset</c>, length capped at
        /// <c>length</c>." In WGSL we cannot create a sub-buffer pointer with a
        /// baked-in offset (storage buffers are bound as <c>array&lt;T&gt;</c> at
        /// fixed bindings; pointer arithmetic only exists via <c>&amp;buf[idx]</c>
        /// for a SPECIFIC index, not a base-offset reference).
        ///
        /// Codegen strategy:
        /// - Emit a <c>let</c> binding that aliases the SubView variable to the
        ///   SOURCE view's variable (no separate buffer; no offset baked in).
        /// - The kernel generator's <c>LoadElementAddress</c> override walks the
        ///   <c>SubViewValue</c> chain in IR via <c>value.Source.Resolve()</c> and
        ///   accumulates the SubView offsets into the LEA's index expression at
        ///   the use site. <c>&amp;subView[idx]</c> becomes
        ///   <c>&amp;underlyingSource[(svOffset1 + svOffset2 + ... + idx)]</c>.
        ///
        /// Result: SubView is "transparent" at codegen — it occupies its own IR
        /// node so the visitor doesn't throw, but produces no per-SubView buffer.
        /// All offset arithmetic happens at the LEA / Load / Store sites that
        /// actually index into the buffer.
        ///
        /// Limitation: <c>GetViewLength</c> on a SubView still returns the source
        /// view's length, not the SubView's. Codecs kernels that motivated this
        /// fix (Av1 / Vp8 / Vp9 / Flac / Vorbis encoders + decoders) don't call
        /// <c>.Length</c> on SubView results. Add a <c>SubViewValue</c>-aware
        /// override of <c>GenerateCode(GetViewLength)</c> if a future kernel does.
        /// </remarks>
        public virtual void GenerateCode(global::ILGPU.IR.Values.SubViewValue value)
        {
            var target = Load(value);
            var source = Load(value.Source);

            // Alias the SubView's variable to the source view. The actual offset
            // is applied at indexing sites by walking value.Source.Resolve() through
            // the SubView chain. Emit a comment so generated WGSL is self-documenting.
            declaredVariables.Add(target.Name);
            if (IsStateMachineActive)
            {
                VariableBuilder.AppendLine($"    let {target.Name} = {source}; // subView (offset applied at LEA, hoisted)");
            }
            else
            {
                AppendLine($"let {target.Name} = {source}; // subView (offset applied at LEA)");
            }
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.NewView value)
        {
            var target = Load(value);
            var source = Load(value.Pointer);
            // Detect shared memory by checking if the resolved source name
            // matches any shared variable name from SharedMemoryVarNames.
            bool isSharedSource = false;
            foreach (var kvp in _baseArgs.SharedMemoryVarNames)
            {
                if (kvp.Value.Name == source.Name)
                {
                    isSharedSource = true;
                    break;
                }
            }

            if (isSharedSource)
            {
                // For shared memory: alias the target to the source array name (no WGSL emission).
                // LoadElementAddress will then directly produce &shared_0[idx].
                valueVariables[value] = new Variable(source.Name, source.Type);
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[DIAG-NEWVIEW-SHARED] Aliased {target} -> {source.Name} (no WGSL emit)");
            }
            else
            {
                // For non-shared buffer sources: emit a let binding to alias the pointer.
                // CRITICAL: if a state machine is active, the let must be hoisted to
                // function scope (VariableBuilder) so it's accessible across all case
                // blocks. Without hoisting, v_22 declared in case 3 is unresolved in case 5.
                declaredVariables.Add(target.Name);
                if (IsStateMachineActive)
                {
                    VariableBuilder.AppendLine($"    let {target.Name} = {source}; // newView (hoisted)");
                }
                else
                {
                    AppendLine($"let {target.Name} = {source}; // newView");
                }
            }
        }

        /// <summary>
        /// Generates code for GetViewLength (ArrayView.Length).
        /// Base implementation emits a comment; override in kernel generator
        /// to emit arrayLength(&paramN) for storage buffer bindings.
        /// </summary>
        public virtual void GenerateCode(global::ILGPU.IR.Values.GetViewLength value)
        {
            var target = Load(value);
            var source = Load(value.View);
            Declare(target);
            // In non-kernel contexts (helper functions), length might
            // not be directly available. For kernel params, the kernel
            // function generator overrides this.
            string lengthType = value.LengthType == BasicValueType.Int64 ? "i64" : "i32";
            AppendLine($"{target} = {lengthType}(arrayLength(&{source}));");
        }

        public virtual void GenerateCode(LoadFieldAddress value)
        {
            var target = Load(value);
            var source = Load(value.Source);
            AppendIndent();
            Builder.Append("let ");
            Builder.Append(target.Name);
            Builder.Append(" = ");

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

            // Local alloca variables are WGSL 'var' value types — use &source.field, not &(*source).field
            if (_localAllocaVarNames.Contains(source.Name))
                Builder.Append($"&{source}.{fieldName};");
            else
                Builder.Append($"&(*{source}).{fieldName};");
            Builder.AppendLine();
        }

        public virtual void GenerateCode(Alloca value)
        {
            // Already handled in SetupAllocations
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.NewArray value)
        {
            // NewArray creates a local array — declare it
            var arrayType = value.Type;
            string elementType = TypeGenerator[arrayType.ElementType];
            int arraySize = 1;
            foreach (var dim in value.Nodes)
            {
                if (dim.Resolve() is PrimitiveValue pv)
                    arraySize *= pv.Int32Value;
            }
            string arrayName = $"local_arr_{_localArrayCounter++}";
            var target = Load(value);
            _allocaArrayNames[target.Name] = arrayName;
            if (IsStateMachineActive)
            {
                VariableBuilder.AppendLine($"    var {arrayName} : array<{elementType}, {arraySize}>;");
            }
            else
            {
                AppendLine($"var {arrayName} : array<{elementType}, {arraySize}>;");
            }
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.LoadArrayElementAddress value)
        {
            var target = Load(value);
            var arraySource = Load(value.ArrayValue);
            var indexVar = Load(value.Dimensions[0]);
            string arrayName = _allocaArrayNames.TryGetValue(arraySource.Name, out var name)
                ? name : arraySource.Name;

            // Scalarized-alloca path: ILGPU IR optimizes single-element array
            // allocations (e.g. `LocalMemory.Allocate<int>(1)`) into scalar
            // allocations - `Alloca.IsArrayAllocation` returns false, the alloca
            // is declared as `var v_5 : i32` and `NewView` aliases it with
            // `let v_7 = v_5;` (value copy). The default codegen below would
            // then emit `&v_7[v_8]` which WGSL rejects with "cannot index type
            // 'i32'". Detect the pattern by walking the IR: if ArrayValue is
            // a NewView wrapping a scalar Alloca (IsSimpleAllocation), the
            // "array" is a single scalar storage and the index must be 0.
            // Emit a direct address-of the alloca's var, skipping the indexing.
            // Fixes WebGPUTests.NoHelperOutLikeAllocaTest reported by Captain
            // on the GitHub Pages live demo 2026-04-25.
            if (TryGetScalarizedAllocaName(value.ArrayValue, out var scalarAllocaName))
            {
                AppendIndent();
                Builder.Append($"let {target.Name} = &{scalarAllocaName};");
                Builder.AppendLine();
                return;
            }

            // Emit a WGSL pointer: let v_N = &array[index];
            AppendIndent();
            Builder.Append($"let {target.Name} = &{arrayName}[{indexVar}];");
            Builder.AppendLine();
        }

        /// <summary>
        /// True when <paramref name="arrayValue"/> traces back via NewView to a
        /// scalar (IsSimpleAllocation) Alloca that this codegen has registered as a
        /// local-alloca var. Outputs the alloca's WGSL variable name on success.
        /// Used by `LoadArrayElementAddress` to skip array indexing when the IR
        /// has scalarized the underlying storage to a single element.
        /// </summary>
        private bool TryGetScalarizedAllocaName(
            global::ILGPU.IR.Value arrayValue,
            out string allocaVarName)
        {
            allocaVarName = string.Empty;
            // Walk: arrayValue should resolve to a NewView whose Pointer
            // resolves to an Alloca with IsSimpleAllocation = true.
            if (arrayValue.Resolve() is not global::ILGPU.IR.Values.NewView newView)
                return false;
            if (newView.Pointer.Resolve() is not global::ILGPU.IR.Values.Alloca alloca)
                return false;
            if (!alloca.IsSimpleAllocation)
                return false;
            // Look up the alloca's WGSL variable name from valueVariables;
            // SetupAllocations registered it under the Alloca IR node's identity.
            if (!valueVariables.TryGetValue(alloca, out var allocaVar))
                return false;
            allocaVarName = allocaVar.Name;
            return true;
        }

        // Constants
        public virtual void GenerateCode(PrimitiveValue value)
        {
            var target = Load(value);
            var type = TypeGenerator[value.Type];
            Declare(target);

            // Check if we need emulation for this constant
            bool isEmulatedF64 = Backend.EnableF64Emulation && value.BasicValueType == BasicValueType.Float64;
            bool isEmulatedI64 = Backend.EnableI64Emulation && value.BasicValueType == BasicValueType.Int64;

            if (isEmulatedF64)
            {
                // CRITICAL FIX: Use IEEE-754 bits to preserve full 64-bit precision!
                // Casting to float loses precision (24-bit mantissa vs 52-bit)
                // Instead, we pass the raw 64-bit representation as two u32 values
                double doubleVal = value.Float64Value;
                ulong bits = BitConverter.DoubleToUInt64Bits(doubleVal);
                uint lo = (uint)(bits & 0xFFFFFFFF);
                uint hi = (uint)(bits >> 32);
                AppendLine($"{target} = f64_from_ieee754_bits({lo}u, {hi}u);");
                return;
            }

            if (isEmulatedI64)
            {
                // For emu_i64 emulation, we need to split the 64-bit value into two 32-bit parts.
                // Deduplicate via module-scope const: repeated values (e.g., INT32_MIN, 0)
                // reference a single const declaration instead of re-emitting the literal.
                long longVal = value.Int64Value;
                uint lo = (uint)(longVal & 0xFFFFFFFF);
                uint hi = (uint)((ulong)longVal >> 32);
                var key = (lo, hi);
                var constants = _baseArgs.I64Constants;
                if (!constants.TryGetValue(key, out var constName))
                {
                    constName = $"_c_i64_{constants.Count}";
                    constants[key] = constName;
                }
                AppendLine($"{target} = {constName};");
                return;
            }

            string valStrStd = value.BasicValueType switch
            {
                BasicValueType.Int1 => value.Int1Value ? "true" : "false",
                BasicValueType.Int8 => value.Int8Value.ToString(),
                BasicValueType.Int16 => value.Int16Value.ToString(),
                BasicValueType.Int32 => value.Int32Value.ToString(),
                BasicValueType.Int64 => value.Int64Value.ToString(),
                BasicValueType.Float16 => FormatFloat((float)value.Float16Value),
                BasicValueType.BFloat16 => FormatFloat((float)value.BFloat16Value),
                BasicValueType.Float8E4M3 => FormatFloat((float)value.Float8E4M3Value),
                BasicValueType.Float8E5M2 => FormatFloat((float)value.Float8E5M2Value),
                BasicValueType.Float4E2M1 => FormatFloat((float)value.Float4E2M1Value),
                BasicValueType.QInt4 => ((int)value.QInt4Value).ToString(), // packed 4-bit -> sign-extended i32
                BasicValueType.Float32 => FormatFloat(value.Float32Value),
                BasicValueType.Float64 => FormatFloat((float)value.Float64Value),
                _ => "0"
            };

            if (value.BasicValueType != BasicValueType.Int1)
            {
                AppendLine($"{target} = {type}({valStrStd});");
            }
            else
            {
                AppendLine($"{target} = {valStrStd};");
            }
        }

        private string FormatFloat(float value)
        {
            // Inf / NaN have no WGSL literal form. WGSL's const-evaluator
            // ALSO refuses to materialise non-finite values from a const
            // bitcast: `bitcast<f32>(0x7F800000u)` is a const-expression that
            // would evaluate to +Inf, and the spec rejects shader creation if
            // a const-expression yields a non-finite value. So we cannot
            // simply emit `bitcast<f32>(...)` for the literal - Chrome rejects
            // the shader at pipeline creation ("Invalid ComputePipeline").
            //
            // Workaround: route through a tiny WGSL helper function. Function
            // calls are not const-evaluated, so the bitcast happens at
            // runtime and the +Inf / -Inf / NaN value is materialised inside
            // the function body (which is a non-const context where bitcast
            // to non-finite f32 is permitted). The helpers are declared once
            // in WGSLEmulationLibrary and trimmed in if any kernel actually
            // references an Inf or NaN literal.
            //
            // Pre-fix this branch substituted +Inf with 3.402823e+38
            // (= float.MaxValue), which silently broke any kernel that
            // compared against +Inf - notably IsInf, where
            // (x == +Inf || x == -Inf) became (x == MaxValue || x == -MaxValue),
            // returning 0 for actually-infinite x. See
            // _DevComms/SpawnDev.ILGPU/data-to-geordi-isinf-wgsl-glsl-codegen-bug-2026-04-28.md.
            if (float.IsPositiveInfinity(value)) return "_f32_pos_inf()";
            if (float.IsNegativeInfinity(value)) return "_f32_neg_inf()";
            if (float.IsNaN(value)) return "_f32_qnan()";

            // float.MaxValue / float.MinValue exceed what the WGSL parser
            // can represent as a decimal literal.  Use bitcast instead.
            // (These ARE finite f32 values so const-bitcast is permitted.)
            if (value == float.MaxValue || value == float.MinValue ||
                value == -float.MaxValue)
            {
                uint bits = BitConverter.SingleToUInt32Bits(value);
                return $"bitcast<f32>(0x{bits:X8}u)";
            }

            var str = value.ToString("G9");
            if (!str.Contains('.') && !str.Contains('e') && !str.Contains('E'))
                str += ".0";
            return str;
        }

        public virtual void GenerateCode(NullValue value)
        {
            var target = Load(value);
            Declare(target);
            AppendLine($"{target} = {target.Type}(); // null");
        }

        public virtual void GenerateCode(global::ILGPU.IR.Values.Barrier value)
        {
            if (value.Kind == global::ILGPU.IR.Values.BarrierKind.GroupLevel)
            {
                AppendLine("workgroupBarrier();");
                AppendLine("storageBarrier();");
            }
        }

        public virtual void GenerateCode(StringValue value)
        {
            if (WebGPUBackend.EmitDebugStrings)
                AppendLine($"// String: {value.String}");
        }

        // Phi
        public virtual void GenerateCode(PhiValue value)
        {
            var target = Load(value);
            Declare(target);
        }

        // Structures
        public virtual void GenerateCode(StructureValue value)
        {
            var target = Load(value);
            Declare(target);
            var sb = new StringBuilder();
            sb.Append($"{target} = {target.Type}(");
            for (int i = 0; i < value.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Load(value[i]));
            }
            sb.Append(");");
            AppendLine(sb.ToString());
        }

        public virtual void GenerateCode(GetField value)
        {
            var target = Load(value);
            var source = Load(value.ObjectValue);
            Declare(target);

            string fieldName = $"field_{value.FieldSpan.Index}";
            if (IsIndexType(value.ObjectValue.Type))
            {
                fieldName = value.FieldSpan.Index switch
                {
                    0 => "x",
                    1 => "y",
                    2 => "z",
                    _ => fieldName
                };
            }

            AppendLine($"{target} = {source}.{fieldName};");
        }

        public virtual void GenerateCode(SetField value)
        {
            var target = Load(value);
            var source = Load(value.ObjectValue);
            var fieldValue = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {source};");

            string fieldName = $"field_{value.FieldSpan.Index}";
            if (IsIndexType(value.ObjectValue.Type))
            {
                fieldName = value.FieldSpan.Index switch
                {
                    0 => "x",
                    1 => "y",
                    2 => "z",
                    _ => fieldName
                };
            }

            AppendLine($"{target}.{fieldName} = {fieldValue};");
        }

        protected bool IsIndexType(TypeNode type)
        {
            var typeName = type.ToString();
            return typeName.Contains("Index") &&
                   (typeName.Contains("1D") || typeName.Contains("2D") || typeName.Contains("3D"));
        }

        // Device Constants
        public virtual void GenerateCode(GridIndexValue value)
        {
            var target = Load(value);
            Declare(target);
            string dim = value.Dimension switch
            {
                DeviceConstantDimension3D.X => "x",
                DeviceConstantDimension3D.Y => "y",
                DeviceConstantDimension3D.Z => "z",
                _ => "x"
            };
            AppendLine($"{target} = i32(group_id.{dim});");
        }

        public virtual void GenerateCode(GroupIndexValue value)
        {
            var target = Load(value);
            Declare(target);
            string dim = value.Dimension switch
            {
                DeviceConstantDimension3D.X => "x",
                DeviceConstantDimension3D.Y => "y",
                DeviceConstantDimension3D.Z => "z",
                _ => "x"
            };
            AppendLine($"{target} = i32(local_id.{dim});");
        }

        public virtual void GenerateCode(GridDimensionValue value)
        {
            var target = Load(value);
            Declare(target);
            string dim = value.Dimension switch
            {
                DeviceConstantDimension3D.X => "x",
                DeviceConstantDimension3D.Y => "y",
                DeviceConstantDimension3D.Z => "z",
                _ => "x"
            };
            // GridDimensionValue = Grid.DimX (number of workgroups in dimension, NOT total threads).
            // In WebGPU WGSL this is num_workgroups.x.
            // The previous code multiplied by workgroup_size, erroneously computing total thread count,
            // which caused GridStrideLoopStride = Grid.DimX * Group.DimX to be 64x too large.
            AppendLine($"{target} = i32(num_workgroups.{dim});");
        }


        public virtual void GenerateCode(GroupDimensionValue value)
        {
            var target = Load(value);
            Declare(target);
            string dim = value.Dimension switch
            {
                DeviceConstantDimension3D.X => "x",
                DeviceConstantDimension3D.Y => "y",
                DeviceConstantDimension3D.Z => "z",
                _ => "x"
            };
            AppendLine($"{target} = i32(workgroup_size.{dim});");
        }

        public virtual void GenerateCode(WarpSizeValue value)
        {
            var target = Load(value);
            Declare(target);
            if (HasSubgroups)
                AppendLine($"{target} = i32(subgroup_size);");
            else
                AppendLine($"{target} = i32(workgroup_size.x);");
        }

        public virtual void GenerateCode(LaneIdxValue value)
        {
            var target = Load(value);
            Declare(target);
            if (HasSubgroups)
                AppendLine($"{target} = i32(subgroup_invocation_id);");
            else
                AppendLine($"{target} = i32(local_index % workgroup_size.x);");
        }

        // Control Flow
        public virtual void GenerateCode(ReturnTerminator value)
        {
            // WGSL supports `return;` anywhere - inside loops, switch cases, etc.
            // Always emit `return;` for void returns. For non-void returns, assign
            // the return variable first (state machine reads it after the loop).
            //
            // Previously, state machine mode emitted `break;` which only exits the
            // innermost loop - NOT the function. This caused `return` inside for-loops
            // to fall through to code after the loop instead of exiting the kernel.
            if (value.IsVoidReturn)
            {
                AppendLine("return;");
            }
            else if (IsStateMachineActive)
            {
                var retVal = Load(value.ReturnValue);
                AppendLine($"_ilgpu_return_val = {retVal};");
                AppendLine("return;");
            }
            else
            {
                var retVal = Load(value.ReturnValue);
                AppendLine($"return {retVal};");
            }
        }

        public virtual void GenerateCode(UnconditionalBranch branch)
        {
            EmitPhiAssignments(branch.BasicBlock, branch.Target);
            int targetIdx = GetBlockIndex(branch.Target);
            AppendLine($"current_block = {targetIdx};");
            AppendLine("continue;");
        }

        public virtual void GenerateCode(IfBranch branch)
        {
            var cond = Load(branch.Condition);
            int trueIdx = GetBlockIndex(branch.TrueTarget);
            int falseIdx = GetBlockIndex(branch.FalseTarget);

            AppendLine($"if ({cond}) {{");
            PushIndent();
            EmitPhiAssignments(branch.BasicBlock, branch.TrueTarget);
            AppendLine($"current_block = {trueIdx};");
            PopIndent();
            AppendLine("} else {");
            PushIndent();
            EmitPhiAssignments(branch.BasicBlock, branch.FalseTarget);
            AppendLine($"current_block = {falseIdx};");
            PopIndent();
            AppendLine("}");
            AppendLine("continue;");
        }

        public virtual void GenerateCode(SwitchBranch branch)
        {
            var selector = Load(branch.Condition);
            AppendLine($"switch ({selector}) {{");
            PushIndent();

            for (int i = 0; i < branch.NumCasesWithoutDefault; i++)
            {
                var target = branch.GetCaseTarget(i);
                int targetIdx = GetBlockIndex(target);
                AppendLine($"case {i}: {{");
                PushIndent();
                EmitPhiAssignments(branch.BasicBlock, target);
                AppendLine($"current_block = {targetIdx};");
                PopIndent();
                AppendLine("}");
            }

            int defaultIdx = GetBlockIndex(branch.DefaultBlock);
            AppendLine("default: {");
            PushIndent();
            EmitPhiAssignments(branch.BasicBlock, branch.DefaultBlock);
            AppendLine($"current_block = {defaultIdx};");
            PopIndent();
            AppendLine("}");

            PopIndent();
            AppendLine("}");
            AppendLine("continue;");
        }

        protected void EmitPhiAssignments(BasicBlock sourceBlock, BasicBlock targetBlock)
        {
            foreach (var valueEntry in targetBlock)
            {
                if (valueEntry.Value is PhiValue phi)
                {
                    var phiVar = Load(phi);
                    var srcValue = phi.GetValue(sourceBlock);
                    if (srcValue != null)
                    {
                        var srcVar = Load(srcValue);
                        AppendLine($"{phiVar} = {srcVar};");
                    }
                }
            }
        }

        // Method Calls
        public virtual void GenerateCode(MethodCall methodCall)
        {
            var target = Load(methodCall);
            Declare(target);

            string name = methodCall.Target.Name;
            // Map common intrinsics if they appear as method calls
            string? wgslFunc = name switch
            {
                // Specialized Intrinsics (Check these first to avoid sub-string matches)
                var n when n.Contains("Rsqrt") => "rsqrt_custom",
                var n when n.Contains("Rcp") => "rcp_custom", // Special handling
                var n when n.Contains("Asin") => "asin",
                var n when n.Contains("Acos") => "acos",
                var n when n.Contains("Atan2") => "atan2",
                var n when n.Contains("Atan") => "atan",
                var n when n.Contains("Sinh") => "sinh",
                var n when n.Contains("Cosh") => "cosh",
                var n when n.Contains("Tanh") => "tanh",
                var n when n.Contains("Step") && !n.Contains("Smooth") => "step",
                var n when n.Contains("SmoothStep") => "smoothstep",
                var n when n.Contains("FusedMultiplyAdd") => "fma",

                var n when n.Contains("PopCount") => "countOneBits",
                var n when n.Contains("TrailingZeroCount") => "countTrailingZeros",
                var n when n.Contains("LeadingZeroCount") => "countLeadingZeros",
                var n when n.Contains("Reverse") => "reverseBits",

                // Standard Math
                var n when n.Contains("Sin") => "sin",
                var n when n.Contains("Cos") => "cos",
                var n when n.Contains("Tan") => "tan",
                var n when n.Contains("Sqrt") => "sqrt",
                var n when n.Contains("Abs") => "abs",
                var n when n.Contains("Pow") => "pow",
                var n when n.Contains("Exp") => "exp",
                var n when n.Contains("Log") => "log",
                var n when n.Contains("Floor") => "floor",
                var n when n.Contains("Ceiling") => "ceil",
                var n when n.Contains("Min") => "min",
                var n when n.Contains("Max") => "max",
                var n when n.Contains("Clamp") => "clamp",
                var n when n.Contains("Sign") => "sign",
                var n when n.Contains("Round") => "round",
                var n when n.Contains("Truncate") => "trunc",
                var n when n.Contains("Lerp") || n.Contains("Mix") => "mix",
                var n when n.Contains("Select") => "select_custom",

                var n when n.Contains("Sinh") => "sinh",
                var n when n.Contains("Cosh") => "cosh",
                var n when n.Contains("Tanh") => "tanh",

                _ => null
            };

            if (wgslFunc != null)
            {
                if (methodCall.Count == 1)
                {
                    var arg = Load(methodCall[0]);

                    if (wgslFunc == "rcp_custom")
                    {
                        AppendLine($"{target} = 1.0 / {arg};");
                        return;
                    }
                    if (wgslFunc == "rsqrt_custom")
                    {
                        AppendLine($"{target} = 1.0 / sqrt({arg});");
                        return;
                    }

                    string call = $"{wgslFunc}({arg})";
                    if (wgslFunc == "sign" && (TypeGenerator[methodCall.Type] == "i32" || TypeGenerator[methodCall.Type] == "u32"))
                    {
                        call = $"i32({call})";
                    }
                    AppendLine($"{target} = {call};");
                    return;
                }
                else if (methodCall.Count == 2)
                {
                    var arg1 = Load(methodCall[0]);
                    var arg2 = Load(methodCall[1]);
                    AppendLine($"{target} = {wgslFunc}({arg1}, {arg2});");
                    return;
                }
                else if (methodCall.Count == 3)
                {
                    var arg1 = Load(methodCall[0]);
                    var arg2 = Load(methodCall[1]);
                    var arg3 = Load(methodCall[2]);

                    // Select(cond, trueVal, falseVal) -> select(falseVal, trueVal, cond)
                    if (wgslFunc == "select_custom")
                    {
                        AppendLine($"{target} = select({arg3}, {arg2}, {arg1});");
                    }
                    else
                    {
                        // FMA or Mix usually
                        AppendLine($"{target} = {wgslFunc}({arg1}, {arg2}, {arg3});");
                    }
                    return;
                }
                else if (methodCall.Count == 3)
                {
                    var arg1 = Load(methodCall[0]);
                    var arg2 = Load(methodCall[1]);
                    var arg3 = Load(methodCall[2]);
                    AppendLine($"{target} = {wgslFunc}({arg1}, {arg2}, {arg3});");
                    return;
                }
            }


            // --- Smart Fallback: Reduction Method Detection ---
            // When GroupExtensions.Reduce / WarpExtensions.Reduce is not registered as an
            // intrinsic for the current backend, ILGPU falls back to the IL implementation
            // (PTXWarpExtensions.Reduce, ILWarpExtensions.Reduce, etc.). The resulting IR
            // MethodCall has a source MethodInfo for the specialized generic variant (e.g.
            // Reduce<int, MaxInt32>). We detect this pattern and emit the appropriate WGSL
            // subgroup intrinsic based on TReduction.CLCommand.
            //
            // CRITICAL: ILGroupExtensions.AllReduce compiles Warp.IsFirstLane as Group.IsFirstThread
            // (i.e. local_id.x==0 && local_id.y==0 && local_id.z==0), which is only TRUE for thread 0
            // of the workgroup. This means only subgroup 0's result ever reaches shared memory.
            // FIX: After the warp-level subgroup op, we emit a complete cross-subgroup barrier
            // accumulation using _grp_sg_results[]. ALL threads then hold the workgroup-wide result,
            // so the subsequent (broken) thread-0-only atomicMax correctly writes the final value.
            var sourceMethodInfo = methodCall.Target.Source as MethodInfo;
            if (sourceMethodInfo != null &&
                (sourceMethodInfo.Name == "Reduce" || sourceMethodInfo.Name == "AllReduce") &&
                sourceMethodInfo.IsGenericMethod)
            {
                string? subgroupOp = GetSubgroupReduceOp(sourceMethodInfo);
                // Early-compute wgslType to check if 64-bit emulated
                string earlyIrType = TypeGenerator[methodCall.Type];
                string earlyWgslType = GetReductionWgslType(sourceMethodInfo) ?? earlyIrType;
                bool is64BitReduce = earlyWgslType.StartsWith("emu_");

                // CRITICAL: subgroupMax/Min/Add do NOT work on vec2<u32> (emulated 64-bit types).
                // Force 64-bit types to the no-subgroups shared-memory path.
                if (subgroupOp != null && HasSubgroups && !is64BitReduce)
                {
                    AppendLine($"// Reduce fallback (group-level): {methodCall.Target.Name} -> {subgroupOp}");
                    var reduceSrc = methodCall.Count > 0 ? Load(methodCall[0]) : null;
                    if (reduceSrc != null)
                    {
                        // Signal kernel generator to declare _grp_sg_results workgroup array
                        UsesGroupReduce = true;

                        // Determine effective WGSL type from TReduction (handles u32, f32, emu_* correctly)
                        string irType = earlyIrType;
                        string wgslType = earlyWgslType;
                        bool needsBitcast = wgslType != irType;

                        // Determine scalar accumulation op from subgroupOp name
                        string wgslAccumOp;
                        if (subgroupOp.EndsWith("Max", StringComparison.Ordinal))
                            wgslAccumOp = "max";
                        else if (subgroupOp.EndsWith("Min", StringComparison.Ordinal))
                            wgslAccumOp = "min";
                        else
                            wgslAccumOp = "+";

                        // Step 1: intra-subgroup reduce (with correct type for subgroup op)
                        string warpResult = $"_warp_sg_result_{target.Name}";
                        string srcExpr = needsBitcast ? $"bitcast<{wgslType}>({reduceSrc})" : $"{reduceSrc}";
                        AppendLine($"var {warpResult} : {wgslType} = {subgroupOp}({srcExpr});");

                        // Step 2: first lane of each subgroup stores to _grp_sg_results
                        AppendLine($"if (subgroup_invocation_id == 0u) {{");
                        PushIndent();
                        AppendLine($"_grp_sg_results[subgroup_id] = {BitcastToU32(warpResult, wgslType)};");
                        PopIndent();
                        AppendLine("}");

                        // Step 3: workgroup barrier so all subgroups have written
                        AppendLine("workgroupBarrier();");

                        // Step 4: all threads cooperatively reduce across subgroups
                        string load0Expr = BitcastFromU32("_grp_sg_results[0u]", wgslType);
                        string loadNExpr = BitcastFromU32("_grp_sg_results[_sgi]", wgslType);
                        AppendLine("{");
                        PushIndent();
                        AppendLine($"var _grp_accum : {wgslType} = {load0Expr};");
                        AppendLine("let _num_sgs : u32 = workgroup_size.x / subgroup_size;");
                        AppendLine("for (var _sgi : u32 = 1u; _sgi < _num_sgs; _sgi++) {");
                        PushIndent();
                        string elemExpr = loadNExpr;
                        if (wgslAccumOp == "+")
                            AppendLine($"_grp_accum = _grp_accum + {elemExpr};");
                        else
                            AppendLine($"_grp_accum = {wgslAccumOp}(_grp_accum, {elemExpr});");
                        PopIndent();
                        AppendLine("}");
                        // Write back, bitcasting to IR type if needed
                        if (needsBitcast)
                            AppendLine($"{target} = bitcast<{irType}>(_grp_accum);");
                        else
                            AppendLine($"{target} = _grp_accum;");
                        PopIndent();
                        AppendLine("}");
                        return;
                    }
                }
                else if (subgroupOp != null || (subgroupOp != null && is64BitReduce))
                {
                    // No subgroups (or 64-bit emulated type): workgroup-level shared-memory reduction.
                    var reduceSrc = methodCall.Count > 0 ? Load(methodCall[0]) : null;
                    if (reduceSrc != null)
                    {
                        UsesWarpShuffleEmulation = true;

                        // Determine effective WGSL type from TReduction
                        string irType = earlyIrType;
                        string wgslType = earlyWgslType;
                        bool needsBitcast = wgslType != irType;

                        string wgslAccumOp;
                        if (subgroupOp.EndsWith("Max", StringComparison.Ordinal))
                            wgslAccumOp = "max";
                        else if (subgroupOp.EndsWith("Min", StringComparison.Ordinal))
                            wgslAccumOp = "min";
                        else
                            wgslAccumOp = "+";

                        AppendLine($"// Reduce via shared memory{(is64BitReduce ? " (64-bit emulated)" : " (subgroups unavailable)")}: {methodCall.Target.Name}");

                        bool is64Bit = is64BitReduce;

                        // Step 1: all threads write their value to shared memory
                        string storeVal = needsBitcast ? $"bitcast<{wgslType}>({reduceSrc})" : $"{reduceSrc}";
                        bool isOzakiF64 = wgslType == "emu_f64" && Backend.UseOzakiF64Emulation;
                        if (is64Bit)
                        {
                            if (isOzakiF64)
                            {
                                // Ozaki emu_f64 = vec4<f32>: store all 4 components
                                AppendLine($"_warp_shuffle_buf[local_index * 4u] = bitcast<u32>({storeVal}.x);");
                                AppendLine($"_warp_shuffle_buf[local_index * 4u + 1u] = bitcast<u32>({storeVal}.y);");
                                AppendLine($"_warp_shuffle_buf[local_index * 4u + 2u] = bitcast<u32>({storeVal}.z);");
                                AppendLine($"_warp_shuffle_buf[local_index * 4u + 3u] = bitcast<u32>({storeVal}.w);");
                            }
                            else
                            {
                                // Dekker emu_f64 = vec2<f32> or emu_i64/emu_u64 = vec2<u32>: store 2 components
                                AppendLine($"_warp_shuffle_buf[local_index * 2u] = bitcast<u32>({storeVal}.x);");
                                AppendLine($"_warp_shuffle_buf[local_index * 2u + 1u] = bitcast<u32>({storeVal}.y);");
                            }
                        }
                        else
                        {
                            AppendLine($"_warp_shuffle_buf[local_index] = {BitcastToU32(storeVal, wgslType)};");
                        }
                        AppendLine("workgroupBarrier();");

                        // Step 2: all threads cooperatively reduce across the workgroup
                        string componentType = wgslType == "emu_f64" ? "f32" : "u32";
                        string load0, loadN;
                        if (is64Bit)
                        {
                            if (isOzakiF64)
                            {
                                // Ozaki: reconstruct vec4<f32> from 4 u32 slots
                                load0 = $"{wgslType}(bitcast<{componentType}>(_warp_shuffle_buf[0u]), bitcast<{componentType}>(_warp_shuffle_buf[1u]), bitcast<{componentType}>(_warp_shuffle_buf[2u]), bitcast<{componentType}>(_warp_shuffle_buf[3u]))";
                                loadN = $"{wgslType}(bitcast<{componentType}>(_warp_shuffle_buf[_ri * 4u]), bitcast<{componentType}>(_warp_shuffle_buf[_ri * 4u + 1u]), bitcast<{componentType}>(_warp_shuffle_buf[_ri * 4u + 2u]), bitcast<{componentType}>(_warp_shuffle_buf[_ri * 4u + 3u]))";
                            }
                            else
                            {
                                // Dekker or integer: reconstruct from 2 u32 slots
                                load0 = $"{wgslType}(bitcast<{componentType}>(_warp_shuffle_buf[0u]), bitcast<{componentType}>(_warp_shuffle_buf[1u]))";
                                loadN = $"{wgslType}(bitcast<{componentType}>(_warp_shuffle_buf[_ri * 2u]), bitcast<{componentType}>(_warp_shuffle_buf[_ri * 2u + 1u]))";
                            }
                        }
                        else
                        {
                            load0 = BitcastFromU32("_warp_shuffle_buf[0u]", wgslType);
                            loadN = BitcastFromU32("_warp_shuffle_buf[_ri]", wgslType);
                        }
                        AppendLine("{");
                        PushIndent();
                        AppendLine($"var _reduce_accum : {wgslType} = {load0};");
                        AppendLine("for (var _ri : u32 = 1u; _ri < workgroup_size.x; _ri++) {");
                        PushIndent();
                        // For 64-bit emulated types, use emulated math helpers instead of
                        // WGSL built-ins (max/min do component-wise on vec2, + lacks carry propagation)
                        string accumExpr = GetEmulated64BitAccumExpr(is64Bit, wgslType, wgslAccumOp, "_reduce_accum", loadN);
                        AppendLine($"_reduce_accum = {accumExpr};");
                        PopIndent();
                        AppendLine("}");
                        if (needsBitcast)
                            AppendLine($"{target} = bitcast<{irType}>(_reduce_accum);");
                        else
                            AppendLine($"{target} = _reduce_accum;");
                        PopIndent();
                        AppendLine("}");
                        AppendLine("workgroupBarrier();");
                        return;
                    }
                }
            }


            // Final fallback: emit a type-correct zero value to avoid WGSL type errors.
            // The 12345.0 literal previously used caused 'abstract-float to i32' type errors.
            AppendLine($"// Call: {methodCall.Target.Name} (Unmapped)");
            string fallbackType = TypeGenerator[methodCall.Type];
            AppendLine($"{target} = {fallbackType}(0); // Unmapped fallback");
        }


        // Casts
        public virtual void GenerateCode(IntAsPointerCast value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {source}; // intAsPtr");
        }

        public virtual void GenerateCode(PointerAsIntCast value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {target.Type}({source}); // ptrAsInt");
        }

        public virtual void GenerateCode(PointerCast value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            Declare(target);
            AppendLine($"{target} = {source}; // ptrCast");
        }

        public virtual void GenerateCode(AddressSpaceCast value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            if (target.Type.StartsWith("ptr<"))
            {
                // WGSL pointers cannot be declared with `var` (Declare() skips ptr types
                // for that reason). Emit a combined declare + initialize via `let` so
                // the variable is defined and bound in one statement. Track the name
                // in declaredVariables so subsequent Load() / Declare() don't re-emit.
                if (declaredVariables.Add(target.Name))
                    AppendLine($"let {target} = {source}; // addrSpaceCast");
                return;
            }
            Declare(target);
            AppendLine($"{target} = {source}; // addrSpaceCast");
        }

        public virtual void GenerateCode(FloatAsIntCast value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            Declare(target);
            // IR-level Float16 source on emulated path: WGSL type is "f32" (Half value
            // promoted to f32 for compute), but the f32 BIT PATTERN is NOT the Half BIT
            // PATTERN. AscendingHalf / DescendingHalf radix-sort encodings depend on
            // operating on the 16-bit Half bits (NumBits=16, sign at bit 15). A naive
            // bitcast<i32>(f32_holding_half_value) gives the f32 IEEE-754 bits instead,
            // which makes radix sort silently produce wrong output (every Half value in
            // the test range f32-encodes to the same low 16 bits = 0x0000 since the
            // mantissa lives in f32 bits 0..22). Round-trip through _f32_to_f16 to recover
            // the Half raw 16-bit pattern in the low 16 bits of a u32, then cast to i32.
            // Native f16 path (source.Type == "f16") keeps its existing packed-vec2 emit.
            bool isEmulatedHalfSource =
                value.Value.Type is global::ILGPU.IR.Types.PrimitiveType pt &&
                pt.BasicValueType == global::ILGPU.BasicValueType.Float16 &&
                source.Type == "f32";
            if (source.Type == "f16")
            {
                // f16 is 2 bytes, target is i32 (4 bytes) -- size-mismatched bitcast is invalid in WGSL.
                // Pack f16 into the low 16 bits of a u32 via vec2<f16>, then cast to i32.
                AppendLine($"{target} = i32(bitcast<u32>(vec2<f16>({source}, f16(0.0))));");
            }
            else if (isEmulatedHalfSource)
            {
                _kernelReferencesF16Helpers = true;
                AppendLine($"{target} = i32(_f32_to_f16({source}));");
            }
            else if (value.Value.Type is global::ILGPU.IR.Types.PrimitiveType bpt &&
                bpt.BasicValueType == global::ILGPU.BasicValueType.BFloat16)
            {
                // bf16 is always emulated (WGSL "f32"); FloatAsInt(bf16) must yield the 16-bit
                // bf16 pattern (AscendingBFloat16 radix sort, NumBits=16) - recover it via
                // _f32_to_bf16, not a bitcast of the promoted f32.
                _kernelReferencesBF16Helpers = true;
                AppendLine($"{target} = i32(_f32_to_bf16({source}));");
            }
            else if (value.Value.Type is global::ILGPU.IR.Types.PrimitiveType fpt &&
                (fpt.BasicValueType == global::ILGPU.BasicValueType.Float8E4M3 ||
                 fpt.BasicValueType == global::ILGPU.BasicValueType.Float8E5M2))
            {
                // FP8 is always emulated (WGSL "f32"); FloatAsInt(fp8) must yield the 8-bit FP8
                // pattern (AscendingFloat8E4M3/E5M2 radix sort, NumBits=8) - recover it via
                // _f32_to_e4m3/_f32_to_e5m2, not a bitcast of the promoted f32.
                _kernelReferencesFP8Helpers = true;
                var fn = fpt.BasicValueType == global::ILGPU.BasicValueType.Float8E4M3
                    ? "_f32_to_e4m3" : "_f32_to_e5m2";
                AppendLine($"{target} = i32({fn}({source}));");
            }
            else if (value.Value.Type is global::ILGPU.IR.Types.PrimitiveType f4pt &&
                f4pt.BasicValueType == global::ILGPU.BasicValueType.Float4E2M1)
            {
                // FP4 is always emulated (WGSL "f32"); FloatAsInt(fp4) must yield the 4-bit E2M1
                // pattern in the low nibble (AscendingFloat4E2M1 radix sort) - recover it via
                // _f32_to_e2m1, not a bitcast of the promoted f32.
                _kernelReferencesFP4Helpers = true;
                AppendLine($"{target} = i32(_f32_to_e2m1({source}));");
            }
            else if (source.Type == "emu_f64")
            {
                // emu_f64 is a Dekker vec2<f32> pair — not the raw IEEE-754 double bits.
                // f64_to_ieee754_bits reconstructs the original 64-bit bit pattern as vec2<u32>,
                // which matches the emu_i64/emu_u64 layout RadixSort bit-extraction requires.
                AppendLine($"{target} = f64_to_ieee754_bits({source});");
            }
            else
            {
                AppendLine($"{target} = bitcast<{target.Type}>({source});");
            }
        }

        public virtual void GenerateCode(IntAsFloatCast value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            Declare(target);
            // Symmetric inverse of FloatAsIntCast for emulated Half: IR-level Float16
            // target on emulated path is WGSL "f32", and the i32 source carries Half raw
            // bits in the low 16 bits. Decode via _f16_to_f32 to recover the value.
            bool isEmulatedHalfTarget =
                value.Type is global::ILGPU.IR.Types.PrimitiveType pt &&
                pt.BasicValueType == global::ILGPU.BasicValueType.Float16 &&
                target.Type == "f32";
            if (target.Type == "f16")
            {
                // i32 is 4 bytes, target f16 is 2 bytes -- size-mismatched bitcast is invalid in WGSL.
                // Reinterpret u32 as vec2<f16> and extract the low half.
                AppendLine($"{target} = bitcast<vec2<f16>>(u32({source})).x;");
            }
            else if (isEmulatedHalfTarget)
            {
                _kernelReferencesF16Helpers = true;
                AppendLine($"{target} = _f16_to_f32(u32({source}) & 0xFFFFu);");
            }
            else if (value.Type is global::ILGPU.IR.Types.PrimitiveType bpt &&
                bpt.BasicValueType == global::ILGPU.BasicValueType.BFloat16)
            {
                // Symmetric inverse for bf16 (always emulated): widen the low-16 bf16 pattern
                // to f32. Defensive - no IntAsFloat->BFloat16 frontend overload today.
                _kernelReferencesBF16Helpers = true;
                AppendLine($"{target} = _bf16_to_f32(u32({source}) & 0xFFFFu);");
            }
            else if (value.Type is global::ILGPU.IR.Types.PrimitiveType fpt &&
                (fpt.BasicValueType == global::ILGPU.BasicValueType.Float8E4M3 ||
                 fpt.BasicValueType == global::ILGPU.BasicValueType.Float8E5M2))
            {
                // Symmetric inverse for FP8 (always emulated): widen the low-8 FP8 pattern to
                // f32. Defensive - no IntAsFloat->Float8 frontend overload today.
                _kernelReferencesFP8Helpers = true;
                var fn = fpt.BasicValueType == global::ILGPU.BasicValueType.Float8E4M3
                    ? "_e4m3_to_f32" : "_e5m2_to_f32";
                AppendLine($"{target} = {fn}(u32({source}) & 0xFFu);");
            }
            else if (value.Type is global::ILGPU.IR.Types.PrimitiveType f4pt &&
                f4pt.BasicValueType == global::ILGPU.BasicValueType.Float4E2M1)
            {
                // Symmetric inverse for FP4 (always emulated): widen the low-nibble E2M1 pattern
                // to f32. Defensive - no IntAsFloat->Float4E2M1 frontend overload today.
                _kernelReferencesFP4Helpers = true;
                AppendLine($"{target} = _e2m1_to_f32(u32({source}) & 0x0Fu);");
            }
            else if (target.Type == "emu_f64")
            {
                // Reconstruct emu_f64 (Dekker vec2<f32>) from the IEEE-754 64-bit bit pattern
                // stored as vec2<u32> (emu_i64 / emu_u64 layout).
                AppendLine($"{target} = f64_from_ieee754_bits({source}.x, {source}.y);");
            }
            else
            {
                AppendLine($"{target} = bitcast<{target.Type}>({source});");
            }
        }

        /// <summary>
        /// Produces a WGSL expression that packs a scalar value into a u32 via bitcast.
        /// For f16 (2 bytes), a direct <c>bitcast&lt;u32&gt;(f16)</c> is invalid because WGSL
        /// requires bitcast operands to have matching byte sizes.  Instead we pack via
        /// <c>bitcast&lt;u32&gt;(vec2&lt;f16&gt;(val, f16(0.0)))</c>.
        /// </summary>
        protected static string BitcastToU32(string expr, string sourceWgslType)
        {
            if (sourceWgslType == "f16")
                return $"bitcast<u32>(vec2<f16>({expr}, f16(0.0)))";
            return $"bitcast<u32>({expr})";
        }

        /// <summary>
        /// Produces a WGSL expression that unpacks a u32 into the given scalar type via bitcast.
        /// For f16 (2 bytes), a direct <c>bitcast&lt;f16&gt;(u32)</c> is invalid.  Instead we
        /// unpack via <c>bitcast&lt;vec2&lt;f16&gt;&gt;(u32).x</c>.
        /// </summary>
        protected static string BitcastFromU32(string expr, string targetWgslType)
        {
            if (targetWgslType == "f16")
                return $"bitcast<vec2<f16>>({expr}).x";
            return $"bitcast<{targetWgslType}>({expr})";
        }

        // Atomics & Barriers

        /// <summary>
        /// Override in subclasses to indicate whether the atomic target pointer refers to a
        /// float-typed parameter. WGSL has no atomic&lt;f32&gt;, so float atomics are stored as
        /// atomic&lt;u32&gt; (raw bits) and emulated via a CAS loop.
        /// </summary>
        protected virtual bool IsFloatAtomicTarget(Value target) => false;

        /// <summary>
        /// Returns true if the atomic target pointer resolves to an unsigned-typed parameter.
        /// WGSL's atomicMin/atomicMax on i32 do signed comparisons, so unsigned reductions need
        /// atomic&lt;u32&gt; buffers and bitcast values to u32 for correct unsigned semantics.
        /// </summary>
        protected virtual bool IsUnsignedAtomicTarget(Value target) => false;

        /// <summary>
        /// Returns true if the atomic target pointer resolves to a buffer with emulated 64-bit
        /// element type (emu_i64, emu_u64, emu_f64). WGSL doesn't support atomic operations on
        /// vec2&lt;u32&gt;, so these use plain storage and direct stores.
        /// </summary>
        protected virtual bool IsEmulated64BitAtomicTarget(Value target) => false;

        public virtual void GenerateCode(GenericAtomic value)
        {
            var target = Load(value);
            var ptr = Load(value.Target);
            var val = Load(value.Value);
            Declare(target);

            // WGSL only supports atomicAdd/Min/Max/etc. on i32 and u32.
            // Emulated 64-bit types (emu_i64, emu_u64, emu_f64 = vec2<u32>) cannot use WGSL atomics.
            // The reduction algorithm already accumulates via shared memory; only thread 0 writes
            // the final result, so a plain store is safe (no race condition).
            //
            // Detection: check both the explicit body-struct tracking AND the value's IR type.
            // The IR value type for emulated 64-bit is vec2<u32> (BasicValueType.None for struct types),
            // but we can also check via TypeGenerator which maps to emu_i64/emu_u64/emu_f64.
            string valWgslType = TypeGenerator[value.Value.Type];
            bool isEmu64 = IsEmulated64BitAtomicTarget(value.Target)
                || valWgslType == "emu_i64" || valWgslType == "emu_u64" || valWgslType == "emu_f64";
            if (isEmu64)
            {
                // For emulated 64-bit: read-compare-write using emulation functions.
                // Only thread 0 writes, so no race, but we still emit a compare-and-store
                // pattern since the output buffer may have the identity value.
                string prefix = valWgslType switch
                {
                    "emu_f64" => "f64",
                    "emu_u64" => "u64",
                    // NOTE: TypeGenerator returns "emu_i64" for BOTH long AND ulong, because ILGPU's
                    // BasicValueType enum has no UInt64 member. Must also check value.IsUnsigned.
                    "emu_i64" => value.IsUnsigned ? "u64" : "i64",
                    _ => value.IsUnsigned ? "u64" : "i64"
                };

                string emuOp = value.Kind switch
                {
                    AtomicKind.Max => $"{prefix}_max",
                    AtomicKind.Min => $"{prefix}_min",
                    AtomicKind.Add => $"{prefix}_add",
                    AtomicKind.Exchange => null,  // plain store
                    _ => $"{prefix}_add"
                };
                if (emuOp != null)
                {
                    // Read current value, apply op, write back
                    string oldVar = $"_emu64_old_{target.Name}";
                    if (valWgslType == "emu_f64")
                    {
                        // emu_f64 (Dekker/Ozaki) atomics: f64 is stored as two u32 words
                        // (IEEE-754 bits). Like i64, we can't atomically update both words.
                        // No lock-free solution exists for f64 arithmetic atomics on WebGPU.
                        throw new NotSupportedException(
                            $"Atomic.{value.Kind} on Float64 is not supported on the WebGPU backend. " +
                            $"Float64 is emulated as two 32-bit words; atomic arithmetic requires both " +
                            $"words to update atomically, which WGSL does not provide. " +
                            $"Use Float32 atomics or restructure to avoid Float64 atomics.");
                    }
                    else
                    {
                        // Check if this is a bitwise op - can use independent i32 atomics
                        bool isBitwiseOp = value.Kind == AtomicKind.And
                            || value.Kind == AtomicKind.Or
                            || value.Kind == AtomicKind.Xor;

                        if (isBitwiseOp)
                        {
                            // Bitwise atomics on emulated i64: two independent i32 atomics
                            string atomicOp = value.Kind switch
                            {
                                AtomicKind.And => "atomicAnd",
                                AtomicKind.Or => "atomicOr",
                                AtomicKind.Xor => "atomicXor",
                                _ => "atomicAnd"
                            };
                            // ptr points to vec2<u32> backed by atomic<u32> storage
                            // Access individual components via pointer arithmetic
                            string oldLoVar = $"_emu64_old_lo_{target.Name}";
                            string oldHiVar = $"_emu64_old_hi_{target.Name}";
                            AppendLine($"// i64 bitwise atomic: independent i32 atomics on lo/hi halves");
                            AppendLine($"let {oldLoVar} = {atomicOp}(&(*{ptr}).x, {val}.x);");
                            AppendLine($"let {oldHiVar} = {atomicOp}(&(*{ptr}).y, {val}.y);");
                            AppendLine($"{target} = {valWgslType}({oldLoVar}, {oldHiVar});");
                        }
                        else if (value.Kind == AtomicKind.Add)
                        {
                            // i64 atomicAdd: lock-free CAS loop on lo half + atomicAdd on hi half.
                            // CAS on lo serializes lo-half updates. atomicAdd on hi is commutative,
                            // so concurrent carries from multiple threads produce the correct result
                            // regardless of ordering. No lock buffer needed.
                            string oldLoVar = $"_emu64_old_lo_{target.Name}";
                            string newLoVar = $"_emu64_new_lo_{target.Name}";
                            string carryVar = $"_emu64_carry_{target.Name}";
                            string casResVar = $"_emu64_cas_{target.Name}";
                            AppendLine($"// i64 atomicAdd: CAS-lo + atomicAdd-hi (lock-free)");
                            AppendLine($"var {oldLoVar} = atomicLoad(&(*{ptr}).x);");
                            AppendLine($"loop {{");
                            PushIndent();
                            AppendLine($"let {newLoVar} = {oldLoVar} + {val}.x;");
                            AppendLine($"let {carryVar} = select(0u, 1u, {newLoVar} < {oldLoVar});");
                            AppendLine($"let {casResVar} = atomicCompareExchangeWeak(&(*{ptr}).x, {oldLoVar}, {newLoVar});");
                            AppendLine($"if ({casResVar}.exchanged) {{");
                            PushIndent();
                            AppendLine($"let _old_hi_{target.Name} = atomicAdd(&(*{ptr}).y, {val}.y + {carryVar});");
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
                            // i64 Min/Max: not safely implementable with only i32 atomics
                            // without a spinlock. Both halves must be read and compared as a
                            // single 64-bit value, which requires atomic consistency across
                            // two separate u32 words. Throw rather than silently produce
                            // wrong results.
                            throw new NotSupportedException(
                                $"Atomic.{value.Kind} on Int64/UInt64 is not supported on the WebGPU backend. " +
                                $"WebGPU only has 32-bit atomics; i64 {value.Kind} requires 64-bit compare-and-swap " +
                                $"which WGSL does not provide. Use i32 atomics or restructure to avoid i64 {value.Kind}.");
                        }
                    }
                }

                else
                {
                    // i64 Exchange: not safely implementable with only i32 atomics.
                    // Both halves must update atomically or a reader sees torn state.
                    // Plain store was here before - silently produced wrong results.
                    throw new NotSupportedException(
                        $"Atomic.Exchange on Int64/UInt64 is not supported on the WebGPU backend. " +
                        $"WebGPU only has 32-bit atomics; i64 Exchange requires both halves to update " +
                        $"atomically, which WGSL does not provide. Use i32 atomics or restructure.");
                }
                return;
            }

            // Float atomics (f32/f16) must be emulated using a CAS loop on atomic<u32>.
            if (IsFloatAtomicTarget(value.Target))
            {
                // The buffer is declared as atomic<u32>. We bitcast f32 <-> u32.
                // CAS loop: atomically read-modify-write the float value.
                string casOld = $"_cas_old_{target.Name}";
                string casNew = $"_cas_new_{target.Name}";
                string casRes = $"_cas_res_{target.Name}";

                string floatType = target.Type;
                AppendLine($"var {casOld} = atomicLoad({ptr});");
                AppendLine($"loop {{");
                PushIndent();

                string floatOld = BitcastFromU32(casOld, floatType);
                string newFloat = value.Kind switch
                {
                    AtomicKind.Add => $"{floatOld} + {val}",
                    AtomicKind.Max => $"max({floatOld}, {val})",
                    AtomicKind.Min => $"min({floatOld}, {val})",
                    AtomicKind.Exchange => $"{val}",
                    _ => throw new NotSupportedException($"Unsupported float atomic kind: {value.Kind}")
                };

                AppendLine($"let {casNew} = {BitcastToU32(newFloat, floatType)};");
                AppendLine($"let {casRes} = atomicCompareExchangeWeak({ptr}, {casOld}, {casNew});");
                AppendLine($"if ({casRes}.exchanged) {{ break; }}");
                AppendLine($"{casOld} = {casRes}.old_value;");
                PopIndent();
                AppendLine($"}}");
                AppendLine($"{target} = {BitcastFromU32(casOld, floatType)};");
            }
            else if (IsUnsignedAtomicTarget(value.Target))
            {
                // Buffer is atomic<u32>. Values from ILGPU IR are i32, need bitcast to u32.
                string op = value.Kind switch
                {
                    AtomicKind.Add => "atomicAdd",
                    AtomicKind.And => "atomicAnd",
                    AtomicKind.Or => "atomicOr",
                    AtomicKind.Xor => "atomicXor",
                    AtomicKind.Max => "atomicMax",
                    AtomicKind.Min => "atomicMin",
                    AtomicKind.Exchange => "atomicExchange",
                    _ => throw new NotSupportedException($"Unsupported unsigned atomic kind: {value.Kind}")
                };

                // Bitcast value to u32 for unsigned atomic semantics
                AppendLine($"{target} = bitcast<i32>({op}({ptr}, bitcast<u32>({val})));");
            }
            else
            {
                string op = value.Kind switch
                {
                    AtomicKind.Add => "atomicAdd",
                    AtomicKind.And => "atomicAnd",
                    AtomicKind.Or => "atomicOr",
                    AtomicKind.Xor => "atomicXor",
                    AtomicKind.Max => "atomicMax",
                    AtomicKind.Min => "atomicMin",
                    AtomicKind.Exchange => "atomicExchange",
                    _ => throw new NotSupportedException($"Unsupported atomic kind: {value.Kind}")
                };

                AppendLine($"{target} = {op}({ptr}, {val});");
            }
        }


        public virtual void GenerateCode(AtomicCAS value)
        {
            var target = Load(value);
            var ptr = Load(value.Target);
            var cmp = Load(value.Value);
            var val = Load(value.CompareValue);

            // i64 CAS: WGSL has no 64-bit atomicCompareExchangeWeak.
            // i64 is emulated as vec2<u32> and WGSL only has 32-bit CAS.
            // Cannot CAS two u32 halves atomically without hardware support.
            var targetType = TypeGenerator[value.Type];
            if (targetType == "emu_i64" || targetType == "emu_u64")
            {
                throw new NotSupportedException(
                    "Atomic.CompareExchange on Int64/UInt64 is not supported on the WebGPU backend. " +
                    "WebGPU only has 32-bit atomicCompareExchangeWeak; i64 CAS requires a 64-bit " +
                    "compare-and-swap which WGSL does not provide.");
            }

            Declare(target);
            AppendLine($"{target} = atomicCompareExchangeWeak({ptr}, {cmp}, {val}).old_value;");
        }



        public virtual void GenerateCode(MemoryBarrier value)
        {
            AppendLine("workgroupBarrier();");
        }

        public virtual void GenerateCode(PredicateBarrier value)
        {
            AppendLine("workgroupBarrier();");
        }

        // Warp Operations
        public virtual void GenerateCode(Broadcast value)
        {
            var target = Load(value);
            var source = Load(value.Variable);
            var origin = Load(value.Origin);
            Declare(target);

            if (value.Kind == global::ILGPU.IR.Values.BroadcastKind.GroupLevel)
            {
                // Workgroup-level broadcast: use shared memory + barrier
                // 1. Origin thread writes value to shared memory
                // 2. All threads synchronize
                // 3. All threads read from shared memory
                AppendLine($"if (i32(local_index) == {origin}) {{");
                PushIndent();
                AppendLine($"_broadcast_temp = {source};");
                PopIndent();
                AppendLine("}");
                AppendLine("workgroupBarrier();");
                AppendLine($"{target} = _broadcast_temp;");
                AppendLine("workgroupBarrier();");
            }
            else
            {
                // WarpLevel broadcast — broadcast from lane 0 of the warp
                if (HasSubgroups)
                {
                    AppendLine($"{target} = subgroupBroadcastFirst({source});");
                }
                else
                {
                    // Shared-memory emulation: write all values, read from warp-base (lane 0)
                    var warpBase = $"_warp_base_{target.Name}";
                    AppendLine($"_warp_shuffle_buf[local_index] = {BitcastToU32($"{source}", source.Type)};");
                    AppendLine("workgroupBarrier();");
                    AppendLine($"let {warpBase} = (local_index / workgroup_size.x) * workgroup_size.x;");
                    AppendLine($"{target} = {BitcastFromU32($"_warp_shuffle_buf[{warpBase}]", target.Type)};");
                    AppendLine("workgroupBarrier();");
                }
            }
        }

        public virtual void GenerateCode(WarpShuffle value)
        {
            var target = Load(value);
            var source = Load(value.Variable);
            var origin = Load(value.Origin);
            Declare(target);
            if (HasSubgroups)
            {
                AppendLine($"{target} = subgroupShuffle({source}, u32({origin}));");
            }
            else
            {
                // Shared-memory emulation:
                // 'origin' is a lane index (0..warpSize-1), not a global thread index.
                // The buffer is indexed by local_index, so we must compute:
                //   warp_base = (local_index / warp_size) * warp_size
                //   src_idx   = warp_base + origin
                var warpBase = $"_wb_{target.Name}";
                AppendLine($"_warp_shuffle_buf[local_index] = {BitcastToU32($"{source}", source.Type)};");
                AppendLine("workgroupBarrier();");
                AppendLine($"let {warpBase} = (local_index / u32(workgroup_size.x)) * u32(workgroup_size.x);");
                AppendLine($"{target} = {BitcastFromU32($"_warp_shuffle_buf[{warpBase} + u32({origin})]", target.Type)};");
                AppendLine("workgroupBarrier();");
            }
        }

        public virtual void GenerateCode(SubWarpShuffle value)
        {
            var target = Load(value);
            var source = Load(value.Variable);
            var origin = Load(value.Origin);
            Declare(target);
            if (HasSubgroups)
            {
                AppendLine($"{target} = subgroupShuffle({source}, u32({origin}));");
            }
            else
            {
                // Same fix as WarpShuffle: origin is lane-relative, buffer is global.
                var warpBase = $"_wb_{target.Name}";
                AppendLine($"_warp_shuffle_buf[local_index] = {BitcastToU32($"{source}", source.Type)};");
                AppendLine("workgroupBarrier();");
                AppendLine($"let {warpBase} = (local_index / u32(workgroup_size.x)) * u32(workgroup_size.x);");
                AppendLine($"{target} = {BitcastFromU32($"_warp_shuffle_buf[{warpBase} + u32({origin})]", target.Type)};");
                AppendLine("workgroupBarrier();");
            }
        }

        // Debug/IO
        public virtual void GenerateCode(DebugAssertOperation value)
        {
            // WGSL doesn't support debug assertions
        }

        public virtual void GenerateCode(WriteToOutput value)
        {
            // WGSL doesn't support stdout
        }

        // Other
        public virtual void GenerateCode(Predicate value)
        {
            var target = Load(value);
            var cond = Load(value.Condition);
            Declare(target);

            bool trueHasLoad = value.TrueValue is global::ILGPU.IR.Values.Load;
            bool falseHasLoad = value.FalseValue is global::ILGPU.IR.Values.Load;

            if (trueHasLoad || falseHasLoad)
            {
                var trueVal = Load(value.TrueValue);
                var falseVal = Load(value.FalseValue);
                AppendLine($"if ({cond}) {{");
                AppendLine($"    {target} = {trueVal};");
                AppendLine($"}} else {{");
                AppendLine($"    {target} = {falseVal};");
                AppendLine($"}}");
            }
            else
            {
                var trueVal = Load(value.TrueValue);
                var falseVal = Load(value.FalseValue);
                AppendLine($"{target} = select({falseVal}, {trueVal}, {cond});");
            }
        }

        public virtual void GenerateCode(DynamicMemoryLengthValue value)
        {
            var target = Load(value);
            Declare(target);
            // Use i32 literal (not u32) since the target variable is i32
            AppendLine($"{target} = 0; // dynamic memory length (placeholder)");
        }

        public virtual void GenerateCode(AlignTo value)
        {
            var target = Load(value);
            var source = Load(value.Source);
            Declare(target);
            AppendLine($"{target} = {source}; // alignTo");
        }

        public virtual void GenerateCode(AsAligned value)
        {
            var target = Load(value);
            var source = Load(value.Source);
            Declare(target);
            AppendLine($"{target} = {source}; // asAligned");
        }

        public virtual void GenerateCode(LanguageEmitValue value)
        {
            // Raw WGSL emission - not commonly used
        }

        public virtual void GenerateThrow(Value value)
        {
            // WebGPU does not support exceptions. 
            // We emit a trap/unreachable, or just a comment if we assume it's unreachable in valid code.
            AppendLine($"// [WGSL] Throw encountered: {value} (Ignored/Unreachable)");

            if (IsStateMachineActive)
            {
                // Break out of the loop
                AppendLine("current_block = -1;");
                AppendLine("break;");
            }
            else
            {
                AppendLine("return;");
            }
        }

        #endregion

        #region Half Conversion Intrinsics

        /// <summary>
        /// Handles <c>HalfExtensions.ConvertHalfToFloat(Half)</c> by emitting a native
        /// WGSL <c>f32(source)</c> conversion instead of the lookup-table implementation
        /// that the CPU path uses. The lookup tables are static .NET arrays that cannot
        /// be accessed from GPU code, so inlining the default implementation produces
        /// all-zero results.
        ///
        /// Emulated path (<c>!HasShaderF16</c>): Half locals are already represented as
        /// <c>f32</c> values in WGSL (see <c>WGSLTypeGenerator</c>), so Half→float is a
        /// pass-through at the IR level. No conversion needed.
        /// </summary>
        public static void GenerateConvertHalfToFloat(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                codeGenerator.Declare(target);
                if (backend.HasShaderF16)
                    codeGenerator.AppendLine($"{target} = f32({operand});");
                else
                    // In emulated mode Half is already f32 - pass through
                    codeGenerator.AppendLine($"{target} = {operand};");
            }
        }

        /// <summary>
        /// Handles <c>HalfExtensions.ConvertFloatToHalf(float)</c> by emitting a native
        /// WGSL <c>f16(source)</c> conversion.
        ///
        /// Emulated path (<c>!HasShaderF16</c>): Half locals are <c>f32</c>, but must carry
        /// Float16 precision. Emits <c>_f16_to_f32(_f32_to_f16(source))</c> to round-trip
        /// through the 16-bit bit pattern, which applies Float16 precision while keeping
        /// the WGSL type <c>f32</c>.
        /// </summary>
        public static void GenerateConvertFloatToHalf(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                codeGenerator.Declare(target);
                if (backend.HasShaderF16)
                    codeGenerator.AppendLine($"{target} = f16({operand});");
                else
                    // In emulated mode Half is f32-typed but needs f16 precision - round-trip
                    codeGenerator.AppendLine($"{target} = _f16_to_f32(_f32_to_f16({operand}));");
            }
        }

        #endregion

        #region Math Intrinsics

        public static void GenerateAbs(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                codeGenerator.Declare(target);
                codeGenerator.AppendLine($"{target} = abs({operand});");
            }
        }

        public static void GenerateSign(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                codeGenerator.Declare(target);
                string call = $"sign({operand})";
                if (codeGenerator.TypeGenerator[value.Type] == "i32" || codeGenerator.TypeGenerator[value.Type] == "u32")
                {
                    call = $"i32({call})";
                }
                codeGenerator.AppendLine($"{target} = {call};");
            }
        }

        public static void GenerateRound(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                codeGenerator.Declare(target);
                codeGenerator.AppendLine($"{target} = round({operand});");
            }
        }

        public static void GenerateTruncate(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                codeGenerator.Declare(target);
                codeGenerator.AppendLine($"{target} = trunc({operand});");
            }
        }

        public static void GenerateAtan2(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var y = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                var x = codeGenerator.LoadIntrinsicValue(methodCall[1].Resolve());
                codeGenerator.Declare(target);
                codeGenerator.AppendLine($"{target} = atan2({y}, {x});");
            }
        }

        public static void GenerateMax(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var a = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                var b = codeGenerator.LoadIntrinsicValue(methodCall[1].Resolve());
                codeGenerator.Declare(target);
                codeGenerator.AppendLine($"{target} = max({a}, {b});");
            }
        }

        public static void GenerateMin(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var a = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                var b = codeGenerator.LoadIntrinsicValue(methodCall[1].Resolve());
                codeGenerator.Declare(target);
                codeGenerator.AppendLine($"{target} = min({a}, {b});");
            }
        }

        public static void GeneratePow(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var b = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                var e = codeGenerator.LoadIntrinsicValue(methodCall[1].Resolve());
                codeGenerator.Declare(target);
                // Same negative-base guard as the kernel BinaryArithmetic PowF path.
                codeGenerator.AppendLine($"{target} = pow(abs({b}), {e}) * select(1.0, -1.0, ({b} < 0.0) && ((abs({e}) % 2.0) >= 1.0));");
            }
        }

        public static void GenerateClamp(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var val = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                var min = codeGenerator.LoadIntrinsicValue(methodCall[1].Resolve());
                var max = codeGenerator.LoadIntrinsicValue(methodCall[2].Resolve());
                codeGenerator.Declare(target);
                codeGenerator.AppendLine($"{target} = clamp({val}, {min}, {max});");
            }
        }

        public static void GenerateFusedMultiplyAdd(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var x = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                var y = codeGenerator.LoadIntrinsicValue(methodCall[1].Resolve());
                var z = codeGenerator.LoadIntrinsicValue(methodCall[2].Resolve());
                codeGenerator.Declare(target);
                codeGenerator.AppendLine($"{target} = fma({x}, {y}, {z});");
            }
        }

        /// <summary>
        /// Generates WGSL code for XMath.Rsqrt (reciprocal square root: 1/sqrt(x)).
        /// </summary>
        public static void GenerateRsqrt(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                codeGenerator.Declare(target);
                // WGSL has inverseSqrt but we use 1.0/sqrt() for clarity
                codeGenerator.AppendLine($"{target} = 1.0 / sqrt({operand});");
            }
        }

        /// <summary>
        /// Generates WGSL code for XMath.Rcp (reciprocal: 1/x).
        /// </summary>
        public static void GenerateRcp(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is MethodCall methodCall)
            {
                var target = codeGenerator.LoadIntrinsicValue(value);
                var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
                codeGenerator.Declare(target);
                codeGenerator.AppendLine($"{target} = 1.0 / {operand};");
            }
        }

        /// <summary>
        /// Generates WGSL code for a warp-level reduction (WarpExtensions.Reduce).
        /// Emits subgroupMax/Min/Add when subgroups are available, otherwise falls back
        /// to a shared-memory butterfly reduction within the warp.
        /// The reduction operation type is determined by TReduction.CLCommand.
        /// </summary>
        public static void GenerateWarpReduce(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (value is not MethodCall methodCall) return;

            var target = codeGenerator.LoadIntrinsicValue(value);
            var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
            codeGenerator.Declare(target);

            var srcMethodInfo = methodCall.Target.Source as MethodInfo;
            string wgslOp = GetSubgroupReduceOp(srcMethodInfo);

            if (backend.HasSubgroups && wgslOp != null)
            {
                codeGenerator.AppendLine($"{target} = {wgslOp}({operand});");
                return;
            }

            // Shared-memory binary-tree warp reduce (subgroups unavailable).
            // Each "warp" is workgroup_size.x threads (consistent with WarpShuffle emulation).
            // Lane 0..workgroup_size.x-1 all contribute; after the loop, lane 0 holds the
            // final result and all lanes read from their warp's slot 0.
            string irType = codeGenerator.TypeGenerator[methodCall.Type];
            string wgslType = codeGenerator.GetReductionWgslType(srcMethodInfo) ?? irType;
            bool needsBitcast = wgslType != irType;
            bool is64Bit = wgslType.StartsWith("emu_");
            bool isOzakiF64 = wgslType == "emu_f64" && backend.UseOzakiF64Emulation;
            string wgslAccumOp = GetWarpReduceAccumOp(wgslOp);
            string sfx = target.Name.Replace("v_", "_").TrimStart('_');

            codeGenerator.UsesWarpShuffleEmulation = true;

            // Step 1: all threads write their value into the shared buffer.
            string storeVal = needsBitcast ? $"bitcast<{wgslType}>({operand})" : $"{operand}";
            if (isOzakiF64)
            {
                codeGenerator.AppendLine($"{{ _warp_shuffle_buf[local_index * 4u] = bitcast<u32>({storeVal}.x); _warp_shuffle_buf[local_index * 4u + 1u] = bitcast<u32>({storeVal}.y); _warp_shuffle_buf[local_index * 4u + 2u] = bitcast<u32>({storeVal}.z); _warp_shuffle_buf[local_index * 4u + 3u] = bitcast<u32>({storeVal}.w); }}");
            }
            else if (is64Bit && wgslType == "emu_f64")
            {
                codeGenerator.AppendLine($"{{ let _wbits_{sfx} = f64_to_ieee754_bits({storeVal}); _warp_shuffle_buf[local_index * 2u] = _wbits_{sfx}.x; _warp_shuffle_buf[local_index * 2u + 1u] = _wbits_{sfx}.y; }}");
            }
            else if (is64Bit)
            {
                codeGenerator.AppendLine($"_warp_shuffle_buf[local_index * 2u] = {storeVal}.x;");
                codeGenerator.AppendLine($"_warp_shuffle_buf[local_index * 2u + 1u] = {storeVal}.y;");
            }
            else
            {
                codeGenerator.AppendLine($"_warp_shuffle_buf[local_index] = {BitcastToU32(storeVal, wgslType)};");
            }
            codeGenerator.AppendLine("workgroupBarrier();");

            // Build load expressions for "self" and "neighbor at local_index + _ws_<sfx>".
            // These strings are WGSL fragments referencing the loop variable _ws_<sfx>.
            string loadSelf, loadNeighbor;
            if (isOzakiF64)
            {
                loadSelf     = $"emu_f64(bitcast<f32>(_warp_shuffle_buf[local_index * 4u]), bitcast<f32>(_warp_shuffle_buf[local_index * 4u + 1u]), bitcast<f32>(_warp_shuffle_buf[local_index * 4u + 2u]), bitcast<f32>(_warp_shuffle_buf[local_index * 4u + 3u]))";
                loadNeighbor = $"emu_f64(bitcast<f32>(_warp_shuffle_buf[(local_index + _ws_{sfx}) * 4u]), bitcast<f32>(_warp_shuffle_buf[(local_index + _ws_{sfx}) * 4u + 1u]), bitcast<f32>(_warp_shuffle_buf[(local_index + _ws_{sfx}) * 4u + 2u]), bitcast<f32>(_warp_shuffle_buf[(local_index + _ws_{sfx}) * 4u + 3u]))";
            }
            else if (is64Bit && wgslType == "emu_f64")
            {
                loadSelf     = $"f64_from_ieee754_bits(_warp_shuffle_buf[local_index * 2u], _warp_shuffle_buf[local_index * 2u + 1u])";
                loadNeighbor = $"f64_from_ieee754_bits(_warp_shuffle_buf[(local_index + _ws_{sfx}) * 2u], _warp_shuffle_buf[(local_index + _ws_{sfx}) * 2u + 1u])";
            }
            else if (is64Bit)
            {
                loadSelf     = $"{wgslType}(_warp_shuffle_buf[local_index * 2u], _warp_shuffle_buf[local_index * 2u + 1u])";
                loadNeighbor = $"{wgslType}(_warp_shuffle_buf[(local_index + _ws_{sfx}) * 2u], _warp_shuffle_buf[(local_index + _ws_{sfx}) * 2u + 1u])";
            }
            else
            {
                loadSelf     = BitcastFromU32("_warp_shuffle_buf[local_index]", wgslType);
                loadNeighbor = BitcastFromU32($"_warp_shuffle_buf[local_index + _ws_{sfx}]", wgslType);
            }

            // Build write-back expressions for self slot after accumulation.
            string writeSelf;
            if (isOzakiF64)
                writeSelf = $"{{ _warp_shuffle_buf[local_index * 4u] = bitcast<u32>(_wr_{sfx}.x); _warp_shuffle_buf[local_index * 4u + 1u] = bitcast<u32>(_wr_{sfx}.y); _warp_shuffle_buf[local_index * 4u + 2u] = bitcast<u32>(_wr_{sfx}.z); _warp_shuffle_buf[local_index * 4u + 3u] = bitcast<u32>(_wr_{sfx}.w); }}";
            else if (is64Bit && wgslType == "emu_f64")
                writeSelf = $"{{ let _wb_{sfx} = f64_to_ieee754_bits(_wr_{sfx}); _warp_shuffle_buf[local_index * 2u] = _wb_{sfx}.x; _warp_shuffle_buf[local_index * 2u + 1u] = _wb_{sfx}.y; }}";
            else if (is64Bit)
                writeSelf = $"{{ _warp_shuffle_buf[local_index * 2u] = _wr_{sfx}.x; _warp_shuffle_buf[local_index * 2u + 1u] = _wr_{sfx}.y; }}";
            else
                writeSelf = $"_warp_shuffle_buf[local_index] = {BitcastToU32($"_wr_{sfx}", wgslType)};";

            // Build load-result expression that reads from warp base (lane 0 of this warp).
            string warpBase0 = "(local_index / workgroup_size.x) * workgroup_size.x";
            string resultExpr;
            if (isOzakiF64)
                resultExpr = $"emu_f64(bitcast<f32>(_warp_shuffle_buf[{warpBase0} * 4u]), bitcast<f32>(_warp_shuffle_buf[{warpBase0} * 4u + 1u]), bitcast<f32>(_warp_shuffle_buf[{warpBase0} * 4u + 2u]), bitcast<f32>(_warp_shuffle_buf[{warpBase0} * 4u + 3u]))";
            else if (is64Bit && wgslType == "emu_f64")
                resultExpr = $"f64_from_ieee754_bits(_warp_shuffle_buf[{warpBase0} * 2u], _warp_shuffle_buf[{warpBase0} * 2u + 1u])";
            else if (is64Bit)
                resultExpr = $"{wgslType}(_warp_shuffle_buf[{warpBase0} * 2u], _warp_shuffle_buf[{warpBase0} * 2u + 1u])";
            else
                resultExpr = BitcastFromU32($"_warp_shuffle_buf[{warpBase0}]", wgslType);

            string accumExpr = GetEmulated64BitAccumExpr(is64Bit, wgslType, wgslAccumOp, $"_wr_{sfx}", $"_wn_{sfx}");
            string assignResult = needsBitcast
                ? $"    {target} = bitcast<{irType}>({resultExpr});"
                : $"    {target} = {resultExpr};";

            // Step 2: binary-tree butterfly — upper half contributes to lower half each round.
            codeGenerator.AppendLine("{");
            codeGenerator.AppendLine($"    var _wr_{sfx} : {wgslType} = {loadSelf};");
            codeGenerator.AppendLine($"    let _wl_{sfx} = local_index % workgroup_size.x;");
            codeGenerator.AppendLine($"    for (var _ws_{sfx} = workgroup_size.x / 2u; _ws_{sfx} > 0u; _ws_{sfx} = _ws_{sfx} / 2u) {{");
            codeGenerator.AppendLine($"        if (_wl_{sfx} < _ws_{sfx}) {{");
            codeGenerator.AppendLine($"            let _wn_{sfx} = {loadNeighbor};");
            codeGenerator.AppendLine($"            _wr_{sfx} = {accumExpr};");
            codeGenerator.AppendLine("        }");
            codeGenerator.AppendLine("        workgroupBarrier();");
            codeGenerator.AppendLine($"        {writeSelf}");
            codeGenerator.AppendLine("        workgroupBarrier();");
            codeGenerator.AppendLine("    }");
            // Step 3: all threads read the final result from their warp's lane-0 slot.
            codeGenerator.AppendLine(assignResult);
            codeGenerator.AppendLine("}");
            codeGenerator.AppendLine("workgroupBarrier();");
        }

        /// <summary>
        /// Generates WGSL code for a group-level reduction (GroupExtensions.Reduce/AllReduce,
        /// ILGroupExtensions.Reduce/AllReduce).
        /// Emits a full cross-subgroup reduction: per-subgroup native reduce (subgroupMax/Min/Add),
        /// then aggregates partial results across all subgroups via the _grp_sg_results workgroup array.
        /// Requires subgroup_id and subgroup_invocation_id builtins (added to kernel signature automatically).
        /// </summary>
        public static void GenerateGroupReduce(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
            => GenerateGroupAllReduce(backend, codeGenerator, value);

        /// <summary>
        /// Core implementation for group-level AllReduce. Emits inline WGSL that:
        /// <list type="number">
        ///   <item>Does a per-subgroup native reduce (subgroupMax / subgroupMin / subgroupAdd).</item>
        ///   <item>Lane 0 of each subgroup stores its partial result to _grp_sg_results[subgroup_id].</item>
        ///   <item>workgroupBarrier() synchronises.</item>
        ///   <item>All threads in subgroup 0 load from _grp_sg_results[their_lane] (padded with identity) and do a
        ///         second subgroup reduce — thread 0 of subgroup 0 then holds the workgroup-wide result.</item>
        ///   <item>Thread 0,0 writes the result back to slot 0; another workgroupBarrier(); all threads read slot 0.</item>
        /// </list>
        /// Sets UsesGroupReduce = true so the kernel header emits _grp_sg_results/atomic vars and the
        /// signature includes subgroup_id / subgroup_size / subgroup_invocation_id.
        /// </summary>
        public static void GenerateGroupAllReduce(WebGPUBackend backend, WGSLCodeGenerator codeGenerator, Value value)
        {
            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-DBG] GenerateGroupAllReduce called, value type={value?.GetType().Name}, isMethodCall={value is MethodCall}");
            if (value is not MethodCall methodCall)
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log("[WGSL-DBG] GenerateGroupAllReduce: early exit - not a MethodCall");
                return;
            }

            // Signal that the kernel needs the workgroup-level shared arrays and subgroup_id builtin.
            codeGenerator.UsesGroupReduce = true;

            var target = codeGenerator.LoadIntrinsicValue(value);
            var operand = codeGenerator.LoadIntrinsicValue(methodCall[0].Resolve());
            codeGenerator.Declare(target);

            // Determine the WGSL subgroup operation and identity value from TReduction type argument.
            var srcMethodInfo = methodCall.Target.Source as MethodInfo;
            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-DBG] srcMethodInfo={srcMethodInfo?.Name}, isGeneric={srcMethodInfo?.IsGenericMethod}, genericArgs={string.Join(",", srcMethodInfo?.GetGenericArguments()?.Select(t => t.Name) ?? Array.Empty<string>())}");
            string? wgslOp = GetSubgroupReduceOp(srcMethodInfo);
            string? identityExpr = GetReductionIdentityExpr(srcMethodInfo);

            // Determine the WGSL scalar type for this reduction early (needed for is64Bit check)
            string irType = codeGenerator.TypeGenerator[methodCall.Type];
            string wgslType = codeGenerator.GetReductionWgslType(srcMethodInfo) ?? irType;
            bool needsBitcast = wgslType != irType;
            bool is64Bit = wgslType.StartsWith("emu_"); // emu_i64, emu_u64, emu_f64 are vec2 types

            if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-DBG] HasSubgroups={backend.HasSubgroups}, wgslOp={wgslOp}, identityExpr={identityExpr}");

            if (is64Bit && wgslOp != null && identityExpr != null)
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-DBG] GenerateGroupAllReduce: 64-bit shared-memory reduction, wgslType={wgslType}");
                // For 64-bit emulated types, perform a complete shared-memory reduction
                // since subgroup ops don't support vec2<u32>.
                codeGenerator.UsesWarpShuffleEmulation = true;

                // Determine accumulation op
                string wgslAccumOp;
                if (wgslOp.EndsWith("Max", StringComparison.Ordinal))
                    wgslAccumOp = "max";
                else if (wgslOp.EndsWith("Min", StringComparison.Ordinal))
                    wgslAccumOp = "min";
                else
                    wgslAccumOp = "+";

                string sfx64 = target.Name.Replace("v_", "_").TrimStart('_');

                // Step 1: all threads write their value to shared memory (2 u32 slots per thread)
                string storeVal = needsBitcast ? $"bitcast<{wgslType}>({operand})" : $"{operand}";

                bool isOzakiF64 = wgslType == "emu_f64" && backend.UseOzakiF64Emulation;
                if (isOzakiF64)
                {
                    // Ozaki emu_f64 = vec4<f32>: store all 4 components
                    codeGenerator.AppendLine($"{{ _warp_shuffle_buf[local_index * 4u] = bitcast<u32>({storeVal}.x); _warp_shuffle_buf[local_index * 4u + 1u] = bitcast<u32>({storeVal}.y); _warp_shuffle_buf[local_index * 4u + 2u] = bitcast<u32>({storeVal}.z); _warp_shuffle_buf[local_index * 4u + 3u] = bitcast<u32>({storeVal}.w); }}");
                }
                else if (wgslType == "emu_f64")
                {
                    // Dekker emu_f64 = vec2<f32>: convert to IEEE-754 bits (2 u32 slots)
                    codeGenerator.AppendLine($"{{ let _bits = f64_to_ieee754_bits({storeVal}); _warp_shuffle_buf[local_index * 2u] = _bits.x; _warp_shuffle_buf[local_index * 2u + 1u] = _bits.y; }}");
                }
                else
                {
                    codeGenerator.AppendLine($"_warp_shuffle_buf[local_index * 2u] = {storeVal}.x;");
                    codeGenerator.AppendLine($"_warp_shuffle_buf[local_index * 2u + 1u] = {storeVal}.y;");
                }
                codeGenerator.AppendLine("workgroupBarrier();");

                // Step 2: all threads cooperatively reduce across the workgroup
                string load0;
                string loadN;
                if (isOzakiF64)
                {
                    // Ozaki: reconstruct vec4<f32> from 4 u32 slots
                    load0 = $"emu_f64(bitcast<f32>(_warp_shuffle_buf[0u]), bitcast<f32>(_warp_shuffle_buf[1u]), bitcast<f32>(_warp_shuffle_buf[2u]), bitcast<f32>(_warp_shuffle_buf[3u]))";
                    loadN = $"emu_f64(bitcast<f32>(_warp_shuffle_buf[_ri_{sfx64} * 4u]), bitcast<f32>(_warp_shuffle_buf[_ri_{sfx64} * 4u + 1u]), bitcast<f32>(_warp_shuffle_buf[_ri_{sfx64} * 4u + 2u]), bitcast<f32>(_warp_shuffle_buf[_ri_{sfx64} * 4u + 3u]))";
                }
                else if (wgslType == "emu_f64")
                {
                    // Dekker: convert from IEEE-754 bits (2 u32 slots)
                    load0 = $"f64_from_ieee754_bits(_warp_shuffle_buf[0u], _warp_shuffle_buf[1u])";
                    loadN = $"f64_from_ieee754_bits(_warp_shuffle_buf[_ri_{sfx64} * 2u], _warp_shuffle_buf[_ri_{sfx64} * 2u + 1u])";
                }
                else if (wgslType.Contains("emu_"))
                {
                    load0 = $"{wgslType}(_warp_shuffle_buf[0u], _warp_shuffle_buf[1u])";
                    loadN = $"{wgslType}(_warp_shuffle_buf[_ri_{sfx64} * 2u], _warp_shuffle_buf[_ri_{sfx64} * 2u + 1u])";
                }
                else
                {
                    load0 = $"{wgslType}(_warp_shuffle_buf[0u], _warp_shuffle_buf[1u])";
                    loadN = $"{wgslType}(_warp_shuffle_buf[_ri_{sfx64} * 2u], _warp_shuffle_buf[_ri_{sfx64} * 2u + 1u])";
                }

                codeGenerator.AppendLine("{");
                codeGenerator.AppendLine($"    var _reduce_accum_{sfx64} : {wgslType} = {load0};");
                codeGenerator.AppendLine($"    for (var _ri_{sfx64} : u32 = 1u; _ri_{sfx64} < workgroup_size.x; _ri_{sfx64}++) {{");
                string accumExpr = GetEmulated64BitAccumExpr(true, wgslType, wgslAccumOp, $"_reduce_accum_{sfx64}", loadN);
                codeGenerator.AppendLine($"        _reduce_accum_{sfx64} = {accumExpr};");
                codeGenerator.AppendLine("    }");
                if (needsBitcast)
                    codeGenerator.AppendLine($"    {target} = bitcast<{irType}>(_reduce_accum_{sfx64});");
                else
                    codeGenerator.AppendLine($"    {target} = _reduce_accum_{sfx64};");
                codeGenerator.AppendLine("}");
                codeGenerator.AppendLine("workgroupBarrier();");
                return;
            }

            if (!backend.HasSubgroups || wgslOp == null || identityExpr == null)
            {
                if (WebGPUBackend.VerboseLogging) WebGPUBackend.Log($"[WGSL-DBG] GenerateGroupAllReduce: fallback, reason: HasSubgroups={backend.HasSubgroups}, wgslOp={wgslOp}, identityExpr={identityExpr}");
                // Fallback: can't do cross-subgroup reduce without subgroups — emit identity.
                codeGenerator.AppendLine($"{target} = {identityExpr ?? "0"}; // group reduce (subgroups unavailable)");
                return;
            }

            // Generate unique variable names using target name as a suffix to avoid clashes in inlined code.
            string sfx = target.Name.Replace("v_", "_").TrimStart('_');

            // Wrap operand in bitcast if needed (e.g., i32 -> u32 for unsigned reductions)
            string operandExpr = needsBitcast ? $"bitcast<{wgslType}>({operand})" : $"{operand}";

            // === Step 1: Per-subgroup reduction ===
            codeGenerator.AppendLine($"let _sg_part_{sfx} = {wgslOp}({operandExpr}); // per-subgroup partial reduce");

            // === Step 2: Lane 0 of each subgroup stores partial result ===
            codeGenerator.AppendLine($"if (subgroup_invocation_id == 0u) {{");
            if (is64Bit)
            {
                // 64-bit emulated types need 2 consecutive u32 slots per subgroup
                codeGenerator.AppendLine($"    _grp_sg_results[subgroup_id * 2u] = bitcast<u32>(_sg_part_{sfx}.x);");
                codeGenerator.AppendLine($"    _grp_sg_results[subgroup_id * 2u + 1u] = bitcast<u32>(_sg_part_{sfx}.y);");
            }
            else
            {
                codeGenerator.AppendLine($"    _grp_sg_results[subgroup_id] = {BitcastToU32($"_sg_part_{sfx}", wgslType)};");
            }
            codeGenerator.AppendLine($"}}");

            // === Step 3: Synchronise so all subgroup slots are visible ===
            codeGenerator.AppendLine($"workgroupBarrier();");

            // === Step 4: Subgroup 0's threads reduce across all subgroup slots ===
            // Number of subgroups = ceil(workgroup_size.x / subgroup_size)
            // Lane i of subgroup 0 reads slot i (padded with identity if i >= num_subgroups)
            codeGenerator.AppendLine($"let _num_sg_{sfx} = (workgroup_size.x + subgroup_size - 1u) / subgroup_size;");
            if (is64Bit)
            {
                string loadExpr = $"{wgslType}(bitcast<{(wgslType == "emu_f64" ? "f32" : "u32")}>(_grp_sg_results[subgroup_invocation_id * 2u]), bitcast<{(wgslType == "emu_f64" ? "f32" : "u32")}>(_grp_sg_results[subgroup_invocation_id * 2u + 1u]))";
                codeGenerator.AppendLine($"let _sg0_val_{sfx} = select({identityExpr}, {loadExpr},");
            }
            else
            {
                string loadExpr = BitcastFromU32("_grp_sg_results[subgroup_invocation_id]", wgslType);
                codeGenerator.AppendLine($"let _sg0_val_{sfx} = select({identityExpr}, {loadExpr},");
            }
            codeGenerator.AppendLine($"    subgroup_invocation_id < _num_sg_{sfx});");
            codeGenerator.AppendLine($"let _final_{sfx} = {wgslOp}(_sg0_val_{sfx}); // second-level reduce across subgroup slots");

            // === Step 5: Thread (0,0) writes final result back so all threads can read it ===
            codeGenerator.AppendLine($"if (subgroup_id == 0u && subgroup_invocation_id == 0u) {{");
            if (is64Bit)
            {
                codeGenerator.AppendLine($"    _grp_sg_results[0] = bitcast<u32>(_final_{sfx}.x);");
                codeGenerator.AppendLine($"    _grp_sg_results[1] = bitcast<u32>(_final_{sfx}.y);");
            }
            else
            {
                codeGenerator.AppendLine($"    _grp_sg_results[0] = {BitcastToU32($"_final_{sfx}", wgslType)};");
            }
            codeGenerator.AppendLine($"}}");

            // === Step 6: Sync and all threads read the final group-wide result ===
            codeGenerator.AppendLine($"workgroupBarrier();");
            if (is64Bit)
            {
                string comp = wgslType == "emu_f64" ? "f32" : "u32";
                string readExpr = $"{wgslType}(bitcast<{comp}>(_grp_sg_results[0]), bitcast<{comp}>(_grp_sg_results[1]))";
                codeGenerator.AppendLine($"{target} = {(needsBitcast ? $"bitcast<{irType}>({readExpr})" : readExpr)};");
            }
            else
            {
                string readExpr = BitcastFromU32("_grp_sg_results[0]", wgslType);
                codeGenerator.AppendLine($"{target} = {(needsBitcast ? $"bitcast<{irType}>({readExpr})" : readExpr)};");
            };
        }

        /// <summary>
        /// Returns the WGSL identity literal for the IScanReduceOperation TReduction type.
        /// E.g. MaxInt32 → "-2147483648", MinInt32 → "2147483647", AddInt32 → "0".
        /// Returns null if the identity cannot be determined.
        /// </summary>
        private static string? GetReductionIdentityExpr(MethodInfo? sourceMethod)
        {
            if (sourceMethod == null) return null;
            try
            {
                Type[] genericArgs = sourceMethod.IsGenericMethod
                    ? sourceMethod.GetGenericArguments()
                    : null;
                if (genericArgs == null || genericArgs.Length < 2) return null;

                Type treductionType = genericArgs[1];
                var instance = Activator.CreateInstance(treductionType);

                // IScanReduceOperation<T>.Identity property
                var identityProp = treductionType.GetProperty(
                    "Identity",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (identityProp == null) return null;

                object? identity = identityProp.GetValue(instance);
                return identity switch
                {
                    int i => i.ToString(),
                    uint u => $"{u}u",
                    float f => float.IsNegativeInfinity(f)
                        ? "_f32_neg_inf()"
                        : float.IsPositiveInfinity(f)
                            ? "_f32_pos_inf()"
                            : float.IsNaN(f)
                                ? "_f32_qnan()"
                                : f.ToString("G9") + "f",
                    long l => LongToEmuI64(l),
                    ulong ul => ULongToEmuU64(ul),
                    double d => DoubleToEmuF64(d),
                    _ => identity?.ToString() ?? "0",
                };
            }
            catch
            {
                return null;
            }
        }


        /// <summary>
        /// Converts a long value to its WGSL emu_i64 (vec2&lt;u32&gt;) literal representation.
        /// The low 32 bits go in .x, the high 32 bits in .y.
        /// </summary>
        private static string LongToEmuI64(long value)
        {
            ulong bits = unchecked((ulong)value);
            uint lo = (uint)(bits & 0xFFFFFFFF);
            uint hi = (uint)(bits >> 32);
            return $"emu_i64({lo}u, {hi}u)";
        }

        /// <summary>
        /// Converts a ulong value to its WGSL emu_u64 (vec2&lt;u32&gt;) literal representation.
        /// The low 32 bits go in .x, the high 32 bits in .y.
        /// </summary>
        private static string ULongToEmuU64(ulong value)
        {
            uint lo = (uint)(value & 0xFFFFFFFF);
            uint hi = (uint)(value >> 32);
            return $"emu_u64({lo}u, {hi}u)";
        }

        /// <summary>
        /// Converts a double value to its WGSL emu_f64 (vec2&lt;f32&gt;) literal representation.
        /// Uses bitcast to split the 64-bit IEEE 754 double into two u32 words,
        /// then bitcasts each to f32 for vec2&lt;f32&gt; storage.
        /// </summary>
        private static string DoubleToEmuF64(double value)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(value);
            uint lo = (uint)(bits & 0xFFFFFFFF);
            uint hi = (uint)(bits >> 32);
            return $"emu_f64(bitcast<f32>({lo}u), bitcast<f32>({hi}u))";
        }

        /// <summary>
        /// Extracts the WGSL subgroup operation name from an open or specialized generic
        /// MethodInfo whose second generic argument is a TReduction implementing
        /// IScanReduceOperation, using the CLCommand property ("max", "min", "add").
        /// Returns null if the operation cannot be determined.
        /// </summary>
        private static string? GetSubgroupReduceOp(MethodInfo? sourceMethod)
        {
            if (sourceMethod == null) return null;
            try
            {
                Type[] genericArgs = sourceMethod.IsGenericMethod
                    ? sourceMethod.GetGenericArguments()
                    : null;

                if (genericArgs == null || genericArgs.Length < 2) return null;

                Type treductionType = genericArgs[1];

                // Instantiate the TReduction struct to read CLCommand
                var instance = Activator.CreateInstance(treductionType);
                var clCommandProp = treductionType.GetProperty(
                    "CLCommand",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (clCommandProp == null) return null;

                string? clCommand = clCommandProp.GetValue(instance) as string;
                return clCommand switch
                {
                    "max" => "subgroupMax",
                    "min" => "subgroupMin",
                    "add" => "subgroupAdd",
                    "and" => "subgroupAnd",
                    "or"  => "subgroupOr",
                    "xor" => "subgroupXor",
                    _ => null,
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Maps a WGSL subgroup operation name to its accumulation operator/function string
        /// suitable for use in the shared-memory butterfly emulation.
        /// Returns "+" for add/and/or/xor (infix), "max" or "min" for comparison ops.
        /// Falls back to "+" when wgslOp is null or unrecognised.
        /// </summary>
        private static string GetWarpReduceAccumOp(string? wgslOp) => wgslOp switch
        {
            "subgroupMax" => "max",
            "subgroupMin" => "min",
            _ => "+",
        };

        /// <summary>
        /// Determines the effective WGSL element type for a reduction based on the TReduction
        /// generic type argument (e.g., MaxUInt32 → "u32", MaxFloat32 → "f32").
        /// ILGPU's IR doesn't distinguish i32/u32, so we must inspect TReduction to get
        /// the correct WGSL type for subgroup and atomic operations that are type-sensitive.
        /// Returns null if the type cannot be determined (caller should fall back to TypeGenerator).
        /// </summary>
        private string? GetReductionWgslType(MethodInfo? sourceMethod)
        {
            if (sourceMethod == null || !sourceMethod.IsGenericMethod) return null;
            try
            {
                var genericArgs = sourceMethod.GetGenericArguments();
                if (genericArgs.Length < 2) return null;
                var tReductionName = genericArgs[1].Name; // e.g. "MaxUInt32", "MinFloat32", "AddInt64"

                if (tReductionName.Contains("UInt32")) return "u32";
                if (tReductionName.Contains("Int32")) return "i32";
                if (tReductionName.Contains("Float32")) return "f32";
                if (tReductionName.Contains("UInt64"))
                    return Backend.EnableI64Emulation ? "emu_u64" : "u32";
                if (tReductionName.Contains("Int64"))
                    return Backend.EnableI64Emulation ? "emu_i64" : "i32";
                if (tReductionName.Contains("Float64") || tReductionName.Contains("Double"))
                    return Backend.EnableF64Emulation ? "emu_f64" : "f32";

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the correct accumulation expression for reduce operations.
        /// For 64-bit emulated types, uses emulated math helpers (i64_max, f64_add, etc.)
        /// instead of WGSL built-ins which would do incorrect component-wise operations on vec2.
        /// </summary>
        private static string GetEmulated64BitAccumExpr(bool is64Bit, string wgslType, string wgslAccumOp, string accumVar, string elemExpr)
        {
            if (!is64Bit)
            {
                // Standard 32-bit path
                if (wgslAccumOp == "+")
                    return $"{accumVar} + {elemExpr}";
                else
                    return $"{wgslAccumOp}({accumVar}, {elemExpr})";
            }

            // 64-bit emulated path: use the emulated helper functions
            // wgslType is one of: emu_i64, emu_u64, emu_f64
            string prefix = wgslType switch
            {
                "emu_f64" => "f64",
                "emu_u64" => "u64",
                "emu_i64" => "i64",
                _ => "i64"
            };

            if (wgslAccumOp == "+")
                return $"{prefix}_add({accumVar}, {elemExpr})";
            else if (wgslAccumOp == "max")
                return $"{prefix}_max({accumVar}, {elemExpr})";
            else if (wgslAccumOp == "min")
                return $"{prefix}_min({accumVar}, {elemExpr})";
            else
                return $"{prefix}_add({accumVar}, {elemExpr})"; // fallback to add
        }

        #endregion
    }
}
