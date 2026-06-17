// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: CLCodeGenerator.Values.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Runtime.OpenCL;
using ILGPU.Util;
using System.Runtime.CompilerServices;

namespace ILGPU.Backends.OpenCL
{
    partial class CLCodeGenerator
    {
        /// <summary cref="IBackendCodeGenerator.GenerateCode(MethodCall)"/>
        public void GenerateCode(MethodCall methodCall)
        {
            var target = methodCall.Target;
            var returnType = target.ReturnType;

            StatementEmitter statementEmitter;
            if (!returnType.IsVoidType)
            {
                var returnValue = Allocate(methodCall);
                statementEmitter = BeginStatement(returnValue);
                statementEmitter.AppendCommand(GetMethodName(target));
            }
            else
            {
                statementEmitter = BeginStatement(GetMethodName(target));
            }

            // Append arguments
            statementEmitter.BeginArguments();
            foreach (var argument in methodCall)
            {
                var variable = Load(argument);
                statementEmitter.AppendArgument(variable);
            }
            statementEmitter.EndArguments();

            // End call
            statementEmitter.Finish();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Parameter)"/>
        public void GenerateCode(Parameter parameter)
        {
            // Parameters are already assigned to variables
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(PhiValue)"/>
        public void GenerateCode(PhiValue phiValue)
        {
            // Phi values are already assigned to variables
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(UnaryArithmeticValue)"/>
        public void GenerateCode(UnaryArithmeticValue value)
        {
            var argument = Load(value.Value);
            var target = Allocate(
                value,
                value.BasicValueType == BasicValueType.Int1
                ? ArithmeticBasicValueType.UInt1 : value.ArithmeticBasicValueType);

            using var statement = BeginStatement(target);
            if (value.BasicValueType != BasicValueType.Int1)
                statement.AppendCast(value.ArithmeticBasicValueType);
            var operation = CLInstructions.GetArithmeticOperation(
                value.Kind,
                value.ArithmeticBasicValueType,
                out bool isFunction);

            if (isFunction)
                statement.AppendCommand(operation);
            statement.BeginArguments();
            if (!isFunction)
                statement.AppendCommand(operation);

            statement.AppendCast(value.ArithmeticBasicValueType);
            statement.AppendArgument(argument);
            statement.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(BinaryArithmeticValue)"/>
        public void GenerateCode(BinaryArithmeticValue value)
        {
            var left = Load(value.Left);
            var right = Load(value.Right);

            var target = Allocate(value, value.ArithmeticBasicValueType);
            using var statement = BeginStatement(target);
            statement.AppendCast(value.ArithmeticBasicValueType);
            var operation = CLInstructions.GetArithmeticOperation(
                value.Kind,
                value.BasicValueType.IsFloat(),
                out bool isFunction);

            if (isFunction)
            {
                statement.AppendCommand(operation);
                statement.BeginArguments();
            }
            else
            {
                statement.OpenParen();
            }

            statement.AppendCast(value.ArithmeticBasicValueType);
            statement.AppendArgument(left);

            if (!isFunction)
                statement.AppendCommand(operation);

            statement.AppendArgument();
            statement.AppendCast(value.ArithmeticBasicValueType);
            statement.Append(right);

            if (isFunction)
                statement.EndArguments();
            else
                statement.CloseParen();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(TernaryArithmeticValue)"/>
        public void GenerateCode(TernaryArithmeticValue value)
        {
            if (!CLInstructions.TryGetArithmeticOperation(
                value.Kind,
                value.BasicValueType.IsFloat(),
                out string? operation))
            {
                throw new InvalidCodeGenerationException();
            }

            var first = Load(value.First);
            var second = Load(value.Second);
            var third = Load(value.Third);

            var target = Allocate(value, value.ArithmeticBasicValueType);
            using var statement = BeginStatement(target);
            statement.AppendCast(value.ArithmeticBasicValueType);
            statement.AppendCommand(operation);
            statement.BeginArguments();

            statement.AppendArgument();
            statement.AppendCast(value.ArithmeticBasicValueType);
            statement.Append(first);

            statement.AppendArgument();
            statement.AppendCast(value.ArithmeticBasicValueType);
            statement.Append(second);

            statement.AppendArgument();
            statement.AppendCast(value.ArithmeticBasicValueType);
            statement.Append(third);

            statement.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(CompareValue)"/>
        public void GenerateCode(CompareValue value)
        {
            var left = Load(value.Left);
            var right = Load(value.Right);

            var target = Allocate(value);
            using var statement = BeginStatement(target);
            statement.AppendCast(value.CompareType);
            statement.AppendArgument(left);
            statement.AppendCommand(
                CLInstructions.GetCompareOperation(
                    value.Kind));
            statement.AppendCast(value.CompareType);
            statement.AppendArgument(right);
            // IEEE 754 unordered float compare: NaN forces TRUE.
            // ILGPU's IR negates `clt + brfalse` to `cge + brtrue [Unordered]`;
            // OpenCL C `>=` is ordered (FALSE for NaN) so without the OR with
            // isunordered(), the negated branch sets bits for NaN inputs
            // (DoubleNaNComparisonTest 2026-04-29). NotEqual is already TRUE
            // for NaN under ordered semantics, so it is excluded.
            if (value.IsUnsignedOrUnordered &&
                value.Left.BasicValueType.IsFloat() &&
                value.Kind != CompareKind.NotEqual)
            {
                statement.AppendCommand("||");
                statement.AppendCommand("isunordered(");
                statement.AppendArgument(left);
                statement.AppendCommand(",");
                statement.AppendArgument(right);
                statement.AppendCommand(")");
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(ConvertValue)"/>
        public void GenerateCode(ConvertValue value)
        {
            var sourceValue = Load(value.Value);

            var target = Allocate(value, value.TargetType);
            using var statement = BeginStatement(target);
            statement.AppendCast(value.TargetType);
            statement.AppendCast(value.SourceType);
            statement.AppendArgument(sourceValue);
        }

        /// <summary>
        /// Generates code for the given cast value.
        /// </summary>
        /// <param name="cast">The cast value to generte code for.</param>
        private void GenerateCodeForCast(CastValue cast)
        {
            var sourceValue = Load(cast.Value);

            var target = Allocate(cast);
            using var statement = BeginStatement(target);
            statement.AppendCast(cast.TargetType);
            statement.AppendArgument(sourceValue);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(IntAsPointerCast)"/>
        public void GenerateCode(IntAsPointerCast cast) => GenerateCodeForCast(cast);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(IntAsPointerCast)"/>
        public void GenerateCode(PointerAsIntCast cast) => GenerateCodeForCast(cast);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(PointerCast)"/>
        public void GenerateCode(PointerCast value) => GenerateCodeForCast(value);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(FloatAsIntCast)"/>
        public void GenerateCode(FloatAsIntCast value)
        {
            var source = Load(value.Value);
            var target = Allocate(value);

            // EMULATED HALF: when cl_khr_fp16 is unavailable, CLTypeGenerator promotes
            // Half values to `float` for compute. The naive emit `as_short(float_value)`
            // is invalid OpenCL (size mismatch: 4 vs 2 bytes) and even if accepted gives
            // f32 IEEE-754 bits, not the 16-bit Half pattern. AscendingHalf radix-sort
            // depends on the Half bit pattern (NumBits=16, sign at bit 15), so without
            // this fix every Half radix sort silently produced wrong output.
            // Use the _f32_to_half_bits helper emitted in the kernel prologue.
            bool isEmulatedHalfSource =
                value.Value.BasicValueType == BasicValueType.Float16
                && !TypeGenerator.Capabilities.Float16Native;
            if (isEmulatedHalfSource)
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_f32_to_half_bits");
                statement.BeginArguments();
                statement.AppendArgument(source);
                statement.EndArguments();
                return;
            }

            // bf16 is ALWAYS emulated as float on OpenCL (no native bf16 type). FloatAsInt(bf16)
            // must yield the 16-bit bf16 pattern, not the promoted-f32 bits - use the
            // _f32_to_bf16_bits helper emitted in the kernel prologue (same one the store path
            // uses). Drives AscendingBFloat16 radix sort (NumBits=16).
            if (value.Value.BasicValueType == BasicValueType.BFloat16)
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_f32_to_bf16_bits");
                statement.BeginArguments();
                statement.AppendArgument(source);
                statement.EndArguments();
                return;
            }

            // FP8 is ALWAYS emulated as float on OpenCL. FloatAsInt(fp8) must yield the 1-byte
            // FP8 pattern, not the promoted-f32 bits - use the _f32_to_e4m3_bits/_f32_to_e5m2_bits
            // helper (same one the store path uses). Drives the AscendingFloat8E4M3/E5M2 radix
            // sort (NumBits=8).
            if (value.Value.BasicValueType == BasicValueType.Float8E4M3 ||
                value.Value.BasicValueType == BasicValueType.Float8E5M2)
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand(
                    value.Value.BasicValueType == BasicValueType.Float8E4M3 ?
                    "_f32_to_e4m3_bits" : "_f32_to_e5m2_bits");
                statement.BeginArguments();
                statement.AppendArgument(source);
                statement.EndArguments();
                return;
            }

            // FP4 is ALWAYS emulated as float on OpenCL. FloatAsInt(fp4) must yield the 4-bit
            // E2M1 pattern (low nibble) via _f32_to_e2m1_bits. Drives the AscendingFloat4E2M1
            // radix sort (NumBits=4).
            if (value.Value.BasicValueType == BasicValueType.Float4E2M1)
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_f32_to_e2m1_bits");
                statement.BeginArguments();
                statement.AppendArgument(source);
                statement.EndArguments();
                return;
            }

            using var statement2 = BeginStatement(target);
            statement2.AppendCommand(
                value.BasicValueType == BasicValueType.Int64 ?
                CLInstructions.DoubleAsLong :
                value.BasicValueType == BasicValueType.Int32 ?
                CLInstructions.FloatAsInt :
                CLInstructions.HalfAsShort);
            statement2.BeginArguments();
            statement2.AppendArgument(source);
            statement2.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(IntAsFloatCast)"/>
        public void GenerateCode(IntAsFloatCast value)
        {
            var source = Load(value.Value);
            var target = Allocate(value);

            // Symmetric inverse of FloatAsIntCast for emulated Half.
            bool isEmulatedHalfTarget =
                value.BasicValueType == BasicValueType.Float16
                && !TypeGenerator.Capabilities.Float16Native;
            if (isEmulatedHalfTarget)
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_half_bits_to_f32");
                statement.BeginArguments();
                statement.AppendArgument(source);
                statement.EndArguments();
                return;
            }

            // Symmetric inverse for bf16 (always emulated): widen the 16-bit pattern to f32.
            // Defensive - the frontend has no IntAsFloat->BFloat16 overload today.
            if (value.BasicValueType == BasicValueType.BFloat16)
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_bf16_bits_to_f32");
                statement.BeginArguments();
                statement.AppendArgument(source);
                statement.EndArguments();
                return;
            }

            // Symmetric inverse for FP8 (always emulated): widen the 1-byte pattern to f32.
            // Defensive - the frontend has no IntAsFloat->Float8 overload today.
            if (value.BasicValueType == BasicValueType.Float8E4M3 ||
                value.BasicValueType == BasicValueType.Float8E5M2)
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand(
                    value.BasicValueType == BasicValueType.Float8E4M3 ?
                    "_e4m3_bits_to_f32" : "_e5m2_bits_to_f32");
                statement.BeginArguments();
                statement.AppendArgument(source);
                statement.EndArguments();
                return;
            }

            // Symmetric inverse for FP4 (always emulated): widen the 4-bit pattern to f32.
            // Defensive - the frontend has no IntAsFloat->Float4E2M1 overload today.
            if (value.BasicValueType == BasicValueType.Float4E2M1)
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_e2m1_bits_to_f32");
                statement.BeginArguments();
                statement.AppendArgument(source);
                statement.EndArguments();
                return;
            }

            using var statement2 = BeginStatement(target);
            statement2.AppendCommand(
                value.BasicValueType == BasicValueType.Float64 ?
                CLInstructions.LongAsDouble :
                value.BasicValueType == BasicValueType.Float32 ?
                CLInstructions.IntAsFloat :
                CLInstructions.ShortAsHalf);
            statement2.BeginArguments();
            statement2.AppendArgument(source);
            statement2.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Predicate)"/>
        public void GenerateCode(Predicate predicate)
        {
            var condition = Load(predicate.Condition);
            var trueValue = Load(predicate.TrueValue);
            var falseValue = Load(predicate.FalseValue);

            var target = Allocate(predicate);
            using var statement = BeginStatement(target);
            statement.AppendArgument(condition);
            statement.AppendCommand(CLInstructions.SelectOperation1);
            statement.AppendArgument(trueValue);
            statement.AppendCommand(CLInstructions.SelectOperation2);
            statement.AppendArgument(falseValue);
        }

        /// <summary>
        /// Throws an exception if the supplied atomic operation is not supported
        /// by the capabilities of the accelerator.
        /// </summary>
        /// <param name="atomic">The atomic operation.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfUnsupportedAtomicOperation(AtomicValue atomic)
        {
            if ((atomic.ArithmeticBasicValueType == ArithmeticBasicValueType.Int64 ||
                atomic.ArithmeticBasicValueType == ArithmeticBasicValueType.UInt64 ||
                atomic.ArithmeticBasicValueType == ArithmeticBasicValueType.Float64) &&
                !TypeGenerator.Capabilities.Int64_Atomics)
            {
                throw CLCapabilityContext.GetNotSupportedInt64_AtomicsException();
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GenericAtomic)"/>
        public void GenerateCode(GenericAtomic atomic)
        {
            ThrowIfUnsupportedAtomicOperation(atomic);

            var target = Load(atomic.Target);
            var value = Load(atomic.Value);
            var result = Allocate(atomic);

            var atomicOperation = CLInstructions.GetAtomicOperation(atomic.Kind);
            using var statement = BeginStatement(result, atomicOperation);
            statement.BeginArguments();
            statement.AppendAtomicCast(atomic.ArithmeticBasicValueType);
            statement.AppendArgument(target);
            statement.AppendArgument();
            statement.AppendCast(atomic.ArithmeticBasicValueType);
            statement.Append(value);
            statement.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(AtomicCAS)"/>
        public void GenerateCode(AtomicCAS atomicCAS)
        {
            ThrowIfUnsupportedAtomicOperation(atomicCAS);

            var target = Load(atomicCAS.Target);
            var value = Load(atomicCAS.Value);
            var compare = Load(atomicCAS.CompareValue);

            // The internal AtomicCAS value "returns" the old value that was stored
            // at the memory location. If the emitted operation fails the comparison
            // check, we will "return" the updated value stored in "targetVariable". If
            // the operation succeeds we will return the old value stored in
            // "targetVariable". Consequently, we will always assign the value stored in
            // "targetVariable" the be the "result" of the computation.
            var targetVariable = Allocate(atomicCAS);

            // Copy the compare value into the target variable to avoid modifications of
            // the input value
            using (var statement = BeginStatement(targetVariable))
                statement.Append(value);

            // Perform the atomic operation and ignore the resulting bool value
            using (var statement = BeginStatement(CLInstructions.AtomicCASOperation))
            {
                statement.BeginArguments();
                statement.AppendAtomicCast(atomicCAS.ArithmeticBasicValueType);
                statement.AppendArgument(target);
                statement.AppendArgumentAddressWithCast(
                    targetVariable,
                    atomicCAS.ArithmeticBasicValueType);
                statement.AppendArgumentWithCast(
                    compare,
                    atomicCAS.ArithmeticBasicValueType);
                statement.EndArguments();
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Alloca)"/>
        public void GenerateCode(Alloca alloca)
        {
            // Ignore alloca
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(MemoryBarrier)"/>
        public void GenerateCode(MemoryBarrier barrier)
        {
            var fenceFlags = CLInstructions.GetMemoryFenceFlags(true);
            var command = CLInstructions.GetMemoryBarrier(
                barrier.Kind,
                out string memoryScope);
            using var statement = BeginStatement(command);
            statement.BeginArguments();

            statement.AppendArgument();
            statement.AppendCommand(fenceFlags);

            statement.AppendArgument();
            statement.AppendCommand(memoryScope);

            statement.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Load)"/>
        public void GenerateCode(Load load)
        {
            var address = Load(load.Source);
            var target = Allocate(load);

            // Float16 emulation: use vload_half(index, basePtr) for correct 2-byte stride
            if (_f16EmulatedLEAs.TryGetValue(address.ToString(), out var f16Lea))
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand($"vload_half(");
                statement.AppendArgument(f16Lea.Index);
                statement.AppendCommand(", ");
                statement.AppendArgument(f16Lea.BasePtr);
                statement.AppendCommand(")");
                return;
            }

            // Float16 emulation fallback: address is a direct base pointer to half[]
            // (no LEA computed - e.g. output[0] optimized to direct base ptr). Use
            // vload_half(0, basePtr) since dereferencing as float* would read 4 bytes
            // from a 2-byte half slot at wrong stride.
            if (IsFloat16PointerEmulated(load.Source.Type))
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("vload_half(0, ");
                statement.AppendArgument(address);
                statement.AppendCommand(")");
                return;
            }

            // BFloat16 emulation: read the raw ushort and convert to f32 via shift helper.
            if (_bf16EmulatedLEAs.TryGetValue(address.ToString(), out var bf16Lea))
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_bf16_bits_to_f32(");
                statement.AppendArgument(bf16Lea.BasePtr);
                statement.AppendIndexer(bf16Lea.Index);
                statement.AppendCommand(")");
                return;
            }
            if (IsBFloat16PointerEmulated(load.Source.Type))
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_bf16_bits_to_f32(");
                statement.AppendCommand(CLInstructions.DereferenceOperation);
                statement.AppendArgument(address);
                statement.AppendCommand(")");
                return;
            }

            // FP8 emulation: read the raw uchar and convert to f32 via the format's helper.
            if (_fp8EmulatedLEAs.TryGetValue(address.ToString(), out var fp8Lea))
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand(fp8Lea.IsE4M3 ? "_e4m3_bits_to_f32(" : "_e5m2_bits_to_f32(");
                statement.AppendArgument(fp8Lea.BasePtr);
                statement.AppendIndexer(fp8Lea.Index);
                statement.AppendCommand(")");
                return;
            }
            if (IsFloat8PointerEmulated(load.Source.Type, out bool loadIsE4M3))
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand(loadIsE4M3 ? "_e4m3_bits_to_f32(" : "_e5m2_bits_to_f32(");
                statement.AppendCommand(CLInstructions.DereferenceOperation);
                statement.AppendArgument(address);
                statement.AppendCommand(")");
                return;
            }

            // FP4 emulation: read the raw uchar (low nibble) and convert to f32 via the E2M1 helper.
            if (_fp4EmulatedLEAs.TryGetValue(address.ToString(), out var fp4Lea))
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_e2m1_bits_to_f32(");
                statement.AppendArgument(fp4Lea.BasePtr);
                statement.AppendIndexer(fp4Lea.Index);
                statement.AppendCommand(")");
                return;
            }
            if (IsFloat4PointerEmulated(load.Source.Type))
            {
                using var statement = BeginStatement(target);
                statement.AppendCommand("_e2m1_bits_to_f32(");
                statement.AppendCommand(CLInstructions.DereferenceOperation);
                statement.AppendArgument(address);
                statement.AppendCommand(")");
                return;
            }

            using var statement2 = BeginStatement(target);
            statement2.AppendCommand(CLInstructions.DereferenceOperation);
            statement2.AppendArgument(address);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Store)"/>
        public void GenerateCode(Store store)
        {
            var address = Load(store.Target);
            var value = Load(store.Value);

            // Float16 emulation: use vstore_half(value, index, basePtr)
            if (_f16EmulatedLEAs.TryGetValue(address.ToString(), out var f16StoreLea))
            {
                using var statement = BeginStatement("vstore_half(");
                statement.AppendArgument(value);
                statement.AppendCommand(", ");
                statement.AppendArgument(f16StoreLea.Index);
                statement.AppendCommand(", ");
                statement.AppendArgument(f16StoreLea.BasePtr);
                statement.AppendCommand(")");
                return;
            }

            // Float16 emulation fallback: target is a direct base pointer to half[]
            // (no LEA computed - e.g. output[0] optimized to direct base ptr). Use
            // vstore_half(value, 0, basePtr) since `*ptr = float_val` would write
            // 4 bytes into a 2-byte half slot, leaving the half value as 0.
            if (IsFloat16PointerEmulated(store.Target.Type))
            {
                using var statement = BeginStatement("vstore_half(");
                statement.AppendArgument(value);
                statement.AppendCommand(", 0, ");
                statement.AppendArgument(address);
                statement.AppendCommand(")");
                return;
            }

            // BFloat16 emulation: convert f32 -> bf16 bits (RNE) and store as raw ushort.
            if (_bf16EmulatedLEAs.TryGetValue(address.ToString(), out var bf16StoreLea))
            {
                using var statement = BeginStatement(bf16StoreLea.BasePtr, bf16StoreLea.Index);
                statement.AppendCommand("_f32_to_bf16_bits(");
                statement.AppendArgument(value);
                statement.AppendCommand(")");
                return;
            }
            if (IsBFloat16PointerEmulated(store.Target.Type))
            {
                using var statement = BeginStatement(CLInstructions.DereferenceOperation);
                statement.AppendArgument(address);
                statement.AppendCommand(CLInstructions.AssignmentOperation);
                statement.AppendCommand("_f32_to_bf16_bits(");
                statement.AppendArgument(value);
                statement.AppendCommand(")");
                return;
            }

            // FP8 emulation: convert f32 -> fp8 bits (RNE) and store as raw uchar.
            if (_fp8EmulatedLEAs.TryGetValue(address.ToString(), out var fp8StoreLea))
            {
                using var statement = BeginStatement(fp8StoreLea.BasePtr, fp8StoreLea.Index);
                statement.AppendCommand(fp8StoreLea.IsE4M3 ? "_f32_to_e4m3_bits(" : "_f32_to_e5m2_bits(");
                statement.AppendArgument(value);
                statement.AppendCommand(")");
                return;
            }
            if (IsFloat8PointerEmulated(store.Target.Type, out bool storeIsE4M3))
            {
                using var statement = BeginStatement(CLInstructions.DereferenceOperation);
                statement.AppendArgument(address);
                statement.AppendCommand(CLInstructions.AssignmentOperation);
                statement.AppendCommand(storeIsE4M3 ? "_f32_to_e4m3_bits(" : "_f32_to_e5m2_bits(");
                statement.AppendArgument(value);
                statement.AppendCommand(")");
                return;
            }

            // FP4 emulation: convert f32 -> e2m1 bits (RNE) and store as raw uchar (low nibble).
            if (_fp4EmulatedLEAs.TryGetValue(address.ToString(), out var fp4StoreLea))
            {
                using var statement = BeginStatement(fp4StoreLea.BasePtr, fp4StoreLea.Index);
                statement.AppendCommand("_f32_to_e2m1_bits(");
                statement.AppendArgument(value);
                statement.AppendCommand(")");
                return;
            }
            if (IsFloat4PointerEmulated(store.Target.Type))
            {
                using var statement = BeginStatement(CLInstructions.DereferenceOperation);
                statement.AppendArgument(address);
                statement.AppendCommand(CLInstructions.AssignmentOperation);
                statement.AppendCommand("_f32_to_e2m1_bits(");
                statement.AppendArgument(value);
                statement.AppendCommand(")");
                return;
            }

            using var statement2 = BeginStatement(CLInstructions.DereferenceOperation);
            statement2.AppendArgument(address);
            statement2.AppendCommand(CLInstructions.AssignmentOperation);
            statement2.AppendArgument(value);
        }

        private bool IsFloat16PointerEmulated(TypeNode type) =>
            type is PointerType ptr
            && ptr.ElementType is PrimitiveType pe
            && pe.BasicValueType == BasicValueType.Float16
            && !TypeGenerator.Capabilities.Float16Native;

        private static bool IsBFloat16PointerEmulated(TypeNode type) =>
            type is PointerType ptr
            && ptr.ElementType is PrimitiveType pe
            && pe.BasicValueType == BasicValueType.BFloat16;

        private static bool IsFloat8PointerEmulated(TypeNode type, out bool isE4M3)
        {
            isE4M3 = false;
            if (type is PointerType ptr
                && ptr.ElementType is PrimitiveType pe
                && (pe.BasicValueType == BasicValueType.Float8E4M3
                    || pe.BasicValueType == BasicValueType.Float8E5M2))
            {
                isE4M3 = pe.BasicValueType == BasicValueType.Float8E4M3;
                return true;
            }
            return false;
        }

        private static bool IsFloat4PointerEmulated(TypeNode type) =>
            type is PointerType ptr
            && ptr.ElementType is PrimitiveType pe
            && pe.BasicValueType == BasicValueType.Float4E2M1;

        /// <summary cref="IBackendCodeGenerator.GenerateCode(LoadFieldAddress)"/>
        public void GenerateCode(LoadFieldAddress value)
        {
            var source = Load(value.Source);
            var target = Allocate(value);

            using var statement = BeginStatement(target);
            statement.AppendCommand(CLInstructions.AddressOfOperation);
            statement.AppendArgument(source);
            statement.AppendFieldViaPtr(value.FieldSpan.Access);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(AlignTo)"/>
        public void GenerateCode(AlignTo value)
        {
            // Load the base view pointer
            var source = Load(value.Source);
            var target = Allocate(value);
            var alignmentVariable = Load(value.AlignmentInBytes);
            var arithmeticBasicValueType =
                value.Source.BasicValueType.GetArithmeticBasicValueType(true);

            // var baseOffset = (int)ptr & (alignmentInBytes - 1);
            var baseOffset = AllocateType(arithmeticBasicValueType);
            using (var statement = BeginStatement(baseOffset))
            {
                statement.AppendCast(arithmeticBasicValueType);
                statement.AppendArgument(source);
                statement.AppendCommand(CLInstructions.GetArithmeticOperation(
                    BinaryArithmeticKind.And,
                    isFloat: false,
                    out bool _));
                // Optimize for the case in which the alignment is a constant value
                if (value.TryGetAlignmentConstant(out int alignment))
                {
                    statement.AppendConstant(alignment - 1);
                }
                else
                {
                    statement.AppendCommand('(');
                    statement.AppendArgument(alignmentVariable);
                    statement.AppendCommand(CLInstructions.GetArithmeticOperation(
                        BinaryArithmeticKind.Sub,
                        isFloat: false,
                        out bool _));
                    statement.AppendConstant(1);
                    statement.AppendCommand(')');
                }
            }

            // if (baseOffset == 0) { 0 } else { alignment - baseOffset }
            var adjustment = AllocateType(arithmeticBasicValueType);
            using (var selectStatement = BeginStatement(adjustment))
            {
                selectStatement.AppendArgument(baseOffset);
                selectStatement.AppendCommand(" == 0 ? 0 : (");
                if (value.TryGetAlignmentConstant(out int alignmentConstant))
                    selectStatement.AppendConstant(alignmentConstant);
                else
                    selectStatement.AppendArgument(alignmentVariable);
                selectStatement.AppendCommand(CLInstructions.GetArithmeticOperation(
                    BinaryArithmeticKind.Sub,
                    isFloat: false,
                    out bool _));
                selectStatement.AppendArgument(baseOffset);
                selectStatement.AppendCommand(')');
            }

            // Adjust the given pointer
            using var command = BeginStatement(target);
            command.AppendCast(value.Type);
            command.AppendCommand('(');
            command.AppendCast(arithmeticBasicValueType);
            command.AppendArgument(source);
            command.AppendCommand(CLInstructions.GetArithmeticOperation(
                BinaryArithmeticKind.Add,
                isFloat: false,
                out bool _));
            command.AppendArgument(adjustment);
            command.AppendCommand(')');
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(AsAligned)"/>
        public void GenerateCode(AsAligned value)
        {
            var source = Load(value.Source);
            Bind(value, source);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(PrimitiveValue)"/>
        public void GenerateCode(PrimitiveValue value) =>
            Allocate(value);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(StringValue)"/>
        public void GenerateCode(StringValue value)
        {
            var target = Allocate(value);
            using var statement = BeginStatement(target);
            statement.AppendConstant(value.String);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(NullValue)"/>
        public void GenerateCode(NullValue value)
        {
            if (value.Type.IsVoidType)
                return;
            var target = Allocate(value);
            if (value.Type is StructureType structureType)
            {
                Declare(target);
                for (int i = 0, e = structureType.NumFields; i < e; ++i)
                {
                    using var statement = BeginStatement(target, i);
                    statement.AppendCast(structureType[i]);
                    statement.AppendConstant(0);
                }
            }
            else
            {
                using var statement = BeginStatement(target);
                statement.AppendCast(value.Type);
                statement.AppendConstant(0);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(StructureValue)"/>
        public void GenerateCode(StructureValue value)
        {
            var target = Allocate(value);
            Declare(target);
            for (int i = 0, e = value.Count; i < e; ++i)
            {
                using var statement = BeginStatement(target, i);
                statement.AppendArgument(Load(value[i]));
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GetField)"/>
        public void GenerateCode(GetField value)
        {
            var source = Load(value.ObjectValue);
            var target = Allocate(value);

            var span = value.FieldSpan;
            if (!span.HasSpan)
            {
                // Extract primitive value from the given target
                using var statement = BeginStatement(target);
                statement.AppendArgument(source);
                statement.AppendField(span.Access);
            }
            else
            {
                // Result is a structure type
                Declare(target);
                for (int i = 0; i < span.Span; ++i)
                {
                    using var statement = BeginStatement(target, i);
                    statement.AppendArgument(source);
                    statement.AppendField(span.Access.Add(i));
                }
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(SetField)"/>
        public void GenerateCode(SetField value)
        {
            var source = Load(value.ObjectValue);
            var set = Load(value.Value);
            var target = Allocate(value);

            // Copy value
            using (var statement = BeginStatement(target))
                statement.AppendArgument(source);

            var span = value.FieldSpan;
            if (!span.HasSpan)
            {
                // Update field value
                using var statement = BeginStatement(target, span.Access);
                statement.AppendArgument(set);
            }
            else
            {
                // Update field values
                for (int i = 0; i < span.Span; ++i)
                {
                    var targetAccess = span.Access.Add(i);
                    using var statement = BeginStatement(target, targetAccess);
                    statement.AppendArgument(set);
                    statement.AppendField(new FieldAccess(i));
                }
            }
        }

        private void MakeIntrinsicValue(
            Value value,
            string operation,
            string? args = null)
        {
            var target = Allocate(value);
            using var statement = BeginStatement(target);
            statement.AppendCommand(operation);
            if (args != null)
            {
                statement.BeginArguments();
                statement.AppendCommand(args);
                statement.EndArguments();
            }
        }

        private void MakeIntrinsicValue(
            Value value,
            string operation,
            DeviceConstantDimension3D dimension) =>
            MakeIntrinsicValue(
                value,
                operation,
                ((int)dimension).ToString());

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GridIndexValue)"/>
        public void GenerateCode(GridIndexValue value) =>
            MakeIntrinsicValue(
                value,
                CLInstructions.GetGridIndex,
                value.Dimension);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GroupIndexValue)"/>
        public void GenerateCode(GroupIndexValue value) =>
            MakeIntrinsicValue(
                value,
                CLInstructions.GetGroupIndex,
                value.Dimension);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GridDimensionValue)"/>
        public void GenerateCode(GridDimensionValue value) =>
            MakeIntrinsicValue(
                value,
                CLInstructions.GetGridSize,
                value.Dimension);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(GroupDimensionValue)"/>
        public void GenerateCode(GroupDimensionValue value) =>
            MakeIntrinsicValue(
                value,
                CLInstructions.GetGroupSize,
                value.Dimension);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(WarpSizeValue)"/>
        public void GenerateCode(WarpSizeValue value) =>
            MakeIntrinsicValue(
                value,
                CLInstructions.GetWarpSize);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(LaneIdxValue)"/>
        public void GenerateCode(LaneIdxValue value) =>
            MakeIntrinsicValue(
                value,
                CLInstructions.GetLaneIndexOperation);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(DynamicMemoryLengthValue)"/>
        public void GenerateCode(DynamicMemoryLengthValue value)
        {
            if (value.AddressSpace != MemoryAddressSpace.Shared)
                throw new InvalidCodeGenerationException();

            // Resolve the name of the global variable containing the length of the
            // shared dynamic memory buffer.
            var dynamicView = value.GetFirstUseNode().ResolveAs<Alloca>().AsNotNull();
            var lengthVariableName = GetSharedMemoryAllocationLengthName(dynamicView);

            // Load the dynamic memory size (in bytes) from the dynamic length variable
            // and divide by the size in bytes of the array element.
            var target = Allocate(value);
            using var statement = BeginStatement(target);
            statement.AppendCast(value.Type);
            var operation = CLInstructions.GetArithmeticOperation(
                BinaryArithmeticKind.Div,
                value.BasicValueType.IsFloat(),
                out bool isFunction);
            if (isFunction)
            {
                statement.AppendCommand(operation);
                statement.BeginArguments();
            }
            else
            {
                statement.OpenParen();
            }

            statement.AppendCast(dynamicView.ArrayLength.Type);
            statement.AppendCommand(lengthVariableName);

            if (!isFunction)
                statement.AppendCommand(operation);

            statement.AppendArgument();
            statement.AppendCast(value.ElementType.BasicValueType);
            statement.AppendConstant(value.ElementType.Size);

            if (isFunction)
                statement.EndArguments();
            else
                statement.CloseParen();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(PredicateBarrier)"/>
        public void GenerateCode(PredicateBarrier barrier)
        {
            var sourcePredicate = Load(barrier.Predicate);
            var target = Allocate(barrier);

            if (!CLInstructions.TryGetPredicateBarrier(
                barrier.Kind,
                out string? operation))
            {
                throw new InvalidCodeGenerationException();
            }

            using var statement = BeginStatement(target);
            statement.AppendCast(BasicValueType.Int1);
            statement.AppendCommand(operation);
            statement.BeginArguments();
            statement.AppendCast(BasicValueType.Int32);
            statement.AppendArgument(sourcePredicate);
            statement.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Barrier)"/>
        public void GenerateCode(Barrier barrier)
        {
            using var statement = BeginStatement(
                CLInstructions.GetBarrier(barrier.Kind));
            statement.BeginArguments();
            statement.AppendCommand(
                CLInstructions.GetMemoryFenceFlags(true));
            statement.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(Broadcast)"/>
        public void GenerateCode(Broadcast broadcast)
        {
            var source = Load(broadcast.Variable);
            var origin = Load(broadcast.Origin);
            var target = Allocate(broadcast);

            using var statement = BeginStatement(target);
            statement.AppendCommand(
                CLInstructions.GetBroadcastOperation(
                broadcast.Kind));
            statement.BeginArguments();
            statement.AppendArgument(source);
            statement.AppendArgument(origin);
            statement.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(WarpShuffle)"/>
        public void GenerateCode(WarpShuffle shuffle)
        {
            if (!Backend.Capabilities.SubGroupShuffle)
            {
                throw new InvalidCodeGenerationException(
                    "Subgroup shuffle requires SubGroupShuffle capability (cl_intel_subgroups or cl_khr_subgroup_shuffle)");
            }

            if (!CLInstructions.TryGetShuffleOperation(
                Backend.Vendor,
                shuffle.Kind,
                Backend.Capabilities.SubGroupShuffle,
                out string? operation))
            {
                throw new InvalidCodeGenerationException();
            }

            var source = Load(shuffle.Variable);
            var origin = Load(shuffle.Origin);
            var target = Allocate(shuffle);

            using var statement = BeginStatement(target);
            statement.AppendCommand(operation);
            statement.BeginArguments();

            statement.AppendArgument(source);
            // Intel intel_sub_group_shuffle_down/up take (current, next, delta); Khronos sub_group_shuffle_down/up take (value, delta)
            bool useIntelApi = Backend.Vendor == CLDeviceVendor.Intel;
            if (useIntelApi && (shuffle.Kind == ShuffleKind.Down || shuffle.Kind == ShuffleKind.Up))
            {
                statement.AppendArgument(source); // Intel "next" param
            }
            statement.AppendArgument(origin);

            statement.EndArguments();
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(SubWarpShuffle)"/>
        public void GenerateCode(SubWarpShuffle shuffle) =>
            throw new InvalidCodeGenerationException();

        /// <summary cref="IBackendCodeGenerator.GenerateCode(DebugAssertOperation)"/>
        public void GenerateCode(DebugAssertOperation debug) =>
            // Invalid debug node -> should have been removed
            debug.Assert(false);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(WriteToOutput)"/>
        public void GenerateCode(WriteToOutput writeToOutput) =>
            // Invalid write node -> should have been removed
            writeToOutput.Assert(false);

        /// <summary cref="IBackendCodeGenerator.GenerateCode(LanguageEmitValue)"/>
        public void GenerateCode(LanguageEmitValue value) =>
            // Ignore PTX instructions.
            value.Assert(value.LanguageKind == LanguageKind.PTX);
    }
}
