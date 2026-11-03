// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityCodeGenerator.Views.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Types;
using ILGPU.IR.Values;

namespace ILGPU.Backends.Velocity
{
    partial class VelocityCodeGenerator<TILEmitter>
    {
        /// <inheritdoc />
        public void GenerateCode(IntAsPointerCast cast)
        {
            // Load the integer information
            Load(cast.Value);

            // Check whether we have to convert it to a 64bit value
            if (cast.SourceType.BasicValueType.IsTreatedAs32Bit())
            {
                // Convert it to a 64bit pointer
                Specializer.Convert32To64(Emitter, VelocityWarpOperationMode.U);
            }

            // The integer can now be interpreted as pointer
            Store(cast);
        }

        /// <summary>
        /// Creates an alias or stores a temporary value to ensure proper phi bindings.
        /// </summary>
        private void AliasOrStore(Value value, Value source)
        {
            if (source is PhiValue)
            {
                Load(source);
                Store(value);
            }
            else
            {
                Alias(value, GetLocal(source));
            }
        }

        /// <inheritdoc />
        public void GenerateCode(PointerAsIntCast cast) =>
            AliasOrStore(cast, cast.Value);

        /// <inheritdoc />
        public void GenerateCode(PointerCast cast) =>
            AliasOrStore(cast, cast.Value);

        /// <inheritdoc />
        public void GenerateCode(AddressSpaceCast value) =>
            AliasOrStore(value, value.Value);

        /// <summary>
        /// Whether <paramref name="source"/> (through pointer and address-space casts) is the element address of
        /// a packed 4-bit view, which counts nibbles, rather than a view's byte base.
        /// </summary>
        private static bool IsPackedNibbleAddress(Value source)
        {
            var cur = source.Resolve();
            for (int depth = 0; cur != null && depth < 8; depth++)
            {
                if (cur is LoadElementAddress lea)
                    return lea.Source.Type is AddressSpaceType ast
                        && ast.ElementType.BasicValueType.IsPackedNibble();
                cur = cur switch
                {
                    AddressSpaceCast asc => asc.Value.Resolve(),
                    PointerCast pc => pc.Value.Resolve(),
                    _ => null,
                };
            }
            return false;
        }

        /// <inheritdoc />
        public void GenerateCode(LoadElementAddress value)
        {
            var sourceType = value.Source.Type.As<AddressSpaceType>(value);
            if (sourceType.ElementType.BasicValueType.IsPackedNibble())
            {
                // Packed 4bit elements (two per byte): the address counts nibbles, the
                // loads and stores of these types split it into byte and nibble.
                // A source that is ITSELF a packed element address (an in-kernel SubView:
                // `view.SubView(off, n)[i]`) already counts nibbles and only adds the offset;
                // doubling it again read element 2*off + i (found 2026-11-02 by
                // PackedQUInt4_InKernelSubView_ReadsItsOwnElements).
                if (IsPackedNibbleAddress(value.Source))
                {
                    Load(value.Offset);
                    if (value.Is32BitAccess)
                        Specializer.Convert32To64(Emitter, VelocityWarpOperationMode.I);
                    Load(value.Source);
                    Specializer.BinaryOperation64(
                        Emitter,
                        BinaryArithmeticKind.Add,
                        VelocityWarpOperationMode.U);
                    Store(value);
                    return;
                }
                Load(value.Source);
                Emitter.EmitConstant(2L);
                Specializer.ConvertScalarTo64(Emitter, VelocityWarpOperationMode.U);
                Load(value.Offset);
                if (value.Is32BitAccess)
                    Specializer.Convert32To64(Emitter, VelocityWarpOperationMode.I);
                Specializer.TernaryOperation64(
                    Emitter,
                    TernaryArithmeticKind.MultiplyAdd,
                    VelocityWarpOperationMode.U);
                Store(value);
                return;
            }

            // Load the raw element offset to multiply
            Load(value.Offset);

            // Widen the source address if necessary
            if (value.Is32BitAccess)
                Specializer.Convert32To64(Emitter, VelocityWarpOperationMode.I);

            // Load the source type information and the element size to multiply
            Emitter.EmitConstant((long)sourceType.ElementType.Size);
            Specializer.ConvertScalarTo64(Emitter, VelocityWarpOperationMode.U);

            // Load the source vector to add
            Load(value.Source);

            // Perform the actual offset computation
            Specializer.TernaryOperation64(
                Emitter,
                TernaryArithmeticKind.MultiplyAdd,
                VelocityWarpOperationMode.U);
            Store(value);
        }

        /// <inheritdoc />
        public void GenerateCode(LoadFieldAddress value)
        {
            // Compute the actual field offset based on the vectorized type
            long offset = value.StructureType.GetOffset(value.FieldSpan.Access.Index);

            // If this results in an actual byte offset... add it
            if (offset != 0L)
            {
                // Load the source addresses
                Load(value.Source);

                // Load constant
                Emitter.EmitConstant(offset);
                Specializer.ConvertScalarTo64(Emitter, VelocityWarpOperationMode.U);

                // Adjust address
                Specializer.BinaryOperation64(
                    Emitter,
                    BinaryArithmeticKind.Add,
                    VelocityWarpOperationMode.U);

                // Store the newly computed offset
                Store(value);
            }
            else
            {
                AliasOrStore(value, value.Source);
            }
        }

        /// <inheritdoc />
        public void GenerateCode(AlignTo value) =>
            AliasOrStore(value, value.Source);

        /// <inheritdoc />
        public void GenerateCode(AsAligned value) =>
            AliasOrStore(value, value.Source);

        /// <inheritdoc />
        public void GenerateCode(DynamicMemoryLengthValue value)
        {
            if (value.AddressSpace != MemoryAddressSpace.Shared)
                throw new InvalidCodeGenerationException();

            // Load our shared memory length
            var elementType = TypeGenerator.GetLinearizedScalarType(value.ElementType);
            Specializer.GetDynamicSharedMemoryLength(Emitter, elementType);

            // Store the computed length
            Store(value);
        }

        /// <inheritdoc />
        public void GenerateCode(LanguageEmitValue value) { }
    }
}
