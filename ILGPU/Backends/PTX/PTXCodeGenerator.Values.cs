// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2018-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: PTXCodeGenerator.Values.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Util;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ILGPU.Backends.PTX
{
    partial class PTXCodeGenerator
    {
        /// <summary cref="IBackendCodeGenerator.GenerateCode(MethodCall)"/>
        public void GenerateCode(MethodCall methodCall)
        {
            const string ReturnValueName = "callRetVal";
            const string CallParamName = "callParam";

            var target = methodCall.Target;

            // Create call sequence
            Builder.AppendLine();
            Builder.AppendLine("\t{");

            for (int i = 0, e = methodCall.Count; i < e; ++i)
            {
                var argument = methodCall.Nodes[i];
                var paramName = CallParamName + i;
                Builder.Append('\t');
                AppendParamDeclaration(Builder, argument.Type, paramName);
                Builder.AppendLine(";");

                // Emit store param command
                var argumentRegister = Load(argument);
                EmitStoreParam(paramName, argumentRegister);
            }

            // Reserve a sufficient amount of memory
            var returnType = target.ReturnType;
            string callCommand = Uniforms.IsUniform(methodCall)
                ? PTXInstructions.UniformMethodCall
                : PTXInstructions.MethodCall;
            if (!returnType.IsVoidType)
            {
                Builder.Append('\t');
                AppendParamDeclaration(Builder, returnType, ReturnValueName);
                Builder.AppendLine(";");
                Builder.Append('\t');
                Builder.Append(callCommand);
                Builder.Append(' ');
                Builder.Append('(');
                Builder.Append(ReturnValueName);
                Builder.Append("), ");
            }
            else
            {
                Builder.Append('\t');
                Builder.Append(callCommand);
                Builder.Append(' ');
            }
            Builder.Append(GetMethodName(target));
            Builder.AppendLine(", (");
            for (int i = 0, e = methodCall.Count; i < e; ++i)
            {
                Builder.Append("\t\t");
                Builder.Append(CallParamName);
                Builder.Append(i);
                if (i + 1 < e)
                    Builder.AppendLine(",");
                else
                    Builder.AppendLine();
            }
            Builder.AppendLine("\t);");

            if (!returnType.IsVoidType)
            {
                // Allocate target register for the return type and load the data
                var returnRegister = Allocate(methodCall);
                EmitLoadParam(ReturnValueName, returnRegister);
            }
            Builder.AppendLine("\t}");
            Builder.AppendLine();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Parameter)"/>
        public void GenerateCode(Parameter parameter)
        {
            // Parameters are already assigned to registers
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(PhiValue)"/>
        public void GenerateCode(PhiValue phiValue)
        {
            // Phi values are already assigned to registers
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(UnaryArithmeticValue)"/>
        public void GenerateCode(UnaryArithmeticValue value)
        {
            var argument = LoadPrimitive(value.Value);
            var targetRegister = AllocateHardware(value);

            using var command = BeginCommand(
                PTXInstructions.GetArithmeticOperation(
                    value.Kind,
                    value.ArithmeticBasicValueType,
                    Backend.Capabilities,
                    FastMath));
            command.AppendArgument(targetRegister);
            command.AppendArgument(argument);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(BinaryArithmeticValue)"/>
        public void GenerateCode(BinaryArithmeticValue value)
        {
            var left = LoadPrimitive(value.Left);
            var right = LoadPrimitive(value.Right);

            var targetRegister = Allocate(value, left.Description);
            using var command = BeginCommand(
                PTXInstructions.GetArithmeticOperation(
                    value.Kind,
                    value.ArithmeticBasicValueType,
                    Backend.Capabilities,
                    FastMath));
            command.AppendArgument(targetRegister);
            // PTX copysign.type d, a, b copies sign of 'a' to magnitude of 'b'.
            // The IR stores Left=magnitude, Right=sign (matching C convention),
            // so we must swap for PTX's reversed convention.
            if (value.Kind == BinaryArithmeticKind.CopySignF)
            {
                command.AppendArgument(right);  // sign source (PTX 'a')
                command.AppendArgument(left);   // magnitude source (PTX 'b')
            }
            else
            {
                command.AppendArgument(left);
                command.AppendArgument(right);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(TernaryArithmeticValue)"/>
        public void GenerateCode(TernaryArithmeticValue value)
        {
            var first = LoadPrimitive(value.First);
            var second = LoadPrimitive(value.Second);
            var third = LoadPrimitive(value.Third);


            var targetRegister = Allocate(value, first.Description);
            using var command = BeginCommand(
                PTXInstructions.GetArithmeticOperation(
                    value.Kind,
                    value.ArithmeticBasicValueType));
            command.AppendArgument(targetRegister);
            command.AppendArgument(first);
            command.AppendArgument(second);
            command.AppendArgument(third);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(CompareValue)"/>
        public void GenerateCode(CompareValue value)
        {
            var left = LoadPrimitive(value.Left);
            var right = LoadPrimitive(value.Right);

            var targetRegister = AllocateHardware(value);
            if (left.Kind == PTXRegisterKind.Predicate)
            {
                // Predicate registers require a special treatment
                using (var command = BeginCommand(
                    PTXInstructions.GetArithmeticOperation(
                        BinaryArithmeticKind.Xor,
                        ArithmeticBasicValueType.UInt1,
                        Backend.Capabilities,
                        false)))
                {
                    command.AppendArgument(targetRegister);
                    command.AppendArgument(left);
                    command.AppendArgument(right);
                }

                if (value.Kind == CompareKind.Equal)
                {
                    using var command = BeginCommand(
                        PTXInstructions.GetArithmeticOperation(
                            UnaryArithmeticKind.Not,
                            ArithmeticBasicValueType.UInt1,
                            Backend.Capabilities,
                            false));
                    command.AppendArgument(targetRegister);
                    command.AppendArgument(targetRegister);
                }
            }
            else
            {
                using var command = BeginCommand(
                    PTXInstructions.GetCompareOperation(
                        value.Kind,
                        value.Flags,
                        value.CompareType));
                command.AppendArgument(targetRegister);
                command.AppendArgument(left);
                command.AppendArgument(right);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(ConvertValue)"/>
        public void GenerateCode(ConvertValue value)
        {
            var sourceType = value.SourceType;
            var targetType = value.TargetType;

            // bf16 is held in an f32 register on PTX. It computes as f32 and is rounded
            // back to bf16 only at the store boundary - identical to the WGSL/WebGL/Wasm/
            // OpenCL backends, which all keep bf16 as an f32 local and pack at load/store.
            // Treat the bf16 endpoint(s) as f32 for the conversion.
            if (sourceType == ArithmeticBasicValueType.BFloat16 ||
                targetType == ArithmeticBasicValueType.BFloat16)
            {
                if (sourceType == ArithmeticBasicValueType.BFloat16)
                    sourceType = ArithmeticBasicValueType.Float32;
                if (targetType == ArithmeticBasicValueType.BFloat16)
                    targetType = ArithmeticBasicValueType.Float32;
                // bf16<->f32 reduces to a register no-op (the rounding happens at store).
                if (sourceType == targetType)
                {
                    Alias(value, value.Value);
                    return;
                }
            }

            // FP8 uses the SAME f32-register model: the FP8 value lives as f32 in-register and is
            // rounded to the 1-byte FP8 grid only at the store boundary (EmitF32ToFP8Bits). So an
            // FP8<->f32 (or FP8<->FP8) ConvertValue is a register no-op here - this is what makes
            // PrecisionConvert.ConvertToSingle/ConvertFromSingle<FP8> lower to nothing on PTX.
            bool srcFp8 = sourceType == ArithmeticBasicValueType.Float8E4M3
                || sourceType == ArithmeticBasicValueType.Float8E5M2;
            bool dstFp8 = targetType == ArithmeticBasicValueType.Float8E4M3
                || targetType == ArithmeticBasicValueType.Float8E5M2;
            if (srcFp8 || dstFp8)
            {
                if (srcFp8) sourceType = ArithmeticBasicValueType.Float32;
                if (dstFp8) targetType = ArithmeticBasicValueType.Float32;
                if (sourceType == targetType)
                {
                    Alias(value, value.Value);
                    return;
                }
            }

            var sourceValue = LoadPrimitive(value.Value);

            var convertOperation = PTXInstructions.GetConvertOperation(
                sourceType,
                targetType);

            var targetRegister = AllocateHardware(value);
            using var command = BeginCommand(convertOperation);
            command.AppendArgument(targetRegister);
            command.AppendArgument(sourceValue);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(IntAsPointerCast)"/>
        public void GenerateCode(IntAsPointerCast cast) => Alias(cast, cast.Value);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(PointerAsIntCast)"/>
        public void GenerateCode(PointerAsIntCast cast) => Alias(cast, cast.Value);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(PointerCast)"/>
        public void GenerateCode(PointerCast cast) => Alias(cast, cast.Value);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(FloatAsIntCast)"/>
        public void GenerateCode(FloatAsIntCast value)
        {
            if (value.Value.BasicValueType == BasicValueType.BFloat16)
            {
                // FloatAsInt(bf16): the value lives in an f32 register (f32-register model).
                // Round it to its 16-bit bf16 pattern via portable bit-manip (EmitF32ToBF16Bits -
                // every CUDA arch) into the Int16 target register (= the raw bf16 bits, exactly
                // the store-side conversion).
                var bf16Source = LoadHardware(value.Value);
                var bf16Target = AllocateHardware(value);
                EmitF32ToBF16Bits(bf16Source, bf16Target);
                return;
            }
            if (value.Value.BasicValueType == BasicValueType.Float8E4M3 ||
                value.Value.BasicValueType == BasicValueType.Float8E5M2)
            {
                // FloatAsInt(fp8): the value lives in an f32 register (f32-register model). Round
                // it to its 1-byte FP8 pattern via portable bit-manip (EmitF32ToFP8Bits - every
                // CUDA arch) into the Int8 target register (held as .b16 in PTX, low 8 bits = the
                // raw FP8 byte, exactly the store-side conversion). Drives the
                // AscendingFloat8E4M3/E5M2 radix sort (NumBits=8).
                bool isE4M3 = value.Value.BasicValueType == BasicValueType.Float8E4M3;
                var fp8Source = LoadHardware(value.Value);
                var fp8Target = AllocateHardware(value);
                EmitF32ToFP8Bits(fp8Source, fp8Target, isE4M3);
                return;
            }

            var source = LoadHardware(value.Value);
            if (source.Kind == PTXRegisterKind.Int16)
            {
                // Reuse the register, since int16 and fp16 registers are the same
                Bind(value, source);
            }
            else
            {
                Debug.Assert(
                    source.Kind == PTXRegisterKind.Float32 ||
                    source.Kind == PTXRegisterKind.Float64);

                var targetRegister = AllocateHardware(value);
                Debug.Assert(
                    targetRegister.Kind == PTXRegisterKind.Int32 ||
                    targetRegister.Kind == PTXRegisterKind.Int64);

                Move(source, targetRegister);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(IntAsFloatCast)"/>
        public void GenerateCode(IntAsFloatCast value)
        {
            if (value.BasicValueType == BasicValueType.BFloat16)
            {
                // IntAsFloat(bf16 bits): widen the 16-bit pattern (Int16 reg) to the f32 value
                // register via portable bit-manip (EmitBF16BitsToF32 - every CUDA arch). Defensive
                // symmetry - the frontend has no IntAsFloat->BFloat16 overload today.
                var src = LoadHardware(value.Value);
                var tgt = AllocateHardware(value);
                EmitBF16BitsToF32(src, tgt);
                return;
            }
            if (value.BasicValueType == BasicValueType.Float8E4M3 ||
                value.BasicValueType == BasicValueType.Float8E5M2)
            {
                // IntAsFloat(fp8 bits): widen the 1-byte pattern (Int8 reg) to the f32 value
                // register via portable bit-manip (EmitFP8BitsToF32 - every CUDA arch). Defensive
                // symmetry - the frontend has no IntAsFloat->Float8 overload today.
                bool isE4M3 = value.BasicValueType == BasicValueType.Float8E4M3;
                var src = LoadHardware(value.Value);
                var tgt = AllocateHardware(value);
                EmitFP8BitsToF32(src, tgt, isE4M3);
                return;
            }

            var source = LoadHardware(value.Value);
            if (source.Kind == PTXRegisterKind.Int16)
            {
                // Reuse the register, since int16 and fp16 registers are the same
                Bind(value, source);
            }
            else
            {
                Debug.Assert(
                    source.Kind == PTXRegisterKind.Int32 ||
                    source.Kind == PTXRegisterKind.Int64);

                var targetRegister = AllocateHardware(value);
                Debug.Assert(
                    targetRegister.Kind == PTXRegisterKind.Float32 ||
                    targetRegister.Kind == PTXRegisterKind.Float64);

                Move(source, targetRegister);
            }
        }

        /// <summary>
        /// Emits complex predicate instructions.
        /// </summary>
        private readonly struct PredicateEmitter : IComplexCommandEmitter
        {
            public PredicateEmitter(PrimitiveRegister predicateRegister)
            {
                PredicateRegister = predicateRegister;
            }

            /// <summary>
            /// The current source type.
            /// </summary>
            public PrimitiveRegister PredicateRegister { get; }

            /// <summary>
            /// Gets the actual select command.
            /// </summary>
            public string AdjustCommand(string command, PrimitiveRegister[] registers) =>
                PTXInstructions.GetSelectValueOperation(registers[0].BasicValueType);

            /// <summary>
            /// Emits nested predicates.
            /// </summary>
            public void Emit(
                CommandEmitter commandEmitter,
                PrimitiveRegister[] registers)
            {
                commandEmitter.AppendArgument(registers[0]);
                commandEmitter.AppendArgument(registers[1]);
                commandEmitter.AppendArgument(registers[2]);
                commandEmitter.AppendArgument(PredicateRegister);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Predicate)"/>
        public void GenerateCode(Predicate predicate)
        {
            var condition = LoadPrimitive(predicate.Condition);
            var trueValue = Load(predicate.TrueValue);
            var falseValue = Load(predicate.FalseValue);

            var targetRegister = Allocate(predicate);
            if (predicate.BasicValueType == BasicValueType.Int1)
            {
                // We need a specific sequence of instructions for predicate registers
                var conditionRegister = EnsureHardwareRegister(condition);
                using (var statement1 = BeginMove(
                    new PredicateConfiguration(conditionRegister, true)))
                {
                    statement1.AppendSuffix(BasicValueType.Int1);
                    statement1.AppendArgument(
                        targetRegister.AsNotNullCast<PrimitiveRegister>());
                    statement1.AppendArgument(
                        trueValue.AsNotNullCast<PrimitiveRegister>());
                }

                using var statement2 = BeginMove(
                    new PredicateConfiguration(conditionRegister, false));
                statement2.AppendSuffix(BasicValueType.Int1);
                statement2.AppendArgument(
                    targetRegister.AsNotNullCast<PrimitiveRegister>());
                statement2.AppendArgument(
                    falseValue.AsNotNullCast<PrimitiveRegister>());
            }
            else
            {
                EmitComplexCommand(
                    string.Empty,
                    new PredicateEmitter(condition),
                    targetRegister,
                    trueValue,
                    falseValue);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GenericAtomic)"/>
        public void GenerateCode(GenericAtomic atomic)
        {
            var target = LoadHardware(atomic.Target);
            var value = LoadPrimitive(atomic.Value);

            var requiresResult =
                atomic.Uses.HasAny ||
                atomic.Kind == AtomicKind.Exchange;
            var atomicOperation = PTXInstructions.GetAtomicOperation(
                atomic.Kind,
                requiresResult);
            var suffix = PTXInstructions.GetAtomicOperationSuffix(
                atomic.Kind,
                atomic.ArithmeticBasicValueType);

            var targetRegister = requiresResult ? AllocateHardware(atomic) : default;
            using var command = BeginCommand(atomicOperation);
            command.AppendNonLocalAddressSpace(
                atomic.Target.Type.AsNotNullCast<AddressSpaceType>().AddressSpace);
            command.AppendSuffix(suffix);
            if (requiresResult)
                command.AppendArgument(targetRegister.AsNotNull());
            command.AppendArgumentValue(target);
            command.AppendArgument(value);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(AtomicCAS)"/>
        public void GenerateCode(AtomicCAS atomicCAS)
        {
            var target = LoadHardware(atomicCAS.Target);
            var value = LoadPrimitive(atomicCAS.Value);
            var compare = LoadPrimitive(atomicCAS.CompareValue);

            var targetRegister = AllocateHardware(atomicCAS);

            using var command = BeginCommand(PTXInstructions.AtomicCASOperation);
            command.AppendNonLocalAddressSpace(
                atomicCAS.Target.Type.AsNotNullCast<AddressSpaceType>().AddressSpace);
            command.AppendSuffix(atomicCAS.BasicValueType);
            command.AppendArgument(targetRegister);
            command.AppendArgumentValue(target);
            command.AppendArgument(value);
            command.AppendArgument(compare);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Alloca)"/>
        public void GenerateCode(Alloca alloca)
        {
            // Ignore alloca
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(MemoryBarrier)"/>
        public void GenerateCode(MemoryBarrier barrier)
        {
            var command = PTXInstructions.GetMemoryBarrier(barrier.Kind);
            Command(command);
        }

        /// <summary>
        /// Emits complex load instructions.
        /// </summary>
        private readonly struct LoadEmitter : IVectorizedCommandEmitter
        {
            private readonly struct IOEmitter : IIOEmitter<int>
            {
                public IOEmitter(
                    PointerType sourceType,
                    HardwareRegister addressRegister)
                {
                    SourceType = sourceType;
                    AddressRegister = addressRegister;
                }

                /// <summary>
                /// The current source type.
                /// </summary>
                public PointerType SourceType { get; }

                /// <summary>
                /// Returns the associated address register.
                /// </summary>
                public HardwareRegister AddressRegister { get; }

                /// <summary>
                /// Emits nested loads.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void Emit(
                    PTXCodeGenerator codeGenerator,
                    string command,
                    PrimitiveRegister register,
                    int offset)
                {
                    using var commandEmitter = codeGenerator.BeginCommand(command);
                    commandEmitter.AppendAddressSpace(SourceType.AddressSpace);
                    commandEmitter.AppendSuffix(
                        ResolveIOType(register.BasicValueType));
                    commandEmitter.AppendArgument(register);
                    commandEmitter.AppendArgumentValue(AddressRegister, offset);
                }
            }

            public LoadEmitter(
                PointerType sourceType,
                HardwareRegister addressRegister)
            {
                Emitter = new IOEmitter(sourceType, addressRegister);
            }

            /// <summary>
            /// The underlying IO emitter.
            /// </summary>
            private IOEmitter Emitter { get; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Emit(
                PTXCodeGenerator codeGenerator,
                string command,
                PrimitiveRegister register,
                int offset) =>
                codeGenerator.EmitIOLoad(
                    Emitter,
                    command,
                    register.AsNotNullCast<HardwareRegister>(),
                    offset);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Emit(
                PTXCodeGenerator codeGenerator,
                string command,
                PrimitiveRegister[] primitiveRegisters,
                int offset)
            {
                using var commandEmitter = codeGenerator.BeginCommand(command);
                commandEmitter.AppendAddressSpace(Emitter.SourceType.AddressSpace);
                commandEmitter.AppendVectorSuffix(primitiveRegisters.Length);
                commandEmitter.AppendSuffix(
                    ResolveIOType(primitiveRegisters[0].BasicValueType));
                commandEmitter.AppendVectorArgument(primitiveRegisters);
                commandEmitter.AppendArgumentValue(Emitter.AddressRegister, offset);
            }
        }

        /// <summary>
        /// Emits a PORTABLE bf16-bits (16-bit value in a .b16 reg) -&gt; f32 conversion using only
        /// basic integer ops (works on EVERY CUDA arch, incl. pre-Ampere sm_61/sm_75). bf16 is the
        /// top 16 bits of an fp32, so zero-extend + left-shift-16 + reinterpret is exact. Replaces
        /// the native <c>cvt.f32.bf16</c>, which is sm_80+ ONLY and fails to compile on older cards
        /// (Pascal/Volta/Turing). Byte-identical to the managed/WGSL/GLSL/Wasm bf16 conversion.
        /// </summary>
        private void EmitBF16BitsToF32(HardwareRegister srcB16, HardwareRegister dstF32)
        {
            var t = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            using (var cmd = BeginCommand("cvt.u32.u16")) // zero-extend the 16 bf16 bits to 32
            {
                cmd.AppendArgument(t);
                cmd.AppendArgument(srcB16);
            }
            using (var cmd = BeginCommand("shl.b32")) // shift into the fp32 high half
            {
                cmd.AppendArgument(t);
                cmd.AppendArgument(t);
                cmd.AppendConstant(16);
            }
            using (var cmd = BeginCommand("mov.b32")) // reinterpret the bits as f32
            {
                cmd.AppendArgument(dstF32);
                cmd.AppendArgument(t);
            }
            FreeRegister(t);
        }

        /// <summary>
        /// Emits a PORTABLE f32 -&gt; bf16-bits (16-bit value in a .b16 reg) conversion using only
        /// basic integer ops (works on EVERY CUDA arch). Round-to-nearest-even with NaN preservation
        /// (a naive truncate collapses some NaNs to Inf), byte-identical to the managed/Wasm
        /// <c>EmitF32ToBF16</c>. Replaces the native <c>cvt.rn.bf16.f32</c> (sm_80+ only).
        /// </summary>
        private void EmitF32ToBF16Bits(HardwareRegister srcF32, HardwareRegister dstB16)
        {
            var bits = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var lsb = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var rounded = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var result = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var nan = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var absb = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var p = AllocateRegister(BasicValueType.Int1, PTXRegisterKind.Predicate);

            using (var cmd = BeginCommand("mov.b32")) { cmd.AppendArgument(bits); cmd.AppendArgument(srcF32); }
            // lsb = (bits >> 16) & 1
            using (var cmd = BeginCommand("shr.u32")) { cmd.AppendArgument(lsb); cmd.AppendArgument(bits); cmd.AppendConstant(16); }
            using (var cmd = BeginCommand("and.b32")) { cmd.AppendArgument(lsb); cmd.AppendArgument(lsb); cmd.AppendConstant(1); }
            // rounded = bits + 0x7FFF + lsb
            using (var cmd = BeginCommand("add.s32")) { cmd.AppendArgument(rounded); cmd.AppendArgument(bits); cmd.AppendConstant(0x7FFF); }
            using (var cmd = BeginCommand("add.s32")) { cmd.AppendArgument(rounded); cmd.AppendArgument(rounded); cmd.AppendArgument(lsb); }
            // result = (rounded >> 16) & 0xFFFF
            using (var cmd = BeginCommand("shr.u32")) { cmd.AppendArgument(result); cmd.AppendArgument(rounded); cmd.AppendConstant(16); }
            using (var cmd = BeginCommand("and.b32")) { cmd.AppendArgument(result); cmd.AppendArgument(result); cmd.AppendConstant(0xFFFF); }
            // NaN override: (bits & 0x7FFFFFFF) > 0x7F800000 ? (((bits>>16)|0x40)&0xFFFF) : result
            using (var cmd = BeginCommand("and.b32")) { cmd.AppendArgument(absb); cmd.AppendArgument(bits); cmd.AppendConstant(0x7FFFFFFF); }
            using (var cmd = BeginCommand("setp.gt.u32")) { cmd.AppendArgument(p); cmd.AppendArgument(absb); cmd.AppendConstant(0x7F800000); }
            using (var cmd = BeginCommand("shr.u32")) { cmd.AppendArgument(nan); cmd.AppendArgument(bits); cmd.AppendConstant(16); }
            using (var cmd = BeginCommand("or.b32")) { cmd.AppendArgument(nan); cmd.AppendArgument(nan); cmd.AppendConstant(0x40); }
            using (var cmd = BeginCommand("and.b32")) { cmd.AppendArgument(nan); cmd.AppendArgument(nan); cmd.AppendConstant(0xFFFF); }
            using (var cmd = BeginCommand("selp.b32")) { cmd.AppendArgument(result); cmd.AppendArgument(nan); cmd.AppendArgument(result); cmd.AppendArgument(p); }
            // dstB16 = (u16)result
            using (var cmd = BeginCommand("cvt.u16.u32")) { cmd.AppendArgument(dstB16); cmd.AppendArgument(result); }

            FreeRegister(bits); FreeRegister(lsb); FreeRegister(rounded);
            FreeRegister(result); FreeRegister(nan); FreeRegister(absb); FreeRegister(p);
        }

        /// <summary>
        /// Emits a PORTABLE FP8 raw-byte (in a .b16/.b32 reg) -&gt; f32 conversion using only basic
        /// integer ops (every CUDA arch). <paramref name="isE4M3"/> selects E4M3FN vs E5M2. Branchless
        /// (setp/selp), subnormal-normalize UNROLLED. Byte-identical to the managed/OpenCL/WGSL/GLSL/Wasm
        /// FP8 conversion (CPU-verified 0/256). FP8 has NO native PTX cvt on the cards we target.
        /// </summary>
        private void EmitFP8BitsToF32(HardwareRegister srcByte, HardwareRegister dstF32, bool isE4M3)
        {
            int mantBits = isE4M3 ? 3 : 2;
            int expMask = isE4M3 ? 0x0F : 0x1F;
            int bias = isE4M3 ? 7 : 15;
            int mantMask = isE4M3 ? 0x07 : 0x03;
            int mantShift = 23 - mantBits;
            int implicitBit = 1 << mantBits;
            int normShifts = isE4M3 ? 3 : 2;

            var bits = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var sign = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var expo = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var mant = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var result = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var e = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var m = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var sub = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var t = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var t2 = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var p = AllocateRegister(BasicValueType.Int1, PTXRegisterKind.Predicate);

            void Emit(string op, params HardwareRegister[] args)
            { using var c = BeginCommand(op); foreach (var a in args) c.AppendArgument(a); }
            void EmitI(string op, HardwareRegister d, HardwareRegister a, long imm)
            { using var c = BeginCommand(op); c.AppendArgument(d); c.AppendArgument(a); c.AppendConstant(imm); }

            // bits = (u32)srcByte & 0xFF   (srcByte may be .b16; widen then mask)
            using (var c = BeginCommand("cvt.u32.u16")) { c.AppendArgument(bits); c.AppendArgument(srcByte); }
            EmitI("and.b32", bits, bits, 0xFF);
            // sign = (bits & 0x80) << 24
            EmitI("and.b32", sign, bits, 0x80);
            EmitI("shl.b32", sign, sign, 24);
            // expo = (bits >> mantBits) & expMask
            EmitI("shr.u32", expo, bits, mantBits);
            EmitI("and.b32", expo, expo, expMask);
            // mant = bits & mantMask
            EmitI("and.b32", mant, bits, mantMask);

            // NORMAL: result = sign | ((expo + (127-bias)) << 23) | (mant << mantShift)
            EmitI("add.s32", t, expo, 127 - bias);
            EmitI("shl.b32", t, t, 23);
            Emit("or.b32", result, sign, t);
            EmitI("shl.b32", t, mant, mantShift);
            Emit("or.b32", result, result, t);

            // SUBNORMAL: e = 127-bias+1; m = mant; normalize (unrolled); sub = sign|(e<<23)|((m&mantMask)<<mantShift)
            using (var c = BeginCommand("mov.u32")) { c.AppendArgument(e); c.AppendConstant(127 - bias + 1); }
            Emit("mov.u32", m, mant);
            for (int s = 0; s < normShifts; s++)
            {
                // p = (m & implicitBit) == 0
                EmitI("and.b32", t, m, implicitBit);
                using (var c = BeginCommand("setp.eq.s32")) { c.AppendArgument(p); c.AppendArgument(t); c.AppendConstant(0); }
                // m = p ? m<<1 : m ; e = p ? e-1 : e
                EmitI("shl.b32", t, m, 1);
                using (var c = BeginCommand("selp.b32")) { c.AppendArgument(m); c.AppendArgument(t); c.AppendArgument(m); c.AppendArgument(p); }
                EmitI("sub.s32", t2, e, 1);
                using (var c = BeginCommand("selp.b32")) { c.AppendArgument(e); c.AppendArgument(t2); c.AppendArgument(e); c.AppendArgument(p); }
            }
            EmitI("shl.b32", t, e, 23);
            Emit("or.b32", sub, sign, t);
            EmitI("and.b32", t, m, mantMask);
            EmitI("shl.b32", t, t, mantShift);
            Emit("or.b32", sub, sub, t);

            // if expo==0: result = (mant==0 ? sign : sub)
            using (var c = BeginCommand("setp.eq.s32")) { c.AppendArgument(p); c.AppendArgument(mant); c.AppendConstant(0); }
            using (var c = BeginCommand("selp.b32")) { c.AppendArgument(t); c.AppendArgument(sign); c.AppendArgument(sub); c.AppendArgument(p); } // t = mant==0?sign:sub
            using (var c = BeginCommand("setp.eq.s32")) { c.AppendArgument(p); c.AppendArgument(expo); c.AppendConstant(0); }
            using (var c = BeginCommand("selp.b32")) { c.AppendArgument(result); c.AppendArgument(t); c.AppendArgument(result); c.AppendArgument(p); }

            if (isE4M3)
            {
                // E4M3 NaN: (bits & 0x7F) == 0x7F -> result = sign | 0x7FC00000
                EmitI("and.b32", t, bits, 0x7F);
                using (var c = BeginCommand("setp.eq.s32")) { c.AppendArgument(p); c.AppendArgument(t); c.AppendConstant(0x7F); }
                EmitI("or.b32", t, sign, 0x7FC00000);
                using (var c = BeginCommand("selp.b32")) { c.AppendArgument(result); c.AppendArgument(t); c.AppendArgument(result); c.AppendArgument(p); }
            }
            else
            {
                // E5M2 expo==0x1F -> Inf/NaN: result = sign | (0xFF<<23) | (mant<<21)
                EmitI("shl.b32", t, mant, mantShift);
                EmitI("or.b32", t, t, 0xFF << 23);
                Emit("or.b32", t, t, sign);
                using (var c = BeginCommand("setp.eq.s32")) { c.AppendArgument(p); c.AppendArgument(expo); c.AppendConstant(0x1F); }
                using (var c = BeginCommand("selp.b32")) { c.AppendArgument(result); c.AppendArgument(t); c.AppendArgument(result); c.AppendArgument(p); }
            }

            // dstF32 = reinterpret(result)
            using (var c = BeginCommand("mov.b32")) { c.AppendArgument(dstF32); c.AppendArgument(result); }

            FreeRegister(bits); FreeRegister(sign); FreeRegister(expo); FreeRegister(mant);
            FreeRegister(result); FreeRegister(e); FreeRegister(m); FreeRegister(sub);
            FreeRegister(t); FreeRegister(t2); FreeRegister(p);
        }

        /// <summary>
        /// Emits a PORTABLE f32 -&gt; FP8 raw-byte (low 8 bits in dst .b16) conversion using only basic
        /// integer ops (every CUDA arch). Branchless (setp/selp), RNE rounding; E4M3 = fn (finite
        /// overflow AND Inf -&gt; NaN, float8_e4m3fn), E5M2 overflows to Inf. Byte-identical to the managed/Wasm
        /// ConvertFloatToFloat8E*M* (CPU-verified). The subnormal shift is clamped (PTX shr is UB for
        /// shift&gt;=32) and edge-guarded to match the managed return-0 cases.
        /// </summary>
        private void EmitF32ToFP8Bits(HardwareRegister srcF32, HardwareRegister dstByte, bool isE4M3)
        {
            int mantBits = isE4M3 ? 3 : 2;
            int bias = isE4M3 ? 7 : 15;
            int dropBits = 23 - mantBits;
            int eMin = isE4M3 ? -6 : -14;

            var bits = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var sign = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var rest = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var f32Exp = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var f32Mant = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var ev = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var result = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var nrm = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var sub = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var signif = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var shift = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var sshift = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var mt = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var rb = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var stk = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var t = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var t2 = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
            var p = AllocateRegister(BasicValueType.Int1, PTXRegisterKind.Predicate);
            var p2 = AllocateRegister(BasicValueType.Int1, PTXRegisterKind.Predicate);

            void Emit(string op, params HardwareRegister[] a) { using var c = BeginCommand(op); foreach (var x in a) c.AppendArgument(x); }
            void EmitI(string op, HardwareRegister d, HardwareRegister a, long imm) { using var c = BeginCommand(op); c.AppendArgument(d); c.AppendArgument(a); c.AppendConstant(imm); }
            void MovI(HardwareRegister d, long imm) { using var c = BeginCommand("mov.u32"); c.AppendArgument(d); c.AppendConstant(imm); }
            void SetpI(string op, HardwareRegister pr, HardwareRegister a, long imm) { using var c = BeginCommand(op); c.AppendArgument(pr); c.AppendArgument(a); c.AppendConstant(imm); }
            void Selp(HardwareRegister d, HardwareRegister tv, HardwareRegister fv, HardwareRegister pr) { using var c = BeginCommand("selp.b32"); c.AppendArgument(d); c.AppendArgument(tv); c.AppendArgument(fv); c.AppendArgument(pr); }

            // bits = reinterpret(srcF32); sign = (bits>>24)&0x80; rest = bits & 0x7FFFFFFF
            using (var c = BeginCommand("mov.b32")) { c.AppendArgument(bits); c.AppendArgument(srcF32); }
            EmitI("shr.u32", sign, bits, 24);
            EmitI("and.b32", sign, sign, 0x80);
            EmitI("and.b32", rest, bits, 0x7FFFFFFF);
            // f32Exp = (rest>>23)&0xFF; f32Mant = rest & 0x7FFFFF; ev = f32Exp - 127
            EmitI("shr.u32", f32Exp, rest, 23);
            EmitI("and.b32", f32Exp, f32Exp, 0xFF);
            EmitI("and.b32", f32Mant, rest, 0x7FFFFF);
            EmitI("sub.s32", ev, f32Exp, 127);

            // ---- NORMAL candidate: round 23->mantBits RNE ----
            // mt = f32Mant >> dropBits ; rb = (f32Mant>>(dropBits-1))&1 ; stk = (f32Mant & ((1<<(dropBits-1))-1))!=0
            EmitI("shr.u32", mt, f32Mant, dropBits);
            EmitI("shr.u32", rb, f32Mant, dropBits - 1);
            EmitI("and.b32", rb, rb, 1);
            EmitI("and.b32", t, f32Mant, (1 << (dropBits - 1)) - 1);
            using (var c = BeginCommand("setp.ne.s32")) { c.AppendArgument(p); c.AppendArgument(t); c.AppendConstant(0); }
            MovI(stk, 0); MovI(t2, 1); Selp(stk, t2, stk, p);
            // nrm = ((ev+bias)<<mantBits) | mt
            EmitI("add.s32", t, ev, bias);
            EmitI("shl.b32", t, t, mantBits);
            Emit("or.b32", nrm, t, mt);
            // roundUp if rb==1 && (stk!=0 || (mt&1))
            EmitI("and.b32", t, mt, 1);
            Emit("or.b32", t, stk, t);
            SetpI("setp.ne.s32", p, t, 0);          // (stk||mt&1)
            SetpI("setp.eq.s32", p2, rb, 1);        // rb==1
            using (var c = BeginCommand("and.pred")) { c.AppendArgument(p); c.AppendArgument(p); c.AppendArgument(p2); }
            EmitI("add.s32", t, nrm, 1);
            Selp(nrm, t, nrm, p);
            if (isE4M3)
            {
                // fn: if nrm (FULL, incl a 0x80 carry) reaches the 0x7F slot -> 0x7F (NaN).
                // Compare nrm directly (not masked) so the round-up-past-448 carry is caught.
                using (var c = BeginCommand("setp.ge.u32")) { c.AppendArgument(p); c.AppendArgument(nrm); c.AppendConstant(0x7F); }
                MovI(t2, 0x7F); Selp(nrm, t2, nrm, p);
            }
            EmitI("and.b32", nrm, nrm, 0x7F);
            Emit("or.b32", nrm, sign, nrm);

            // ---- SUBNORMAL candidate (ev<eMin) ----
            // signif = f32Mant | 0x800000 ; shift = (eMin-ev)+dropBits
            EmitI("or.b32", signif, f32Mant, 0x800000);
            MovI(t, eMin);
            Emit("sub.s32", shift, t, ev);           // eMin - ev  (>=1)
            EmitI("add.s32", shift, shift, dropBits);
            // sshift = min(shift,31)
            MovI(t, 31);
            using (var c = BeginCommand("min.s32")) { c.AppendArgument(sshift); c.AppendArgument(shift); c.AppendArgument(t); }
            // mt = signif >> sshift
            Emit("shr.u32", mt, signif, sshift);
            // rb = (signif >> (sshift-1)) & 1
            EmitI("sub.s32", t, sshift, 1);
            Emit("shr.u32", rb, signif, t);
            EmitI("and.b32", rb, rb, 1);
            // stk = (signif & ((1<<(sshift-1))-1)) != 0
            MovI(t2, 1);
            Emit("shl.b32", t2, t2, t);              // 1 << (sshift-1)
            EmitI("sub.s32", t2, t2, 1);
            Emit("and.b32", t2, signif, t2);
            SetpI("setp.ne.s32", p, t2, 0);
            MovI(stk, 0); MovI(t, 1); Selp(stk, t, stk, p);
            // roundUp if rb==1 && (stk||mt&1)
            EmitI("and.b32", t, mt, 1);
            Emit("or.b32", t, stk, t);
            SetpI("setp.ne.s32", p, t, 0);
            SetpI("setp.eq.s32", p2, rb, 1);
            using (var c = BeginCommand("and.pred")) { c.AppendArgument(p); c.AppendArgument(p); c.AppendArgument(p2); }
            EmitI("add.s32", t, mt, 1);
            Selp(mt, t, mt, p);
            // sub = sign | (mt & 0x7F)
            EmitI("and.b32", t, mt, 0x7F);
            Emit("or.b32", sub, sign, t);
            // guards: f32Exp==0 -> sign ; shift>31 -> sign
            SetpI("setp.eq.s32", p, f32Exp, 0);
            Selp(sub, sign, sub, p);
            SetpI("setp.gt.s32", p, shift, 31);
            Selp(sub, sign, sub, p);

            // ---- assemble: result = normal; if ev<eMin -> sub; if overflow -> sat/Inf; if NaN/Inf -> special
            Emit("mov.u32", result, nrm);
            SetpI("setp.lt.s32", p, ev, eMin);
            Selp(result, sub, result, p);

            // overflow
            if (isE4M3)
            {
                // fn: only ev>8 is unconditional overflow -> sign|0x7F (NaN). ev==8 is handled by
                // the normal RNE path + its full-outBits>=0x7F clamp (449->448, >464->NaN).
                SetpI("setp.gt.s32", p, ev, 8);
                EmitI("or.b32", t, sign, 0x7F);
                Selp(result, t, result, p);
            }
            else
            {
                // ev>15 -> sign|0x7C
                SetpI("setp.gt.s32", p, ev, 15);
                EmitI("or.b32", t, sign, 0x7C);
                Selp(result, t, result, p);
            }

            // NaN/Inf input (highest precedence)
            if (isE4M3)
            {
                // rest >= 0x7F800000 -> sign | 0x7F
                using (var c = BeginCommand("setp.ge.u32")) { c.AppendArgument(p); c.AppendArgument(rest); c.AppendConstant(0x7F800000); }
                EmitI("or.b32", t, sign, 0x7F);
                Selp(result, t, result, p);
            }
            else
            {
                // rest > 0x7F800000 -> sign|0x7F (NaN) ; rest == 0x7F800000 -> sign|0x7C (Inf)
                EmitI("or.b32", t, sign, 0x7C);
                using (var c = BeginCommand("setp.eq.s32")) { c.AppendArgument(p); c.AppendArgument(rest); c.AppendConstant(0x7F800000); }
                Selp(result, t, result, p);
                EmitI("or.b32", t, sign, 0x7F);
                using (var c = BeginCommand("setp.gt.u32")) { c.AppendArgument(p); c.AppendArgument(rest); c.AppendConstant(0x7F800000); }
                Selp(result, t, result, p);
            }

            // dstByte = (u16)(result & 0xFF)
            EmitI("and.b32", result, result, 0xFF);
            using (var c = BeginCommand("cvt.u16.u32")) { c.AppendArgument(dstByte); c.AppendArgument(result); }

            FreeRegister(bits); FreeRegister(sign); FreeRegister(rest); FreeRegister(f32Exp);
            FreeRegister(f32Mant); FreeRegister(ev); FreeRegister(result); FreeRegister(nrm);
            FreeRegister(sub); FreeRegister(signif); FreeRegister(shift); FreeRegister(sshift);
            FreeRegister(mt); FreeRegister(rb); FreeRegister(stk); FreeRegister(t); FreeRegister(t2);
            FreeRegister(p); FreeRegister(p2);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Load)"/>
        public void GenerateCode(Load load)
        {
            var address = LoadHardware(load.Source);
            var sourceType = load.Source.Type.AsNotNullCast<PointerType>();

            if (load.Type.BasicValueType == BasicValueType.BFloat16)
            {
                // bf16 storage is a packed 16-bit value (the top half of an fp32). Load the
                // raw 16 bits into a temp .b16 register, then widen to the f32 value
                // register via portable bit-manip (EmitBF16BitsToF32 - works on every CUDA arch,
                // unlike the sm_80+ cvt.f32.bf16). bf16 computes as
                // f32 in-register and is re-rounded only at the store boundary - matching
                // the WGSL/WebGL/Wasm/OpenCL backends.
                var bf16Target = AllocateHardware(load);
                var rawReg = AllocateRegister(
                    BasicValueType.Int16,
                    PTXRegisterKind.Int16);
                using (var cmd = BeginCommand(PTXInstructions.LoadOperation))
                {
                    cmd.AppendAddressSpace(sourceType.AddressSpace);
                    cmd.AppendSuffix("b16");
                    cmd.AppendArgument(rawReg);
                    cmd.AppendArgumentValue(address, 0);
                }
                EmitBF16BitsToF32(rawReg, bf16Target);
                FreeRegister(rawReg);
                return;
            }

            if (load.Type.BasicValueType == BasicValueType.Float8E4M3 ||
                load.Type.BasicValueType == BasicValueType.Float8E5M2)
            {
                // FP8 storage is a packed 1-byte value; load it into a temp .b16 register, then
                // widen to the f32 value register via portable bit-manip (every CUDA arch - FP8
                // has no native PTX cvt on the cards we target). f32-register model like bf16.
                bool isE4M3 = load.Type.BasicValueType == BasicValueType.Float8E4M3;
                var fp8Target = AllocateHardware(load);
                var rawReg = AllocateRegister(BasicValueType.Int16, PTXRegisterKind.Int16);
                using (var cmd = BeginCommand(PTXInstructions.LoadOperation))
                {
                    cmd.AppendAddressSpace(sourceType.AddressSpace);
                    cmd.AppendSuffix("u8");
                    cmd.AppendArgument(rawReg);
                    cmd.AppendArgumentValue(address, 0);
                }
                EmitFP8BitsToF32(rawReg, fp8Target, isE4M3);
                FreeRegister(rawReg);
                return;
            }

            var targetRegister = Allocate(load);

            EmitVectorizedCommand(
                load.Source,
                sourceType.ElementType.Alignment,
                PTXInstructions.LoadOperation,
                new LoadEmitter(sourceType, address),
                targetRegister);
        }

        /// <summary>
        /// Emits complex store instructions.
        /// </summary>
        private readonly struct StoreEmitter : IVectorizedCommandEmitter
        {
            private readonly struct IOEmitter : IIOEmitter<int>
            {
                public IOEmitter(
                    PointerType targetType,
                    HardwareRegister addressRegister)
                {
                    TargetType = targetType;
                    AddressRegister = addressRegister;
                }

                /// <summary>
                /// The current source type.
                /// </summary>
                public PointerType TargetType { get; }

                /// <summary>
                /// Returns the associated address register.
                /// </summary>
                public HardwareRegister AddressRegister { get; }

                /// <summary>
                /// Emits nested stores.
                /// </summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                public void Emit(
                    PTXCodeGenerator codeGenerator,
                    string command,
                    PrimitiveRegister register,
                    int offset)
                {
                    using var commandEmitter = codeGenerator.BeginCommand(command);
                    commandEmitter.AppendAddressSpace(TargetType.AddressSpace);
                    commandEmitter.AppendSuffix(
                        ResolveIOType(register.BasicValueType));
                    commandEmitter.AppendArgumentValue(AddressRegister, offset);
                    commandEmitter.AppendArgument(register);
                }
            }

            public StoreEmitter(
                PointerType targetType,
                HardwareRegister addressRegister)
            {
                Emitter = new IOEmitter(targetType, addressRegister);
            }

            /// <summary>
            /// The underlying IO emitter.
            /// </summary>
            private IOEmitter Emitter { get; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Emit(
                PTXCodeGenerator codeGenerator,
                string command,
                PrimitiveRegister register,
                int offset) =>
                codeGenerator.EmitIOStore(
                    Emitter,
                    command,
                    register,
                    offset);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Emit(
                PTXCodeGenerator codeGenerator,
                string command,
                PrimitiveRegister[] primitiveRegisters,
                int offset)
            {
                using var commandEmitter = codeGenerator.BeginCommand(command);
                commandEmitter.AppendAddressSpace(Emitter.TargetType.AddressSpace);
                commandEmitter.AppendVectorSuffix(primitiveRegisters.Length);
                commandEmitter.AppendSuffix(
                    ResolveIOType(primitiveRegisters[0].BasicValueType));
                commandEmitter.AppendArgumentValue(Emitter.AddressRegister, offset);
                commandEmitter.AppendVectorArgument(primitiveRegisters);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Store)"/>
        public void GenerateCode(Store store)
        {
            var address = LoadHardware(store.Target);
            var targetType = store.Target.Type.AsNotNullCast<PointerType>();
            var value = Load(store.Value);

            // Key the bf16-narrowing store off the TARGET BUFFER element type, NOT the value type.
            // bf16 is held as f32 in-register, and the `(float)bf16` widening Convert is a no-op
            // alias - so the VALUE reaching a `floatBuf[i] = (float)bf16Buf[i]` store is still typed
            // BFloat16. Keying off the value type made that store narrow back to bf16 + st.b16 (2 bytes)
            // into a 4-byte float slot -> the float read back ~0 (Tuvok's "bf16 store/load returns
            // zeros" bug). The store must match the destination buffer: bf16* -> cvt.rn.bf16.f32 + st.b16;
            // f32* -> plain st.f32 of the f32 value register. (Also fixes the inverse: storing an f32
            // result into a bf16 buffer now narrows correctly instead of st.f32 overflowing the 2-byte slot.)
            if (targetType.ElementType.BasicValueType == BasicValueType.BFloat16)
            {
                // bf16 store: round the f32 value register to bf16 via portable bit-manip
                // (EmitF32ToBF16Bits - RNE + NaN guard, works on every CUDA arch unlike the
                // sm_80+ cvt.rn.bf16.f32) into a temp .b16 register, then write the raw 16 bits.
                // EnsureHardwareRegister materializes a bf16 constant into an f32 register first.
                // Rounds identically to every other backend.
                var valueReg = EnsureHardwareRegister(
                    value.AsNotNullCast<PrimitiveRegister>());
                var rawReg = AllocateRegister(
                    BasicValueType.Int16,
                    PTXRegisterKind.Int16);
                EmitF32ToBF16Bits(valueReg, rawReg);
                using (var cmd = BeginCommand(PTXInstructions.StoreOperation))
                {
                    cmd.AppendAddressSpace(targetType.AddressSpace);
                    cmd.AppendSuffix("b16");
                    cmd.AppendArgumentValue(address, 0);
                    cmd.AppendArgument(rawReg);
                }
                FreeRegister(rawReg);
                return;
            }

            if (targetType.ElementType.BasicValueType == BasicValueType.Float8E4M3 ||
                targetType.ElementType.BasicValueType == BasicValueType.Float8E5M2)
            {
                // FP8 store: round the f32 value register to the 1-byte FP8 pattern via portable
                // bit-manip (EmitF32ToFP8Bits - every CUDA arch) into a temp .b16 register, then
                // write the low byte. Keyed off the TARGET BUFFER element type (same reason as bf16).
                bool isE4M3 = targetType.ElementType.BasicValueType == BasicValueType.Float8E4M3;
                var valueReg = EnsureHardwareRegister(value.AsNotNullCast<PrimitiveRegister>());
                var rawReg = AllocateRegister(BasicValueType.Int16, PTXRegisterKind.Int16);
                EmitF32ToFP8Bits(valueReg, rawReg, isE4M3);
                using (var cmd = BeginCommand(PTXInstructions.StoreOperation))
                {
                    cmd.AppendAddressSpace(targetType.AddressSpace);
                    cmd.AppendSuffix("u8");
                    cmd.AppendArgumentValue(address, 0);
                    cmd.AppendArgument(rawReg);
                }
                FreeRegister(rawReg);
                return;
            }

            // A bf16-TYPED value stored to a NON-bf16 buffer (the target-bf16 case was handled above).
            // bf16 is held in an f32 register and the `(float)bf16` widening Convert is a no-op alias
            // that preserves the bf16 IR type, so `floatBuf[i] = (float)bf16Buf[i]` reaches here with a
            // bf16-typed value register. Falling through to EmitIOStore would re-narrow it (cvt.rn.bf16.f32
            // + st.b16) into the wider (e.g. 4-byte f32) destination slot -> the value reads back ~0
            // (Tuvok's "bf16 store/load returns zeros" bug). Store the f32 bits directly as the target
            // element type instead. (Struct-field bf16 stores keep using EmitIOStore: there the register
            // type and the field storage type agree, so its register-type-keyed packing is correct.)
            if (value is PrimitiveRegister bf16Value &&
                bf16Value.BasicValueType == BasicValueType.BFloat16)
            {
                var f32Reg = EnsureHardwareRegister(bf16Value);
                using var cmd = BeginCommand(PTXInstructions.StoreOperation);
                cmd.AppendAddressSpace(targetType.AddressSpace);
                cmd.AppendSuffix(ResolveIOType(targetType.ElementType.BasicValueType));
                cmd.AppendArgumentValue(address, 0);
                cmd.AppendArgument(f32Reg);
                return;
            }

            EmitVectorizedCommand(
                store.Target,
                targetType.ElementType.Alignment,
                PTXInstructions.StoreOperation,
                new StoreEmitter(targetType, address),
                value);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(LoadFieldAddress)"/>
        public void GenerateCode(LoadFieldAddress value)
        {
            var source = LoadPrimitive(value.Source);
            var fieldOffset = value.StructureType.GetOffset(
                value.FieldSpan.Access);

            if (fieldOffset != 0)
            {
                var targetRegister = AllocateHardware(value);
                using var command = BeginCommand(
                    PTXInstructions.GetArithmeticOperation(
                        BinaryArithmeticKind.Add,
                        Backend.PointerArithmeticType,
                        Backend.Capabilities,
                        false));
                command.AppendArgument(targetRegister);
                command.AppendArgument(source);
                command.AppendConstant(fieldOffset);
            }
            else
            {
                Alias(value, value.Source);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(AlignTo)"/>
        public void GenerateCode(AlignTo value)
        {
            // Load the 32-bit or 64-bit base pointer
            var ptr = LoadHardware(value.Source);
            var arithmeticBasicValueType =
                value.Source.BasicValueType.GetArithmeticBasicValueType(true);

            // Load the alignment value into a register
            var alignment = LoadPrimitive(value.AlignmentInBytes);

            // var baseOffset = (int)ptr & (alignmentInBytes - 1);
            var tempRegister = AllocateRegister(ptr.Description);

            // Get the specialized and and convert operations
            var andOperation = PTXInstructions.GetArithmeticOperation(
                BinaryArithmeticKind.And,
                arithmeticBasicValueType,
                Backend.Capabilities,
                FastMath);
            var convertOperation = PTXInstructions.GetConvertOperation(
                alignment.BasicValueType.GetArithmeticBasicValueType(
                    isUnsigned: false),
                tempRegister.BasicValueType.GetArithmeticBasicValueType(
                    isUnsigned: true));

            // Check for a predefined alignment constant
            bool hasConstantAlignment;
            if (hasConstantAlignment = value.TryGetAlignmentConstant(
                out int alignmentConstant))
            {
                // Emit a specialized instruction using an inline constant
                using var command = BeginCommand(andOperation);
                command.AppendArgument(tempRegister);
                command.AppendArgument(ptr);
                command.AppendConstant(alignmentConstant);
            }
            else
            {
                // Convert the alignment information if necessary
                if (tempRegister.Kind != alignment.Kind)
                {
                    using var convert = BeginCommand(convertOperation);
                    convert.AppendArgument(tempRegister);
                    convert.AppendArgument(alignment);
                }

                // Compute the actual alignment mask
                using (var alignmentMinusOne = BeginCommand(
                    PTXInstructions.GetArithmeticOperation(
                        BinaryArithmeticKind.Sub,
                        arithmeticBasicValueType,
                        Backend.Capabilities,
                        FastMath)))
                {
                    alignmentMinusOne.AppendArgument(tempRegister);
                    alignmentMinusOne.AppendArgument(tempRegister);
                    alignmentMinusOne.AppendConstant(1);
                }

                // Compute the actual temp register contents
                using var command = BeginCommand(andOperation);
                command.AppendArgument(tempRegister);
                command.AppendArgument(ptr);
                command.AppendArgument(tempRegister);
            }

            // if (baseOffset == 0) ...
            using var predicate = new PredicateScope(this);
            using (var command = BeginCommand(
                PTXInstructions.GetCompareOperation(
                    CompareKind.Equal,
                    CompareFlags.None,
                    arithmeticBasicValueType)))
            {
                command.AppendArgument(predicate.PredicateRegister);
                command.AppendArgument(tempRegister);
                command.AppendConstant(0);
            }

            // Allocate the target register
            var targetRegister = AllocateHardware(value);

            // Use the same value as before the case of baseOffset = 0
            Move(
                ptr,
                targetRegister,
                predicate: predicate.GetConfiguration(true));

            // We need a temporary register to store the converted alignment
            var alignmentOffsetRegister = AllocateRegister(ptr.Description);
            if (!hasConstantAlignment && alignmentOffsetRegister.Kind != alignment.Kind)
            {
                using var convert = BeginCommand(
                    convertOperation,
                    predicate: predicate.GetConfiguration(false));
                convert.AppendArgument(alignmentOffsetRegister);
                convert.AppendArgument(alignment);
            }
            else
            {
                // Move the alignment constant into the offset register
                using var move = BeginMove(
                    predicate: predicate.GetConfiguration(false));
                move.AppendArgument(alignmentOffsetRegister);
                move.AppendConstant(alignmentConstant);
            }

            // Compute the alignment offset:
            // baseOffset = alignment - baseOffset
            using (var command = BeginCommand(
                PTXInstructions.GetArithmeticOperation(
                    BinaryArithmeticKind.Sub,
                    arithmeticBasicValueType,
                    Backend.Capabilities,
                    FastMath),
                predicate: predicate.GetConfiguration(false)))
            {
                command.AppendArgument(tempRegister);
                command.AppendArgument(alignmentOffsetRegister);
                command.AppendArgument(tempRegister);
            }

            // Adjust the given pointer if baseOffset != 0
            using (var command = BeginCommand(
                PTXInstructions.GetArithmeticOperation(
                    BinaryArithmeticKind.Add,
                    arithmeticBasicValueType,
                    Backend.Capabilities,
                    FastMath),
                predicate: predicate.GetConfiguration(false)))
            {
                command.AppendArgument(targetRegister);
                command.AppendArgument(ptr);
                command.AppendArgument(tempRegister);
            }

            Free(tempRegister);
            Free(alignmentOffsetRegister);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(AsAligned)"/>
        public void GenerateCode(AsAligned value)
        {
            var source = LoadPrimitive(value.Source);
            Bind(value, source);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(PrimitiveValue)"/>
        public void GenerateCode(PrimitiveValue value)
        {
            // Check whether we are loading an FP16 value. In this case, we have to
            // move the resulting constant into a register since the PTX compiler
            // expects a converted FP16 value in the scope of a register.
            var description = ResolveRegisterDescription(value.Type);
            var register = new ConstantRegister(description, value);
            if (value.BasicValueType == BasicValueType.Float16)
                Bind(value, EnsureHardwareRegister(register));
            else
                Bind(value, register);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(StringValue)"/>
        public void GenerateCode(StringValue value)
        {
            // Check for already existing global constant
            var key = (value.Encoding, value.String);
            if (!stringConstants.TryGetValue(key, out string? stringBinding))
            {
                stringBinding = "__strconst" + value.Id;
                stringConstants.Add(key, stringBinding);
            }

            // Move the value into the target register
            var register = AllocateHardware(value);
            using (var command = BeginMove())
            {
                command.AppendSuffix(register.Description.BasicValueType);
                command.AppendArgument(register);
                command.AppendRawValueReference(stringBinding);
            }

            // Convert the string value into the generic address space
            // string (global) -> string (generic) (in place conversion)
            CreateAddressSpaceCast(
                register,
                register,
                MemoryAddressSpace.Global,
                MemoryAddressSpace.Generic);
        }

        /// <summary>
        /// Emits complex null values.
        /// </summary>
        private readonly struct NullEmitter : IComplexCommandEmitter
        {
            /// <summary>
            /// Returns the same command.
            /// </summary>
            public string AdjustCommand(string command, PrimitiveRegister[] registers) =>
                command;

            /// <summary>
            /// Emits nested null values.
            /// </summary>
            public void Emit(
                CommandEmitter commandEmitter,
                PrimitiveRegister[] registers)
            {
                var primaryRegister = registers[0];

                commandEmitter.AppendRegisterMovementSuffix(
                    primaryRegister.BasicValueType);
                commandEmitter.AppendArgument(primaryRegister);
                commandEmitter.AppendNull(primaryRegister.Kind);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(NullValue)"/>
        public void GenerateCode(NullValue value)
        {
            switch (value.Type)
            {
                case VoidType _:
                    // Ignore void type nulls
                    break;
                default:
                    var targetRegister = Allocate(value);
                    EmitComplexCommand(
                        PTXInstructions.MoveOperation,
                        new NullEmitter(),
                        targetRegister);
                    break;
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(StructureValue)"/>
        public void GenerateCode(StructureValue value)
        {
            var childRegisters = ImmutableArray.CreateBuilder<Register>(value.Count);
            for (int i = 0, e = value.Count; i < e; ++i)
                childRegisters.Add(Load(value[i]));
            Bind(
                value,
                new CompoundRegister(
                    value.StructureType,
                    childRegisters.MoveToImmutable()));
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GetField)"/>
        public void GenerateCode(GetField value)
        {
            var source = LoadAs<CompoundRegister>(value.ObjectValue);
            if (!value.FieldSpan.HasSpan)
            {
                Bind(value, source.Children[value.FieldSpan.Index]);
            }
            else
            {
                int span = value.FieldSpan.Span;
                var childRegisters = ImmutableArray.CreateBuilder<Register>(span);
                for (int i = 0; i < span; ++i)
                    childRegisters.Add(source.Children[i + value.FieldSpan.Index]);
                Bind(
                    value,
                    new CompoundRegister(
                        value.Type.AsNotNullCast<StructureType>(),
                        childRegisters.MoveToImmutable()));
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(SetField)"/>
        public void GenerateCode(SetField value)
        {
            var source = LoadAs<CompoundRegister>(value.ObjectValue);
            var type = value.StructureType;
            var childRegisters = ImmutableArray.CreateBuilder<Register>(type.NumFields);
            for (int i = 0, e = type.NumFields; i < e; ++i)
                childRegisters.Add(source.Children[i]);

            if (!value.FieldSpan.HasSpan)
            {
                childRegisters[value.FieldSpan.Index] = Load(value.Value);
            }
            else
            {
                var structureValue = LoadAs<CompoundRegister>(value.Value);
                for (int i = 0; i < value.FieldSpan.Span; ++i)
                {
                    childRegisters[i + value.FieldSpan.Index] =
                        structureValue.Children[i];
                }
            }
            Bind(
                value,
                new CompoundRegister(
                    value.StructureType,
                    childRegisters.MoveToImmutable()));
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GridIndexValue)"/>
        public void GenerateCode(GridIndexValue value) =>
            MoveFromIntrinsicRegister(
                value,
                PTXRegisterKind.Ctaid,
                (int)value.Dimension);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GroupIndexValue)"/>
        public void GenerateCode(GroupIndexValue value) =>
            MoveFromIntrinsicRegister(
                value,
                PTXRegisterKind.Tid,
                (int)value.Dimension);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GridDimensionValue)"/>
        public void GenerateCode(GridDimensionValue value) =>
            MoveFromIntrinsicRegister(
                value,
                PTXRegisterKind.NctaId,
                (int)value.Dimension);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GroupDimensionValue)"/>
        public void GenerateCode(GroupDimensionValue value) =>
            MoveFromIntrinsicRegister(
                value,
                PTXRegisterKind.NtId,
                (int)value.Dimension);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(WarpSizeValue)"/>
        public void GenerateCode(WarpSizeValue value) =>
            throw new InvalidCodeGenerationException();

        /// <summary cref="IBackendCodeGenerator.GenerateCode(LaneIdxValue)"/>
        public void GenerateCode(LaneIdxValue value) =>
            MoveFromIntrinsicRegister(
                value,
                PTXRegisterKind.LaneId);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(DynamicMemoryLengthValue)"/>
        public void GenerateCode(DynamicMemoryLengthValue value)
        {
            if (value.AddressSpace != MemoryAddressSpace.Shared)
                throw new InvalidCodeGenerationException();

            // Load the dynamic memory size (in bytes) from the PTX special register
            // and divide by the size in bytes of the array element.
            var lengthRegister = AllocateHardware(value);
            var dynamicMemorySizeRegister = MoveFromIntrinsicRegister(
                PTXRegisterKind.DynamicSharedMemorySize);

            using var command = BeginCommand(
                PTXInstructions.GetArithmeticOperation(
                    BinaryArithmeticKind.Div,
                    ArithmeticBasicValueType.UInt32,
                    Backend.Capabilities,
                    false));
            command.AppendArgument(lengthRegister);
            command.AppendArgument(dynamicMemorySizeRegister);
            command.AppendConstant(value.ElementType.Size);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(PredicateBarrier)"/>
        public void GenerateCode(PredicateBarrier barrier)
        {
            var targetRegister = AllocateHardware(barrier);
            var sourcePredicate = LoadPrimitive(barrier.Predicate);
            switch (barrier.Kind)
            {
                case PredicateBarrierKind.And:
                case PredicateBarrierKind.Or:
                    using (var command = BeginCommand(
                        PTXInstructions.GetPredicateBarrier(barrier.Kind)))
                    {
                        command.AppendArgument(targetRegister);
                        command.AppendConstant(0);
                        command.AppendArgument(sourcePredicate);
                    }
                    break;
                case PredicateBarrierKind.PopCount:
                    using (var command = BeginCommand(
                        PTXInstructions.GetPredicateBarrier(barrier.Kind)))
                    {
                        command.AppendArgument(targetRegister);
                        command.AppendConstant(0);
                        command.AppendArgument(sourcePredicate);
                    }
                    break;
                default:
                    throw new InvalidCodeGenerationException();
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Barrier)"/>
        public void GenerateCode(Barrier barrier)
        {
            using var command = BeginCommand(PTXInstructions.GetBarrier(barrier.Kind));
            switch (barrier.Kind)
            {
                case BarrierKind.WarpLevel:
                    command.AppendConstant(
                        PTXInstructions.AllThreadsInAWarpMemberMask);
                    break;
                case BarrierKind.GroupLevel:
                    command.AppendConstant(0);
                    break;
                default:
                    throw new InvalidCodeGenerationException();
            }
        }

        /// <summary>
        /// Represents an abstract emitter of warp shuffle masks.
        /// </summary>
        private interface IShuffleEmitter
        {
            /// <summary>
            /// Emits a new warp mask.
            /// </summary>
            /// <param name="commandEmitter">The current command emitter.</param>
            void EmitWarpMask(CommandEmitter commandEmitter);
        }

        /// <summary>
        /// Creates a new shuffle operation.
        /// </summary>
        /// <typeparam name="TShuffleEmitter">The emitter type.</typeparam>
        /// <param name="shuffle">The current shuffle operation.</param>
        /// <param name="shuffleEmitter">The shuffle emitter.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EmitShuffleOperation<TShuffleEmitter>(
            ShuffleOperation shuffle,
            in TShuffleEmitter shuffleEmitter)
            where TShuffleEmitter : struct, IShuffleEmitter
        {
            var variable = LoadPrimitive(shuffle.Variable);
            var delta = LoadPrimitive(shuffle.Origin);

            var targetRegister = Allocate(shuffle, variable.Description);

            var shuffleOperation = PTXInstructions.GetShuffleOperation(shuffle.Kind);
            using var command = BeginCommand(shuffleOperation);
            command.AppendArgument(targetRegister);
            command.AppendArgument(variable);
            command.AppendArgument(delta);

            // Invoke the shuffle emitter
            shuffleEmitter.EmitWarpMask(command);

            command.AppendConstant(PTXInstructions.AllThreadsInAWarpMemberMask);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Broadcast)"/>
        public void GenerateCode(Broadcast broadcast) =>
            throw new InvalidCodeGenerationException();

        /// <summary>
        /// Emits warp masks of <see cref="WarpShuffle"/> operations.
        /// </summary>
        private readonly struct WarpShuffleEmitter : IShuffleEmitter
        {
            /// <summary>
            /// The basic mask that has be combined with an 'or' command
            /// in case of a <see cref="ShuffleKind.Xor"/> or a
            /// <see cref="ShuffleKind.Down"/> shuffle instruction.
            /// </summary>
            public const int XorDownMask = 0x1f;

            /// <summary>
            /// The amount of bits the basic mask has to be shifted to
            /// the left.
            /// </summary>
            public const int BaseMaskShiftAmount = 8;

            /// <summary>
            /// Constructs a new shuffle emitter.
            /// </summary>
            /// <param name="shuffleKind">The current shuffle kind.</param>
            public WarpShuffleEmitter(ShuffleKind shuffleKind)
            {
                ShuffleKind = shuffleKind;
            }

            /// <summary>
            /// The shuffle kind.
            /// </summary>
            public ShuffleKind ShuffleKind { get; }

            /// <summary cref="IShuffleEmitter.EmitWarpMask(CommandEmitter)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EmitWarpMask(CommandEmitter commandEmitter)
            {
                if (ShuffleKind == ShuffleKind.Up)
                    commandEmitter.AppendConstant(0);
                else
                    commandEmitter.AppendConstant(XorDownMask);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(WarpShuffle)"/>
        public void GenerateCode(WarpShuffle shuffle) =>
            EmitShuffleOperation(
                shuffle,
                new WarpShuffleEmitter(shuffle.Kind));

        /// <summary>
        /// Emits warp masks of <see cref="SubWarpShuffle"/> operations.
        /// </summary>
        private readonly struct SubWarpShuffleEmitter : IShuffleEmitter
        {
            /// <summary>
            /// Constructs a new shuffle emitter.
            /// </summary>
            /// <param name="warpMaskRegister">The current mask register.</param>
            public SubWarpShuffleEmitter(PrimitiveRegister warpMaskRegister)
            {
                WarpMaskRegister = warpMaskRegister;
            }

            /// <summary>
            /// Returns the current mask register.
            /// </summary>
            public PrimitiveRegister WarpMaskRegister { get; }

            /// <summary cref="IShuffleEmitter.EmitWarpMask(CommandEmitter)"/>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void EmitWarpMask(CommandEmitter commandEmitter) =>
                commandEmitter.AppendArgument(WarpMaskRegister);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(SubWarpShuffle)"/>
        public void GenerateCode(SubWarpShuffle shuffle)
        {
            // Compute the actual warp mask
            var width = LoadPrimitive(shuffle.Width);

            // Create basic mask
            var baseRegister = AllocateRegister(width.Description);
            using (var command = BeginCommand(
                PTXInstructions.GetArithmeticOperation(
                    BinaryArithmeticKind.Sub,
                    ArithmeticBasicValueType.UInt32,
                    Backend.Capabilities,
                    false)))
            {
                command.AppendArgument(baseRegister);
                command.AppendConstant(PTXBackend.WarpSize);
                command.AppendArgument(width);
            }

            // Shift mask
            var maskRegister = AllocateRegister(width.Description);
            using (var command = BeginCommand(
                PTXInstructions.GetArithmeticOperation(
                    BinaryArithmeticKind.Shl,
                    ArithmeticBasicValueType.UInt32,
                    Backend.Capabilities,
                    false)))
            {
                command.AppendArgument(maskRegister);
                command.AppendArgument(baseRegister);
                command.AppendConstant(WarpShuffleEmitter.BaseMaskShiftAmount);
            }
            FreeRegister(baseRegister);

            // Adjust mask register
            if (shuffle.Kind != ShuffleKind.Up)
            {
                var adjustedMaskRegister = AllocateRegister(width.Description);
                using (var command = BeginCommand(
                    PTXInstructions.GetArithmeticOperation(
                        BinaryArithmeticKind.Or,
                        ArithmeticBasicValueType.UInt32,
                        Backend.Capabilities,
                        false)))
                {
                    command.AppendArgument(adjustedMaskRegister);
                    command.AppendArgument(maskRegister);
                    command.AppendConstant(WarpShuffleEmitter.XorDownMask);
                }

                FreeRegister(maskRegister);
                maskRegister = adjustedMaskRegister;
            }

            EmitShuffleOperation(
                shuffle,
                new SubWarpShuffleEmitter(maskRegister));
            FreeRegister(maskRegister);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(DebugAssertOperation)"/>
        public void GenerateCode(DebugAssertOperation debug) =>
            // Invalid debug node -> should have been removed
            debug.Assert(false);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(WriteToOutput)"/>
        public void GenerateCode(WriteToOutput writeToOutput) =>
            // Invalid write node -> should have been removed
            writeToOutput.Assert(false);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(LanguageEmitValue)"/>
        public void GenerateCode(LanguageEmitValue emit)
        {
            // Ignore non-PTX instructions.
            if (emit.LanguageKind != LanguageKind.PTX)
                return;

            // Load argument registers.
            var registers = InlineList<PrimitiveRegister>.Create(emit.Nodes.Length);

            for (var argumentIdx = 0; argumentIdx < emit.Count; argumentIdx++)
            {
                var argument = emit.Nodes[argumentIdx];

                if (emit.UsingRefParams)
                {
                    // If there is an input, initialize with the supplied argument value.
                    var pointerType = argument.Type.AsNotNullCast<PointerType>();
                    var pointerElementType = pointerType.ElementType;

                    var targetRegister = AllocateRegister(
                        ResolveRegisterDescription(pointerElementType));
                    registers.Add(targetRegister);

                    if (emit.IsInputArgument(argumentIdx))
                    {
                        var address = LoadHardware(argument);
                        EmitVectorizedCommand(
                            argument,
                            pointerElementType.Alignment,
                            PTXInstructions.LoadOperation,
                            new LoadEmitter(pointerType, address),
                            targetRegister);
                    }
                }
                else
                {
                    // If there is an output, allocate a new register to store the value.
                    registers.Add(
                        emit.IsOutputArgument(argumentIdx)
                        ? AllocateRegister(ResolveRegisterDescription(
                            argument.Type.AsNotNullCast<PointerType>().ElementType))
                        : LoadPrimitive(argument));
                }
            }

            // Emit the PTX assembly string
            Builder.Append('\t');

            using (var emitter = new CommandEmitter(Builder, string.Empty, string.Empty))
            {
                foreach (var expression in emit.Expressions)
                {
                    if (expression.HasArgument)
                    {
                        emitter.AppendArgument(registers[expression.Argument]);
                    }
                    else
                    {
                        emitter.AppendRawString(expression.String.AsNotNull());
                    }
                }
            }

            // For each output argument, write the value to the address.
            for (var argumentIdx = 0; argumentIdx < emit.Count; argumentIdx++)
            {
                if (emit.IsOutputArgument(argumentIdx))
                {
                    var outputArgument = emit.Nodes[argumentIdx];
                    var address = LoadHardware(outputArgument);
                    var targetType = outputArgument.Type.AsNotNullCast<PointerType>();
                    var newValue = registers[argumentIdx];

                    EmitVectorizedCommand(
                        outputArgument,
                        targetType.ElementType.Alignment,
                        PTXInstructions.StoreOperation,
                        new StoreEmitter(targetType, address),
                        newValue);
                }
            }
        }
    }
}
