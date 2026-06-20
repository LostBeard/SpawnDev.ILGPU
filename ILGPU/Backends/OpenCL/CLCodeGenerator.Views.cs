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

using ILGPU.IR;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Util;

namespace ILGPU.Backends.OpenCL
{
    partial class CLCodeGenerator
    {
        /// <summary>
        /// Traces a packed-4-bit LEA source view back to its kernel parameter and reports whether
        /// that parameter's CLR element type is the UNSIGNED <see cref="QUInt4"/> (vs signed
        /// <see cref="QInt4"/>). BasicValueType.QInt4 is shared by both, so the packed nibble load
        /// must consult the CLR param type to choose zero-extend (QUInt4) vs sign-extend (QInt4).
        /// Only valid on the entry method (where Method.Parameters map to EntryPoint.Parameters);
        /// returns false (→ signed) for helper methods or any source it cannot trace to a view param.
        /// </summary>
        private bool PackedViewSourceIsQUInt4(Value source)
        {
            var cur = source.Resolve();
            for (int depth = 0; cur != null && depth < 20; depth++)
            {
                if (cur is Parameter p)
                {
                    int mi = -1;
                    for (int i = 0; i < Method.Parameters.Count; i++)
                        if (Method.Parameters[i] == p) { mi = i; break; }
                    if (mi < 0) return false;
                    if (Method.HasFlags(MethodFlags.EntryPoint))
                    {
                        int userIdx = mi - EntryPoint.KernelIndexParameterOffset;
                        if (userIdx < 0 || userIdx >= EntryPoint.Parameters.Count) return false;
                        var t = EntryPoint.Parameters[userIdx];
                        return t.IsGenericType
                            && t.GetGenericArguments() is var g && g.Length > 0 && g[0] == typeof(QUInt4);
                    }
                    // Helper (non-entry) method: map the IR param to the managed CLR parameter via
                    // Method.Source so a QUInt4 view loaded inside a NoInlining helper zero-extends.
                    if (!Method.HasSource) return false;
                    var clr = Method.Source.GetParameters();
                    if (mi < 0 || mi >= clr.Length) return false;
                    var ct = clr[mi].ParameterType;
                    if (ct.IsByRef) ct = ct.GetElementType()!;
                    return ct.IsGenericType
                        && ct.GetGenericArguments() is var cg && cg.Length > 0 && cg[0] == typeof(QUInt4);
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

            // Int4/UInt4 PACKED emulation: the buffer is uchar* with 2 nibbles per byte. Keep the
            // element index (do NOT fold into a byte address) so the Load/Store can compute the byte
            // (index>>1) and nibble ((index&1)*4). Track (basePtr, index, isSigned).
            if (value.Type is PointerType ptrTypeInt4
                && ptrTypeInt4.ElementType is PrimitiveType ptElemQInt4
                && ptElemQInt4.BasicValueType == BasicValueType.QInt4)
            {
                var targetInt4 = AllocatePointerType(ptrTypeInt4);
                using (var statement = BeginStatement(targetInt4))
                {
                    statement.AppendCommand(CLInstructions.AddressOfOperation);
                    statement.Append(source);
                    statement.AppendIndexer(elementIndex);
                }
                Bind(value, targetInt4);
                // BasicValueType.QInt4 does not carry signedness (QInt4 and QUInt4 both lower to it),
                // so the packed LOAD sign-extends for signed QInt4 and zero-extends for unsigned QUInt4.
                // Recover the signedness from the source view's CLR param type (EntryPoint.Parameters).
                bool isSignedQ4 = !PackedViewSourceIsQUInt4(value.Source);
                _qint4EmulatedLEAs[targetInt4.ToString()] = (source, elementIndex, isSignedQ4);
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
