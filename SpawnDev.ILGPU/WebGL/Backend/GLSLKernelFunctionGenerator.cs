// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGL
//                        Copyright (c) 2024 SpawnDev Project
//
// File: GLSLKernelFunctionGenerator.cs
//
// Generates GLSL ES 3.0 vertex shader code for kernel entry points.
// Uses Transform Feedback to capture output buffers and gl_VertexID for invocation index.
// Parameters are passed via Uniform Buffer Objects (UBOs) for scalars and
// Texture Buffer Objects (TBOs) for array data.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Backends.EntryPoints;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Analyses.ControlFlowDirection;
using global::ILGPU.IR.Analyses.TraversalOrders;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;
using System.Text;

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// Generates GLSL ES 3.0 vertex shader code for kernel (entry point) functions.
    /// Transform Feedback captures output varyings; gl_VertexID provides the invocation index.
    /// </summary>
    internal sealed class GLSLKernelFunctionGenerator : GLSLCodeGenerator
    {
        #region Fields

        private readonly EntryPoint EntryPoint;
        private readonly AllocaKindInformation SharedAllocations;
        private readonly AllocaKindInformation DynamicSharedAllocations;

        // Emulated 64-bit parameter tracking
        private readonly HashSet<int> _emulatedF64Params = new();
        private readonly HashSet<int> _emulatedI64Params = new();
        private readonly Dictionary<string, (int ParamIndex, bool IsF64)> _emulatedVarMappings = new();

        // Cross-block pointer fix
        private readonly HashSet<Value> _crossBlockPointers = new();
        private readonly Dictionary<string, string> _crossBlockPointerExprs = new();

        // Output buffer detection: only params with Store targets get TF varyings
        private readonly HashSet<int> _outputParamIndices = new();
        // Multi-store TF: count of Store operations per output buffer (detected in pre-analysis)
        private readonly Dictionary<int, int> _outputStoreCount = new();
        // Multi-store TF: runtime counter for assigning store slots during code generation
        private readonly Dictionary<int, int> _currentStoreSlot = new();
        // Params targeted by GenericAtomic (Atomic.Add etc.) — get atomic vote TF varyings
        private readonly HashSet<int> _atomicParamIndices = new();

        // Input buffer detection: params with Load sources get sampler uniforms
        private readonly HashSet<int> _inputParamIndices = new();

        // Hoisting
        private readonly HashSet<Value> _hoistedPrimitives = new();
        private readonly HashSet<Value> _hoistedIndexFields = new();

        // CFG and analysis
        private CFG<ReversePostOrder, Forwards> _cfg;
        private Dominators<Backwards> _postDominators;
        private Loops<ReversePostOrder, Forwards> _loops;
        private HashSet<BasicBlock> _visitedBlocks = new();
        private readonly Stack<BasicBlock> _activeLoopHeaders = new();
        /// <summary>
        /// The current loop's header exit target. Used by EmitBreakWithIntermediateCode
        /// to distinguish body-break-specific blocks from the shared merge block.
        /// </summary>
        private BasicBlock? _glslHeaderExitTarget;
        private int _loopCounter = 0;

        // Parameter binding info for runtime
        private readonly List<KernelParameterBinding> _parameterBindings = new();

        // Output buffer info
        private readonly List<OutputVaryingInfo> _outputVaryings = new();
        // Pre-indexed lookups for O(1) output varying access (built after all varyings are added)
        private Dictionary<int, OutputVaryingInfo>? _atomicVoteIndex;           // paramIdx → atomic vote varying
        private Dictionary<(int, string), OutputVaryingInfo>? _emulatedIndex;   // (paramIdx, suffix) → emulated varying
        private Dictionary<(int, int), OutputVaryingInfo>? _storeSlotIndex;     // (paramIdx, slot) → multi-store varying
        private Dictionary<int, OutputVaryingInfo>? _singleStoreIndex;          // paramIdx → single-store varying (slot < 0)
        private Dictionary<int, OutputVaryingInfo>? _paramFallbackIndex;        // paramIdx → first varying for param

        // Struct buffer field counts: paramIndex → number of flattened scalar fields
        private readonly Dictionary<int, int> _structFieldCounts = new();
        // Struct buffer field GLSL types: paramIndex → [fieldType0, fieldType1, ...]
        private readonly Dictionary<int, List<string>> _structFieldTypes = new();
        // Sub-word element tracking: paramIndex → elementByteSize (1=byte, 2=short/half)
        private readonly Dictionary<int, int> _subWordParams = new();
        // Float16 sub-word params need f16-to-f32 conversion instead of sign extension
        private readonly HashSet<int> _subWordFloat16Params = new();
        private readonly HashSet<int> _subWordBFloat16Params = new();
        // FP8 sub-word params (1-byte storage, 4 per R32I texel): paramIdx -> isE4M3
        // (true = Float8E4M3, false = Float8E5M2). Load/store apply _e4m3/_e5m2 conversion.
        private readonly Dictionary<int, bool> _subWordFloat8Params = new();
        // FP4 sub-word params (E2M1FN, 1-byte storage, 4-bit value in low nibble, 4 per R32I texel).
        // Load/store apply _e2m1_to_f32 / _f32_to_e2m1 conversion (value computes as float).
        private readonly HashSet<int> _subWordFloat4Params = new();
        // Packed 4-bit int params (QInt4/QUInt4): TRUE 2-nibbles-per-byte (8 per R32I texel). LOAD
        // extracts the (idx&7) nibble of texel (idx>>3) and sign-extends. STORE needs atomic word
        // RMW which WebGL has no instruction for (Transform Feedback is gather-only, one record per
        // thread) - so a packed in-kernel store is structurally impossible on WebGL and fails loud.
        private readonly HashSet<int> _subWordQInt4Params = new();
        // Unsigned sub-word params need zero-extension instead of sign extension
        private readonly HashSet<int> _subWordUnsignedParams = new();
        // Maps LEA variable names to their sub-word param index
        private readonly Dictionary<string, int> _subWordLEAVars = new();

        // === Body-struct parameter support (mirror of WGSL _bodyStructParams pattern) ===
        // A body struct is a value-type kernel parameter whose fields include 2+ ArrayView<T>.
        // Each view field becomes its own sampler binding (u_param{N}_f{M}); host-side
        // dispatch decomposes the body struct into per-field buffer_ref entries with a
        // synthetic param index = (paramIndex + 1) * 1000 + fieldIdx.
        // Populated by ScanBodyStructParams in GenerateCode() before the analyzers run.
        private readonly Dictionary<int, List<BodyStructFieldInfoGL>> _bodyStructParamsGL = new();
        // (paramIdx, fieldIdx) -> field-local variable name in the kernel body (for GetField redirect)
        private readonly Dictionary<(int, int), string> _bodyStructFieldVars = new();
        // synthetic param index -> binding name override for ALL body-struct view fields.
        // Used by every site that would emit `u_param{N}` for a single-view param so the
        // body-struct path emits `u_param{realN}_f{M}` instead. Populated by EmitBodyStructDeclarations.
        private readonly Dictionary<int, string> _bodyStructFieldBindingNames = new();
        // Type IDs of body struct C# types — GLSLTypeGenerator skips emitting their UBO
        // struct definitions because the fields don't make sense as UBO scalars.
        private readonly HashSet<long> _bodyStructTypesToSkip = new();

        /// <summary>
        /// During cross-assembly MethodCall inlining, maps helper-method
        /// <see cref="Parameter"/> nodes to the kernel call-site argument values so
        /// <see cref="ResolveToParameter"/> and <see cref="TryFindBodyStructRoot"/> see
        /// the real body-struct kernel param (e.g. TensorView inp), not the helper's
        /// distinct Parameter object.
        /// </summary>
        private readonly Dictionary<global::ILGPU.IR.Values.Parameter, Value> _inlineParamAliasesGL = new();

        /// <summary>
        /// Kernel parameter offset: number of leading IR parameters that are NOT user buffers.
        ///
        /// For auto-grouped stream kernels (LoadAutoGroupedStreamKernel&lt;Index1D, ...&gt;),
        /// Method.Parameters[0] is the implicit thread index — skip it.
        ///
        /// For explicitly-launched kernels (LoadStreamKernel&lt;TParam&gt;) and grid-stride
        /// (LongIndex1D-first) kernels, Method.Parameters[0] is a user param — do NOT skip.
        ///
        /// Mirrors `WGSLKernelFunctionGenerator.KernelParamOffset` so explicit-launch +
        /// body-struct kernels (e.g. Tests23_RegisterHeavyBody_ExplicitOneByOne) bind the
        /// body struct param correctly instead of treating it as the implicit index.
        /// </summary>
        private int KernelParamOffset
        {
            get
            {
                if (EntryPoint.IndexType == IndexType.None) return 0;

                // Auto-grouped kernels: first param is always the thread index — skip it.
                if (EntryPoint.IndexType != IndexType.KernelConfig) return 1;

                // KernelConfig (explicit-launch / grid-stride): inspect the first param type.
                // Index1D/2D/3D → stream-kernel index, skip. LongIndex1D/2D/3D or any user
                // type (struct, ArrayView) → don't skip.
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

        #region Constructor

        public GLSLKernelFunctionGenerator(in GeneratorArgs args, Method method, Allocas allocas)
            : base(args, method, allocas)
        {
            _generatorArgs = args;
            EntryPoint = args.EntryPoint;
            SharedAllocations = args.SharedAllocations;
            DynamicSharedAllocations = args.DynamicSharedAllocations;

            _cfg = method.Blocks.CreateCFG();
            _postDominators = _cfg.Blocks.CreatePostDominators();
            _loops = _cfg.CreateLoops();

            AnalyzeCrossBlockPointers();
        }

        /// <summary>Stored args for accessing shared OutputVaryings collection.</summary>
        private readonly GeneratorArgs _generatorArgs;

        #endregion

        #region Analysis

        private void AnalyzeCrossBlockPointers()
        {
            // Find LEA values used in different blocks
            var leaBlocks = new Dictionary<Value, BasicBlock>();
            foreach (var block in Method.Blocks)
            {
                foreach (var value in block)
                {
                    if (value.Value is LoadElementAddress lea)
                        leaBlocks[lea] = block;
                }
            }

            foreach (var block in Method.Blocks)
            {
                foreach (var value in block)
                {
                    if (value.Value is global::ILGPU.IR.Values.Load load)
                    {
                        var src = load.Source.Resolve();
                        if (src is LoadElementAddress lea && leaBlocks.TryGetValue(lea, out var defBlock) && defBlock != block)
                            _crossBlockPointers.Add(lea);
                    }
                    else if (value.Value is Store store)
                    {
                        var tgt = store.Target.Resolve();
                        if (tgt is LoadElementAddress lea && leaBlocks.TryGetValue(lea, out var defBlock) && defBlock != block)
                            _crossBlockPointers.Add(lea);
                    }
                }
            }
        }

        /// <summary>
        /// Pre-scan all IR blocks to detect which buffer parameters are targets of
        /// Store instructions. Only those params need TF output varyings. This avoids
        /// wasting TF slots on read-only input buffers, which causes interleaving
        /// bugs in multi-buffer kernels.
        /// </summary>
        /// <summary>
        /// Resolves a buffer access (load source or store target) to the param index used
        /// for input/output/TF tracking. Body-struct view fields use the synthetic index
        /// from <see cref="TryResolveBodyStructField"/> (handles ArrayView1D wrapper chains);
        /// direct view params use their kernel param index.
        /// </summary>
        private bool TryGetBufferTrackingKey(Value src, int paramOffset, out int bufferKey)
        {
            bufferKey = -1;
            var resolved = src.Resolve();
            if (TryResolveBodyStructField(resolved, out _, out _, out var bsInfo) && bsInfo.IsView)
            {
                bufferKey = bsInfo.SyntheticParamIndex;
                return true;
            }
            var (param, fieldIdx) = ResolveToParamAndFieldStatic(resolved);
            if (param == null || param.Index < paramOffset)
                return false;
            bufferKey = (fieldIdx >= 0 && _bodyStructParamsGL.TryGetValue(param.Index, out var bsf)
                && fieldIdx < bsf.Count && bsf[fieldIdx].IsView)
                ? bsf[fieldIdx].SyntheticParamIndex
                : param.Index;
            return true;
        }

        private void AnalyzeOutputBuffers()
        {
            int paramOffset = KernelParamOffset;
            // Track unique LEA source expressions (array[index]) per param.
            // Multiple LEA IR nodes for the same buffer[index] in different code paths
            // should count as 1 store. Only LEAs with distinct indices (e.g., data[i*6+0]
            // vs data[i*6+1]) represent true multi-store patterns.
            var uniqueLeaExprs = new Dictionary<int, HashSet<string>>();
            foreach (var block in Method.Blocks)
            {
                foreach (var value in block)
                {
                    if (value.Value is Store store)
                    {
                        var target = store.Target.Resolve();
                        if (TryGetBufferTrackingKey(target, paramOffset, out int outKey))
                        {
                            _outputParamIndices.Add(outKey);
                            if (!uniqueLeaExprs.TryGetValue(outKey, out var exprSet))
                            {
                                exprSet = new HashSet<string>();
                                uniqueLeaExprs[outKey] = exprSet;
                            }
                            // Use the LEA's source expression (array+index) as key.
                            // LEA nodes like lea._1433: output_1408[index_1406] and
                            // lea._1442: output_1408[index_1406] share the same source expression
                            // and should count as 1.
                            if (target is LoadElementAddress lea)
                            {
                                // Extract the source expression (Source[index]) for deduplication
                                string sourceExpr = $"{lea.Source}[{lea.Offset}]";
                                exprSet.Add(sourceExpr);
                            }
                            else
                            {
                                // Non-LEA targets are unique per store
                                exprSet.Add(target.ToString());
                            }
                        }
                    }
                    else if (value.Value is GenericAtomic atomic)
                    {
                        // Atomic operations (Atomic.Add etc.) write to a buffer element.
                        // Mark the target param as an output so it gets an atomic vote TF varying.
                        var atomicTarget = atomic.Target.Resolve();
                        if (TryGetBufferTrackingKey(atomicTarget, paramOffset, out int outKey))
                        {
                            _atomicParamIndices.Add(outKey);
                            _outputParamIndices.Add(outKey);
                        }
                    }
                }
            }
            // Populate store counts from unique source expression counts
            foreach (var kvp in uniqueLeaExprs)
                _outputStoreCount[kvp.Key] = kvp.Value.Count;
        }

        /// <summary>
        /// Pre-scan all IR blocks to detect which buffer parameters are sources of
        /// Load instructions. Only those params need sampler uniform declarations.
        /// Output-only buffers (in _outputParamIndices but NOT in _inputParamIndices)
        /// must NOT have sampler uniforms, because WebGL 2 generates GL_INVALID_OPERATION
        /// when an active sampler has no valid texture bound.
        /// </summary>
        private void AnalyzeInputBuffers()
        {
            int paramOffset = KernelParamOffset;
            foreach (var block in Method.Blocks)
            {
                foreach (var value in block)
                {
                    if (value.Value is global::ILGPU.IR.Values.Load load)
                    {
                        // Trace: Load.Source → LEA → GetField? → ... → Parameter.
                        // Body-struct view-field loads key by synthetic param index.
                        if (TryGetBufferTrackingKey(load.Source.Resolve(), paramOffset, out int inKey))
                            _inputParamIndices.Add(inKey);
                    }
                }
            }
        }

        /// <summary>
        /// Walks chained GetFields starting from <paramref name="src"/> looking for
        /// a body-struct view field. Handles two shapes:
        /// 1. Raw ArrayView field: `GetField(bodyStructParam, fi)` (one step)
        /// 2. ArrayView1D-wrapped field: `GetField(GetField(bodyStructParam, fi), 0)`
        ///    (two steps — outer extracts the wrapper, inner extracts BaseView)
        /// Returns true with the body-struct param index, view-field index in the
        /// outer body struct, and the field info when matched.
        /// </summary>
        private bool TryResolveBodyStructField(Value src,
            out int bodyParamIdx, out int bodyFieldIdx, out BodyStructFieldInfoGL info)
        {
            bodyParamIdx = -1; bodyFieldIdx = -1; info = null!;

            if (src is GetField gf && TryFindBodyStructRoot(gf, out var bsParam, out var rootInfo, out _))
            {
                if (rootInfo.IsView)
                {
                    bodyParamIdx = bsParam.Index;
                    bodyFieldIdx = rootInfo.FieldIndex;
                    info = rootInfo;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Walks a GetField chain back to its root parameter. If the chain terminates
        /// at a body-struct parameter, returns the body-struct field info for the
        /// outermost GetField (i.e. the one extracting the user's C# field directly
        /// from the body struct). chainDepth is the count of GetField links walked
        /// (1 = direct, 2 = ArrayView1D wrapper, 3+ = deeper unwrap of BaseView etc).
        /// </summary>
        /// <summary>
        /// Cross-assembly MethodCall inlining binds callee <c>this</c> to a
        /// <see cref="Load"/> of a struct snapshot, not always the kernel
        /// <see cref="global::ILGPU.IR.Values.Parameter"/> directly. Peel those wrappers
        /// before body-struct root detection.
        /// </summary>
        private static Value? PeelStructAccessRoot(Value root)
        {
            var current = root.Resolve();
            for (int i = 0; i < 16 && current != null; i++)
            {
                if (current is global::ILGPU.IR.Values.Load load)
                    current = load.Source.Resolve();
                else if (current is ConvertValue cv)
                    current = cv.Value.Resolve();
                else if (current is AddressSpaceCast asc)
                    current = asc.Value.Resolve();
                else
                    return current;
            }

            return current;
        }

        private bool TryFindBodyStructRoot(GetField value,
            out global::ILGPU.IR.Values.Parameter bodyParam,
            out BodyStructFieldInfoGL rootFieldInfo,
            out int chainDepth)
        {
            bodyParam = null!;
            rootFieldInfo = null!;
            chainDepth = 0;

            // Walk up: collect each GetField link, tracking the outermost (closest to
            // the parameter) field index. The chain looks like:
            //   value = GetField(x_n, fi_n) [innermost to value]
            //   x_n = GetField(x_{n-1}, fi_{n-1})
            //   ...
            //   x_1 = GetField(parameter, outerFi)  // outerFi is the body-struct field index
            //   x_1.ObjectValue.Resolve() is Parameter (or Load(Parameter) after cross-asm inline)
            GetField current = value;
            int outerFi = -1;
            int depth = 0;
            while (true)
            {
                depth++;
                outerFi = current.FieldSpan.Index;
                var resolvedObject = PeelStructAccessRoot(current.ObjectValue);
                if (resolvedObject is global::ILGPU.IR.Values.Parameter param)
                {
                    if (_inlineParamAliasesGL.TryGetValue(param, out var aliasedArg))
                    {
                        var peeledAlias = PeelStructAccessRoot(aliasedArg);
                        if (peeledAlias is global::ILGPU.IR.Values.Parameter kernelParam)
                            param = kernelParam;
                    }

                    if (_bodyStructParamsGL.TryGetValue(param.Index, out var bsFields))
                    {
                        if (outerFi >= 0 && outerFi < bsFields.Count)
                        {
                            bodyParam = param;
                            rootFieldInfo = bsFields[outerFi];
                            chainDepth = depth;
                            return true;
                        }
                    }
                    return false;
                }
                if (resolvedObject is GetField nextGf)
                {
                    current = nextGf;
                    if (depth > 8) return false; // safety cap
                    continue;
                }
                return false;
            }
        }

        /// <summary>
        /// Returns the GLSL uniform/sampler binding name for a given parameter index.
        /// For direct view params returns "u_param{idx}". For synthetic body-struct
        /// view fields (idx >= 1000), returns the per-field name "u_param{realN}_f{fi}"
        /// from _bodyStructFieldBindingNames. Use this everywhere a texelFetch or
        /// uniform reference is emitted — concatenating "_offset", "_tileW", "_length"
        /// onto the result yields the matching companion uniforms.
        /// </summary>
        private string GetParamBindingName(int paramIdx)
        {
            if (paramIdx >= 1000 && _bodyStructFieldBindingNames.TryGetValue(paramIdx, out var name))
                return name;
            return $"u_param{paramIdx}";
        }

        /// <summary>
        /// Walks a value tree and returns (parameter, getFieldIndex) where getFieldIndex
        /// is the field index of the FIRST GetField encountered while walking up to a
        /// Parameter. Returns -1 when the value resolves directly to a Parameter without
        /// any GetField in the chain (i.e. direct view-param access, not a body-struct
        /// field access).
        ///
        /// Used by AnalyzeInputBuffers / AnalyzeOutputBuffers to track per-field
        /// input/output flags via synthetic param index (paramIdx+1)*1000 + fieldIdx
        /// for body-struct view fields.
        /// </summary>
        private static (global::ILGPU.IR.Values.Parameter? Param, int FieldIndex) ResolveToParamAndFieldStatic(Value value)
        {
            return ResolveToParamAndFieldStaticInner(value, -1);
        }

        private static (global::ILGPU.IR.Values.Parameter? Param, int FieldIndex) ResolveToParamAndFieldStaticInner(Value value, int pendingFieldIdx)
        {
            if (value is global::ILGPU.IR.Values.Parameter p) return (p, pendingFieldIdx);
            if (value is GetField gf)
            {
                // First GetField encountered captures the field index. Walking through
                // a body-struct view field is exactly one GetField step above the LEA.
                int captured = pendingFieldIdx >= 0 ? pendingFieldIdx : gf.FieldSpan.Index;
                return ResolveToParamAndFieldStaticInner(gf.ObjectValue, captured);
            }
            if (value is global::ILGPU.IR.Values.Load load) return ResolveToParamAndFieldStaticInner(load.Source, pendingFieldIdx);
            if (value is LoadElementAddress lea) return ResolveToParamAndFieldStaticInner(lea.Source, pendingFieldIdx);
            if (value is LoadFieldAddress lfa) return ResolveToParamAndFieldStaticInner(lfa.Source, pendingFieldIdx);
            if (value is NewView nv) return ResolveToParamAndFieldStaticInner(nv.Pointer, pendingFieldIdx);
            if (value is AddressSpaceCast asc) return ResolveToParamAndFieldStaticInner(asc.Value, pendingFieldIdx);
            if (value is SubViewValue sv) return ResolveToParamAndFieldStaticInner(sv.Source, pendingFieldIdx);
            if (value is ConvertValue cv) return ResolveToParamAndFieldStaticInner(cv.Value, pendingFieldIdx);
            if (value is PhiValue phi)
            {
                for (int i = 0; i < phi.Count; i++)
                {
                    var (paramX, fldX) = ResolveToParamAndFieldStaticInner(phi[i], pendingFieldIdx);
                    if (paramX != null) return (paramX, fldX);
                }
            }
            return (null, -1);
        }

        /// <summary>
        /// Static version of ResolveToParameter that doesn't depend on code gen state.
        /// Used during pre-scan analysis before code generation starts.
        /// </summary>
        private static global::ILGPU.IR.Values.Parameter? ResolveToParameterStatic(Value value)
        {
            if (value is global::ILGPU.IR.Values.Parameter p) return p;
            if (value is GetField gf) return ResolveToParameterStatic(gf.ObjectValue);
            if (value is global::ILGPU.IR.Values.Load load) return ResolveToParameterStatic(load.Source);
            if (value is LoadElementAddress lea) return ResolveToParameterStatic(lea.Source);
            if (value is LoadFieldAddress lfa) return ResolveToParameterStatic(lfa.Source);
            // NewView creates a view from a pointer — trace through to the source
            if (value is NewView nv) return ResolveToParameterStatic(nv.Pointer);
            // AddressSpaceCast changes address space — trace through
            if (value is AddressSpaceCast asc) return ResolveToParameterStatic(asc.Value);
            // SubViewValue creates a sub-range — trace the source view
            if (value is SubViewValue sv) return ResolveToParameterStatic(sv.Source);
            // ConvertValue (pointer/view casts) — trace through
            if (value is ConvertValue cv) return ResolveToParameterStatic(cv.Value);
            if (value is PhiValue phi)
            {
                // For phi nodes, check all source values — if any traces to a buffer param, include it
                for (int i = 0; i < phi.Count; i++)
                {
                    var result = ResolveToParameterStatic(phi[i]);
                    if (result != null) return result;
                }
            }
            return null;
        }

        #endregion

        #region Code Generation

        public override void GenerateHeader(StringBuilder builder)
        {
            // Precision and version are added in WebGLBackend.CreateKernelBuilder
        }

        public override void GenerateCode()
        {
            // 0. Scan kernel parameters for body structs (struct params with multiple
            //    ArrayView fields). Must run BEFORE the analyzers so AnalyzeOutputBuffers
            //    and AnalyzeInputBuffers can route through GetField for body-struct view
            //    fields via the synthetic param-index encoding.
            ScanBodyStructParams();

            // 1. Detect emulated 64-bit parameters
            DetectEmulatedParameters();

            // 1.5. Detect which buffer params are store targets (need TF outputs)
            AnalyzeOutputBuffers();

            // 1.6. Detect which buffer params are load sources (need sampler uniforms)
            AnalyzeInputBuffers();

            // 2. Analyze hoisting needs
            AnalyzeHoisting();

            // 3. Post-dominators and loops already created in constructor

            // 4. Emit emulation library only if the kernel actually uses f64/i64/f16 types.
            //    Including the library unconditionally produces a massive vertex shader
            //    that ANGLE's D3D11 backend cannot compile with Transform Feedback.
            var (kernelNeedsF64, kernelNeedsI64, kernelNeedsF16, kernelNeedsBF16, kernelNeedsFP8, kernelNeedsFP4) = KernelUsesEmulatedTypes();
            // Float16 emulation is always active on WebGL when the kernel uses Half
            // types - GLSL ES 3.0 has no hardware f16 path. Detected via IR scan above,
            // not just _subWordFloat16Params (which is populated AFTER library emission).
            if ((Backend.EnableF64Emulation && kernelNeedsF64) ||
                (Backend.EnableI64Emulation && kernelNeedsI64) ||
                kernelNeedsF16 || kernelNeedsBF16 || kernelNeedsFP8 || kernelNeedsFP4)
            {
                Builder.AppendLine("// ============ Emulation Library ============");
                Builder.AppendLine(GLSLEmulationLibrary.GetEmulationLibrary(
                    Backend.EnableF64Emulation && kernelNeedsF64,
                    Backend.UseOzakiF64Emulation,
                    Backend.EnableI64Emulation && kernelNeedsI64,
                    kernelNeedsF16,
                    includeBF16: kernelNeedsBF16,
                    includeFP8: kernelNeedsFP8,
                    includeFP4: kernelNeedsFP4));
            }

            // 5. Insert a placeholder for struct type definitions.
            //    Struct types are discovered lazily during EmitParameterDeclarations() and
            //    later code generation, so we can't emit them here yet. The placeholder
            //    will be replaced with actual struct definitions in WebGLBackend.CreateKernel()
            //    after all code generation is complete.
            Builder.AppendLine("// __STRUCT_DEFS_PLACEHOLDER__");

            // 6. Emit parameter declarations (uniforms and samplers)
            EmitParameterDeclarations();

            // 7. Emit output varyings for Transform Feedback
            EmitOutputVaryings();
            BuildOutputVaryingIndex();

            // 8. Emit main() vertex shader entry point
            Builder.AppendLine("void main() {");
            PushIndent();

            // 9. Setup index from gl_VertexID
            SetupIndexVariables();

            // 10. Load parameters into local variables
            SetupParameterBindings();

            // 10.5. Initialize atomic vote varyings to 0 before the kernel body may return early.
            // This ensures threads that take an early-return path emit 0 (no contribution).
            InitializeAtomicVoteVaryings();

            // 11. Emit hoisted variable declarations
            EmitHoistedDeclarations();

            // 12. Generate kernel body
            GenerateKernelBody();

            PopIndent();
            Builder.AppendLine("}");
        }

        #endregion

        #region Emulated Parameter Detection

        private void DetectEmulatedParameters()
        {
            int paramOffset = KernelParamOffset;
            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;

                var type = param.ParameterType;
                var unwrapped = UnwrapType(type);

                if (unwrapped is PrimitiveType pt)
                {
                    if (Backend.EnableF64Emulation && pt.BasicValueType == BasicValueType.Float64)
                        _emulatedF64Params.Add(param.Index);
                    else if (Backend.EnableI64Emulation && pt.BasicValueType == BasicValueType.Int64)
                        _emulatedI64Params.Add(param.Index);
                }
            }
        }

        /// <summary>
        /// Scans the kernel's parameters and IR values to determine if f64/i64
        /// emulation types are actually used. This prevents unconditionally including
        /// the entire emulation library for simple float32 kernels, which causes
        /// ANGLE D3D11 to fail compiling the large vertex shader.
        /// </summary>
        private (bool needsF64, bool needsI64, bool needsF16, bool needsBF16, bool needsFP8, bool needsFP4) KernelUsesEmulatedTypes()
        {
            bool needsF64 = _emulatedF64Params.Count > 0;
            bool needsI64 = _emulatedI64Params.Count > 0;
            bool needsF16 = _subWordFloat16Params.Count > 0;
            bool needsBF16 = _subWordBFloat16Params.Count > 0;
            bool needsFP8 = _subWordFloat8Params.Count > 0;
            bool needsFP4 = _subWordFloat4Params.Count > 0;

            // Early exit if all already detected from parameters
            if (needsF64 && needsI64 && needsF16 && needsBF16 && needsFP8 && needsFP4) return (needsF64, needsI64, needsF16, needsBF16, needsFP8, needsFP4);

            // Scan all values in the kernel's IR blocks for f64/i64/f16 usage
            foreach (var block in Method.Blocks)
            {
                foreach (Value value in block)
                {
                    var bvt = value.BasicValueType;
                    if (!needsF64 && bvt == BasicValueType.Float64)
                        needsF64 = true;
                    if (!needsI64 && bvt == BasicValueType.Int64)
                        needsI64 = true;
                    if (!needsF16 && bvt == BasicValueType.Float16)
                        needsF16 = true;
                    if (!needsBF16 && bvt == BasicValueType.BFloat16)
                        needsBF16 = true;
                    if (!needsFP8 && (bvt == BasicValueType.Float8E4M3 || bvt == BasicValueType.Float8E5M2))
                        needsFP8 = true;
                    if (!needsFP4 && bvt == BasicValueType.Float4E2M1)
                        needsFP4 = true;

                    if (needsF64 && needsI64 && needsF16 && needsBF16 && needsFP8 && needsFP4) return (needsF64, needsI64, needsF16, needsBF16, needsFP8, needsFP4);
                }
            }

            return (needsF64, needsI64, needsF16, needsBF16, needsFP8, needsFP4);
        }

        private static TypeNode UnwrapType(TypeNode type)
        {
            while (type is PointerType pt) type = pt.ElementType;
            while (type is ViewType vt) type = vt.ElementType;
            // For ArrayView2D/3D: the IR type is a StructureType wrapping a ViewType + stride info.
            // Use structural detection (matching IsMultiDim approach) instead of string check,
            // because StructureType.ToString() doesn't contain "ArrayView".
            if (type is StructureType st && st.NumFields > 0 &&
                (st.Fields[0] is ViewType || st.Fields[0] is PointerType || st.Fields[0].ToString().Contains("View")))
            {
                // The first flattened field is the buffer pointer — unwrap it to get element type
                return UnwrapType(st.Fields[0]);
            }
            return type;
        }

        #endregion

        #region Hoisting Analysis

        private void AnalyzeHoisting()
        {
            // For single-block kernels, no hoisting is needed.
            if (Method.Blocks.Count <= 1) return;

            // For multi-block kernels (state machine path), GLSL switch/case blocks
            // have strict scoping — variables declared inside one case {} block are not
            // visible in other case blocks. Hoist ALL values to function scope to avoid
            // undeclared identifier errors.
            foreach (var block in Method.Blocks)
            {
                foreach (var value in block)
                {
                    var node = value.Value.Type;
                    // PointerType (LEA results, alloca pointers) must be hoisted: structured
                    // if/else emits branch-scoped `int v_N = ...` otherwise, and a sibling
                    // branch's `v_N = ...` hits undeclared identifier (IntMathTest v_34).
                    if (node.IsPrimitiveType || node.IsStructureType || node is PointerType)
                        _hoistedPrimitives.Add(value.Value);
                }
            }
        }

        #endregion

        #region Body Struct Detection

        /// <summary>
        /// Per-field metadata for a body-struct kernel parameter. Mirrors WGSL's
        /// BodyStructFieldInfo. Populated by ScanBodyStructParams.
        /// </summary>
        private sealed class BodyStructFieldInfoGL
        {
            /// <summary>The IR field index within the parent body-struct type.</summary>
            public int FieldIndex { get; set; }
            /// <summary>The IR type of this field.</summary>
            public TypeNode FieldType { get; set; } = null!;
            /// <summary>True when the field is an ArrayView (raw or wrapped). Maps to a sampler binding.</summary>
            public bool IsView { get; set; }
            /// <summary>True when the field is a primitive scalar. Packed into the existing scalar uniform path.</summary>
            public bool IsScalar { get; set; }
            /// <summary>True when the field is ArrayView1D wrapper metadata (Length / Dense flag) following a view.
            /// Suppressed in code emission; the per-field length uniform is the canonical source.</summary>
            public bool IsViewMetadata { get; set; }
            /// <summary>The C# field index within the user struct (in declaration order). Different from
            /// FieldIndex when the IR flattens ArrayView1D wrappers into multiple IR fields.</summary>
            public int ClrFieldIndex { get; set; }
            /// <summary>The synthetic param index = (paramIndex + 1) * 1000 + FieldIndex.</summary>
            public int SyntheticParamIndex { get; set; }
            /// <summary>The GLSL sampler uniform name (e.g. "u_param2_f0") when IsView.</summary>
            public string BindingName { get; set; } = "";
            /// <summary>Element type GLSL name (int / uint / float) for view fields.</summary>
            public string GlslElementType { get; set; } = "int";
            /// <summary>1 for byte/sbyte, 2 for short/ushort/Half. 0 for full-word fields.</summary>
            public int SubWordElemSize { get; set; }
            /// <summary>True when sub-word and signed (sbyte/short).</summary>
            public bool IsUnsignedSubWord { get; set; }
            /// <summary>True when sub-word and Float16-emulated.</summary>
            public bool IsFloat16 { get; set; }
            /// <summary>True when sub-word and bfloat16-emulated.</summary>
            public bool IsBFloat16 { get; set; }
            /// <summary>True when sub-word and FP8-emulated (1-byte Float8E4M3/E5M2).</summary>
            public bool IsFloat8 { get; set; }
            /// <summary>When <see cref="IsFloat8"/>: true = Float8E4M3, false = Float8E5M2.</summary>
            public bool IsFloat8E4M3 { get; set; }
            /// <summary>True when sub-word and FP4-emulated (E2M1FN, 1-byte storage, 4-bit low nibble).</summary>
            public bool IsFloat4 { get; set; }
            /// <summary>The raw element type (PrimitiveType or StructureType) of the view.</summary>
            public TypeNode? ViewElementType { get; set; }
            /// <summary>The C# field type from the user's struct (used by host-side reflection).</summary>
            public Type? ClrFieldType { get; set; }
            /// <summary>The C# field name from the user's struct (debug clarity).</summary>
            public string ClrFieldName { get; set; } = "";
        }

        /// <summary>
        /// True when a TypeNode represents an ArrayView (raw ViewType or a struct whose
        /// first field is a view — i.e. ArrayView1D wrapper).
        /// Mirror of WGSLKernelFunctionGenerator.IsViewFieldType.
        /// </summary>
        private static bool IsViewFieldType(TypeNode type)
        {
            if (type is ViewType) return true;
            if (type is StructureType st)
            {
                if (st.NumFields > 0 &&
                    (st.Fields[0] is ViewType ||
                     st.Fields[0] is PointerType ||
                     st.Fields[0].ToString().Contains("View")))
                    return true;
            }
            if (type.ToString().Contains("View")) return true;
            return false;
        }

        /// <summary>
        /// True for TensorView-shaped body structs: one embedded view (typically
        /// ArrayView1D) plus multiple Int32 shape fields (D0..D3, Rank). These have
        /// NumFields == 6 like ArrayView3D wrappers but are user body structs, not views.
        /// </summary>
        private static bool IsTensorViewLikeBodyStruct(StructureType st)
        {
            int viewFieldCount = 0;
            int int32FieldCount = 0;
            for (int i = 0; i < st.NumFields; i++)
            {
                if (IsViewFieldType(st.Fields[i]))
                    viewFieldCount++;
                else if (st.Fields[i] is PrimitiveType pt
                    && pt.BasicValueType == BasicValueType.Int32)
                    int32FieldCount++;
            }

            return viewFieldCount == 1 && int32FieldCount >= 4;
        }

        /// <summary>
        /// True when a StructureType is a "body struct" — a user-defined struct kernel
        /// parameter holding at least one ArrayView field, distinct from the
        /// ArrayView1D/2D/3D wrappers themselves. Discrimination:
        ///   - 0 view fields → not a body struct (could be a scalar struct UBO, etc.)
        ///   - 2+ view fields → definitely a body struct
        ///   - exactly 1 view field → body struct UNLESS the type matches an
        ///     ArrayView wrapper shape (NumFields ∈ {3,4,6} with Field[0] = View)
        ///
        /// Mirror of WGSL's IsBodyStruct check, generalized to also recognize
        /// single-view body structs like `Tests23_OnlyShortStruct { ArrayView<short> S0 }`.
        /// </summary>
        private static bool IsBodyStruct(StructureType st)
        {
            if (IsTensorViewLikeBodyStruct(st))
                return true;

            int viewFieldCount = 0;
            for (int i = 0; i < st.NumFields; i++)
            {
                if (IsViewFieldType(st.Fields[i]))
                    viewFieldCount++;
            }
            if (viewFieldCount == 0) return false;
            if (viewFieldCount >= 2) return true;

            // Exactly 1 view field. Distinguish a body struct (e.g. NumFields=1 with
            // a single ArrayView<T> field) from an ArrayView1D/2D/3D wrapper (NumFields
            // ∈ {3,4,6} with Field[0] being the view pointer).
            if (st.NumFields > 0 && IsViewFieldType(st.Fields[0]))
            {
                if (st.NumFields == 3) return false; // ArrayView1D wrapper
                if (st.NumFields == 4) return false; // ArrayView2D wrapper
                if (st.NumFields == 6) return false; // ArrayView3D wrapper
            }
            return true;
        }

        /// <summary>
        /// Walks kernel parameters and populates _bodyStructParamsGL for each body-struct
        /// parameter. Must run BEFORE AnalyzeOutputBuffers/AnalyzeInputBuffers so those
        /// passes can route through GetField for body-struct view fields.
        /// </summary>
        private void ScanBodyStructParams()
        {
            foreach (var param in Method.Parameters)
            {
                if (param.Index < KernelParamOffset) continue;
                if (param.ParameterType is not StructureType st) continue;
                if (!IsBodyStruct(st)) continue;

                var fields = new List<BodyStructFieldInfoGL>(st.NumFields);
                int userIdx = param.Index - KernelParamOffset;
                Type? clrParamType = null;
                System.Reflection.FieldInfo[]? clrFields = null;
                if (userIdx >= 0 && userIdx < EntryPoint.Parameters.Count)
                {
                    clrParamType = EntryPoint.Parameters[userIdx];
                    if (clrParamType.IsValueType)
                    {
                        clrFields = clrParamType.GetFields(
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public);
                    }
                }

                // State machine for ArrayView1D-wrapper expanded fields. The IR
                // flattens `ArrayView1D<T, Stride1D.Dense> S` to multiple sequential
                // fields: ViewType, Int64 (Length), optional Int8 (Dense flag).
                // Mark view-following Int64 / Int8 fields as IsViewMetadata so the
                // host-side dispatch counts only the user-visible C# fields.
                int clrFieldCounter = 0;
                int viewMetadataPhase = 0; // 0=idle, 1=after view ptr, 2=after Int64
                string lastViewBindingName = "";
                for (int fi = 0; fi < st.NumFields; fi++)
                {
                    var fieldType = st.Fields[fi];
                    bool isView = IsViewFieldType(fieldType);
                    bool isViewMetadata = false;
                    if (isView)
                    {
                        viewMetadataPhase = 1;
                    }
                    else if (viewMetadataPhase == 1)
                    {
                        // ArrayView wrapper expansion: View → Int64 (length) → optional
                        // Int8 (Dense flag). Mark Int64 as view metadata regardless of
                        // position. The WGSL backend uses an isLastField heuristic to
                        // disambiguate Length from a trailing scalar reduce-value, but
                        // GLSL falls through to a standard 2D-view stride emit on any
                        // unmatched field, generating undefined `u_param{N}_stride[]`
                        // references — so we must claim every Int64-after-view as
                        // metadata to suppress the fallthrough.
                        string typeStr = fieldType.ToString();
                        if (typeStr.Contains("Int64"))
                        {
                            isViewMetadata = true;
                            viewMetadataPhase = 2;
                        }
                        else
                        {
                            viewMetadataPhase = 0;
                        }
                    }
                    else if (viewMetadataPhase == 2)
                    {
                        string typeStr = fieldType.ToString();
                        if (typeStr.Contains("Int8"))
                        {
                            isViewMetadata = true;
                            viewMetadataPhase = 0;
                        }
                        else
                        {
                            viewMetadataPhase = 0;
                        }
                    }

                    bool isScalar = !isView && !isViewMetadata && fieldType is PrimitiveType;

                    // ClrFieldIndex maps an IR view field to its C# wrapper index.
                    // Each view-leading position advances the counter; metadata
                    // doesn't advance; pure scalar fields also advance (they are
                    // their own C# field).
                    int clrIdx = (isView || isScalar) ? clrFieldCounter : -1;
                    if (isView || isScalar) clrFieldCounter++;

                    // Metadata fields share the binding name of their associated view —
                    // their length / Dense-flag value comes from that view's `_length`
                    // uniform. Without this aliasing, the kernel emit references
                    // `u_param{N}_f{metadata_fi}_length` which is never declared.
                    string bindingName = isViewMetadata && !string.IsNullOrEmpty(lastViewBindingName)
                        ? lastViewBindingName
                        : $"u_param{param.Index}_f{fi}";
                    var info = new BodyStructFieldInfoGL
                    {
                        FieldIndex = fi,
                        FieldType = fieldType,
                        IsView = isView,
                        IsScalar = isScalar,
                        IsViewMetadata = isViewMetadata,
                        ClrFieldIndex = clrIdx,
                        SyntheticParamIndex = (param.Index + 1) * 1000 + fi,
                        BindingName = bindingName,
                        ClrFieldName = (clrFields != null && clrIdx >= 0 && clrIdx < clrFields.Length)
                            ? clrFields[clrIdx].Name : $"field_{fi}",
                        ClrFieldType = (clrFields != null && clrIdx >= 0 && clrIdx < clrFields.Length)
                            ? clrFields[clrIdx].FieldType : null,
                    };
                    if (isView)
                        lastViewBindingName = bindingName;

                    if (isView)
                    {
                        // Resolve the view element type (matches WebGPU's resolution path).
                        TypeNode? elemType = null;
                        if (fieldType is ViewType vt) elemType = vt.ElementType;
                        else if (fieldType is StructureType wrapSt && wrapSt.NumFields > 0)
                        {
                            // ArrayView1D wrapper — Field 0 is the inner ViewType (or PointerType post-LowerViews).
                            if (wrapSt.Fields[0] is ViewType innerVt) elemType = innerVt.ElementType;
                            else if (wrapSt.Fields[0] is PointerType innerPt) elemType = innerPt.ElementType;
                        }
                        info.ViewElementType = elemType;

                        // Default GLSL element type (mirrors GetBufferElementType but specialized for body-struct fields).
                        if (elemType is PrimitiveType ept)
                        {
                            switch (ept.BasicValueType)
                            {
                                case BasicValueType.Int8:
                                    info.SubWordElemSize = 1;
                                    info.GlslElementType = "int";
                                    if (info.ClrFieldType?.IsGenericType == true &&
                                        info.ClrFieldType.GetGenericArguments()[0] == typeof(byte))
                                        info.IsUnsignedSubWord = true;
                                    break;
                                case BasicValueType.Int16:
                                    info.SubWordElemSize = 2;
                                    info.GlslElementType = "int";
                                    if (info.ClrFieldType?.IsGenericType == true &&
                                        info.ClrFieldType.GetGenericArguments()[0] == typeof(ushort))
                                        info.IsUnsignedSubWord = true;
                                    break;
                                case BasicValueType.Float16:
                                    info.SubWordElemSize = 2;
                                    info.GlslElementType = "int";
                                    info.IsFloat16 = true;
                                    break;
                                case BasicValueType.BFloat16:
                                    info.SubWordElemSize = 2;
                                    info.GlslElementType = "int";
                                    info.IsBFloat16 = true;
                                    break;
                                case BasicValueType.Float8E4M3:
                                    info.SubWordElemSize = 1;       // 1-byte FP8, 4 per R32I texel
                                    info.GlslElementType = "int";
                                    info.IsFloat8 = true;
                                    info.IsFloat8E4M3 = true;
                                    break;
                                case BasicValueType.Float8E5M2:
                                    info.SubWordElemSize = 1;
                                    info.GlslElementType = "int";
                                    info.IsFloat8 = true;
                                    info.IsFloat8E4M3 = false;
                                    break;
                                case BasicValueType.Float4E2M1:
                                    info.SubWordElemSize = 1;       // 1-byte FP4 (4-bit low nibble), 4 per R32I texel
                                    info.GlslElementType = "int";
                                    info.IsFloat4 = true;
                                    break;
                                case BasicValueType.Float32:
                                    info.GlslElementType = "float";
                                    break;
                                case BasicValueType.Int32:
                                default:
                                    info.GlslElementType = "int";
                                    break;
                                case BasicValueType.Float64:
                                case BasicValueType.Int64:
                                    // V1 throws clean error; v2 follow-up adds emulated 64-bit body-struct fields.
                                    throw new NotSupportedException(
                                        $"WebGL body-struct field '{info.ClrFieldName}' is ArrayView<{ept.BasicValueType}> " +
                                        $"on body-struct param {param.Index}. Emulated 64-bit body-struct view fields " +
                                        $"are not supported in this rc — use separate ArrayView<long>/ArrayView<double> " +
                                        $"kernel parameters or wrap them outside the body struct. Tracked as v2 follow-up.");
                            }
                        }
                    }

                    fields.Add(info);
                }

                _bodyStructParamsGL[param.Index] = fields;
                _bodyStructTypesToSkip.Add(st.Id);
                _generatorArgs.BodyStructTypeIdsToSkip.Add(st.Id);
            }
        }

        #endregion

        #region Parameter Declarations

        private void EmitParameterDeclarations()
        {
            int paramOffset = KernelParamOffset;
            int bindingIndex = 0;

            // Emit dimension uniforms for multi-dimensional kernels
            if (EntryPoint.IndexType == IndexType.Index2D || EntryPoint.IndexType == IndexType.Index3D)
            {
                Builder.AppendLine("uniform highp int u_dimWidth; // grid X dimension");
                Builder.AppendLine("uniform highp int u_dimHeight; // grid Y dimension");
            }

            // Emit grid/group dimension uniforms for kernels using Grid.Idx/Group.Idx
            // These are always available — set by the WebGL dispatch code
            Builder.AppendLine("uniform highp int u_groupDimX; // threads per workgroup (X)");
            Builder.AppendLine("uniform highp int u_gridDimX; // number of workgroups (X)");
            Builder.AppendLine("uniform highp int u_gridDimY; // number of workgroups (Y)");

            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;

                // === Body struct early-out ===
                // Body structs (struct param with 2+ ArrayView fields) are decomposed into
                // one sampler binding per view field. This must run BEFORE IsMultiDim because
                // the existing NumFields∈{3,4,6} heuristic mis-classifies them as fake 1D/2D/3D
                // ArrayView wrappers. Single-ArrayView wrappers (3 fields) and ArrayView1D
                // wrappers continue to flow through the existing single-binding path —
                // IsBodyStruct only returns true for viewFieldCount >= 2.
                if (_bodyStructParamsGL.TryGetValue(param.Index, out var bsFields))
                {
                    bindingIndex = EmitBodyStructDeclarations(param.Index, bsFields, bindingIndex);
                    continue;
                }

                var type = param.ParameterType;
                IsMultiDim(type, out var isMultiDim, out var isView, out var is1DView, out var is2DView, out var is3DView);
                var elementType = UnwrapType(type);
                bool isStruct = elementType is StructureType && !isView;

                if (isView || type is ViewType)
                {
                    // Array-like parameter: use a highp sampler or SSBO-like mechanism
                    // In WebGL2, we use a uniform sampler for input and TF varying for output
                    // Multi-dim views (ArrayView2D/3D) are NOT struct buffers even though
                    // their IR param type is StructureType — they're container structs
                    // holding a ViewType + stride info, not user-defined struct element types
                    bool isStructBuffer = !isMultiDim
                        && elementType is StructureType structElType
                        && !TypeGenerator[elementType].StartsWith("vec")
                        && !TypeGenerator[elementType].StartsWith("ivec")
                        && !TypeGenerator[elementType].StartsWith("uvec");
                    
                    string glslType;
                    if (isStructBuffer)
                    {
                        // Struct element buffer: flatten to per-field layout
                        // All data stored as R32I texture, reinterpret float fields with intBitsToFloat
                        var flatFields = FlattenStructFields(elementType);
                        _structFieldCounts[param.Index] = flatFields.Count;
                        _structFieldTypes[param.Index] = flatFields;
                        glslType = "int"; // R32I texture for raw bit access
                    }
                    else
                    {
                        glslType = GetBufferElementType(elementType);
                    }

                    // Track sub-word element types (Int16, etc.)
                    // GLSL has no 16-bit types; texels are 32-bit.
                    // Sub-word views pack 2 shorts per texel, requiring extraction in shader.
                    if (elementType is PrimitiveType swPt)
                    {
                        if (swPt.BasicValueType == BasicValueType.Int8)
                        {
                            _subWordParams[param.Index] = 1;
                            glslType = "int"; // Force R32I for packed byte data
                            // Detect unsigned (byte) via CLR param type
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
                        else if (swPt.BasicValueType == BasicValueType.Int16)
                        {
                            _subWordParams[param.Index] = 2;
                            glslType = "int"; // Force R32I texture for packed sub-word data
                            // Detect unsigned (ushort) via CLR param type
                            int userIdx = param.Index - KernelParamOffset;
                            if (userIdx >= 0 && userIdx < EntryPoint.Parameters.Count)
                            {
                                var clrType = EntryPoint.Parameters[userIdx];
                                if (clrType.IsGenericType)
                                {
                                    var genArgs = clrType.GetGenericArguments();
                                    if (genArgs.Length > 0 && genArgs[0] == typeof(ushort))
                                        _subWordUnsignedParams.Add(param.Index);
                                }
                            }
                        }
                        else if (swPt.BasicValueType == BasicValueType.Float16)
                        {
                            _subWordParams[param.Index] = 2; // WebGL has no native f16
                            _subWordFloat16Params.Add(param.Index);
                            glslType = "int"; // Force R32I texture for packed sub-word bit data
                        }
                        else if (swPt.BasicValueType == BasicValueType.BFloat16)
                        {
                            _subWordParams[param.Index] = 2; // WebGL has no native bf16
                            _subWordBFloat16Params.Add(param.Index);
                            glslType = "int"; // Force R32I texture for packed sub-word bit data
                        }
                        else if (swPt.BasicValueType == BasicValueType.Float8E4M3 ||
                                 swPt.BasicValueType == BasicValueType.Float8E5M2)
                        {
                            _subWordParams[param.Index] = 1; // FP8 = 1 byte (4 per texel)
                            _subWordFloat8Params[param.Index] = swPt.BasicValueType == BasicValueType.Float8E4M3;
                            glslType = "int"; // Force R32I texture for packed sub-word bit data
                        }
                        else if (swPt.BasicValueType == BasicValueType.Float4E2M1)
                        {
                            _subWordParams[param.Index] = 1; // FP4 = 1 byte storage (4-bit low nibble, 4 per texel)
                            _subWordFloat4Params.Add(param.Index);
                            glslType = "int"; // Force R32I texture for packed sub-word bit data
                        }
                        else if (swPt.BasicValueType == BasicValueType.QInt4)
                        {
                            _subWordParams[param.Index] = 1; // registers as sub-word; the QInt4 tag routes Load to the 8-per-texel nibble path
                            _subWordQInt4Params.Add(param.Index);
                            glslType = "int"; // Force R32I texture for packed sub-word bit data
                        }
                    }

                    // Only declare a sampler uniform for buffers that are actually READ.
                    // Output-only buffers (only in _outputParamIndices, not in _inputParamIndices)
                    // must NOT get sampler uniforms — WebGL2 generates GL_INVALID_OPERATION
                    // when an active sampler has no valid texture bound at draw time.
                    bool isInputBuffer = _inputParamIndices.Contains(param.Index);

                    if (isInputBuffer)
                    {
                        // Input buffer: use a uniform sampler (TBO emulated as texture)
                        // Use isampler2D for int/struct buffers, usampler2D for uint, sampler2D for float
                        string samplerType = glslType switch
                        {
                            "int" => "isampler2D",
                            "uint" => "usampler2D",
                            _ => "sampler2D"
                        };
                        Builder.AppendLine($"uniform highp {samplerType} u_param{param.Index}; // buffer param[{param.Index}]{(isStructBuffer ? " (struct, flattened)" : "")}");
                        // Tile width for 2D texture tiling (supports buffers > MAX_TEXTURE_SIZE)
                        Builder.AppendLine($"uniform highp int u_param{param.Index}_tileW;");
                        // SubView element offset — added to texelFetch indices for SubView buffers
                        Builder.AppendLine($"uniform highp int u_param{param.Index}_offset;");
                    }
                    _parameterBindings.Add(new KernelParameterBinding(param.Index, bindingIndex++, KernelParamKind.Buffer, glslType,
                        structFieldCount: isStructBuffer ? _structFieldCounts[param.Index] : 0));
                    _generatorArgs.ParameterBindings.Add(_parameterBindings[^1]);
                    _bufferGlslTypes[param.Index] = glslType;

                    if (isStructBuffer)
                    {
                        // Emit length uniform for struct buffers (element count, not texel count)
                        Builder.AppendLine($"uniform highp int u_param{param.Index}_length; // struct element count");
                    }
                    else
                    {
                        // Emit length uniform for all view params so GetViewLength can reference it
                        Builder.AppendLine($"uniform highp int u_param{param.Index}_length; // element count");
                    }

                    // Multi-dim stride buffer
                    if (isMultiDim && (is2DView || is3DView))
                    {
                        int strideCount = is3DView ? 3 : 2;
                        Builder.AppendLine($"uniform highp int u_param{param.Index}_stride[{strideCount}]; // stride");
                        _parameterBindings.Add(new KernelParameterBinding(param.Index, bindingIndex++, KernelParamKind.Stride, "int"));
                        _generatorArgs.ParameterBindings.Add(_parameterBindings[^1]);
                    }
                }
                else if (isStruct)
                {
                    // Struct parameter: UBO (no precision qualifier allowed on structs)
                    string glslType = TypeGenerator[elementType];
                    Builder.AppendLine($"uniform {glslType} u_param{param.Index}; // struct param[{param.Index}]");
                    _parameterBindings.Add(new KernelParameterBinding(param.Index, bindingIndex++, KernelParamKind.Scalar, glslType));
                    _generatorArgs.ParameterBindings.Add(_parameterBindings[^1]);
                }
                else
                {
                    // Scalar parameter
                    string glslType = TypeGenerator[elementType];

                    if (_emulatedF64Params.Contains(param.Index))
                    {
                        // f64 emulation: pass as 2 uint uniforms
                        Builder.AppendLine($"uniform highp uint u_param{param.Index}_lo; // emu_f64 low bits");
                        Builder.AppendLine($"uniform highp uint u_param{param.Index}_hi; // emu_f64 high bits");
                        _parameterBindings.Add(new KernelParameterBinding(param.Index, bindingIndex++, KernelParamKind.EmulatedF64, "uint"));
                        _generatorArgs.ParameterBindings.Add(_parameterBindings[^1]);
                    }
                    else if (_emulatedI64Params.Contains(param.Index))
                    {
                        // i64 emulation: pass as 2 uint uniforms
                        Builder.AppendLine($"uniform highp uint u_param{param.Index}_lo; // emu_i64 low bits");
                        Builder.AppendLine($"uniform highp uint u_param{param.Index}_hi; // emu_i64 high bits");
                        _parameterBindings.Add(new KernelParameterBinding(param.Index, bindingIndex++, KernelParamKind.EmulatedI64, "uint"));
                        _generatorArgs.ParameterBindings.Add(_parameterBindings[^1]);
                    }
                    else
                    {
                        Builder.AppendLine($"uniform highp {glslType} u_param{param.Index}; // scalar param[{param.Index}]");
                        _parameterBindings.Add(new KernelParameterBinding(param.Index, bindingIndex++, KernelParamKind.Scalar, glslType));
                        _generatorArgs.ParameterBindings.Add(_parameterBindings[^1]);
                    }
                }
            }

            Builder.AppendLine();
        }

        /// <summary>
        /// Emits per-field uniform declarations for a body-struct kernel parameter.
        /// Each ArrayView field gets its own sampler (`u_param{N}_f{M}`) plus the
        /// standard `_tileW`, `_offset`, `_length` companion uniforms — same shape as
        /// a single-view param, just keyed by the synthetic param index.
        /// Scalar fields of a body struct are emitted as primitive uniforms
        /// (`u_param{N}_f{M}`) using the existing scalar uniform path.
        /// </summary>
        private int EmitBodyStructDeclarations(int paramIndex, List<BodyStructFieldInfoGL> fields, int bindingIndex)
        {
            // Whether each field is read or written by the kernel; computed by
            // AnalyzeInputBuffers / AnalyzeOutputBuffers via synthetic param indices.
            // (Falls back to "input" if not resolved — the existing single-view path
            // does the same thing for parameters with no detected store target.)
            foreach (var f in fields)
            {
                // Suppress emit for ArrayView1D wrapper metadata (Length / Dense flag).
                // These are only present in the IR layout — the per-field length
                // uniform we emit covers the host's needs.
                if (f.IsViewMetadata) continue;

                int synth = f.SyntheticParamIndex;

                // Add manifest entry for runtime dispatch decomposition.
                bool isOutputForManifest = _outputParamIndices.Contains(synth);
                _generatorArgs.BodyStructManifest.Add(new BodyStructBindingEntry
                {
                    ParamIndex = paramIndex,
                    FieldIndex = f.FieldIndex,
                    ClrFieldIndex = f.ClrFieldIndex,
                    SyntheticParamIndex = synth,
                    BindingName = f.BindingName,
                    GlslElementType = f.GlslElementType,
                    IsView = f.IsView,
                    IsScalar = f.IsScalar,
                    SubWordElemSize = f.SubWordElemSize,
                    IsUnsignedSubWord = f.IsUnsignedSubWord,
                    IsFloat16 = f.IsFloat16,
                    IsBFloat16 = f.IsBFloat16,
                    IsOutputBuffer = isOutputForManifest,
                    ClrFieldName = f.ClrFieldName,
                });

                if (f.IsView)
                {
                    bool isInputBuffer = _inputParamIndices.Contains(synth);
                    bool isOutputBuffer = _outputParamIndices.Contains(synth);

                    // Default to input if neither analyzer marked it (read-implicit; matches
                    // the single-view path). Output-only buffers never get a sampler uniform.
                    if (!isInputBuffer && !isOutputBuffer) isInputBuffer = true;

                    string glslType = f.GlslElementType;

                    // Register binding name override so all texelFetch / TF varying sites
                    // resolve `u_param{synthIdx}` to the actual `u_param{realN}_f{M}` name.
                    _bodyStructFieldBindingNames[synth] = f.BindingName;
                    _bufferGlslTypes[synth] = glslType;

                    // Track sub-word tagging on the synthetic index so the existing
                    // sub-word texelFetch + shift+mask machinery picks up body-struct
                    // fields without per-site changes.
                    if (f.SubWordElemSize > 0)
                    {
                        _subWordParams[synth] = f.SubWordElemSize;
                        if (f.IsUnsignedSubWord) _subWordUnsignedParams.Add(synth);
                        if (f.IsFloat16) _subWordFloat16Params.Add(synth);
                        if (f.IsBFloat16) _subWordBFloat16Params.Add(synth);
                        if (f.IsFloat8) _subWordFloat8Params[synth] = f.IsFloat8E4M3;
                        if (f.IsFloat4) _subWordFloat4Params.Add(synth);
                    }

                    if (isInputBuffer)
                    {
                        string samplerType = glslType switch
                        {
                            "int" => "isampler2D",
                            "uint" => "usampler2D",
                            _ => "sampler2D"
                        };
                        Builder.AppendLine($"uniform highp {samplerType} {f.BindingName}; // body-struct field {paramIndex}.{f.ClrFieldName}");
                        Builder.AppendLine($"uniform highp int {f.BindingName}_tileW;");
                        Builder.AppendLine($"uniform highp int {f.BindingName}_offset;");
                    }
                    Builder.AppendLine($"uniform highp int {f.BindingName}_length;");

                    var binding = new KernelParameterBinding(
                        paramIndex: synth,
                        bindingIndex: bindingIndex++,
                        kind: KernelParamKind.Buffer,
                        glslType: glslType,
                        structFieldCount: 0);
                    _parameterBindings.Add(binding);
                    _generatorArgs.ParameterBindings.Add(binding);
                }
                else if (f.IsScalar)
                {
                    // Body-struct primitive scalar field: standard uniform.
                    string scalarGlsl = TypeGenerator[f.FieldType];
                    Builder.AppendLine($"uniform highp {scalarGlsl} {f.BindingName}; // body-struct scalar {paramIndex}.{f.ClrFieldName}");
                    var binding = new KernelParameterBinding(
                        paramIndex: synth,
                        bindingIndex: bindingIndex++,
                        kind: KernelParamKind.Scalar,
                        glslType: scalarGlsl);
                    _parameterBindings.Add(binding);
                    _generatorArgs.ParameterBindings.Add(binding);
                }
                else
                {
                    // Body-struct field that's neither view nor primitive scalar.
                    // E.g. nested struct or unsupported shape. v1 throws clear error.
                    throw new NotSupportedException(
                        $"WebGL body-struct field '{f.ClrFieldName}' on param {paramIndex} has unsupported type " +
                        $"{f.FieldType}. v1 supports ArrayView fields and primitive scalar fields only. " +
                        $"Tracked as v2 follow-up.");
                }
            }
            return bindingIndex;
        }

        private string GetBufferElementType(TypeNode elementType)
        {
            if (elementType is PrimitiveType pt)
            {
                return pt.BasicValueType switch
                {
                    BasicValueType.Float32 => "float",
                    BasicValueType.Int32 => "int",
                    BasicValueType.Int16 => "int",
                    BasicValueType.Int8 => "int",
                    BasicValueType.Float64 when Backend.EnableF64Emulation => "uint",
                    BasicValueType.Int64 when Backend.EnableI64Emulation => "uint",
                    _ => "float"
                };
            }
            return TypeGenerator[elementType];
        }

        /// <summary>
        /// Recursively flattens a struct type into its primitive GLSL field types.
        /// For example, OuterStruct { InnerStruct { float Val }, int ID } → ["float", "int"]
        /// </summary>
        private List<string> FlattenStructFields(TypeNode type)
        {
            var fields = new List<string>();
            if (type is StructureType st)
            {
                foreach (var fieldType in st.Fields)
                {
                    if (fieldType is StructureType nested)
                        fields.AddRange(FlattenStructFields(nested));
                    else
                        fields.Add(GetBufferElementType(fieldType));
                }
            }
            else
            {
                fields.Add(GetBufferElementType(type));
            }
            return fields;
        }

        /// <summary>
        /// Recursively generates (fieldAccessPath, glslType) pairs mapping flat field index
        /// to the hierarchical GLSL struct field access path.
        /// For OuterStruct { InnerStruct { float Val }, int ID }:
        ///   → [(".field_0.field_0", "float"), (".field_1", "int")]
        /// </summary>
        private List<(string Path, string GlslType)> GenerateStructFieldPaths(TypeNode type, string prefix = "")
        {
            var result = new List<(string, string)>();
            if (type is StructureType st)
            {
                int fieldIdx = 0;
                foreach (var fieldType in st.Fields)
                {
                    string fieldPath = $"{prefix}.field_{fieldIdx}";
                    if (fieldType is StructureType nested)
                        result.AddRange(GenerateStructFieldPaths(nested, fieldPath));
                    else
                        result.Add((fieldPath, GetBufferElementType(fieldType)));
                    fieldIdx++;
                }
            }
            else
            {
                result.Add((prefix, GetBufferElementType(type)));
            }
            return result;
        }

        #endregion

        #region Output Varyings (Transform Feedback)

        private void EmitOutputVaryings()
        {
            // For Transform Feedback, output buffers must be declared as 'out' varyings
            // The runtime will configure TF to capture these
            int paramOffset = KernelParamOffset;
            int outIndex = 0;

            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;

                // Body-struct early-out: emit one TF varying per OUTPUT view field, keyed
                // by synthetic param index so the Store path's _singleStoreIndex /
                // _storeSlotIndex lookups find the correct varying.
                if (_bodyStructParamsGL.TryGetValue(param.Index, out var bsFields))
                {
                    foreach (var f in bsFields)
                    {
                        if (!f.IsView) continue;
                        if (!_outputParamIndices.Contains(f.SyntheticParamIndex)) continue;

                        string fieldGlslType = f.GlslElementType;
                        bool needsFlat = fieldGlslType != "float";
                        string flatPrefix = needsFlat ? "flat " : "";
                        string varyingName = $"tf_out_{param.Index}_f{f.FieldIndex}";

                        int storeCount = _outputStoreCount.GetValueOrDefault(f.SyntheticParamIndex, 1);
                        if (storeCount > 1)
                        {
                            for (int slot = 0; slot < storeCount; slot++)
                            {
                                string slotName = $"{varyingName}_s{slot}";
                                Builder.AppendLine($"{flatPrefix}out highp {fieldGlslType} {slotName}; // body-struct TF param[{param.Index}] field {f.FieldIndex} slot {slot}/{storeCount}");
                                var slotInfo = new OutputVaryingInfo(f.SyntheticParamIndex, outIndex++, slotName, fieldGlslType, storeSlot: slot, storeCount: storeCount);
                                _outputVaryings.Add(slotInfo);
                                _generatorArgs.OutputVaryings.Add(slotInfo);
                            }
                        }
                        else
                        {
                            Builder.AppendLine($"{flatPrefix}out highp {fieldGlslType} {varyingName}; // body-struct TF param[{param.Index}] field {f.FieldIndex}");
                            var info = new OutputVaryingInfo(f.SyntheticParamIndex, outIndex++, varyingName, fieldGlslType);
                            _outputVaryings.Add(info);
                            _generatorArgs.OutputVaryings.Add(info);
                        }
                    }
                    continue;
                }

                var type = param.ParameterType;
                // Use the same detection as EmitParameterDeclarations — ArrayView2D/3D
                // appear as struct types in the IR, not ViewType, so we need IsMultiDim
                IsMultiDim(type, out var isMultiDim, out var isView, out var is1DView, out var is2DView, out var is3DView);

                if (isView || type is ViewType || type is PointerType)
                {
                    // Only emit TF varyings for buffer params that actually have Store targets.
                    // Read-only input buffers don't need TF outputs, and including them wastes
                    // TF slots and causes interleaving bugs in multi-buffer readback.
                    if (!_outputParamIndices.Contains(param.Index))
                        continue;

                    // Atomic-only params get a special "vote" TF varying instead of a direct-write varying.
                    // Each thread emits its increment amount; JS sums all votes and adds to buffer[element].
                    if (_atomicParamIndices.Contains(param.Index) && !_outputStoreCount.ContainsKey(param.Index))
                    {
                        string glslType = _bufferGlslTypes.TryGetValue(param.Index, out var cachedType) ? cachedType : "int";
                        bool needsFlat = glslType != "float";
                        string flatPrefix = needsFlat ? "flat " : "";
                        string varyingName = $"tf_atomic_vote_{param.Index}";
                        Builder.AppendLine($"{flatPrefix}out highp {glslType} {varyingName}; // atomic vote for param[{param.Index}]");
                        var varyingInfo = new OutputVaryingInfo(param.Index, outIndex++, varyingName, glslType, isAtomicVote: true);
                        _outputVaryings.Add(varyingInfo);
                        _generatorArgs.OutputVaryings.Add(varyingInfo);
                        continue;
                    }

                    var elementType = UnwrapType(type);
                    bool isEmulatedF64 = _emulatedF64Params.Contains(param.Index);
                    bool isEmulatedI64 = _emulatedI64Params.Contains(param.Index);

                    if (isEmulatedF64 || isEmulatedI64)
                    {
                        // Emulated 64-bit types need TWO uint TF outputs (lo and hi words)
                        Builder.AppendLine($"flat out highp uint tf_out_{param.Index}_lo; // TF output for param[{param.Index}] (emu 64-bit lo)");
                        Builder.AppendLine($"flat out highp uint tf_out_{param.Index}_hi; // TF output for param[{param.Index}] (emu 64-bit hi)");
                        var loInfo = new OutputVaryingInfo(param.Index, outIndex++, $"tf_out_{param.Index}_lo", "uint",
                            isEmulated: true, emulatedSuffix: "lo");
                        var hiInfo = new OutputVaryingInfo(param.Index, outIndex++, $"tf_out_{param.Index}_hi", "uint",
                            isEmulated: true, emulatedSuffix: "hi");
                        _outputVaryings.Add(loInfo);
                        _outputVaryings.Add(hiInfo);
                        _generatorArgs.OutputVaryings.Add(loInfo);
                        _generatorArgs.OutputVaryings.Add(hiInfo);
                    }
                    else if (_structFieldCounts.TryGetValue(param.Index, out var fieldCount) && fieldCount > 0)
                    {
                        // Struct element buffer: emit per-field TF varyings
                        var fieldTypes = _structFieldTypes[param.Index];
                        for (int fi = 0; fi < fieldCount; fi++)
                        {
                            string fieldGlslType = fieldTypes[fi];
                            bool needsFlat = fieldGlslType != "float";
                            string flatPrefix = needsFlat ? "flat " : "";
                            string varyingName = $"tf_out_{param.Index}_f{fi}";
                            Builder.AppendLine($"{flatPrefix}out highp {fieldGlslType} {varyingName}; // TF output for param[{param.Index}] field {fi}");
                            var varyingInfo = new OutputVaryingInfo(param.Index, outIndex++, varyingName, fieldGlslType, fieldIndex: fi);
                            _outputVaryings.Add(varyingInfo);
                            _generatorArgs.OutputVaryings.Add(varyingInfo);
                        }
                    }
                    else
                    {
                        // Use the already-computed GLSL type from EmitParameterDeclarations,
                        // which correctly handles 2D/3D views (StructureType in IR).
                        // Falling back to GetBufferElementType(UnwrapType) for safety.
                        string glslType = _bufferGlslTypes.TryGetValue(param.Index, out var cached)
                            ? cached
                            : GetBufferElementType(UnwrapType(type));

                        // TF varyings must be flat for non-float types AND structs (which may contain int fields)
                        bool needsFlat = glslType != "float";
                        string flatPrefix = needsFlat ? "flat " : "";
                        // GLSL ES 3.0: precision qualifiers are illegal on struct types
                        string precisionPrefix = glslType.StartsWith("struct_") ? "" : "highp ";

                        int storeCount = _outputStoreCount.GetValueOrDefault(param.Index, 1);
                        if (storeCount > 1)
                        {
                            // Multi-store TF: emit N varyings (one per store slot)
                            for (int slot = 0; slot < storeCount; slot++)
                            {
                                string varyingName = $"tf_out_{param.Index}_s{slot}";
                                Builder.AppendLine($"{flatPrefix}out {precisionPrefix}{glslType} {varyingName}; // TF output for param[{param.Index}] slot {slot}/{storeCount}");
                                var varyingInfo = new OutputVaryingInfo(param.Index, outIndex++, varyingName, glslType, storeSlot: slot, storeCount: storeCount);
                                _outputVaryings.Add(varyingInfo);
                                _generatorArgs.OutputVaryings.Add(varyingInfo);
                            }
                        }
                        else
                        {
                            Builder.AppendLine($"{flatPrefix}out {precisionPrefix}{glslType} tf_out_{param.Index}; // TF output for param[{param.Index}]");
                            var varyingInfo = new OutputVaryingInfo(param.Index, outIndex++, $"tf_out_{param.Index}", glslType);
                            _outputVaryings.Add(varyingInfo);
                            _generatorArgs.OutputVaryings.Add(varyingInfo);
                        }
                    }
                }
            }

            Builder.AppendLine();
        }

        /// <summary>
        /// Builds lookup dictionaries from _outputVaryings for O(1) access during code generation.
        /// Must be called after EmitOutputVaryings() populates the list.
        /// </summary>
        private void BuildOutputVaryingIndex()
        {
            _atomicVoteIndex = new Dictionary<int, OutputVaryingInfo>();
            _emulatedIndex = new Dictionary<(int, string), OutputVaryingInfo>();
            _storeSlotIndex = new Dictionary<(int, int), OutputVaryingInfo>();
            _singleStoreIndex = new Dictionary<int, OutputVaryingInfo>();
            _paramFallbackIndex = new Dictionary<int, OutputVaryingInfo>();

            foreach (var ov in _outputVaryings)
            {
                if (ov.IsAtomicVote)
                    _atomicVoteIndex.TryAdd(ov.ParamIndex, ov);
                if (ov.IsEmulated && ov.EmulatedSuffix != null)
                    _emulatedIndex.TryAdd((ov.ParamIndex, ov.EmulatedSuffix), ov);
                if (ov.StoreSlot >= 0)
                    _storeSlotIndex.TryAdd((ov.ParamIndex, ov.StoreSlot), ov);
                if (ov.StoreSlot < 0 && !ov.IsAtomicVote && !ov.IsEmulated && ov.FieldIndex < 0)
                    _singleStoreIndex.TryAdd(ov.ParamIndex, ov);
                _paramFallbackIndex.TryAdd(ov.ParamIndex, ov);
            }
        }

        #endregion

        #region Index Setup

        private void SetupIndexVariables()
        {
            // gl_VertexID provides the flat invocation index
            switch (EntryPoint.IndexType)
            {
                case IndexType.Index1D:
                    AppendLine("int _idx = gl_VertexID;");
                    break;
                case IndexType.Index2D:
                    AppendLine("int _flat_idx = gl_VertexID;");
                    // Need width from a uniform
                    AppendLine("int _idx_x = _flat_idx % u_dimWidth;");
                    AppendLine("int _idx_y = _flat_idx / u_dimWidth;");
                    AppendLine("ivec2 _idx = ivec2(_idx_x, _idx_y);");
                    break;
                case IndexType.Index3D:
                    AppendLine("int _flat_idx = gl_VertexID;");
                    AppendLine("int _idx_x = _flat_idx % u_dimWidth;");
                    AppendLine("int _idx_y = (_flat_idx / u_dimWidth) % u_dimHeight;");
                    AppendLine("int _idx_z = _flat_idx / (u_dimWidth * u_dimHeight);");
                    AppendLine("ivec3 _idx = ivec3(_idx_x, _idx_y, _idx_z);");
                    break;
            }

            // Bind the implicit index parameter (only for auto-grouped kernels, not LoadStreamKernel)
            if (Method.Parameters.Count > 0 && EntryPoint.IndexType != IndexType.None)
            {
                var indexParam = Method.Parameters[0];
                string idxType = EntryPoint.IndexType switch
                {
                    IndexType.Index1D => "int",
                    IndexType.Index2D => "ivec2",
                    IndexType.Index3D => "ivec3",
                    _ => "int"
                };
                var variable = new Variable("_idx", idxType);
                Bind(indexParam, variable);
            }

            // Set gl_Position to dummy value (required for vertex shader)
            AppendLine("gl_Position = vec4(0.0, 0.0, 0.0, 1.0);");
            AppendLine("");
        }

        #endregion

        #region Parameter Binding Load

        private void SetupParameterBindings()
        {
            int paramOffset = KernelParamOffset;

            foreach (var param in Method.Parameters)
            {
                if (param.Index < paramOffset) continue;

                // Body struct parameters: each ArrayView field is accessed via the
                // per-field sampler binding (u_param{N}_f{M}). The body-struct value
                // itself is never materialized as a local — GetField on the struct param
                // is intercepted in the standard codegen path (LEA hook in
                // GenerateCode(LoadElementAddress)) and routed by synthetic param index.
                // No-op here.
                if (_bodyStructParamsGL.ContainsKey(param.Index))
                {
                    AppendLine($"// param{param.Index} is body struct (per-field access via u_param{param.Index}_f<N>)");
                    continue;
                }

                var type = param.ParameterType;
                var variable = Load(param);
                IsMultiDim(type, out var isMultiDim, out var isView, out var is1DView, out var is2DView, out var is3DView);
                bool isViewType = isView || type is ViewType;

                if (isViewType)
                {
                    // Buffer parameters: the variable represents the sampler/texture
                    // Actual access is done via texelFetch in LoadElementAddress
                    AppendLine($"// param{param.Index} is buffer (accessed via u_param{param.Index})");
                }
                else if (_emulatedF64Params.Contains(param.Index))
                {
                    Declare(variable);
                    AppendLine($"{variable.Name} = f64_from_ieee754_bits(u_param{param.Index}_lo, u_param{param.Index}_hi);");
                }
                else if (_emulatedI64Params.Contains(param.Index))
                {
                    Declare(variable);
                    AppendLine($"{variable.Name} = uvec2(u_param{param.Index}_lo, u_param{param.Index}_hi);");
                }
                else
                {
                    Declare(variable);
                    AppendLine($"{variable.Name} = u_param{param.Index};");
                }
            }

            AppendLine("");
        }

        #endregion

        #region Hoisted Declarations

        private void EmitHoistedDeclarations()
        {
            foreach (var value in _hoistedPrimitives)
            {
                var variable = Load(value);
                // All LEA / alloca pointers are int indices in GLSL (see EmitLeaIntPointer),
                // regardless of element type. TypeGenerator maps PointerType → element type
                // (e.g. struct_BVHNode for a struct[] alloca), which produced invalid hoists
                // like `struct_N v_K = struct_N(0)` (BVHRayTraversalTest).
                string type;
                string init;
                if (value.Type is PointerType)
                {
                    type = "int";
                    init = "0";
                }
                else if (value.Type is global::ILGPU.IR.Types.StructureType structType
                    && TypeGenerator[value.Type].StartsWith("struct_"))
                {
                    type = TypeGenerator[value.Type];
                    init = GetStructDefaultInitializer(structType);
                }
                else
                {
                    type = TypeGenerator[value.Type];
                    init = GetDefaultValue(type);
                }
                TryEmitDeclaration(variable.Name, type, init);
            }
        }

        private string GetDefaultValue(string glslType) => glslType switch
        {
            "bool" => "false",
            "int" => "0",
            "uint" => "0u",
            "float" => "0.0",
            "vec2" => "vec2(0.0)",
            "vec3" => "vec3(0.0)",
            "vec4" => "vec4(0.0)",
            "ivec2" => "ivec2(0)",
            "ivec3" => "ivec3(0)",
            "uvec2" => "uvec2(0u)",
            _ => $"{glslType}(0)"
        };

        private void EmitHoistedOrTypedAssignment(Value value, Variable target, string valueExpr, string comment = "")
        {
            if (_hoistedPrimitives.Contains(value))
                AppendLine($"{target} = {valueExpr};{(string.IsNullOrEmpty(comment) ? "" : $" // {comment}")}");
            else
                EmitTypedAssignment(target, TypeGenerator[value.Type], valueExpr, comment);
        }

        /// <summary>
        /// Override Alloca to declare local arrays at function scope (not inside switch/case).
        /// In multi-block kernels, arrays must be visible across all state machine blocks.
        /// </summary>
        public override void GenerateCode(Alloca value)
        {
            if (value.IsArrayAllocation(out var lengthVal))
            {
                int arraySize = lengthVal.Int32Value;
                string elementType = TypeGenerator[value.AllocaType];
                string arrayName = $"local_arr_{_localArrayCounter++}";
                var target = Load(value);
                _allocaArrayNames[target.Name] = arrayName;
                bool firstPtrDecl = declaredVariables.Add(target.Name);
                // Use VariableBuilder when state machine is active to hoist to function scope
                if (IsStateMachineActive)
                {
                    VariableBuilder.AppendLine($"    {elementType} {arrayName}[{arraySize}];");
                    // Declare the alloca's pointer variable as offset=0 so downstream
                    // "generic LEA" codegen that emits `v_target = v_alloca + offset`
                    // compiles cleanly. Without this, LocalMemory<int>(>=32) kernels
                    // that survive SSAStructureConstruction (per its length threshold)
                    // hit "undeclared identifier" on the alloca's pointer variable.
                    // Tuvok 2026-04-24 VP9 iDCT 8x8 WebGL path.
                    if (firstPtrDecl)
                        VariableBuilder.AppendLine($"    int {target.Name} = 0;");
                }
                else
                {
                    AppendLine($"{elementType} {arrayName}[{arraySize}];");
                    if (firstPtrDecl)
                        AppendLine($"int {target.Name} = 0;");
                }
                return;
            }

            // Scalarized-array case: ILGPU IR can optimize `LocalMemory.Allocate<T>(1)`
            // so neither IsArrayAllocation nor IsSimpleAllocation reflects the
            // user-array intent (ArrayLength gets simplified). The signal that
            // distinguishes "user-array, scalarized" from "compiler-scratch scalar"
            // is whether the alloca has a NewView consumer - LocalMemory returns
            // an ArrayView through NewView; compiler-generated scratch doesn't.
            // Treat the user-array case as a 1-element array so downstream
            // NewView + LEA + Store/Load preserve array semantics. Without this,
            // the GLSL emits `int v_X;` (scalar), NewView fallback emits a
            // comment-only alias for v_Y, and downstream `v_Y + offset` is
            // an undeclared identifier. Fixes WebGLTests.NoHelperOutLikeAllocaTest.
            bool hasNewViewConsumer = false;
            foreach (var use in value.Uses)
            {
                if (use.Resolve() is global::ILGPU.IR.Values.NewView)
                {
                    hasNewViewConsumer = true;
                    break;
                }
            }
            if (hasNewViewConsumer)
            {
                string elementType = TypeGenerator[value.AllocaType];
                string arrayName = $"local_arr_{_localArrayCounter++}";
                var target = Load(value);
                _allocaArrayNames[target.Name] = arrayName;
                bool firstPtrDecl = declaredVariables.Add(target.Name);
                if (IsStateMachineActive)
                {
                    VariableBuilder.AppendLine($"    {elementType} {arrayName}[1];");
                    if (firstPtrDecl)
                        VariableBuilder.AppendLine($"    int {target.Name} = 0;");
                }
                else
                {
                    AppendLine($"{elementType} {arrayName}[1];");
                    if (firstPtrDecl)
                        AppendLine($"int {target.Name} = 0;");
                }
                return;
            }

            // Scalar alloca (e.g. `out int sum` lowering to a single int slot).
            // Without this declaration, downstream code that writes through the
            // alloca's variable name (e.g. `v_2 = v_1;` for the implicit zero
            // init, or `v_15 = v_2;` for a load-back after a fn-def helper
            // wrote through its inout param) hits "undeclared identifier".
            // The base WGSL backend pre-declares scalar allocas via
            // SetupAllocations + GenerateCodeInternal, but the GLSL kernel
            // path uses GenerateKernelBody which skips that setup; we declare
            // the slot here on first visit instead.
            {
                string elementType = TypeGenerator[value.AllocaType];
                var target = Load(value);
                if (declaredVariables.Add(target.Name))
                {
                    if (IsStateMachineActive)
                        VariableBuilder.AppendLine($"    {elementType} {target.Name};");
                    else
                        AppendLine($"{elementType} {target.Name};");
                }
            }
        }

        /// <summary>
        /// Override NewArray to declare local arrays at function scope (not inside switch/case).
        /// NewArray is the IR value for inline array initializations like BVHNode[] nodes = [...].
        /// </summary>
        public override void GenerateCode(global::ILGPU.IR.Values.NewArray value)
        {
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
            bool firstPtrDecl = declaredVariables.Add(target.Name);
            if (IsStateMachineActive)
            {
                VariableBuilder.AppendLine($"    {elementType} {arrayName}[{arraySize}];");
                if (firstPtrDecl)
                    VariableBuilder.AppendLine($"    int {target.Name} = 0;");
            }
            else
            {
                AppendLine($"{elementType} {arrayName}[{arraySize}];");
                if (firstPtrDecl)
                    AppendLine($"int {target.Name} = 0;");
            }
        }

        #endregion

        #region Kernel Body Generation

        private void GenerateKernelBody()
        {
            if (Method.Blocks.Count == 1)
            {
                GenerateBlockCode(Method.Blocks.First());
                return;
            }

            // Always use structured code gen — the state machine (switch/case
            // inside a loop) triggers ANGLE D3D11 "Error compiling dynamic
            // vertex executable" when Transform Feedback is active.
            _visitedBlocks.Clear();
            _activeLoopHeaders.Clear();
            GenerateStructuredCode(Method.EntryBlock, null);
        }

        private void GenerateBlockCode(BasicBlock block)
        {
            // Emit all non-terminator values in the block
            foreach (var value in block)
            {
                if (value.Value is TerminatorValue) continue;
                GenerateCodeFor(value.Value);
            }
        }

        /// <summary>
        /// Finds the innermost loop that has the given block as a header.
        /// </summary>
        private Loops<ReversePostOrder, Forwards>.Node? FindLoopForHeader(BasicBlock block)
        {
            for (int i = 0; i < _loops.Count; i++)
            {
                var loop = _loops[i];
                foreach (var header in loop.Headers)
                {
                    if (header == block)
                        return loop;
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if target is a back-edge to an active loop header (should emit 'continue').
        /// </summary>
        private bool IsBackEdgeToActiveLoop(BasicBlock target)
        {
            return _activeLoopHeaders.Contains(target);
        }

        /// <summary>
        /// Emit a branch to target, handling loop continue/break/fall-through.
        /// Returns true if the branch was emitted as continue/break (caller should not recurse).
        /// </summary>
        private bool EmitBranchTarget(BasicBlock target, BasicBlock source, BasicBlock? stop)
        {
            PushPhiValues(target, source);

            // Back-edge to active loop header → continue
            if (IsBackEdgeToActiveLoop(target))
            {
                AppendLine("continue;");
                return true;
            }

            // Target is the stop block → let caller handle it
            if (target == stop)
                return true;

            // Already visited → skip
            if (_visitedBlocks.Contains(target))
                return true;

            return false;
        }

        private void GenerateStructuredCode(BasicBlock current, BasicBlock? stop)
        {
            if (current == null || current == stop || _visitedBlocks.Contains(current)) return;
            _visitedBlocks.Add(current);

            // Check if this block is a loop header
            var loop = FindLoopForHeader(current);
            if (loop != null)
            {
                // ANGLE D3D11 crashes on while(true) — use bounded for loop instead.
                // The loop body's own break/condition controls actual iteration.
                var loopVarName = $"_loop{_loopCounter++}";
                AppendLine($"for (int {loopVarName} = 0; {loopVarName} < 100000; {loopVarName}++) {{");
                PushIndent();

                _activeLoopHeaders.Push(current);

                // Detect the header's exit target before entering the loop body.
                // This is the first block outside the loop that the header's normal
                // exit leads to — used for post-loop continuation.
                BasicBlock? headerExitTarget = null;
                if (current.Terminator is IfBranch headerBranch)
                {
                    if (!loop.Contains(headerBranch.TrueTarget) || ExitsLoopTransitively(headerBranch.TrueTarget, loop))
                        headerExitTarget = headerBranch.TrueTarget;
                    else if (!loop.Contains(headerBranch.FalseTarget) || ExitsLoopTransitively(headerBranch.FalseTarget, loop))
                        headerExitTarget = headerBranch.FalseTarget;
                }
                _glslHeaderExitTarget = headerExitTarget;

                // Remove current from visited so we can re-enter it for the loop body
                _visitedBlocks.Remove(current);

                // Generate the loop body starting from the header
                GenerateLoopBody(current, loop, stop);

                _activeLoopHeaders.Pop();
                _glslHeaderExitTarget = null;

                PopIndent();
                AppendLine("}");

                // Continue with exit blocks after the loop.
                // Use the header's exit target for continuation instead of iterating
                // all loop.Exits — avoids processing body-break-specific intermediate
                // blocks (whose code was emitted inside the break scope).
                BasicBlock? postLoopBlock = headerExitTarget;
                if (postLoopBlock == null && loop.Exits.Length > 0)
                    postLoopBlock = loop.Exits[0]; // Fallback

                if (postLoopBlock != null)
                {
                    var exitBlock = postLoopBlock;
                    // Skip pass-through blocks
                    int skipLimit = 10;
                    while (exitBlock != null && exitBlock != stop
                        && !_visitedBlocks.Contains(exitBlock)
                        && skipLimit-- > 0)
                    {
                        if (exitBlock.Terminator is UnconditionalBranch exitUBranch
                            && !HasNonPhiInstructions(exitBlock))
                        {
                            _visitedBlocks.Add(exitBlock);
                            exitBlock = exitUBranch.Target;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (exitBlock != null && exitBlock != stop && !_visitedBlocks.Contains(exitBlock))
                    {
                        GenerateStructuredCode(exitBlock, stop);
                    }
                }
                return;
            }

            // Not a loop header — emit the block's values
            GenerateBlockCode(current);

            // Handle terminator
            var terminator = current.Terminator;

            if (terminator is ReturnTerminator rt)
            {
                GenerateCode(rt);
            }
            else if (terminator is UnconditionalBranch ub)
            {
                if (!EmitBranchTarget(ub.Target, current, stop))
                    GenerateStructuredCode(ub.Target, stop);
            }
            else if (terminator is IfBranch ib)
            {
                EmitIfBranch(ib, current, stop);
            }
        }

        /// <summary>
        /// Generates the body of a loop (all blocks inside the loop).
        /// </summary>
        private void GenerateLoopBody(BasicBlock current, Loops<ReversePostOrder, Forwards>.Node loop, BasicBlock? outerStop)
        {
            if (current == null || _visitedBlocks.Contains(current)) return;

            // Check if this block is a NESTED loop header (different from current loop)
            var nestedLoop = FindLoopForHeader(current);
            if (nestedLoop != null && nestedLoop != loop)
            {
                // Delegate to GenerateStructuredCode which emits the nested for() construct
                GenerateStructuredCode(current, outerStop);
                return;
            }

            _visitedBlocks.Add(current);

            // Emit the block's values
            GenerateBlockCode(current);

            // Handle terminator with loop-aware logic
            var terminator = current.Terminator;

            if (terminator is ReturnTerminator rt)
            {
                GenerateCode(rt);
            }
            else if (terminator is UnconditionalBranch ub)
            {
                PushPhiValues(ub.Target, current);

                if (IsBackEdgeToActiveLoop(ub.Target))
                {
                    AppendLine("continue;");
                }
                else if (ExitsLoopTransitively(ub.Target, loop))
                {
                    // Exit the loop — trace through intermediate blocks first
                    EmitBreakWithIntermediateCode(ub.Target, current, loop);
                }
                else
                {
                    GenerateLoopBody(ub.Target, loop, outerStop);
                }
            }
            else if (terminator is IfBranch ib)
            {
                EmitLoopIfBranch(ib, current, loop, outerStop);
            }
        }

        /// <summary>
        /// Emits an if/else branch inside a loop body.
        /// </summary>
        private void EmitLoopIfBranch(IfBranch ib, BasicBlock source, Loops<ReversePostOrder, Forwards>.Node loop, BasicBlock? outerStop)
        {
            var trueTarget = ib.TrueTarget;
            var falseTarget = ib.FalseTarget;
            var cond = Load(ib.Condition);

            bool trueIsBackEdge = IsBackEdgeToActiveLoop(trueTarget);
            bool falseIsBackEdge = IsBackEdgeToActiveLoop(falseTarget);
            // DEBUG IL FIX: Use transitive exit detection.
            // In Debug builds, Roslyn inserts intermediate blocks between
            // branches and the actual loop exit. Follow unconditional branch
            // chains to detect indirect exits.
            bool trueIsExit = ExitsLoopTransitively(trueTarget, loop);
            bool falseIsExit = ExitsLoopTransitively(falseTarget, loop);


            // Case 1: Loop condition check — one side continues, other exits
            if (trueIsBackEdge && falseIsExit)
            {
                // if (cond) continue; else break;
                AppendLine($"if (!{cond}) {{");
                PushIndent();
                PushPhiValues(falseTarget, source);
                EmitBreakWithIntermediateCode(falseTarget, source, loop);
                PopIndent();
                AppendLine("}");
                PushPhiValues(trueTarget, source);
                AppendLine("continue;");
                return;
            }
            if (falseIsBackEdge && trueIsExit)
            {
                AppendLine($"if ({cond}) {{");
                PushIndent();
                PushPhiValues(trueTarget, source);
                EmitBreakWithIntermediateCode(trueTarget, source, loop);
                PopIndent();
                AppendLine("}");
                PushPhiValues(falseTarget, source);
                AppendLine("continue;");
                return;
            }

            // Case 2: One side is a back-edge, other continues in-loop
            if (trueIsBackEdge)
            {
                AppendLine($"if ({cond}) {{");
                PushIndent();
                PushPhiValues(trueTarget, source);
                AppendLine("continue;");
                PopIndent();
                AppendLine("}");
                PushPhiValues(falseTarget, source);
                if (falseIsExit)
                    EmitBreakWithIntermediateCode(falseTarget, source, loop);
                else
                    GenerateLoopBody(falseTarget, loop, outerStop);
                return;
            }
            if (falseIsBackEdge)
            {
                AppendLine($"if (!{cond}) {{");
                PushIndent();
                PushPhiValues(falseTarget, source);
                AppendLine("continue;");
                PopIndent();
                AppendLine("}");
                PushPhiValues(trueTarget, source);
                if (trueIsExit)
                    EmitBreakWithIntermediateCode(trueTarget, source, loop);
                else
                    GenerateLoopBody(trueTarget, loop, outerStop);
                return;
            }

            // Case 3: One side exits the loop, other stays
            if (trueIsExit && !falseIsExit)
            {
                AppendLine($"if ({cond}) {{");
                PushIndent();
                PushPhiValues(trueTarget, source);
                EmitBreakWithIntermediateCode(trueTarget, source, loop);
                PopIndent();
                AppendLine("}");
                PushPhiValues(falseTarget, source);
                GenerateLoopBody(falseTarget, loop, outerStop);
                return;
            }
            if (falseIsExit && !trueIsExit)
            {
                AppendLine($"if (!{cond}) {{");
                PushIndent();
                PushPhiValues(falseTarget, source);
                EmitBreakWithIntermediateCode(falseTarget, source, loop);
                PopIndent();
                AppendLine("}");
                PushPhiValues(trueTarget, source);
                GenerateLoopBody(trueTarget, loop, outerStop);
                return;
            }

            // Case 4: Both sides stay in loop — use post-dominator for merge
            var merge = _postDominators?.GetImmediateDominator(source);
            bool mergeInLoop = merge != null && loop.Contains(merge);

            // FIX: When the post-dominator is outside the loop (due to a break-exit
            // path from one branch), find the in-loop merge by checking which target
            // reaches the header through a UB chain (it's the continuation block).
            if (!mergeInLoop && merge != null)
            {
                BasicBlock? inLoopMerge = null;
                if (loop.Contains(trueTarget) && ReachesHeaderThroughUBChain(trueTarget, loop))
                    inLoopMerge = trueTarget;
                else if (loop.Contains(falseTarget) && ReachesHeaderThroughUBChain(falseTarget, loop))
                    inLoopMerge = falseTarget;
                if (inLoopMerge != null)
                {
                    merge = inLoopMerge;
                    mergeInLoop = true;
                }
            }

            // Note: We intentionally do NOT temporarily mark the merge as visited.
            // The merge block may be needed as a continuation target by nested
            // branches (e.g., the merge for an outer if/else might be the PHI
            // write-back + continue block that an inner branch needs to reach).

            // Save visited state before the true branch so that blocks consumed
            // by the true path can be re-visited by the false path. This is
            // essential when both paths share a common back-edge block (e.g.,
            // the PHI write-back + continue block in BVH traversal).
            var visitedBeforeTrueBranch = new HashSet<BasicBlock>(_visitedBlocks);

            AppendLine($"if ({cond}) {{");
            PushIndent();
            PushPhiValues(trueTarget, source);
            if (trueTarget == merge)
            {
                // True goes directly to merge — nothing to generate
            }
            else
            {
                GenerateLoopBody(trueTarget, loop, outerStop);
            }
            PopIndent();
            AppendLine("} else {");
            PushIndent();
            PushPhiValues(falseTarget, source);

            // Restore visited state: remove blocks that were only visited during
            // the true branch, so the false branch can reach shared targets.
            _visitedBlocks.IntersectWith(visitedBeforeTrueBranch);

            if (falseTarget == merge)
            {
                // False goes directly to merge — nothing to generate
            }
            else
            {
                GenerateLoopBody(falseTarget, loop, outerStop);
            }
            PopIndent();
            AppendLine("}");

            // Always regenerate the merge block after the if/else by restoring
            // _visitedBlocks to the pre-branch state. This handles two failure modes:
            //
            // 1. The false branch may have internally visited BB_phi_merge (e.g.,
            //    via BB_if_body → BB_phi_merge), leaving it marked visited and
            //    causing the old `!Contains(merge)` guard to skip generation.
            //
            // 2. Sub-blocks of the merge (e.g., BB_case0 in a color-select switch)
            //    may have been visited during the true or false branch. If only
            //    `Remove(merge)` were used, those sub-blocks would still be marked
            //    visited when the merge re-runs its own nested Case-4 branches,
            //    producing empty else-blocks and wrong phi values.
            //
            // By intersecting back to visitedBeforeTrueBranch we let the merge and
            // its entire sub-tree regenerate cleanly. Any code emitted after an
            // already-executed `continue` is dead but harmless.
            if (mergeInLoop)
            {
                _visitedBlocks.IntersectWith(visitedBeforeTrueBranch);
                GenerateLoopBody(merge, loop, outerStop);
            }
        }

        /// <summary>
        /// Emits an if/else branch outside a loop (acyclic structured flow).
        /// </summary>
        private void EmitIfBranch(IfBranch ib, BasicBlock source, BasicBlock? stop)
        {
            var trueTarget = ib.TrueTarget;
            var falseTarget = ib.FalseTarget;
            var merge = _postDominators?.GetImmediateDominator(source);

            PushPhiValues(trueTarget, source);

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
            PushPhiValues(falseTarget, source);
            // Restore visited state: remove blocks that were only visited during
            // the true branch, so the false branch can reach shared targets.
            _visitedBlocks.IntersectWith(visitedBeforeTrueBranch);
            GenerateStructuredCode(falseTarget, merge);
            PopIndent();
            AppendLine("}");

            if (merge != null && merge != stop)
                GenerateStructuredCode(merge, stop);
        }

        private void PushPhiValues(BasicBlock targetBlock, BasicBlock sourceBlock)
        {
            foreach (var value in targetBlock)
            {
                if (value.Value is PhiValue phi)
                {
                    var targetVar = Load(phi);
                    for (int i = 0; i < phi.Count; i++)
                    {
                        if (phi.Sources[i] == sourceBlock)
                        {
                            var sourceVal = Load(phi[i]);
                            if (!declaredVariables.Contains(targetVar.Name))
                                Declare(targetVar);
                            AppendLine($"{targetVar} = {sourceVal};");

                            // Propagate buffer pointer mappings through phi nodes.
                            // If the source is a LEA-derived pointer, the phi target
                            // must inherit the param mapping so Stores can find TF outputs.
                            if (_leaParamMap.TryGetValue(sourceVal.Name, out var phiParamIdx))
                                _leaParamMap[targetVar.Name] = phiParamIdx;
                            if (_crossBlockPointerExprs.TryGetValue(sourceVal.Name, out var phiExpr))
                                _crossBlockPointerExprs[targetVar.Name] = phiExpr;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Emits code for intermediate blocks between a break source and the loop exit,
        /// then emits the break statement. When ILGPU generates IR for `hitT = t; steps = i; break;`,
        /// the assignments end up in intermediate blocks between the break source and the
        /// merge/exit block. These blocks must have their code emitted before the GLSL break.
        /// 
        /// CRITICAL: Does NOT add intermediate blocks to _visitedBlocks — they may need
        /// to be re-entered by the post-loop code generator.
        /// </summary>
        private void EmitBreakWithIntermediateCode(BasicBlock exitTarget, BasicBlock sourceBlock,
            Loops<ReversePostOrder, Forwards>.Node? currentLoop = null)
        {
            // Trace through intermediate blocks that have unconditional branches.
            // IMPORTANT: Stop when we reach a block outside the current loop — its PHIs
            // belong to an ancestor loop and will be handled by post-loop code emission.
            // Without this check, nested loops push PHI values for ancestor loop counters
            // that reference increment variables not yet computed (triple-nested loop bug).
            var current = exitTarget;
            int maxDepth = 8; // Safety limit
            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (current.Terminator is UnconditionalBranch uBranch)
                {
                    // Stop if next block is outside current loop
                    if (currentLoop != null && !currentLoop.Contains(uBranch.Target))
                        break;
                    // Emit this intermediate block's code (assignments like hitT = t)
                    GenerateBlockCode(current);
                    // Push PHI values to the next block
                    PushPhiValues(uBranch.Target, current);
                    current = uBranch.Target;
                }
                else
                {
                    break; // Not an unconditional branch, stop tracing
                }
            }

            // Follow the exit chain OUTSIDE the loop to find merge-point PHIs.
            // When a loop has multiple exit paths (header normal exit + body break),
            // they converge at a merge block whose PHIs need values from ALL exits.
            // The exit block may be a pass-through (no PHIs) that chains to the merge.
            // This handles the LoopBreakAssignment pattern where break-path assignments
            // need to reach the post-loop merge block.
            if (currentLoop != null)
            {
                // FIX: If the current block is outside the loop and is NOT the header's
                // exit target, it's a body-break-specific intermediate block (e.g.,
                // contains `flagged = true`). Emit its code inside the break scope
                // and mark visited so it's not re-emitted after the loop.
                if (!currentLoop.Contains(current)
                    && _glslHeaderExitTarget != null
                    && current != _glslHeaderExitTarget
                    && HasNonPhiInstructions(current))
                {
                    GenerateBlockCode(current);
                    _visitedBlocks.Add(current);
                }

                // Push PHIs through the exit chain (outside the loop)
                // Stop at parent loop headers to avoid overwriting their PHIs
                var exitChain = current;
                for (int depth = 0; depth < maxDepth; depth++)
                {
                    if (exitChain.Terminator is UnconditionalBranch exitUB)
                    {
                        var next = exitUB.Target;
                        if (IsBlockLoopHeader(next)) break;
                        // Only push if we're outside the loop (don't re-push inside)
                        if (!currentLoop.Contains(exitChain))
                        {
                            PushPhiValues(next, exitChain);
                        }
                        exitChain = next;
                    }
                    else break;
                }
            }

            // Check if the exit path leads directly to a ReturnTerminator
            // (through empty pass-through blocks). If so, this is a genuine kernel
            // return inside the loop, not a break to post-loop code.
            if (IsReturnExit(current))
                AppendLine("return;");
            else
                AppendLine("break;");
        }

        /// <summary>
        /// Checks if a block is a loop header for any loop in the kernel.
        /// </summary>
        private bool IsBlockLoopHeader(BasicBlock block)
        {
            foreach (var loop in _loops)
                foreach (var header in loop.Headers)
                    if (header == block) return true;
            return false;
        }

        /// <summary>
        /// DEBUG IL FIX: Checks whether a block exits the loop, either directly
        /// or transitively through a chain of unconditional branches.
        /// 
        /// In Debug builds, Roslyn inserts intermediate basic blocks between
        /// control flow decisions and their actual targets. This method follows
        /// such chains to determine if the eventual destination is outside the loop.
        /// </summary>
        private static bool ExitsLoopTransitively(
            BasicBlock target,
            Loops<ReversePostOrder, Forwards>.Node loop)
        {
            // Fast path: direct exit
            if (!loop.Contains(target))
                return true;

            // Follow unconditional branch chains through intermediate blocks
            var current = target;
            int maxDepth = 10; // Safety limit
            for (int i = 0; i < maxDepth; i++)
            {
                if (current.Terminator is UnconditionalBranch uBranch)
                {
                    if (!loop.Contains(uBranch.Target))
                        return true;
                    current = uBranch.Target;
                }
                else
                {
                    break;
                }
            }
            return false;
        }

        /// <summary>
        /// DEBUG IL FIX: Checks if a block contains any non-PHI, non-terminator instructions.
        /// Pure pass-through blocks can be skipped in post-loop exit chain processing
        /// since their PHI values were already handled by the loop's break paths.
        /// </summary>
        /// <summary>
        /// Check if a block reaches a loop header through an unconditional branch chain.
        /// Used to identify the loop's continuation block (back-edge source).
        /// </summary>
        private static bool ReachesHeaderThroughUBChain(BasicBlock block, Loops<ReversePostOrder, Forwards>.Node loop)
        {
            var current = block;
            for (int i = 0; i < 10; i++)
            {
                if (current.Terminator is UnconditionalBranch ub)
                {
                    foreach (var header in loop.Headers)
                    {
                        if (ub.Target == header) return true;
                    }
                    if (!loop.Contains(ub.Target)) return false;
                    current = ub.Target;
                }
                else return false;
            }
            return false;
        }

        private static bool HasNonPhiInstructions(BasicBlock block)
        {
            foreach (var value in block)
            {
                if (value.Value is TerminatorValue) continue;
                if (value.Value is PhiValue) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a block (or chain of unconditional branches from it) leads DIRECTLY
        /// to a ReturnTerminator without passing through any blocks that contain real code.
        /// Only follows empty pass-through blocks (PHI-only + unconditional branch/return).
        /// A block with non-PHI instructions is post-loop code, not a direct return path.
        /// Ported from WGSLKernelFunctionGenerator.IsReturnExit.
        /// </summary>
        private static bool IsReturnExit(BasicBlock block)
        {
            int limit = 10;
            var current = block;
            while (current != null && limit-- > 0)
            {
                if (HasNonPhiInstructions(current))
                    return false;
                if (current.Terminator is ReturnTerminator)
                    return true;
                if (current.Terminator is UnconditionalBranch ub)
                    current = ub.Target;
                else
                    break;
            }
            return false;
        }

        /// <summary>
        /// Override EmitPhiAssignments for state machine path to propagate
        /// _leaParamMap and _crossBlockPointerExprs through phi nodes.
        /// Without this, multi-block kernels lose their buffer pointer mappings
        /// after phi resolution, causing Stores to miss TF outputs.
        /// </summary>
        protected override void EmitPhiAssignments(BasicBlock sourceBlock, BasicBlock targetBlock)
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
                        // GLSL ES 3.0: cast to phi variable type if needed
                        string srcExpr = CastIfNeeded(srcVar, phiVar.Type);
                        AppendLine($"{phiVar} = {srcExpr};");

                        // Propagate buffer pointer mappings through phi nodes
                        if (_leaParamMap.TryGetValue(srcVar.Name, out var phiParamIdx))
                            _leaParamMap[phiVar.Name] = phiParamIdx;
                        if (_crossBlockPointerExprs.TryGetValue(srcVar.Name, out var phiExpr))
                            _crossBlockPointerExprs[phiVar.Name] = phiExpr;
                    }
                }
            }
        }

        #endregion

        #region Overrides for Kernel-Specific Code Gen

        public override void GenerateCode(global::ILGPU.IR.Values.Parameter parameter) { }

        public override void GenerateCode(global::ILGPU.IR.Values.AddressSpaceCast value)
        {
            // GLSL has no pointer types - both ref/out params and direct
            // memory accesses lower through the same int variable. Cross-
            // address-space casts (e.g. local alloca -> function param) are
            // therefore a no-op at the GLSL level: alias the cast result to
            // the source variable so downstream code (notably fn-def calls
            // with `inout int` ref params) sees the original alloca and the
            // helper's writes propagate back via inout semantics. Without
            // this aliasing the cast emitted `int v_X; v_X = v_Y;` which
            // copied the value at the call site - the helper then wrote to
            // a temporary and the caller never observed the result.
            var source = Load(value.Value);
            valueVariables[value] = source;
        }

        public override void GenerateCode(ReturnTerminator value)
        {
            // Kernel: just return from main()
            AppendLine("return;");
        }

        /// <summary>
        /// Emits 0 to every atomic vote TF varying at the start of main() so that
        /// threads taking an early-return path contribute nothing to the accumulated sum.
        /// Must be called after EmitOutputVaryings() populates _outputVaryings.
        /// </summary>
        private void InitializeAtomicVoteVaryings()
        {
            foreach (var varying in _outputVaryings)
            {
                if (varying.IsAtomicVote)
                    AppendLine($"{varying.VaryingName} = {varying.GlslType}(0); // atomic vote default: no contribution");
            }
        }

        /// <summary>
        /// Emulates Atomic.Add in WebGL2 vertex shaders using the "vote" TF pattern:
        /// each thread emits its increment to a TF varying; JS sums all per-vertex
        /// contributions and adds the total to buffer[element] after the draw.
        ///
        /// Limitations:
        /// - The return value (old value before add) is approximated as 0. Kernels
        ///   that use Atomic.Add as a compaction slot allocator (e.g. DepthToGaussianKernel)
        ///   cannot be correctly emulated in WebGL2 — use the ILGPU 2D-dispatch fix instead.
        /// - Only Add is handled; other atomic kinds fall back to the base no-op.
        /// </summary>
        public override void GenerateCode(GenericAtomic value)
        {
            // WebGL only supports Atomic.Add via the vote TF varying workaround.
            // All other atomic operations (And, Or, Xor, Min, Max, Exchange) are
            // unsupported and MUST throw - silent zeros produce wrong results.
            if (value.Kind != AtomicKind.Add)
            {
                throw new NotSupportedException(
                    $"Atomic.{value.Kind} is not supported on the WebGL backend. " +
                    $"Only Atomic.Add has partial support (via Transform Feedback vote pattern). " +
                    $"Use WebGPU, Wasm, or a desktop backend for kernels that require {value.Kind} atomics.");
            }

            var target = Load(value);
            var address = Load(value.Target);
            var atomicVal = Load(value.Value);

            // Return value: approximate as 0 (WebGL Add vote pattern does not return old value)
            if (!_hoistedPrimitives.Contains(value))
                Declare(target);
            AppendLine($"{target} = {target.Type}(0); // atomic return (WebGL2 Add vote emulation)");

            // Emit the increment to the atomic vote TF varying so JS can accumulate it
            if (_leaParamMap.TryGetValue(address.ToString(), out var paramIdx))
            {
                _atomicVoteIndex!.TryGetValue(paramIdx, out var atomicVarying);
                if (atomicVarying != null)
                {
                    string castVal = CastIfNeeded(atomicVal, atomicVarying.GlslType ?? "int");
                    AppendLine($"{atomicVarying.VaryingName} = {castVal}; // atomic vote emit");
                }
            }
        }

        /// <summary>
        /// Cross-assembly struct accessors (e.g. SpawnDev.ILGPU.ML.Tensors.TensorView.Get2D)
        /// compile to <see cref="MethodCall"/> nodes when the kernel lambda lives in a
        /// different assembly. <see cref="GLSLFunctionGenerator"/> cannot route buffer
        /// access for body-struct fields (no <see cref="TryResolveBodyStructField"/>), so
        /// fn-call emission silently zeros. Inline single-block implementations at the
        /// kernel call site by binding callee parameters to the caller's variables -
        /// same pattern as <see cref="WGSLKernelFunctionGenerator.GenerateCode(MethodCall)"/>.
        /// </summary>
        public override void GenerateCode(MethodCall methodCall)
        {
            var targetMethod = methodCall.Target;

            if (targetMethod.HasImplementation
                && !targetMethod.HasFlags(MethodFlags.External)
                && !targetMethod.HasFlags(MethodFlags.Intrinsic)
                && targetMethod.HasFlags(MethodFlags.Inline))
            {
                var blockList = targetMethod.Blocks.ToList();
                if (blockList.Count == 1)
                {
                    InlineCrossAssemblyMethodCall(methodCall, targetMethod, blockList[0]);
                    return;
                }
            }

            base.GenerateCode(methodCall);
        }

        private void InlineCrossAssemblyMethodCall(
            MethodCall methodCall,
            Method targetMethod,
            BasicBlock block)
        {
            var savedParamBindings = new List<(Value Param, Variable? Previous)>();

            _inlineParamAliasesGL.Clear();
            for (int i = 0; i < targetMethod.Parameters.Count && i < methodCall.Nodes.Length; i++)
            {
                var param = targetMethod.Parameters[i];
                var arg = methodCall.Nodes[i].Resolve();
                if (param is global::ILGPU.IR.Values.Parameter hp)
                    _inlineParamAliasesGL[hp] = arg;

                valueVariables.TryGetValue(param, out var previous);
                savedParamBindings.Add((param, previous));
                valueVariables[param] = Load(arg);
            }

            Variable? resultVar = null;
            if (!methodCall.Type.IsVoidType)
            {
                resultVar = Load(methodCall);
                Declare(resultVar);
            }

            foreach (var value in block)
                GenerateCodeFor(value);

            if (block.Terminator is ReturnTerminator ret
                && resultVar != null
                && !ret.IsVoidReturn)
            {
                AppendLine($"{resultVar} = {Load(ret.ReturnValue.Resolve())};");
            }

            foreach (var (param, previous) in savedParamBindings)
            {
                if (previous != null)
                    valueVariables[param] = previous;
                else
                    valueVariables.Remove(param);
            }

            _inlineParamAliasesGL.Clear();
        }

        public override void GenerateCode(LoadElementAddress value)
        {
            var target = Load(value);
            var offset = Load(value.Offset);

            // Body-struct view-field LEA: when LEA's source resolves (possibly through
            // an ArrayView1D wrapper's nested GetField extracting BaseView) to
            // GetField(bodyStructParam, viewFieldIdx), route to the per-field synthetic
            // param index. Walks chained GetFields to handle both raw ArrayView<T> body
            // fields (one GetField step) and ArrayView1D<T,Stride> body fields (two
            // GetField steps: outer extracts wrapper, inner extracts BaseView).
            if (TryResolveBodyStructField(value.Source.Resolve(),
                    out var bsParamIdx, out var bsFieldIdx, out var bsViewInfo))
            {
                int synth = bsViewInfo.SyntheticParamIndex;
                string bindingName = bsViewInfo.BindingName;

                if (_crossBlockPointers.Contains(value))
                    _crossBlockPointerExprs[target.Name] = $"texelFetch({bindingName}, ivec2((int({offset}) + {bindingName}_offset) % {bindingName}_tileW, (int({offset}) + {bindingName}_offset) / {bindingName}_tileW), 0)";

                EmitLeaIntPointer(target, offset.ToString(), $"body-struct LEA into {bindingName}");
                Bind(value, new Variable(target.Name, "int"));
                _leaParamMap[target.Name] = synth;
                if (_subWordParams.ContainsKey(synth))
                    _subWordLEAVars[target.Name] = synth;
                return;
            }

            if (ResolveToParameter(value.Source) is global::ILGPU.IR.Values.Parameter param)
            {
                int paramOffset = KernelParamOffset;
                if (param.Index >= paramOffset)
                {
                    // Buffer access via texelFetch from sampler2D
                    if (_emulatedF64Params.Contains(param.Index) || _emulatedI64Params.Contains(param.Index))
                    {
                        bool isF64 = _emulatedF64Params.Contains(param.Index);
                        _emulatedVarMappings[target.Name] = (param.Index, isF64);

                        AppendLine($"int {target.Name}_base_idx = int({offset}) * 2;");
                        return;
                    }

                    if (_crossBlockPointers.Contains(value))
                    {
                        string cbBn = GetParamBindingName(param.Index);
                        _crossBlockPointerExprs[target.Name] = $"texelFetch({cbBn}, ivec2((int({offset}) + {cbBn}_offset) % {cbBn}_tileW, (int({offset}) + {cbBn}_offset) / {cbBn}_tileW), 0)";
                    }

                    EmitLeaIntPointer(target, offset.ToString(), $"LEA into param{param.Index}");
                    // Re-bind with correct type: the GLSL variable is declared as 'int',
                    // but the IR type is a pointer whose element type may differ (e.g. float).
                    Bind(value, new Variable(target.Name, "int"));
                    // Store the param index for Load/Store to use
                    _leaParamMap[target.Name] = param.Index;
                    // Track sub-word LEA for Load extraction
                    if (_subWordParams.ContainsKey(param.Index))
                        _subWordLEAVars[target.Name] = param.Index;
                    return;
                }
            }

            // Fallback: check if the source variable is already in _leaParamMap
            // This happens for 2D/3D views where GetField(viewParam, 0) creates a buffer
            // alias that is in _leaParamMap, but ResolveToParameter can't trace through
            // the StructureType → GetField → ViewType chain
            var sourceVal = Load(value.Source);
            if (_leaParamMap.TryGetValue(sourceVal.ToString(), out var sourceParamIdx))
            {
                if (_crossBlockPointers.Contains(value))
                {
                    string srcBn = GetParamBindingName(sourceParamIdx);
                    _crossBlockPointerExprs[target.Name] = $"texelFetch({srcBn}, ivec2((int({offset}) + {srcBn}_offset) % {srcBn}_tileW, (int({offset}) + {srcBn}_offset) / {srcBn}_tileW), 0)";
                }

                EmitLeaIntPointer(target, offset.ToString(), $"LEA into param{sourceParamIdx} (via alias)");
                Bind(value, new Variable(target.Name, "int"));
                _leaParamMap[target.Name] = sourceParamIdx;
                if (_subWordParams.ContainsKey(sourceParamIdx))
                    _subWordLEAVars[target.Name] = sourceParamIdx;
                return;
            }

            // Local-alloca LEA: when source is an array alloca we declared as
            // `local_arr_N[size]` with accompanying `int v_source = 0;`, register
            // `_leaArrayExprs[target] = "local_arr_N[offset]"` so downstream Store/Load
            // emit `local_arr_N[offset] = val;` / `val = local_arr_N[offset];` instead
            // of the generic-LEA integer-sum pattern that gets overwritten by Store's
            // fallback branch. Without this hand-off, stores go to the LEA scalar
            // itself (`v_target = val`) and disappear, producing silent-wrong-output.
            // Tuvok 2026-04-24 VP9 iDCT 8x8 WebGL, final data-correctness layer.
            if (_allocaArrayNames.TryGetValue(sourceVal.Name, out var localArrName))
            {
                Declare(target);
                AppendLine($"{target} = {sourceVal} + {offset}; // LEA into {localArrName}");
                _leaArrayExprs[target.Name] = $"{localArrName}[{offset}]";
                return;
            }

            Declare(target);
            AppendLine($"{target} = {sourceVal} + {offset}; // generic LEA");
        }

        private readonly Dictionary<string, int> _leaParamMap = new();
        /// <summary>Maps parameter index → GLSL element type ("int", "uint", "float") for buffer params.</summary>
        private readonly Dictionary<int, string> _bufferGlslTypes = new();

        public override void GenerateCode(global::ILGPU.IR.Values.Load loadVal)
        {
            var target = Load(loadVal);
            var source = Load(loadVal.Source);

            // Local array element access via LAEA pointer
            if (_leaArrayExprs.TryGetValue(source.Name, out var arrayExpr))
            {
                if (_hoistedPrimitives.Contains(loadVal))
                    AppendLine($"{target} = {arrayExpr};");
                else
                    EmitTypedAssignment(target, TypeGenerator[loadVal.Type], arrayExpr);
                return;
            }

            // Check for direct parameter load (View alias)
            if (loadVal.Source.Resolve() is global::ILGPU.IR.Values.Parameter param && param.Type is ViewType)
            {
                int paramOffset = KernelParamOffset;
                if (param.Index >= paramOffset)
                {
                    AppendLine($"// {target} aliases buffer param{param.Index}");
                    _leaParamMap[target.Name] = param.Index;
                    return;
                }
            }

            // Emulated 64-bit buffer access
            if (_emulatedVarMappings.TryGetValue(source.ToString(), out var emulInfo))
            {
                string baseIdxVar = $"{source}_base_idx";
                Declare(target);
                // Check if this sampler is already integer-typed
                bool emuSamplerIsInt = _bufferGlslTypes.TryGetValue(emulInfo.ParamIndex, out var emuSamplerType)
                    && (emuSamplerType == "int" || emuSamplerType == "uint");
                string emuBn = GetParamBindingName(emulInfo.ParamIndex);
                string emuFetchLo = $"texelFetch({emuBn}, ivec2(({baseIdxVar} + {emuBn}_offset) % {emuBn}_tileW, ({baseIdxVar} + {emuBn}_offset) / {emuBn}_tileW), 0).r";
                string emuFetchHi = $"texelFetch({emuBn}, ivec2(({baseIdxVar} + 1 + {emuBn}_offset) % {emuBn}_tileW, ({baseIdxVar} + 1 + {emuBn}_offset) / {emuBn}_tileW), 0).r";
                // Integer samplers already return int/uint; float samplers need floatBitsToUint
                string emuValLo = emuSamplerIsInt ? $"uint({emuFetchLo})" : $"floatBitsToUint({emuFetchLo})";
                string emuValHi = emuSamplerIsInt ? $"uint({emuFetchHi})" : $"floatBitsToUint({emuFetchHi})";
                if (emulInfo.IsF64)
                {
                    AppendLine($"{target} = f64_from_ieee754_bits(");
                    PushIndent();
                    AppendLine($"{emuValLo},");
                    AppendLine($"{emuValHi});");
                    PopIndent();
                }
                else
                {
                    AppendLine($"{target} = uvec2(");
                    PushIndent();
                    AppendLine($"{emuValLo},");
                    AppendLine($"{emuValHi});");
                    PopIndent();
                }
                return;
            }

            // Cross-block pointer fix
            if (_crossBlockPointerExprs.TryGetValue(source.ToString(), out var inlineExpr))
            {
                string crossExpr = $"{inlineExpr}.r";
                // For integer samplers, texelFetch already returns int/uint; just cast to target type
                string castExpr = CastIfNeeded(crossExpr, target.Type);
                if (_hoistedPrimitives.Contains(loadVal))
                    AppendLine($"{target} = {castExpr};");
                else
                {
                    Declare(target);
                    AppendLine($"{target} = {castExpr};");
                }
                return;
            }

            // SUB-WORD VIEW: extract sub-word value from packed 32-bit texel
            if (_subWordLEAVars.TryGetValue(source.ToString(), out var subWordParamIdx))
            {
                var idx = source.ToString();
                var elemSize = _subWordParams[subWordParamIdx];
                string swBn = GetParamBindingName(subWordParamIdx);
                string extractExpr;
                if (_subWordQInt4Params.Contains(subWordParamIdx))
                {
                    // Packed 4-bit nibble extraction: 8 nibbles per R32I texel.
                    // texel = idx>>3, shift = (idx&7)*4, mask 0xF, sign-extend (signed QInt4).
                    var texelIdx = $"(({idx}) / 8 + {swBn}_offset)";
                    var shift = $"(({idx}) % 8) * 4";
                    var fetch = $"texelFetch({swBn}, ivec2({texelIdx} % {swBn}_tileW, {texelIdx} / {swBn}_tileW), 0).r";
                    var rawNib = $"(({fetch}) >> ({shift})) & 0xF";
                    extractExpr = $"(({rawNib}) >= 8 ? ({rawNib}) - 16 : ({rawNib}))"; // sign-extend signed 4-bit
                }
                else if (elemSize == 1)
                {
                    // Byte extraction: 4 bytes per texel
                    var texelIdx = $"(({idx}) / 4 + {swBn}_offset)";
                    var shift = $"(({idx}) % 4) * 8";
                    var fetch = $"texelFetch({swBn}, ivec2({texelIdx} % {swBn}_tileW, {texelIdx} / {swBn}_tileW), 0).r";
                    var rawByte = $"(({fetch}) >> ({shift})) & 0xFF";
                    if (_subWordFloat8Params.TryGetValue(subWordParamIdx, out bool fp8IsE4M3))
                        // FP8: same 1-byte storage, convert the raw byte to float (value computes as float).
                        extractExpr = $"{(fp8IsE4M3 ? "_e4m3_to_f32" : "_e5m2_to_f32")}(uint({rawByte}))";
                    else if (_subWordFloat4Params.Contains(subWordParamIdx))
                        // FP4: same 1-byte storage, convert the raw byte (4-bit value in low nibble) to float.
                        extractExpr = $"_e2m1_to_f32(uint({rawByte}))";
                    else if (_subWordUnsignedParams.Contains(subWordParamIdx))
                        extractExpr = rawByte; // byte: zero-extend (0-255)
                    else
                        extractExpr = $"(({rawByte}) >= 128 ? ({rawByte}) - 256 : ({rawByte}))"; // sbyte: sign-extend
                }
                else if (_subWordFloat16Params.Contains(subWordParamIdx))
                {
                    // Float16 extraction: 2 halves per texel, call _f16_to_f32 helper.
                    // Helper from GLSLEmulationLibrary.F16Functions handles all cases:
                    // signed zero, denormals (flushed), normal, Inf, NaN. Previous
                    // inline code mishandled exp==0 denormals and exp==31 Inf/NaN.
                    var texelIdx = $"({idx} / 2 + {swBn}_offset)";
                    var shift = $"(({idx}) % 2) * 16";
                    var fetch = $"texelFetch({swBn}, ivec2({texelIdx} % {swBn}_tileW, {texelIdx} / {swBn}_tileW), 0).r";
                    var rawExpr = $"uint((({fetch}) >> ({shift})) & 0xFFFF)";
                    extractExpr = $"_f16_to_f32({rawExpr})";
                }
                else if (_subWordBFloat16Params.Contains(subWordParamIdx))
                {
                    // bfloat16 extraction: 2 bf16 per texel, call _bf16_to_f32 helper
                    // (same 2-byte sub-word texel layout as Float16, different conversion).
                    var texelIdx = $"({idx} / 2 + {swBn}_offset)";
                    var shift = $"(({idx}) % 2) * 16";
                    var fetch = $"texelFetch({swBn}, ivec2({texelIdx} % {swBn}_tileW, {texelIdx} / {swBn}_tileW), 0).r";
                    var rawExpr = $"uint((({fetch}) >> ({shift})) & 0xFFFF)";
                    extractExpr = $"_bf16_to_f32({rawExpr})";
                }
                else if (_subWordUnsignedParams.Contains(subWordParamIdx))
                {
                    // UInt16 extraction: 2 ushorts per texel, zero-extension
                    var texelIdx = $"({idx} / 2 + {swBn}_offset)";
                    var shift = $"(({idx}) % 2) * 16";
                    var fetch = $"texelFetch({swBn}, ivec2({texelIdx} % {swBn}_tileW, {texelIdx} / {swBn}_tileW), 0).r";
                    extractExpr = $"(({fetch}) >> ({shift})) & 0xFFFF"; // zero-extend: & 0xFFFF gives 0-65535
                }
                else // elemSize == 2, Int16
                {
                    // Int16 extraction: 2 shorts per texel, with sign extension
                    var texelIdx = $"({idx} / 2 + {swBn}_offset)";
                    var shift = $"(({idx}) % 2) * 16";
                    var fetch = $"texelFetch({swBn}, ivec2({texelIdx} % {swBn}_tileW, {texelIdx} / {swBn}_tileW), 0).r";
                    var rawExpr = $"(({fetch}) >> ({shift})) & 0xFFFF";
                    // Sign-extend: if bit 15 set, extend to full int negative
                    extractExpr = $"(({rawExpr}) >= 32768 ? ({rawExpr}) - 65536 : ({rawExpr}))";
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

            // LEA-based buffer read via texelFetch
            if (_leaParamMap.TryGetValue(source.ToString(), out var leaParamIdx))
            {
                string leaBn = GetParamBindingName(leaParamIdx);
                // Check if this is a struct buffer load
                if (_structFieldCounts.TryGetValue(leaParamIdx, out var structFieldCount) && structFieldCount > 0)
                {
                    // Struct buffer: construct struct from per-field texelFetch
                    // Find the element type to get field paths
                    var leaParam = Method.Parameters.FirstOrDefault(p => p.Index == leaParamIdx);
                    if (leaParam != null)
                    {
                        var elemType = UnwrapType(leaParam.ParameterType);
                        var fieldPaths = GenerateStructFieldPaths(elemType);
                        string structType = TypeGenerator[elemType];
                        Declare(target);
                        // Use intBitsToFloat for float fields since texture is R32I
                        for (int fi = 0; fi < fieldPaths.Count; fi++)
                        {
                            string fetchExpr = $"texelFetch({leaBn}, ivec2((int({source}) * {structFieldCount} + {fi} + {leaBn}_offset) % {leaBn}_tileW, (int({source}) * {structFieldCount} + {fi} + {leaBn}_offset) / {leaBn}_tileW), 0).r";
                            string valExpr = fieldPaths[fi].GlslType == "float"
                                ? $"intBitsToFloat({fetchExpr})"
                                : fetchExpr;
                            AppendLine($"{target}{fieldPaths[fi].Path} = {valExpr};");
                        }
                        return;
                    }
                }

                // Non-struct: standard single-texel load
                string fetchExprStd = $"texelFetch({leaBn}, ivec2((int({source}) + {leaBn}_offset) % {leaBn}_tileW, (int({source}) + {leaBn}_offset) / {leaBn}_tileW), 0).r";
                // For integer samplers (isampler2D/usampler2D), texelFetch already returns int/uint.
                // For float samplers, texelFetch returns float; we may need floatBitsToInt/floatBitsToUint.
                bool samplerIsInt = _bufferGlslTypes.TryGetValue(leaParamIdx, out var samplerGlslType)
                    && (samplerGlslType == "int" || samplerGlslType == "uint");
                string castExpr;
                if (samplerIsInt)
                {
                    // Integer sampler: texelFetch returns int (isampler2D) or uint (usampler2D)
                    // Just cast to target type normally (int→int is identity, uint→int is cast)
                    castExpr = CastIfNeeded(fetchExprStd, target.Type);
                }
                else
                {
                    // Float sampler: texelFetch returns float
                    // For integer targets, we need bit-level reinterpretation
                    if (target.Type == "int")
                        castExpr = $"floatBitsToInt({fetchExprStd})";
                    else if (target.Type == "uint")
                        castExpr = $"floatBitsToUint({fetchExprStd})";
                    else
                        castExpr = CastIfNeeded(fetchExprStd, target.Type);
                }
                if (_hoistedPrimitives.Contains(loadVal))
                    AppendLine($"{target} = {castExpr};");
                else
                {
                    Declare(target);
                    AppendLine($"{target} = {castExpr};");
                }
                return;
            }

            // Standard load
            if (_hoistedPrimitives.Contains(loadVal))
                AppendLine($"{target} = {source};");
            else
            {
                Declare(target);
                AppendLine($"{target} = {source};");
            }
        }

        public override void GenerateCode(global::ILGPU.IR.Values.Store storeVal)
        {
            var address = Load(storeVal.Target);
            var val = Load(storeVal.Value);

            // Local array element write via LAEA pointer
            if (_leaArrayExprs.TryGetValue(address.Name, out var arrayExpr))
            {
                AppendLine($"{arrayExpr} = {val};");
                return;
            }

            // Emulated 64-bit buffer store via TF output
            if (_emulatedVarMappings.TryGetValue(address.ToString(), out var emulInfo))
            {
                // Find the lo and hi TF varyings for this param
                _emulatedIndex!.TryGetValue((emulInfo.ParamIndex, "lo"), out var loOutput);
                _emulatedIndex!.TryGetValue((emulInfo.ParamIndex, "hi"), out var hiOutput);
                if (loOutput != null && hiOutput != null)
                {
                    if (emulInfo.IsF64)
                    {
                        // f64: vec2(hi_float, lo_float) double-float representation
                        // Must convert back to IEEE 754 bits via f64_to_ieee754_bits() → uvec2(lo, hi)
                        AppendLine($"{{");
                        AppendLine($"  uvec2 _ieee_bits = f64_to_ieee754_bits({val});");
                        AppendLine($"  {loOutput.VaryingName} = _ieee_bits.x;");
                        AppendLine($"  {hiOutput.VaryingName} = _ieee_bits.y;");
                        AppendLine($"}}");
                    }
                    else
                    {
                        // i64/u64: uvec2(lo, hi) — already uint components
                        AppendLine($"{loOutput.VaryingName} = {val}.x;");
                        AppendLine($"{hiOutput.VaryingName} = {val}.y;");
                    }
                }
                return;
            }

            // LEA-based buffer write → TF output
            if (_leaParamMap.TryGetValue(address.ToString(), out var leaParamIdx))
            {
                // Check if this is a struct buffer store
                if (_structFieldCounts.TryGetValue(leaParamIdx, out var structFieldCount) && structFieldCount > 0)
                {
                    // Struct buffer: decompose struct into per-field TF outputs
                    var structOutputs = _outputVaryings.Where(o => o.ParamIndex == leaParamIdx && o.FieldIndex >= 0).OrderBy(o => o.FieldIndex).ToList();
                    var leaParam = Method.Parameters.FirstOrDefault(p => p.Index == leaParamIdx);
                    if (leaParam != null && structOutputs.Count > 0)
                    {
                        var elemType = UnwrapType(leaParam.ParameterType);
                        var fieldPaths = GenerateStructFieldPaths(elemType);
                        for (int fi = 0; fi < Math.Min(fieldPaths.Count, structOutputs.Count); fi++)
                        {
                            string fieldExpr = $"{val}{fieldPaths[fi].Path}";
                            // TF varying type matches the field's native type, so direct assignment
                            AppendLine($"{structOutputs[fi].VaryingName} = {fieldExpr};");
                        }
                        return;
                    }
                }

                int storeCount = _outputStoreCount.GetValueOrDefault(leaParamIdx, 1);
                if (storeCount > 1)
                {
                    // Multi-store TF: route this store to the next sequential slot
                    int slot = _currentStoreSlot.GetValueOrDefault(leaParamIdx, 0);
                    _storeSlotIndex!.TryGetValue((leaParamIdx, slot), out var slotOutput);
                    if (slotOutput != null)
                    {
                        string tfValExpr = CastIfNeeded(val, slotOutput.GlslType ?? "float");
                        AppendLine($"{slotOutput.VaryingName} = {tfValExpr};");
                        _currentStoreSlot[leaParamIdx] = slot + 1;
                        return;
                    }
                }

                _singleStoreIndex!.TryGetValue(leaParamIdx, out var output);
                if (output != null)
                {
                    // Float16 sub-word Store: call _f32_to_f16 helper, cast to int
                    // for the Transform Feedback varying. Helper from
                    // GLSLEmulationLibrary.F16Functions preserves mantissa on overflow
                    // so NaN stays NaN (previous inline code zeroed mantissa, losing NaN).
                    if (_subWordFloat16Params.Contains(leaParamIdx))
                    {
                        AppendLine($"{output.VaryingName} = int(_f32_to_f16({val}));");
                        return;
                    }
                    if (_subWordBFloat16Params.Contains(leaParamIdx))
                    {
                        AppendLine($"{output.VaryingName} = int(_f32_to_bf16({val}));");
                        return;
                    }
                    if (_subWordFloat8Params.TryGetValue(leaParamIdx, out bool fp8IsE4M3Store))
                    {
                        // FP8 sub-word store: convert f32 -> 8-bit pattern, write to the TF varying;
                        // host readback packs 4 bytes per texel (the byte sub-word layout).
                        AppendLine($"{output.VaryingName} = int({(fp8IsE4M3Store ? "_f32_to_e4m3" : "_f32_to_e5m2")}({val}));");
                        return;
                    }
                    if (_subWordFloat4Params.Contains(leaParamIdx))
                    {
                        // FP4 sub-word store: convert f32 -> 4-bit pattern (low nibble), write to the TF
                        // varying; host readback packs 4 bytes per texel (the byte sub-word layout).
                        // This branch fires ONLY when the TARGET element is FP4. A widening store to a
                        // wider buffer (floatBuf[i] = (float)fp4Buf[i]) has a Float32 target param so it
                        // is NOT in _subWordFloat4Params and falls through to the f32 single-element
                        // store below with the already-widened val - no re-narrowing.
                        AppendLine($"{output.VaryingName} = int(_f32_to_e2m1({val}));");
                        return;
                    }
                    if (_subWordQInt4Params.Contains(leaParamIdx))
                    {
                        // Packed 4-bit (QInt4) store: each thread writes ONE nibble, but 8 share a
                        // texel - the host readback would have to repack at 8-nibbles-per-texel (the
                        // FP4/byte path packs 4). That host-side density is not yet wired, so a store
                        // here would silently mis-pack. Fail loud (no silent garbage - WebGL rule).
                        // Reading packed QInt4 on WebGL IS supported (the nibble texelFetch+shift load).
                        throw new SpawnDev.ILGPU.UnsupportedKernelFeatureException(
                            feature: "packed QInt4 in-kernel store",
                            backend: global::ILGPU.Runtime.AcceleratorType.WebGL,
                            remediation: "Packed 4-bit (QInt4) stores need host-side 8-nibbles-per-texel repacking " +
                                "not yet wired on WebGL. Use WebGPU/CUDA/OpenCL for packed writes; reading a packed " +
                                "QInt4 view on WebGL is supported.");
                    }

                    // Single-element TF output (one value per vertex invocation)
                    // GLSL ES 3.0: cast value to match output type if needed
                    string tfValExpr = CastIfNeeded(val, output.GlslType ?? "float");
                    AppendLine($"{output.VaryingName} = {tfValExpr};");
                    return;
                }
            }

            // Cross-block pointer → resolve to TF output if available
            if (_crossBlockPointerExprs.TryGetValue(address.ToString(), out _))
            {
                // Cross-block pointers should also map to TF outputs via _leaParamMap
                // (propagated through PushPhiValues). Try one more time.
                if (_leaParamMap.TryGetValue(address.ToString(), out var cbParamIdx))
                {
                    _paramFallbackIndex!.TryGetValue(cbParamIdx, out var cbOutput);
                    if (cbOutput != null)
                    {
                        string cbValExpr = CastIfNeeded(val, cbOutput.GlslType ?? "float");
                        AppendLine($"{cbOutput.VaryingName} = {cbValExpr};");
                        return;
                    }
                }
                AppendLine($"// Store to cross-block pointer (no TF output found)");
                return;
            }

            // GLSL ES 3.0: cast value to match address type if needed
            string valExpr = CastIfNeeded(val, address.Type);
            AppendLine($"{address} = {valExpr};");
        }

        public override void GenerateCode(NewView value)
        {
            var target = Load(value);
            var source = Load(value.Pointer);
            // Propagate the param mapping if source is a param alias
            if (_leaParamMap.TryGetValue(source.ToString(), out var paramIdx))
                _leaParamMap[target.Name] = paramIdx;

            // When source is a local-memory array alloca (tracked via
            // _allocaArrayNames), forward the NewView target to the same backing array.
            // GLSL ES 3.0 doesn't allow array-value assignment (`int tmp2[N] = tmp1;` is
            // invalid), so we can't emit a true copy. Instead, subsequent `target[i]`
            // accesses must resolve to the source's backing array. Without this, the
            // NewView target was declared-without-declaration (just a comment) and later
            // references produced "undeclared identifier v_X" at GLSL compile time
            // - the symptom Tuvok's 2026-04-24 VP9 iDCT 8x8 report surfaced on WebGL.
            if (_allocaArrayNames.TryGetValue(source.Name, out var backingArray))
            {
                valueVariables[value] = source;
                _allocaArrayNames[target.Name] = backingArray;
                AppendLine($"// NewView: aliased to local alloca backing {backingArray} (no emit)");
                return;
            }

            AppendLine($"// NewView: {target} aliases {source}");
        }

        /// <summary>
        /// Handles ArrayView.Length: traces the view back to its kernel parameter
        /// and emits a reference to the u_param{N}_length uniform.
        /// This uniform is already declared by EmitParameterDeclarations and set at dispatch time.
        /// </summary>
        public override void GenerateCode(GetViewLength value)
        {
            var target = Load(value);

            // Trace the view back to its kernel parameter
            var param = ResolveToParameter(value.View);
            if (param != null && param.Index >= KernelParamOffset)
            {
                // GetViewLength returns long (Int64). In emulated i64 mode, the GLSL type is uvec2.
                // We must construct uvec2(uint(length), 0u) rather than assigning int directly.
                string glslType = TypeGenerator[value.Type];
                string lengthExpr;
                if (glslType == "uvec2")
                {
                    // Emulated i64: wrap the int length as uvec2(uint(len), 0u)
                    lengthExpr = $"uvec2(uint(u_param{param.Index}_length), 0u)";
                }
                else
                {
                    // Non-emulated path (shouldn't happen for long, but be safe)
                    lengthExpr = $"u_param{param.Index}_length";
                }
                EmitHoistedOrTypedAssignment(value, target, lengthExpr);
            }
            else
            {
                // Fallback: emit typed zero (should not happen for well-formed kernels)
                Declare(target);
                AppendLine($"{target} = {GetDefaultValue(TypeGenerator[value.Type])}; // GetViewLength: could not resolve view to parameter");
            }
        }

        /// <summary>
        /// SetField on a body-struct param — suppress the emit. ILGPU's IR sometimes
        /// produces field-by-field SetFields when a struct value flows through PHI
        /// or partial-update paths; for body structs we don't materialize a GLSL
        /// struct (its fields are accessed via per-field samplers / scalar uniforms),
        /// so emitting `v_0.field_N = ...` would reference an undeclared identifier.
        /// All real consumption of body-struct fields goes through GetField + LEA hooks.
        /// </summary>
        public override void GenerateCode(SetField value)
        {
            // Suppress writes targeting a body-struct param's value or a chain rooted at one.
            var rootParam = ResolveToParameter(value.ObjectValue);
            if (rootParam != null && _bodyStructParamsGL.ContainsKey(rootParam.Index))
            {
                AppendLine($"// SetField on body-struct param {rootParam.Index} field {value.FieldSpan.Index} (suppressed; per-field samplers handle access)");
                return;
            }
            base.GenerateCode(value);
        }

        public override void GenerateCode(GetField value)
        {
            if (_hoistedIndexFields.Contains(value)) return;

            // Body-struct field access redirect. Walk the GetField chain back to the
            // root parameter. If the chain ends at a body-struct param AND the
            // immediate-outermost GetField references a view field, suppress the
            // standard struct.field_N emit — the LEA hook handles real buffer access,
            // and intermediate GetFields (extracting BaseView / stride / length from
            // an ArrayView1D wrapper) are no-ops or trivial uniform references.
            if (TryFindBodyStructRoot(value, out var bsRootParam, out var rootFieldInfo, out int chainDepth))
            {
                int fi = value.FieldSpan.Index;
                var target = Load(value);
                if (rootFieldInfo.IsViewMetadata)
                {
                    // ArrayView wrapper metadata fields (Length, Dense flag). Emit a
                    // length-uniform reference for Int64 lengths or a 0 placeholder
                    // for the Dense flag. Important: must intercept here so the
                    // standard GenerateCode(GetField) doesn't fall through to a
                    // 2D-view stride emit and reference an undeclared uniform.
                    string typeName = TypeGenerator[value.Type];
                    string metadataExpr = typeName switch
                    {
                        "uvec2" => $"i64_from_i32({rootFieldInfo.BindingName}_length)",
                        "int" => $"{rootFieldInfo.BindingName}_length",
                        "uint" => $"{rootFieldInfo.BindingName}_length",
                        _ => $"{typeName}(0)"
                    };
                    EmitHoistedOrTypedAssignment(value, target, metadataExpr);
                    return;
                }
                if (rootFieldInfo.IsView)
                {
                    // The root field is a view (raw or wrapped). The kernel body's
                    // LEA hook recognizes the GetField chain via TryResolveBodyStructField.
                    // No GLSL emit needed for any link in the chain.
                    if (chainDepth == 1)
                    {
                        // Outer GetField: GetField(bodyParam, fi) returning the view (or wrapper)
                        AppendLine($"// body-struct view field {bsRootParam.Index}.{rootFieldInfo.ClrFieldName} (handled by LEA hook)");
                        return;
                    }
                    // Inner GetField step (e.g. ArrayView1D wrapper unwrap to BaseView,
                    // or further unwrap of BaseView to its underlying pointer / length).
                    // Emit either a no-op comment (BaseView pointer extraction — covered
                    // by LEA hook) or a length-uniform reference for the wrapper's
                    // outer Length field.
                    if (chainDepth == 2 && fi == 0)
                    {
                        AppendLine($"// body-struct view-chain step {bsRootParam.Index}.{rootFieldInfo.ClrFieldName} (handled by LEA hook)");
                        return;
                    }
                    if (chainDepth == 2 && fi == 1)
                    {
                        // ArrayView1D wrapper Length field
                        EmitHoistedOrTypedAssignment(value, target, $"{rootFieldInfo.BindingName}_length");
                        return;
                    }
                    // Anything else in the chain (stride extraction, BaseView's own
                    // sub-fields like Buffer/Index/Length) — emit a no-op. The actual
                    // buffer access bypasses these intermediate values via the LEA hook.
                    AppendLine($"// body-struct view-chain depth-{chainDepth} fi={fi} {bsRootParam.Index}.{rootFieldInfo.ClrFieldName} (no-op; LEA handles)");
                    return;
                }
                if (rootFieldInfo.IsScalar && chainDepth == 1)
                {
                    // Direct scalar field of body struct.
                    EmitHoistedOrTypedAssignment(value, target, rootFieldInfo.BindingName);
                    return;
                }
            }

            // Check if this is a kernel parameter (View) field access
            // Guard: when the object is a struct loaded from a struct buffer, we must use
            // standard field access. ResolveToParameter would incorrectly traverse through
            // the struct Load back to the View parameter. We detect this by checking if the
            // object's Load source traces back to a LEA param that has struct field counts.
            bool isLoadedStructFromBuffer = false;
            if (value.ObjectValue.Resolve() is global::ILGPU.IR.Values.Load objLoad
                && _leaParamMap.TryGetValue(Load(objLoad.Source).Name, out var objLeaParamIdx)
                && _structFieldCounts.ContainsKey(objLeaParamIdx))
            {
                isLoadedStructFromBuffer = true;
            }
            if (!isLoadedStructFromBuffer &&
                ResolveToParameter(value.ObjectValue) is global::ILGPU.IR.Values.Parameter param)
            {
                int paramOffset = KernelParamOffset;
                if (param.Index >= paramOffset)
                {
                    IsMultiDim(param.ParameterType, out var isMultiDim, out var isView, out var is1DView, out var is2DView, out var is3DView);
                    if (isView)
                    {
                        var target = Load(value);
                        string prefix = _hoistedPrimitives.Contains(value) ? "" : $"{TypeGenerator[value.Type]} ";

                        if (value.FieldSpan.Index == 0)
                        {
                            // Field 0: buffer pointer alias
                            _leaParamMap[target.Name] = param.Index;
                            AppendLine($"// {target} = buffer alias for param{param.Index}");
                            return;
                        }

                        // Buffer length via uniform
                        string totalLen = $"u_param{param.Index}_length";

                        if (is2DView)
                        {
                            // Stride fields: some may be Int64 (→ uvec2), some Int32 (→ int).
                            // Wrap with i64_from_i32() only when the target type is uvec2.
                            string glslType = TypeGenerator[value.Type];
                            bool needsI64 = glslType == "uvec2";
                            switch (value.FieldSpan.Index)
                            {
                                case 1:
                                    if (needsI64) AppendLine($"{prefix}{target} = i64_from_i32(u_param{param.Index}_stride[0]);");
                                    else AppendLine($"{prefix}{target} = u_param{param.Index}_stride[0];");
                                    return;
                                case 2:
                                    if (needsI64) AppendLine($"{prefix}{target} = i64_from_i32(u_param{param.Index}_stride[1]);");
                                    else AppendLine($"{prefix}{target} = u_param{param.Index}_stride[1];");
                                    return;
                                case 3:
                                    if (needsI64) AppendLine($"{prefix}{target} = i64_from_i32(u_param{param.Index}_stride[0]);");
                                    else AppendLine($"{prefix}{target} = u_param{param.Index}_stride[0];");
                                    return;
                            }
                        }
                        else if (is3DView)
                        {
                            // Same type-aware conversion as 2D
                            string glslType = TypeGenerator[value.Type];
                            bool needsI64 = glslType == "uvec2";
                            switch (value.FieldSpan.Index)
                            {
                                case 1:
                                    if (needsI64) AppendLine($"{prefix}{target} = i64_from_i32(u_param{param.Index}_stride[0]);");
                                    else AppendLine($"{prefix}{target} = u_param{param.Index}_stride[0];");
                                    return;
                                case 2:
                                    if (needsI64) AppendLine($"{prefix}{target} = i64_from_i32(u_param{param.Index}_stride[1]);");
                                    else AppendLine($"{prefix}{target} = u_param{param.Index}_stride[1];");
                                    return;
                                case 3:
                                    if (needsI64) AppendLine($"{prefix}{target} = i64_from_i32(u_param{param.Index}_stride[2]);");
                                    else AppendLine($"{prefix}{target} = u_param{param.Index}_stride[2];");
                                    return;
                                case 4:
                                    if (needsI64) AppendLine($"{prefix}{target} = i64_from_i32(u_param{param.Index}_stride[0]);");
                                    else AppendLine($"{prefix}{target} = u_param{param.Index}_stride[0];");
                                    return;
                                case 5:
                                    if (needsI64) AppendLine($"{prefix}{target} = i64_from_i32(u_param{param.Index}_stride[0] * u_param{param.Index}_stride[1]);");
                                    else AppendLine($"{prefix}{target} = u_param{param.Index}_stride[0] * u_param{param.Index}_stride[1];");
                                    return;
                            }
                        }
                        else if (is1DView)
                        {
                            // Flattened ArrayView1D<T, TStride> field layout depends on the
                            // base ArrayView<T> shape AND the stride. For Stride1D.Dense
                            // (empty struct) the layout is (Buffer, Length); for strided
                            // 1D views it can be (Buffer, Index, Length) or (Buffer,
                            // Length, Stride). The FieldSpan.Index alone does not
                            // distinguish — case 1 might be either Index (i32) or Length
                            // (i64). Type-drive the dispatch instead: Length is the only
                            // i64 leaf in any 1D view layout, so emulated-i64 (`uvec2`)
                            // type at case 1 OR case 2 means we want lenExpr1D. Plain int
                            // at the same indices is the Index/stride-offset slot.
                            string glslType1D = TypeGenerator[value.Type];
                            bool needsI64_1D = glslType1D == "uvec2";
                            string zeroExpr1D = needsI64_1D ? "uvec2(0u)" : "0";
                            string lenExpr1D = needsI64_1D ? $"uvec2(uint({totalLen}), 0u)" : totalLen;
                            switch (value.FieldSpan.Index)
                            {
                                case 1:
                                    if (needsI64_1D)
                                        AppendLine($"{prefix}{target} = {lenExpr1D};"); // Length (Dense layout: Buffer, Length)
                                    else
                                        AppendLine($"{prefix}{target} = {zeroExpr1D};"); // Index/stride offset (= 0 for non-SubView)
                                    return;
                                case 2: AppendLine($"{prefix}{target} = {lenExpr1D};"); return; // Length (post-Index layout)
                            }
                        }

                        {
                            string glslTypeFb = TypeGenerator[value.Type];
                            string lenExprFb = glslTypeFb == "uvec2" ? $"uvec2(uint({totalLen}), 0u)" : totalLen;
                            AppendLine($"{prefix}{target} = {lenExprFb};");
                        }
                        return;
                    }
                }

                // Kernel index parameter field access
                if (param.Index < paramOffset)
                {
                    var target = Load(value);
                    var source = Load(value.ObjectValue);
                    string prefix = _hoistedPrimitives.Contains(value) ? "" : "int ";

                    if (EntryPoint.IndexType == IndexType.Index2D)
                    {
                        string comp = value.FieldSpan.Index == 0 ? "x" : "y";
                        AppendLine($"{prefix}{target} = {source}.{comp};");
                        return;
                    }
                    else if (EntryPoint.IndexType == IndexType.Index3D)
                    {
                        string comp = value.FieldSpan.Index switch { 0 => "x", 1 => "y", _ => "z" };
                        AppendLine($"{prefix}{target} = {source}.{comp};");
                        return;
                    }
                    else if (EntryPoint.IndexType == IndexType.Index1D)
                    {
                        if (value.FieldSpan.Index == 0)
                            AppendLine($"{prefix}{target} = {source};");
                        else
                            AppendLine($"{prefix}{target} = 0;");
                        return;
                    }
                }
            }

            // Standard field access
            base.GenerateCode(value);
        }

        public override void GenerateCode(BinaryArithmeticValue value)
        {
            var target = Load(value);
            var left = Load(value.Left);
            var right = Load(value.Right);
            string prefix = _hoistedPrimitives.Contains(value) ? "" : $"{TypeGenerator[value.Type]} ";

            var leftType = TypeGenerator[value.Left.Type];
            var rightType = TypeGenerator[value.Right.Type];
            bool isEmulatedF64 = Backend.EnableF64Emulation && (leftType == "vec2" || rightType == "vec2" || (Backend.UseOzakiF64Emulation && (leftType == "vec4" || rightType == "vec4")));
            bool isEmulatedI64 = Backend.EnableI64Emulation && (leftType == "uvec2" || rightType == "uvec2");

            if (isEmulatedF64)
            {
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
                if (emulFunc != null) { AppendLine($"{prefix}{target} = {emulFunc}({left}, {right});"); return; }
            }

            if (isEmulatedI64)
            {
                string? emulFunc = value.Kind switch
                {
                    BinaryArithmeticKind.Add => "i64_add",
                    BinaryArithmeticKind.Sub => "i64_sub",
                    BinaryArithmeticKind.Mul => "i64_mul",
                    BinaryArithmeticKind.And => "i64_and",
                    BinaryArithmeticKind.Or => "i64_or",
                    BinaryArithmeticKind.Xor => "i64_xor",
                    BinaryArithmeticKind.Shl => "i64_shl",
                    BinaryArithmeticKind.Shr => "i64_shr",
                    _ => null
                };
                if (emulFunc != null)
                {
                    if (value.Kind == BinaryArithmeticKind.Shl || value.Kind == BinaryArithmeticKind.Shr)
                        AppendLine($"{prefix}{target} = {emulFunc}({left}, uint({right}));");
                    else
                        AppendLine($"{prefix}{target} = {emulFunc}({left}, {right});");
                    return;
                }
            }

            // Standard ops
            if (value.Kind == BinaryArithmeticKind.Min || value.Kind == BinaryArithmeticKind.Max)
            {
                string func = value.Kind == BinaryArithmeticKind.Min ? "min" : "max";
                AppendLine($"{prefix}{target} = {func}({left}, {right});");
                return;
            }

            if (value.Kind == BinaryArithmeticKind.PowF)
            {
                // GLSL ES 3.0: pow(x, y) is undefined for x < 0; ANGLE typically emits
                // it as exp(y * log(x)) and log(negative_x) is NaN. Mirror the base
                // GLSLCodeGenerator fix: when the exponent is a constant non-negative
                // integer (0..8), expand to repeated multiplication. Otherwise fall
                // through to native pow(). Surfaced 2026-05-04 by Data's WebGL
                // DistilBERT/SemanticSearch/GPT-2 LayerNorm `(x - mean)^2`.
                if (value.Right.Resolve() is PrimitiveValue pv)
                {
                    float pf = pv.BasicValueType == BasicValueType.Float32 || pv.BasicValueType == BasicValueType.Float16
                        ? pv.Float32Value
                        : (pv.BasicValueType == BasicValueType.Float64 ? (float)pv.Float64Value : float.NaN);
                    if (!float.IsNaN(pf) && pf >= 0f && pf <= 8f && pf == (int)pf)
                    {
                        int n = (int)pf;
                        if (n == 0) { AppendLine($"{prefix}{target} = 1.0;"); return; }
                        if (n == 1) { AppendLine($"{prefix}{target} = {left};"); return; }
                        var sb = new StringBuilder();
                        sb.Append(left);
                        for (int i = 1; i < n; i++) sb.Append(" * ").Append(left);
                        AppendLine($"{prefix}{target} = {sb};");
                        return;
                    }
                }
                // Runtime-safe pow for the case where the exponent isn't a literal
                // PrimitiveValue (e.g., loaded from an ONNX initializer ArrayView -
                // DistilBERT LayerNorm `(x - mean)^pow_const_2.0` per Data's
                // 2026-05-04 first-divergent capture). For x >= 0, native pow.
                // For x < 0, pow(abs(x), y) with sign correction for odd integer y.
                AppendLine($"{prefix}{target} = ({left} >= 0.0 ? pow({left}, {right}) : pow(abs({left}), {right}) * (mod({right}, 2.0) >= 1.0 ? -1.0 : 1.0));");
                return;
            }

            if (value.Kind == BinaryArithmeticKind.Atan2F)
            {
                AppendLine($"{prefix}{target} = atan({left}, {right});");
                return;
            }

            // CopySign: GLSL ES 3.0 has no copysign built-in.
            // Can't use abs(x)*sign(y) because sign(0)=0 which zeroes the result.
            // Use a ternary to handle the zero case correctly.
            if (value.Kind == BinaryArithmeticKind.CopySignF)
            {
                AppendLine($"{prefix}{target} = ({right} < 0.0) ? -abs({left}) : abs({left});");
                return;
            }

            // Float remainder — GLSL ES 3.0 does not support % for floats
            if (value.Kind == BinaryArithmeticKind.Rem && (leftType == "float" || leftType.StartsWith("float")))
            {
                AppendLine($"{prefix}{target} = {left} - {right} * floor({left} / {right});");
                return;
            }

            // Check if this is a boolean operation. GLSL requires logical operators
            // (&&, ||) for booleans, not bitwise (&, |).
            string op;
            var resultGlslType = TypeGenerator[value.Type];
            bool isBoolOp = resultGlslType == "bool"
                || TypeGenerator[value.Left.Type] == "bool"
                || booleanVariables.Contains(target.Name);
            if (isBoolOp && value.Kind == BinaryArithmeticKind.And)
                op = "&&";
            else if (isBoolOp && value.Kind == BinaryArithmeticKind.Or)
                op = "||";
            else
                op = GetArithmeticOp(value.Kind);

            // GLSL ES 3.0: shift-left of a signed int where the result would set
            // the sign bit is UNDEFINED behavior — Chrome ANGLE produces
            // 0x80000001 instead of 0x80000000 for `0x800000 << 8`. ILGPU's IL
            // `shl` opcode is signedness-agnostic and has no `.un` variant, so
            // the IR can't carry a per-shift IsUnsigned flag. Fix unconditionally:
            // emit `int(uint(left) << uint(right))` for integer shifts so the GLSL
            // compiler uses the well-defined unsigned-shift semantics. Bit pattern
            // is identical to signed shift for every non-overflowing case
            // (verified in tests). Surfaced 2026-05-04 by Tests23_BareUintShift.
            if ((value.Kind == BinaryArithmeticKind.Shl || value.Kind == BinaryArithmeticKind.Shr)
                && (leftType == "int" || leftType == "uint")
                && (rightType == "int" || rightType == "uint"))
            {
                // For Shr, only switch to unsigned when the IR flagged it (`.un` IL).
                // Signed right-shift is well-defined and we must preserve sign extension.
                bool useUnsigned = value.Kind == BinaryArithmeticKind.Shl || value.IsUnsigned;
                if (useUnsigned)
                {
                    AppendLine($"{prefix}{target} = {resultGlslType}(uint({left}) {op} uint({right}));");
                    return;
                }
            }

            // Sister fix to the Shl/Shr unsigned cast. GLSL ES 3.0 `int / int` and
            // `int % int` are signed div/rem; for high-bit-set i32-as-uint operands
            // the result is wrong. Tuvok's `OpusRangeDecoderGpu_DecodeUint` on WebGL
            // 2026-05-04: `0x80000000u / 6u` returned `0xEAAAAAAB` instead of
            // `0x15555555`. Cast through uint when the IR flagged the op as unsigned.
            // Bit pattern preserved on cast back to int (or stays as uint).
            if ((value.Kind == BinaryArithmeticKind.Div || value.Kind == BinaryArithmeticKind.Rem)
                && value.IsUnsigned
                && (leftType == "int" || leftType == "uint")
                && (rightType == "int" || rightType == "uint"))
            {
                AppendLine($"{prefix}{target} = {resultGlslType}(uint({left}) {op} uint({right}));");
                return;
            }

            AppendLine($"{prefix}{target} = {left} {op} {right};");
        }

        public override void GenerateCode(UnaryArithmeticValue value)
        {
            var target = Load(value);
            var operand = Load(value.Value);
            var operandType = TypeGenerator[value.Value.Type];
            string prefix = _hoistedPrimitives.Contains(value) ? "" : $"{TypeGenerator[value.Type]} ";

            bool isEmulatedF64 = Backend.EnableF64Emulation && (operandType == "vec2" || (Backend.UseOzakiF64Emulation && operandType == "vec4"));
            bool isEmulatedI64 = Backend.EnableI64Emulation && operandType == "uvec2";

            if (isEmulatedF64)
            {
                string? emulFunc = value.Kind switch
                {
                    UnaryArithmeticKind.Neg => "f64_neg",
                    UnaryArithmeticKind.Abs => "f64_abs",
                    _ => null
                };
                if (emulFunc != null) { AppendLine($"{prefix}{target} = {emulFunc}({operand});"); return; }
            }

            if (isEmulatedI64)
            {
                string? emulFunc = value.Kind switch
                {
                    UnaryArithmeticKind.Neg => "i64_neg",
                    UnaryArithmeticKind.Abs => "i64_abs",
                    _ => null
                };
                if (emulFunc != null) { AppendLine($"{prefix}{target} = {emulFunc}({operand});"); return; }
            }

            // Fall back to base class for non-emulated types
            base.GenerateCode(value);
        }

        public override void GenerateCode(TernaryArithmeticValue value)
        {
            var target = Load(value);
            var first = Load(value.First);
            var second = Load(value.Second);
            var third = Load(value.Third);
            string prefix = _hoistedPrimitives.Contains(value) ? "" : $"{TypeGenerator[value.Type]} ";

            var firstType = TypeGenerator[value.First.Type];
            bool isEmulatedF64 = Backend.EnableF64Emulation && (firstType == "vec2" || (Backend.UseOzakiF64Emulation && firstType == "vec4"));

            if (isEmulatedF64)
            {
                // FMA: a*b+c using double-float emulation
                AppendLine($"{prefix}{target} = f64_add(f64_mul({first}, {second}), {third});");
                return;
            }

            // Fall back: GLSL ES 3.0 has no fma() — emulate with a*b+c
            AppendLine($"{prefix}{target} = ({first} * {second} + {third});");
        }

        public override void GenerateCode(CompareValue value)
        {
            var target = Load(value);
            var left = Load(value.Left);
            var right = Load(value.Right);
            string prefix = _hoistedPrimitives.Contains(value) ? "" : "bool ";

            var leftType = TypeGenerator[value.Left.Type];
            bool isEmulatedF64 = Backend.EnableF64Emulation && (leftType == "vec2" || (Backend.UseOzakiF64Emulation && leftType == "vec4"));
            bool isEmulatedI64 = Backend.EnableI64Emulation && leftType == "uvec2";

            if (isEmulatedF64)
            {
                string? f = value.Kind switch
                {
                    CompareKind.LessThan => "f64_lt", CompareKind.LessEqual => "f64_le",
                    CompareKind.GreaterThan => "f64_gt", CompareKind.GreaterEqual => "f64_ge",
                    CompareKind.Equal => "f64_eq", CompareKind.NotEqual => "f64_ne", _ => null
                };
                if (f != null)
                {
                    // IEEE 754 unordered compare: NaN forces TRUE except NotEqual.
                    // ILGPU's IR negates `clt+brfalse` to `cge+brtrue [Unordered]`;
                    // an ordered cge for NaN is FALSE so the bit gets set.
                    if (value.IsUnsignedOrUnordered && value.Kind != CompareKind.NotEqual)
                    {
                        AppendLine(
                            $"{prefix}{target} = (_f32_is_nan_bits({left}.x) || _f32_is_nan_bits({right}.x)) || {f}({left}, {right});");
                    }
                    else
                    {
                        AppendLine($"{prefix}{target} = {f}({left}, {right});");
                    }
                    return;
                }
            }

            if (isEmulatedI64)
            {
                string? f = value.Kind switch
                {
                    CompareKind.LessThan => "i64_lt", CompareKind.LessEqual => "i64_le",
                    CompareKind.GreaterThan => "i64_gt", CompareKind.GreaterEqual => "i64_ge",
                    CompareKind.Equal => "i64_eq", CompareKind.NotEqual => "i64_ne", _ => null
                };
                if (f != null) { AppendLine($"{prefix}{target} = {f}({left}, {right});"); return; }
            }

            string op = GetCompareOp(value.Kind);

            bool isFloatScalar = (value.Left.BasicValueType == BasicValueType.Float32
                || value.Left.BasicValueType == BasicValueType.Float16);

            // Two cases for f32 NaN safety - mirror of WGSL fix:
            //   1. Unordered LT/LE/GT/GE: emit `is_nan(l) || is_nan(r) || (l op r)`.
            //   2. Equal/NotEqual: drivers may compare NaN bit patterns as equal.
            //      Always apply NaN guard for IEEE-strict semantics.
            // Diagnosed against FloatNaNComparisonTest 2026-04-29.
            bool isNativeFloatUnordered = isFloatScalar && value.IsUnsignedOrUnordered
                && value.Kind != CompareKind.NotEqual
                && value.Kind != CompareKind.Equal;
            bool isNativeFloatEqualLike = isFloatScalar
                && (value.Kind == CompareKind.Equal || value.Kind == CompareKind.NotEqual);

            if (isNativeFloatUnordered)
            {
                // Inline the bit-pattern NaN check (exponent all 1s AND mantissa
                // nonzero) so we don't depend on _f32_is_nan_bits being included
                // — that helper is only emitted with the f64 emulation library.
                string LIsNaN = $"((floatBitsToUint({left}) & 0x7F800000u) == 0x7F800000u && (floatBitsToUint({left}) & 0x007FFFFFu) != 0u)";
                string RIsNaN = $"((floatBitsToUint({right}) & 0x7F800000u) == 0x7F800000u && (floatBitsToUint({right}) & 0x007FFFFFu) != 0u)";
                AppendLine($"{prefix}{target} = ({LIsNaN} || {RIsNaN} || ({left} {op} {right}));");
            }
            else if (isNativeFloatEqualLike)
            {
                string LIsNaN = $"((floatBitsToUint({left}) & 0x7F800000u) == 0x7F800000u && (floatBitsToUint({left}) & 0x007FFFFFu) != 0u)";
                string RIsNaN = $"((floatBitsToUint({right}) & 0x7F800000u) == 0x7F800000u && (floatBitsToUint({right}) & 0x007FFFFFu) != 0u)";
                if (value.Kind == CompareKind.Equal)
                    AppendLine($"{prefix}{target} = (!({LIsNaN}) && !({RIsNaN}) && ({left} == {right}));");
                else
                    AppendLine($"{prefix}{target} = ({LIsNaN} || {RIsNaN} || ({left} != {right}));");
            }
            else
            {
                // For unsigned integer comparisons (`uint <= uintConst` etc.),
                // cast operands to uint so GLSL ES 3.0 uses unsigned semantics.
                // ILGPU's IR represents both signed and unsigned ints as
                // BasicValueType.Int32 with `IsUnsignedOrUnordered` on the
                // CompareValue node; the GLSLTypeGenerator maps Int32 → "int"
                // by default. Without this cast, `state.Rng <= 0x800000u`
                // evaluates as signed and Tuvok's libopus Normalize loop ran
                // to its safety cap on WebGL (rng=0x80000000 signed = -2147483648
                // <= 0x800000). Surfaced 2026-05-04 by Tests23.UintCompareInLoop.
                string rightType = TypeGenerator[value.Right.Type];
                bool isIntegerType = (leftType == "int" || leftType == "uint")
                    && (rightType == "int" || rightType == "uint");
                if (value.IsUnsignedOrUnordered && isIntegerType
                    && value.Kind != CompareKind.Equal && value.Kind != CompareKind.NotEqual)
                {
                    AppendLine($"{prefix}{target} = uint({left}) {op} uint({right});");
                }
                else
                {
                    AppendLine($"{prefix}{target} = {left} {op} {right};");
                }
            }
        }



        public override void GenerateCode(ConvertValue value)
        {
            var target = Load(value);
            var source = Load(value.Value);
            var targetType = TypeGenerator[value.Type];
            var sourceType = TypeGenerator[value.Value.Type];
            string prefix = _hoistedPrimitives.Contains(value) ? "" : $"{targetType} ";

            bool isEmulatedF64Target = Backend.EnableF64Emulation && (targetType == "vec2" || (Backend.UseOzakiF64Emulation && targetType == "vec4"));
            bool isEmulatedI64Target = Backend.EnableI64Emulation && targetType == "uvec2";
            bool isEmulatedF64Source = Backend.EnableF64Emulation && (sourceType == "vec2" || (Backend.UseOzakiF64Emulation && sourceType == "vec4"));
            bool isEmulatedI64Source = Backend.EnableI64Emulation && sourceType == "uvec2";

            // Detect unsigned source conversion (e.g. uint → float)
            bool isSourceUnsigned = (value.Flags & ConvertFlags.SourceUnsigned) == ConvertFlags.SourceUnsigned;

            if (isEmulatedF64Target)
            {
                if (isEmulatedF64Source) AppendLine($"{prefix}{target} = {source};");
                else if (isEmulatedI64Source) AppendLine($"{prefix}{target} = f64_from_f32(float(i64_to_i32({source})));");
                else if (sourceType == "float") AppendLine($"{prefix}{target} = f64_from_f32({source});");
                else if (isSourceUnsigned && sourceType == "int") AppendLine($"{prefix}{target} = f64_from_f32(float(uint({source})));");
                else AppendLine($"{prefix}{target} = f64_from_f32(float({source}));");
                return;
            }
            if (isEmulatedI64Target)
            {
                if (isEmulatedI64Source) AppendLine($"{prefix}{target} = {source};");
                else if (isEmulatedF64Source) AppendLine($"{prefix}{target} = i64_from_i32(int(f64_to_f32({source})));");
                else if (sourceType == "int") AppendLine($"{prefix}{target} = i64_from_i32({source});");
                else if (sourceType == "uint") AppendLine($"{prefix}{target} = u64_from_u32({source});");
                else AppendLine($"{prefix}{target} = i64_from_i32(int({source}));");
                return;
            }
            if (isEmulatedF64Source)
            {
                if (targetType == "float") AppendLine($"{prefix}{target} = f64_to_f32({source});");
                else AppendLine($"{prefix}{target} = {targetType}(f64_to_f32({source}));");
                return;
            }
            if (isEmulatedI64Source)
            {
                if (targetType == "int") AppendLine($"{prefix}{target} = i64_to_i32({source});");
                else if (targetType == "uint") AppendLine($"{prefix}{target} = u64_to_u32({source});");
                else if (targetType == "float") AppendLine($"{prefix}{target} = float(i64_to_i32({source}));");
                else AppendLine($"{prefix}{target} = {targetType}(i64_to_i32({source}));");
                return;
            }

            // Unsigned int → float: must cast through uint to preserve unsigned value
            if (isSourceUnsigned && sourceType == "int" && targetType == "float")
            {
                AppendLine($"{prefix}{target} = float(uint({source}));");
                return;
            }

            // Sub-word narrowing for Int16 / Int8 targets baked into the cast
            // expression - same pattern as base GLSL + WGSL + Wasm fixes.
            // Combine into one statement so the declaration prefix
            // (`int v_X = ...`) wraps the narrowed value cleanly without a
            // re-declaration.
            string castExpr = $"{targetType}({source})";
            if (targetType == "int")
            {
                bool isTargetUnsigned = (value.Flags & ConvertFlags.TargetUnsigned) == ConvertFlags.TargetUnsigned;
                var dstBasicType = value.Type.BasicValueType;
                if (dstBasicType == BasicValueType.Int16)
                    castExpr = isTargetUnsigned ? $"({castExpr} & 0xFFFF)" : SignExtend16(castExpr);
                else if (dstBasicType == BasicValueType.Int8)
                    castExpr = isTargetUnsigned ? $"({castExpr} & 0xFF)" : SignExtend8(castExpr);
                else
                {
                    // Widening from a SUB-WORD source (Int16/Int8) to a wider int (Int32). The
                    // source may be zero-extended - from an unsigned sub-word load, or a
                    // `(short)`/`(sbyte)` signedness reinterpret the core IR ELIDES (short and
                    // ushort share BasicValueType.Int16). C# sign-extends short->int and
                    // zero-extends ushort->int; SourceUnsigned carries which. Re-extend the low
                    // bits so the high bits are correct - PopArithmeticArgs promotes the `(short)`
                    // operand of `(short)Interop.FloatAsInt(half) >> 15` (AscendingHalf's
                    // ones-complement mask) to Int32 via THIS convert before the shift; without it
                    // `>> 15` saw a zero-extended value and returned 0 instead of 0xFFFF for
                    // negative Halves. Idempotent for already-extended values; desktop unaffected.
                    var srcBasicType = value.Value.BasicValueType;
                    if (srcBasicType == BasicValueType.Int16)
                        castExpr = isSourceUnsigned ? $"({castExpr} & 0xFFFF)" : SignExtend16(castExpr);
                    else if (srcBasicType == BasicValueType.Int8)
                        castExpr = isSourceUnsigned ? $"({castExpr} & 0xFF)" : SignExtend8(castExpr);
                }
            }
            EmitHoistedOrTypedAssignment(value, target, castExpr);
        }

        #endregion

        #region Helpers

        private static string GetArithmeticOp(BinaryArithmeticKind kind) => kind switch
        {
            BinaryArithmeticKind.Add => "+",
            BinaryArithmeticKind.Sub => "-",
            BinaryArithmeticKind.Mul => "*",
            BinaryArithmeticKind.Div => "/",
            BinaryArithmeticKind.Rem => "%",
            BinaryArithmeticKind.And => "&",
            BinaryArithmeticKind.Or => "|",
            BinaryArithmeticKind.Xor => "^",
            BinaryArithmeticKind.Shl => "<<",
            BinaryArithmeticKind.Shr => ">>",
            _ => "+"
        };

        private static string GetCompareOp(CompareKind kind) => kind switch
        {
            CompareKind.Equal => "==",
            CompareKind.NotEqual => "!=",
            CompareKind.LessThan => "<",
            CompareKind.LessEqual => "<=",
            CompareKind.GreaterThan => ">",
            CompareKind.GreaterEqual => ">=",
            _ => "=="
        };

        private static void IsMultiDim(TypeNode type, out bool isMultiDim, out bool isView,
            out bool is1DView, out bool is2DView, out bool is3DView)
        {
            is1DView = false;
            is2DView = false;
            is3DView = false;
            isMultiDim = false;
            isView = false;

            // 1. Direct type check: ViewType is a 1D view
            if (type is ViewType)
            {
                isView = true;
                is1DView = true;
                return;
            }

            string typeName = type.ToString();

            // 2. Structural check: ArrayView2D/3D are StructureType wrappers around a ViewType
            //    Port from the WGSL backend's proven detection logic
            //
            // 2026-05-04 fix: ONLY set `isView=true` when the field count matches one of
            // the known ArrayView shapes (3/4/6 flattened fields). For multi-view container
            // structs (e.g., ManyIntViewsStruct with 12 ArrayView<int>, Tuvok's
            // VorbisPacketDecodeStaticInputs with 38), NumFields exceeds those and the
            // struct is NOT a single view — the dispatcher must treat it as a scalar
            // and serialize each view-ptr to its own field offset (same as the Wasm
            // IsViewType fix). Pre-fix, isView=true unconditionally on any struct with
            // a leading view-typed field, which made the WebGL dispatcher route
            // multi-view containers through the single-view path and lose 11 of 12
            // buffer registrations.
            if (type is StructureType st)
            {
                // Check if first flattened field is a ViewType or its string contains "View"
                if (st.NumFields > 0 && (st.Fields[0] is ViewType || st.Fields[0] is PointerType || st.Fields[0].ToString().Contains("View")))
                {
                    // Distinguish 1D/2D/3D by flattened field count:
                    //   1D ArrayView<T> = 3 fields (pointer, index, length) but 1D is ViewType not StructureType
                    //   2D ArrayView2D<T> = 4 fields (pointer, index, length, stride)
                    //   3D ArrayView3D<T> = 6 fields (pointer, index, length, strideX, strideY, strideZ)
                    if (st.NumFields == 6)
                    {
                        if (!IsTensorViewLikeBodyStruct(st))
                        {
                            isView = true;
                            is3DView = true;
                            isMultiDim = true;
                        }
                    }
                    else if (st.NumFields == 4)
                    {
                        isView = true;
                        is2DView = true;
                        isMultiDim = true;
                    }
                    else if (st.NumFields == 3)
                    {
                        isView = true;
                        is1DView = true;
                        isMultiDim = false;
                    }
                    // NumFields outside {3, 4, 6}: NOT a single-view wrapper —
                    // multi-view container struct. isView stays false; the
                    // dispatcher routes it through the scalar-struct path.
                }
            }

            // 3. Fallback: string check (covers any edge cases)
            if (!isView && typeName.Contains("ArrayView"))
            {
                isView = true;
                if (typeName.Contains("2D") || typeName.Contains("3D")) isMultiDim = true;
                if (typeName.Contains("3D")) is3DView = true;
                else if (typeName.Contains("2D")) is2DView = true;
                else is1DView = true;
            }
        }

        private global::ILGPU.IR.Values.Parameter? ResolveToParameter(Value value)
        {
            if (value is global::ILGPU.IR.Values.Parameter p)
            {
                if (_inlineParamAliasesGL.TryGetValue(p, out var aliased)
                    && aliased.Resolve() is global::ILGPU.IR.Values.Parameter kernelParam)
                {
                    return kernelParam;
                }

                return p;
            }

            if (value is GetField gf) return ResolveToParameter(gf.ObjectValue);
            if (value is global::ILGPU.IR.Values.Load load) return ResolveToParameter(load.Source);
            if (value is LoadElementAddress lea) return ResolveToParameter(lea.Source);
            if (value is LoadFieldAddress lfa) return ResolveToParameter(lfa.Source);
            // NewView creates a view from a pointer — trace through to the source.
            // Without this, `view.Length` on an ArrayView1D parameter fails to resolve
            // to its kernel parameter (since BaseView access lowers through NewView)
            // and the GLSL codegen falls back to emitting 0 for the length, which
            // silently breaks every runtime bounds check (`idx < view.Length`) — the
            // gather/scatter/anything-with-bounds-check then returns the typed default.
            if (value is NewView nv) return ResolveToParameter(nv.Pointer);
            // AddressSpaceCast changes address space — trace through.
            if (value is AddressSpaceCast asc) return ResolveToParameter(asc.Value);
            // SubViewValue creates a sub-range — trace the source view.
            if (value is SubViewValue sv) return ResolveToParameter(sv.Source);
            // ConvertValue (pointer/view casts) — trace through.
            if (value is ConvertValue cv) return ResolveToParameter(cv.Value);
            if (value is PhiValue phi)
            {
                for (int i = 0; i < phi.Count; i++)
                {
                    var result = ResolveToParameter(phi[i]);
                    if (result != null) return result;
                }
            }
            return null;
        }

        #endregion

        #region Grid/Group Index Support

        // WebGL has no native workgroups — we emulate using uniforms set at dispatch time.
        // u_groupDimX = threads per workgroup (X), u_gridDimX = number of workgroups (X/Y)
        // linearGridIdx = gl_VertexID / u_groupDimX
        // Grid.IdxX = linearGridIdx % u_gridDimX
        // Grid.IdxY = linearGridIdx / u_gridDimX
        // Group.IdxX = gl_VertexID % u_groupDimX

        public override void GenerateCode(GridIndexValue value)
        {
            var target = Load(value);
            Declare(target);
            if (value.Dimension == DeviceConstantDimension3D.X)
                AppendLine($"{target} = (gl_VertexID / u_groupDimX) % u_gridDimX;");
            else if (value.Dimension == DeviceConstantDimension3D.Y)
                AppendLine($"{target} = (gl_VertexID / u_groupDimX) / u_gridDimX;");
            else
                AppendLine($"{target} = ((gl_VertexID / u_groupDimX) / u_gridDimX) / u_gridDimY;");
        }

        public override void GenerateCode(GroupIndexValue value)
        {
            var target = Load(value);
            Declare(target);
            if (value.Dimension == DeviceConstantDimension3D.X)
                AppendLine($"{target} = gl_VertexID % u_groupDimX;");
            else
                AppendLine($"{target} = 0; // WebGL only supports 1D groups");
        }

        public override void GenerateCode(GroupDimensionValue value)
        {
            var target = Load(value);
            Declare(target);
            if (value.Dimension == DeviceConstantDimension3D.X)
                AppendLine($"{target} = u_groupDimX;");
            else
                AppendLine($"{target} = 1; // WebGL only supports 1D groups");
        }

        public override void GenerateCode(GridDimensionValue value)
        {
            var target = Load(value);
            Declare(target);
            if (value.Dimension == DeviceConstantDimension3D.X)
                AppendLine($"{target} = u_gridDimX;");
            else if (value.Dimension == DeviceConstantDimension3D.Y)
                AppendLine($"{target} = u_gridDimY;");
            else
                AppendLine($"{target} = 1;");
        }

        #endregion
    }

    #region Supporting Types

    public enum KernelParamKind
    {
        Buffer,
        Scalar,
        Stride,
        EmulatedF64,
        EmulatedI64
    }

    /// <summary>
    /// Per-field metadata for a body-struct kernel parameter. Produced by the
    /// codegen pass for every ArrayView field of a body struct; consumed by
    /// WebGLAccelerator at dispatch time to decompose the user's body-struct arg
    /// into per-field buffer_ref dispatch entries.
    /// </summary>
    public class BodyStructBindingEntry
    {
        /// <summary>The IR parameter index of the body struct itself (e.g. 1).</summary>
        public int ParamIndex { get; set; }
        /// <summary>The IR field index within the body-struct type. May not match the
        /// C# field index when the IR flattens ArrayView1D wrappers into multiple
        /// IR fields. Use ClrFieldIndex for host-side reflection lookup.</summary>
        public int FieldIndex { get; set; }
        /// <summary>The C# field index within the user struct (in declaration order).
        /// Used by host-side WebGLAccelerator to walk reflection.GetFields() in order.</summary>
        public int ClrFieldIndex { get; set; }
        /// <summary>The synthetic param index = (ParamIndex + 1) * 1000 + FieldIndex.</summary>
        public int SyntheticParamIndex { get; set; }
        /// <summary>The GLSL sampler uniform name (e.g. "u_param2_f0") when IsView.</summary>
        public string BindingName { get; set; } = "";
        /// <summary>The GLSL element type (int / uint / float).</summary>
        public string GlslElementType { get; set; } = "int";
        /// <summary>True when this field is an ArrayView (gets its own sampler binding).</summary>
        public bool IsView { get; set; }
        /// <summary>True when this field is a primitive scalar (passed via uniform).</summary>
        public bool IsScalar { get; set; }
        /// <summary>1 for byte, 2 for short/Half, 0 for full-word.</summary>
        public int SubWordElemSize { get; set; }
        /// <summary>True when the sub-word field is unsigned (byte/ushort).</summary>
        public bool IsUnsignedSubWord { get; set; }
        /// <summary>True when the sub-word field is Float16-emulated.</summary>
        public bool IsFloat16 { get; set; }
        /// <summary>True when the sub-word field is bfloat16-emulated.</summary>
        public bool IsBFloat16 { get; set; }
        /// <summary>True when the kernel writes to this field (needs TF varying).</summary>
        public bool IsOutputBuffer { get; set; }
        /// <summary>The C# field name from the user's body struct (debug clarity).</summary>
        public string ClrFieldName { get; set; } = "";
    }

    public readonly struct KernelParameterBinding
    {
        public int ParamIndex { get; }
        public int BindingIndex { get; }
        public KernelParamKind Kind { get; }
        public string GlslType { get; }
        /// <summary>For struct element buffers: number of flattened scalar fields per element. 0 for non-struct.</summary>
        public int StructFieldCount { get; }

        public KernelParameterBinding(int paramIndex, int bindingIndex, KernelParamKind kind, string glslType, int structFieldCount = 0)
        {
            ParamIndex = paramIndex;
            BindingIndex = bindingIndex;
            Kind = kind;
            GlslType = glslType;
            StructFieldCount = structFieldCount;
        }
    }

    public class OutputVaryingInfo
    {
        public int ParamIndex { get; }
        public int OutputIndex { get; }
        public string VaryingName { get; }
        public string GlslType { get; }
        /// <summary>True if this varying is one half of an emulated 64-bit value (lo or hi).</summary>
        public bool IsEmulated { get; }
        /// <summary>For emulated varyings: "lo" or "hi". Null for normal varyings.</summary>
        public string? EmulatedSuffix { get; }
        /// <summary>For struct field varyings: the field index within the struct. -1 for non-struct.</summary>
        public int FieldIndex { get; }

        /// <summary>For multi-store buffers: the slot index (0-based). -1 for single-store.</summary>
        public int StoreSlot { get; }
        /// <summary>For multi-store buffers: total number of stores per vertex. 1 for single-store.</summary>
        public int StoreCount { get; }
        /// <summary>
        /// True when this varying emits an atomic vote (increment amount per thread).
        /// The JS worker sums all per-vertex values and adds the total to the buffer element,
        /// instead of writing each vertex's value to its own sequential slot.
        /// </summary>
        public bool IsAtomicVote { get; }

        public OutputVaryingInfo(int paramIndex, int outputIndex, string varyingName, string glslType,
            bool isEmulated = false, string? emulatedSuffix = null, int fieldIndex = -1,
            int storeSlot = -1, int storeCount = 1, bool isAtomicVote = false)
        {
            ParamIndex = paramIndex;
            OutputIndex = outputIndex;
            VaryingName = varyingName;
            GlslType = glslType;
            IsEmulated = isEmulated;
            EmulatedSuffix = emulatedSuffix;
            FieldIndex = fieldIndex;
            StoreSlot = storeSlot;
            StoreCount = storeCount;
            IsAtomicVote = isAtomicVote;
        }
    }

    #endregion
}
