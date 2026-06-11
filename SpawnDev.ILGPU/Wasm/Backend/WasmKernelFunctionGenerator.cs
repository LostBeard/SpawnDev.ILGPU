// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmKernelFunctionGenerator.cs
//
// Generates the kernel entry-point function in WebAssembly binary format.
// Handles parameter binding, index computation, memory layout, and
// the main multi-block state machine for kernel execution.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Analyses.ControlFlowDirection;
using global::ILGPU.IR.Analyses.TraversalOrders;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;
using System.Text;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>
    /// Generates the Wasm kernel function.
    ///
    /// Function signature (1D): kernel(globalIdx, dimX, param0_offset, param0_len, param1_value, ...)
    ///
    /// The kernel imports a shared WebAssembly.Memory. Buffer parameters are passed as
    /// byte offsets into this linear memory. Scalar parameters are passed directly.
    ///
    /// Memory layout:
    ///   [0..sync_region] [buffer0_offset..] [buffer1_offset..] [scalar_area..]
    ///   Offsets are computed by the accelerator at dispatch time.
    /// </summary>
    public class WasmKernelFunctionGenerator : WasmCodeGenerator
    {
        /// <summary>
        /// Wasm function parameter types (built during Setup).
        /// </summary>
        public readonly List<byte> FuncParamTypes = new();

        /// <summary>
        /// Maps ILGPU parameter index to its Wasm local indices.
        /// For ArrayView: [offset_local, length_local]
        /// For scalars: [value_local]
        /// </summary>
        private readonly Dictionary<int, uint[]> _paramLocals = new();

        /// <summary>
        /// For view parameters, maps ILGPU param index to the first stride field index in the IR struct.
        /// Strides are always the trailing Int32 fields after the pointer + Int64 extent fields.
        /// </summary>
        private readonly Dictionary<int, int> _viewStrideStartField = new();

        /// <summary>
        /// When single-block helpers are inlined, maps the helper's <see cref="Parameter"/>
        /// nodes to the call-site argument values so <see cref="TraceToParameter"/> can
        /// resolve struct fields (e.g. TensorView.Data) back to the kernel parameter.
        /// </summary>
        private readonly Dictionary<global::ILGPU.IR.Values.Parameter, Value> _inlineParamAliases = new();

        /// <summary>Per body-struct param: IR field metadata (populated by <see cref="ScanBodyStructParams"/>).</summary>
        private readonly Dictionary<int, List<BodyStructFieldInfoWasm>> _bodyStructFields = new();

        /// <summary>Expanded flat view args for body-struct embedded views: (IR param index, IR view field index) → locals.</summary>
        private readonly Dictionary<(int paramIndex, int viewFieldIndex), uint[]> _bodyStructViewLocals = new();

        /// <summary>
        /// The parameter info for marshaling (written to GeneratorArgs).
        /// </summary>
        private readonly List<WasmParamInfo> _paramInfos = new();

        /// <summary>
        /// Wasm local index for the global index.
        /// </summary>
        private uint _globalIdxLocal;

        /// <summary>
        /// Wasm local index for dimension X.
        /// </summary>
        private uint _dimXLocal;

        /// <summary>
        /// Wasm local index for dimension Y (used for 2D/3D kernels).
        /// </summary>
        private uint _dimYLocal;

        /// <summary>
        /// Wasm local index for scratch memory base address.
        /// Used by StructureValue for temporary struct construction in linear memory.
        /// </summary>
        private uint _scratchBaseLocal;

        /// <summary>
        /// Tracks the next available byte offset within the scratch memory area.
        /// Reset for each kernel compilation.
        /// </summary>
        private int _scratchNextOffset = 0;

        /// <summary>
        /// Tracks scratch offsets for struct Load copies. Each unique Load IR node
        /// gets its own scratch slot for snapshot semantics (SSA-keyed). The offset
        /// is safe because it's allocated AFTER phase state + helper scratch.
        /// </summary>
        private readonly Dictionary<string, int> _structLoadSlots = new();

        /// <summary>
        /// Counter for struct-to-array Store yields emitted during code generation.
        /// Must match the pre-count for correct br_table indexing.
        /// </summary>
        private int _structStoreYieldsEmitted = 0;

        /// <summary>
        /// The index type of the kernel (Index1D, Index2D, Index3D).
        /// </summary>
        private IndexType _indexType = IndexType.Index1D;

        /// <summary>
        /// The IR parameter that represents the implicit index (for 2D/3D decomposition).
        /// </summary>
        private global::ILGPU.IR.Values.Parameter? _indexParam;

        /// <summary>
        /// Whether this kernel uses views (ArrayView).
        /// </summary>
        private bool _hasViews = false;

        /// <summary>
        /// Maps BasicBlock to its index in the state machine.
        /// </summary>
        private readonly Dictionary<BasicBlock, int> _blockMap = new();

        /// <summary>
        /// State local for the state machine (_block variable).
        /// </summary>
        private uint _stateLocal;
        private uint _spillVerifyAddr, _spillVerifyVal32, _spillVerifyVal64;
        private uint _yieldedLocal;

        /// <summary>
        /// Whether the state machine is active (multi-block kernel).
        /// </summary>
        private bool _isStateMachine = false;

        /// <summary>
        /// Number of blocks in the state machine (used for br_table nesting depth).
        /// </summary>
        private int _blockCount = 0;

        /// <summary>
        /// Index of the block currently being emitted (0-indexed). Used for br depth calculation.
        /// </summary>
        private int _currentBlockEmitIndex = 0;

        // === Group execution params (for shared memory + barrier support) ===

        /// <summary>
        /// Wasm local index for group SIZE (total threads in workgroup = GroupDim.X*Y*Z).
        /// NOTE: despite the name, this carries the total group size, not GroupDim.X. The
        /// per-dimension group sizes are <see cref="_realGroupDimXLocal"/> / <see cref="_realGroupDimYLocal"/>.
        /// The total is what the barrier last-thread check and group-id (globalIdx/groupSize)
        /// computations need, so this stays = groupSize. Do not repurpose it to GroupDim.X.
        /// </summary>
        private uint _groupDimXLocal;

        /// <summary>
        /// Wasm local index for the REAL GroupDim.X (threads per workgroup in X). Used to
        /// decompose the linear thread id / linear group id into per-dimension Group.Idx /
        /// Grid.Idx so 2D/3D groups work (mirrors the CPU backend). For 1D groups == groupSize.
        /// </summary>
        private uint _realGroupDimXLocal;

        /// <summary>
        /// Wasm local index for the REAL GroupDim.Y (threads per workgroup in Y). 1 for 1D groups.
        /// GroupDim.Z is derived in-kernel as groupSize / (GroupDim.X * GroupDim.Y).
        /// </summary>
        private uint _realGroupDimYLocal;

        /// <summary>
        /// Wasm local index for thread index within the workgroup.
        /// </summary>
        private uint _threadIdXLocal;

        /// <summary>
        /// Wasm local index for the base address of shared memory region in linear memory.
        /// </summary>
        private uint _sharedMemBaseLocal;

        /// <summary>
        /// Wasm local index for the base address of barrier slots in linear memory.
        /// </summary>
        private uint _barrierBaseLocal;

        /// <summary>
        /// Wasm local index for the dynamic shared memory element count (passed at dispatch time).
        /// </summary>
        private uint _dynamicSharedLengthLocal;

        /// <summary>
        /// Wasm local index for the phase parameter (fiber dispatch).
        /// Phase 0 = first execution, Phase N = resume after barrier N.
        /// </summary>
        private uint _phaseParamLocal;

        /// <summary>
        /// When true, barriers emit save-locals + return instead of memory.atomic.wait32.
        /// The worker script calls the kernel once per phase per fiber.
        /// </summary>
        private bool _phaseMode = false;
        private bool _needsSyncYields = false;

        /// <summary>
        /// Counter for barrier synchronization points in the kernel.
        /// In phase mode, this also counts the number of phase transitions.
        /// </summary>
        private int _barrierCounter = 0;

        /// <summary>
        /// Whether this kernel uses shared memory or barriers.
        /// </summary>
        private bool _hasBarriers = false;

        /// <summary>
        /// Offset within per-thread scratch where phase state (locals) is spilled.
        /// Set after alloca usage is known, before state machine code generation.
        /// </summary>
        private int _phaseStateOffset = 0;

        /// <summary>
        /// Byte positions in <see cref="Code"/> where a full-locals SAVE block must be
        /// inserted. Saves are DEFERRED (not emitted inline) so every save covers the
        /// FINAL local count — symmetric with the deferred restore prologue. Without this,
        /// an inline save at an early barrier captured only <c>_locals.Count</c>-as-of-then,
        /// while the restore (built at the end) reads the final count, so any local
        /// allocated after that barrier was restored from an unwritten/stale scratch slot.
        /// Cleared at the start of each function's state-machine generation.
        /// </summary>
        private readonly List<int> _saveAllLocalsInsertPoints = new();

        /// <summary>
        /// Cumulative scratch offset for helper calls. Each helper with barriers
        /// gets its own region AFTER the kernel's state + previous helpers' state.
        /// </summary>
        private int _helperScratchCumulativeOffset = 0;

        /// <summary>
        /// Helper scratch base locals and their cumulative offsets.
        /// Populated during codegen, used in prologue generation to set the values.
        /// </summary>
        private readonly List<(uint localIdx, int cumulativeOffset)> _helperScratchBaseLocals = new();

        /// <summary>
        /// Per-helper dedicated scratch region locals for the SINGLE-CALL path (no-barrier
        /// helpers): one region per unique helper, shared across its call sites (a
        /// no-barrier helper keeps no cross-call state - only its completion-persist
        /// writes land there, which previously clobbered the KERNEL's spill slots).
        /// </summary>
        private readonly Dictionary<global::ILGPU.IR.Method, uint> _singleCallHelperScratch = new();

        /// <summary>
        /// Total bytes allocated for shared memory in this kernel.
        /// </summary>
        private int _sharedMemorySize = 0;

        /// <summary>
        /// Tracks which helpers have had their shared allocations set up,
        /// to avoid inflating _sharedMemorySize with duplicate allocations.
        /// </summary>
        private readonly HashSet<Method> _setupSharedHelpers = new();

        /// <summary>
        /// Maps Alloca IR values to their byte offsets within the shared memory region.
        /// </summary>
        private readonly Dictionary<string, int> _sharedAllocaOffsets = new();

        /// <summary>
        /// Stores (elementType string, arraySize) metadata for each shared alloca entry,
        /// keyed by the same key as _sharedAllocaOffsets. Used for fallback matching
        /// when the Alloca IR node encountered during code generation is a different
        /// instance from the one registered during SetupSharedAllocations().
        /// </summary>
        private readonly Dictionary<string, (string ElemType, int ArraySize)> _sharedAllocaMetadata = new();

        /// <summary>
        /// Element size in bytes for dynamic shared memory allocations.
        /// </summary>
        private int _dynamicSharedElementSize = 0;

        // === Helper function mode ===

        /// <summary>
        /// Whether this generator is producing a helper function (not the kernel entry point).
        /// Helper functions have different parameter setup and return value handling.
        /// </summary>
        private bool _isHelperFunction = false;

        /// <summary>
        /// Wasm local index for the helper's return value (non-void helpers only).
        /// </summary>
        private uint _helperResultLocal;

        /// <summary>
        /// Wasm type of the helper's return value, or null for void helpers.
        /// </summary>
        private byte? _helperResultType;

        // === Exposed state for helper generation ===

        /// <summary>
        /// Exposes shared memory alloca offsets for use by helper function generators.
        /// </summary>
        internal IReadOnlyDictionary<string, int> SharedAllocaOffsets => _sharedAllocaOffsets;

        /// <summary>
        /// Exposes shared memory alloca metadata for use by helper function generators.
        /// </summary>
        internal IReadOnlyDictionary<string, (string ElemType, int ArraySize)> SharedAllocaMetadata => _sharedAllocaMetadata;

        /// <summary>
        /// Exposes the current shared memory size for use by helper function generators.
        /// </summary>
        internal int SharedMemorySizeValue => _sharedMemorySize;

        public WasmKernelFunctionGenerator(
            in GeneratorArgs args,
            Method method,
            Allocas allocas)
            : base(args, method, allocas)
        {
        }

        /// <summary>
        /// Result of generating a helper function body.
        /// </summary>
        internal class HelperFunctionResult
        {
            public byte[] ParamTypes { get; set; } = Array.Empty<byte>();
            public byte[] ResultTypes { get; set; } = Array.Empty<byte>();
            public List<WasmLocal> Locals { get; set; } = new();
            public byte[] Code { get; set; } = Array.Empty<byte>();
            public int BarrierCount { get; set; }
            public int SharedMemorySize { get; set; }
            public int ScratchPerThread { get; set; }
        }

        /// <summary>
        /// Generates this method as a standalone helper function (not the kernel entry point).
        /// Called from WasmBackend.CreateKernel() for multi-block helpers.
        ///
        /// The helper function receives the same fixed context params as the kernel
        /// (globalIdx, dimX, dimY, scratchBase, groupDimX, threadIdX, sharedMemBase,
        /// barrierBase, dynamicSharedLength), followed by the helper's IR parameters.
        /// Returns a value via the Wasm function return mechanism.
        /// </summary>
        internal HelperFunctionResult GenerateAsHelper(
            IReadOnlyDictionary<string, int> sharedAllocaOffsets,
            IReadOnlyDictionary<string, (string ElemType, int ArraySize)> sharedAllocaMetadata,
            int sharedMemorySize,
            Dictionary<string, uint> mathImports)
        {
            _isHelperFunction = true;
            MathImports = mathImports;

            // Copy shared memory allocation state from the kernel generator.
            // The kernel has already computed offsets for all static shared allocas
            // (including this helper's). The helper starts from this base and may
            // add more allocations (e.g., Broadcast slots) during code generation.
            foreach (var kv in sharedAllocaOffsets)
                _sharedAllocaOffsets[kv.Key] = kv.Value;
            foreach (var kv in sharedAllocaMetadata)
                _sharedAllocaMetadata[kv.Key] = kv.Value;
            _sharedMemorySize = sharedMemorySize;
            if (sharedMemorySize > 0)
                _hasBarriers = true;

            // Set up helper-specific parameters
            SetupHelperParameters();

            // Generate code (state machine for multi-block)
            var blocks = Method.Blocks;
            _blockCount = blocks.Count;

            if (_blockCount <= 1)
            {
                _isStateMachine = false;
                if (_blockCount == 1)
                {
                    var singleBlock = blocks.First();
                    foreach (var value in singleBlock)
                        GenerateCodeFor(value);
                    if (singleBlock.Terminator != null)
                        GenerateCodeFor(singleBlock.Terminator);
                }
            }
            else
            {
                GenerateStateMachineCode(blocks);
            }

            // After the state machine, push the return value onto the stack.
            // In phase mode: state machine already pushes _yieldedLocal (i32).
            // Don't push the original return value — it would double-push.
            if (!_phaseMode && _helperResultType.HasValue)
            {
                WasmModuleBuilder.EmitLocalGet(Code, _helperResultLocal);
            }

            // Determine result types
            // Option E: helpers always return their natural result type.
            // The yield flag is communicated via scratch[0], not the return value.
            byte[] resultTypes;
            if (_helperResultType.HasValue)
                resultTypes = new[] { _helperResultType.Value };
            else
                resultTypes = Array.Empty<byte>();

            return new HelperFunctionResult
            {
                ParamTypes = FuncParamTypes.ToArray(),
                ResultTypes = resultTypes,
                Locals = _locals,
                Code = Code.ToArray(),
                BarrierCount = _barrierCounter,
                SharedMemorySize = _sharedMemorySize,
                ScratchPerThread = (_scratchNextOffset + 7) & ~7,
            };
        }

        /// <summary>
        /// Sets up parameter-to-local mappings for a helper function.
        /// Fixed context params first (same as kernel), then the helper's IR params.
        /// </summary>
        private void SetupHelperParameters()
        {
            _locals.Clear();
            _localMap.Clear();
            _nextLocalIndex = 0;
            _paramCount = 0;

            // Fixed context params (same order as kernel — 9 params)
            _globalIdxLocal = _nextLocalIndex++;
            _paramCount++;
            _dimXLocal = _nextLocalIndex++;
            _paramCount++;
            _dimYLocal = _nextLocalIndex++;
            _paramCount++;
            _scratchBaseLocal = _nextLocalIndex++;
            _paramCount++;
            _scratchNextOffset = 0;
            _groupDimXLocal = _nextLocalIndex++;
            _paramCount++;
            _threadIdXLocal = _nextLocalIndex++;
            _paramCount++;
            _sharedMemBaseLocal = _nextLocalIndex++;
            _paramCount++;
            _barrierBaseLocal = _nextLocalIndex++;
            _paramCount++;
            _dynamicSharedLengthLocal = _nextLocalIndex++;
            _paramCount++;

            _phaseParamLocal = _nextLocalIndex++;
            _paramCount++;

            // Real per-dimension group sizes (for 2D/3D group decomposition). Appended after the
            // original system params so user-param indices shift uniformly with _paramCount.
            _realGroupDimXLocal = _nextLocalIndex++;
            _paramCount++;
            _realGroupDimYLocal = _nextLocalIndex++;
            _paramCount++;

            FuncParamTypes.Clear();
            FuncParamTypes.Add(WasmOpCodes.I32); // globalIdx
            FuncParamTypes.Add(WasmOpCodes.I32); // dimX
            FuncParamTypes.Add(WasmOpCodes.I32); // dimY
            FuncParamTypes.Add(WasmOpCodes.I32); // scratchBase
            FuncParamTypes.Add(WasmOpCodes.I32); // groupDimX (= total groupSize)
            FuncParamTypes.Add(WasmOpCodes.I32); // threadIdX
            FuncParamTypes.Add(WasmOpCodes.I32); // sharedMemBase
            FuncParamTypes.Add(WasmOpCodes.I32); // barrierBase
            FuncParamTypes.Add(WasmOpCodes.I32); // dynamicSharedLength
            FuncParamTypes.Add(WasmOpCodes.I32); // phaseId (fiber dispatch)
            FuncParamTypes.Add(WasmOpCodes.I32); // realGroupDimX
            FuncParamTypes.Add(WasmOpCodes.I32); // realGroupDimY

            // Helper's IR parameters
            var parameters = Method.Parameters;
            for (int i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                var wasmType = GetWasmTypeFromIR(param.Type);
                FuncParamTypes.Add(wasmType);
                uint paramLocal = _nextLocalIndex++;
                _paramCount++;
                _localMap[GetValueKey(param)] = paramLocal;

                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Helper] param[{i}] '{GetValueKey(param)}' -> local_{paramLocal} (type={wasmType:X2})");
            }

            // Determine return type from the method's return type
            var returnType = Method.ReturnType;
            if (returnType != null && !returnType.IsVoidType)
            {
                _helperResultType = GetWasmTypeFromIR(returnType);
                _helperResultLocal = AllocateNewLocal(_helperResultType.Value);
                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Helper] Return type: {_helperResultType:X2}, resultLocal=local_{_helperResultLocal}");
            }

            // Re-register shared memory allocations. The offsets were pre-computed by the
            // kernel generator and copied in GenerateAsHelper(). SetupSharedAllocations
            // will skip keys that already exist (guard in that method), ensuring no
            // double-counting. However, if the alloca IR values here have different keys
            // from the kernel's copy (different instances), this re-registration ensures
            // they're properly mapped.
            SetupSharedAllocations(Allocas.SharedAllocations, isDynamic: false);
            SetupSharedAllocations(Allocas.DynamicSharedAllocations, isDynamic: true);

            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Helper] Setup complete: {_nextLocalIndex} locals, {FuncParamTypes.Count} params, sharedMem={_sharedMemorySize}");
        }

        /// <summary>
        /// Sets up parameter-to-local mappings. Must be called before visiting IR blocks,
        /// since ILGPU calls GenerateCode() BEFORE GenerateHeader().
        /// </summary>
        private void SetupParameters()
        {
            if (_parametersInitialized) return;
            _parametersInitialized = true;

            var entryPoint = _generatorArgs.EntryPoint;
            var parameters = Method.Parameters;

            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Setup] Parameters.Count={parameters.Count}, IsExplicitlyGrouped={entryPoint.IsExplicitlyGrouped}, _nextLocalIndex={_nextLocalIndex}");

            // Reset local tracking — params occupy indices 0..N-1
            // in the Wasm function. We track the param count so that
            // AllocateLocal knows when to emit extra locals.
            _locals.Clear();
            _localMap.Clear();
            _nextLocalIndex = 0;
            _paramCount = 0;

            // Store the index type for decomposition in GetField
            _indexType = entryPoint.IndexType;

            ScanBodyStructParams();

            // Fixed params: globalIdx (i32), dimX (i32), dimY (i32), scratchBase (i32),
            //               groupDimX (i32), threadIdX (i32), sharedMemBase (i32), barrierBase (i32)
            _globalIdxLocal = _nextLocalIndex++;
            _paramCount++;

            _dimXLocal = _nextLocalIndex++;
            _paramCount++;

            _dimYLocal = _nextLocalIndex++;
            _paramCount++;

            _scratchBaseLocal = _nextLocalIndex++;
            _paramCount++;
            _scratchNextOffset = 0;

            _groupDimXLocal = _nextLocalIndex++;
            _paramCount++;

            _threadIdXLocal = _nextLocalIndex++;
            _paramCount++;

            _sharedMemBaseLocal = _nextLocalIndex++;
            _paramCount++;

            _barrierBaseLocal = _nextLocalIndex++;
            _paramCount++;

            _dynamicSharedLengthLocal = _nextLocalIndex++;
            _paramCount++;

            _phaseParamLocal = _nextLocalIndex++;
            _paramCount++;

            // Real per-dimension group sizes (for 2D/3D group decomposition). Appended after the
            // original system params so user-param indices shift uniformly with _paramCount.
            _realGroupDimXLocal = _nextLocalIndex++;
            _paramCount++;
            _realGroupDimYLocal = _nextLocalIndex++;
            _paramCount++;

            // FuncParamTypes tracks Wasm type signatures for the module builder
            FuncParamTypes.Clear();
            FuncParamTypes.Add(WasmOpCodes.I32); // param 0: globalIdx
            FuncParamTypes.Add(WasmOpCodes.I32); // param 1: dimX
            FuncParamTypes.Add(WasmOpCodes.I32); // param 2: dimY
            FuncParamTypes.Add(WasmOpCodes.I32); // param 3: scratchBase
            FuncParamTypes.Add(WasmOpCodes.I32); // param 4: groupDimX (= total groupSize)
            FuncParamTypes.Add(WasmOpCodes.I32); // param 5: threadIdX
            FuncParamTypes.Add(WasmOpCodes.I32); // param 6: sharedMemBase
            FuncParamTypes.Add(WasmOpCodes.I32); // param 7: barrierBase
            FuncParamTypes.Add(WasmOpCodes.I32); // param 8: dynamicSharedLength
            FuncParamTypes.Add(WasmOpCodes.I32); // param 9: phaseId (fiber dispatch)
            FuncParamTypes.Add(WasmOpCodes.I32); // param 10: realGroupDimX
            FuncParamTypes.Add(WasmOpCodes.I32); // param 11: realGroupDimY

            // CRITICAL: Determine if param 0 is the implicit index parameter.
            // IndexType is unreliable: ILGPU overrides it to KernelConfig for ALL kernels
            // using SharedMemory, even those with an actual Index1D first parameter.
            // Instead, we check: (1) IndexType != None (kernel expects some index), AND
            // (2) parameters[0] is NOT an ArrayView type (views are always user params), AND
            // (3) parameters[0] looks like an actual index type — either a PrimitiveType
            //     (Int1D scalarized) or a StructureType matching the IndexNumFields fingerprint
            //     (Index2D = 2 i32 fields, Index3D = 3 i32 fields). Without (3), a
            //     KernelConfig-launched explicit kernel whose first user param happens to be
            //     a multi-view body struct gets misclassified as having an implicit Index
            //     and the param-info loop skips its only user param entirely (paramInfos
            //     stays empty, the dispatcher falls back to Marshal.StructureToPtr which
            //     fails with "DisposeBase must have StructLayout" on ArrayView<T>'s
            //     internal MemoryBuffer reference field). Surfaced by
            //     `Tests23_RegisterHeavyBody_ExplicitOneByOne_NoLaunchFailure`, 2026-05-05.
            //
            // This correctly handles:
            //   SharedMemoryKernel(Index1D, ArrayView) → param[0]=PrimitiveType → startIdx=1
            //   PrefixSumKernel(ArrayView)             → param[0]=View → startIdx=0
            //   Kernel2D(Index2D, ArrayView2D)          → param[0]=StructureType(2 i32 fields) → startIdx=1
            //   ExplicitKernel(BodyStruct{12 views})    → param[0]=StructureType(12 views) → startIdx=0
            bool hasImplicitIndex = entryPoint.IndexType != IndexType.None
                && parameters.Count > 0
                && !IsViewType(parameters[0].Type)
                && LooksLikeIndexType(parameters[0].Type, entryPoint.IndexType);
            int startIdx = hasImplicitIndex ? 1 : 0;

            // Map the implicit index param to globalIdx
            if (startIdx == 1 && parameters.Count > 0)
            {
                _indexParam = parameters[0];

                // Check if the implicit index is Int64 (LongIndex1D).
                // globalIdx is i32, but the IR parameter might be i64.
                // Create an i64 local that extends globalIdx for i64 params.
                if (GetWasmTypeFromIR(parameters[0].Type) == WasmOpCodes.I64)
                {
                    // Int64 implicit index (LongIndex1D) = paddedNumDataElements.
                    // This is NOT the thread index — it's the loop bound / extent.
                    // Don't map to _globalIdxLocal. Instead, let it be a regular user param
                    // by NOT mapping it and NOT incrementing startIdx.
                    _indexParam = null; // not an index param
                    startIdx = 0; // all params are user params
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Setup] Int64 param is extent, not index — treating as user param");
                }
                else
                {
                    _localMap[GetValueKey(_indexParam)] = _globalIdxLocal;
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Setup] Index param {GetValueKey(_indexParam)} -> local_{_globalIdxLocal}, IndexType={_indexType}");
                }
            }

            for (int i = startIdx; i < parameters.Count; i++)
            {
                var param = parameters[i];
                var paramType = param.Type;
                bool isView = IsViewType(paramType);

                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Setup] param[{i}] id={param.Id} type={paramType} isView={isView} _nextLocalIndex={_nextLocalIndex}");

                if (isView)
                {
                    _hasViews = true;

                    FuncParamTypes.Add(WasmOpCodes.I32); // byte offset
                    uint offsetLocal = _nextLocalIndex++;
                    _paramCount++;

                    FuncParamTypes.Add(WasmOpCodes.I32); // element count
                    uint lengthLocal = _nextLocalIndex++;
                    _paramCount++;

                    FuncParamTypes.Add(WasmOpCodes.I32); // stride (YStride for 2D, YStride for 3D)
                    uint strideLocal = _nextLocalIndex++;
                    _paramCount++;

                    FuncParamTypes.Add(WasmOpCodes.I32); // stride2 (ZStride for 3D, 0 for 1D/2D)
                    uint stride2Local = _nextLocalIndex++;
                    _paramCount++;

                    _paramLocals[i] = new[] { offsetLocal, lengthLocal, strideLocal, stride2Local };
                    _localMap[GetValueKey(param)] = offsetLocal;

                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Setup]   View: {GetValueKey(param)}=local_{offsetLocal} (length=local_{lengthLocal}, stride=local_{strideLocal}, stride2=local_{stride2Local})");

                    // Determine stride field start index from the IR struct type
                    // The struct layout is: [View, Int64..., Int32...] where Int32 fields are strides
                    int strideStart = 1; // default: right after field 0
                    if (paramType is StructureType structType)
                    {
                        int fieldCount = structType.NumFields;
                        // Find first Int32 field after field 0 - these are stride fields
                        strideStart = fieldCount; // default to end (no strides)
                        for (int fi = 1; fi < fieldCount; fi++)
                        {
                            var fieldType = structType.Fields[fi];
                            if (fieldType is PrimitiveType pft && pft.BasicValueType == BasicValueType.Int32)
                            {
                                strideStart = fi;
                                break;
                            }
                        }
                    }
                    _viewStrideStartField[i] = strideStart;
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Setup]   View strideStartField={strideStart}");

                    var elemType = GetViewElementType(paramType);
                    int elemSize = 4;
                    if (elemType is PrimitiveType pt)
                        elemSize = GetElementSize(pt.BasicValueType);
                    else if (elemType is StructureType st)
                        elemSize = st.Size;

                    _paramInfos.Add(new WasmParamInfo
                    {
                        Index = i,
                        Name = $"param{i}",
                        IsView = true,
                        WasmType = WasmOpCodes.I32,
                        ElementSize = elemSize,
                    });
                }
                else if (paramType is StructureType bodySt && IsBodyStruct(bodySt)
                    && _bodyStructFields.TryGetValue(i, out var bsFields))
                {
                    _hasViews = true;
                    foreach (var bf in bsFields)
                    {
                        if (!bf.IsView)
                            continue;

                        FuncParamTypes.Add(WasmOpCodes.I32);
                        uint offsetLocal = _nextLocalIndex++;
                        _paramCount++;
                        FuncParamTypes.Add(WasmOpCodes.I32);
                        uint lengthLocal = _nextLocalIndex++;
                        _paramCount++;
                        FuncParamTypes.Add(WasmOpCodes.I32);
                        uint strideLocal = _nextLocalIndex++;
                        _paramCount++;
                        FuncParamTypes.Add(WasmOpCodes.I32);
                        uint stride2Local = _nextLocalIndex++;
                        _paramCount++;

                        _bodyStructViewLocals[(i, bf.FieldIndex)] = new[]
                        {
                            offsetLocal, lengthLocal, strideLocal, stride2Local,
                        };
                        if (WasmBackend.VerboseLogging)
                            WasmBackend.Log($"[Wasm-Setup]   BodyStruct view: param[{i}] field[{bf.FieldIndex}] -> locals {offsetLocal},{lengthLocal},{strideLocal},{stride2Local}");
                    }

                    var wasmType = GetWasmTypeFromIR(paramType);
                    FuncParamTypes.Add(wasmType);
                    uint valLocal = _nextLocalIndex++;
                    _paramCount++;

                    _paramLocals[i] = new[] { valLocal };
                    _localMap[GetValueKey(param)] = valLocal;

                    var structFields = new List<StructFieldInfo>();
                    FlattenStructLayout(bodySt, 0, structFields);

                    _paramInfos.Add(new WasmParamInfo
                    {
                        Index = i,
                        Name = $"param{i}",
                        IsBodyStruct = true,
                        IsScalar = true,
                        WasmType = wasmType,
                        StructFields = structFields,
                        StructSize = bodySt.Size,
                        IRTypeName = paramType?.GetType().Name + ":" + paramType?.ToString(),
                    });
                }
                else
                {
                    var wasmType = GetWasmTypeFromIR(paramType);
                    FuncParamTypes.Add(wasmType);
                    uint valLocal = _nextLocalIndex++;
                    _paramCount++;

                    _paramLocals[i] = new[] { valLocal };
                    _localMap[GetValueKey(param)] = valLocal;

                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Setup]   Scalar: {GetValueKey(param)}=local_{valLocal}");

                    // Record struct layout info for dispatch-side serialization
                    List<StructFieldInfo> structFields = null;
                    if (paramType is StructureType sType)
                    {
                        structFields = new List<StructFieldInfo>();
                        if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Setup]   Struct layout: fields={sType.NumFields}, size={sType.Size}, alignment={sType.Alignment}");
                        FlattenStructLayout(sType, 0, structFields);
                        if (WasmBackend.VerboseLogging)
                            foreach (var sf in structFields)
                                WasmBackend.Log($"[Wasm-Setup]     leaf: offset={sf.Offset}, wasmType=0x{sf.WasmType:X2}, size={sf.Size}, isViewPtr={sf.IsViewPtr}");
                    }

                    _paramInfos.Add(new WasmParamInfo
                    {
                        Index = i,
                        Name = $"param{i}",
                        IsScalar = true,
                        WasmType = wasmType,
                        StructFields = structFields,
                        StructSize = (paramType is StructureType st) ? st.Size : 0,
                        IRTypeName = paramType?.GetType().Name + ":" + paramType?.ToString(),
                    });
                }
            }

            // Store param infos for the backend
            _generatorArgs.ParamInfos = _paramInfos;
            _generatorArgs.IrUserParamIndexOffset = startIdx;

            // Setup shared memory allocations
            SetupSharedAllocations(Allocas.SharedAllocations, isDynamic: false);
            SetupSharedAllocations(Allocas.DynamicSharedAllocations, isDynamic: true);

            // Propagate metadata to GeneratorArgs for WasmCompiledKernel
            // NOTE: Only set SharedMemorySize and DynamicSharedElementSize here.
            // BarrierCount and HasBarriers are set AFTER code generation in GenerateCode()
            // because _barrierCounter is incremented during IR visiting (GenerateCode(Barrier)).
            _generatorArgs.SharedMemorySize = _sharedMemorySize;
            _generatorArgs.DynamicSharedElementSize = _dynamicSharedElementSize;

            // (i64 fixup removed — Int64 params are now handled as user params)

            if (WasmBackend.VerboseLogging)
            {
                WasmBackend.Log($"[Wasm-Setup] Final: _nextLocalIndex={_nextLocalIndex}, _paramCount={_paramCount}, FuncParamTypes={FuncParamTypes.Count}");
                WasmBackend.Log($"[Wasm-Setup] _localMap: {string.Join(", ", _localMap.Select(kv => $"{kv.Key}={kv.Value}"))}");
                WasmBackend.Log($"[Wasm-Setup] SharedMemorySize={_sharedMemorySize}, HasBarriers={_hasBarriers}");
            }
        }

        private bool _parametersInitialized = false;
        private bool _needsI64IndexFixup = false;

        /// <summary>
        /// GenerateHeader is called AFTER GenerateCode by ILGPU.
        /// It only needs to ensure FuncParamTypes is populated (which SetupParameters already did).
        /// </summary>
        public override void GenerateHeader(StringBuilder builder)
        {
            // SetupParameters was already called from GenerateCode.
            // FuncParamTypes is already populated. Nothing else to do here.
            SetupParameters(); // safe to call again (idempotent)
        }

        /// <summary>
        /// Gets the block index for a basic block in the state machine.
        /// </summary>
        private int GetBlockIndex(BasicBlock block)
        {
            if (_blockMap.TryGetValue(block, out var idx))
                return idx;
            return 0;
        }

        /// <summary>
        /// Pushes phi values before a branch.
        /// For each target block, find any phi values that source from the current block
        /// and assign them before transitioning.
        /// </summary>
        private void PushPhiValues(Branch branch)
        {
            for (int i = 0; i < branch.NumTargets; i++)
            {
                var target = branch.Targets[i];
                foreach (var valueEntry in target)
                {
                    if (valueEntry.Value is PhiValue phi)
                    {
                        // Find the value from the current block for this phi
                        for (int j = 0; j < phi.Count; j++)
                        {
                            if (phi.Sources[j] == branch.BasicBlock)
                            {
                                var phiLocal = GetLocal(phi);
                                var srcValue = phi[j].Resolve();
                                EmitGetLocal(srcValue);

                                // Coerce type if source and PHI local differ
                                var srcType = GetWasmTypeFromIR(srcValue.Type);
                                var phiType = GetLocalType(phiLocal);
                                if (srcType == WasmOpCodes.I64 && phiType == WasmOpCodes.I32)
                                    Code.Add(WasmOpCodes.I32WrapI64);
                                else if (srcType == WasmOpCodes.I32 && phiType == WasmOpCodes.I64)
                                    Code.Add(WasmOpCodes.I64ExtendI32S);

                                WasmModuleBuilder.EmitLocalSet(Code, phiLocal);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Computes the br depth to reach $loop from the code section of the current block.
        /// After block K's End has been emitted, remaining nesting above is:
        ///   (N-K-1) remaining blocks + $loop + $exit
        /// The label stack at Block K's code position:
        ///   depth 0: B[K+1], depth 1: B[K+2], ..., depth (N-K-2): B[N-1],
        ///   depth (N-K-1): $loop, depth (N-K): $exit
        /// So depth to $loop = N - K - 1 = _blockCount - _currentBlockEmitIndex - 1.
        /// </summary>
        private uint GetBrDepthToLoop(int extraNesting = 0)
        {
            return (uint)(_blockCount - _currentBlockEmitIndex - 1 + extraNesting);
        }

        /// <summary>
        /// Emits state transition: set _state = blockIndex, then br $loop.
        /// extraNesting accounts for additional nesting from if/else blocks.
        /// </summary>
        private void EmitBranchToBlock(int targetBlockIndex, int extraNesting = 0)
        {
            WasmModuleBuilder.EmitI32Const(Code, targetBlockIndex);
            WasmModuleBuilder.EmitLocalSet(Code, _stateLocal);
            Code.Add(WasmOpCodes.Br);
            WasmModuleBuilder.EmitU32Leb128(Code, GetBrDepthToLoop(extraNesting));
        }

        /// <summary>
        /// Assigns Wasm function indices to multi-block helper methods.
        /// Called at the start of kernel GenerateCode(), after all helpers have been
        /// registered in data.HelperMethods by CreateFunctionCodeGenerator().
        /// </summary>
        private void AssignHelperFunctionIndices()
        {
            if (_generatorArgs.HelperFunctionIndices.Count > 0)
                return; // Already assigned (e.g., called twice)

            // Log helper info
            if (WasmBackend.VerboseLogging)
                foreach (var kvp in _generatorArgs.HelperMethods)
                    WasmBackend.Log($"Wasm: Helper '{kvp.Key.Name}' blocks={kvp.Key.Blocks.Count}");

            if (_generatorArgs.HelperMethods.Count == 0)
                return; // No helpers

            int importCount = WasmBackend.UnaryMathFuncs.Length + WasmBackend.BinaryMathFuncs.Length
                + (_generatorArgs.ExtraImportCount); // e.g., jsAtomicsWait import
            int nextHelperFuncIdx = importCount + 1; // +1 for the kernel function

            foreach (var kvp in _generatorArgs.HelperMethods)
            {
                int barrierCount = ComputeEffectiveBarrierCount(kvp.Key);
                // Promote to separate Wasm function if:
                // - Multi-block (needs its own state machine, can't nest), OR
                // - Contains barriers directly or in sub-helpers (sense locals must be
                //   isolated per call to prevent stale sense values when called
                //   multiple times in a loop)
                if (WasmBackend.VerboseLogging)
                {
                    var irTypes = new List<string>();
                    foreach (var b in kvp.Key.Blocks)
                        foreach (var e in b)
                            irTypes.Add(e.Value.GetType().Name);
                    WasmBackend.Log($"[Wasm-HelperCheck] '{kvp.Key.Name}' blocks={kvp.Key.Blocks.Count}, effectiveBarriers={barrierCount}, promoted={kvp.Key.Blocks.Count > 1 || barrierCount > 0}, irTypes=[{string.Join(",", irTypes)}]");
                }
                if (kvp.Key.Blocks.Count > 1 || barrierCount > 0)
                {
                    _generatorArgs.HelperFunctionIndices[kvp.Key] = nextHelperFuncIdx++;
                    _generatorArgs.HelperBarrierCounts[kvp.Key] = barrierCount;
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-PreScan] Helper '{kvp.Key.Name}': pre-scan barriers={barrierCount}, blocks={kvp.Key.Blocks.Count}");

                    // Estimate helper's scratch size for kernel-side offset computation.
                    // Actual size is computed during helper codegen, but we need an estimate
                    // now so the kernel can give each helper its own scratch region.
                    // Count IR values as proxy for locals (each local ≤ 8 bytes), add overhead.
                    int irValueCount = 0;
                    foreach (var block in kvp.Key.Blocks)
                        irValueCount += block.Count;
                    // Conservative: 11 params + irValues locals, 8 bytes each, + 8 yield flag + alignment
                    int estimatedHelperScratch = ((11 + irValueCount) * 8 + 8 + 7) & ~7;
                    _generatorArgs.HelperScratchEstimates[kvp.Key] = estimatedHelperScratch;
                    _generatorArgs.HelperFunctionOrder.Add(kvp.Key);
                    string reason = kvp.Key.Blocks.Count > 1 ? "multi-block" : "has-barriers";
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"Wasm: Helper '{kvp.Key.Name}' promoted ({reason}): funcIdx={_generatorArgs.HelperFunctionIndices[kvp.Key]}, barriers={barrierCount}, blocks={kvp.Key.Blocks.Count}");
                }
            }
        }

        /// <summary>
        /// Computes the effective barrier count for a method, recursively counting
        /// barriers from sub-helper MethodCalls. This is necessary because a helper's
        /// barriers may not be direct IR Barrier values — they may be in sub-methods
        /// that are called via MethodCall within the helper's blocks.
        ///
        /// Each Barrier value counts as 1, each Broadcast as 2 (store barrier + load barrier).
        /// Each MethodCall to a known helper adds that helper's effective barrier count.
        /// </summary>
        private int ComputeEffectiveBarrierCount(Method method, HashSet<Method>? visited = null)
        {
            visited ??= new HashSet<Method>();
            if (!visited.Add(method))
                return 0; // Prevent infinite recursion for cyclic calls

            int count = 0;
            foreach (var block in method.Blocks)
            {
                foreach (var entry in block)
                {
                    var value = entry.Value;
                    if (value is global::ILGPU.IR.Values.Barrier)
                        count++;
                    else if (value is global::ILGPU.IR.Values.Broadcast)
                        count += 2;
                    else if (value is global::ILGPU.IR.Values.ShuffleOperation)
                        count += 2; // Warp.Shuffle emulation = publish + reuse-guard barriers
                    else if (value is global::ILGPU.IR.Values.MethodCall call)
                    {
                        // Recursively count barriers in registered sub-helpers only.
                        if (_generatorArgs.HelperMethods.ContainsKey(call.Target))
                            count += ComputeEffectiveBarrierCount(call.Target, visited);
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Generates the function body by visiting all blocks.
        /// For multi-block kernels, uses a state machine with loop/block/br_table.
        /// </summary>
        public override void GenerateCode()
        {
            // CRITICAL: Set up parameter mappings FIRST, before any IR visiting.
            // ILGPU calls GenerateCode() BEFORE GenerateHeader().
            SetupParameters();

            // Assign function indices for multi-block helpers. This must happen here
            // (not in CreateKernelCodeGenerator) because ILGPU calls CreateKernelCodeGenerator
            // BEFORE CreateFunctionCodeGenerator, so data.HelperMethods is empty at that point.
            // By the time GenerateCode() runs, all helpers have been registered.
            AssignHelperFunctionIndices();

            var blocks = Method.Blocks;
            _blockCount = blocks.Count;

            // Check if single-block kernel has barriers — if so, force state machine
            // so phase-split dispatch works (single-block + barrier + fiber dispatch
            // would deadlock with memory.atomic.wait32).
            // Also check for MethodCalls to helpers that have barriers — these need
            // phase mode for the helper call loop even though no Barrier IR node exists
            // in the kernel's own block.
            bool singleBlockHasBarriers = false;
            if (_blockCount == 1)
            {
                foreach (var entry in blocks.First())
                {
                    if (entry.Value is global::ILGPU.IR.Values.Barrier || entry.Value is global::ILGPU.IR.Values.Broadcast || entry.Value is global::ILGPU.IR.Values.ShuffleOperation)
                    { singleBlockHasBarriers = true; break; }
                    if (entry.Value is MethodCall mc)
                    {
                        bool hasHelperBarriers = _generatorArgs.HelperBarrierCounts.TryGetValue(mc.Target, out int hbc) && hbc > 0;
                        if (!hasHelperBarriers)
                        {
                            foreach (var kv in _generatorArgs.HelperBarrierCounts)
                                if (kv.Key.Name == mc.Target.Name && kv.Value > 0)
                                { hasHelperBarriers = true; break; }
                        }
                        if (hasHelperBarriers)
                        { singleBlockHasBarriers = true; break; }
                    }
                }
            }

            if (_blockCount == 1 && !singleBlockHasBarriers)
            {
                // Single block without barriers: no state machine needed
                _isStateMachine = false;
                var singleBlock = blocks.First();
                foreach (var value in singleBlock)
                    GenerateCodeFor(value);
                if (singleBlock.Terminator != null)
                    GenerateCodeFor(singleBlock.Terminator);
                // i32 return value (0 = done)
                WasmModuleBuilder.EmitI32Const(Code, 0);
            }
            else
            {
                // Multi-block OR single-block-with-barriers: use state machine
                GenerateStateMachineCode(blocks);
            }

            // CRITICAL: Propagate BarrierCount, HasBarriers, and SharedMemorySize AFTER code generation.
            // _barrierCounter is incremented during IR visiting by GenerateCode(Barrier),
            // and _sharedMemorySize is incremented by GenerateCode(Broadcast) for broadcast slots,
            // so they must be read after all blocks have been visited.
            _generatorArgs.BarrierCount = _barrierCounter;
            _generatorArgs.HasBarriers = _hasBarriers;
            _generatorArgs.SharedMemorySize = _sharedMemorySize;
            _generatorArgs.ScratchPerThread = (_scratchNextOffset + 7) & ~7;

            // NOTE: Barrier sense flag reset for multi-group dispatch is handled in the
            // JavaScript worker loop (BuildWasmWorkerScript) to avoid generating
            // unreachable code after the Wasm return instruction.

            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-CodeGen] Final BarrierCount={_barrierCounter}, HasBarriers={_hasBarriers}, SharedMemorySize={_sharedMemorySize}");
        }

        /// <summary>
        /// Generates state machine code for multi-block kernels.
        /// 
        /// Wasm structured control flow pattern:
        ///   block $exit
        ///     loop $loop
        ///       block $blockN
        ///         block $blockN-1
        ///           ...
        ///           block $block0
        ///             local.get $state
        ///             br_table $block0 $block1 ... $blockN $exit
        ///           end ;; $block0
        ///           [code for block 0]
        ///           br $loop
        ///         end ;; $block1
        ///         [code for block 1]
        ///         br $loop
        ///       end ;; $blockN
        ///       [code for block N]
        ///       br $loop
        ///     end ;; $loop
        ///   end ;; $exit
        /// </summary>
        private void GenerateStateMachineCode(BasicBlockCollection<ReversePostOrder, Forwards> blocks)
        {
            _isStateMachine = true;

            // Allocate state local
            _stateLocal = AllocateNewLocal(WasmOpCodes.I32);
            // Allocate yielded flag (0=done, 1=yielded at barrier)
            _yieldedLocal = AllocateNewLocal(WasmOpCodes.I32);
            // Fixed temporaries for the VERIFIED spill loop (see EmitSaveAllLocalsTo)
            _spillVerifyAddr = AllocateNewLocal(WasmOpCodes.I32);
            _spillVerifyVal32 = AllocateNewLocal(WasmOpCodes.I32);
            _spillVerifyVal64 = AllocateNewLocal(WasmOpCodes.I64);

            // === Phase 1: Pre-scan for barriers and compute expanded block count ===
            // Each IR block that contains a Barrier instruction gets split into two
            // state machine entries: pre-barrier code and post-barrier continuation.
            // This is needed for fiber dispatch: at barrier points, the kernel saves
            // locals and returns. The next phase call restores locals and the br_table
            // dispatches to the continuation block.

            // Pre-count ALL barrier emissions across all IR blocks.
            // Each EmitBarrier call = 1 continuation block needed in the state machine.
            // Barrier: 1. Broadcast: 2.
            // MethodCall to barrier-helper: helperBarrierCount continuation blocks
            // (each helper yield becomes a kernel yield with its own continuation).
            // Two-pass counting: first count direct barriers, then decide whether to
            // include helper barriers (only for kernels with no own barriers).
            int directBarriers = 0;
            int helperBarriers = 0;
            int helperCallCount = 0; // number of barrier-helper MethodCalls
            foreach (var block in blocks)
            {
                foreach (var entry in block)
                {
                    if (entry.Value is global::ILGPU.IR.Values.Barrier)
                        directBarriers += 1;
                    else if (entry.Value is global::ILGPU.IR.Values.Broadcast)
                        directBarriers += 2; // before + after barriers
                    else if (entry.Value is global::ILGPU.IR.Values.ShuffleOperation)
                        directBarriers += 2; // publish + reuse-guard barriers
                    else if (!_isHelperFunction && entry.Value is MethodCall mc1)
                    {
                        if (_generatorArgs.HelperBarrierCounts.TryGetValue(mc1.Target, out int hbc1) && hbc1 > 0)
                        {
                            helperCallCount++;
                            // helperBarrierCount yields + 1 sync yield after done = hbc1 + 1
                            helperBarriers += hbc1 + 1;
                            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-BarrierScan] Found helper '{mc1.Target.Name}' with {hbc1} barriers + 1 sync (direct)");
                        }
                        else
                        {
                            bool found = false;
                            foreach (var kv in _generatorArgs.HelperBarrierCounts)
                            {
                                if (kv.Key.Name == mc1.Target.Name && kv.Value > 0)
                                {
                                    helperCallCount++;
                                    helperBarriers += kv.Value + 1; // +1 for sync yield
                                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-BarrierScan] Found helper '{mc1.Target.Name}' with {kv.Value} barriers + 1 sync (name fallback)");
                                    found = true; break;
                                }
                            }
                            if (!found && false) // disabled — was causing regressions
                            {
                                int computed = ComputeEffectiveBarrierCount(mc1.Target);
                                if (computed > 0)
                                {
                                    helperBarriers += computed;
                                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-BarrierScan] Computed {computed} barriers for '{mc1.Target.Name}' (not in HelperBarrierCounts)");
                                }
                            }
                        }
                    }
                }
            }
            // Fix B: DISABLED — counting struct-to-Global Store yields removed
            int structStoreYields = 0;

            // Sync yields are only needed when there are 2+ barrier-helper calls
            // (to prevent shared memory stomping between sequential calls).
            // With 1 call, no sync yield needed — remove the extra counts.
            _needsSyncYields = helperCallCount >= 1;
            if (!_needsSyncYields && helperCallCount == 1)
                helperBarriers -= 1; // undo the +1 sync yield added during scan
            int totalBarriers = directBarriers + helperBarriers + structStoreYields;
            int expandedBlockCount = _blockCount + totalBarriers;

            // Build block map: assign state machine indices to each IR block.
            // Both kernels and helpers use dynamic block splitting — barriers create
            // continuation blocks that the br_table dispatches to on re-entry.
            int smIndex = 0;
            foreach (var block in blocks)
            {
                int count = 0;
                foreach (var entry in block)
                {
                    if (entry.Value is global::ILGPU.IR.Values.Barrier) count += 1;
                    else if (entry.Value is global::ILGPU.IR.Values.Broadcast) count += 2;
                    else if (entry.Value is global::ILGPU.IR.Values.ShuffleOperation) count += 2;
                    else if (!_isHelperFunction && entry.Value is global::ILGPU.IR.Values.Store st2
                        && st2.Value.Resolve().Type is StructureType
                        && st2.Target.Resolve().Type is AddressSpaceType ast2
                        && ast2.AddressSpace == MemoryAddressSpace.Global)
                        count += 1;
                    else if (!_isHelperFunction && entry.Value is MethodCall mc2)
                    {
                        // Only count helper barriers for yield-per-phase (no own barriers).
                        // Kernels with own barriers use internal loop (no continuation blocks).
                        // Each helper call: N yield blocks + (1 sync yield if needsSyncYields)
                        int syncExtra = _needsSyncYields ? 1 : 0;
                        bool found2 = false;
                        if (_generatorArgs.HelperBarrierCounts.TryGetValue(mc2.Target, out int hbc2) && hbc2 > 0)
                        { count += hbc2 + syncExtra; found2 = true; }
                        if (!found2)
                        {
                            foreach (var kv in _generatorArgs.HelperBarrierCounts)
                            {
                                if (kv.Key.Name == mc2.Target.Name && kv.Value > 0)
                                { count += kv.Value + syncExtra; found2 = true; break; }
                            }
                        }
                    }
                }
                _blockMap[block] = smIndex;
                smIndex += 1 + count;
            }

            // Expand _blockCount for both kernels and helpers.
            _blockCount = expandedBlockCount;

            // Phase mode: enable when ANY barriers exist (own or via helpers).
            bool anyHelperHasBarriers = _generatorArgs.HelperBarrierCounts.Values.Any(c => c > 0);
            _phaseMode = totalBarriers > 0 || anyHelperHasBarriers || _generatorArgs.PhaseCount > 1;
            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Phase] totalBarriers={totalBarriers} (direct={directBarriers} helper={helperBarriers} calls={helperCallCount} sync={_needsSyncYields}), phaseMode={_phaseMode}, blockCount={_blockCount}, expandedBlockCount={expandedBlockCount}");
            // Reserve first 4 bytes of scratch for yield flag (Option E).
            // Phase state starts after, 8-byte aligned.
            int reservedForYieldFlag = _phaseMode ? 4 : 0;
            _phaseStateOffset = ((_scratchNextOffset + reservedForYieldFlag) + 7) & ~7; // 8-byte align

            // Advance _scratchNextOffset past the phase state region BEFORE IR visiting.
            // Struct Load scratch copies allocated during IR visiting MUST be after the
            // state save region. Otherwise, the yield state save overwrites the struct
            // snapshot data, causing in-place pre-sort aliasing in RadixSortKernel1.
            // (See pairs-sort-BEST-FIX.md for full analysis, reviewed by #1.)
            //
            // Option 1 (team-approved): estimate stateSize from IR value count.
            // Each IR value becomes ~1 Wasm local. Each local takes 4 or 8 bytes in
            // the state save. Use worst-case 8 bytes per value + margin for system locals.
            if (_phaseMode)
            {
                int irValueCount = 0;
                foreach (var block in blocks)
                    irValueCount += block.Count;
                // Margin raised 20 -> 100 (2026-06-10): EmitVerifiedAtomicStore allocates
                // 2 locals per atomic-store site during IR visiting; the spill region
                // must cover them or saves overflow into the alloca region.
                int estimatedStateBytes = (irValueCount + 100) * 8;
                _scratchNextOffset = (_phaseStateOffset + estimatedStateBytes + 7) & ~7;
            }

            if (WasmBackend.VerboseLogging)
            {
                WasmBackend.Log($"[Wasm-SM] State machine: {blocks.Count} IR blocks, {totalBarriers} barriers, {expandedBlockCount} SM blocks, phaseMode={_phaseMode}");
                foreach (var kvp in _blockMap)
                {
                    var termName = kvp.Key.Terminator?.GetType().Name ?? "none";
                    WasmBackend.Log($"[Wasm-SM]   Block {kvp.Value}: {kvp.Key} terminator={termName}");
                }
            }

            // === Phase 2: Generate state machine ===

            // Initialize state to 0 (first block) for phase 0
            WasmModuleBuilder.EmitI32Const(Code, 0);
            WasmModuleBuilder.EmitLocalSet(Code, _stateLocal);

            // Phase entry code will be generated AFTER all IR visiting,
            // when all locals are known. For now, record the insertion point.
            int phaseEntryInsertPoint = Code.Count;

            // SAVE blocks are likewise deferred to the end (final local count) and recorded
            // here as they are reached. Reset per function (kernel + each helper share this
            // generator's fields) so positions never leak across function bodies.
            _saveAllLocalsInsertPoints.Clear();

            // block $exit (void — return value tracked via _yieldedLocal)
            Code.Add(WasmOpCodes.Block);
            Code.Add(WasmOpCodes.Void);

            // loop $loop
            Code.Add(WasmOpCodes.Loop);
            Code.Add(WasmOpCodes.Void);

            // Nested blocks for dispatch: one per expanded SM block
            for (int i = 0; i < _blockCount; i++)
            {
                Code.Add(WasmOpCodes.Block);
                Code.Add(WasmOpCodes.Void);
            }

            // br_table dispatch
            WasmModuleBuilder.EmitLocalGet(Code, _stateLocal);
            Code.Add(WasmOpCodes.BrTable);
            WasmModuleBuilder.EmitU32Leb128(Code, (uint)_blockCount);
            for (int i = 0; i < _blockCount; i++)
                WasmModuleBuilder.EmitU32Leb128(Code, (uint)i);
            WasmModuleBuilder.EmitU32Leb128(Code, (uint)(_blockCount + 1)); // default: br $exit

            // === Phase 3: Emit code for each block ===
            // Barriers are handled dynamically by EmitBarrier which inserts block
            // splits (End + continuation block) on the fly.
            int currentSmIndex = 0;
            foreach (var block in blocks)
            {
                // End the innermost remaining block — br_table lands here
                Code.Add(WasmOpCodes.End);
                _currentBlockEmitIndex = _blockMap[block];
                currentSmIndex = _blockMap[block];

                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SM] SM block {_currentBlockEmitIndex}: {block}");

                // Emit all values (EmitBarrier handles splits dynamically)
                foreach (var entry in block)
                    GenerateCodeFor(entry);

                // Handle terminator
                if (block.Terminator != null)
                    GenerateCodeFor(block.Terminator);

                // Advance past any continuation blocks this block created
                currentSmIndex = _currentBlockEmitIndex + 1;
            }

            // end $loop
            Code.Add(WasmOpCodes.End);

            // end $exit
            Code.Add(WasmOpCodes.End);

            // Phase mode: persist completion state to scratch so that re-entry
            // on future phases dispatches to the default exit (returns 0).
            // ONLY run when the kernel actually completed (yieldedLocal == 0).
            // When yielding (yieldedLocal == 1), br $exit jumps here too — must NOT overwrite.
            if (_phaseMode)
            {
                WasmModuleBuilder.EmitLocalGet(Code, _yieldedLocal);
                WasmModuleBuilder.EmitI32Const(Code, 0);
                Code.Add(WasmOpCodes.I32Eq);
                Code.Add(WasmOpCodes.If);
                Code.Add(WasmOpCodes.Void);
                {
                    // Save _stateLocal = _blockCount (past all br_table entries → default → exit)
                    WasmModuleBuilder.EmitI32Const(Code, _blockCount);
                    WasmModuleBuilder.EmitLocalSet(Code, _stateLocal);
                    int stateOffset = GetLocalSpillOffset(_stateLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                    WasmModuleBuilder.EmitI32Const(Code, stateOffset);
                    Code.Add(WasmOpCodes.I32Add);
                    WasmModuleBuilder.EmitLocalGet(Code, _stateLocal);
                    WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0);
                    // Also persist _yieldedLocal = 0 so re-entry returns 0.
                    // This is the TAIL write of the completion persist - verified (FIFO drain).
                    int yieldedOffset = GetLocalSpillOffset(_yieldedLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                    WasmModuleBuilder.EmitI32Const(Code, yieldedOffset);
                    Code.Add(WasmOpCodes.I32Add);
                    WasmModuleBuilder.EmitLocalGet(Code, _yieldedLocal);
                    EmitVerifiedAtomicStore(32);
                }
                Code.Add(WasmOpCodes.End); // end if
            }

            // Push return value
            if (_isHelperFunction && _phaseMode && _helperResultType.HasValue)
            {
                // Option E: helper returns actual result (yield flag is in scratch[0])
                WasmModuleBuilder.EmitLocalGet(Code, _helperResultLocal);
            }
            else if (_phaseMode || (!_isHelperFunction))
            {
                // Kernel always returns i32 (_yieldedLocal)
                WasmModuleBuilder.EmitLocalGet(Code, _yieldedLocal);
            }
            // Helpers not in phase mode: return value handled by GenerateAsHelper

            // Propagate phase info
            if (_phaseMode)
            {
                // Extend scratch to include phase state. Use Math.Max to preserve
                // any scratch allocated during IR visiting (struct Load copy slots).
                int stateSize = ComputePhaseStateSize();
                int estimatedEnd = _scratchNextOffset; // current end (set by Option 1 estimate)
                int actualEnd = _phaseStateOffset + stateSize;
                if (actualEnd > estimatedEnd)
                {
                    // CRITICAL: the estimate was too small! State save will overflow into struct Load slots.
                    WasmBackend.Log($"[Wasm-CRITICAL] Phase state OVERFLOW: estimated end={estimatedEnd}, actual end={actualEnd}, diff={actualEnd - estimatedEnd} bytes. IR values may have been undercounted or locals grew during IR visiting.");
                }
                _scratchNextOffset = Math.Max(_scratchNextOffset, actualEnd);

                // NOW generate the phase entry prologue (all locals are known).
                // This code restores locals from scratch when phaseId > 0.
                var prologueCode = new List<byte>();
                WasmModuleBuilder.EmitLocalGet(prologueCode, _phaseParamLocal);
                WasmModuleBuilder.EmitI32Const(prologueCode, 0);
                prologueCode.Add(WasmOpCodes.I32GtS);
                prologueCode.Add(WasmOpCodes.If);
                prologueCode.Add(WasmOpCodes.Void);
                // Emit restore inline into prologueCode
                EmitRestoreAllLocalsTo(prologueCode);
                // Reset yielded flag
                WasmModuleBuilder.EmitI32Const(prologueCode, 0);
                WasmModuleBuilder.EmitLocalSet(prologueCode, _yieldedLocal);
                prologueCode.Add(WasmOpCodes.End); // end if

                // For phase 0 (first entry): ensure view-derived locals are initialized.
                // The IR may generate GetField(view, Length) → local copy that's only
                // populated when the GetField code block executes. On first entry, if the
                // GetField is in a later code block, the local stays at 0.
                // We can't easily determine which locals need initialization, so this is
                // handled by the EmitRestoreAllLocals which reads from zeroed scratch.
                // The real fix should ensure GetField for view parameters always uses
                // the function parameter local directly via _paramLocals.

                // Set helper scratch base locals (runs on every phase, not just restore).
                // Each helper gets scratchBase + _scratchNextOffset + cumulativeOffset.
                foreach (var (localIdx, cumOffset) in _helperScratchBaseLocals)
                {
                    int offset = ((_scratchNextOffset + cumOffset) + 7) & ~7; // 8-byte align
                    WasmModuleBuilder.EmitLocalGet(prologueCode, _scratchBaseLocal);
                    WasmModuleBuilder.EmitI32Const(prologueCode, offset);
                    prologueCode.Add(WasmOpCodes.I32Add);
                    WasmModuleBuilder.EmitLocalSet(prologueCode, localIdx);
                }

                // Build the full-count SAVE block once (final _locals → symmetric with the
                // restore prologue above). Inserted at every recorded save site.
                var saveBlock = new List<byte>();
                EmitSaveAllLocalsTo(saveBlock);

                // Coordinate ALL deferred insertions — the restore prologue + every save
                // block — in a SINGLE pass, in DESCENDING byte position. Inserting from the
                // back forward keeps every not-yet-applied (lower) position valid, since an
                // insert only shifts bytes at HIGHER positions. Wasm branches use structured
                // depth (not byte offsets), so the inserted bytes never break control flow.
                var deferredInserts = new List<(int pos, List<byte> bytes)>();
                deferredInserts.Add((phaseEntryInsertPoint, prologueCode));
                for (int s = 0; s < _saveAllLocalsInsertPoints.Count; s++)
                    deferredInserts.Add((_saveAllLocalsInsertPoints[s], saveBlock));

                // Sort by position descending (no LINQ — codegen runs in Blazor WASM).
                deferredInserts.Sort((a, b) => b.pos.CompareTo(a.pos));
                for (int s = 0; s < deferredInserts.Count; s++)
                    Code.InsertRange(deferredInserts[s].pos, deferredInserts[s].bytes);
            }
            // Set PhaseCount only for the kernel (not helpers — helpers don't drive dispatch).
            if (!_isHelperFunction)
                _generatorArgs.PhaseCount = _phaseMode ? (totalBarriers + 1) : 1;

            // Record scratch bytes used per thread. Set AFTER phase state and helper
            // scratch regions are finalized so the size includes everything.
            // _scratchNextOffset now includes: alloca + yield flag + phase state + helper scratch.
            // Extend _scratchNextOffset to include helper scratch regions.
            // Line 939 (after GenerateStateMachineCode returns) will use this to set
            // ScratchPerThread, ensuring it includes phase state + helper scratch.
            _scratchNextOffset += _helperScratchCumulativeOffset;
        }

        /// <summary>
        /// Gets the scratch memory offset for a specific local's spill slot.
        /// </summary>
        private int GetLocalSpillOffset(uint localIdx)
        {
            int offset = _phaseStateOffset;
            int localOffset = (int)localIdx - (int)_paramCount;
            for (int i = 0; i < localOffset && i < _locals.Count; i++)
            {
                switch (_locals[i].Type)
                {
                    case WasmOpCodes.I32:
                    case WasmOpCodes.F32:
                        offset += 4;
                        break;
                    case WasmOpCodes.I64:
                    case WasmOpCodes.F64:
                        offset += 8;
                        break;
                }
            }
            return offset;
        }

        // === Control Flow Overrides ===

        public override void GenerateCode(ReturnTerminator returnTerminator)
        {
            // Helper functions: store return value before exiting
            if (_isHelperFunction && _helperResultType.HasValue && !returnTerminator.IsVoidReturn)
            {
                EmitGetLocal(returnTerminator.ReturnValue.Resolve());
                WasmModuleBuilder.EmitLocalSet(Code, _helperResultLocal);

                // In phase mode (Option E): write yield flag 0 (done) to scratch[0].
                // The function returns the actual result via _helperResultLocal.
                if (_phaseMode)
                {
                    WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                    WasmModuleBuilder.EmitI32Const(Code, 0); // done flag
                    WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0);
                }
            }

            if (_isStateMachine)
            {
                // Set state to an invalid value and br to $loop.
                // $loop will re-dispatch via br_table which hits the default target → $exit.
                WasmModuleBuilder.EmitI32Const(Code, _blockCount); // out-of-range state
                WasmModuleBuilder.EmitLocalSet(Code, _stateLocal);
                // Note: state is NOT persisted to scratch here. The Return's state
                // (= _blockCount = out-of-range) ensures the current phase exits cleanly.
                // On any subsequent re-entry, the prologue restores the LAST YIELD's state,
                // but _yieldedLocal is reset to 0, so the kernel exits via the normal path.
                Code.Add(WasmOpCodes.Br);
                WasmModuleBuilder.EmitU32Leb128(Code, GetBrDepthToLoop());
            }
            else
            {
                // Non-state-machine: push i32 return for kernel (always) and helpers in phase mode
                if (!_isHelperFunction || _phaseMode)
                    WasmModuleBuilder.EmitI32Const(Code, 0);
                Code.Add(WasmOpCodes.Return);
            }
        }

        public override void GenerateCode(UnconditionalBranch branch)
        {
            if (!_isStateMachine) return;

            PushPhiValues(branch);
            int targetBlock = GetBlockIndex(branch.Target);
            EmitBranchToBlock(targetBlock);
        }

        public override void GenerateCode(IfBranch branch)
        {
            if (!_isStateMachine) return;

            PushPhiValues(branch);

            int trueBlock = GetBlockIndex(branch.TrueTarget);
            int falseBlock = GetBlockIndex(branch.FalseTarget);
            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SM] IfBranch: true→{trueBlock} false→{falseBlock} (blockCount={_blockCount})");

            EmitGetLocal(branch.Condition.Resolve());
            Code.Add(WasmOpCodes.If);
            Code.Add(WasmOpCodes.Void);
            // True branch — inside if{}, adds 1 nesting level
            EmitBranchToBlock(trueBlock, 1);
            Code.Add(WasmOpCodes.Else);
            // False branch — still inside if/else{}, same extra nesting
            EmitBranchToBlock(falseBlock, 1);
            Code.Add(WasmOpCodes.End);
        }

        public override void GenerateCode(SwitchBranch branch)
        {
            if (!_isStateMachine) return;

            PushPhiValues(branch);

            int defaultBlock = GetBlockIndex(branch.DefaultBlock);

            // For each case, use if chain — each if adds +1 nesting
            for (int i = 0; i < branch.NumCasesWithoutDefault; i++)
            {
                EmitGetLocal(branch.Condition.Resolve());
                WasmModuleBuilder.EmitI32Const(Code, i);
                Code.Add(WasmOpCodes.I32Eq);
                Code.Add(WasmOpCodes.If);
                Code.Add(WasmOpCodes.Void);
                int caseBlock = GetBlockIndex(branch.GetCaseTarget(i));
                EmitBranchToBlock(caseBlock, 1);
                Code.Add(WasmOpCodes.End);
            }

            // Default — no extra nesting
            EmitBranchToBlock(defaultBlock);
        }

        /// <summary>
        /// Merges the generated code into the StringBuilder (for debug output)
        /// and builds the Wasm module binary.
        /// </summary>
        public override void Merge(StringBuilder builder)
        {
            builder.AppendLine($"// Wasm kernel: {FuncParamTypes.Count} params, {_locals.Count} locals, {Code.Count} instruction bytes");
        }

        /// <summary>
        /// Gets the Wasm function body (locals + code) for this kernel.
        /// </summary>
        public WasmFuncBody GetFunctionBody()
        {
            return new WasmFuncBody
            {
                Locals = _locals,
                Code = Code.ToArray()
            };
        }

        /// <summary>
        /// Gets the function parameter types.
        /// </summary>
        public byte[] GetParamTypes()
        {
            return FuncParamTypes.ToArray();
        }

        #region Override: Memory Operations

        public override void GenerateCode(Load value)
        {
            // Global Float16 loads (e.g. ArrayView1D<Half>[idx]) must not use the
            // StructureType scratch-snapshot path — ILGPU can type the load as
            // StructureType even though the source is a 2-byte global half element.
            if (value.Source.Resolve().Type is AddressSpaceType addrHalfLd
                && addrHalfLd.ElementType is PrimitiveType ptHalfLd
                && ptHalfLd.BasicValueType == BasicValueType.Float16
                && (value.Type is PrimitiveType ptGlobalHalf
                    && ptGlobalHalf.BasicValueType == BasicValueType.Float16
                    || value.Type is StructureType stHalfLd && stHalfLd.Size <= 8))
            {
                var targetHalf = AllocateLocal(value, GetWasmType(value));
                EmitGetLocal(value.Source.Resolve());
                if (_hasBarriers)
                {
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad16U);
                    Code.Add(0x01); Code.Add(0x00);
                }
                else
                    WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I32Load16U, 1, 0);
                EmitF16ToF32();
                WasmModuleBuilder.EmitLocalSet(Code, targetHalf);
                return;
            }

            // For struct types, copy to scratch for snapshot semantics.
            // Without this, in-place pre-sort `view[pos] = value` can overwrite
            // the source data that another thread's Load references (aliasing).
            // This causes ~1-5 element corruptions in large (16K+) sorts.
            if (value.Type is StructureType structType)
            {
                int structSize = structType.Size;
                int alignedSize = (structSize + 7) & ~7;
                string valueKey = GetValueKey(value);
                if (!_structLoadSlots.TryGetValue(valueKey, out int scratchOffset))
                {
                    scratchOffset = _scratchNextOffset;
                    _scratchNextOffset += alignedSize;
                    _structLoadSlots[valueKey] = scratchOffset;
                }
                var target = AllocateLocal(value, WasmOpCodes.I32);
                var source = value.Source.Resolve();
                for (int i = 0; i < structType.NumFields; i++)
                {
                    var fieldAccess = new FieldAccess(i);
                    int byteOffset = structType.GetOffset(fieldAccess);
                    var fieldType = structType.Fields[i];
                    // Float16 fields are 2 bytes in struct memory but GetWasmTypeFromIR returns F32.
                    // Must use 2-byte load/store to preserve struct layout (no f16↔f32 conversion here).
                    bool isFloat16Field = fieldType is PrimitiveType ptF16Chk
                        && ptF16Chk.BasicValueType == BasicValueType.Float16;
                    bool isViewPtrField = fieldType is AddressSpaceType;
                    byte fieldWasmType = GetWasmTypeFromIR(fieldType);
                    byte loadOp, storeOp;
                    uint align;
                    if (isFloat16Field)
                    {
                        // Float16: 2-byte raw binary copy (no f32 promotion in struct copy)
                        loadOp = WasmOpCodes.I32Load16U;
                        storeOp = WasmOpCodes.I32Store16;
                        align = 1;
                    }
                    else if (isViewPtrField && fieldType.Size >= 8)
                    {
                        loadOp = WasmOpCodes.I64Load;
                        storeOp = WasmOpCodes.I64Store;
                        align = 3;
                    }
                    else switch (fieldWasmType)
                    {
                        case WasmOpCodes.I64: loadOp = WasmOpCodes.I64Load; storeOp = WasmOpCodes.I64Store; align = 3; break;
                        case WasmOpCodes.F32: loadOp = WasmOpCodes.F32Load; storeOp = WasmOpCodes.F32Store; align = 2; break;
                        case WasmOpCodes.F64: loadOp = WasmOpCodes.F64Load; storeOp = WasmOpCodes.F64Store; align = 3; break;
                        default: loadOp = WasmOpCodes.I32Load; storeOp = WasmOpCodes.I32Store; align = 2; break;
                    }
                    // dest addr = scratchBase + scratchOffset + byteOffset
                    WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                    WasmModuleBuilder.EmitI32Const(Code, scratchOffset + byteOffset);
                    Code.Add(WasmOpCodes.I32Add);
                    // source addr
                    EmitGetLocal(source);
                    if (byteOffset > 0)
                    {
                        WasmModuleBuilder.EmitI32Const(Code, byteOffset);
                        Code.Add(WasmOpCodes.I32Add);
                    }
                    // Load from source (atomic in barrier kernels for cross-worker visibility)
                    if (_hasBarriers)
                    {
                        if (isFloat16Field)
                        {
                            // Float16: 2-byte atomic load
                            WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad16U, 1, 0);
                        }
                        else if (isViewPtrField && fieldType.Size >= 8)
                        {
                            WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I64AtomicLoad, 3, 0);
                        }
                        else switch (fieldWasmType)
                        {
                            case WasmOpCodes.I64:
                                WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I64AtomicLoad, 3, 0);
                                break;
                            case WasmOpCodes.F32:
                                WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad, 2, 0);
                                // Keep as i32 — store to scratch as i32 (same bit pattern)
                                break;
                            case WasmOpCodes.F64:
                                WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I64AtomicLoad, 3, 0);
                                // Keep as i64 — store to scratch as i64 (same bit pattern)
                                break;
                            default: // I32
                                WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad, 2, 0);
                                break;
                        }
                        // Store to scratch (per-thread private, but use matching int type for
                        // the value on the stack after atomic load of float fields)
                        if (isFloat16Field)
                        {
                            // Float16: 2-byte store to scratch (raw f16 bits)
                            WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store16, 1, 0);
                        }
                        else if (isViewPtrField && fieldType.Size >= 8)
                        {
                            WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I64Store, 3, 0);
                        }
                        else switch (fieldWasmType)
                        {
                            case WasmOpCodes.I64:
                                WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I64Store, 3, 0);
                                break;
                            case WasmOpCodes.F32:
                                // Stack has i32 from atomic load; store as i32 (same bits as f32)
                                WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0);
                                break;
                            case WasmOpCodes.F64:
                                // Stack has i64 from atomic load; store as i64 (same bits as f64)
                                WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I64Store, 3, 0);
                                break;
                            default: // I32
                                WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0);
                                break;
                        }
                    }
                    else
                    {
                        WasmModuleBuilder.EmitLoad(Code, loadOp, align, 0);
                        WasmModuleBuilder.EmitStore(Code, storeOp, align, 0);
                    }
                }
                WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                WasmModuleBuilder.EmitI32Const(Code, scratchOffset);
                Code.Add(WasmOpCodes.I32Add);
                WasmModuleBuilder.EmitLocalSet(Code, target);
                return;
            }

            var target2 = AllocateLocal(value, GetWasmType(value));
            var source2 = value.Source.Resolve();
            var wasmType = GetWasmType(value);

            // Check if this is a Float16 load (2-byte element, promoted to f32)
            bool isFloat16 = value.Type is PrimitiveType pt2 && pt2.BasicValueType == BasicValueType.Float16;
            // Check if this is an Int16 load (2-byte element, promoted to i32)
            bool isInt16 = value.Type is PrimitiveType ptI16 && ptI16.BasicValueType == BasicValueType.Int16;
            // Check if this is an Int8 load (1-byte element, promoted to i32)
            bool isInt8 = value.Type is PrimitiveType ptI8 && ptI8.BasicValueType == BasicValueType.Int8;

            // Push the address (byte offset in linear memory)
            EmitGetLocal(source2);

            if (isFloat16)
            {
                // Float16: load 2 bytes as u16, convert to f32 via IEEE 754 bit manipulation
                if (_hasBarriers)
                {
                    // Atomic 16-bit load (2-byte aligned)
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad16U);
                    Code.Add(0x01); Code.Add(0x00); // align=1 (2 bytes), offset=0
                }
                else
                    WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I32Load16U, 1, 0);
                EmitF16ToF32(); // inline conversion: i32 (f16 bits) → f32
            }
            else if (isInt16)
            {
                // Detect unsigned (ushort) via CLR param type
                bool isUnsigned16 = false;
                var resolvedSource = value.Source.Resolve();
                if (resolvedSource is LoadElementAddress lea16)
                {
                    var leaSrc = lea16.Source.Resolve();
                    if (leaSrc is global::ILGPU.IR.Values.Parameter srcParam)
                    {
                        int userIdx = srcParam.Index - 1; // KernelParamOffset is typically 1
                        var ep = _generatorArgs.EntryPoint;
                        if (userIdx >= 0 && userIdx < ep.Parameters.Count)
                        {
                            var clrType = ep.Parameters[userIdx];
                            if (clrType.IsGenericType)
                            {
                                var genArgs = clrType.GetGenericArguments();
                                if (genArgs.Length > 0 && genArgs[0] == typeof(ushort))
                                    isUnsigned16 = true;
                            }
                        }
                    }
                }

                if (isUnsigned16)
                {
                    // UInt16: load 2 bytes with zero extension to i32
                    if (_hasBarriers)
                    {
                        Code.Add(WasmOpCodes.AtomicPrefix);
                        WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad16U);
                        Code.Add(0x01); Code.Add(0x00);
                        // No sign extension - zero-extend is correct for unsigned
                    }
                    else
                        WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I32Load16U, 1, 0);
                }
                else
                {
                    // Int16: load 2 bytes with sign extension to i32
                    if (_hasBarriers)
                    {
                        Code.Add(WasmOpCodes.AtomicPrefix);
                        WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad16U);
                        Code.Add(0x01); Code.Add(0x00);
                        Code.Add(WasmOpCodes.I32Extend16S);
                    }
                    else
                        WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I32Load16S, 1, 0);
                }
            }
            else if (isInt8)
            {
                // Detect unsigned (byte) vs signed (sbyte) via CLR param type
                bool isUnsigned8 = false;
                var resolvedSrc8 = value.Source.Resolve();
                if (resolvedSrc8 is LoadElementAddress lea8)
                {
                    var leaSrc8 = lea8.Source.Resolve();
                    if (leaSrc8 is global::ILGPU.IR.Values.Parameter srcParam8)
                    {
                        int userIdx8 = srcParam8.Index - 1;
                        var ep8 = _generatorArgs.EntryPoint;
                        if (userIdx8 >= 0 && userIdx8 < ep8.Parameters.Count)
                        {
                            var clrType8 = ep8.Parameters[userIdx8];
                            if (clrType8.IsGenericType)
                            {
                                var genArgs8 = clrType8.GetGenericArguments();
                                if (genArgs8.Length > 0 && genArgs8[0] == typeof(byte))
                                    isUnsigned8 = true;
                            }
                        }
                    }
                }

                if (isUnsigned8)
                {
                    // UInt8 (byte): load 1 byte with zero extension
                    if (_hasBarriers)
                    {
                        Code.Add(WasmOpCodes.AtomicPrefix);
                        WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad8U);
                        Code.Add(0x00); Code.Add(0x00); // align=0, offset=0
                    }
                    else
                        WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I32Load8U, 0, 0);
                }
                else
                {
                    // Int8 (sbyte): load 1 byte with sign extension
                    if (_hasBarriers)
                    {
                        Code.Add(WasmOpCodes.AtomicPrefix);
                        WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad8U);
                        Code.Add(0x00); Code.Add(0x00);
                        Code.Add(WasmOpCodes.I32Extend8S);
                    }
                    else
                        WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I32Load8S, 0, 0);
                }
            }
            // For barrier kernels, use atomic loads for ALL types for cross-worker visibility.
            // Float types use reinterpret: i32.atomic.load → f32.reinterpret_i32 (or i64 → f64).
            else if (_hasBarriers)
            {
                switch (wasmType)
                {
                    case WasmOpCodes.I64:
                        WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I64AtomicLoad, 3, 0);
                        break;
                    case WasmOpCodes.I32:
                        WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad, 2, 0);
                        break;
                    case WasmOpCodes.F32:
                        // i32.atomic.load then f32.reinterpret_i32
                        WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad, 2, 0);
                        Code.Add(WasmOpCodes.F32ReinterpretI32);
                        break;
                    case WasmOpCodes.F64:
                        // i64.atomic.load then f64.reinterpret_i64
                        WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I64AtomicLoad, 3, 0);
                        Code.Add(WasmOpCodes.F64ReinterpretI64);
                        break;
                    default:
                        WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad, 2, 0);
                        break;
                }
            }
            else
            {
                byte loadOp;
                uint align;
                switch (wasmType)
                {
                    case WasmOpCodes.I64:
                        loadOp = WasmOpCodes.I64Load;
                        align = 3;
                        break;
                    case WasmOpCodes.F32:
                        loadOp = WasmOpCodes.F32Load;
                        align = 2;
                        break;
                    case WasmOpCodes.F64:
                        loadOp = WasmOpCodes.F64Load;
                        align = 3;
                        break;
                    default:
                        loadOp = WasmOpCodes.I32Load;
                        align = 2;
                        break;
                }
                WasmModuleBuilder.EmitLoad(Code, loadOp, align, 0);
            }
            WasmModuleBuilder.EmitLocalSet(Code, target2);
        }


        public override void GenerateCode(LoadElementAddress value)
        {
            var target = AllocateLocal(value);
            var source = value.Source.Resolve();
            var index = value.Offset.Resolve();

            {
                Value? leaSrc = source;
                for (int leaDepth = 0; leaDepth < 12 && leaSrc != null; leaDepth++)
                {
                    if (leaSrc is GetField gfLea
                        && TryResolveBodyStructField(gfLea, out var bsParamLea, out var bsInfoLea, out _)
                        && bsInfoLea.IsView
                        && _bodyStructViewLocals.TryGetValue((bsParamLea.Index, bsInfoLea.FieldIndex), out var bsViewLocalsLea))
                    {
                        WasmModuleBuilder.EmitLocalGet(Code, bsViewLocalsLea[0]);
                        EmitGetLocal(index.Resolve());
                        if (GetWasmTypeFromIR(index.Type) == WasmOpCodes.I64)
                            Code.Add(WasmOpCodes.I32WrapI64);
                        int elemSizeLea = 4;
                        if (value.Type is AddressSpaceType addrBs
                            && addrBs.ElementType is PrimitiveType ptBs)
                            elemSizeLea = GetElementSize(ptBs.BasicValueType);
                        WasmModuleBuilder.EmitI32Const(Code, elemSizeLea);
                        Code.Add(WasmOpCodes.I32Mul);
                        Code.Add(WasmOpCodes.I32Add);
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        return;
                    }

                    leaSrc = leaSrc switch
                    {
                        GetField gfWalk => gfWalk.ObjectValue.Resolve(),
                        NewView nv => nv.Pointer.Resolve(),
                        global::ILGPU.IR.Values.Load load => load.Source.Resolve(),
                        ConvertValue cv => cv.Value.Resolve(),
                        AddressSpaceCast asc => asc.Value.Resolve(),
                        _ => null,
                    };
                }
            }

            // Cross-assembly body structs (e.g. ML TensorView<T>) may lower inp.Data[idx] as
            // ArrayView1D ops on a temp without a GetField in the LEA walk chain. Trace to the
            // kernel body-struct param and use the expanded view offset locals from dispatch.
            {
                int bsParamIdx = TraceToParameter(source);
                if (bsParamIdx >= 0
                    && _bodyStructFields.TryGetValue(bsParamIdx, out var bsFieldsTraced))
                {
                    int viewFi = -1;
                    for (int fi = 0; fi < bsFieldsTraced.Count; fi++)
                    {
                        if (bsFieldsTraced[fi].IsView)
                        {
                            viewFi = bsFieldsTraced[fi].FieldIndex;
                            break;
                        }
                    }

                    if (viewFi >= 0
                        && _bodyStructViewLocals.TryGetValue((bsParamIdx, viewFi), out var bsViewLocalsTraced))
                    {
                        WasmModuleBuilder.EmitLocalGet(Code, bsViewLocalsTraced[0]);
                        EmitGetLocal(index.Resolve());
                        if (GetWasmTypeFromIR(index.Type) == WasmOpCodes.I64)
                            Code.Add(WasmOpCodes.I32WrapI64);
                        int elemSizeBs = 4;
                        if (value.Type is AddressSpaceType addrBs2
                            && addrBs2.ElementType is PrimitiveType ptBs2)
                            elemSizeBs = GetElementSize(ptBs2.BasicValueType);
                        WasmModuleBuilder.EmitI32Const(Code, elemSizeBs);
                        Code.Add(WasmOpCodes.I32Mul);
                        Code.Add(WasmOpCodes.I32Add);
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        return;
                    }
                }
            }

            var sourceLocal = GetLocal(source);
            var indexLocal = GetLocal(index);
            if (WasmBackend.VerboseLogging)
            {
                WasmBackend.Log($"[Wasm-LEA] target=local_{target}, source=local_{sourceLocal} (IR={source.GetType().Name} id={source.Id} type={source.Type}), index=local_{indexLocal} (IR={index.GetType().Name} id={index.Id})");
                WasmBackend.Log($"[Wasm-LEA] _localMap dump: {string.Join(", ", _localMap.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }

            // Determine element size
            int elemSize = 4;
            if (value.Type is AddressSpaceType addrType)
            {
                if (addrType.ElementType is PrimitiveType pt)
                    elemSize = GetElementSize(pt.BasicValueType);
                else if (addrType.ElementType is StructureType st)
                    elemSize = st.Size;
            }

            // addr = source + index * elemSize
            // Source is an i32 memory address
            EmitGetLocal(source);
            // Wrap i64 source to i32 if needed (pointers are i32 in Wasm)
            if (GetWasmTypeFromIR(source.Type) == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I32WrapI64);

            // Index may be i64 (ILGPU uses Int64 for linearized 2D/3D array indices)
            EmitGetLocal(index);
            if (GetWasmTypeFromIR(index.Type) == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I32WrapI64);

            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-LEA] value.Type={value.Type}, elemSize={elemSize}");
            WasmModuleBuilder.EmitI32Const(Code, elemSize);
            Code.Add(WasmOpCodes.I32Mul);
            Code.Add(WasmOpCodes.I32Add);

            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        #endregion

        #region Override: GetField for ArrayView decomposition

        public override void GenerateCode(GetField value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            var source = value.ObjectValue.Resolve();
            var fieldIndex = (int)value.FieldSpan.Index;

            // Check if source is the implicit index parameter for 2D/3D decomposition
            if (source is global::ILGPU.IR.Values.Parameter indexParam && indexParam == _indexParam)
            {
                switch (_indexType)
                {
                    case IndexType.Index2D:
                        if (fieldIndex == 0) // X = globalIdx % dimX
                        {
                            WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                            WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                            Code.Add(WasmOpCodes.I32RemS);
                        }
                        else // Y = globalIdx / dimX
                        {
                            WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                            WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                            Code.Add(WasmOpCodes.I32DivS);
                        }
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        return;

                    case IndexType.Index3D:
                        if (fieldIndex == 0) // X = globalIdx % dimX
                        {
                            WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                            WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                            Code.Add(WasmOpCodes.I32RemS);
                        }
                        else if (fieldIndex == 1) // Y = (globalIdx / dimX) % dimY
                        {
                            WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                            WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                            Code.Add(WasmOpCodes.I32DivS);
                            WasmModuleBuilder.EmitLocalGet(Code, _dimYLocal);
                            Code.Add(WasmOpCodes.I32RemS);
                        }
                        else // Z = globalIdx / (dimX * dimY)
                        {
                            WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                            WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                            WasmModuleBuilder.EmitLocalGet(Code, _dimYLocal);
                            Code.Add(WasmOpCodes.I32Mul);
                            Code.Add(WasmOpCodes.I32DivS);
                        }
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        return;

                    default: // Index1D - just use globalIdx directly
                        WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        return;
                }
            }

            if (TryResolveBodyStructField(value, out var bsParam, out var bsInfo, out _)
                && bsInfo.IsView
                && _bodyStructViewLocals.TryGetValue((bsParam.Index, bsInfo.FieldIndex), out var bsViewLocals))
            {
                var targetWasmType = GetWasmType(value);
                int strideStartField = 3;
                if (fieldIndex == 0)
                {
                    WasmModuleBuilder.EmitLocalGet(Code, bsViewLocals[0]);
                }
                else if (fieldIndex < strideStartField)
                {
                    if (fieldIndex == 1)
                    {
                        WasmModuleBuilder.EmitLocalGet(Code, bsViewLocals[1]);
                        if (targetWasmType == WasmOpCodes.I64)
                            Code.Add(WasmOpCodes.I64ExtendI32S);
                    }
                    else
                    {
                        if (targetWasmType == WasmOpCodes.I64)
                            WasmModuleBuilder.EmitI64Const(Code, 0);
                        else
                            WasmModuleBuilder.EmitI32Const(Code, 0);
                    }
                }
                else if (fieldIndex - strideStartField == 0)
                {
                    WasmModuleBuilder.EmitLocalGet(Code, bsViewLocals[2]);
                    if (targetWasmType == WasmOpCodes.I64)
                        Code.Add(WasmOpCodes.I64ExtendI32S);
                }
                else if (fieldIndex - strideStartField == 1)
                {
                    WasmModuleBuilder.EmitLocalGet(Code, bsViewLocals[3]);
                    if (targetWasmType == WasmOpCodes.I64)
                        Code.Add(WasmOpCodes.I64ExtendI32S);
                }
                else
                {
                    if (targetWasmType == WasmOpCodes.I64)
                        WasmModuleBuilder.EmitI64Const(Code, 1);
                    else
                        WasmModuleBuilder.EmitI32Const(Code, 1);
                }

                if (targetWasmType == WasmOpCodes.I32
                    && GetWasmTypeFromIR(value.Type) == WasmOpCodes.I64)
                    Code.Add(WasmOpCodes.I32WrapI64);

                WasmModuleBuilder.EmitLocalSet(Code, target);
                return;
            }

            // Check if source is (or traces to) a Parameter that maps to an ArrayView.
            // For ArrayView1D params, the source might be a GetField(param, field)
            // rather than the Parameter directly. Use TraceToParameter to resolve.
            int resolvedParamIdx = -1;
            if (source is global::ILGPU.IR.Values.Parameter directParam && IsViewType(directParam.Type))
            {
                for (int pi = 0; pi < Method.Parameters.Count; pi++)
                    if (Method.Parameters[pi] == directParam) { resolvedParamIdx = pi; break; }
            }
            else
            {
                // Trace through GetField/NewView/etc. to find the underlying view param
                resolvedParamIdx = TraceToParameter(source);
                // Only use if the resolved param is actually a view
                if (resolvedParamIdx >= 0 && resolvedParamIdx < Method.Parameters.Count
                    && !IsViewType(Method.Parameters[resolvedParamIdx].Type))
                    resolvedParamIdx = -1;
            }

            if (resolvedParamIdx >= 0)
            {
                var param = Method.Parameters[resolvedParamIdx];
                int paramIdx = resolvedParamIdx;
                if (_paramLocals.TryGetValue(paramIdx, out var locals))
                {
                    var targetWasmType = GetWasmType(value);
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-GetField] View param[{paramIdx}] field={fieldIndex} targetType={targetWasmType:X} localsCount={locals.Length} sourceType={param.Type}");

                    // Determine stride start field for this view parameter
                    int strideStartField = _viewStrideStartField.GetValueOrDefault(paramIdx, 3);
                    int strideFieldOffset = fieldIndex - strideStartField; // 0=stride1, 1=stride2, etc.

                    if (fieldIndex == 0)
                    {
                        // Ptr (byte offset)
                        WasmModuleBuilder.EmitLocalGet(Code, locals[0]);
                    }
                    else if (fieldIndex < strideStartField)
                    {
                        // Context-sensitive field handling:
                        // - ArrayView1D (StructureType): fields = [ptr, Extent(i64), Stride...]
                        //   Field 1 = Extent = Length → return locals[1]
                        // - ArrayView (AddressSpaceType): fields = [ptr, Index(i64), Length(i64)]
                        //   Field 1 = Index/Offset → always 0 for whole-buffer views
                        //   Field 2 = Length → return locals[1]
                        bool isStructView = param.Type is StructureType;
                        if (fieldIndex == 1 && !isStructView)
                        {
                            // ArrayView's Index/Offset field — always 0
                            if (targetWasmType == WasmOpCodes.I64)
                                WasmModuleBuilder.EmitI64Const(Code, 0);
                            else
                                WasmModuleBuilder.EmitI32Const(Code, 0);
                        }
                        else
                        {
                            // Length/Extent — return element count from locals[1]
                            WasmModuleBuilder.EmitLocalGet(Code, locals[1]);
                            if (targetWasmType == WasmOpCodes.I64)
                                Code.Add(WasmOpCodes.I64ExtendI32S);
                        }
                    }
                    else if (strideFieldOffset == 0)
                    {
                        // Stride1 (YStride)
                        if (locals.Length > 2)
                        {
                            WasmModuleBuilder.EmitLocalGet(Code, locals[2]);
                            if (targetWasmType == WasmOpCodes.I64)
                                Code.Add(WasmOpCodes.I64ExtendI32S);
                        }
                        else
                        {
                            if (targetWasmType == WasmOpCodes.I64)
                                WasmModuleBuilder.EmitI64Const(Code, 1);
                            else
                                WasmModuleBuilder.EmitI32Const(Code, 1);
                        }
                    }
                    else if (strideFieldOffset == 1)
                    {
                        // Stride2 (ZStride)
                        if (locals.Length > 3)
                        {
                            WasmModuleBuilder.EmitLocalGet(Code, locals[3]);
                            if (targetWasmType == WasmOpCodes.I64)
                                Code.Add(WasmOpCodes.I64ExtendI32S);
                        }
                        else
                        {
                            if (targetWasmType == WasmOpCodes.I64)
                                WasmModuleBuilder.EmitI64Const(Code, 0);
                            else
                                WasmModuleBuilder.EmitI32Const(Code, 0);
                        }
                    }
                    else
                    {
                        // Unknown/higher stride field
                        if (targetWasmType == WasmOpCodes.I64)
                            WasmModuleBuilder.EmitI64Const(Code, 0);
                        else
                            WasmModuleBuilder.EmitI32Const(Code, 0);
                    }
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    return;
                }
            }

            // Handle struct field access from memory
            if (source.Type is StructureType structType)
            {
                var fieldAccess = new FieldAccess(fieldIndex);
                int byteOffset = structType.GetOffset(fieldAccess);
                var fieldType = structType.Fields[fieldIndex];
                bool isFloat16Field = fieldType is PrimitiveType ptF16Gf
                    && ptF16Gf.BasicValueType == BasicValueType.Float16;
                bool isInt16Field = fieldType is PrimitiveType ptI16Gf
                    && ptI16Gf.BasicValueType == BasicValueType.Int16;
                byte fieldWasmType = GetWasmTypeFromIR(fieldType);

                // Push address: source + byteOffset
                EmitGetLocal(source);
                if (byteOffset > 0)
                {
                    WasmModuleBuilder.EmitI32Const(Code, byteOffset);
                    Code.Add(WasmOpCodes.I32Add);
                }

                bool isViewPtrField = fieldType is AddressSpaceType;
                if (isViewPtrField && fieldType.Size >= 8)
                {
                    if (_hasBarriers)
                        WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I64AtomicLoad, 3, 0);
                    else
                        WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I64Load, 3, 0);
                    if (GetWasmType(value) == WasmOpCodes.I32)
                        Code.Add(WasmOpCodes.I32WrapI64);
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    return;
                }

                if (isFloat16Field)
                {
                    // Float16: load 2 bytes as u16, then convert f16 bits → f32
                    if (_hasBarriers)
                        WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad16U, 1, 0);
                    else
                        WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I32Load16U, 1, 0);
                    EmitF16ToF32();
                }
                else if (isInt16Field)
                {
                    // Int16: load 2 bytes with sign extension to i32
                    if (_hasBarriers)
                    {
                        WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad16U, 1, 0);
                        Code.Add(WasmOpCodes.I32Extend16S); // sign-extend after atomic unsigned load
                    }
                    else
                        WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I32Load16S, 1, 0);
                }
                else
                {
                    // Emit typed load for the field (atomic for barrier kernels)
                    EmitTypedLoad(fieldWasmType);
                }
                WasmModuleBuilder.EmitLocalSet(Code, target);
                return;
            }

            // Fallback: pass through
            EmitGetLocal(source);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public override void GenerateCode(FloatAsIntCast value)
        {
            // Float16 lives in an F32 Wasm local (promoted). A plain I32ReinterpretF32
            // would give the 32-bit f32 bit pattern, not the original 16-bit f16 bits.
            // EmitF32ToF16() converts the promoted f32 value back to its 16-bit bit
            // pattern as an I32 — exactly what Interop.FloatAsInt(Half) should return.
            var srcIRType = value.Value.Resolve().Type;
            bool isSrcFloat16 = srcIRType is PrimitiveType ptSrc
                && ptSrc.BasicValueType == BasicValueType.Float16;
            if (isSrcFloat16)
            {
                var target = AllocateLocal(value, WasmOpCodes.I32);
                EmitGetLocal(value.Value.Resolve());
                EmitF32ToF16();
                WasmModuleBuilder.EmitLocalSet(Code, target);
                return;
            }
            base.GenerateCode(value);
        }

        public override void GenerateCode(IntAsFloatCast value)
        {
            // Reverse of FloatAsIntCast: when the target is Float16, the source I32 holds
            // a 16-bit f16 bit pattern. A plain F32ReinterpretI32 would treat all 32 bits
            // as an f32. EmitF16ToF32() properly expands the 16-bit pattern to f32.
            var dstIRType = value.Type;
            bool isDstFloat16 = dstIRType is PrimitiveType ptDst
                && ptDst.BasicValueType == BasicValueType.Float16;
            if (isDstFloat16)
            {
                var target = AllocateLocal(value, WasmOpCodes.F32);
                EmitGetLocal(value.Value.Resolve());
                EmitF16ToF32();
                WasmModuleBuilder.EmitLocalSet(Code, target);
                return;
            }
            base.GenerateCode(value);
        }

        public override void GenerateCode(SetField value)
        {
            // SetField modifies a field of a struct and returns the modified struct.
            // In our Wasm model, structs are memory addresses, so we write the field
            // value to memory at base + offset and return the base address.
            var objectValue = value.ObjectValue.Resolve();

            if (objectValue.Type is StructureType structType)
            {
                var target = AllocateLocal(value, WasmOpCodes.I32);
                int fieldIdx = (int)value.FieldSpan.Index;
                var fieldAccess = new FieldAccess(fieldIdx);
                int byteOffset = structType.GetOffset(fieldAccess);
                var fieldType = structType.Fields[fieldIdx];
                byte fieldWasmType = GetWasmTypeFromIR(fieldType);
                bool isFloat16Field = fieldType is PrimitiveType ptF16Sf
                    && ptF16Sf.BasicValueType == BasicValueType.Float16;

                // Store the new field value to memory: mem[base + offset] = newValue
                EmitGetLocal(objectValue); // push base address
                if (byteOffset > 0)
                {
                    WasmModuleBuilder.EmitI32Const(Code, byteOffset);
                    Code.Add(WasmOpCodes.I32Add);
                }
                EmitGetLocal(value.Value.Resolve()); // push new value

                // Float16: value is f32 on Wasm stack — convert to f16 bits, 2-byte store
                if (isFloat16Field)
                {
                    EmitF32ToF16();
                    if (_hasBarriers)
                        EmitVerifiedAtomicStore(16);
                    else
                        WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store16, 1, 0);
                }
                else if (_hasBarriers)
                {
                    EmitTypedStore(fieldWasmType);
                }
                else
                {
                    byte storeOp;
                    uint align;
                    switch (fieldWasmType)
                    {
                        case WasmOpCodes.I64:
                            storeOp = WasmOpCodes.I64Store;
                            align = 3;
                            break;
                        case WasmOpCodes.F32:
                            storeOp = WasmOpCodes.F32Store;
                            align = 2;
                            break;
                        case WasmOpCodes.F64:
                            storeOp = WasmOpCodes.F64Store;
                            align = 3;
                            break;
                        default:
                            storeOp = WasmOpCodes.I32Store;
                            align = 2;
                            break;
                    }
                    WasmModuleBuilder.EmitStore(Code, storeOp, align, 0);
                }

                // Return the base address (unchanged — struct was modified in-place)
                EmitGetLocal(objectValue);
                WasmModuleBuilder.EmitLocalSet(Code, target);
                return;
            }

            // Non-struct fallback: pass through the value
            var fallbackTarget = AllocateLocal(value, GetWasmType(value));
            EmitGetLocal(value.Value.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, fallbackTarget);
        }

        /// <summary>
        /// VERIFIED atomic store (Seven, 2026-06-10 - the Wasm residual large-sort race).
        /// Instrumented at instruction level (debug rings, 21/21 events): under CPU
        /// oversubscription an atomic store in a barrier kernel can SILENTLY FAIL TO
        /// LAND - the immediately following same-thread atomic read-back returns the
        /// previous value, and the slot stays stale (the classic event: the boundaries
        /// out-param copy pair, where the left field landed and the right field
        /// vanished, exactly the "window of consecutive stores vanishes" signature
        /// that produced the one-publication-behind carry corruption). The read-back
        /// DETECTED the miss in every instrumented event, so the defense is
        /// store -> read back -> retry until it sticks. Expects [addr, value(int)] on
        /// the stack like a plain atomic store; emits a bounded-in-practice retry loop
        /// (the store lands on the first try in the overwhelming majority of cases -
        /// one extra atomic load per store is the steady-state cost).
        /// width: 8, 16, 32, or 64 (sub-word compares mask the value accordingly).
        /// </summary>
        private void EmitVerifiedAtomicStore(int width)
        {
            bool is64 = width == 64;
            var valLocal = AllocateNewLocal(is64 ? WasmOpCodes.I64 : WasmOpCodes.I32);
            var addrLocal = AllocateNewLocal(WasmOpCodes.I32);
            WasmModuleBuilder.EmitLocalSet(Code, valLocal);
            WasmModuleBuilder.EmitLocalSet(Code, addrLocal);

            // Read-back via RMW(+0), NOT an atomic load: a plain load read-back can be
            // satisfied by store-forwarding (returning the own in-flight store) while
            // the store still never lands in memory - a lying verify. RMWs proved
            // truthful in every instrumented event (exact counters on all victims).
            (byte storeOp, byte rmwOp, uint align) = width switch
            {
                8 => (WasmOpCodes.I32AtomicStore8, WasmOpCodes.I32AtomicRmw8AddU, 0u),
                16 => (WasmOpCodes.I32AtomicStore16, WasmOpCodes.I32AtomicRmw16AddU, 1u),
                64 => (WasmOpCodes.I64AtomicStore, WasmOpCodes.I64AtomicRmwAdd, 3u),
                _ => (WasmOpCodes.I32AtomicStore, WasmOpCodes.I32AtomicRmwAdd, 2u),
            };

            Code.Add(WasmOpCodes.Loop);
            Code.Add(WasmOpCodes.Void);
            // store
            WasmModuleBuilder.EmitLocalGet(Code, addrLocal);
            WasmModuleBuilder.EmitLocalGet(Code, valLocal);
            WasmModuleBuilder.EmitAtomicRmw(Code, storeOp, align, 0);
            // read back (truthful RMW(+0))
            WasmModuleBuilder.EmitLocalGet(Code, addrLocal);
            if (is64)
                WasmModuleBuilder.EmitI64Const(Code, 0);
            else
                WasmModuleBuilder.EmitI32Const(Code, 0);
            WasmModuleBuilder.EmitAtomicRmw(Code, rmwOp, align, 0);
            // compare against the (masked) stored value
            WasmModuleBuilder.EmitLocalGet(Code, valLocal);
            if (width == 8)
            {
                WasmModuleBuilder.EmitI32Const(Code, 0xFF);
                Code.Add(WasmOpCodes.I32And);
            }
            else if (width == 16)
            {
                WasmModuleBuilder.EmitI32Const(Code, 0xFFFF);
                Code.Add(WasmOpCodes.I32And);
            }
            Code.Add(is64 ? WasmOpCodes.I64Ne : WasmOpCodes.I32Ne);
            Code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(Code, 0); // retry
            Code.Add(WasmOpCodes.End); // end loop
        }

        public override void GenerateCode(Store value)
        {
            var target = value.Target.Resolve();
            var storeValue = value.Value.Resolve();

            // Track Stores into kernel parameters so the dispatcher can skip
            // copy-OUT for input-only buffers (Wasm copy-OUT race fix). Helper
            // functions don't contribute — their parameter indices are local
            // and don't map back to the kernel signature.
            TrackParamWrite(target, "Store");

            // Global Float16 stores (e.g. ArrayView1D<Half>[idx] = …) must run before the
            // StructureType field-copy path. ILGPU can lower the stored half as a
            // StructureType wrapper; the field-copy path writes the wrong width.
            if (target.Type is AddressSpaceType addrF16Global
                && addrF16Global.ElementType is PrimitiveType ptF16Global
                && ptF16Global.BasicValueType == BasicValueType.Float16)
            {
                EmitGetLocal(target);
                EmitGetLocal(storeValue);
                EmitF32ToF16();
                if (_hasBarriers)
                    EmitVerifiedAtomicStore(16);
                else
                    WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store16, 1, 0);
                return;
            }

            // Fix B v4: DISABLED — #1's analysis shows barriers already separate Load/Store
            // phases. Each thread writes to a unique position (guaranteed by exclusive scan),
            // and struct Load scratch copies preserve data across barriers. The extra yield
            // per grid-stride iteration caused ~20K phases for 260K-element sorts (timeout).
            // Disabling to verify the intermittent failures are independent of Fix B.
            if (false && _phaseMode && !_isHelperFunction
                && storeValue.Type is StructureType
                && target.Type is AddressSpaceType targetAst
                && targetAst.AddressSpace == MemoryAddressSpace.Global)
            {
                _structStoreYieldsEmitted++;
                int continuationIndex = _currentBlockEmitIndex + 1;
                EmitSaveAllLocals();
                WasmModuleBuilder.EmitI32Const(Code, continuationIndex);
                WasmModuleBuilder.EmitLocalSet(Code, _stateLocal);
                {
                    int stateOffset = GetLocalSpillOffset(_stateLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                    WasmModuleBuilder.EmitI32Const(Code, stateOffset);
                    Code.Add(WasmOpCodes.I32Add);
                    WasmModuleBuilder.EmitLocalGet(Code, _stateLocal);
                    WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0);
                }
                WasmModuleBuilder.EmitI32Const(Code, 1);
                WasmModuleBuilder.EmitLocalSet(Code, _yieldedLocal);
                uint exitDepth = (uint)(_blockCount - _currentBlockEmitIndex);
                Code.Add(WasmOpCodes.Br);
                WasmModuleBuilder.EmitU32Leb128(Code, exitDepth);
                Code.Add(WasmOpCodes.End);
                _currentBlockEmitIndex = continuationIndex;
            }

            // For struct types, we need to copy each field from source to destination
            if (storeValue.Type is StructureType structType)
            {
                // ArrayView1D<Half>[idx] = (Half)expr lowers storeValue as a small StructureType
                // (not PrimitiveType Float16). The field-copy loop writes the wrong width.
                if (target.Type is AddressSpaceType addrHalfMis
                    && addrHalfMis.ElementType is PrimitiveType ptHalfMis
                    && ptHalfMis.BasicValueType == BasicValueType.Float16
                    && structType.Size <= 8)
                {
                    EmitGetLocal(target);
                    EmitGetLocal(storeValue);
                    if (GetWasmType(storeValue) == WasmOpCodes.F32)
                        EmitF32ToF16();
                    WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store16, 1, 0);
                    return;
                }

                int structSize = structType.Size;
                // Copy field by field
                for (int i = 0; i < structType.NumFields; i++)
                {
                    var fieldAccess = new FieldAccess(i);
                    int byteOffset = structType.GetOffset(fieldAccess);
                    var fieldType = structType.Fields[i];
                    // Float16 fields are 2 bytes in struct memory — use 2-byte ops
                    bool isFloat16Field = fieldType is PrimitiveType ptF16St
                        && ptF16St.BasicValueType == BasicValueType.Float16;
                    byte fieldWasmType = GetWasmTypeFromIR(fieldType);

                    byte loadOp, storeOp;
                    uint align;
                    if (isFloat16Field)
                    {
                        loadOp = WasmOpCodes.I32Load16U;
                        storeOp = WasmOpCodes.I32Store16;
                        align = 1;
                    }
                    else switch (fieldWasmType)
                    {
                        case WasmOpCodes.I64:
                            loadOp = WasmOpCodes.I64Load;
                            storeOp = WasmOpCodes.I64Store;
                            align = 3;
                            break;
                        case WasmOpCodes.F32:
                            loadOp = WasmOpCodes.F32Load;
                            storeOp = WasmOpCodes.F32Store;
                            align = 2;
                            break;
                        case WasmOpCodes.F64:
                            loadOp = WasmOpCodes.F64Load;
                            storeOp = WasmOpCodes.F64Store;
                            align = 3;
                            break;
                        default:
                            loadOp = WasmOpCodes.I32Load;
                            storeOp = WasmOpCodes.I32Store;
                            align = 2;
                            break;
                    }

                    // Push destination address + offset
                    EmitGetLocal(target);
                    if (byteOffset > 0)
                    {
                        WasmModuleBuilder.EmitI32Const(Code, byteOffset);
                        Code.Add(WasmOpCodes.I32Add);
                    }

                    // Load field value from source address + offset.
                    // If the source was loaded via struct Load snapshot, it points to
                    // scratch (safe copy), not the original array (which may be overwritten).
                    EmitGetLocal(storeValue);
                    if (byteOffset > 0)
                    {
                        WasmModuleBuilder.EmitI32Const(Code, byteOffset);
                        Code.Add(WasmOpCodes.I32Add);
                    }
                    // Load field value (atomic for barrier kernels). The barrier-kernel
                    // copy is a PURE INT MOVE (no float reinterpret round-trip needed -
                    // bit-exact either way) so the verified store can compare raw bits.
                    if (_hasBarriers)
                    {
                        if (isFloat16Field)
                        {
                            // Float16: 2-byte atomic load (raw f16 bits as i32)
                            WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad16U, 1, 0);
                        }
                        else switch (fieldWasmType)
                        {
                            case WasmOpCodes.I64:
                            case WasmOpCodes.F64:
                                WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I64AtomicLoad, 3, 0);
                                break;
                            default: // I32 / F32
                                WasmModuleBuilder.EmitAtomicRmw(Code, WasmOpCodes.I32AtomicLoad, 2, 0);
                                break;
                        }
                    }
                    else
                        WasmModuleBuilder.EmitLoad(Code, loadOp, align, 0);

                    // Store to destination. Barrier kernels use the VERIFIED atomic store
                    // (store -> read back -> retry) - the boundaries out-param field copy
                    // emitted here was THE instrumented vanishing-store site of the
                    // residual large-sort race (see EmitVerifiedAtomicStore).
                    if (_hasBarriers)
                    {
                        if (isFloat16Field)
                            EmitVerifiedAtomicStore(16);
                        else switch (fieldWasmType)
                        {
                            case WasmOpCodes.I64:
                            case WasmOpCodes.F64:
                                EmitVerifiedAtomicStore(64);
                                break;
                            default: // I32 / F32
                                EmitVerifiedAtomicStore(32);
                                break;
                        }
                    }
                    else
                        WasmModuleBuilder.EmitStore(Code, storeOp, align, 0);
                }
                return;
            }

            // Check if this is a Float16 store (f32 value → 2-byte memory)
            bool isFloat16Store = false;
            if (target.Type is AddressSpaceType addrTypeF16
                && addrTypeF16.ElementType is PrimitiveType ptF16
                && ptF16.BasicValueType == BasicValueType.Float16)
            {
                isFloat16Store = true;
            }

            if (isFloat16Store)
            {
                // Float16: convert f32 → f16 bits, store 2 bytes
                EmitGetLocal(target);
                EmitGetLocal(storeValue);
                EmitF32ToF16(); // inline conversion: f32 → i32 (f16 bits)
                if (_hasBarriers)
                    EmitVerifiedAtomicStore(16);
                else
                    WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store16, 1, 0);
                return;
            }

            // Check if this is an Int16 store (i32 value → 2-byte memory)
            bool isInt16Store = false;
            if (storeValue.Type is PrimitiveType ptI16Store
                && ptI16Store.BasicValueType == BasicValueType.Int16)
                isInt16Store = true;
            // Also check target pointer element type (kernel writes via pointer)
            if (!isInt16Store && target.Type is AddressSpaceType addrI16
                && addrI16.ElementType is PrimitiveType ptAddrI16
                && ptAddrI16.BasicValueType == BasicValueType.Int16)
                isInt16Store = true;

            if (isInt16Store)
            {
                // Int16: store lower 16 bits of i32 value as 2 bytes
                EmitGetLocal(target);
                EmitGetLocal(storeValue);
                if (_hasBarriers)
                    EmitVerifiedAtomicStore(16);
                else
                    WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store16, 1, 0);
                return;
            }

            // Check if this is an Int8 store (i32 value -> 1-byte memory)
            bool isInt8Store = false;
            if (storeValue.Type is PrimitiveType ptI8Store
                && ptI8Store.BasicValueType == BasicValueType.Int8)
                isInt8Store = true;
            if (!isInt8Store && target.Type is AddressSpaceType addrI8
                && addrI8.ElementType is PrimitiveType ptAddrI8
                && ptAddrI8.BasicValueType == BasicValueType.Int8)
                isInt8Store = true;

            if (isInt8Store)
            {
                // Int8: store lower 8 bits of i32 value as 1 byte
                EmitGetLocal(target);
                EmitGetLocal(storeValue);
                if (_hasBarriers)
                    EmitVerifiedAtomicStore(8);
                else
                    WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store8, 0, 0);
                return;
            }

            // Non-struct types: typed store.
            // Determine the actual types from the locals to handle width mismatches
            // (e.g., i64 value stored to i32 location via view.IntLength → debug[0]).
            var wasmType = GetWasmTypeFromIR(storeValue.Type);
            byte destType = wasmType;
            // Check target pointer's element type
            if (target.Type is AddressSpaceType addrType)
            {
                byte elemType = GetWasmTypeFromIR(addrType.ElementType);
                if (elemType != wasmType && elemType != 0)
                    destType = elemType;
            }
            // Also check the actual local type (more reliable than IR type)
            var storeLocalIdx = GetLocal(storeValue);
            byte actualType = GetLocalType(storeLocalIdx);

            EmitGetLocal(target);
            EmitGetLocalByIndex(storeLocalIdx);

            // Truncate i64 → i32 if storing to i32 memory
            if (actualType == WasmOpCodes.I64 && destType == WasmOpCodes.I32)
                Code.Add(WasmOpCodes.I32WrapI64);
            // Extend i32 → i64 if storing to i64 memory
            else if (actualType == WasmOpCodes.I32 && destType == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I64ExtendI32S);

            if (_hasBarriers)
            {
                // VERIFIED atomic store for ALL types in barrier kernels (the residual
                // large-sort race fix - see EmitVerifiedAtomicStore). Float types use
                // reinterpret: i32.reinterpret_f32 -> verified i32 store (or i64/f64).
                switch (destType)
                {
                    case WasmOpCodes.I64:
                        EmitVerifiedAtomicStore(64);
                        break;
                    case WasmOpCodes.F32:
                        Code.Add(WasmOpCodes.I32ReinterpretF32);
                        EmitVerifiedAtomicStore(32);
                        break;
                    case WasmOpCodes.F64:
                        Code.Add(WasmOpCodes.I64ReinterpretF64);
                        EmitVerifiedAtomicStore(64);
                        break;
                    default: // I32
                        EmitVerifiedAtomicStore(32);
                        break;
                }
            }
            else
            {
                // Non-barrier kernels: regular store
                byte sOp;
                uint sAlign;
                switch (destType)
                {
                    case WasmOpCodes.I64:
                        sOp = WasmOpCodes.I64Store;
                        sAlign = 3;
                        break;
                    case WasmOpCodes.F32:
                        sOp = WasmOpCodes.F32Store;
                        sAlign = 2;
                        break;
                    case WasmOpCodes.F64:
                        sOp = WasmOpCodes.F64Store;
                        sAlign = 3;
                        break;
                    default:
                        sOp = WasmOpCodes.I32Store;
                        sAlign = 2;
                        break;
                }
                WasmModuleBuilder.EmitStore(Code, sOp, sAlign, 0);
            }
        }

        public override void GenerateCode(StructureValue value)
        {
            // StructureValue constructs a struct from field values.
            // Allocate space in scratch memory, store each field, return the base address.
            var target = AllocateLocal(value, WasmOpCodes.I32);

            if (value.Type is StructureType structType && value.Count > 0)
            {
                // Compute this struct's base address in scratch memory
                int baseOffset = _scratchNextOffset;
                _scratchNextOffset += structType.Size;
                // Align to 8 bytes for next allocation
                _scratchNextOffset = (_scratchNextOffset + 7) & ~7;

                // Compute base address = scratchBaseLocal + baseOffset
                WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                if (baseOffset > 0)
                {
                    WasmModuleBuilder.EmitI32Const(Code, baseOffset);
                    Code.Add(WasmOpCodes.I32Add);
                }
                WasmModuleBuilder.EmitLocalSet(Code, target);

                // Store each field to memory at the correct offset
                for (int i = 0; i < value.Count && i < structType.NumFields; i++)
                {
                    var fieldAccess = new FieldAccess(i);
                    int fieldOffset = structType.GetOffset(fieldAccess);
                    var fieldType = structType.Fields[i];
                    // Float16 fields are 2 bytes in struct memory — use 2-byte store
                    bool isFloat16Field = fieldType is PrimitiveType ptF16Sf
                        && ptF16Sf.BasicValueType == BasicValueType.Float16;
                    byte fieldWasmType = GetWasmTypeFromIR(fieldType);

                    // Push destination address: target + fieldOffset
                    WasmModuleBuilder.EmitLocalGet(Code, target);
                    if (fieldOffset > 0)
                    {
                        WasmModuleBuilder.EmitI32Const(Code, fieldOffset);
                        Code.Add(WasmOpCodes.I32Add);
                    }

                    // Push field value
                    EmitGetLocal(value[i].Resolve());

                    // Float16: value is f32 on the Wasm stack — convert to f16 bits before 2-byte store
                    if (isFloat16Field)
                    {
                        EmitF32ToF16();
                        WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store16, 1, 0);
                    }
                    else
                    {
                        // Emit typed store
                        byte storeOp;
                        uint align;
                        switch (fieldWasmType)
                        {
                            case WasmOpCodes.I64:
                                storeOp = WasmOpCodes.I64Store;
                                align = 3;
                                break;
                            case WasmOpCodes.F32:
                                storeOp = WasmOpCodes.F32Store;
                                align = 2;
                                break;
                            case WasmOpCodes.F64:
                                storeOp = WasmOpCodes.F64Store;
                                align = 3;
                                break;
                            default:
                                storeOp = WasmOpCodes.I32Store;
                                align = 2;
                                break;
                        }
                        WasmModuleBuilder.EmitStore(Code, storeOp, align, 0);
                    }
                }
            }
            else
            {
                // Non-struct or empty: pass through first field or zero
                if (value.Count > 0)
                    EmitGetLocal(value[0].Resolve());
                else
                    WasmModuleBuilder.EmitI32Const(Code, 0);
                WasmModuleBuilder.EmitLocalSet(Code, target);
            }
        }

        #endregion

        #region Override: MethodCall (Helper Functions)

        /// <summary>
        /// Overrides MethodCall to handle helper functions:
        /// - Single-block helpers are inlined directly (fast, no state machine needed).
        /// - Multi-block helpers are emitted as separate Wasm functions and called via
        ///   the 'call' instruction. This avoids nested state machines which corrupt
        ///   control flow when both the kernel and helper have multiple blocks.
        /// </summary>
        public override void GenerateCode(MethodCall methodCall)
        {
            var targetMethod = methodCall.Target;

            // Registered helpers (same compilation unit). Cross-assembly struct accessors
            // (e.g. SpawnDev.ILGPU.ML.Tensors.TensorView.Get2D) stay as MethodCall in the
            // kernel IR but are not in HelperMethods — base.GenerateCode returns 0 for them.
            bool registeredHelper = _generatorArgs.HelperMethods.TryGetValue(targetMethod, out var helperAllocas);
            bool inlineCrossAssembly = !registeredHelper
                && targetMethod.HasImplementation
                && !targetMethod.HasFlags(global::ILGPU.IR.MethodFlags.External)
                && !targetMethod.HasFlags(global::ILGPU.IR.MethodFlags.Intrinsic);

            if (!registeredHelper && !inlineCrossAssembly)
            {
                base.GenerateCode(methodCall);
                return;
            }

            // Set up the helper's shared memory allocations (needed for both paths
            // so that the kernel's _sharedMemorySize accounts for the helper's allocas).
            // Only set up ONCE per unique helper — multiple calls to the same helper
            // share the same shared memory region.
            if (registeredHelper)
            {
                bool alreadySetup = _setupSharedHelpers.Contains(targetMethod);
                if (!alreadySetup)
                {
                    // Also check by name (generic specializations may differ by reference)
                    foreach (var m in _setupSharedHelpers)
                        if (m.Name == targetMethod.Name) { alreadySetup = true; break; }
                }
                if (!alreadySetup)
                {
                    _setupSharedHelpers.Add(targetMethod);
                    SetupSharedAllocations(helperAllocas.SharedAllocations, isDynamic: false);
                    SetupSharedAllocations(helperAllocas.DynamicSharedAllocations, isDynamic: true);
                }
            }

            // Allocate result local if non-void
            uint? resultLocal = null;
            if (!methodCall.Type.IsVoidType)
            {
                resultLocal = AllocateLocal(methodCall, GetWasmType(methodCall));
            }

            // Check if this is a multi-block helper with a pre-assigned function index.
            // Try direct reference lookup first, then name-based fallback
            // (generic specializations may create different Method instances).
            // Cross-assembly accessors always use the inline path below.
            bool foundHelper = false;
            int helperFuncIdx = 0;
            if (registeredHelper)
            {
                foundHelper = _generatorArgs.HelperFunctionIndices.TryGetValue(targetMethod, out helperFuncIdx);
                if (!foundHelper)
                {
                    foreach (var kv in _generatorArgs.HelperFunctionIndices)
                    {
                        if (kv.Key.Name == targetMethod.Name)
                        { helperFuncIdx = kv.Value; foundHelper = true; break; }
                    }
                }
            }
            if (foundHelper)
            {
                // Multi-block helper: emit function call.
                // Look up barrier count — use same key that found the function index.
                // The HelperBarrierCounts and HelperFunctionIndices are populated from the
                // same HelperMethods keys, so if one found a match, the other should too.
                int helperBarrierCount = 0;
                if (!_generatorArgs.HelperBarrierCounts.TryGetValue(targetMethod, out helperBarrierCount))
                {
                    // Name-based fallback: try exact name, then base name (before _NNN suffix)
                    foreach (var kv in _generatorArgs.HelperBarrierCounts)
                    {
                        if (kv.Key.Name == targetMethod.Name)
                        { helperBarrierCount = kv.Value; break; }
                    }
                    // If still not found, try matching by base name (strip numeric suffix)
                    if (helperBarrierCount == 0)
                    {
                        var baseName = System.Text.RegularExpressions.Regex.Replace(
                            targetMethod.Name, @"_\d+$", "");
                        foreach (var kv in _generatorArgs.HelperBarrierCounts)
                        {
                            var kvBaseName = System.Text.RegularExpressions.Regex.Replace(
                                kv.Key.Name, @"_\d+$", "");
                            if (kvBaseName == baseName)
                            { helperBarrierCount = kv.Value; break; }
                        }
                    }
                }

                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Call] helper='{targetMethod.Name}' funcIdx={helperFuncIdx}, barriers={helperBarrierCount}, phaseMode={_phaseMode}");

                // No barrier reset needed — generation-counting barriers require no
                // per-call reset. The generation counter monotonically increases, so
                // fresh function locals always read the correct current generation.

                if (_phaseMode && helperBarrierCount > 0)
                {
                    // Phase mode: yield-per-phase for ALL helper calls.
                    // Each helper phase must run across ALL threads before any thread advances
                    // to the next phase (barrier semantics). So when the helper yields, the
                    // kernel must also yield back to the worker script.
                    //
                    // Each helper barrier becomes a kernel yield point. For a helper with N
                    // barriers, we emit N yield points. Each yield point:
                    // 1. Calls helper with helperPhaseLocal
                    // 2. If helper yielded (returned 1): increment helperPhaseLocal, save
                    //    kernel state, yield kernel to worker
                    // 3. If helper done (returned 0): continue to next instruction
                    //
                    // The yield uses the same dynamic block splitting as EmitBarrier:
                    // save locals, set _stateLocal to continuation, br $exit.
                    // On re-entry, br_table dispatches back here to call the helper again.

                    var helperPhaseLocal = AllocateNewLocal(WasmOpCodes.I32);

                    // Allocate a local for this helper's scratch base address.
                    // Value is set in the prologue (after all locals are known) as:
                    // scratchBase + kernelFinalScratchEnd + cumulativeHelperOffset
                    var helperScratchBaseLocal = AllocateNewLocal(WasmOpCodes.I32);
                    int helperCumulativeForThisCall = _helperScratchCumulativeOffset;
                    // Record for prologue generation
                    _helperScratchBaseLocals.Add((helperScratchBaseLocal, helperCumulativeForThisCall));

                    // N barriers in helper = N+1 phases (N yields + 1 completion)
                    int helperPhaseCount = helperBarrierCount + 1;
                    for (int hb = 0; hb < helperPhaseCount; hb++)
                    {
                        // Push helper arguments
                        WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, _dimYLocal);
                        // Helper scratch base: computed in prologue as
                        // scratchBase + kernelFinalScratchEnd + cumulativeOffset
                        WasmModuleBuilder.EmitLocalGet(Code, helperScratchBaseLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, _groupDimXLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, _threadIdXLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, _sharedMemBaseLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, _barrierBaseLocal);
                        if (_barrierCounter > 0)
                        {
                            WasmModuleBuilder.EmitI32Const(Code, _barrierCounter * 8);
                            Code.Add(WasmOpCodes.I32Add);
                        }
                        WasmModuleBuilder.EmitLocalGet(Code, _dynamicSharedLengthLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, helperPhaseLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimYLocal);
                        for (int i = 0; i < targetMethod.Parameters.Count && i < methodCall.Nodes.Length; i++)
                            EmitGetLocal(methodCall.Nodes[i].Resolve());

                        WasmModuleBuilder.EmitCall(Code, (uint)helperFuncIdx);

                        // Option E: helper returns actual result. Store it.
                        // Yield flag is in scratch[0].
                        // Void helpers push nothing on the stack - no Drop needed.
                        // (Pre-fix: void MethodCall was silently skipped before reaching
                        // this dispatcher so the Drop never ran. After the void-MethodCall
                        // skip was lifted in WasmCodeGenerator.GenerateCodeFor, void helpers
                        // reach here and a stale Drop caused a Wasm stack underflow that
                        // showed up as RadixSort kernel hang / wrong output.)
                        if (resultLocal.HasValue)
                            WasmModuleBuilder.EmitLocalSet(Code, resultLocal.Value);

                        if (hb < helperPhaseCount - 1)
                        {
                            // Intermediate phase: helper yielded.
                            // Increment helperPhaseLocal and yield kernel.
                            WasmModuleBuilder.EmitLocalGet(Code, helperPhaseLocal);
                            WasmModuleBuilder.EmitI32Const(Code, 1);
                            Code.Add(WasmOpCodes.I32Add);
                            WasmModuleBuilder.EmitLocalSet(Code, helperPhaseLocal);

                            // Yield kernel — same pattern as EmitBarrier
                            int continuationIndex = _currentBlockEmitIndex + 1;

                            EmitSaveAllLocals();

                            WasmModuleBuilder.EmitI32Const(Code, continuationIndex);
                            WasmModuleBuilder.EmitLocalSet(Code, _stateLocal);
                            {
                                int stateOffset = GetLocalSpillOffset(_stateLocal);
                                WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                                WasmModuleBuilder.EmitI32Const(Code, stateOffset);
                                Code.Add(WasmOpCodes.I32Add);
                                WasmModuleBuilder.EmitLocalGet(Code, _stateLocal);
                                WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0);
                            }

                            WasmModuleBuilder.EmitI32Const(Code, 1);
                            WasmModuleBuilder.EmitLocalSet(Code, _yieldedLocal);

                            uint exitDepth = (uint)(_blockCount - _currentBlockEmitIndex);
                            Code.Add(WasmOpCodes.Br);
                            WasmModuleBuilder.EmitU32Leb128(Code, exitDepth);

                            // Close current block, open continuation
                            Code.Add(WasmOpCodes.End);
                            _currentBlockEmitIndex = continuationIndex;
                        }
                        else
                        {
                            // Final phase: helper is done. resultLocal has the valid result.
                            // Reset helperPhaseLocal for any future calls
                            WasmModuleBuilder.EmitI32Const(Code, 0);
                            WasmModuleBuilder.EmitLocalSet(Code, helperPhaseLocal);

                            // Sync yield: only needed when 2+ barrier-helper calls exist.
                            // Ensures ALL threads read the current scan's results before the
                            // next call overwrites the shared helper memory.
                            if (_needsSyncYields)
                            {
                                int syncContinuation = _currentBlockEmitIndex + 1;
                                EmitSaveAllLocals();
                                WasmModuleBuilder.EmitI32Const(Code, syncContinuation);
                                WasmModuleBuilder.EmitLocalSet(Code, _stateLocal);
                                int stateOffset2 = GetLocalSpillOffset(_stateLocal);
                                WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                                WasmModuleBuilder.EmitI32Const(Code, stateOffset2);
                                Code.Add(WasmOpCodes.I32Add);
                                WasmModuleBuilder.EmitLocalGet(Code, _stateLocal);
                                WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0);
                                WasmModuleBuilder.EmitI32Const(Code, 1);
                                WasmModuleBuilder.EmitLocalSet(Code, _yieldedLocal);
                                uint syncExitDepth = (uint)(_blockCount - _currentBlockEmitIndex);
                                Code.Add(WasmOpCodes.Br);
                                WasmModuleBuilder.EmitU32Leb128(Code, syncExitDepth);
                                Code.Add(WasmOpCodes.End);
                                _currentBlockEmitIndex = syncContinuation;
                            }
                        }
                    }

                    // Advance cumulative scratch offset for next helper call (8-byte aligned)
                    if (_generatorArgs.HelperScratchEstimates.TryGetValue(targetMethod, out int helperScratch))
                        _helperScratchCumulativeOffset += (helperScratch + 7) & ~7;
                }
                // (internal loop path removed — using yield-per-phase exclusively)
                else
                {
                    // Non-phase or no helper barriers: single call.
                    //
                    // TWO FIXES (2026-06-11, Seven - the RegBlocked "got 0"/garbage and
                    // FusedLinear scattered-error bugs):
                    // 1. SCRATCH: a phase-mode-compiled helper (helpers inherit phase mode
                    //    from the kernel's PhaseCount) writes its completion state into its
                    //    scratch region. Passing the KERNEL's scratch base here made the
                    //    helper CLOBBER THE KERNEL'S FIBER SPILL SLOTS on every call.
                    //    Give it a dedicated region (shared across call sites of the same
                    //    helper - a no-barrier helper keeps no cross-call state).
                    // 2. PHASE: passing the kernel's live phase param made the helper's
                    //    prologue run `if (phase > 0) restore-all-locals` from scratch it
                    //    never saved - garbage locals, garbage br_table dispatch, garbage
                    //    return. A no-barrier helper is a fresh full run every call: phase 0.
                    uint singleCallScratchLocal;
                    if (_singleCallHelperScratch.TryGetValue(targetMethod, out var scs))
                    {
                        singleCallScratchLocal = scs;
                    }
                    else
                    {
                        singleCallScratchLocal = AllocateNewLocal(WasmOpCodes.I32);
                        _helperScratchBaseLocals.Add((singleCallScratchLocal, _helperScratchCumulativeOffset));
                        if (_generatorArgs.HelperScratchEstimates.TryGetValue(targetMethod, out int singleHelperScratch))
                            _helperScratchCumulativeOffset += (singleHelperScratch + 7) & ~7;
                        else
                            _helperScratchCumulativeOffset += 256; // conservative minimum
                        _singleCallHelperScratch[targetMethod] = singleCallScratchLocal;
                    }

                    // Push context params
                    WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _dimYLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, singleCallScratchLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _groupDimXLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _threadIdXLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _sharedMemBaseLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _barrierBaseLocal);
                    if (_barrierCounter > 0)
                    {
                        WasmModuleBuilder.EmitI32Const(Code, _barrierCounter * 8);
                        Code.Add(WasmOpCodes.I32Add);
                    }
                    WasmModuleBuilder.EmitLocalGet(Code, _dynamicSharedLengthLocal);
                    WasmModuleBuilder.EmitI32Const(Code, 0); // phase 0: fresh run, no restore
                    WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                    WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimYLocal);
                    for (int i = 0; i < targetMethod.Parameters.Count && i < methodCall.Nodes.Length; i++)
                        EmitGetLocal(methodCall.Nodes[i].Resolve());

                    WasmModuleBuilder.EmitCall(Code, (uint)helperFuncIdx);

                    // Phase-mode helper return convention (Option E, see the state-machine
                    // epilogue ~line 1395): a NON-VOID helper returns its ACTUAL RESULT
                    // (the yield flag lives in scratch[0]); only VOID helpers return the
                    // yield flag i32. The old unconditional `if (_phaseMode) Drop` here
                    // DISCARDED a non-void no-barrier helper's real return value, leaving
                    // resultLocal at 0 - the "got 0" failures in phase-mode kernels calling
                    // non-void multi-block helpers (FusedFFN_RegBlocked{Tanh,Erf}GELU:
                    // the erf/tanh activation helpers). Fixed 2026-06-11 (Seven).
                    if (resultLocal.HasValue)
                        WasmModuleBuilder.EmitLocalSet(Code, resultLocal.Value);
                    else if (_phaseMode)
                        Code.Add(WasmOpCodes.Drop); // void phase helper returns yield-flag i32
                }

                // Advance barrier counter for subsequent barriers/calls
                _barrierCounter += helperBarrierCount;
                if (helperBarrierCount > 0)
                {
                    _hasBarriers = true;

                    if (!_phaseMode)
                    {
                        // Non-phase mode: emit traditional post-helper barrier
                        EmitBarrier(_barrierCounter);
                    }
                    // In phase mode: skip post-helper barrier — the fiber dispatch
                    // runs all fibers sequentially per phase, providing implicit sync.
                    // The helper's own barriers handle intra-helper sync via phase yields.
                    // Only increment for multi-helper sync (2+ helpers need inter-helper barriers).
                    // Single-helper kernels don't need the sync yield — the state machine
                    // already excludes it (line 1067-1068). Incrementing here without
                    // the matching state machine entry causes an off-by-one that puts the
                    // last barrier's phase ID outside the br_table range.
                    if (_needsSyncYields)
                        _barrierCounter++;
                }

                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Call] Done calling '{targetMethod.Name}', barrierCounter now={_barrierCounter}");
            }
            else
            {
                // Inline path: single-block or multi-block (nested SM fallback)
                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Inline] Inlining helper: {targetMethod.Name} ({targetMethod.Parameters.Count} params, {methodCall.Nodes.Length} args)");

                var savedInlineLocalMap = new List<(string ParamKey, bool HadEntry, uint PreviousLocal)>();

                _inlineParamAliases.Clear();
                for (int i = 0; i < targetMethod.Parameters.Count && i < methodCall.Nodes.Length; i++)
                {
                    if (targetMethod.Parameters[i] is global::ILGPU.IR.Values.Parameter hp)
                        _inlineParamAliases[hp] = methodCall.Nodes[i].Resolve();
                }

                // Map call arguments to helper's parameters (restore after inline so
                // helper param keys do not leak into the kernel's _localMap).
                for (int i = 0; i < targetMethod.Parameters.Count && i < methodCall.Nodes.Length; i++)
                {
                    var param = targetMethod.Parameters[i];
                    var arg = methodCall.Nodes[i].Resolve();
                    var paramKey = GetValueKey(param);

                    bool hadEntry = _localMap.TryGetValue(paramKey, out uint previousLocal);
                    savedInlineLocalMap.Add((paramKey, hadEntry, previousLocal));

                    if (_localMap.TryGetValue(GetValueKey(arg), out uint argLocal))
                    {
                        _localMap[paramKey] = argLocal;
                        if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Inline]   param[{i}] '{paramKey}' -> existing local_{argLocal}");
                    }
                    else
                    {
                        // Struct kernel params are i32 scratch addresses in Wasm.
                        byte paramWasmType = param.Type is StructureType
                            ? WasmOpCodes.I32
                            : GetWasmType(param);
                        var local = AllocateLocal(param, paramWasmType);
                        EmitGetLocal(arg);
                        WasmModuleBuilder.EmitLocalSet(Code, local);
                        _localMap[paramKey] = local;
                        if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Inline]   param[{i}] '{paramKey}' -> new local_{local} (copied)");
                    }
                }

                var blocks = targetMethod.Blocks;
                var blockList = blocks.ToList();

                if (blockList.Count <= 1)
                {
                    // Single block: visit inline (no state machine needed)
                    if (blockList.Count == 1)
                    {
                        var block = blockList[0];
                        foreach (var value in block)
                            GenerateCodeFor(value);

                        if (block.Terminator is ReturnTerminator ret && resultLocal.HasValue && !ret.IsVoidReturn)
                        {
                            EmitGetLocal(ret.ReturnValue.Resolve());
                            WasmModuleBuilder.EmitLocalSet(Code, resultLocal.Value);
                        }
                    }

                    foreach (var (paramKey, hadEntry, previousLocal) in savedInlineLocalMap)
                    {
                        if (hadEntry)
                            _localMap[paramKey] = previousLocal;
                        else
                            _localMap.Remove(paramKey);
                    }
                    _inlineParamAliases.Clear();
                }
                else
                {
                    // Multi-block: nested state machine (fallback for when function call is unavailable)
                    var savedBlockMap = new Dictionary<BasicBlock, int>(_blockMap);
                    var savedStateLocal = _stateLocal;
                    var savedBlockCount = _blockCount;
                    var savedIsStateMachine = _isStateMachine;
                    var savedCurrentBlockEmitIndex = _currentBlockEmitIndex;

                    _blockMap.Clear();
                    _isStateMachine = true;
                    _stateLocal = AllocateNewLocal(WasmOpCodes.I32);
                    int helperBlockCount = blockList.Count;
                    _blockCount = helperBlockCount;

                    int blockIndex = 0;
                    foreach (var block in blockList)
                        _blockMap[block] = blockIndex++;

                    WasmModuleBuilder.EmitI32Const(Code, 0);
                    WasmModuleBuilder.EmitLocalSet(Code, _stateLocal);

                    Code.Add(WasmOpCodes.Block); Code.Add(WasmOpCodes.Void);
                    Code.Add(WasmOpCodes.Loop); Code.Add(WasmOpCodes.Void);
                    for (int i = 0; i < helperBlockCount; i++)
                    {
                        Code.Add(WasmOpCodes.Block); Code.Add(WasmOpCodes.Void);
                    }

                    WasmModuleBuilder.EmitLocalGet(Code, _stateLocal);
                    Code.Add(WasmOpCodes.BrTable);
                    WasmModuleBuilder.EmitU32Leb128(Code, (uint)helperBlockCount);
                    for (int i = 0; i < helperBlockCount; i++)
                        WasmModuleBuilder.EmitU32Leb128(Code, (uint)i);
                    WasmModuleBuilder.EmitU32Leb128(Code, (uint)(helperBlockCount + 1));

                    int blockIdx = 0;
                    foreach (var block in blockList)
                    {
                        Code.Add(WasmOpCodes.End);
                        _currentBlockEmitIndex = blockIdx;

                        foreach (var value in block)
                            GenerateCodeFor(value);

                        if (block.Terminator is ReturnTerminator ret)
                        {
                            if (resultLocal.HasValue && !ret.IsVoidReturn)
                            {
                                EmitGetLocal(ret.ReturnValue.Resolve());
                                WasmModuleBuilder.EmitLocalSet(Code, resultLocal.Value);
                            }
                            Code.Add(WasmOpCodes.Br);
                            WasmModuleBuilder.EmitU32Leb128(Code, (uint)(helperBlockCount - blockIdx));
                        }
                        else if (block.Terminator != null)
                        {
                            GenerateCodeFor(block.Terminator);
                        }

                        blockIdx++;
                    }

                    Code.Add(WasmOpCodes.End);
                    Code.Add(WasmOpCodes.End);

                    _blockMap.Clear();
                    foreach (var kv in savedBlockMap)
                        _blockMap[kv.Key] = kv.Value;
                    _stateLocal = savedStateLocal;
                    _blockCount = savedBlockCount;
                    _isStateMachine = savedIsStateMachine;
                    _currentBlockEmitIndex = savedCurrentBlockEmitIndex;

                    foreach (var (paramKey, hadEntry, previousLocal) in savedInlineLocalMap)
                    {
                        if (hadEntry)
                            _localMap[paramKey] = previousLocal;
                        else
                            _localMap.Remove(paramKey);
                    }
                    _inlineParamAliases.Clear();
                }

                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Inline] Done inlining {targetMethod.Name}");
            }
        }

        /// <summary>
        /// Emits phi value assignments for a block (used during inlining).
        /// </summary>
        private void EmitPhiValues(BasicBlock block)
        {
            // Phi values are handled by PushPhiValues calls in branch terminators.
            // Nothing extra needed here.
        }

        #endregion

        #region Override: Device Constants

        public override void GenerateCode(GroupIndexValue value)
        {
            var target = AllocateLocal(value);
            // l = threadIdX (linear thread index within the workgroup). Decompose into the
            // per-dimension Group.Idx using the real per-dim group sizes (X-fastest, like CPU/CUDA).
            // For 1D groups realGroupDimX == groupSize and realGroupDimY == 1, so this reduces to
            // Group.IdxX = l, Group.IdxY = Group.IdxZ = 0.
            if (value.Dimension == DeviceConstantDimension3D.X)
            {
                // Group.IdxX = l % GroupDim.X
                WasmModuleBuilder.EmitLocalGet(Code, _threadIdXLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                Code.Add(WasmOpCodes.I32RemU);
            }
            else if (value.Dimension == DeviceConstantDimension3D.Y)
            {
                // Group.IdxY = (l / GroupDim.X) % GroupDim.Y
                WasmModuleBuilder.EmitLocalGet(Code, _threadIdXLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                Code.Add(WasmOpCodes.I32DivU);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimYLocal);
                Code.Add(WasmOpCodes.I32RemU);
            }
            else // Z
            {
                // Group.IdxZ = l / (GroupDim.X * GroupDim.Y)
                WasmModuleBuilder.EmitLocalGet(Code, _threadIdXLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimYLocal);
                Code.Add(WasmOpCodes.I32Mul);
                Code.Add(WasmOpCodes.I32DivU);
            }
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public override void GenerateCode(GridIndexValue value)
        {
            var target = AllocateLocal(value);
            // g = globalIdx / groupSize (linear group index; _groupDimXLocal carries the TOTAL group
            // size). Decompose into per-dimension Grid.Idx using gridDim per axis = dim / realGroupDim
            // (X-fastest). For 1D launches gridDimX = numGroups and gridDimY == 1.
            if (value.Dimension == DeviceConstantDimension3D.X)
            {
                // Grid.IdxX = g % gridDimX,  gridDimX = dimX / realGroupDimX
                WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _groupDimXLocal);   // groupSize
                Code.Add(WasmOpCodes.I32DivU); // g
                WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                Code.Add(WasmOpCodes.I32DivU); // gridDimX
                Code.Add(WasmOpCodes.I32RemU);
            }
            else if (value.Dimension == DeviceConstantDimension3D.Y)
            {
                // Grid.IdxY = (g / gridDimX) % gridDimY,  gridDimY = dimY / realGroupDimY
                WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _groupDimXLocal);
                Code.Add(WasmOpCodes.I32DivU); // g
                WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                Code.Add(WasmOpCodes.I32DivU); // gridDimX
                Code.Add(WasmOpCodes.I32DivU); // g / gridDimX
                WasmModuleBuilder.EmitLocalGet(Code, _dimYLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimYLocal);
                Code.Add(WasmOpCodes.I32DivU); // gridDimY
                Code.Add(WasmOpCodes.I32RemU);
            }
            else // Z
            {
                // Grid.IdxZ = g / (gridDimX * gridDimY)
                WasmModuleBuilder.EmitLocalGet(Code, _globalIdxLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _groupDimXLocal);
                Code.Add(WasmOpCodes.I32DivU); // g
                WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                Code.Add(WasmOpCodes.I32DivU); // gridDimX
                WasmModuleBuilder.EmitLocalGet(Code, _dimYLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimYLocal);
                Code.Add(WasmOpCodes.I32DivU); // gridDimY
                Code.Add(WasmOpCodes.I32Mul);  // gridDimX * gridDimY
                Code.Add(WasmOpCodes.I32DivU);
            }
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public override void GenerateCode(GroupDimensionValue value)
        {
            var target = AllocateLocal(value);
            // Per-dimension group size. X/Y are passed; Z = groupSize / (X * Y).
            if (value.Dimension == DeviceConstantDimension3D.X)
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
            else if (value.Dimension == DeviceConstantDimension3D.Y)
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimYLocal);
            else // Z = groupSize / (GroupDim.X * GroupDim.Y)
            {
                WasmModuleBuilder.EmitLocalGet(Code, _groupDimXLocal); // groupSize
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimYLocal);
                Code.Add(WasmOpCodes.I32Mul);
                Code.Add(WasmOpCodes.I32DivU);
            }
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public override void GenerateCode(GridDimensionValue value)
        {
            var target = AllocateLocal(value);
            // gridDim per axis = (total threads in axis) / (group size in axis).
            // dimX/dimY are totalThreads per axis (GridDim*GroupDim), passed by dispatch.
            if (value.Dimension == DeviceConstantDimension3D.X)
            {
                WasmModuleBuilder.EmitLocalGet(Code, _dimXLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimXLocal);
                Code.Add(WasmOpCodes.I32DivU);
            }
            else if (value.Dimension == DeviceConstantDimension3D.Y)
            {
                WasmModuleBuilder.EmitLocalGet(Code, _dimYLocal);
                WasmModuleBuilder.EmitLocalGet(Code, _realGroupDimYLocal);
                Code.Add(WasmOpCodes.I32DivU);
            }
            else // Z: no dimZ is plumbed (only dimX/dimY) → grid Z extent is 1
                WasmModuleBuilder.EmitI32Const(Code, 1);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        // Emulated warp/subgroup support. Wasm has no hardware warps; a "warp" is WasmWarpSize
        // consecutive lanes of the linear thread id. LaneIdx = threadIdX % WarpSize, and WarpIdx /
        // IsFirstLane / IsLastLane derive from LaneIdx + WarpSize in IL (Warp.cs). Warp.Shuffle is
        // emulated via shared memory + a barrier (see GenerateCode(WarpShuffle)). The base
        // WasmCodeGenerator emits the WarpSize=1 / LaneIdx=0 stubs; these overrides make them real.
        public override void GenerateCode(WarpSizeValue value)
        {
            var target = AllocateLocal(value);
            WasmModuleBuilder.EmitI32Const(Code, WasmBackend.WasmWarpSize);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public override void GenerateCode(LaneIdxValue value)
        {
            var target = AllocateLocal(value);
            // LaneIdx = threadIdX % WarpSize
            WasmModuleBuilder.EmitLocalGet(Code, _threadIdXLocal);
            WasmModuleBuilder.EmitI32Const(Code, WasmBackend.WasmWarpSize);
            Code.Add(WasmOpCodes.I32RemU);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public override void GenerateCode(DynamicMemoryLengthValue value)
        {
            // Dynamic shared memory length = element count (passed at dispatch time via kernel param 8)
            var target = AllocateLocal(value);
            WasmModuleBuilder.EmitLocalGet(Code, _dynamicSharedLengthLocal);
            WasmModuleBuilder.EmitLocalSet(Code, target);
            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-DynShared] DynamicMemoryLengthValue -> local_{target} = local_{_dynamicSharedLengthLocal}");
        }

        /// <summary>
        /// Handles ArrayView.Length: traces the view back to its kernel parameter
        /// and reads the pre-stored length local (locals[1]).
        /// This is the correct cross-backend approach — the length is already passed
        /// at dispatch time as a Wasm function parameter.
        /// </summary>
        public override void GenerateCode(GetViewLength value)
        {
            // GetViewLength returns i64 (long) in ILGPU, but our length local is i32.
            // We must extend i32 → i64 using I64ExtendI32S.
            var target = AllocateLocal(value, WasmOpCodes.I64);

            // Trace the view back to its kernel parameter.
            // For ArrayView<T> params, value.View.Resolve() IS the Parameter directly.
            // For ArrayView1D<T, TStride> params, the view goes through GetField
            // (to extract BaseView from the struct) — we must trace through the
            // chain of operations to find the original Parameter.
            var viewSource = value.View.Resolve();
            int paramIdx = TraceToParameter(viewSource);

            if (paramIdx >= 0 && _paramLocals.TryGetValue(paramIdx, out var locals) && locals.Length > 1)
            {
                // locals[1] is the element count (i32) for this view parameter.
                // Extend to i64 since GetViewLength returns long.
                WasmModuleBuilder.EmitLocalGet(Code, locals[1]);
                Code.Add(WasmOpCodes.I64ExtendI32S);
                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-GetViewLength] param[{paramIdx}] length -> local_{locals[1]} (i32 extended to i64)");
            }
            else
            {
                // Fallback: emit 0 as i64 (should not happen for well-formed kernels)
                WasmModuleBuilder.EmitI64Const(Code, 0);
                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-GetViewLength] WARN: could not resolve view to parameter (source={viewSource?.GetType().Name}), emitting 0L");
            }

            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        /// <summary>
        /// Traces an IR value back through GetField, NewView, AddressSpaceCast, etc.
        /// to find the underlying kernel Parameter. Returns the parameter index, or -1.
        /// This is needed because ArrayView1D params wrap ArrayView via GetField(BaseView),
        /// so the view doesn't directly resolve to the Parameter.
        ///
        /// IMPORTANT: this method is shared with <see cref="GenerateCode(GetViewLength)"/>.
        /// DO NOT add `LoadElementAddress` / `SubViewValue` / similar pointer-arithmetic
        /// cases here — they break length-resolution for sub-views. Use
        /// <see cref="TraceWriteTargetToParameter"/> for tracking Store/Atomic write
        /// targets where pointer-walking is correct.
        /// </summary>
        /// <summary>
        /// Cross-assembly MethodCall inlining can bind callee <c>this</c> to a
        /// <see cref="Load"/> of a struct snapshot instead of the kernel parameter.
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

        private int TraceToParameter(Value source)
        {
            var current = PeelStructAccessRoot(source);
            int depth = 0;
            while (current != null && depth < 20)
            {
                if (current is global::ILGPU.IR.Values.Parameter p)
                {
                    if (_inlineParamAliases.TryGetValue(p, out var aliasedRoot))
                    {
                        current = aliasedRoot.Resolve();
                        depth++;
                        continue;
                    }

                    for (int pi = 0; pi < Method.Parameters.Count; pi++)
                    {
                        if (Method.Parameters[pi] == current) return pi;
                    }
                    return -1;
                }

                if (current is GetField gf)
                    current = gf.ObjectValue.Resolve();
                else if (current is global::ILGPU.IR.Values.Load load)
                    current = load.Source.Resolve();
                else if (current is ConvertValue cv)
                    current = cv.Value.Resolve();
                else if (current is NewView nv)
                    current = nv.Pointer.Resolve();
                else if (current is AddressSpaceCast asc)
                    current = asc.Value.Resolve();
                else if (current is PointerCast pc)
                    current = pc.Value.Resolve();
                else
                    break;

                depth++;
            }
            return -1;
        }

        /// <summary>
        /// Traces a Store/Atomic write target back to its underlying kernel Parameter.
        /// Handles all the indirections a write target goes through (LoadElementAddress
        /// for indexed buffer writes, LoadFieldAddress for struct field writes,
        /// SubViewValue for writes through a sub-view) — strictly more permissive than
        /// <see cref="TraceToParameter"/>, which is constrained to view-resolution
        /// patterns where pointer-arithmetic-walking would break length lookups.
        /// </summary>
        private int TraceWriteTargetToParameter(Value source)
        {
            var current = PeelStructAccessRoot(source);
            int depth = 0;
            while (current != null && depth < 20)
            {
                if (current is global::ILGPU.IR.Values.Parameter)
                {
                    for (int pi = 0; pi < Method.Parameters.Count; pi++)
                    {
                        if (Method.Parameters[pi] == current) return pi;
                    }
                    return -1;
                }

                if (current is GetField gf)
                    current = gf.ObjectValue.Resolve();
                else if (current is NewView nv)
                    current = nv.Pointer.Resolve();
                else if (current is AddressSpaceCast asc)
                    current = asc.Value.Resolve();
                else if (current is PointerCast pc)
                    current = pc.Value.Resolve();
                else if (current is LoadElementAddress lea)
                    current = lea.Source.Resolve();
                else if (current is LoadFieldAddress lfa)
                    current = lfa.Source.Resolve();
                else if (current is SubViewValue sv)
                    current = sv.Source.Resolve();
                // NOTE: Load (loading a pointer-typed value from memory) intentionally
                // NOT walked. A Store's target is always an address, never a Loaded
                // value — and over-walking through Load chains makes the trace resolve
                // to non-buffer parameters (e.g. the implicit Index1D), polluting
                // `WrittenParamIndices` with non-buffer indices and causing the
                // dispatcher's gate to skip-out the wrong buffer (RadixSort100K
                // regression at index 32).
                else
                    break;

                depth++;
            }
            return -1;
        }

        /// <summary>
        /// Set of kernel-parameter indices the kernel WRITES to. Populated by the
        /// codegen via <see cref="TrackParamWrite"/> calls from every IR write handler
        /// (<see cref="GenerateCode(Store)"/>, atomic RMW, atomic CAS). Consumed by
        /// <see cref="WasmAccelerator"/> to skip copy-OUT for buffers the kernel didn't
        /// modify — required correctness fix for the 2026-05-03 Wasm copy-OUT race
        /// where an input-only paramsBuf reused across overlapping dispatches had the
        /// unconditional copy-OUT clobber host CopyFromCPU writes with stale
        /// wasmMemory bytes (see _DevComms/SpawnDev.ILGPU/geordi-to-team-wasm-copy-out-race-2026-05-03.md).
        /// </summary>
        internal readonly HashSet<int> WrittenParamIndices = new();

        /// <summary>
        /// Diagnostic record of every write the codegen visits in the kernel body.
        /// Format per entry: "WriteKind TargetType -> param[N]". Used to confirm the
        /// trace is catching every write target shape ILGPU emits.
        /// </summary>
        internal readonly List<string> StoreTargetTrace = new();

        /// <summary>
        /// Records that the kernel writes to <paramref name="target"/>. If the target
        /// resolves to a kernel Parameter via <see cref="TraceWriteTargetToParameter"/>
        /// (the write-tracking-specific trace that walks LEA / SubViewValue / Load
        /// indirections in addition to the view-shape cases), the parameter's index
        /// is added to <see cref="WrittenParamIndices"/>. Helper functions don't
        /// contribute — their Method.Parameters are local to the helper and don't
        /// map back to the dispatch kernel's parameter list.
        /// </summary>
        private void TrackParamWrite(Value target, string kind)
        {
            if (_isHelperFunction) return;
            int paramIdx = TraceWriteTargetToParameter(target);
            StoreTargetTrace.Add($"{kind} {target?.GetType().Name ?? "null"} -> param[{paramIdx}]");
            // IR param 0 is always the implicit Index — kernels never WRITE to it.
            // If the trace lands there, it walked through too far (a Load chain or
            // similar that reached a non-buffer parameter). Drop those — feeding
            // false positives into the dispatcher's gate causes copy-OUT to skip
            // the WRONG buffer (the user's actual output gets dropped because the
            // gate thinks the kernel "wrote" to a different param).
            if (paramIdx > 0)
                WrittenParamIndices.Add(paramIdx);
        }

        /// <inheritdoc/>
        public override void GenerateCode(GenericAtomic value)
        {
            // Atomic RMW writes to its Target (LEA, NewView etc.) — same trace path
            // as Store. Without tracking these, the copy-OUT skip path would mark
            // RadixSort's histogram buffers as input-only and zero them on copy-OUT.
            TrackParamWrite(value.Target.Resolve(), "GenericAtomic");
            base.GenerateCode(value);
        }

        /// <inheritdoc/>
        public override void GenerateCode(AtomicCAS value)
        {
            TrackParamWrite(value.Target.Resolve(), "AtomicCAS");
            base.GenerateCode(value);
        }

        #endregion

        #region Shared Memory

        /// <summary>
        /// Sets up shared memory allocations by computing byte offsets within the shared memory region.
        /// Each allocation gets a fixed offset from sharedMemBase that the Alloca handler will use.
        /// </summary>
        private void SetupSharedAllocations(AllocaKindInformation allocas, bool isDynamic)
        {
            foreach (var allocaInfo in allocas)
            {
                var key = GetValueKey(allocaInfo.Alloca);

                // Skip if already registered — prevents shared memory inflation
                // when the same helper is called multiple times.
                if (_sharedAllocaOffsets.ContainsKey(key))
                    continue;

                int elemSize = 4; // default i32
                string elemTypeStr = "i32";

                // Determine element size from the alloca type
                if (allocaInfo.Alloca.Type is AddressSpaceType addrType)
                {
                    elemTypeStr = addrType.ElementType.ToString();
                    if (addrType.ElementType is PrimitiveType pt)
                        elemSize = GetElementSize(pt.BasicValueType);
                    else if (addrType.ElementType is StructureType st)
                        elemSize = st.Size;
                }

                int arraySize = allocaInfo.IsArray ? allocaInfo.ArraySize : 1;

                if (isDynamic)
                {
                    // Dynamic shared memory: size determined at runtime
                    // Placed at the end of static shared allocations
                    _sharedAllocaOffsets[key] = _sharedMemorySize;
                    _sharedAllocaMetadata[key] = (elemTypeStr, arraySize);
                    _dynamicSharedElementSize = elemSize;
                    _hasBarriers = true;
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SharedMem] Dynamic alloca {key}: offset={_sharedMemorySize}, elemSize={elemSize}");
                    // Don't advance _sharedMemorySize — dynamic size is not known at compile time
                }
                else if (allocaInfo.IsArray)
                {
                    int arrayBytes = allocaInfo.ArraySize * elemSize;
                    // Align to 4 bytes
                    arrayBytes = (arrayBytes + 3) & ~3;
                    _sharedAllocaOffsets[key] = _sharedMemorySize;
                    _sharedAllocaMetadata[key] = (elemTypeStr, arraySize);
                    _sharedMemorySize += arrayBytes;
                    _hasBarriers = true;
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SharedMem] Static array alloca {key}: offset={_sharedMemorySize - arrayBytes}, size={arrayBytes}, arrayLen={allocaInfo.ArraySize}, elemSize={elemSize}");
                }
                else
                {
                    // Single scalar shared
                    int scalarBytes = (elemSize + 3) & ~3; // align to 4
                    _sharedAllocaOffsets[key] = _sharedMemorySize;
                    _sharedAllocaMetadata[key] = (elemTypeStr, arraySize);
                    _sharedMemorySize += scalarBytes;
                    _hasBarriers = true;
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SharedMem] Scalar alloca {key}: offset={_sharedMemorySize - scalarBytes}, size={scalarBytes}");
                }
            }
        }

        /// <summary>
        /// Override Alloca for shared memory: emit address = sharedMemBase + offset
        /// </summary>
        public override void GenerateCode(Alloca value)
        {
            var key = GetValueKey(value);
            if (!_sharedAllocaOffsets.TryGetValue(key, out int offset))
            {
                // Primary key miss — try fallback matching by element type + array size.
                // The Alloca IR node from GenerateCode may be a different instance
                // from the one registered during SetupSharedAllocations().
                // Try fallback matching regardless of address space — the IR optimizer
                // may have changed Shared to Generic, but the alloca still maps to shared memory.
                {
                    string allocaElemType = value.AllocaType.ToString();
                    long allocaArrayLen = value.ArrayLength.Resolve() is global::ILGPU.IR.Values.PrimitiveValue pv
                        ? pv.Int64Value : -1;

                    string? matchedKey = null;
                    // Try exact match (type + size)
                    foreach (var kvp in _sharedAllocaMetadata)
                    {
                        if (kvp.Value.ElemType == allocaElemType && kvp.Value.ArraySize == allocaArrayLen)
                        {
                            matchedKey = kvp.Key;
                            break;
                        }
                    }
                    // Try type-only match if exact fails
                    if (matchedKey == null)
                    {
                        foreach (var kvp in _sharedAllocaMetadata)
                        {
                            if (kvp.Value.ElemType == allocaElemType)
                            {
                                matchedKey = kvp.Key;
                                break;
                            }
                        }
                    }

                    if (matchedKey != null && _sharedAllocaOffsets.TryGetValue(matchedKey, out offset))
                    {
                        if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SharedMem] Alloca {key}: fallback match to {matchedKey} (type={allocaElemType}, size={allocaArrayLen}, addrSpace={value.AddressSpace})");
                    }
                    else if (value.AddressSpace != MemoryAddressSpace.Shared)
                    {
                    // Not shared memory — local alloca.
                    // Allocate in scratch memory (NOT address 0, which would corrupt
                    // the data buffer region — see Wasm/CLAUDE.md RADIX RULE).
                    int allocSize = 8; // default
                    if (value.Type is AddressSpaceType localAddrType)
                    {
                        if (localAddrType.ElementType is PrimitiveType localPt)
                            allocSize = GetElementSize(localPt.BasicValueType);
                        else if (localAddrType.ElementType is StructureType localSt)
                            allocSize = localSt.Size;
                    }
                    if (value.ArrayLength.Resolve() is global::ILGPU.IR.Values.PrimitiveValue localPv)
                        allocSize *= localPv.Int32Value;

                    int baseOff = _scratchNextOffset;
                    _scratchNextOffset += allocSize;
                    _scratchNextOffset = (_scratchNextOffset + 7) & ~7;

                    var localTarget = AllocateLocal(value, WasmOpCodes.I32);
                    WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                    if (baseOff > 0)
                    {
                        WasmModuleBuilder.EmitI32Const(Code, baseOff);
                        Code.Add(WasmOpCodes.I32Add);
                    }
                    WasmModuleBuilder.EmitLocalSet(Code, localTarget);
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Alloca] Local alloca: key={key}, size={allocSize}, scratchOffset={baseOff}");
                    return;
                    }
                    else
                    {
                        // Shared address space but no matching entry
                        if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SharedMem] WARNING: Alloca {key} has no matching shared entry");
                        base.GenerateCode(value);
                        return;
                    }
                }
            }

            // Shared memory allocation: compute address in linear memory
            var target = AllocateLocal(value);
            WasmModuleBuilder.EmitLocalGet(Code, _sharedMemBaseLocal);
            if (offset > 0)
            {
                WasmModuleBuilder.EmitI32Const(Code, offset);
                Code.Add(WasmOpCodes.I32Add);
            }
            WasmModuleBuilder.EmitLocalSet(Code, target);
            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SharedMem] Alloca {key}: sharedMemBase + {offset}");
        }

        #endregion

        #region Atomic Memory Access for Multi-Worker

        /// <summary>
        /// Override to use atomic loads in barrier kernels for cross-worker visibility.
        /// Float types use i32/i64 atomic load + reinterpret.
        /// </summary>
        protected override void EmitTypedLoad(byte wasmType)
        {
            if (!_hasBarriers) { base.EmitTypedLoad(wasmType); return; }
            switch (wasmType)
            {
                case WasmOpCodes.I32:
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad);
                    Code.Add(0x02); Code.Add(0x00);
                    break;
                case WasmOpCodes.I64:
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I64AtomicLoad);
                    Code.Add(0x03); Code.Add(0x00);
                    break;
                case WasmOpCodes.F32:
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad);
                    Code.Add(0x02); Code.Add(0x00);
                    Code.Add(WasmOpCodes.F32ReinterpretI32);
                    break;
                case WasmOpCodes.F64:
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I64AtomicLoad);
                    Code.Add(0x03); Code.Add(0x00);
                    Code.Add(WasmOpCodes.F64ReinterpretI64);
                    break;
                default:
                    base.EmitTypedLoad(wasmType);
                    break;
            }
        }

        /// <summary>
        /// Override to use atomic stores in barrier kernels for cross-worker visibility.
        /// Float types use reinterpret + i32/i64 atomic store.
        /// </summary>
        protected override void EmitTypedStore(byte wasmType)
        {
            if (!_hasBarriers) { base.EmitTypedStore(wasmType); return; }
            // Barrier kernels: VERIFIED atomic store (residual large-sort race fix -
            // see EmitVerifiedAtomicStore).
            switch (wasmType)
            {
                case WasmOpCodes.I32:
                    EmitVerifiedAtomicStore(32);
                    break;
                case WasmOpCodes.I64:
                    EmitVerifiedAtomicStore(64);
                    break;
                case WasmOpCodes.F32:
                    Code.Add(WasmOpCodes.I32ReinterpretF32);
                    EmitVerifiedAtomicStore(32);
                    break;
                case WasmOpCodes.F64:
                    Code.Add(WasmOpCodes.I64ReinterpretF64);
                    EmitVerifiedAtomicStore(64);
                    break;
                default:
                    base.EmitTypedStore(wasmType);
                    break;
            }
        }

        #endregion

        #region Barrier Synchronization

        /// <summary>
        /// Emits a sense-reversing barrier using Wasm atomic instructions.
        /// Each barrier uses 8 bytes at barrierBase + (barrierIdx * 8):
        ///   - offset +0: arrival counter (i32)
        ///   - offset +4: sense flag (i32)
        /// 
        /// Algorithm:
        ///   old = i32.atomic.rmw.add(barrierAddr, 1)
        ///   if (old + 1 == groupDimX):   // last thread
        ///     i32.atomic.store(barrierAddr, 0)   // reset counter
        ///     new_sense = 1 - i32.atomic.load(senseAddr)
        ///     i32.atomic.store(senseAddr, new_sense)
        ///     memory.atomic.notify(senseAddr, MAX)
        ///   else:
        ///     sense = i32.atomic.load(senseAddr)
        ///     memory.atomic.wait32(senseAddr, sense, -1)  // wait until sense flips
        /// </summary>
        public override void GenerateCode(global::ILGPU.IR.Values.Barrier barrier)
        {
            int barrierIdx = _barrierCounter++;
            _hasBarriers = true;
            EmitBarrier(barrierIdx);
        }

        /// <summary>
        /// Counter for broadcast shared memory slots.
        /// </summary>
        private int _broadcastCounter = 0;

        /// <summary>
        /// Override for Broadcast: emulates Group.Broadcast using shared memory + atomic barriers.
        /// Pattern:
        ///   1. Origin thread stores its value to a dedicated shared memory slot
        ///   2. Barrier (all threads sync)
        ///   3. All threads load from the shared slot
        ///   4. Barrier (prevent origin from overwriting before others finish reading)
        /// </summary>
        public override void GenerateCode(Broadcast broadcast)
        {
            var wasmType = GetWasmType(broadcast);
            var target = AllocateLocal(broadcast, wasmType);
            var source = broadcast.Variable.Resolve();
            var origin = broadcast.Origin.Resolve();

            // Allocate a value slot and a tag slot in shared memory for this broadcast.
            // The tag is a MONOTONIC PER-EXECUTION SEQUENCE (2026-06-10, Seven): each fiber
            // keeps a per-site iteration counter in a local (uniform control flow keeps all
            // fibers in lockstep), the origin publishes value THEN tag=seq, and readers spin
            // until tag == their own seq. This makes freshness LOCALLY VERIFIABLE for every
            // execution: a stale slot (one publication behind) can never pass the guard, it
            // just makes the reader wait out the propagation. The previous tag (= group index)
            // was VACUOUS within a group - constant across a kernel's repeated broadcasts to
            // the same slot (e.g. the scan tile-carry loop) and equal to the zero-initialized
            // slot for group 0 - which silently exposed the one-publication-behind residual
            // at the JS->wasm re-entry boundary. The spin needs no yield escape: the origin's
            // store always completes a full dispatcher phase BEFORE any reader spins (barrier1
            // separates them), so the wait is bounded by memory propagation, not scheduling.
            // Cross-group: the dispatcher zeroes the tag slot between groups and the seq local
            // restarts at 1 with fresh locals, so group N+1's first spin still verifies.
            int broadcastSlotOffset = _sharedMemorySize;
            int slotSize = wasmType switch
            {
                WasmOpCodes.I64 => 8,
                WasmOpCodes.F64 => 8,
                _ => 4
            };
            int valueSlotSize = (slotSize + 3) & ~3; // align to 4
            int tagSlotOffset = broadcastSlotOffset + valueSlotSize; // i32 tag
            _sharedMemorySize += valueSlotSize + 4;
            _hasBarriers = true;

            // Determine store/load ops and alignment for this type
            byte storeOp, loadOp;
            uint align;
            switch (wasmType)
            {
                case WasmOpCodes.I64:
                    storeOp = WasmOpCodes.I64Store;
                    loadOp = WasmOpCodes.I64Load;
                    align = 3;
                    break;
                case WasmOpCodes.F32:
                    storeOp = WasmOpCodes.F32Store;
                    loadOp = WasmOpCodes.F32Load;
                    align = 2;
                    break;
                case WasmOpCodes.F64:
                    storeOp = WasmOpCodes.F64Store;
                    loadOp = WasmOpCodes.F64Load;
                    align = 3;
                    break;
                default: // I32
                    storeOp = WasmOpCodes.I32Store;
                    loadOp = WasmOpCodes.I32Load;
                    align = 2;
                    break;
            }

            // Per-site monotonic sequence: seq += 1 on EVERY execution, ALL fibers.
            // Uniform control flow (required by the barriers below) keeps every fiber's
            // counter identical; the local is saved/restored across yields with all locals,
            // and resets to 0 with fresh locals at each new group (phase 0, no restore).
            var seqLocal = AllocateNewLocal(WasmOpCodes.I32);
            WasmModuleBuilder.EmitLocalGet(Code, seqLocal);
            WasmModuleBuilder.EmitI32Const(Code, 1);
            Code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(Code, seqLocal);

            // Step 1: if (threadIdX == origin) { mem[sharedMemBase + offset] = source }
            WasmModuleBuilder.EmitLocalGet(Code, _threadIdXLocal);
            EmitGetLocal(origin);
            Code.Add(WasmOpCodes.I32Eq);
            Code.Add(WasmOpCodes.If);
            Code.Add(WasmOpCodes.Void);

            // Store: address = sharedMemBase + broadcastSlotOffset
            WasmModuleBuilder.EmitLocalGet(Code, _sharedMemBaseLocal);
            if (broadcastSlotOffset > 0)
            {
                WasmModuleBuilder.EmitI32Const(Code, broadcastSlotOffset);
                Code.Add(WasmOpCodes.I32Add);
            }
            // Atomic store for multi-worker visibility
            EmitGetLocal(source);
            if (_hasBarriers)
            {
                // Convert to atomic store (reinterpret floats to ints)
                switch (wasmType)
                {
                    case WasmOpCodes.F32:
                        Code.Add(WasmOpCodes.I32ReinterpretF32);
                        EmitVerifiedAtomicStore(32); // residual race fix
                        break;
                    case WasmOpCodes.F64:
                        Code.Add(WasmOpCodes.I64ReinterpretF64);
                        EmitVerifiedAtomicStore(64); // residual race fix
                        break;
                    case WasmOpCodes.I64:
                        EmitVerifiedAtomicStore(64); // residual race fix
                        break;
                    default: // I32
                        EmitVerifiedAtomicStore(32); // residual race fix
                        break;
                }
            }
            else
                WasmModuleBuilder.EmitStore(Code, storeOp, align, 0);

            // Publish tag = seq AFTER the value store (program order is load-bearing:
            // a reader that observes tag == seq via seq_cst load is guaranteed to see
            // the value store that seq_cst-precedes it). Readers verify before loading.
            WasmModuleBuilder.EmitLocalGet(Code, _sharedMemBaseLocal);
            if (tagSlotOffset > 0)
            {
                WasmModuleBuilder.EmitI32Const(Code, tagSlotOffset);
                Code.Add(WasmOpCodes.I32Add);
            }
            WasmModuleBuilder.EmitLocalGet(Code, seqLocal);
            EmitVerifiedAtomicStore(32); // residual race fix

            Code.Add(WasmOpCodes.End); // end if

            // Step 2: Barrier — ensure the origin's store is visible to all threads
            int barrier1 = _barrierCounter++;
            EmitBarrier(barrier1);

            // Step 3: All threads spin until tag == THIS execution's seq, then load value.
            // A slot still holding the previous publication (tag = seq-1) can no longer
            // pass; the reader waits out the propagation instead of silently consuming
            // stale data. Bounded wait: the origin's store completed a full dispatcher
            // phase ago (barrier1), so this only ever waits on memory visibility.
            Code.Add(WasmOpCodes.Block);
            Code.Add(WasmOpCodes.Void);
            Code.Add(WasmOpCodes.Loop);
            Code.Add(WasmOpCodes.Void);
            WasmModuleBuilder.EmitLocalGet(Code, _sharedMemBaseLocal);
            if (tagSlotOffset > 0)
            {
                WasmModuleBuilder.EmitI32Const(Code, tagSlotOffset);
                Code.Add(WasmOpCodes.I32Add);
            }
            Code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad);
            Code.Add(0x02); Code.Add(0x00);
            WasmModuleBuilder.EmitLocalGet(Code, seqLocal);
            Code.Add(WasmOpCodes.I32Eq);
            Code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(Code, 1); // break out of spin loop
            Code.Add(WasmOpCodes.Br);
            WasmModuleBuilder.EmitU32Leb128(Code, 0); // continue spin
            Code.Add(WasmOpCodes.End); // end loop
            Code.Add(WasmOpCodes.End); // end block

            // Load from the shared slot (atomic for multi-worker)
            WasmModuleBuilder.EmitLocalGet(Code, _sharedMemBaseLocal);
            if (broadcastSlotOffset > 0)
            {
                WasmModuleBuilder.EmitI32Const(Code, broadcastSlotOffset);
                Code.Add(WasmOpCodes.I32Add);
            }
            if (_hasBarriers)
            {
                // Atomic load (reinterpret for floats)
                switch (wasmType)
                {
                    case WasmOpCodes.F32:
                        Code.Add(WasmOpCodes.AtomicPrefix);
                        WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad);
                        Code.Add(0x02); Code.Add(0x00);
                        Code.Add(WasmOpCodes.F32ReinterpretI32);
                        break;
                    case WasmOpCodes.F64:
                        Code.Add(WasmOpCodes.AtomicPrefix);
                        WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I64AtomicLoad);
                        Code.Add(0x03); Code.Add(0x00);
                        Code.Add(WasmOpCodes.F64ReinterpretI64);
                        break;
                    case WasmOpCodes.I64:
                        Code.Add(WasmOpCodes.AtomicPrefix);
                        WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I64AtomicLoad);
                        Code.Add(0x03); Code.Add(0x00);
                        break;
                    default: // I32
                        Code.Add(WasmOpCodes.AtomicPrefix);
                        WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad);
                        Code.Add(0x02); Code.Add(0x00);
                        break;
                }
            }
            else
                WasmModuleBuilder.EmitLoad(Code, loadOp, align, 0);
            WasmModuleBuilder.EmitLocalSet(Code, target);

            // Step 4: Barrier — prevent broadcast slot reuse before all threads have read
            int barrier2 = _barrierCounter++;
            EmitBarrier(barrier2);

            _broadcastCounter++;
            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Broadcast] Broadcast #{_broadcastCounter - 1}: slot offset={broadcastSlotOffset}, barriers={barrier1},{barrier2}");
        }

        // Real Warp.Shuffle / SubWarpShuffle emulation (Wasm has no hardware warps). Mirrors the
        // Broadcast emulation + the WebGPU shared-memory shuffle: EVERY lane writes its value into a
        // per-lane shared slot (indexed by the linear thread id), a barrier publishes all writes, then
        // each lane reads the slot of (warpBase + sourceLane). 'Origin' is already the resolved absolute
        // source lane (the frontend lowers Down/Up/Xor into it — see ILGPU Warp.cs), so we don't branch
        // on ShuffleKind; we mask it into the warp (origin % WarpSize) for bounds-safety. Two barriers
        // (publish + reuse-guard) — counted as 2 in the barrier scans, same as Broadcast.
        public override void GenerateCode(WarpShuffle shuffle) => EmitWarpShuffle(shuffle);
        public override void GenerateCode(SubWarpShuffle shuffle) => EmitWarpShuffle(shuffle);

        private void EmitWarpShuffle(ShuffleOperation shuffle)
        {
            var wasmType = GetWasmType(shuffle);
            var target = AllocateLocal(shuffle, wasmType);
            var source = shuffle.Variable.Resolve();
            var origin = shuffle.Origin.Resolve();

            const int maxLanes = 256; // Wasm MaxNumThreadsPerGroup; slot per lane of the linear thread id
            int slotSize = wasmType switch
            {
                WasmOpCodes.I64 => 8,
                WasmOpCodes.F64 => 8,
                _ => 4
            };
            int valueSlotSize = (slotSize + 3) & ~3;
            int slotBase = _sharedMemorySize;
            _sharedMemorySize += maxLanes * valueSlotSize;
            _hasBarriers = true;

            int warpSize = WasmBackend.WasmWarpSize;

            // ── Step 1: shared[slotBase + threadIdX*valueSlotSize] = source (every lane, atomic) ──
            WasmModuleBuilder.EmitLocalGet(Code, _sharedMemBaseLocal);
            if (slotBase > 0)
            {
                WasmModuleBuilder.EmitI32Const(Code, slotBase);
                Code.Add(WasmOpCodes.I32Add);
            }
            WasmModuleBuilder.EmitLocalGet(Code, _threadIdXLocal);
            WasmModuleBuilder.EmitI32Const(Code, valueSlotSize);
            Code.Add(WasmOpCodes.I32Mul);
            Code.Add(WasmOpCodes.I32Add); // addr = sharedMemBase + slotBase + threadIdX*valueSlotSize
            EmitGetLocal(source);
            switch (wasmType)
            {
                case WasmOpCodes.F32:
                    Code.Add(WasmOpCodes.I32ReinterpretF32);
                    EmitVerifiedAtomicStore(32); // residual race fix
                    break;
                case WasmOpCodes.F64:
                    Code.Add(WasmOpCodes.I64ReinterpretF64);
                    EmitVerifiedAtomicStore(64); // residual race fix
                    break;
                case WasmOpCodes.I64:
                    EmitVerifiedAtomicStore(64); // residual race fix
                    break;
                default: // I32
                    EmitVerifiedAtomicStore(32); // residual race fix
                    break;
            }

            // ── Step 2: publish barrier ──
            int barrier1 = _barrierCounter++;
            EmitBarrier(barrier1);

            // ── Step 3: srcLane = (threadIdX / warpSize)*warpSize + (origin % warpSize); read its slot ──
            var srcLaneLocal = AllocateNewLocal(WasmOpCodes.I32);
            WasmModuleBuilder.EmitLocalGet(Code, _threadIdXLocal);
            WasmModuleBuilder.EmitI32Const(Code, warpSize);
            Code.Add(WasmOpCodes.I32DivU); // WarpIdx
            WasmModuleBuilder.EmitI32Const(Code, warpSize);
            Code.Add(WasmOpCodes.I32Mul);  // warpBase
            EmitGetLocal(origin);
            WasmModuleBuilder.EmitI32Const(Code, warpSize);
            Code.Add(WasmOpCodes.I32RemU); // origin % warpSize (in-warp, bounds-safe)
            Code.Add(WasmOpCodes.I32Add);  // warpBase + maskedLane
            WasmModuleBuilder.EmitLocalSet(Code, srcLaneLocal);

            WasmModuleBuilder.EmitLocalGet(Code, _sharedMemBaseLocal);
            if (slotBase > 0)
            {
                WasmModuleBuilder.EmitI32Const(Code, slotBase);
                Code.Add(WasmOpCodes.I32Add);
            }
            WasmModuleBuilder.EmitLocalGet(Code, srcLaneLocal);
            WasmModuleBuilder.EmitI32Const(Code, valueSlotSize);
            Code.Add(WasmOpCodes.I32Mul);
            Code.Add(WasmOpCodes.I32Add); // addr = sharedMemBase + slotBase + srcLane*valueSlotSize
            switch (wasmType)
            {
                case WasmOpCodes.F32:
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad);
                    Code.Add(0x02); Code.Add(0x00);
                    Code.Add(WasmOpCodes.F32ReinterpretI32);
                    break;
                case WasmOpCodes.F64:
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I64AtomicLoad);
                    Code.Add(0x03); Code.Add(0x00);
                    Code.Add(WasmOpCodes.F64ReinterpretI64);
                    break;
                case WasmOpCodes.I64:
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I64AtomicLoad);
                    Code.Add(0x03); Code.Add(0x00);
                    break;
                default: // I32
                    Code.Add(WasmOpCodes.AtomicPrefix);
                    WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad);
                    Code.Add(0x02); Code.Add(0x00);
                    break;
            }
            WasmModuleBuilder.EmitLocalSet(Code, target);

            // ── Step 4: reuse-guard barrier (prevent a later shuffle overwriting before all reads) ──
            int barrier2 = _barrierCounter++;
            EmitBarrier(barrier2);
        }

        /// <summary>
        /// Emits code to save all non-parameter locals to per-thread scratch memory.
        /// Used at barrier points in phase mode to preserve state across phase calls.
        /// Layout: scratchBase + _phaseStateOffset, one slot per local.
        /// </summary>
        /// <summary>
        /// Records that a full-locals SAVE block must be inserted at the current code
        /// position. The block itself is emitted later (see <see cref="_saveAllLocalsInsertPoints"/>
        /// and the deferred-insert pass at the end of body generation) so it captures the
        /// FINAL local count — exactly mirroring the deferred restore prologue. Emitting
        /// nothing inline is safe: a save leaves the Wasm value stack empty, so the
        /// subsequent yield bookkeeping (continuation store, br) is unaffected, and the
        /// block lands in front of it when inserted. Wasm uses structured control flow
        /// (br depth, not byte offsets), so later byte insertion does not disturb branches.
        /// </summary>
        private void EmitSaveAllLocals()
        {
            _saveAllLocalsInsertPoints.Add(Code.Count);
        }

        /// <summary>
        /// Emits the actual save-all-locals byte sequence into <paramref name="target"/>.
        /// Mirror of <see cref="EmitRestoreAllLocalsTo"/> — same <c>_locals</c> list, same
        /// type-stepped offsets from <see cref="_phaseStateOffset"/> — so save and restore
        /// are symmetric by construction.
        /// </summary>
        private void EmitSaveAllLocalsTo(List<byte> target)
        {
            // Plain spills, ALL locals, plain state-slot writes at yields - the PROVEN
            // 0/360-gate shape. Two falsified variants (2026-06-11, Seven), do not retry:
            // - VERIFIED state-slot writes + plain state spill = write-write inversion
            //   (the delayed plain spill lands AFTER the verified write -> fiber resumes
            //   at the OLD continuation): bisected to 4/120 corrupt @ 48w.
            // - Fully-VERIFIED spills (every local): correct but 2.7x at production
            //   configs - rejected on perf. The rare in-browser late-spill tail
            //   (~1/1000 dispatches in bundled Chromium) is tracked for a
            //   liveness-based spill reduction, which would make verified spills
            //   affordable.
            int offset = _phaseStateOffset;
            for (int i = 0; i < _locals.Count; i++)
            {
                uint localIdx = (uint)(_paramCount + i);
                byte type = _locals[i].Type;
                WasmModuleBuilder.EmitLocalGet(target, _scratchBaseLocal);
                WasmModuleBuilder.EmitI32Const(target, offset);
                target.Add(WasmOpCodes.I32Add);
                WasmModuleBuilder.EmitLocalGet(target, localIdx);
                switch (type)
                {
                    case WasmOpCodes.I32: WasmModuleBuilder.EmitStore(target, WasmOpCodes.I32Store, 2, 0); offset += 4; break;
                    case WasmOpCodes.I64: WasmModuleBuilder.EmitStore(target, WasmOpCodes.I64Store, 3, 0); offset += 8; break;
                    case WasmOpCodes.F32: WasmModuleBuilder.EmitStore(target, WasmOpCodes.F32Store, 2, 0); offset += 4; break;
                    case WasmOpCodes.F64: WasmModuleBuilder.EmitStore(target, WasmOpCodes.F64Store, 3, 0); offset += 8; break;
                }
            }
        }

        /// <summary>
        /// Emits code to restore all non-parameter locals from per-thread scratch memory.
        /// Used at phase entry (phaseId > 0) to restore state from previous phase.
        /// </summary>
        private void EmitRestoreAllLocals() => EmitRestoreAllLocalsTo(Code);

        private void EmitRestoreAllLocalsTo(List<byte> target)
        {
            int offset = _phaseStateOffset;
            for (int i = 0; i < _locals.Count; i++)
            {
                uint localIdx = (uint)(_paramCount + i);
                byte type = _locals[i].Type;

                // Address = scratchBase + offset
                WasmModuleBuilder.EmitLocalGet(target, _scratchBaseLocal);
                WasmModuleBuilder.EmitI32Const(target, offset);
                target.Add(WasmOpCodes.I32Add);

                // Load based on type
                switch (type)
                {
                    case WasmOpCodes.I32:
                        WasmModuleBuilder.EmitLoad(target, WasmOpCodes.I32Load, 2, 0);
                        offset += 4;
                        break;
                    case WasmOpCodes.I64:
                        WasmModuleBuilder.EmitLoad(target, WasmOpCodes.I64Load, 3, 0);
                        offset += 8;
                        break;
                    case WasmOpCodes.F32:
                        WasmModuleBuilder.EmitLoad(target, WasmOpCodes.F32Load, 2, 0);
                        offset += 4;
                        break;
                    case WasmOpCodes.F64:
                        WasmModuleBuilder.EmitLoad(target, WasmOpCodes.F64Load, 3, 0);
                        offset += 8;
                        break;
                }

                // Set the local
                WasmModuleBuilder.EmitLocalSet(target, localIdx);
            }
        }

        /// <summary>
        /// Computes the total scratch bytes needed for phase state spilling.
        /// </summary>
        private int ComputePhaseStateSize()
        {
            int size = 0;
            for (int i = 0; i < _locals.Count; i++)
            {
                switch (_locals[i].Type)
                {
                    case WasmOpCodes.I32:
                    case WasmOpCodes.F32:
                        size += 4;
                        break;
                    case WasmOpCodes.I64:
                    case WasmOpCodes.F64:
                        size += 8;
                        break;
                }
            }
            return size;
        }

        public override void GenerateCode(MemoryBarrier barrier)
        {
            // MemoryBarrier (fence) — emit atomic.fence or just a barrier
            // For Wasm with shared memory, we use the same sense-reversing barrier
            // since all threads need to synchronize memory visibility
            // However, MemoryBarrier is typically just a fence, not a full barrier
            // For now, emit atomic.fence (0x03) 
            Code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(Code, 0x03); // atomic.fence
            Code.Add(0x00); // memory index 0
        }

        /// <summary>
        /// Emits a generation-counting barrier using Wasm atomic instructions.
        ///
        /// Each barrier uses 8 bytes at barrierBase + (barrierIdx * 8):
        ///   - offset +0: arrival counter (i32)
        ///   - offset +4: generation counter (i32)
        ///
        /// No per-thread local state is needed. The generation counter monotonically
        /// increases. Each invocation reads the current generation, waits for it to
        /// increment. This is safe for:
        ///   - Loop re-entry (generation keeps increasing)
        ///   - Cross-function calls (no stale per-thread sense locals)
        ///   - Multi-group dispatch (no reset needed between groups)
        ///
        /// Algorithm:
        ///   atomic.fence                                    // flush prior stores
        ///   myGen = i32.atomic.load(genAddr)                // read current gen
        ///   old = i32.atomic.rmw.add(arrivalAddr, 1)
        ///   if (old + 1 == groupDimX):                      // last thread
        ///     i32.atomic.store(arrivalAddr, 0)              // reset counter
        ///     i32.atomic.rmw.add(genAddr, 1)                // bump generation
        ///     memory.atomic.notify(genAddr, MAX)            // wake all waiters
        ///   else:
        ///     loop:                                         // spin-wait
        ///       curGen = i32.atomic.load(genAddr)
        ///       if (curGen != myGen) break                  // generation advanced
        ///       memory.atomic.wait32(genAddr, curGen, -1)   // block until change
        ///   atomic.fence                                    // acquire: see others' stores
        /// </summary>
        private void EmitBarrier(int barrierIdx)
        {
            if (_phaseMode)
            {
                // Phase mode: check if this is the barrier we should yield at.
                // _barrierCounter tracks which barrier we're at. _phaseParamLocal
                // indicates which phase we're running. If phase == barrierIndex,
                // this is the yield point. Otherwise, skip (re-execution is safe
                // because locals are restored and execution is sequential).
                //
                // For the KERNEL (not helper), we use dynamic block splitting
                // which is more efficient. For HELPERS, we use this skip approach
                // because dynamic block splitting conflicts with if/else nesting.

                {
                    // Dynamic block splitting (used for both kernel and helper):
                    int continuationIndex = _currentBlockEmitIndex + 1;

                    EmitSaveAllLocals();

                    WasmModuleBuilder.EmitI32Const(Code, continuationIndex);
                    WasmModuleBuilder.EmitLocalSet(Code, _stateLocal);

                    {
                        int stateOffset = GetLocalSpillOffset(_stateLocal);
                        WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                        WasmModuleBuilder.EmitI32Const(Code, stateOffset);
                        Code.Add(WasmOpCodes.I32Add);
                        WasmModuleBuilder.EmitLocalGet(Code, _stateLocal);
                        WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0);
                    }

                    WasmModuleBuilder.EmitI32Const(Code, 1);
                    WasmModuleBuilder.EmitLocalSet(Code, _yieldedLocal);

                    // Option E: helpers write yield flag to scratch[0]
                    if (_isHelperFunction)
                    {
                        WasmModuleBuilder.EmitLocalGet(Code, _scratchBaseLocal);
                        WasmModuleBuilder.EmitI32Const(Code, 1); // yielded
                        WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0);
                    }

                    uint exitDepth = (uint)(_blockCount - _currentBlockEmitIndex);
                    Code.Add(WasmOpCodes.Br);
                    WasmModuleBuilder.EmitU32Leb128(Code, exitDepth);

                    Code.Add(WasmOpCodes.End);
                    _currentBlockEmitIndex = continuationIndex;
                }

                return; // Don't emit the wait32 barrier code
            }

            int byteOffset = barrierIdx * 8;

            var arrivalAddrLocal = AllocateNewLocal(WasmOpCodes.I32);
            var genAddrLocal = AllocateNewLocal(WasmOpCodes.I32);
            var myGenLocal = AllocateNewLocal(WasmOpCodes.I32);
            var arrivedLocal = AllocateNewLocal(WasmOpCodes.I32);

            // === Step 0: Fence — flush all prior non-atomic stores ===
            Code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(Code, 0x03); // atomic.fence
            Code.Add(0x00);

            // === Step 1: Compute barrier addresses ===
            // arrivalAddr = barrierBase + byteOffset
            WasmModuleBuilder.EmitLocalGet(Code, _barrierBaseLocal);
            WasmModuleBuilder.EmitI32Const(Code, byteOffset);
            Code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(Code, arrivalAddrLocal);

            // genAddr = barrierBase + byteOffset + 4
            WasmModuleBuilder.EmitLocalGet(Code, _barrierBaseLocal);
            WasmModuleBuilder.EmitI32Const(Code, byteOffset + 4);
            Code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(Code, genAddrLocal);

            // === Step 2: Read current generation ===
            // myGen = i32.atomic.load(genAddr)
            WasmModuleBuilder.EmitLocalGet(Code, genAddrLocal);
            Code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad);
            Code.Add(0x02);
            Code.Add(0x00);
            WasmModuleBuilder.EmitLocalSet(Code, myGenLocal);

            // === Step 3: Atomically increment arrival counter ===
            // arrived = i32.atomic.rmw.add(arrivalAddr, 1) + 1
            WasmModuleBuilder.EmitLocalGet(Code, arrivalAddrLocal);
            WasmModuleBuilder.EmitI32Const(Code, 1);
            Code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicRmwAdd);
            Code.Add(0x02);
            Code.Add(0x00);
            WasmModuleBuilder.EmitI32Const(Code, 1);
            Code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(Code, arrivedLocal);

            // === Step 4: Branch on last thread ===
            WasmModuleBuilder.EmitLocalGet(Code, arrivedLocal);
            WasmModuleBuilder.EmitLocalGet(Code, _groupDimXLocal);
            Code.Add(WasmOpCodes.I32Eq);

            Code.Add(WasmOpCodes.If);
            Code.Add(WasmOpCodes.Void);

            // === LAST THREAD: reset counter, bump generation, notify ===

            // i32.atomic.store(arrivalAddr, 0)
            WasmModuleBuilder.EmitLocalGet(Code, arrivalAddrLocal);
            WasmModuleBuilder.EmitI32Const(Code, 0);
            EmitVerifiedAtomicStore(32); // residual race fix

            // i32.atomic.rmw.add(genAddr, 1) — bump generation. No notify:
            // V8 14.7+ has a confirmed FutexEmulation race for memory.atomic.notify
            // (g-issues.chromium.org/issues/490434403). The pure-spin path below
            // exits on value-mismatch fast-check, so notify is unnecessary.
            // History: rc.7 used wait32+notify; rc.8 attempted variants; rc.25 ported
            // the dispatcher to pure-spin + JS-side Atomics.wait yield. rc.27
            // removes the last wait32+notify call from the in-kernel EmitBarrier
            // path so the entire Wasm backend is wait/notify-free.
            WasmModuleBuilder.EmitLocalGet(Code, genAddrLocal);
            WasmModuleBuilder.EmitI32Const(Code, 1);
            Code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicRmwAdd);
            Code.Add(0x02);
            Code.Add(0x00);
            Code.Add(WasmOpCodes.Drop); // discard old value

            Code.Add(WasmOpCodes.Else);

            // === NOT LAST: pure spin-wait until generation advances ===
            // No memory.atomic.wait32: V8 14.7+ FutexEmulation race makes the
            // wasm-side wait/notify path unreliable (reproduced 2026-05-24 — see
            // Plans/wasm-waitnotify-still-races-2026-05-24.md). Pure spin works
            // correctly across all engines. Outer dispatcher's spin-yield + JS
            // Atomics.wait mechanism handles OS-level descheduling under CPU
            // oversubscription.
            //
            // NOTE: WasmBackend.UseWaitNotifyBarriers only re-enables wait/notify on
            // the DISPATCHER phase/group barriers (WasmBackend.cs), which is the path
            // phase-mode kernels (RadixSort/Scan/Reduce) actually take. This in-kernel
            // EmitBarrier path is bypassed in phase mode, so it stays pure-spin
            // regardless of the flag.
            Code.Add(WasmOpCodes.Loop);
            Code.Add(WasmOpCodes.Void);

            // curGen = i32.atomic.load(genAddr)
            var curGenLocal = AllocateNewLocal(WasmOpCodes.I32);
            WasmModuleBuilder.EmitLocalGet(Code, genAddrLocal);
            Code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(Code, WasmOpCodes.I32AtomicLoad);
            Code.Add(0x02);
            Code.Add(0x00);
            WasmModuleBuilder.EmitLocalSet(Code, curGenLocal);

            // if (curGen == myGen) br $spin — keep spinning
            WasmModuleBuilder.EmitLocalGet(Code, curGenLocal);
            WasmModuleBuilder.EmitLocalGet(Code, myGenLocal);
            Code.Add(WasmOpCodes.I32Eq);
            Code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(Code, 0); // br_if $spin (continue loop)

            Code.Add(WasmOpCodes.End); // end loop $spin
            Code.Add(WasmOpCodes.End); // end if/else

            // === Step 5: Fence — acquire ===
            Code.Add(WasmOpCodes.AtomicPrefix);
            WasmModuleBuilder.EmitU32Leb128(Code, 0x03); // atomic.fence
            Code.Add(0x00);

            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Barrier] Emitted generation barrier #{barrierIdx} at byteOffset={byteOffset}");
        }

        #endregion

        #region Helpers

        private sealed class BodyStructFieldInfoWasm
        {
            public int FieldIndex { get; set; }
            public bool IsView { get; set; }
            public bool IsViewMetadata { get; set; }
            public bool IsScalar { get; set; }
        }

        private static bool IsViewFieldType(TypeNode type)
        {
            if (type is AddressSpaceType)
                return true;
            if (type is StructureType st
                && st.NumFields > 0
                && (st.Fields[0] is AddressSpaceType
                    || st.Fields[0].ToString().Contains("View", StringComparison.Ordinal)))
                return true;
            return type.ToString().Contains("View", StringComparison.Ordinal);
        }

        private void ScanBodyStructParams()
        {
            _bodyStructFields.Clear();
            foreach (var param in Method.Parameters)
            {
                if (param.ParameterType is not StructureType st || !IsBodyStruct(st))
                    continue;

                var fields = new List<BodyStructFieldInfoWasm>(st.NumFields);
                int viewMetadataPhase = 0;
                for (int fi = 0; fi < st.NumFields; fi++)
                {
                    var fieldType = st.Fields[fi];
                    bool isView = IsViewFieldType(fieldType);
                    bool isViewMetadata = false;
                    if (isView)
                        viewMetadataPhase = 1;
                    else if (viewMetadataPhase == 1)
                    {
                        if (fieldType.ToString().Contains("Int64", StringComparison.Ordinal))
                        {
                            isViewMetadata = true;
                            viewMetadataPhase = 2;
                        }
                        else
                            viewMetadataPhase = 0;
                    }
                    else if (viewMetadataPhase == 2)
                    {
                        if (fieldType.ToString().Contains("Int8", StringComparison.Ordinal))
                        {
                            isViewMetadata = true;
                            viewMetadataPhase = 0;
                        }
                        else
                            viewMetadataPhase = 0;
                    }

                    fields.Add(new BodyStructFieldInfoWasm
                    {
                        FieldIndex = fi,
                        IsView = isView,
                        IsViewMetadata = isViewMetadata,
                        IsScalar = !isView && !isViewMetadata && fieldType is PrimitiveType,
                    });
                }

                _bodyStructFields[param.Index] = fields;
            }
        }

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

            if (viewFieldCount == 0)
                return false;
            if (viewFieldCount >= 2)
                return true;

            if (st.NumFields > 0 && IsViewFieldType(st.Fields[0]))
            {
                if (st.NumFields == 3 || st.NumFields == 4 || st.NumFields == 6)
                    return false;
            }

            return true;
        }

        private bool TryResolveBodyStructField(
            GetField value,
            out global::ILGPU.IR.Values.Parameter bodyParam,
            out BodyStructFieldInfoWasm info,
            out int chainDepth)
        {
            bodyParam = null!;
            info = null!;
            chainDepth = 0;
            if (!TryFindBodyStructRoot(value, out bodyParam, out info, out chainDepth))
                return false;
            return info.IsView;
        }

        private bool TryFindBodyStructRoot(
            GetField value,
            out global::ILGPU.IR.Values.Parameter bodyParam,
            out BodyStructFieldInfoWasm rootFieldInfo,
            out int chainDepth)
        {
            bodyParam = null!;
            rootFieldInfo = null!;
            chainDepth = 0;

            Value? current = value;
            int outerFi = -1;
            int depth = 0;
            while (current != null && depth < 20)
            {
                depth++;
                if (current is not GetField gf)
                    return false;

                outerFi = gf.FieldSpan.Index;
                current = PeelStructAccessRoot(gf.ObjectValue);
                if (current is global::ILGPU.IR.Values.Parameter param)
                {
                    if (_inlineParamAliases.TryGetValue(param, out var aliasedRoot))
                    {
                        current = aliasedRoot.Resolve();
                        continue;
                    }

                    if (param.ParameterType is not StructureType st || !IsBodyStruct(st))
                        return false;
                    if (!_bodyStructFields.TryGetValue(param.Index, out var fields))
                        return false;
                    if (outerFi < 0 || outerFi >= fields.Count)
                        return false;

                    bodyParam = param;
                    rootFieldInfo = fields[outerFi];
                    chainDepth = depth;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Recursively flattens an IR StructureType into its leaf fields.
        /// ILGPU's StructureType already stores flattened fields (no nested structs),
        /// so we can enumerate them directly.
        /// </summary>
        private void FlattenStructLayout(StructureType sType, int baseOffset, List<StructFieldInfo> result)
        {
            for (int i = 0; i < sType.NumFields; i++)
            {
                var fieldType = sType[i]; // Already a leaf type (flattened by ILGPU)
                var fieldOffset = sType.GetOffset(new FieldAccess(i));
                var wasmType = GetWasmTypeFromIR(fieldType);

                result.Add(new StructFieldInfo
                {
                    Offset = baseOffset + fieldOffset,
                    WasmType = wasmType,
                    Size = fieldType.Size,
                    IsViewPtr = fieldType is AddressSpaceType,
                });
            }
        }

        /// <summary>
        /// Conservative check that <paramref name="type"/> matches the shape of an
        /// implicit Index parameter for the given <paramref name="entryIndexType"/>:
        /// PrimitiveType (Int32/Int64) for Index1D / KernelConfig, or a StructureType
        /// containing exactly N Int32 leaf fields where N matches the index dimensionality
        /// (2 for Index2D, 3 for Index3D). Anything else — particularly multi-view body
        /// structs — returns false so the dispatcher knows the first user parameter is
        /// not the implicit index. (rc.16 RegisterHeavyBody explicit-launch fix, 2026-05-05.)
        /// </summary>
        private static bool LooksLikeIndexType(TypeNode type, IndexType entryIndexType)
        {
            // Index1D / LongIndex1D / KernelConfig (Index1D variant): primitive i32 or i64.
            if (type is PrimitiveType pt)
            {
                return pt.BasicValueType == BasicValueType.Int32
                    || pt.BasicValueType == BasicValueType.Int64;
            }
            // Index2D / Index3D: StructureType with exactly N Int32 fields.
            if (type is StructureType st)
            {
                int expectedFields = entryIndexType switch
                {
                    IndexType.Index2D => 2,
                    IndexType.Index3D => 3,
                    // Index1D / KernelConfig — when scalarized to a struct (rare), 1 field.
                    _ => 1,
                };
                if (st.NumFields != expectedFields) return false;
                for (int i = 0; i < st.NumFields; i++)
                {
                    if (st.Fields[i] is not PrimitiveType fpt
                        || fpt.BasicValueType != BasicValueType.Int32)
                        return false;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// TensorView&lt;T&gt; body structs embed one view plus Int32 shape fields.
        /// They must use the scalar-struct scratch path, not the single-view param path.
        /// </summary>
        private static bool IsTensorViewLikeBodyStruct(StructureType st)
        {
            int viewFieldCount = 0;
            int int32FieldCount = 0;
            for (int i = 0; i < st.NumFields; i++)
            {
                var field = st.Fields[i];
                if (field is AddressSpaceType
                    || (field is StructureType fst
                        && fst.NumFields > 0
                        && (fst.Fields[0] is AddressSpaceType
                            || fst.Fields[0].ToString().Contains("View"))))
                    viewFieldCount++;
                else if (field is PrimitiveType pt
                    && pt.BasicValueType == BasicValueType.Int32)
                    int32FieldCount++;
            }

            return viewFieldCount == 1 && int32FieldCount >= 4;
        }

        protected bool IsViewType(TypeNode type)
        {
            if (type is AddressSpaceType)
                return true;
            if (type is StructureType structType)
            {
                if (IsTensorViewLikeBodyStruct(structType))
                    return false;

                // A view StructureType (ArrayView<T>, ArrayView1D<T, TStride>) has
                // EXACTLY ONE AddressSpaceType DirectField (the pointer). Wrapper
                // structs like ArrayView1D add primitive metadata fields (Extent,
                // Stride) but only one pointer.
                //
                // Multi-view container structs (ManyIntViewsStruct,
                // VorbisPacketDecodeStaticInputs, custom user struct-of-views)
                // have MULTIPLE AddressSpaceType DirectFields — those are NOT
                // single views; they must go through the scalar-struct
                // serialization path so each view-ptr lands at its own field
                // offset in scratch memory and the kernel's GetField reads
                // each one independently.
                //
                // Pre-2026-05-04: this returned `true` for ANY struct whose
                // first DirectField is AddressSpaceType, which made the
                // dispatcher treat multi-view structs as single views (only V0's
                // buffer registered, V1..V11's pointers lost — task #16). Fix:
                // count AddressSpaceType DirectFields and only return `true`
                // for the single-AddressSpaceType case.
                int viewPtrCount = 0;
                foreach (var df in structType.DirectFields)
                {
                    if (df is AddressSpaceType) viewPtrCount++;
                }
                if (viewPtrCount == 1
                    && structType.DirectFields.Length > 0
                    && structType.DirectFields[0] is AddressSpaceType)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the element type of an ArrayView.
        /// </summary>
        protected TypeNode? GetViewElementType(TypeNode type)
        {
            if (type is AddressSpaceType addrType)
                return addrType.ElementType;
            if (type is StructureType structType && structType.NumFields > 0)
            {
                var firstField = structType.Fields[0];
                if (firstField is AddressSpaceType addr)
                    return addr.ElementType;
            }
            return null;
        }

        #endregion

        #region Float16 Conversion

        /// <summary>
        /// Emits inline Wasm code to convert f16 bits (i32 on stack) to f32.
        /// IEEE 754 half-precision: sign(1) | exponent(5) | mantissa(10)
        /// IEEE 754 single-precision: sign(1) | exponent(8) | mantissa(23)
        /// </summary>
        private void EmitF16ToF32()
        {
            // Stack has: i32 (f16 bits, 16-bit value)
            // We need to produce: f32
            //
            // Algorithm:
            //   sign = (h >> 15) & 1
            //   exp = (h >> 10) & 0x1F
            //   mant = h & 0x3FF
            //   if exp == 0 && mant == 0: return sign << 31 (±0)
            //   if exp == 0: denormal → normalize
            //   if exp == 31: inf/nan → exp = 255
            //   else: exp = exp + 112 (rebias: -15 + 127)
            //   result = (sign << 31) | (exp << 23) | (mant << 13)
            //
            // Simplified: handle ±0 and normal cases (skip denormals/inf/nan for now)
            var h = AllocateNewLocal(WasmOpCodes.I32);
            var sign = AllocateNewLocal(WasmOpCodes.I32);
            var exp = AllocateNewLocal(WasmOpCodes.I32);
            var mant = AllocateNewLocal(WasmOpCodes.I32);
            var result = AllocateNewLocal(WasmOpCodes.I32);

            WasmModuleBuilder.EmitLocalSet(Code, h);

            // sign = (h >> 15) & 1
            WasmModuleBuilder.EmitLocalGet(Code, h);
            WasmModuleBuilder.EmitI32Const(Code, 15);
            Code.Add(WasmOpCodes.I32ShrU);
            WasmModuleBuilder.EmitI32Const(Code, 1);
            Code.Add(WasmOpCodes.I32And);
            WasmModuleBuilder.EmitLocalSet(Code, sign);

            // exp = (h >> 10) & 0x1F
            WasmModuleBuilder.EmitLocalGet(Code, h);
            WasmModuleBuilder.EmitI32Const(Code, 10);
            Code.Add(WasmOpCodes.I32ShrU);
            WasmModuleBuilder.EmitI32Const(Code, 0x1F);
            Code.Add(WasmOpCodes.I32And);
            WasmModuleBuilder.EmitLocalSet(Code, exp);

            // mant = h & 0x3FF
            WasmModuleBuilder.EmitLocalGet(Code, h);
            WasmModuleBuilder.EmitI32Const(Code, 0x3FF);
            Code.Add(WasmOpCodes.I32And);
            WasmModuleBuilder.EmitLocalSet(Code, mant);

            // if exp == 0 && mant == 0: result = sign << 31 (±0)
            // if exp == 0 && mant != 0: denormal (approximate as 0 for simplicity)
            // if exp == 31: inf/nan → result = (sign<<31) | (0xFF<<23) | (mant<<13)
            // else: result = (sign<<31) | ((exp+112)<<23) | (mant<<13)

            // Start with normal case: (sign<<31) | ((exp+112)<<23) | (mant<<13)
            WasmModuleBuilder.EmitLocalGet(Code, sign);
            WasmModuleBuilder.EmitI32Const(Code, 31);
            Code.Add(WasmOpCodes.I32Shl);

            WasmModuleBuilder.EmitLocalGet(Code, exp);
            WasmModuleBuilder.EmitI32Const(Code, 112);
            Code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitI32Const(Code, 23);
            Code.Add(WasmOpCodes.I32Shl);
            Code.Add(WasmOpCodes.I32Or);

            WasmModuleBuilder.EmitLocalGet(Code, mant);
            WasmModuleBuilder.EmitI32Const(Code, 13);
            Code.Add(WasmOpCodes.I32Shl);
            Code.Add(WasmOpCodes.I32Or);
            WasmModuleBuilder.EmitLocalSet(Code, result);

            // Handle exp==0 (zero/denormal): result = sign << 31
            WasmModuleBuilder.EmitLocalGet(Code, exp);
            Code.Add(WasmOpCodes.I32Eqz);
            Code.Add(WasmOpCodes.If);
            Code.Add(WasmOpCodes.Void);
            WasmModuleBuilder.EmitLocalGet(Code, sign);
            WasmModuleBuilder.EmitI32Const(Code, 31);
            Code.Add(WasmOpCodes.I32Shl);
            WasmModuleBuilder.EmitLocalSet(Code, result);
            Code.Add(WasmOpCodes.End);

            // Handle exp==31 (inf/nan): result = (sign<<31) | (0xFF<<23) | (mant<<13)
            WasmModuleBuilder.EmitLocalGet(Code, exp);
            WasmModuleBuilder.EmitI32Const(Code, 31);
            Code.Add(WasmOpCodes.I32Eq);
            Code.Add(WasmOpCodes.If);
            Code.Add(WasmOpCodes.Void);
            WasmModuleBuilder.EmitLocalGet(Code, sign);
            WasmModuleBuilder.EmitI32Const(Code, 31);
            Code.Add(WasmOpCodes.I32Shl);
            WasmModuleBuilder.EmitI32Const(Code, 0xFF << 23);
            Code.Add(WasmOpCodes.I32Or);
            WasmModuleBuilder.EmitLocalGet(Code, mant);
            WasmModuleBuilder.EmitI32Const(Code, 13);
            Code.Add(WasmOpCodes.I32Shl);
            Code.Add(WasmOpCodes.I32Or);
            WasmModuleBuilder.EmitLocalSet(Code, result);
            Code.Add(WasmOpCodes.End);

            // f32.reinterpret_i32
            WasmModuleBuilder.EmitLocalGet(Code, result);
            Code.Add(WasmOpCodes.F32ReinterpretI32);
        }

        /// <summary>
        /// Emits inline Wasm code to convert f32 (on stack) to f16 bits (i32).
        /// Truncates mantissa and rebiases exponent.
        /// </summary>
        private void EmitF32ToF16()
        {
            // Stack has: f32
            // We need to produce: i32 (f16 bits, 16-bit value)
            var bits = AllocateNewLocal(WasmOpCodes.I32);
            var sign = AllocateNewLocal(WasmOpCodes.I32);
            var exp = AllocateNewLocal(WasmOpCodes.I32);
            var mant = AllocateNewLocal(WasmOpCodes.I32);

            // Reinterpret f32 to i32
            Code.Add(WasmOpCodes.I32ReinterpretF32);
            WasmModuleBuilder.EmitLocalSet(Code, bits);

            // sign = (bits >> 31) & 1
            WasmModuleBuilder.EmitLocalGet(Code, bits);
            WasmModuleBuilder.EmitI32Const(Code, 31);
            Code.Add(WasmOpCodes.I32ShrU);
            WasmModuleBuilder.EmitLocalSet(Code, sign);

            // exp = (bits >> 23) & 0xFF
            WasmModuleBuilder.EmitLocalGet(Code, bits);
            WasmModuleBuilder.EmitI32Const(Code, 23);
            Code.Add(WasmOpCodes.I32ShrU);
            WasmModuleBuilder.EmitI32Const(Code, 0xFF);
            Code.Add(WasmOpCodes.I32And);
            WasmModuleBuilder.EmitLocalSet(Code, exp);

            // mant = (bits >> 13) & 0x3FF
            WasmModuleBuilder.EmitLocalGet(Code, bits);
            WasmModuleBuilder.EmitI32Const(Code, 13);
            Code.Add(WasmOpCodes.I32ShrU);
            WasmModuleBuilder.EmitI32Const(Code, 0x3FF);
            Code.Add(WasmOpCodes.I32And);
            WasmModuleBuilder.EmitLocalSet(Code, mant);

            // Rebias exponent: f16_exp = f32_exp - 112
            // Clamp: if f32_exp <= 112 → f16_exp = 0 (underflow to zero)
            //         if f32_exp >= 143 → f16_exp = 31 (overflow to inf)
            //         if f32_exp == 255 → f16_exp = 31 (inf/nan)
            WasmModuleBuilder.EmitLocalGet(Code, exp);
            WasmModuleBuilder.EmitI32Const(Code, 112);
            Code.Add(WasmOpCodes.I32Sub);
            WasmModuleBuilder.EmitLocalSet(Code, exp);

            // Clamp underflow
            WasmModuleBuilder.EmitLocalGet(Code, exp);
            WasmModuleBuilder.EmitI32Const(Code, 0);
            Code.Add(WasmOpCodes.I32LtS);
            Code.Add(WasmOpCodes.If);
            Code.Add(WasmOpCodes.Void);
            WasmModuleBuilder.EmitI32Const(Code, 0);
            WasmModuleBuilder.EmitLocalSet(Code, exp);
            WasmModuleBuilder.EmitI32Const(Code, 0);
            WasmModuleBuilder.EmitLocalSet(Code, mant);
            Code.Add(WasmOpCodes.End);

            // Clamp overflow
            WasmModuleBuilder.EmitLocalGet(Code, exp);
            WasmModuleBuilder.EmitI32Const(Code, 31);
            Code.Add(WasmOpCodes.I32GtS);
            Code.Add(WasmOpCodes.If);
            Code.Add(WasmOpCodes.Void);
            WasmModuleBuilder.EmitI32Const(Code, 31);
            WasmModuleBuilder.EmitLocalSet(Code, exp);
            Code.Add(WasmOpCodes.End);

            // Assemble: (sign << 15) | (exp << 10) | mant
            WasmModuleBuilder.EmitLocalGet(Code, sign);
            WasmModuleBuilder.EmitI32Const(Code, 15);
            Code.Add(WasmOpCodes.I32Shl);
            WasmModuleBuilder.EmitLocalGet(Code, exp);
            WasmModuleBuilder.EmitI32Const(Code, 10);
            Code.Add(WasmOpCodes.I32Shl);
            Code.Add(WasmOpCodes.I32Or);
            WasmModuleBuilder.EmitLocalGet(Code, mant);
            Code.Add(WasmOpCodes.I32Or);
        }

        #endregion
    }
}
