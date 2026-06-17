// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: CLCodeGenerator.Views.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Util;

namespace ILGPU.Backends.OpenCL
{
    partial class CLCodeGenerator
    {
        /// <summary cref="IBackendCodeGenerator.GenerateCode(LoadElementAddress)"/>
        public void GenerateCode(LoadElementAddress value)
        {
            var elementIndex = LoadAs<PrimitiveVariable>(value.Offset);
            var source = Load(value.Source);

            // Float16 emulation: when cl_khr_fp16 is unavailable (Float16Native=false),
            // don't compute &source[idx] (wrong stride - uses float size). Instead,
            // store base pointer + element index for vload_half/vstore_half in the
            // Load/Store handlers. Gated on !Float16Native because Capabilities.Float16
            // is now always true on OpenCL (emulation always works via vload_half).
            if (value.Type is PointerType ptrType
                && ptrType.ElementType is PrimitiveType ptElem
                && ptElem.BasicValueType == BasicValueType.Float16
                && !TypeGenerator.Capabilities.Float16Native)
            {
                var target = AllocatePointerType(ptrType);
                // Still emit the &source[idx] for the variable binding (won't be dereferenced)
                using (var statement = BeginStatement(target))
                {
                    statement.AppendCommand(CLInstructions.AddressOfOperation);
                    statement.Append(source);
                    statement.AppendIndexer(elementIndex);
                }
                Bind(value, target);
                // Track for vload_half/vstore_half: base pointer + element index
                _f16EmulatedLEAs[target.ToString()] = (source, elementIndex);
                return;
            }

            // BFloat16 emulation: bf16 has no native OpenCL type, so views are ushort* and
            // load/store convert via shift helpers. Track (basePtr, index) like the Half path.
            if (value.Type is PointerType ptrTypeBf
                && ptrTypeBf.ElementType is PrimitiveType ptElemBf
                && ptElemBf.BasicValueType == BasicValueType.BFloat16)
            {
                var targetBf = AllocatePointerType(ptrTypeBf);
                using (var statement = BeginStatement(targetBf))
                {
                    statement.AppendCommand(CLInstructions.AddressOfOperation);
                    statement.Append(source);
                    statement.AppendIndexer(elementIndex);
                }
                Bind(value, targetBf);
                _bf16EmulatedLEAs[targetBf.ToString()] = (source, elementIndex);
                return;
            }

            // FP8 emulation: E4M3/E5M2 have no native OpenCL type, so views are uchar* and
            // load/store convert via the _e*m* helpers. Track (basePtr, index, isE4M3).
            if (value.Type is PointerType ptrTypeFp8
                && ptrTypeFp8.ElementType is PrimitiveType ptElemFp8
                && (ptElemFp8.BasicValueType == BasicValueType.Float8E4M3
                    || ptElemFp8.BasicValueType == BasicValueType.Float8E5M2))
            {
                var targetFp8 = AllocatePointerType(ptrTypeFp8);
                using (var statement = BeginStatement(targetFp8))
                {
                    statement.AppendCommand(CLInstructions.AddressOfOperation);
                    statement.Append(source);
                    statement.AppendIndexer(elementIndex);
                }
                Bind(value, targetFp8);
                _fp8EmulatedLEAs[targetFp8.ToString()] =
                    (source, elementIndex, ptElemFp8.BasicValueType == BasicValueType.Float8E4M3);
                return;
            }

            // FP4 emulation: E2M1 has no native OpenCL type, so views are uchar* (4-bit value in
            // the low nibble) and load/store convert via the _e2m1 helpers. Track (basePtr, index).
            if (value.Type is PointerType ptrTypeFp4
                && ptrTypeFp4.ElementType is PrimitiveType ptElemFp4
                && ptElemFp4.BasicValueType == BasicValueType.Float4E2M1)
            {
                var targetFp4 = AllocatePointerType(ptrTypeFp4);
                using (var statement = BeginStatement(targetFp4))
                {
                    statement.AppendCommand(CLInstructions.AddressOfOperation);
                    statement.Append(source);
                    statement.AppendIndexer(elementIndex);
                }
                Bind(value, targetFp4);
                _fp4EmulatedLEAs[targetFp4.ToString()] = (source, elementIndex);
                return;
            }

            var target2 = AllocatePointerType(value.Type.AsNotNullCast<PointerType>());

            using (var statement = BeginStatement(target2))
            {
                statement.AppendCommand(CLInstructions.AddressOfOperation);
                statement.Append(source);
                statement.AppendIndexer(elementIndex);
            }

            Bind(value, target2);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(AddressSpaceCast)"/>
        public void GenerateCode(AddressSpaceCast value)
        {
            var targetType = value.TargetType.AsNotNullCast<AddressSpaceType>();
            var source = Load(value.Value);
            var target = Allocate(value);

            bool isOperation = CLInstructions.TryGetAddressSpaceCast(
                value.TargetAddressSpace,
                out string? operation);

            void GeneratePointerCast(StatementEmitter statement)
            {
                if (isOperation)
                {
                    // There is a specific cast operation
                    statement.AppendCommand(operation.AsNotNull());
                    statement.BeginArguments();
                }
                else
                {
                    statement.AppendPointerCast(TypeGenerator[targetType.ElementType]);
                }
                statement.Append(source);
            }

            using (var statement = BeginStatement(target))
            {
                GeneratePointerCast(statement);
                if (isOperation)
                    statement.EndArguments();
            }
        }
    }
}
