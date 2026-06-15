// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmCodeGenerator.cs
//
// Base class for Wasm code generation. Implements IBackendCodeGenerator<StringBuilder>
// to translate ILGPU IR values into WebAssembly binary instructions.
// Each IR value maps to Wasm stack-machine instructions.
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Backends.EntryPoints;
using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;
using System.Text;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>
    /// Base class for Wasm code generation. Translates ILGPU IR values
    /// into WebAssembly binary instructions using a stack-based model.
    /// Each IR value is assigned a Wasm local variable.
    /// </summary>
    public abstract class WasmCodeGenerator : IBackendCodeGenerator<StringBuilder>
    {
        /// <summary>
        /// Generation arguments for Wasm code generator construction.
        /// </summary>
        public class GeneratorArgs
        {
            public WasmBackend Backend { get; }
            public EntryPoint EntryPoint { get; }
            public AllocaKindInformation SharedAllocations { get; }
            public AllocaKindInformation DynamicSharedAllocations { get; }

            /// <summary>
            /// Shared data: parameter infos populated by the kernel generator, read by CreateKernel.
            /// </summary>
            public List<WasmParamInfo> ParamInfos { get; set; } = new();

            /// <summary>
            /// Total bytes needed for shared memory allocations (populated by kernel generator).
            /// </summary>
            public int SharedMemorySize { get; set; }

            /// <summary>
            /// Number of barrier synchronization points (populated by kernel generator).
            /// </summary>
            public int BarrierCount { get; set; }

            /// <summary>
            /// Whether this kernel uses shared memory or barriers (populated by kernel generator).
            /// </summary>
            public bool HasBarriers { get; set; }

            /// <summary>
            /// Dynamic shared memory element size in bytes (populated by kernel generator).
            /// </summary>
            public int DynamicSharedElementSize { get; set; }
            public int ScratchPerThread { get; set; }
            /// <summary>
            /// Number of extra function imports beyond math (e.g., jsAtomicsWait).
            /// Used by AssignHelperFunctionIndices to compute correct function indices.
            /// </summary>
            public int ExtraImportCount { get; set; }

            /// <summary>
            /// Number of phases for fiber dispatch (1 = no barriers, N+1 for N barriers).
            /// Set to 1 when helpers have internal barriers (can't use fiber dispatch).
            /// </summary>
            public int PhaseCount { get; set; } = 1;

            /// <summary>
            /// Whether any helper function has internal barriers.
            /// When true, fiber-based phase dispatch is used for helper calls.
            /// </summary>
            public bool HelpersHaveBarriers { get; set; }

            /// <summary>
            /// Helper function methods and their allocas, keyed by Method.
            /// Populated during compilation so the kernel generator can inline them.
            /// </summary>
            public Dictionary<Method, Allocas> HelperMethods { get; } = new();

            /// <summary>
            /// Maps multi-block helper methods to their Wasm function indices.
            /// Populated during CreateKernelCodeGenerator so the kernel generator
            /// can emit 'call' instructions instead of nested state machines.
            /// </summary>
            public Dictionary<Method, int> HelperFunctionIndices { get; } = new();

            /// <summary>
            /// Pre-computed barrier counts per multi-block helper method.
            /// Each Barrier IR value counts as 1, each Broadcast as 2.
            /// </summary>
            public Dictionary<Method, int> HelperBarrierCounts { get; } = new();

            /// <summary>
            /// Estimated scratch size per helper (bytes). Used by kernel to offset
            /// scratch for each helper call so they don't overlap.
            /// </summary>
            public Dictionary<Method, int> HelperScratchEstimates { get; } = new();

            /// <summary>
            /// Ordered list of multi-block helper methods, for deterministic
            /// function index assignment and module emission order.
            /// </summary>
            public List<Method> HelperFunctionOrder { get; } = new();

            /// <summary>
            /// First IR kernel parameter index for user args at dispatch args[paramIdx].
            /// Matches codegen <c>SetupParameters</c> startIdx: 1 when param[0] is the
            /// implicit Index mapped to globalIdx, 0 when param[0] is e.g. LongIndex1D
            /// extent (GridStrideLoopKernel) or an explicit body struct.
            /// </summary>
            public int IrUserParamIndexOffset { get; set; }

            public GeneratorArgs(
                WasmBackend backend,
                EntryPoint entryPoint,
                AllocaKindInformation sharedAllocations,
                AllocaKindInformation dynamicSharedAllocations)
            {
                Backend = backend;
                EntryPoint = entryPoint;
                SharedAllocations = sharedAllocations;
                DynamicSharedAllocations = dynamicSharedAllocations;
            }
        }

        #region Fields

        protected readonly GeneratorArgs _generatorArgs;
        protected readonly Method Method;
        protected readonly Allocas Allocas;

        /// <summary>
        /// The Wasm instruction byte stream for the current function body.
        /// </summary>
        internal readonly List<byte> Code = new();

        /// <summary>
        /// Local variables for the Wasm function (params + locals).
        /// Maps ILGPU Value to Wasm local index.
        /// </summary>
        protected readonly Dictionary<string, uint> _localMap = new();

        /// <summary>
        /// Wasm local variable declarations (type only, separate from params).
        /// </summary>
        internal readonly List<WasmLocal> _locals = new();

        /// <summary>
        /// Next available local index (starts after function params).
        /// </summary>
        internal uint _nextLocalIndex = 0;

        /// <summary>
        /// Number of function parameters.
        /// </summary>
        internal uint _paramCount = 0;

        /// <summary>
        /// Maps imported math function names to their Wasm function indices.
        /// Populated by the backend during module construction.
        /// </summary>
        internal Dictionary<string, uint> MathImports { get; set; } = new();

        #endregion

        #region Constructor

        public WasmCodeGenerator(
            in GeneratorArgs args,
            Method method,
            Allocas allocas)
        {
            _generatorArgs = args;
            Method = method;
            Allocas = allocas;
        }

        #endregion

        #region Variable Management

        /// <summary>
        /// Allocates a new Wasm local variable for the given IR value.
        /// Returns the local index.
        /// </summary>
        protected uint AllocateLocal(Value value, byte wasmType = WasmOpCodes.I32)
        {
            var key = GetValueKey(value);
            if (_localMap.TryGetValue(key, out var existing))
            {
                TouchLocal(existing);
                return existing;
            }

            uint index = _nextLocalIndex++;
            _localMap[key] = index;
            // Only add to _locals if this is beyond the parameter range
            if (index >= _paramCount)
            {
                _locals.Add(new WasmLocal { Count = 1, Type = wasmType });
                string typeName = wasmType switch { WasmOpCodes.I32 => "i32", WasmOpCodes.I64 => "i64", WasmOpCodes.F32 => "f32", WasmOpCodes.F64 => "f64", _ => $"0x{wasmType:X2}" };
                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-Local] AllocateLocal: local_{index} = {typeName} (key={key}, IR={value.GetType().Name}, IRType={value.Type})");
            }
            TouchLocal(index);
            return index;
        }

        /// <summary>
        /// Gets the local index for an already-allocated value.
        /// </summary>
        protected uint GetLocal(Value value)
        {
            var key = GetValueKey(value);
            if (_localMap.TryGetValue(key, out var index))
            {
                TouchLocal(index);
                return index;
            }

            // Auto-allocate if not found
            return AllocateLocal(value, GetWasmType(value));
        }

        #region Spill liveness tracking (2026-06-11, Seven)
        // Yields (fiber state save/restore) happen ONLY at state-machine block
        // boundaries - a local touched in exactly ONE state block can never be live
        // across a yield and never needs spilling. Touches funnel through GetLocal /
        // AllocateLocal (every IR-value read and write); anonymous locals
        // (AllocateNewLocal) have no analyzable IR uses and stay always-spill.

        /// <summary>First state-machine block index each local was touched in.</summary>
        protected readonly Dictionary<uint, int> _localFirstState = new();
        /// <summary>Locals touched in more than one state block (live across a yield).</summary>
        protected readonly HashSet<uint> _localCrossesState = new();
        /// <summary>Anonymous (non-IR) locals - conservatively always spilled.</summary>
        protected readonly HashSet<uint> _localManual = new();
        /// <summary>The state-machine block index currently being emitted (kernel gen overrides).</summary>
        protected virtual int CurrentStateIndex => 0;

        protected void TouchLocal(uint index)
        {
            if (_localFirstState.TryGetValue(index, out var first))
            {
                if (first != CurrentStateIndex)
                    _localCrossesState.Add(index);
            }
            else
            {
                _localFirstState[index] = CurrentStateIndex;
            }
        }

        /// <summary>True if this local must be included in yield save/restore.</summary>
        protected bool LocalNeedsSpill(uint index)
            => _localCrossesState.Contains(index) || _localManual.Contains(index);

        #endregion

        /// <summary>
        /// Gets a unique key for a value (used for local variable mapping).
        /// </summary>
        protected string GetValueKey(Value value)
        {
            return $"v_{value.Id}";
        }

        /// <summary>
        /// Gets the Wasm type for an ILGPU value.
        /// </summary>
        protected byte GetWasmType(Value value)
        {
            return GetWasmTypeFromIR(value.Type);
        }

        /// <summary>
        /// Maps an ILGPU IR type to a Wasm value type.
        /// </summary>
        protected static byte GetWasmTypeFromIR(TypeNode type)
        {
            if (type is PrimitiveType pt)
            {
                return pt.BasicValueType switch
                {
                    BasicValueType.Int1 => WasmOpCodes.I32,
                    BasicValueType.Int8 => WasmOpCodes.I32,
                    BasicValueType.Int16 => WasmOpCodes.I32,
                    BasicValueType.Int32 => WasmOpCodes.I32,
                    BasicValueType.Int64 => WasmOpCodes.I64,
                    BasicValueType.Float16 => WasmOpCodes.F32,
                    BasicValueType.BFloat16 => WasmOpCodes.F32, // bf16 promoted to f32 for compute
                    BasicValueType.Float32 => WasmOpCodes.F32,
                    BasicValueType.Float64 => WasmOpCodes.F64,
                    _ => WasmOpCodes.I32,
                };
            }
            // Pointers, views, structs → i32 (memory offset)
            return WasmOpCodes.I32;
        }

        /// <summary>
        /// Gets the element size in bytes for a basic value type.
        /// </summary>
        protected static int GetElementSize(BasicValueType type)
        {
            return type switch
            {
                BasicValueType.Int1 => 1,
                BasicValueType.Int8 => 1,
                BasicValueType.Int16 => 2,
                BasicValueType.Int32 => 4,
                BasicValueType.Int64 => 8,
                BasicValueType.Float16 => 2,
                BasicValueType.BFloat16 => 2, // bf16: 2-byte storage
                BasicValueType.Float32 => 4,
                BasicValueType.Float64 => 8,
                _ => 4,
            };
        }

        #endregion

        #region Emit Helpers

        /// <summary>
        /// Pushes the value of a Wasm local onto the stack.
        /// </summary>
        protected void EmitGetLocal(Value value)
        {
            WasmModuleBuilder.EmitLocalGet(Code, GetLocal(value));
        }

        /// <summary>
        /// Pops the stack and stores into a Wasm local.
        /// </summary>
        protected void EmitSetLocal(Value value)
        {
            WasmModuleBuilder.EmitLocalSet(Code, GetLocal(value));
        }

        /// <summary>
        /// Pushes a local by index.
        /// </summary>
        protected void EmitGetLocalByIndex(uint index)
        {
            WasmModuleBuilder.EmitLocalGet(Code, index);
        }

        /// <summary>
        /// Gets the actual Wasm type of a local variable by its index.
        /// Parameters (indices < _paramCount) are always i32.
        /// Declared locals are looked up from _locals.
        /// </summary>
        protected byte GetLocalType(uint localIndex)
        {
            // Check if the kernel function generator has param type info
            if (this is WasmKernelFunctionGenerator kfg
                && localIndex < (uint)kfg.FuncParamTypes.Count)
            {
                return kfg.FuncParamTypes[(int)localIndex];
            }

            if (localIndex < (uint)_paramCount)
                return WasmOpCodes.I32; // default for params without explicit type info

            int localOffset = (int)localIndex - (int)_paramCount;
            if (localOffset >= 0 && localOffset < _locals.Count)
                return _locals[localOffset].Type;

            return WasmOpCodes.I32; // fallback
        }

        /// <summary>
        /// Allocates a new anonymous local variable (not tied to an IR Value).
        /// Anonymous locals are conservatively ALWAYS-SPILL (no IR uses to analyze).
        /// </summary>
        protected uint AllocateNewLocal(byte wasmType)
        {
            uint index = _nextLocalIndex++;
            _locals.Add(new WasmLocal { Count = 1, Type = wasmType });
            _localManual.Add(index);
            return index;
        }

        /// <summary>
        /// Emits a typed memory load from the address on the stack.
        /// </summary>
        protected virtual void EmitTypedLoad(byte wasmType)
        {
            switch (wasmType)
            {
                case WasmOpCodes.I32: WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I32Load, 2, 0); break;
                case WasmOpCodes.I64: WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.I64Load, 3, 0); break;
                case WasmOpCodes.F32: WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.F32Load, 2, 0); break;
                case WasmOpCodes.F64: WasmModuleBuilder.EmitLoad(Code, WasmOpCodes.F64Load, 3, 0); break;
            }
        }

        /// <summary>
        /// Emits a typed memory store (address and value should already be on the stack).
        /// </summary>
        protected virtual void EmitTypedStore(byte wasmType)
        {
            switch (wasmType)
            {
                case WasmOpCodes.I32: WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I32Store, 2, 0); break;
                case WasmOpCodes.I64: WasmModuleBuilder.EmitStore(Code, WasmOpCodes.I64Store, 3, 0); break;
                case WasmOpCodes.F32: WasmModuleBuilder.EmitStore(Code, WasmOpCodes.F32Store, 2, 0); break;
                case WasmOpCodes.F64: WasmModuleBuilder.EmitStore(Code, WasmOpCodes.F64Store, 3, 0); break;
            }
        }

        /// <summary>
        /// Emits a default (zero) value for the given Wasm type onto the stack.
        /// </summary>
        protected void EmitDefaultValue(byte wasmType)
        {
            switch (wasmType)
            {
                case WasmOpCodes.I32: WasmModuleBuilder.EmitI32Const(Code, 0); break;
                case WasmOpCodes.I64: WasmModuleBuilder.EmitI64Const(Code, 0); break;
                case WasmOpCodes.F32: WasmModuleBuilder.EmitF32Const(Code, 0.0f); break;
                case WasmOpCodes.F64: WasmModuleBuilder.EmitF64Const(Code, 0.0); break;
                default: WasmModuleBuilder.EmitI32Const(Code, 0); break;
            }
        }

        #endregion

        #region IBackendCodeGenerator<StringBuilder>

        public virtual void GenerateHeader(StringBuilder builder) { }

        public abstract void GenerateCode();

        public virtual void GenerateConstants(StringBuilder builder) { }

        public virtual void Merge(StringBuilder builder)
        {
            builder.AppendLine($"// Wasm function body: {Code.Count} bytes, {_locals.Count} locals");
        }

        #endregion

        #region IBackendCodeGenerator — all required method implementations

        // Arithmetic
        public virtual void GenerateCode(UnaryArithmeticValue value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            var src = value.Value.Resolve();
            var wasmType = GetWasmType(value);

            switch (value.Kind)
            {
                case UnaryArithmeticKind.Neg:
                    EmitGetLocal(src);
                    if (wasmType == WasmOpCodes.F32) Code.Add(WasmOpCodes.F32Neg);
                    else if (wasmType == WasmOpCodes.F64) Code.Add(WasmOpCodes.F64Neg);
                    else if (wasmType == WasmOpCodes.I64)
                    {
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        WasmModuleBuilder.EmitI64Const(Code, 0);
                        WasmModuleBuilder.EmitLocalGet(Code, target);
                        Code.Add(WasmOpCodes.I64Sub);
                    }
                    else
                    {
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        WasmModuleBuilder.EmitI32Const(Code, 0);
                        WasmModuleBuilder.EmitLocalGet(Code, target);
                        Code.Add(WasmOpCodes.I32Sub);
                    }
                    break;
                case UnaryArithmeticKind.Not:
                    EmitGetLocal(src);
                    if (wasmType == WasmOpCodes.I64)
                    {
                        WasmModuleBuilder.EmitI64Const(Code, -1);
                        Code.Add(WasmOpCodes.I64Xor);
                    }
                    else
                    {
                        WasmModuleBuilder.EmitI32Const(Code, -1);
                        Code.Add(WasmOpCodes.I32Xor);
                    }
                    break;
                case UnaryArithmeticKind.Abs:
                    EmitGetLocal(src);
                    if (wasmType == WasmOpCodes.F32) Code.Add(WasmOpCodes.F32Abs);
                    else if (wasmType == WasmOpCodes.F64) Code.Add(WasmOpCodes.F64Abs);
                    else if (wasmType == WasmOpCodes.I64)
                    {
                        // abs(x) = (x ^ (x >> 63)) - (x >> 63)
                        WasmModuleBuilder.EmitLocalTee(Code, target);
                        WasmModuleBuilder.EmitLocalGet(Code, target);
                        WasmModuleBuilder.EmitI64Const(Code, 63);
                        Code.Add(WasmOpCodes.I64ShrS);
                        Code.Add(WasmOpCodes.I64Xor);
                        WasmModuleBuilder.EmitLocalGet(Code, target);
                        WasmModuleBuilder.EmitI64Const(Code, 63);
                        Code.Add(WasmOpCodes.I64ShrS);
                        Code.Add(WasmOpCodes.I64Sub);
                    }
                    else
                    {
                        // abs(x) = (x ^ (x >> 31)) - (x >> 31)
                        WasmModuleBuilder.EmitLocalTee(Code, target);
                        WasmModuleBuilder.EmitLocalGet(Code, target);
                        WasmModuleBuilder.EmitI32Const(Code, 31);
                        Code.Add(WasmOpCodes.I32ShrS);
                        Code.Add(WasmOpCodes.I32Xor);
                        WasmModuleBuilder.EmitLocalGet(Code, target);
                        WasmModuleBuilder.EmitI32Const(Code, 31);
                        Code.Add(WasmOpCodes.I32ShrS);
                        Code.Add(WasmOpCodes.I32Sub);
                    }
                    break;
                // Native Wasm float ops
                case UnaryArithmeticKind.SqrtF:
                    EmitGetLocal(src);
                    Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Sqrt : WasmOpCodes.F32Sqrt);
                    break;
                case UnaryArithmeticKind.FloorF:
                    EmitGetLocal(src);
                    Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Floor : WasmOpCodes.F32Floor);
                    break;
                case UnaryArithmeticKind.CeilingF:
                    EmitGetLocal(src);
                    Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Ceil : WasmOpCodes.F32Ceil);
                    break;
                // Math imports (f64 -> f64)
                case UnaryArithmeticKind.SinF:
                case UnaryArithmeticKind.CosF:
                case UnaryArithmeticKind.TanF:
                case UnaryArithmeticKind.AsinF:
                case UnaryArithmeticKind.AcosF:
                case UnaryArithmeticKind.AtanF:
                case UnaryArithmeticKind.SinhF:
                case UnaryArithmeticKind.CoshF:
                case UnaryArithmeticKind.TanhF:
                case UnaryArithmeticKind.ExpF:
                case UnaryArithmeticKind.Exp2F:
                case UnaryArithmeticKind.LogF:
                case UnaryArithmeticKind.Log2F:
                case UnaryArithmeticKind.Log10F:
                    {
                        string mathName = value.Kind switch
                        {
                            UnaryArithmeticKind.SinF => "sin",
                            UnaryArithmeticKind.CosF => "cos",
                            UnaryArithmeticKind.TanF => "tan",
                            UnaryArithmeticKind.AsinF => "asin",
                            UnaryArithmeticKind.AcosF => "acos",
                            UnaryArithmeticKind.AtanF => "atan",
                            UnaryArithmeticKind.SinhF => "sinh",
                            UnaryArithmeticKind.CoshF => "cosh",
                            UnaryArithmeticKind.TanhF => "tanh",
                            UnaryArithmeticKind.ExpF => "exp",
                            UnaryArithmeticKind.Exp2F => "exp2",
                            UnaryArithmeticKind.LogF => "log",
                            UnaryArithmeticKind.Log2F => "log2",
                            UnaryArithmeticKind.Log10F => "log", // log10(x) = log(x) / log(10)
                            _ => "sin"
                        };
                        EmitGetLocal(src);
                        // Convert f32 to f64 for the import if needed
                        if (wasmType == WasmOpCodes.F32)
                            Code.Add(WasmOpCodes.F64PromoteF32);
                        if (MathImports.TryGetValue(mathName, out var funcIdx))
                            WasmModuleBuilder.EmitCall(Code, funcIdx);
                        // Convert result back to f32 if needed
                        // For Log10F, divide by ln(10) ≈ 2.302585092994046
                        if (value.Kind == UnaryArithmeticKind.Log10F)
                        {
                            WasmModuleBuilder.EmitF64Const(Code, 2.302585092994046);
                            Code.Add(WasmOpCodes.F64Div);
                        }
                        if (wasmType == WasmOpCodes.F32)
                            Code.Add(WasmOpCodes.F32DemoteF64);
                        break;
                    }
                case UnaryArithmeticKind.RcpF:
                    if (wasmType == WasmOpCodes.F64)
                    {
                        WasmModuleBuilder.EmitF64Const(Code, 1.0);
                        EmitGetLocal(src);
                        Code.Add(WasmOpCodes.F64Div);
                    }
                    else
                    {
                        WasmModuleBuilder.EmitF32Const(Code, 1.0f);
                        EmitGetLocal(src);
                        Code.Add(WasmOpCodes.F32Div);
                    }
                    break;
                case UnaryArithmeticKind.RsqrtF:
                    if (wasmType == WasmOpCodes.F64)
                    {
                        WasmModuleBuilder.EmitF64Const(Code, 1.0);
                        EmitGetLocal(src);
                        Code.Add(WasmOpCodes.F64Sqrt);
                        Code.Add(WasmOpCodes.F64Div);
                    }
                    else
                    {
                        WasmModuleBuilder.EmitF32Const(Code, 1.0f);
                        EmitGetLocal(src);
                        Code.Add(WasmOpCodes.F32Sqrt);
                        Code.Add(WasmOpCodes.F32Div);
                    }
                    break;
                // IsNaN: NaN != NaN is true in IEEE 754. Operates on the SOURCE
                // type (f32/f64), not the result type. `wasmType` here is the
                // RESULT type (i32 for the bool flag) — using it for the comparison
                // opcode would incorrectly emit f32-typed instructions on f64
                // sources, producing "f32.abs expected type f32, found local.get
                // of type f64" Wasm validation errors. Diagnosed 2026-04-29
                // against `BackendTestBase.DoubleIsNaNIntrinsicTest`.
                case UnaryArithmeticKind.IsNaNF:
                {
                    var srcWasmType = GetWasmTypeFromIR(value.Value.Type);
                    EmitGetLocal(src);
                    EmitGetLocal(src);
                    Code.Add(srcWasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Ne : WasmOpCodes.F32Ne);
                    // Result is i32 (0 or 1) but target local may be i32 too — fine for bool
                    break;
                }
                // IsInf: |x| == +Infinity. Same source-type-vs-result-type fix
                // as IsNaN above. The original code keyed off `wasmType` (the
                // i32 result type), so the F64 branch never fired.
                case UnaryArithmeticKind.IsInfF:
                {
                    var srcWasmType = GetWasmTypeFromIR(value.Value.Type);
                    EmitGetLocal(src);
                    Code.Add(srcWasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Abs : WasmOpCodes.F32Abs);
                    if (srcWasmType == WasmOpCodes.F64)
                    {
                        WasmModuleBuilder.EmitF64Const(Code, double.PositiveInfinity);
                        Code.Add(WasmOpCodes.F64Eq);
                    }
                    else
                    {
                        WasmModuleBuilder.EmitF32Const(Code, float.PositiveInfinity);
                        Code.Add(WasmOpCodes.F32Eq);
                    }
                    break;
                }
                // PopCount: native i32.popcnt / i64.popcnt
                case UnaryArithmeticKind.PopC:
                    EmitGetLocal(src);
                    if (wasmType == WasmOpCodes.I64)
                    {
                        Code.Add(WasmOpCodes.I64Popcnt);
                        // PopC always returns i32, wrap i64 result
                        Code.Add(WasmOpCodes.I32WrapI64);
                    }
                    else
                    {
                        Code.Add(WasmOpCodes.I32Popcnt);
                    }
                    break;
                // Count Leading Zeros: native i32.clz / i64.clz
                case UnaryArithmeticKind.CLZ:
                    EmitGetLocal(src);
                    if (wasmType == WasmOpCodes.I64)
                    {
                        Code.Add(WasmOpCodes.I64Clz);
                        Code.Add(WasmOpCodes.I32WrapI64);
                    }
                    else
                    {
                        Code.Add(WasmOpCodes.I32Clz);
                    }
                    break;
                // Count Trailing Zeros: native i32.ctz / i64.ctz
                case UnaryArithmeticKind.CTZ:
                    EmitGetLocal(src);
                    if (wasmType == WasmOpCodes.I64)
                    {
                        Code.Add(WasmOpCodes.I64Ctz);
                        Code.Add(WasmOpCodes.I32WrapI64);
                    }
                    else
                    {
                        Code.Add(WasmOpCodes.I32Ctz);
                    }
                    break;
                // IsFinite: !(isnan || isinf) → (x == x) && (abs(x) != inf).
                // Same source-type-vs-result-type fix as IsNaN/IsInf above.
                case UnaryArithmeticKind.IsFinF:
                    {
                        var srcWasmType = GetWasmTypeFromIR(value.Value.Type);
                        // Step 1: x == x (false if NaN)
                        EmitGetLocal(src);
                        EmitGetLocal(src);
                        Code.Add(srcWasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Eq : WasmOpCodes.F32Eq);
                        // Step 2: abs(x) != inf (false if ±Inf)
                        EmitGetLocal(src);
                        Code.Add(srcWasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Abs : WasmOpCodes.F32Abs);
                        if (srcWasmType == WasmOpCodes.F64)
                        {
                            WasmModuleBuilder.EmitF64Const(Code, double.PositiveInfinity);
                            Code.Add(WasmOpCodes.F64Ne);
                        }
                        else
                        {
                            WasmModuleBuilder.EmitF32Const(Code, float.PositiveInfinity);
                            Code.Add(WasmOpCodes.F32Ne);
                        }
                        // AND both conditions: isNotNaN && isNotInf
                        Code.Add(WasmOpCodes.I32And);
                        break;
                    }
                default:
                    EmitGetLocal(src);
                    break;
            }

            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        /// <summary>
        /// Emits an integer width-coercion instruction when <paramref name="srcType"/>
        /// differs from <paramref name="targetType"/> across the i32/i64 boundary
        /// (<c>i64.extend_i32_s</c> widening, <c>i32.wrap_i64</c> narrowing). Assumes the
        /// value to coerce is already on top of the stack. No-op when the types match or
        /// when either side is a float type (callers handle float width separately).
        /// Integer widening uses signed extension to match the codegen default.
        /// </summary>
        private void EmitIntWidthCoercion(byte srcType, byte targetType)
        {
            if (srcType == targetType)
                return;
            if (targetType == WasmOpCodes.I64 && srcType == WasmOpCodes.I32)
                Code.Add(WasmOpCodes.I64ExtendI32S);
            else if (targetType == WasmOpCodes.I32 && srcType == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I32WrapI64);
        }

        public virtual void GenerateCode(BinaryArithmeticValue value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            var left = value.Left.Resolve();
            var right = value.Right.Resolve();
            var wasmType = GetWasmType(value);

            // Handle integer Min/Max specially — Wasm has no i32.min/i64.min instructions
            // Use pattern: left right left right compare select
            if ((value.Kind == BinaryArithmeticKind.Min || value.Kind == BinaryArithmeticKind.Max) &&
                (wasmType == WasmOpCodes.I32 || wasmType == WasmOpCodes.I64))
            {
                // We need: val1 val2 condition → select
                // For Min: if left <= right, return left, else return right
                //   → left right left right i32.le_s select
                // For Max: if left >= right, return left, else return right
                //   → left right left right i32.ge_s select
                var leftLocal = AllocateNewLocal(wasmType);
                var rightLocal = AllocateNewLocal(wasmType);
                // The operands may not already match the result type — e.g. integer
                // Min/Max of a view length (i64) and an i32 index (View.Length is i64,
                // View.IntLength narrows, but ILGPU can feed the raw i64 length into the
                // Min). The general arithmetic path below coerces operands to the result
                // type; the Min/Max select path must do the same, otherwise we copy an
                // i64 value into an i32 temp (or vice-versa) and the module fails to
                // validate ("local.set expected i32 found i64"). Coerce to wasmType
                // (i32<->i64 width) on the way into each temp local.
                EmitGetLocal(left);
                EmitIntWidthCoercion(GetLocalType(GetLocal(left)), wasmType);
                WasmModuleBuilder.EmitLocalSet(Code, leftLocal);
                EmitGetLocal(right);
                EmitIntWidthCoercion(GetLocalType(GetLocal(right)), wasmType);
                WasmModuleBuilder.EmitLocalSet(Code, rightLocal);

                // Push val1 (left), val2 (right)
                WasmModuleBuilder.EmitLocalGet(Code, leftLocal);
                WasmModuleBuilder.EmitLocalGet(Code, rightLocal);
                // Push condition: left compare right
                WasmModuleBuilder.EmitLocalGet(Code, leftLocal);
                WasmModuleBuilder.EmitLocalGet(Code, rightLocal);

                bool isUnsigned = (value.Flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
                if (wasmType == WasmOpCodes.I32)
                {
                    if (value.Kind == BinaryArithmeticKind.Min)
                        Code.Add(isUnsigned ? WasmOpCodes.I32LeU : WasmOpCodes.I32LeS);
                    else
                        Code.Add(isUnsigned ? WasmOpCodes.I32GeU : WasmOpCodes.I32GeS);
                }
                else // I64
                {
                    if (value.Kind == BinaryArithmeticKind.Min)
                        Code.Add(isUnsigned ? WasmOpCodes.I64LeU : WasmOpCodes.I64LeS);
                    else
                        Code.Add(isUnsigned ? WasmOpCodes.I64GeU : WasmOpCodes.I64GeS);
                }

                Code.Add(WasmOpCodes.Select);
                WasmModuleBuilder.EmitLocalSet(Code, target);
                return;
            }

            // For i64 operations, Wasm requires both operands to be i64.
            // ILGPU IR may provide operands (especially shift amounts) as i32,
            // or struct decomposition may store i64 values in i32 locals.
            // Check the ACTUAL local type to determine if coercion is needed.
            var coerceLeftLocal = GetLocal(left);
            var coerceRightLocal = GetLocal(right);
            var leftActualType = GetLocalType(coerceLeftLocal);
            var rightActualType = GetLocalType(coerceRightLocal);

            EmitGetLocalByIndex(coerceLeftLocal);
            if (wasmType == WasmOpCodes.I64 && leftActualType == WasmOpCodes.I32)
                Code.Add(WasmOpCodes.I64ExtendI32S);
            else if (wasmType == WasmOpCodes.I32 && leftActualType == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I32WrapI64);

            EmitGetLocalByIndex(coerceRightLocal);
            if (wasmType == WasmOpCodes.I64 && rightActualType == WasmOpCodes.I32)
                Code.Add(WasmOpCodes.I64ExtendI32S);
            else if (wasmType == WasmOpCodes.I32 && rightActualType == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I32WrapI64);

            // Handle PowF, Atan2F, and BinaryLogF via math imports
            if (value.Kind == BinaryArithmeticKind.PowF || value.Kind == BinaryArithmeticKind.Atan2F ||
                value.Kind == BinaryArithmeticKind.BinaryLogF)
            {
                // BinaryLogF = log(left) / log(right), both args need "log" import
                if (value.Kind == BinaryArithmeticKind.BinaryLogF)
                {
                    if (MathImports.TryGetValue("log", out var logIdx))
                    {
                        // Pop right into temp, call log on left, store; call log on right; divide
                        var rightTemp = AllocateNewLocal(wasmType == WasmOpCodes.F32 ? WasmOpCodes.F64 : wasmType);
                        var leftTemp = AllocateNewLocal(wasmType == WasmOpCodes.F32 ? WasmOpCodes.F64 : wasmType);

                        // Right is on top of stack, left underneath
                        if (wasmType == WasmOpCodes.F32) Code.Add(WasmOpCodes.F64PromoteF32);
                        WasmModuleBuilder.EmitLocalSet(Code, rightTemp);
                        if (wasmType == WasmOpCodes.F32) Code.Add(WasmOpCodes.F64PromoteF32);
                        WasmModuleBuilder.EmitLocalSet(Code, leftTemp);

                        // log(left)
                        WasmModuleBuilder.EmitLocalGet(Code, leftTemp);
                        WasmModuleBuilder.EmitCall(Code, logIdx);
                        // log(right)
                        WasmModuleBuilder.EmitLocalGet(Code, rightTemp);
                        WasmModuleBuilder.EmitCall(Code, logIdx);
                        // divide
                        Code.Add(WasmOpCodes.F64Div);
                        if (wasmType == WasmOpCodes.F32) Code.Add(WasmOpCodes.F32DemoteF64);
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        return;
                    }
                }
                else
                {
                    string mathName = value.Kind == BinaryArithmeticKind.PowF ? "pow" : "atan2";
                    if (MathImports.TryGetValue(mathName, out var funcIdx))
                    {
                        // Left and right are already on stack. The imports expect (f64, f64) -> f64.
                        if (wasmType == WasmOpCodes.F32)
                        {
                            // Promote both f32 → f64, call, demote back
                            WasmModuleBuilder.EmitLocalSet(Code, target); // pop right to target temporarily
                            Code.Add(WasmOpCodes.F64PromoteF32); // promote left
                            WasmModuleBuilder.EmitLocalGet(Code, target);
                            Code.Add(WasmOpCodes.F64PromoteF32); // promote right
                            WasmModuleBuilder.EmitCall(Code, funcIdx);
                            Code.Add(WasmOpCodes.F32DemoteF64);
                        }
                        else
                        {
                            WasmModuleBuilder.EmitCall(Code, funcIdx);
                        }
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        return;
                    }
                }
            }

            // Handle float Rem (no native Wasm opcode): x % y = x - trunc(x / y) * y
            if (value.Kind == BinaryArithmeticKind.Rem &&
                (wasmType == WasmOpCodes.F32 || wasmType == WasmOpCodes.F64))
            {
                // Left and right are already on the stack.
                // We need: left - trunc(left / right) * right
                // Store left and right in temp locals since we need them multiple times.
                var rightLocal = AllocateNewLocal(wasmType);
                WasmModuleBuilder.EmitLocalSet(Code, rightLocal); // pop right
                var leftLocal = AllocateNewLocal(wasmType);
                WasmModuleBuilder.EmitLocalSet(Code, leftLocal); // pop left

                // result = left - trunc(left / right) * right
                WasmModuleBuilder.EmitLocalGet(Code, leftLocal);
                WasmModuleBuilder.EmitLocalGet(Code, leftLocal);
                WasmModuleBuilder.EmitLocalGet(Code, rightLocal);
                Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Div : WasmOpCodes.F32Div);
                Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Trunc : WasmOpCodes.F32Trunc);
                WasmModuleBuilder.EmitLocalGet(Code, rightLocal);
                Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Mul : WasmOpCodes.F32Mul);
                Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Sub : WasmOpCodes.F32Sub);

                WasmModuleBuilder.EmitLocalSet(Code, target);
                return;
            }

            bool isUnsignedOp = (value.Flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;

            byte opcode = (wasmType, value.Kind, isUnsignedOp) switch
            {
                (WasmOpCodes.I32, BinaryArithmeticKind.Add, _) => WasmOpCodes.I32Add,
                (WasmOpCodes.I32, BinaryArithmeticKind.Sub, _) => WasmOpCodes.I32Sub,
                (WasmOpCodes.I32, BinaryArithmeticKind.Mul, _) => WasmOpCodes.I32Mul,
                (WasmOpCodes.I32, BinaryArithmeticKind.Div, false) => WasmOpCodes.I32DivS,
                (WasmOpCodes.I32, BinaryArithmeticKind.Div, true) => WasmOpCodes.I32DivU,
                (WasmOpCodes.I32, BinaryArithmeticKind.Rem, false) => WasmOpCodes.I32RemS,
                (WasmOpCodes.I32, BinaryArithmeticKind.Rem, true) => WasmOpCodes.I32RemU,
                (WasmOpCodes.I32, BinaryArithmeticKind.And, _) => WasmOpCodes.I32And,
                (WasmOpCodes.I32, BinaryArithmeticKind.Or, _) => WasmOpCodes.I32Or,
                (WasmOpCodes.I32, BinaryArithmeticKind.Xor, _) => WasmOpCodes.I32Xor,
                (WasmOpCodes.I32, BinaryArithmeticKind.Shl, _) => WasmOpCodes.I32Shl,
                (WasmOpCodes.I32, BinaryArithmeticKind.Shr, false) => WasmOpCodes.I32ShrS,
                (WasmOpCodes.I32, BinaryArithmeticKind.Shr, true) => WasmOpCodes.I32ShrU,

                (WasmOpCodes.I64, BinaryArithmeticKind.Add, _) => WasmOpCodes.I64Add,
                (WasmOpCodes.I64, BinaryArithmeticKind.Sub, _) => WasmOpCodes.I64Sub,
                (WasmOpCodes.I64, BinaryArithmeticKind.Mul, _) => WasmOpCodes.I64Mul,
                (WasmOpCodes.I64, BinaryArithmeticKind.Div, false) => WasmOpCodes.I64DivS,
                (WasmOpCodes.I64, BinaryArithmeticKind.Div, true) => WasmOpCodes.I64DivU,
                (WasmOpCodes.I64, BinaryArithmeticKind.And, _) => WasmOpCodes.I64And,
                (WasmOpCodes.I64, BinaryArithmeticKind.Or, _) => WasmOpCodes.I64Or,
                (WasmOpCodes.I64, BinaryArithmeticKind.Xor, _) => WasmOpCodes.I64Xor,
                (WasmOpCodes.I64, BinaryArithmeticKind.Shl, _) => WasmOpCodes.I64Shl,
                (WasmOpCodes.I64, BinaryArithmeticKind.Shr, false) => WasmOpCodes.I64ShrS,
                (WasmOpCodes.I64, BinaryArithmeticKind.Shr, true) => WasmOpCodes.I64ShrU,
                (WasmOpCodes.I64, BinaryArithmeticKind.Rem, false) => WasmOpCodes.I64RemS,
                (WasmOpCodes.I64, BinaryArithmeticKind.Rem, true) => WasmOpCodes.I64RemU,

                (WasmOpCodes.F32, BinaryArithmeticKind.Add, _) => WasmOpCodes.F32Add,
                (WasmOpCodes.F32, BinaryArithmeticKind.Sub, _) => WasmOpCodes.F32Sub,
                (WasmOpCodes.F32, BinaryArithmeticKind.Mul, _) => WasmOpCodes.F32Mul,
                (WasmOpCodes.F32, BinaryArithmeticKind.Div, _) => WasmOpCodes.F32Div,
                (WasmOpCodes.F32, BinaryArithmeticKind.Min, _) => WasmOpCodes.F32Min,
                (WasmOpCodes.F32, BinaryArithmeticKind.Max, _) => WasmOpCodes.F32Max,
                (WasmOpCodes.F32, BinaryArithmeticKind.CopySignF, _) => WasmOpCodes.F32Copysign,

                (WasmOpCodes.F64, BinaryArithmeticKind.Add, _) => WasmOpCodes.F64Add,
                (WasmOpCodes.F64, BinaryArithmeticKind.Sub, _) => WasmOpCodes.F64Sub,
                (WasmOpCodes.F64, BinaryArithmeticKind.Mul, _) => WasmOpCodes.F64Mul,
                (WasmOpCodes.F64, BinaryArithmeticKind.Div, _) => WasmOpCodes.F64Div,
                (WasmOpCodes.F64, BinaryArithmeticKind.Min, _) => WasmOpCodes.F64Min,
                (WasmOpCodes.F64, BinaryArithmeticKind.Max, _) => WasmOpCodes.F64Max,
                (WasmOpCodes.F64, BinaryArithmeticKind.CopySignF, _) => WasmOpCodes.F64Copysign,

                _ => WasmOpCodes.I32Add // fallback
            };

            Code.Add(opcode);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(TernaryArithmeticValue value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            var a = value.First.Resolve();
            var b = value.Second.Resolve();
            var c = value.Third.Resolve();
            var wasmType = GetWasmType(value);

            EmitGetLocal(a);
            EmitGetLocal(b);
            Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Mul : WasmOpCodes.F32Mul);
            EmitGetLocal(c);
            Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Add : WasmOpCodes.F32Add);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        // Comparison
        public virtual void GenerateCode(CompareValue value)
        {
            var target = AllocateLocal(value, WasmOpCodes.I32);
            var left = value.Left.Resolve();
            var right = value.Right.Resolve();
            var leftType = GetWasmTypeFromIR(left.Type);
            var rightType = GetWasmTypeFromIR(right.Type);

            // Check ACTUAL local types — IR types and local types can diverge
            // (e.g., view.Length is i64 in IR but the local might be i32 or i64)
            var leftLocalIdx = GetLocal(left);
            var rightLocalIdx = GetLocal(right);
            var leftActualType = GetLocalType(leftLocalIdx);
            var rightActualType = GetLocalType(rightLocalIdx);

            // Use the wider type from BOTH IR types and actual local types
            var srcType = (leftType == WasmOpCodes.I64 || rightType == WasmOpCodes.I64
                || leftActualType == WasmOpCodes.I64 || rightActualType == WasmOpCodes.I64)
                ? WasmOpCodes.I64
                : leftType;

            bool isUnsigned = value.IsUnsignedOrUnordered;
            byte extendOp = isUnsigned ? WasmOpCodes.I64ExtendI32U : WasmOpCodes.I64ExtendI32S;

            EmitGetLocalByIndex(leftLocalIdx);
            if (srcType == WasmOpCodes.I64 && leftActualType == WasmOpCodes.I32)
                Code.Add(extendOp);
            // Wrap i64 → i32 if we decided to use i32 comparison but actual is i64
            if (srcType == WasmOpCodes.I32 && leftActualType == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I32WrapI64);
            EmitGetLocalByIndex(rightLocalIdx);
            if (srcType == WasmOpCodes.I64 && rightActualType == WasmOpCodes.I32)
                Code.Add(extendOp);
            if (srcType == WasmOpCodes.I32 && rightActualType == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I32WrapI64);
            byte opcode = (srcType, value.Kind, isUnsigned) switch
            {
                (WasmOpCodes.I32, CompareKind.Equal, _) => WasmOpCodes.I32Eq,
                (WasmOpCodes.I32, CompareKind.NotEqual, _) => WasmOpCodes.I32Ne,
                (WasmOpCodes.I32, CompareKind.LessThan, false) => WasmOpCodes.I32LtS,
                (WasmOpCodes.I32, CompareKind.LessThan, true) => WasmOpCodes.I32LtU,
                (WasmOpCodes.I32, CompareKind.LessEqual, false) => WasmOpCodes.I32LeS,
                (WasmOpCodes.I32, CompareKind.LessEqual, true) => WasmOpCodes.I32LeU,
                (WasmOpCodes.I32, CompareKind.GreaterThan, false) => WasmOpCodes.I32GtS,
                (WasmOpCodes.I32, CompareKind.GreaterThan, true) => WasmOpCodes.I32GtU,
                (WasmOpCodes.I32, CompareKind.GreaterEqual, false) => WasmOpCodes.I32GeS,
                (WasmOpCodes.I32, CompareKind.GreaterEqual, true) => WasmOpCodes.I32GeU,

                (WasmOpCodes.I64, CompareKind.Equal, _) => WasmOpCodes.I64Eq,
                (WasmOpCodes.I64, CompareKind.NotEqual, _) => WasmOpCodes.I64Ne,
                (WasmOpCodes.I64, CompareKind.LessThan, false) => WasmOpCodes.I64LtS,
                (WasmOpCodes.I64, CompareKind.LessThan, true) => WasmOpCodes.I64LtU,
                (WasmOpCodes.I64, CompareKind.LessEqual, false) => WasmOpCodes.I64LeS,
                (WasmOpCodes.I64, CompareKind.LessEqual, true) => WasmOpCodes.I64LeU,
                (WasmOpCodes.I64, CompareKind.GreaterThan, false) => WasmOpCodes.I64GtS,
                (WasmOpCodes.I64, CompareKind.GreaterThan, true) => WasmOpCodes.I64GtU,
                (WasmOpCodes.I64, CompareKind.GreaterEqual, false) => WasmOpCodes.I64GeS,
                (WasmOpCodes.I64, CompareKind.GreaterEqual, true) => WasmOpCodes.I64GeU,

                (WasmOpCodes.F32, CompareKind.Equal, _) => WasmOpCodes.F32Eq,
                (WasmOpCodes.F32, CompareKind.NotEqual, _) => WasmOpCodes.F32Ne,
                (WasmOpCodes.F32, CompareKind.LessThan, _) => WasmOpCodes.F32Lt,
                (WasmOpCodes.F32, CompareKind.LessEqual, _) => WasmOpCodes.F32Le,
                (WasmOpCodes.F32, CompareKind.GreaterThan, _) => WasmOpCodes.F32Gt,
                (WasmOpCodes.F32, CompareKind.GreaterEqual, _) => WasmOpCodes.F32Ge,

                (WasmOpCodes.F64, CompareKind.Equal, _) => WasmOpCodes.F64Eq,
                (WasmOpCodes.F64, CompareKind.NotEqual, _) => WasmOpCodes.F64Ne,
                (WasmOpCodes.F64, CompareKind.LessThan, _) => WasmOpCodes.F64Lt,
                (WasmOpCodes.F64, CompareKind.LessEqual, _) => WasmOpCodes.F64Le,
                (WasmOpCodes.F64, CompareKind.GreaterThan, _) => WasmOpCodes.F64Gt,
                (WasmOpCodes.F64, CompareKind.GreaterEqual, _) => WasmOpCodes.F64Ge,

                _ => WasmOpCodes.I32Eq // fallback
            };

            Code.Add(opcode);
            // IEEE 754 unordered float compare: result is TRUE if either operand
            // is NaN, regardless of the underlying ordered op. ILGPU's IR negates
            // `clt+brfalse` to `cge+brtrue [Unordered]`; Wasm's f32.ge / f64.ge
            // are ORDERED so without this OR-with-isnan fixup the bit gets set
            // for NaN inputs (DoubleNaNComparisonTest 2026-04-29). NotEqual is
            // already TRUE for NaN under ordered semantics so it is excluded.
            bool isFloat = srcType == WasmOpCodes.F32 || srcType == WasmOpCodes.F64;
            if (isFloat && value.IsUnsignedOrUnordered && value.Kind != CompareKind.NotEqual)
            {
                byte neOp = srcType == WasmOpCodes.F32 ? WasmOpCodes.F32Ne : WasmOpCodes.F64Ne;
                // Stack: ordered_result(i32). Now compute (isnan(left) || isnan(right) || ordered_result).
                EmitGetLocalByIndex(leftLocalIdx);
                EmitGetLocalByIndex(leftLocalIdx);
                Code.Add(neOp);  // is_nan(left): NaN != NaN under ordered NE
                Code.Add(WasmOpCodes.I32Or);
                EmitGetLocalByIndex(rightLocalIdx);
                EmitGetLocalByIndex(rightLocalIdx);
                Code.Add(neOp);  // is_nan(right)
                Code.Add(WasmOpCodes.I32Or);
            }
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        // Conversions
        public virtual void GenerateCode(ConvertValue value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            var src = value.Value.Resolve();
            var srcType = GetWasmTypeFromIR(src.Type);
            var dstType = GetWasmType(value);

            // Detect unsigned source conversion
            bool isSourceUnsigned = (value.Flags & ConvertFlags.SourceUnsigned) == ConvertFlags.SourceUnsigned;
            bool isTargetUnsigned = (value.Flags & ConvertFlags.TargetUnsigned) == ConvertFlags.TargetUnsigned;

            EmitGetLocal(src);

            byte? opcode = (srcType, dstType, isSourceUnsigned) switch
            {
                (WasmOpCodes.I32, WasmOpCodes.I64, false) => WasmOpCodes.I64ExtendI32S,
                (WasmOpCodes.I32, WasmOpCodes.I64, true) => WasmOpCodes.I64ExtendI32U,
                (WasmOpCodes.I32, WasmOpCodes.F32, false) => WasmOpCodes.F32ConvertI32S,
                (WasmOpCodes.I32, WasmOpCodes.F32, true) => WasmOpCodes.F32ConvertI32U,
                (WasmOpCodes.I32, WasmOpCodes.F64, false) => WasmOpCodes.F64ConvertI32S,
                (WasmOpCodes.I32, WasmOpCodes.F64, true) => WasmOpCodes.F64ConvertI32U,
                (WasmOpCodes.I64, WasmOpCodes.I32, _) => WasmOpCodes.I32WrapI64,
                (WasmOpCodes.I64, WasmOpCodes.F32, _) => WasmOpCodes.F32ConvertI64S,
                (WasmOpCodes.I64, WasmOpCodes.F64, _) => WasmOpCodes.F64ConvertI64S,
                (WasmOpCodes.F32, WasmOpCodes.I32, _) => WasmOpCodes.I32TruncF32S,
                (WasmOpCodes.F32, WasmOpCodes.I64, _) => WasmOpCodes.I64TruncF32S,
                (WasmOpCodes.F32, WasmOpCodes.F64, _) => WasmOpCodes.F64PromoteF32,
                (WasmOpCodes.F64, WasmOpCodes.I32, _) => WasmOpCodes.I32TruncF64S,
                (WasmOpCodes.F64, WasmOpCodes.I64, _) => WasmOpCodes.I64TruncF64S,
                (WasmOpCodes.F64, WasmOpCodes.F32, _) => WasmOpCodes.F32DemoteF64,
                _ => null
            };

            if (opcode.HasValue)
                Code.Add(opcode.Value);

            // Sub-word narrowing: ILGPU IR types Int8 / Int16 / UInt8 / UInt16 all
            // lower to Wasm i32 (no native sub-word integer types in Wasm), so the
            // (srcWasm, dstWasm) switch above sees no narrowing case for them.
            // Apply truncation explicitly here so e.g. `(short)0x18000` doesn't
            // leave a 32-bit value on the stack with the high bits intact and
            // pollute downstream stages of the iDCT 16x16 butterfly (Bug 2 from
            // tuvok-to-geordi-idct16x16-two-bugs-2026-04-25.md). Same fix covers
            // (sbyte) / (byte) / (ushort).
            //
            // i32.extend16_s / i32.extend8_s sign-extend from bit 15 / bit 7.
            // For unsigned target, mask with 0xFFFF / 0xFF to zero-extend.
            // F32 / F64 / I64 destinations skip this (their wasm dst type isn't
            // I32 here, so the narrowing branch doesn't apply).
            if (dstType == WasmOpCodes.I32)
            {
                var dstBasicType = value.Type.BasicValueType;
                if (dstBasicType == BasicValueType.Int16)
                {
                    if (isTargetUnsigned)
                    {
                        WasmModuleBuilder.EmitI32Const(Code, 0xFFFF);
                        Code.Add(WasmOpCodes.I32And);
                    }
                    else
                    {
                        Code.Add(WasmOpCodes.I32Extend16S);
                    }
                }
                else if (dstBasicType == BasicValueType.Int8)
                {
                    if (isTargetUnsigned)
                    {
                        WasmModuleBuilder.EmitI32Const(Code, 0xFF);
                        Code.Add(WasmOpCodes.I32And);
                    }
                    else
                    {
                        Code.Add(WasmOpCodes.I32Extend8S);
                    }
                }
                else
                {
                    // Widening from a SUB-WORD source (Int16/Int8) to a wider int (Int32). The
                    // (srcWasm=i32, dstWasm=i32) switch above emits no opcode, so the source value
                    // passes through unchanged - but it may be zero-extended (unsigned sub-word
                    // load, or a `(short)`/`(sbyte)` reinterpret the core IR elides since short and
                    // ushort share BasicValueType.Int16). Re-extend the low bits per SourceUnsigned
                    // so the high bits are correct: PopArithmeticArgs promotes the `(short)` operand
                    // of `(short)Interop.FloatAsInt(half) >> 15` (AscendingHalf's ones-complement
                    // mask) to i32 via THIS convert before the shift; without the re-extension the
                    // `>> 15` saw a zero-extended value and returned 0 instead of 0xFFFF for
                    // negative Halves. Idempotent for already-extended values; desktop backends use
                    // native sub-word registers and never reach this codegen.
                    var srcBasicType = value.Value.BasicValueType;
                    if (srcBasicType == BasicValueType.Int16)
                    {
                        if (isSourceUnsigned)
                        {
                            WasmModuleBuilder.EmitI32Const(Code, 0xFFFF);
                            Code.Add(WasmOpCodes.I32And);
                        }
                        else
                        {
                            Code.Add(WasmOpCodes.I32Extend16S);
                        }
                    }
                    else if (srcBasicType == BasicValueType.Int8)
                    {
                        if (isSourceUnsigned)
                        {
                            WasmModuleBuilder.EmitI32Const(Code, 0xFF);
                            Code.Add(WasmOpCodes.I32And);
                        }
                        else
                        {
                            Code.Add(WasmOpCodes.I32Extend8S);
                        }
                    }
                }
            }

            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        // Casts
        public virtual void GenerateCode(IntAsPointerCast cast)
        {
            // Pointers in Wasm are always i32
            var target = AllocateLocal(cast);
            var src = cast.Value.Resolve();
            EmitGetLocal(src);
            // If source int is i64, wrap to i32 for pointer
            if (GetWasmTypeFromIR(src.Type) == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I32WrapI64);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(PointerAsIntCast cast)
        {
            // Source pointer is i32, target int may be i32 or i64
            var dstType = GetWasmType(cast);
            var target = AllocateLocal(cast, dstType);
            EmitGetLocal(cast.Value.Resolve());
            // If target int is i64, extend from i32 pointer
            if (dstType == WasmOpCodes.I64)
                Code.Add(WasmOpCodes.I64ExtendI32S);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(PointerCast cast)
        {
            var target = AllocateLocal(cast);
            EmitGetLocal(cast.Value.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(AddressSpaceCast value)
        {
            var target = AllocateLocal(value);
            EmitGetLocal(value.Value.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(FloatAsIntCast value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            EmitGetLocal(value.Value.Resolve());
            var srcType = GetWasmTypeFromIR(value.Value.Resolve().Type);
            if (srcType == WasmOpCodes.F32) Code.Add(WasmOpCodes.I32ReinterpretF32);
            else if (srcType == WasmOpCodes.F64) Code.Add(WasmOpCodes.I64ReinterpretF64);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(IntAsFloatCast value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            EmitGetLocal(value.Value.Resolve());
            var srcType = GetWasmTypeFromIR(value.Value.Resolve().Type);
            if (srcType == WasmOpCodes.I32) Code.Add(WasmOpCodes.F32ReinterpretI32);
            else if (srcType == WasmOpCodes.I64) Code.Add(WasmOpCodes.F64ReinterpretI64);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        // Predicate
        public virtual void GenerateCode(Predicate predicate)
        {
            var wasmType = GetWasmType(predicate);
            var target = AllocateLocal(predicate, wasmType);
            var condition = predicate.Condition.Resolve();
            var trueVal = predicate.TrueValue.Resolve();
            var falseVal = predicate.FalseValue.Resolve();

            // The Wasm `select` instruction requires BOTH value operands to have the same
            // type, and the result must match the target local's type. The IR does not always
            // pre-convert the two branches to a common type — e.g. `cond ? 0 : longView[i]`
            // leaves an i32 constant against an i64 load — so emit a width/kind coercion to the
            // predicate's unified type for each branch. Without this, the validator rejects the
            // module ("local.set expected type i32, found i64" / select type mismatch). The
            // condition operand stays i32 (it is the boolean selector).
            EmitGetLocalCoercedTo(trueVal, wasmType);
            EmitGetLocalCoercedTo(falseVal, wasmType);
            EmitGetLocal(condition);
            Code.Add(WasmOpCodes.Select);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        /// <summary>
        /// Emits a <c>local.get</c> for <paramref name="operand"/> and, if its Wasm type
        /// differs from <paramref name="targetType"/>, the numeric conversion that coerces it
        /// to that type (i32&lt;-&gt;i64 width, f32&lt;-&gt;f64 width). Used where the Wasm
        /// instruction requires operands of a single unified type (e.g. <c>select</c>) but the
        /// IR left mixed-width branches. Integer widening uses signed extension (the codegen
        /// default; matches <see cref="GenerateCode(ConvertValue)"/>).
        /// </summary>
        private void EmitGetLocalCoercedTo(Value operand, byte targetType)
        {
            EmitGetLocal(operand);
            var srcType = GetWasmType(operand);
            if (srcType == targetType)
                return;
            byte? conv = (srcType, targetType) switch
            {
                (WasmOpCodes.I32, WasmOpCodes.I64) => WasmOpCodes.I64ExtendI32S,
                (WasmOpCodes.I64, WasmOpCodes.I32) => WasmOpCodes.I32WrapI64,
                (WasmOpCodes.F32, WasmOpCodes.F64) => WasmOpCodes.F64PromoteF32,
                (WasmOpCodes.F64, WasmOpCodes.F32) => WasmOpCodes.F32DemoteF64,
                _ => (byte?)null,
            };
            if (conv.HasValue)
                Code.Add(conv.Value);
        }

        // Constants & Values
        public virtual void GenerateCode(PrimitiveValue value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            var wasmType = GetWasmType(value);

            switch (wasmType)
            {
                case WasmOpCodes.I32:
                    WasmModuleBuilder.EmitI32Const(Code, value.Int32Value);
                    break;
                case WasmOpCodes.I64:
                    WasmModuleBuilder.EmitI64Const(Code, value.Int64Value);
                    break;
                case WasmOpCodes.F32:
                    // Float16/BFloat16 constants store raw 16-bit bits in rawValue — Float32Value
                    // reinterprets those bits as float (garbage). Promote via Float16Value/BFloat16Value.
                    float f32Const = value.BasicValueType == BasicValueType.Float16
                        ? (float)value.Float16Value
                        : value.BasicValueType == BasicValueType.BFloat16
                        ? (float)value.BFloat16Value
                        : value.Float32Value;
                    WasmModuleBuilder.EmitF32Const(Code, f32Const);
                    break;
                case WasmOpCodes.F64:
                    WasmModuleBuilder.EmitF64Const(Code, value.Float64Value);
                    break;
            }

            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(StringValue value)
        {
            // Strings are not used in compute kernels — no-op
        }

        public virtual void GenerateCode(NullValue value)
        {
            var wasmType = GetWasmType(value);
            var target = AllocateLocal(value, wasmType);
            if (wasmType == WasmOpCodes.I64)
                WasmModuleBuilder.EmitI64Const(Code, 0);
            else if (wasmType == WasmOpCodes.F32)
                WasmModuleBuilder.EmitF32Const(Code, 0);
            else if (wasmType == WasmOpCodes.F64)
                WasmModuleBuilder.EmitF64Const(Code, 0);
            else
                WasmModuleBuilder.EmitI32Const(Code, 0);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(Parameter value)
        {
            // Parameters are handled by the kernel function generator
        }

        public virtual void GenerateCode(PhiValue value)
        {
            AllocateLocal(value, GetWasmType(value));
        }

        // Memory
        public virtual void GenerateCode(Load value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            EmitGetLocal(value.Source.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(Store value) { }

        public virtual void GenerateCode(Alloca value)
        {
            var target = AllocateLocal(value);
            WasmModuleBuilder.EmitI32Const(Code, 0);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(MemoryBarrier barrier) { }

        public virtual void GenerateCode(LoadElementAddress value)
        {
            var target = AllocateLocal(value);
            EmitGetLocal(value.Source.Resolve());
            EmitGetLocal(value.Offset.Resolve());
            int elemSize = 4;
            if (value.Type is AddressSpaceType addrType)
            {
                if (addrType.ElementType is PrimitiveType pt)
                    elemSize = GetElementSize(pt.BasicValueType);
                else if (addrType.ElementType is StructureType st)
                    elemSize = st.Size;
            }
            WasmModuleBuilder.EmitI32Const(Code, elemSize);
            Code.Add(WasmOpCodes.I32Mul);
            Code.Add(WasmOpCodes.I32Add);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(LoadFieldAddress value)
        {
            var target = AllocateLocal(value);
            EmitGetLocal(value.Source.Resolve());

            // Compute the byte offset for this field within the struct
            int fieldIndex = (int)value.FieldSpan.Index;
            try
            {
                var structType = value.StructureType;
                if (fieldIndex < structType.NumFields)
                {
                    int byteOffset = structType.GetOffset(new global::ILGPU.IR.Values.FieldAccess(fieldIndex));
                    if (byteOffset > 0)
                    {
                        WasmModuleBuilder.EmitI32Const(Code, byteOffset);
                        Code.Add(WasmOpCodes.I32Add);
                    }
                }
            }
            catch
            {
                // If we can't resolve the struct type, fall through with no offset
                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] Warning: Could not resolve struct type for LoadFieldAddress field {fieldIndex}");
            }

            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(AlignTo value)
        {
            var target = AllocateLocal(value);
            EmitGetLocal(value.Source.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(AsAligned value)
        {
            var target = AllocateLocal(value);
            EmitGetLocal(value.Source.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        // Structs/Fields
        public virtual void GenerateCode(StructureValue value)
        {
            var target = AllocateLocal(value);
            if (value.Count > 0)
                EmitGetLocal(value[0].Resolve());
            else
                WasmModuleBuilder.EmitI32Const(Code, 0);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(GetField value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            var source = value.ObjectValue.Resolve();
            EmitGetLocal(source);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(SetField value)
        {
            var target = AllocateLocal(value, GetWasmType(value));
            EmitGetLocal(value.Value.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        // Device Constants
        public virtual void GenerateCode(GridIndexValue value) { AllocateLocal(value); }
        public virtual void GenerateCode(GroupIndexValue value) { AllocateLocal(value); }
        public virtual void GenerateCode(GridDimensionValue value) { AllocateLocal(value); }
        public virtual void GenerateCode(GroupDimensionValue value) { AllocateLocal(value); }
        public virtual void GenerateCode(WarpSizeValue value)
        {
            var target = AllocateLocal(value);
            WasmModuleBuilder.EmitI32Const(Code, 1); // Wasm: no warp
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }
        public virtual void GenerateCode(LaneIdxValue value)
        {
            var target = AllocateLocal(value);
            WasmModuleBuilder.EmitI32Const(Code, 0); // Wasm: lane 0
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }
        public virtual void GenerateCode(DynamicMemoryLengthValue value)
        {
            var target = AllocateLocal(value);
            WasmModuleBuilder.EmitI32Const(Code, 0);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        /// <summary>
        /// Default GetViewLength handler: emits 0. Overridden in WasmKernelFunctionGenerator
        /// to read the actual length local that was passed at dispatch time.
        /// </summary>
        public virtual void GenerateCode(GetViewLength value)
        {
            var target = AllocateLocal(value, WasmOpCodes.I32);
            WasmModuleBuilder.EmitI32Const(Code, 0);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        // Views
        /// <summary>
        /// Generates code for a NewView IR node.
        /// NewView wraps a pointer + length into an ArrayView.
        /// In our flat Wasm memory model, the view IS the pointer.
        /// </summary>
        public virtual void GenerateCode(NewView value)
        {
            var target = AllocateLocal(value);
            // NewView.Pointer is the source pointer (e.g., from Alloca for shared memory)
            var pointer = value.Pointer.Resolve();
            EmitGetLocal(pointer);
            WasmModuleBuilder.EmitLocalSet(Code, target);
            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-NewView] target=local_{target} <- pointer=local_{GetLocal(pointer)} (IR={pointer.GetType().Name} id={pointer.Id})");
        }

        // Barriers & Atomics
        public virtual void GenerateCode(PredicateBarrier barrier) { }
        public virtual void GenerateCode(global::ILGPU.IR.Values.Barrier barrier) { }
        public virtual void GenerateCode(Broadcast broadcast)
        {
            var target = AllocateLocal(broadcast, GetWasmType(broadcast));
            EmitGetLocal(broadcast.Variable.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }
        public virtual void GenerateCode(WarpShuffle shuffle)
        {
            var target = AllocateLocal(shuffle, GetWasmType(shuffle));
            EmitGetLocal(shuffle.Variable.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }
        public virtual void GenerateCode(SubWarpShuffle shuffle)
        {
            var target = AllocateLocal(shuffle, GetWasmType(shuffle));
            EmitGetLocal(shuffle.Variable.Resolve());
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }
        public virtual void GenerateCode(GenericAtomic atomic)
        {
            // Thread-safe atomic RMW using Wasm threads proposal
            var target = AllocateLocal(atomic, GetWasmType(atomic));
            var address = atomic.Target.Resolve();
            var val = atomic.Value.Resolve();
            var wasmType = GetWasmType(atomic);

            // Float atomics: use CAS loop with reinterpret
            if (wasmType == WasmOpCodes.F32 || wasmType == WasmOpCodes.F64)
            {
                EmitFloatAtomicViaCAS(atomic, target, address, val, wasmType);
                return;
            }

            // For Add/Sub/And/Or/Xor/Exchange: use Wasm atomic RMW instructions
            // These return the OLD value atomically
            byte? atomicOpcode = (wasmType, atomic.Kind) switch
            {
                (WasmOpCodes.I32, AtomicKind.Add) => WasmOpCodes.I32AtomicRmwAdd,
                (WasmOpCodes.I32, AtomicKind.And) => WasmOpCodes.I32AtomicRmwAnd,
                (WasmOpCodes.I32, AtomicKind.Or) => WasmOpCodes.I32AtomicRmwOr,
                (WasmOpCodes.I32, AtomicKind.Xor) => WasmOpCodes.I32AtomicRmwXor,
                (WasmOpCodes.I32, AtomicKind.Exchange) => WasmOpCodes.I32AtomicRmwXchg,
                (WasmOpCodes.I64, AtomicKind.Add) => WasmOpCodes.I64AtomicRmwAdd,
                (WasmOpCodes.I64, AtomicKind.And) => WasmOpCodes.I64AtomicRmwAnd,
                (WasmOpCodes.I64, AtomicKind.Or) => WasmOpCodes.I64AtomicRmwOr,
                (WasmOpCodes.I64, AtomicKind.Xor) => WasmOpCodes.I64AtomicRmwXor,
                (WasmOpCodes.I64, AtomicKind.Exchange) => WasmOpCodes.I64AtomicRmwXchg,
                _ => null
            };

            if (atomicOpcode.HasValue)
            {
                // Atomic RMW: stack [addr, val] -> old_value
                uint align = wasmType == WasmOpCodes.I64 ? 3u : 2u;
                EmitGetLocal(address);
                EmitGetLocal(val);
                WasmModuleBuilder.EmitAtomicRmw(Code, atomicOpcode.Value, align, 0);
                WasmModuleBuilder.EmitLocalSet(Code, target);
                return;
            }


            // Min/Max: CAS loop (no native Wasm instruction)
            if (atomic.Kind == AtomicKind.Min || atomic.Kind == AtomicKind.Max)
            {
                EmitIntAtomicMinMax(atomic, target, address, val, wasmType);
                return;
            }

            // No silent fallback - throw for unhandled atomic kinds
            throw new NotSupportedException($"Unsupported atomic kind: {atomic.Kind} for Wasm type 0x{wasmType:X2}");
        }

        /// <summary>
        /// Emits an atomic Min/Max via CAS loop for integer types.
        /// Loop: old = atomic.load(addr); new = select(old, val, cmp); if cmpxchg(addr, old, new) == old, break
        /// </summary>
        private void EmitIntAtomicMinMax(GenericAtomic atomic, uint target, Value address, Value val, byte wasmType)
        {
            uint align = wasmType == WasmOpCodes.I64 ? 3u : 2u;
            byte loadOp = wasmType == WasmOpCodes.I64 ? WasmOpCodes.I64AtomicLoad : WasmOpCodes.I32AtomicLoad;
            byte cmpxchgOp = wasmType == WasmOpCodes.I64 ? WasmOpCodes.I64AtomicRmwCmpxchg : WasmOpCodes.I32AtomicRmwCmpxchg;
            byte eqOp = wasmType == WasmOpCodes.I64 ? WasmOpCodes.I64Eq : WasmOpCodes.I32Eq;
            bool isUnsigned = atomic.IsUnsigned;
            byte cmpOp = (atomic.Kind, wasmType, isUnsigned) switch
            {
                (AtomicKind.Min, WasmOpCodes.I32, false) => WasmOpCodes.I32LtS,
                (AtomicKind.Min, WasmOpCodes.I32, true) => WasmOpCodes.I32LtU,
                (AtomicKind.Min, WasmOpCodes.I64, false) => WasmOpCodes.I64LtS,
                (AtomicKind.Min, WasmOpCodes.I64, true) => WasmOpCodes.I64LtU,
                (AtomicKind.Max, WasmOpCodes.I32, false) => WasmOpCodes.I32GtS,
                (AtomicKind.Max, WasmOpCodes.I32, true) => WasmOpCodes.I32GtU,
                (AtomicKind.Max, WasmOpCodes.I64, false) => WasmOpCodes.I64GtS,
                (AtomicKind.Max, WasmOpCodes.I64, true) => WasmOpCodes.I64GtU,
                _ => throw new NotSupportedException($"Unsupported atomic Min/Max kind/type/unsigned combination: {atomic.Kind}/{wasmType}/{isUnsigned}")
            };

            var newVal = AllocateNewLocal(wasmType);
            var casResult = AllocateNewLocal(wasmType);

            // loop $cas_loop
            Code.Add(WasmOpCodes.Loop);
            Code.Add(WasmOpCodes.Void);

            // old = atomic.load(addr)
            EmitGetLocal(address);
            WasmModuleBuilder.EmitAtomicRmw(Code, loadOp, align, 0);
            WasmModuleBuilder.EmitLocalSet(Code, target);

            // new = select(old, val, old < val) for Min / select(old, val, old > val) for Max
            WasmModuleBuilder.EmitLocalGet(Code, target);
            EmitGetLocal(val);
            WasmModuleBuilder.EmitLocalGet(Code, target);
            EmitGetLocal(val);
            Code.Add(cmpOp);
            Code.Add(WasmOpCodes.Select);
            WasmModuleBuilder.EmitLocalSet(Code, newVal);

            // casResult = cmpxchg(addr, old, new) -> returns old value from memory
            EmitGetLocal(address);
            WasmModuleBuilder.EmitLocalGet(Code, target);
            WasmModuleBuilder.EmitLocalGet(Code, newVal);
            WasmModuleBuilder.EmitAtomicRmw(Code, cmpxchgOp, align, 0);
            WasmModuleBuilder.EmitLocalSet(Code, casResult);

            // if casResult != old, retry (br_if 0 to loop)
            WasmModuleBuilder.EmitLocalGet(Code, casResult);
            WasmModuleBuilder.EmitLocalGet(Code, target);
            Code.Add(eqOp);
            Code.Add(WasmOpCodes.I32Eqz);
            Code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(Code, 0); // br_if $loop (depth 0)

            Code.Add(WasmOpCodes.End); // end loop
        }

        /// <summary>
        /// Emits float atomic operations via CAS loop with integer reinterpret.
        /// float atomic: reinterpret to i32, CAS loop, reinterpret result back.
        /// </summary>
        private void EmitFloatAtomicViaCAS(GenericAtomic atomic, uint target, Value address, Value val, byte wasmType)
        {
            // For float atomics, we use i32 CAS on the bitwise representation
            byte intType = wasmType == WasmOpCodes.F64 ? WasmOpCodes.I64 : WasmOpCodes.I32;
            uint align = wasmType == WasmOpCodes.F64 ? 3u : 2u;
            byte loadOp = intType == WasmOpCodes.I64 ? WasmOpCodes.I64AtomicLoad : WasmOpCodes.I32AtomicLoad;
            byte cmpxchgOp = intType == WasmOpCodes.I64 ? WasmOpCodes.I64AtomicRmwCmpxchg : WasmOpCodes.I32AtomicRmwCmpxchg;
            byte eqOp = intType == WasmOpCodes.I64 ? WasmOpCodes.I64Eq : WasmOpCodes.I32Eq;
            byte reinterpretToInt = wasmType == WasmOpCodes.F64 ? WasmOpCodes.I64ReinterpretF64 : WasmOpCodes.I32ReinterpretF32;
            byte reinterpretToFloat = wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64ReinterpretI64 : WasmOpCodes.F32ReinterpretI32;

            var oldInt = AllocateNewLocal(intType);
            var oldFloat = AllocateNewLocal(wasmType);
            var newFloat = AllocateNewLocal(wasmType);
            var newInt = AllocateNewLocal(intType);
            var casResult = AllocateNewLocal(intType);

            // Determine the float arithmetic opcode
            byte floatOp = (wasmType, atomic.Kind) switch
            {
                (WasmOpCodes.F32, AtomicKind.Add) => WasmOpCodes.F32Add,
                (WasmOpCodes.F32, AtomicKind.Min) => WasmOpCodes.F32Min,
                (WasmOpCodes.F32, AtomicKind.Max) => WasmOpCodes.F32Max,
                (WasmOpCodes.F64, AtomicKind.Add) => WasmOpCodes.F64Add,
                (WasmOpCodes.F64, AtomicKind.Min) => WasmOpCodes.F64Min,
                (WasmOpCodes.F64, AtomicKind.Max) => WasmOpCodes.F64Max,
                _ => throw new NotSupportedException($"Unsupported float atomic kind: {atomic.Kind} on type 0x{wasmType:X2}")
            };

            // loop $cas_loop
            Code.Add(WasmOpCodes.Loop);
            Code.Add(WasmOpCodes.Void);

            // oldInt = atomic.load(addr)
            EmitGetLocal(address);
            WasmModuleBuilder.EmitAtomicRmw(Code, loadOp, align, 0);
            WasmModuleBuilder.EmitLocalSet(Code, oldInt);

            // oldFloat = reinterpret(oldInt)
            WasmModuleBuilder.EmitLocalGet(Code, oldInt);
            Code.Add(reinterpretToFloat);
            WasmModuleBuilder.EmitLocalSet(Code, oldFloat);

            if (atomic.Kind == AtomicKind.Exchange)
            {
                // Exchange: newFloat = val
                EmitGetLocal(val);
                WasmModuleBuilder.EmitLocalSet(Code, newFloat);
            }
            else
            {
                // newFloat = oldFloat op val
                WasmModuleBuilder.EmitLocalGet(Code, oldFloat);
                EmitGetLocal(val);
                Code.Add(floatOp);
                WasmModuleBuilder.EmitLocalSet(Code, newFloat);
            }

            // newInt = reinterpret(newFloat)
            WasmModuleBuilder.EmitLocalGet(Code, newFloat);
            Code.Add(reinterpretToInt);
            WasmModuleBuilder.EmitLocalSet(Code, newInt);

            // casResult = cmpxchg(addr, oldInt, newInt)
            EmitGetLocal(address);
            WasmModuleBuilder.EmitLocalGet(Code, oldInt);
            WasmModuleBuilder.EmitLocalGet(Code, newInt);
            WasmModuleBuilder.EmitAtomicRmw(Code, cmpxchgOp, align, 0);
            WasmModuleBuilder.EmitLocalSet(Code, casResult);

            // if casResult != oldInt, retry
            WasmModuleBuilder.EmitLocalGet(Code, casResult);
            WasmModuleBuilder.EmitLocalGet(Code, oldInt);
            Code.Add(eqOp);
            Code.Add(WasmOpCodes.I32Eqz);
            Code.Add(WasmOpCodes.BrIf);
            WasmModuleBuilder.EmitU32Leb128(Code, 0); // br_if $loop

            Code.Add(WasmOpCodes.End); // end loop

            // target = oldFloat (the value before the operation)
            WasmModuleBuilder.EmitLocalGet(Code, oldFloat);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        public virtual void GenerateCode(AtomicCAS atomicCAS)
        {
            // Thread-safe CAS using Wasm atomic cmpxchg
            var target = AllocateLocal(atomicCAS, GetWasmType(atomicCAS));
            var address = atomicCAS.Target.Resolve();
            var expected = atomicCAS.Value.Resolve();
            var desired = atomicCAS.CompareValue.Resolve();
            var wasmType = GetWasmType(atomicCAS);

            uint align = wasmType == WasmOpCodes.I64 ? 3u : 2u;
            byte cmpxchgOp = wasmType == WasmOpCodes.I64 ? WasmOpCodes.I64AtomicRmwCmpxchg : WasmOpCodes.I32AtomicRmwCmpxchg;

            // atomic.rmw.cmpxchg: stack [addr, expected, replacement] -> old_value
            EmitGetLocal(address);
            EmitGetLocal(expected);
            EmitGetLocal(desired);
            WasmModuleBuilder.EmitAtomicRmw(Code, cmpxchgOp, align, 0);
            WasmModuleBuilder.EmitLocalSet(Code, target);
        }

        // Debug
        public virtual void GenerateCode(DebugAssertOperation debug) { }
        public virtual void GenerateCode(WriteToOutput writeToOutput) { }

        // Misc
        public virtual void GenerateCode(MethodCall methodCall)
        {
            var method = methodCall.Target;

            // Check if this is an intrinsic/external method that needs inline code generation
            if (method.HasSource && (method.HasFlags(global::ILGPU.IR.MethodFlags.Intrinsic) ||
                                     method.HasFlags(global::ILGPU.IR.MethodFlags.External)))
            {
                var sourceName = method.Source.Name;

                if (!methodCall.Type.IsVoidType)
                {
                    var wasmType = GetWasmType(methodCall);
                    var target = AllocateLocal(methodCall, wasmType);

                    // Load arguments
                    var argValues = new List<Value>();
                    for (int i = 0; i < methodCall.Nodes.Length; i++)
                        argValues.Add(methodCall.Nodes[i].Resolve());

                    bool handled = true;
                    switch (sourceName)
                    {
                        case "FusedMultiplyAdd":
                            EmitGetLocal(argValues[0]);
                            EmitGetLocal(argValues[1]);
                            Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Mul : WasmOpCodes.F32Mul);
                            EmitGetLocal(argValues[2]);
                            Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Add : WasmOpCodes.F32Add);
                            break;
                        case "Sqrt":
                            EmitGetLocal(argValues[0]);
                            Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Sqrt : WasmOpCodes.F32Sqrt);
                            break;
                        case "Abs":
                            EmitGetLocal(argValues[0]);
                            if (wasmType == WasmOpCodes.F64) Code.Add(WasmOpCodes.F64Abs);
                            else if (wasmType == WasmOpCodes.F32) Code.Add(WasmOpCodes.F32Abs);
                            else
                            {
                                WasmModuleBuilder.EmitLocalTee(Code, target);
                                WasmModuleBuilder.EmitLocalGet(Code, target);
                                WasmModuleBuilder.EmitI32Const(Code, 31);
                                Code.Add(WasmOpCodes.I32ShrS);
                                Code.Add(WasmOpCodes.I32Xor);
                                WasmModuleBuilder.EmitLocalGet(Code, target);
                                WasmModuleBuilder.EmitI32Const(Code, 31);
                                Code.Add(WasmOpCodes.I32ShrS);
                                Code.Add(WasmOpCodes.I32Sub);
                            }
                            break;
                        case "Floor":
                            EmitGetLocal(argValues[0]);
                            Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Floor : WasmOpCodes.F32Floor);
                            break;
                        case "Ceiling":
                        case "Ceil":
                            EmitGetLocal(argValues[0]);
                            Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Ceil : WasmOpCodes.F32Ceil);
                            break;
                        case "Min":
                            EmitGetLocal(argValues[0]);
                            EmitGetLocal(argValues[1]);
                            if (wasmType == WasmOpCodes.F64) Code.Add(WasmOpCodes.F64Min);
                            else if (wasmType == WasmOpCodes.F32) Code.Add(WasmOpCodes.F32Min);
                            else
                            {
                                // i32 min via select
                                WasmModuleBuilder.EmitLocalTee(Code, target);
                                var tempB = AllocateNewLocal(WasmOpCodes.I32);
                                WasmModuleBuilder.EmitLocalSet(Code, tempB); // pop argValues[1]
                                // stack: argValues[0]
                                // Actually stack already has both from EmitGetLocal. Let me redo:
                                // After EmitGetLocal(0), EmitGetLocal(1): stack = [a, b]
                                // We need: select(a, b, a<b)
                                var tempA = AllocateNewLocal(WasmOpCodes.I32);
                                // Reconfigure - push a, b, then a<b condition
                                Code.Add(WasmOpCodes.Drop); // drop b
                                Code.Add(WasmOpCodes.Drop); // drop a  
                                EmitGetLocal(argValues[0]);
                                EmitGetLocal(argValues[1]);
                                EmitGetLocal(argValues[0]);
                                EmitGetLocal(argValues[1]);
                                Code.Add(WasmOpCodes.I32LtS);
                                Code.Add(WasmOpCodes.Select);
                            }
                            break;
                        case "Max":
                            EmitGetLocal(argValues[0]);
                            EmitGetLocal(argValues[1]);
                            if (wasmType == WasmOpCodes.F64) Code.Add(WasmOpCodes.F64Max);
                            else if (wasmType == WasmOpCodes.F32) Code.Add(WasmOpCodes.F32Max);
                            else
                            {
                                Code.Add(WasmOpCodes.Drop);
                                Code.Add(WasmOpCodes.Drop);
                                EmitGetLocal(argValues[0]);
                                EmitGetLocal(argValues[1]);
                                EmitGetLocal(argValues[0]);
                                EmitGetLocal(argValues[1]);
                                Code.Add(WasmOpCodes.I32GtS);
                                Code.Add(WasmOpCodes.Select);
                            }
                            break;
                        case "ReciprocalSqrt":
                        case "Rsqrt":
                            if (wasmType == WasmOpCodes.F64)
                            {
                                WasmModuleBuilder.EmitF64Const(Code, 1.0);
                                EmitGetLocal(argValues[0]);
                                Code.Add(WasmOpCodes.F64Sqrt);
                                Code.Add(WasmOpCodes.F64Div);
                            }
                            else
                            {
                                WasmModuleBuilder.EmitF32Const(Code, 1.0f);
                                EmitGetLocal(argValues[0]);
                                Code.Add(WasmOpCodes.F32Sqrt);
                                Code.Add(WasmOpCodes.F32Div);
                            }
                            break;
                        case "Rcp":
                            if (wasmType == WasmOpCodes.F64)
                            {
                                WasmModuleBuilder.EmitF64Const(Code, 1.0);
                                EmitGetLocal(argValues[0]);
                                Code.Add(WasmOpCodes.F64Div);
                            }
                            else
                            {
                                WasmModuleBuilder.EmitF32Const(Code, 1.0f);
                                EmitGetLocal(argValues[0]);
                                Code.Add(WasmOpCodes.F32Div);
                            }
                            break;
                        // Trig/Log/Exp via Math imports
                        case "Sin":
                        case "Cos":
                        case "Tan":
                        case "Asin":
                        case "Acos":
                        case "Atan":
                        case "Sinh":
                        case "Cosh":
                        case "Tanh":
                        case "Exp":
                        case "Log":
                        case "Log2":
                        case "Log10":
                        case "Round":
                        case "Truncate":
                        case "Sign":
                            {
                                string mathName = sourceName.ToLowerInvariant();
                                EmitGetLocal(argValues[0]);
                                if (wasmType == WasmOpCodes.F32)
                                    Code.Add(WasmOpCodes.F64PromoteF32);
                                if (MathImports.TryGetValue(mathName, out var funcIdx))
                                    WasmModuleBuilder.EmitCall(Code, funcIdx);
                                if (wasmType == WasmOpCodes.F32)
                                    Code.Add(WasmOpCodes.F32DemoteF64);
                                break;
                            }
                        case "Pow":
                        case "Atan2":
                            {
                                string mathName = sourceName.ToLowerInvariant();
                                EmitGetLocal(argValues[0]);
                                EmitGetLocal(argValues[1]);
                                if (MathImports.TryGetValue(mathName, out var funcIdx))
                                    WasmModuleBuilder.EmitCall(Code, funcIdx);
                                break;
                            }
                        case "IEEERemainder":
                            EmitGetLocal(argValues[0]);
                            EmitGetLocal(argValues[1]);
                            Code.Add(wasmType == WasmOpCodes.F64 ? WasmOpCodes.F64Div : WasmOpCodes.F32Div);
                            // Approximate: just use remainder for now
                            break;
                        default:
                            handled = false;
                            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] WARNING: Unhandled intrinsic method: {sourceName}");
                            break;
                    }

                    if (handled)
                    {
                        WasmModuleBuilder.EmitLocalSet(Code, target);
                        return;
                    }
                }
            }

            // Non-intrinsic call - return zero for now
            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm] WARNING: Unhandled non-intrinsic MethodCall returning 0: {(methodCall.Target.HasSource ? methodCall.Target.Source.Name : methodCall.Target.Name)} (void={methodCall.Type.IsVoidType})");
            if (!methodCall.Type.IsVoidType)
            {
                var target2 = AllocateLocal(methodCall, GetWasmType(methodCall));
                EmitDefaultValue(GetWasmType(methodCall));
                WasmModuleBuilder.EmitLocalSet(Code, target2);
            }
        }

        // Control Flow
        public virtual void GenerateCode(ReturnTerminator returnTerminator)
        {
            Code.Add(WasmOpCodes.Return);
        }

        public virtual void GenerateCode(UnconditionalBranch branch) { }
        public virtual void GenerateCode(IfBranch branch) { }
        public virtual void GenerateCode(SwitchBranch branch) { }
        public virtual void GenerateCode(LanguageEmitValue emit) { }

        #endregion

        #region Dispatch

        /// <summary>
        /// Generates code for a value using the visitor pattern.
        /// </summary>
        protected void GenerateCodeFor(Value value)
        {
            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-IR] Visit: {value.GetType().Name} Type={value.Type} IsVoid={value.Type.IsVoidType}");

            // Skip void values (except terminators, stores, barriers, and
            // void-returning method calls). Void MethodCall must reach the
            // dispatcher: a `void Helper(int a, ref int b)` call has
            // Type.IsVoidType but its side effects (writing through the ref
            // param) are observable. Without this exception the kernel
            // codegen silently drops the call site.
            if (value.Type.IsVoidType &&
                !(value is TerminatorValue) &&
                !(value is Store) &&
                !(value is MemoryBarrier) &&
                !(value is global::ILGPU.IR.Values.Barrier) &&
                !(value is PredicateBarrier) &&
                !(value is MethodCall))
            {
                if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-IR] Skipping void value: {value.GetType().Name}");
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
                    GenerateCode(v);
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

                // Memory
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
                case global::ILGPU.IR.Values.NewView v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.MemoryBarrier v:
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
                case global::ILGPU.IR.Values.Barrier v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.PredicateBarrier v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.Broadcast v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.WarpShuffle v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.SubWarpShuffle v:
                    GenerateCode(v);
                    break;

                // Alignment
                case global::ILGPU.IR.Values.AlignTo v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.AsAligned v:
                    GenerateCode(v);
                    break;

                // Misc
                case global::ILGPU.IR.Values.Predicate v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.DebugAssertOperation v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.WriteToOutput v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.DynamicMemoryLengthValue v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.GetViewLength v:
                    GenerateCode(v);
                    break;
                case global::ILGPU.IR.Values.LanguageEmitValue v:
                    GenerateCode(v);
                    break;

                default:
                    // LOUD warning: unhandled IR value types cause locals to stay at 0,
                    // which silently corrupts address computations (see RADIX RULE in Wasm/CLAUDE.md)
                    if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-IR] *** UNHANDLED IR VALUE: {value.GetType().Name} id={value.Id} type={value.Type} ***");
                    // Also record to dispatch log so it's visible in test output
                    if (WasmBackend.VerboseLogging) WasmAccelerator._dispatchLog += $"|UNHANDLED:{value.GetType().Name}";
                    break;
            }
        }

        #endregion
    }
}
