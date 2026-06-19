// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2018-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: PTXCodeGenerator.Views.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Util;
using System.Collections.Generic;
using System.Diagnostics;

namespace ILGPU.Backends.PTX
{
    partial class PTXCodeGenerator
    {
        /// <summary>
        /// Maps a packed-4-bit (QInt4) <see cref="LoadElementAddress"/> to the kept nibble-shift
        /// register ((index &amp; 1) &lt;&lt; 2 = 0 or 4) so the corresponding Load can extract + sign-extend
        /// the right nibble of the byte at base + (index &gt;&gt; 1). The shift register is intentionally
        /// not freed (it must outlive the LEA to the Load, and one LEA may feed several Loads).
        /// </summary>
        private readonly Dictionary<Value, HardwareRegister> _qint4LEAShift = new();

        /// <summary>
        /// Traces a packed-4-bit Load/LEA source view back to its kernel parameter and reports
        /// whether that parameter's CLR element type is the UNSIGNED <see cref="QUInt4"/> (vs signed
        /// <see cref="QInt4"/>). BasicValueType.QInt4 is shared by both, so the packed nibble load
        /// must consult the CLR param type to choose zero-extend (QUInt4) vs sign-extend (QInt4).
        /// Only valid on the entry method (where Method.Parameters map to EntryPoint.Parameters);
        /// returns false (→ signed) for helper methods or any source it cannot trace to a view param.
        /// </summary>
        private bool PackedViewSourceIsQUInt4(Value source)
        {
            if (!Method.HasFlags(MethodFlags.EntryPoint))
                return false;
            var cur = source.Resolve();
            for (int depth = 0; cur != null && depth < 20; depth++)
            {
                if (cur is Parameter p)
                {
                    int mi = -1;
                    for (int i = 0; i < Method.Parameters.Count; i++)
                        if (Method.Parameters[i] == p) { mi = i; break; }
                    if (mi < 0) return false;
                    int userIdx = mi - EntryPoint.KernelIndexParameterOffset;
                    if (userIdx < 0 || userIdx >= EntryPoint.Parameters.Count) return false;
                    var t = EntryPoint.Parameters[userIdx];
                    return t.IsGenericType
                        && t.GetGenericArguments() is var g && g.Length > 0 && g[0] == typeof(QUInt4);
                }
                cur = cur switch
                {
                    GetField gf => gf.ObjectValue.Resolve(),
                    LoadFieldAddress lfa => lfa.Source.Resolve(),
                    Load ld => ld.Source.Resolve(),
                    ConvertValue cv => cv.Value.Resolve(),
                    NewView nv => nv.Pointer.Resolve(),
                    AddressSpaceCast asc => asc.Value.Resolve(),
                    PointerCast pc => pc.Value.Resolve(),
                    SubViewValue sv => sv.Source.Resolve(),
                    LoadElementAddress lea => lea.Source.Resolve(),
                    _ => null
                };
            }
            return false;
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(LoadElementAddress)"/>
        public void GenerateCode(LoadElementAddress value)
        {
            var elementIndex = LoadPrimitive(value.Offset);
            var targetAddressRegister = AllocateHardware(value);
            Debug.Assert(value.IsPointerAccess, "Invalid pointer access");

            var address = LoadPrimitive(value.Source);
            var sourceType = value.Source.Type.AsNotNullCast<AddressSpaceType>();
            var elementSize = sourceType.ElementType.Size;

            // Packed 4-bit (QInt4): 2 nibbles per byte. Address by the BYTE (index >> 1) and keep the
            // nibble shift (index & 1) << 2 for the Load. effIndex/effSize feed the normal address
            // math below with a stride of 1 byte over the packed buffer.
            var effIndex = elementIndex;
            var effSize = elementSize;
            if (sourceType.ElementType is PrimitiveType qpt
                && qpt.BasicValueType == BasicValueType.QInt4)
            {
                bool is32 = value.Is32BitAccess;
                // byteIndex = index >> 1
                var byteIndex = is32
                    ? AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32)
                    : AllocateRegister(BasicValueType.Int64, PTXRegisterKind.Int64);
                using (var c = BeginCommand(is32 ? "shr.u32" : "shr.u64"))
                { c.AppendArgument(byteIndex); c.AppendArgument(elementIndex); c.AppendConstant(1); }
                effIndex = byteIndex;
                effSize = 1;

                // keptShift (i32) = (index & 1) << 2   (0 for the low nibble, 4 for the high nibble)
                var keptShift = AllocateRegister(BasicValueType.Int32, PTXRegisterKind.Int32);
                if (is32)
                {
                    using var c = BeginCommand("and.b32");
                    c.AppendArgument(keptShift); c.AppendArgument(elementIndex); c.AppendConstant(1);
                }
                else
                {
                    using (var c = BeginCommand("cvt.u32.u64"))
                    { c.AppendArgument(keptShift); c.AppendArgument(elementIndex); }
                    using var c2 = BeginCommand("and.b32");
                    c2.AppendArgument(keptShift); c2.AppendArgument(keptShift); c2.AppendConstant(1);
                }
                using (var c = BeginCommand("shl.b32"))
                { c.AppendArgument(keptShift); c.AppendArgument(keptShift); c.AppendConstant(2); }
                _qint4LEAShift[value] = keptShift;
            }

            if (value.Is32BitAccess)
            {
                // Perform two efficient operations TODO
                var offsetRegister = AllocatePlatformRegister(out RegisterDescription _);
                using (var command = BeginCommand(
                    PTXInstructions.GetLEAMulOperation(Backend.PointerArithmeticType)))
                {
                    command.AppendArgument(offsetRegister);
                    command.AppendArgument(effIndex);
                    command.AppendConstant(effSize);
                }

                using (var command = BeginCommand(
                    PTXInstructions.GetArithmeticOperation(
                        BinaryArithmeticKind.Add,
                        Backend.PointerArithmeticType,
                        Backend.Capabilities,
                        false)))
                {
                    command.AppendArgument(targetAddressRegister);
                    command.AppendArgument(address);
                    command.AppendArgument(offsetRegister);
                }

                FreeRegister(offsetRegister);
            }
            else
            {
                // Use an efficient MAD instruction to compute the effective address
                using var command = BeginCommand(
                    PTXInstructions.GetArithmeticOperation(
                        TernaryArithmeticKind.MultiplyAdd,
                        Backend.PointerArithmeticType));
                command.AppendArgument(targetAddressRegister);
                command.AppendArgument(effIndex);
                command.AppendConstant(effSize);
                command.AppendArgument(address);
            }
        }

