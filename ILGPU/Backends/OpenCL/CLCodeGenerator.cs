// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: CLCodeGenerator.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.EntryPoints;
using ILGPU.IR;
using ILGPU.IR.Analyses;
using ILGPU.IR.Intrinsics;
using ILGPU.IR.Values;
using ILGPU.Util;
using System.Collections.Generic;
using System.Text;

namespace ILGPU.Backends.OpenCL
{
    /// <summary>
    /// Generates OpenCL source code out of IR values.
    /// </summary>
    /// <remarks>The code needs to be prepared for this code generator.</remarks>
    public abstract partial class CLCodeGenerator :
        CLVariableAllocator,
        IBackendCodeGenerator<StringBuilder>
    {
        #region Nested Types

        /// <summary>
        /// Generation arguments for code-generator construction.
        /// </summary>
        public readonly struct GeneratorArgs
        {
            internal GeneratorArgs(
                CLBackend backend,
                CLTypeGenerator typeGenerator,
                SeparateViewEntryPoint entryPoint,
                in AllocaKindInformation sharedAllocations,
                in AllocaKindInformation dynamicSharedAllocations)
            {
                Backend = backend;
                TypeGenerator = typeGenerator;
                EntryPoint = entryPoint;
                KernelTypeGenerator = new CLKernelTypeGenerator(
                    typeGenerator,
                    entryPoint);

                SharedAllocations = sharedAllocations;
                DynamicSharedAllocations = dynamicSharedAllocations;
            }

            /// <summary>
            /// Returns the underlying backend.
            /// </summary>
            public CLBackend Backend { get; }

            /// <summary>
            /// Returns the associated type generator.
            /// </summary>
            public CLTypeGenerator TypeGenerator { get; }

            /// <summary>
            /// Returns the current entry point.
            /// </summary>
            public SeparateViewEntryPoint EntryPoint { get; }

            /// <summary>
            /// Returns all shared allocations.
            /// </summary>
            public AllocaKindInformation SharedAllocations { get; }

            /// <summary>
            /// Returns all dynamic shared allocations.
            /// </summary>
            public AllocaKindInformation DynamicSharedAllocations { get; }

            /// <summary>
            /// Returns the current kernel-type generator.
            /// </summary>
            internal CLKernelTypeGenerator KernelTypeGenerator { get; }
        }

        /// <summary>
        /// Represents a parameter that is mapped to OpenCL.
        /// </summary>
        protected internal readonly struct MappedParameter
        {
            #region Instance

            /// <summary>
            /// Constructs a new mapped parameter.
            /// </summary>
            /// <param name="variable">The OpenCL variable.</param>
            /// <param name="clName">The name of the parameter in OpenCL code.</param>
            /// <param name="parameter">The source parameter.</param>
            public MappedParameter(
                Variable variable,
                string clName,
                Parameter parameter)
            {
                Variable = variable;
                CLName = clName;
                Parameter = parameter;
            }

            #endregion

            #region Properties

            /// <summary>
            /// Returns the associated OpenCL variable.
            /// </summary>
            public Variable Variable { get; }

            /// <summary>
            /// Returns the name of the parameter in OpenCL code.
            /// </summary>
            public string CLName { get; }

            /// <summary>
            /// Returns the source parameter.
            /// </summary>
            public Parameter Parameter { get; }

            #endregion
        }

        /// <summary>
        /// Represents a parameter logic to setup function parameters.
        /// </summary>
        protected interface IParametersSetupLogic
        {
            /// <summary>
            /// Gets the corresponding OpenCL type for the given parameter.
            /// </summary>
            /// <param name="parameter">The parameter.</param>
            /// <returns>The resulting OpenCL type representation.</returns>
            string GetParameterType(Parameter parameter);

            /// <summary>
            /// Handles an intrinsic parameter and returns the
            /// associated allocated variable (if any).
            /// </summary>
            /// <param name="parameterOffset">
            /// The current intrinsic parameter index.
            /// </param>
            /// <param name="parameter">The intrinsic parameter.</param>
            /// <returns>The allocated variable (if any).</returns>
            Variable? HandleIntrinsicParameter(int parameterOffset, Parameter parameter);
        }

        #endregion

        #region Static

        /// <summary>
        /// Returns the OpenCL function name for the given function.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <returns>The resolved OpenCL function name.</returns>
        protected static string GetMethodName(Method method)
        {
            var handleName = method.Handle.Name;
            if (method.HasFlags(MethodFlags.External))
            {
                return handleName;
            }
            else
            {
                // Constructor names in MSIL start with a dot. This is a reserved
                // character that cannot be used in the OpenCL function name. Replace
                // the dot with an underscore, assuming that the rest of the name is
                // using valid characters. Appending the IR node identifier will ensure
                // that there are no conflicts.
                return handleName.StartsWith(".", System.StringComparison.Ordinal)
                    ? handleName.Substring(1) + "_" + method.Id
                    : handleName + "_" + method.Id;
            }
        }

        /// <summary>
        /// Returns the OpenCL parameter name for the given parameter.
        /// </summary>
        /// <param name="parameter">The parameter.</param>
        /// <returns>The resolved OpenCL parameter name.</returns>
        protected static string GetParameterName(Parameter parameter) =>
            "_" + parameter.Name + "_" + parameter.Id.ToString();

        #endregion

        #region Instance

        private int labelCounter;
        private readonly Dictionary<BasicBlock, string> blockLookup =
            new Dictionary<BasicBlock, string>(new BasicBlock.Comparer());
        private readonly string labelPrefix;

        // Float16 emulation: tracks LEAs into half* buffers when cl_khr_fp16 is unavailable.
        // Key: target variable name, Value: (basePointer variable, elementIndex variable)
        // Used by Load/Store to emit vload_half/vstore_half with correct index.
        internal readonly Dictionary<string, (Variable BasePtr, Variable Index)> _f16EmulatedLEAs = new();

        // BFloat16 emulation (always emulated - no native OpenCL bf16 type): tracks LEAs into
        // ushort* buffers. Load/Store convert via _bf16_bits_to_f32 / _f32_to_bf16_bits helpers.
        internal readonly Dictionary<string, (Variable BasePtr, Variable Index)> _bf16EmulatedLEAs = new();

        // FP8 emulation (always emulated - no native OpenCL fp8 type): tracks LEAs into uchar*
        // buffers. Load/Store convert via _e4m3/_e5m2 helpers; IsE4M3 selects the format.
        internal readonly Dictionary<string, (Variable BasePtr, Variable Index, bool IsE4M3)> _fp8EmulatedLEAs = new();

        // FP4 emulation (always emulated - no native OpenCL fp4 type): tracks LEAs into uchar*
        // buffers (4-bit E2M1 value in the low nibble). Load/Store convert via _e2m1 helpers.
        internal readonly Dictionary<string, (Variable BasePtr, Variable Index)> _fp4EmulatedLEAs = new();

        private StringBuilder prefixBuilder = new StringBuilder();
        private StringBuilder suffixBuilder = new StringBuilder();

        /// <summary>
        /// Constructs a new code generator.
        /// </summary>
        /// <param name="args">The generator arguments.</param>
        /// <param name="method">The current method.</param>
        /// <param name="allocas">All local allocas.</param>
        internal CLCodeGenerator(in GeneratorArgs args, Method method, Allocas allocas)
            : base(args.TypeGenerator)
        {
            Backend = args.Backend;
            Method = method;
            ImplementationProvider = Backend.IntrinsicProvider;
            Allocas = allocas;

            labelPrefix = "L_" + Method.Id.ToString();

            Builder = prefixBuilder;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated backend.
        /// </summary>
        public CLBackend Backend { get; }

        /// <summary>
        /// Returns the associated method.
        /// </summary>
        public Method Method { get; }

        /// <summary>
        /// Returns all local allocas.
        /// </summary>
        public Allocas Allocas { get; }

        /// <summary>
        /// Returns the current intrinsic provider for code-generation purposes.
        /// </summary>
        public IntrinsicImplementationProvider<CLIntrinsic.Handler>
            ImplementationProvider
        { get; }

        /// <summary>
        /// Returns the associated string builder.
        /// </summary>
        public StringBuilder Builder { get; private set; }

        /// <summary>
        /// Returns the associated string builder.
        /// </summary>
        public StringBuilder VariableBuilder { get; } = new StringBuilder();

        #endregion

        #region IBackendCodeGenerator

        /// <summary>
        /// Generates a function declaration in PTX code.
        /// </summary>
        public abstract void GenerateHeader(StringBuilder builder);

        /// <summary>
        /// Generates PTX code.
        /// </summary>
        public abstract void GenerateCode();

        /// <summary>
        /// Generates PTX constant declarations.
        /// </summary>
        /// <param name="builder">The target builder.</param>
        public void GenerateConstants(StringBuilder builder)
        {
            // No constants to emit
        }

        /// <summary cref="IBackendCodeGenerator{TKernelBuilder}.Merge(TKernelBuilder)"/>
        public void Merge(StringBuilder builder) => builder.Append(Builder);

        #endregion

        #region General Code Generation

        /// <summary>
        /// Declares a new label.
        /// </summary>
        /// <returns>The declared label.</returns>
        private string DeclareLabel() => labelPrefix + labelCounter++;

        /// <summary>
        /// Marks the given label.
        /// </summary>
        /// <param name="label">The label to mark.</param>
        protected void MarkLabel(string label)
        {
            Builder.Append('\t');
            Builder.Append(label);
            Builder.AppendLine(": ;");
        }

        /// <summary>
        /// Generates parameter declarations by writing them to the
        /// target builder provided.
        /// </summary>
        /// <typeparam name="TSetupLogic">
        /// The dependent code-generator type to use.
        /// </typeparam>
        /// <param name="logic">The type generator to use.</param>
        /// <param name="targetBuilder">The target builder to use.</param>
        /// <param name="paramOffset">The intrinsic parameter offset.</param>
        /// <summary>
        /// Suffix for the raw-storage signature name of an emulated sub-word (bf16 / emulated Half)
        /// scalar kernel param. The signature declares the param at its 2-byte STORAGE type under this
        /// raw name (so the host's 2-byte clSetKernelArg lines up - declaring it as the 4-byte compute
        /// `float` threw CL_INVALID_ARG_SIZE); the body then converts it to the float value variable.
        /// </summary>
        private const string SubWordScalarRawSuffix = "_swraw";

        /// <summary>
        /// Emulated sub-word scalar kernel params collected during <see cref="SetupParameters"/>:
        /// (value variable name, basic value type). The kernel generator emits a storage->float
        /// conversion for each at the top of the body via <see cref="EmitSubWordScalarParamConversions"/>.
        /// </summary>
        private readonly List<(string Name, BasicValueType Type)> _subWordScalarParams = new();

        /// <summary>True if <paramref name="param"/> is a by-value scalar of an EMULATED sub-word type
        /// (bf16 + FP8 always; Half when cl_khr_fp16 is absent) - the case whose storage (1-2 bytes)
        /// differs from its OpenCL compute type (`float`, 4 bytes). Native-half scalars and full-width
        /// types are fine.</summary>
        private bool IsEmulatedSubWordScalarParam(Parameter param) =>
            param.ParameterType is ILGPU.IR.Types.PrimitiveType prim &&
            (prim.BasicValueType == BasicValueType.BFloat16 ||
             prim.BasicValueType == BasicValueType.Float8E4M3 ||
             prim.BasicValueType == BasicValueType.Float8E5M2 ||
             prim.BasicValueType == BasicValueType.Float4E2M1 ||
             (prim.BasicValueType == BasicValueType.Float16 &&
              !TypeGenerator.Capabilities.Float16Native));

        protected void SetupParameters<TSetupLogic>(
            StringBuilder targetBuilder,
            ref TSetupLogic logic,
            int paramOffset)
            where TSetupLogic : struct, IParametersSetupLogic
        {
            bool attachComma = false;
            int offset = 0;

            foreach (var param in Method.Parameters)
            {
                Variable? variable;
                if (offset < paramOffset)
                {
                    variable = logic.HandleIntrinsicParameter(offset, param);
                    offset++;
                }
                else
                {
                    variable = Allocate(param);
                }

                if (variable == null)
                    continue;

                if (attachComma)
                    targetBuilder.AppendLine(",");

                targetBuilder.Append('\t');
                if (IsEmulatedSubWordScalarParam(param))
                {
                    // Declare at 2-byte STORAGE (ushort for bf16, half for emulated Half) under a raw
                    // name; the body converts it to the float value variable (EmitSubWordScalarParam-
                    // Conversions). Matches how bf16/Half buffer ELEMENTS are 2-byte storage + converted.
                    var bvt = ((ILGPU.IR.Types.PrimitiveType)param.ParameterType)
                        .BasicValueType;
                    // Raw STORAGE under a raw name: `ushort` (2 bytes) for bf16/Half, `uchar` (1 byte)
                    // for FP8 - matching the managed raw bits the host packs. NVIDIA OpenCL rejects a
                    // `half` SCALAR arg without cl_khr_fp16, so all emulated sub-word scalars use an
                    // integer storage type + the bit-conversion helpers.
                    // FP8 (1 byte) and FP4 (1 byte, low nibble) both use uchar storage; bf16/Half ushort.
                    bool isByteScalar = bvt == BasicValueType.Float8E4M3
                        || bvt == BasicValueType.Float8E5M2
                        || bvt == BasicValueType.Float4E2M1;
                    targetBuilder.Append(isByteScalar ? "uchar" : "ushort");
                    targetBuilder.Append(' ');
                    targetBuilder.Append(variable.VariableName);
                    targetBuilder.Append(SubWordScalarRawSuffix);
                    _subWordScalarParams.Add((variable.VariableName, bvt));
                }
                else
                {
                    targetBuilder.Append(logic.GetParameterType(param));
                    targetBuilder.Append(' ');
                    targetBuilder.Append(variable.VariableName);
                }

                attachComma = true;
            }
        }

        /// <summary>
        /// Emits the storage->float conversion for each emulated sub-word scalar param collected in
        /// <see cref="SetupParameters"/>: <c>float val = _bf16_bits_to_f32(val_swraw);</c> (bf16) or
        /// <c>float val = vload_half(0, &amp;val_swraw);</c> (emulated Half). Call at the top of the
        /// kernel body, before the param value variable is used. The value variable is already bound to
        /// the IR param, so the rest of the body reads the converted float unchanged.
        /// </summary>
        protected void EmitSubWordScalarParamConversions()
        {
            foreach (var (name, type) in _subWordScalarParams)
            {
                Builder.Append("\tfloat ");
                Builder.Append(name);
                Builder.Append(" = ");
                if (type == BasicValueType.BFloat16)
                {
                    Builder.Append("_bf16_bits_to_f32(");
                    Builder.Append(name);
                    Builder.Append(SubWordScalarRawSuffix);
                    Builder.Append(')');
                }
                else if (type == BasicValueType.Float8E4M3)
                {
                    Builder.Append("_e4m3_bits_to_f32(");
                    Builder.Append(name);
                    Builder.Append(SubWordScalarRawSuffix);
                    Builder.Append(')');
                }
                else if (type == BasicValueType.Float8E5M2)
                {
                    Builder.Append("_e5m2_bits_to_f32(");
                    Builder.Append(name);
                    Builder.Append(SubWordScalarRawSuffix);
                    Builder.Append(')');
                }
                else if (type == BasicValueType.Float4E2M1)
                {
                    Builder.Append("_e2m1_bits_to_f32(");
                    Builder.Append(name);
                    Builder.Append(SubWordScalarRawSuffix);
                    Builder.Append(')');
                }
                else
                {
                    // Emulated Half: widen the raw 16 bits to f32 via the same bit helper the buffer
                    // path uses (_half_bits_to_f32(short); takes signed short - the cast is bit-preserving).
                    Builder.Append("_half_bits_to_f32((short)");
                    Builder.Append(name);
                    Builder.Append(SubWordScalarRawSuffix);
                    Builder.Append(')');
                }
                Builder.AppendLine(";");
            }
        }

        /// <summary>
        /// Setups a given allocation.
        /// </summary>
        /// <param name="allocaInfo">The single allocation to declare.</param>
        /// <param name="addressSpace">The target address space.</param>
        /// <returns>The allocated variable.</returns>
        protected Variable DeclareAllocation(
            in AllocaInformation allocaInfo,
            MemoryAddressSpace addressSpace)
        {
            var addressSpacePrefix = CLInstructions.GetAddressSpacePrefix(addressSpace);
            var allocationVariable = AllocateType(allocaInfo.ElementType);

            // Declare alloca using element-type information
            AppendIndent();
            Builder.Append(addressSpacePrefix);
            Builder.Append(' ');
            Builder.Append(TypeGenerator[allocaInfo.ElementType]);
            Builder.Append(' ');
            Builder.Append(allocationVariable.VariableName);

            if (allocaInfo.IsArray)
            {
                Builder.Append('[');
                Builder.Append(allocaInfo.ArraySize);
                Builder.Append(']');
            }

            Builder.AppendLine(";");
            return allocationVariable;
        }

        /// <summary>
        /// Setups local or shared allocations.
        /// </summary>
        /// <param name="allocas">The allocations to setup.</param>
        /// <param name="addressSpace">The source address space.local).</param>
        protected void SetupAllocations(
            AllocaKindInformation allocas,
            MemoryAddressSpace addressSpace)
        {
            foreach (var allocaInfo in allocas)
            {
                var allocationVariable = DeclareAllocation(allocaInfo, addressSpace);
                var allocaVariable = Allocate(allocaInfo.Alloca);

                // Since allocas are basically pointers in the IR we have to
                // 'convert' the local allocations into generic pointers
                using var statement = BeginStatement(allocaVariable);
                statement.AppendOperation(CLInstructions.AddressOfOperation);
                statement.Append(allocationVariable);
                if (allocaInfo.IsArray)
                    statement.AppendIndexer("0");
            }
            Builder.AppendLine();
        }

        /// <summary>
        /// Binds shared memory allocations.
        /// </summary>
        /// <param name="allocas">All allocations to bind.</param>
        protected void BindSharedMemoryAllocation(in AllocaKindInformation allocas)
        {
            foreach (var allocaInfo in allocas)
            {
                Bind(
                    allocaInfo.Alloca,
                    GetSharedMemoryAllocationVariable(allocaInfo));
            }
        }

        /// <summary>
        /// Generates code for all basic blocks.
        /// </summary>
        protected void GenerateCodeInternal()
        {
            // Setup allocations
            SetupAllocations(Allocas.LocalAllocations, MemoryAddressSpace.Local);

            // Build branch targets
            var blocks = Method.Blocks;
            foreach (var block in blocks)
                blockLookup.Add(block, DeclareLabel());

            // Find all phi nodes, allocate target registers and setup internal mapping
            var phiMapping = new Dictionary<BasicBlock, List<Variable>>(
                new BasicBlock.Comparer());
            var dominators = Method.Blocks.CreateDominators();
            var phiBindings = PhiBindings.Create(
                blocks,
                (block, phiValue) =>
                {
                    var variable = Allocate(phiValue);

                    var targetBlock = block;
                    foreach (var argument in phiValue)
                    {
                        targetBlock = argument.BasicBlock == null
                            ? dominators.Root
                            : dominators.GetImmediateCommonDominator(
                                targetBlock,
                                argument.BasicBlock);

                        if (targetBlock == dominators.Root)
                            break;
                    }

                    if (!phiMapping.TryGetValue(targetBlock, out var phiVariables))
                    {
                        phiVariables = new List<Variable>();
                        phiMapping.Add(targetBlock, phiVariables);
                    }
                    phiVariables.Add(variable);
                });
            var intermediatePhiVariables = new Dictionary<Value, Variable>(
                phiBindings.MaxNumIntermediatePhis);

            // Stash for the per-target BindPhis emit during terminator codegen.
            this.phiBindings = phiBindings;
            this.phiBindingsValid = true;
            this.intermediatePhiVariables = intermediatePhiVariables;

            // Generate code
            foreach (var block in blocks)
            {
                // Mark block label
                MarkLabel(blockLookup[block]);

                // Declare phi variables (if any)
                if (phiMapping.TryGetValue(block, out var phisToDeclare))
                {
                    foreach (var phiVariable in phisToDeclare)
                        Declare(phiVariable);
                }

                foreach (var value in block)
                {
                    // Check for intrinsic implementation
                    if (ImplementationProvider.TryGetCodeGenerator(
                        value,
                        out var intrinsicCodeGenerator))
                    {
                        // Generate specialized code for this intrinsic node
                        intrinsicCodeGenerator(Backend, this, value);
                    }
                    else
                    {
                        // Emit value
                        this.GenerateCodeFor(value);
                    }
                }

                // Per-target phi binding emission moved into the terminator emit
                // (see GenerateCode(IfBranch) / (UnconditionalBranch) / (SwitchBranch)).
                // This mirrors PTXCodeGenerator's BindPhis(target) approach and fixes
                // the "do-while loop with non-phi alias of a back-edge phi" bug:
                // emitting all bindings unconditionally before the branch caused
                // back-edge phi updates to fire even when the branch was going to a
                // non-loop successor, stomping values that should have been preserved
                // (e.g. `u = v` inside the loop where v is the loop's phi).
                // Diagnosed 2026-04-28 against SpawnDev.Codecs Av1RangeDecoderGpu.DecodeCdfQ15.

                // Build terminator
                this.GenerateCodeFor(block.Terminator.AsNotNull());
                Builder.AppendLine();
            }

            // Clear the per-method state.
            this.phiBindingsValid = false;
            this.intermediatePhiVariables = null;
        }

        /// <summary>
        /// Phi bindings stash; populated for the duration of GenerateCodeInternal so
        /// the terminator emit (per-target BindPhis) can reach it. PhiBindings is a
        /// readonly struct, not a class, so we use a `phiBindingsValid` companion flag
        /// rather than nullable-struct (which loses the value-type semantics needed for
        /// TryGetBindings).
        /// </summary>
        private PhiBindings phiBindings;
        private bool phiBindingsValid;

        /// <summary>
        /// Intermediate phi variable cache; shared across BindPhis calls within a block
        /// (a binding's source-temp save must be visible to the next BindPhis target call
        /// from the same predecessor block).
        /// </summary>
        private Dictionary<Value, Variable>? intermediatePhiVariables;

        /// <summary>
        /// Emits phi bindings for the edge from the current block to <paramref name="target"/>.
        /// When <paramref name="target"/> is null, emits all bindings (matches PTX null-target
        /// fall-back when no per-target separation is needed). Mirrors PTXCodeGenerator.BindPhis.
        /// </summary>
        /// <param name="currentBlock">The block whose terminator is being emitted.</param>
        /// <param name="target">The successor block being branched to, or null for "all".</param>
        internal void BindPhis(BasicBlock currentBlock, BasicBlock? target)
        {
            if (!phiBindingsValid || intermediatePhiVariables == null) return;
            if (!phiBindings.TryGetBindings(currentBlock, out var bindings)) return;

            // The intermediate-temp dict is per-block (scope of phi swap detection within
            // one binding set). Each new block emit starts with a fresh dict; per-target
            // calls within the SAME block share the dict so a save-then-load handoff
            // works across two BindPhis calls (e.g. for an IfBranch with both targets).
            // We clear when we re-enter a new currentBlock; the outer loop handles that
            // via the call sequence.

            foreach (var (phiValue, value) in bindings)
            {
                // Reject phis not flowing to the target edge (PTX-style filter).
                // When target is null, every binding emits.
                if (target is not null && phiValue.BasicBlock != target)
                    continue;

                var phiTargetVariable = Load(phiValue);

                if (bindings.IsIntermediate(phiValue))
                {
                    if (!intermediatePhiVariables.TryGetValue(
                            phiValue,
                            out var intermediateVariable))
                    {
                        intermediateVariable = AllocateType(phiValue.Type);
                        intermediatePhiVariables.Add(phiValue, intermediateVariable);
                        Declare(intermediateVariable);
                    }
                    Move(intermediateVariable, phiTargetVariable);
                }

                var sourceVariable = intermediatePhiVariables
                    .TryGetValue(value, out var tempVariable)
                    ? tempVariable
                    : Load(value);

                Move(phiTargetVariable, sourceVariable);
            }
        }

        /// <summary>
        /// Reset the intermediate-temp scope at the start of each terminator emit.
        /// Called from the unconditional branch / if branch / switch branch generators.
        /// </summary>
        internal void ResetPhiBindingScope()
        {
            intermediatePhiVariables?.Clear();
        }

        #endregion
    }
}
