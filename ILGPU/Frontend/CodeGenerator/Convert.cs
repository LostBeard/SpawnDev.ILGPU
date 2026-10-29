// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2018-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: Convert.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Util;
using System;

namespace ILGPU.Frontend
{
    partial class CodeGenerator
    {
        /// <summary>
        /// Realizes a convert instruction.
        /// </summary>
        /// <param name="targetType">The target type.</param>
        /// <param name="instructionFlags">The instruction flags.</param>
        private void MakeConvert(
            Type targetType,
            ILInstructionFlags instructionFlags)
        {
            var value = Block.Pop();
            var convertFlags = ConvertFlags.None;
            if (instructionFlags.HasFlags(ILInstructionFlags.Unsigned))
                convertFlags |= ConvertFlags.SourceUnsigned;
            if (targetType.IsUnsignedInt())
            {
                convertFlags |= ConvertFlags.SourceUnsigned;
                convertFlags |= ConvertFlags.TargetUnsigned;
            }
            var targetTypeNode = Builder.CreateType(targetType);
            var result = CreateConversion(
                value,
                targetTypeNode,
                convertFlags);

            // conv.i1 / u1 / i2 / u2 push their result extended to 32 bits by the
            // target type's sign, as loads of these types do (LoadOntoEvaluationStack):
            // a later conv.u8 / conv.r.un of it then reads that 32-bit value, and
            // (long)(uint)(short)x zero-extends the sign-extended short.
            if (result.BasicValueType == BasicValueType.Int8 ||
                result.BasicValueType == BasicValueType.Int16)
            {
                result = CreateConversion(
                    result,
                    Builder.GetPrimitiveType(BasicValueType.Int32),
                    targetType.IsUnsignedInt()
                        ? ConvertFlags.SourceUnsigned
                        : ConvertFlags.None);
            }
            Block.Push(result);
        }

        /// <summary>
        /// Coverts the given value to the target type.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="targetType">The target type.</param>
        /// <param name="flags">
        /// True, if the comparison should be forced to be unsigned.
        /// </param>
        public Value CreateConversion(
            Value value,
            TypeNode targetType,
            ConvertFlags flags)
        {
            if (value.Type is AddressSpaceType)
            {
                var otherType = targetType.AsNotNullCast<AddressSpaceType>();
                value = Builder.CreateAddressSpaceCast(
                    Location,
                    value,
                    otherType.AddressSpace);
                return otherType is ViewType
                    ? (Value)Builder.CreateViewCast(
                        Location,
                        value,
                        otherType.ElementType)
                    : (Value)Builder.CreatePointerCast(
                        Location,
                        value,
                        otherType.ElementType);
            }
            else if (
                targetType is PointerType targetPointerType &&
                targetPointerType.ElementType.IsRootType)
            {
                // Must be a reflection array call
                // FIXME: note that we have to update this spot once we add support
                // for class types
                Location.Assert(value.Type is StructureType);
                return value;
            }
            return Builder.CreateConvert(
                Location,
                value,
                targetType,
                flags);
        }
    }
}