        /// <summary>
        /// Creates an address-space cast conversion.
        /// </summary>
        /// <param name="sourceRegister">The source register.</param>
        /// <param name="targetRegister">The target register.</param>
        /// <param name="sourceAddressSpace">The source address space.</param>
        /// <param name="targetAddressSpace">The target address space.</param>
        private void CreateAddressSpaceCast(
            PrimitiveRegister sourceRegister,
            HardwareRegister targetRegister,
            MemoryAddressSpace sourceAddressSpace,
            MemoryAddressSpace targetAddressSpace)
        {
            var toGeneric = targetAddressSpace == MemoryAddressSpace.Generic;
            var addressSpaceOperation = PTXInstructions.GetAddressSpaceCast(toGeneric);
            var addressSpaceOperationSuffix =
                PTXInstructions.GetAddressSpaceCastSuffix(Backend);

            using var command = BeginCommand(addressSpaceOperation);
            command.AppendAddressSpace(
                toGeneric ? sourceAddressSpace : targetAddressSpace);
            command.AppendSuffix(addressSpaceOperationSuffix);
            command.AppendArgument(targetRegister);
            command.AppendArgument(sourceRegister);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(AddressSpaceCast)"/>
        public void GenerateCode(AddressSpaceCast value)
        {
            var sourceType = value.SourceType.As<AddressSpaceType>(value);
            var targetAdressRegister = AllocateHardware(value);
            value.Assert(value.IsPointerCast);

            var address = LoadPrimitive(value.Value);
            CreateAddressSpaceCast(
                address,
                targetAdressRegister,
                sourceType.AddressSpace,
                value.TargetAddressSpace);
        }
    }
}
