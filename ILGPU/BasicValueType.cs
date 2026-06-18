// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: BasicValueType.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

namespace ILGPU
{
    /// <summary>
    /// Represents a basic value type.
    /// </summary>
    public enum BasicValueType
    {
        /// <summary>
        /// Represent a non-basic value type.
        /// </summary>
        None,

        /// <summary>
        /// Represents an 1-bit integer.
        /// </summary>
        Int1,

        /// <summary>
        /// Represents an 8-bit integer.
        /// </summary>
        Int8,

        /// <summary>
        /// Represents a 16-bit integer.
        /// </summary>
        Int16,

        /// <summary>
        /// Represents a 32-bit integer.
        /// </summary>
        Int32,

        /// <summary>
        /// Represents a 64-bit integer.
        /// </summary>
        Int64,

        /// <summary>
        /// Represents a 16-bit float.
        /// </summary>
        Float16,

        /// <summary>
        /// Represents a 32-bit float.
        /// </summary>
        Float32,

        /// <summary>
        /// Represents a 64-bit float.
        /// </summary>
        Float64,

        /// <summary>
        /// Represents a 16-bit brain float (bfloat16). Appended at the end so existing
        /// ordinals (and the positional type tables indexed by them) are unchanged.
        /// </summary>
        BFloat16,

        /// <summary>
        /// Represents an 8-bit float in OCP E4M3 (E4M3FN) layout. Appended at the end so
        /// existing ordinals (and the positional type tables indexed by them) are unchanged.
        /// </summary>
        Float8E4M3,

        /// <summary>
        /// Represents an 8-bit float in OCP E5M2 layout. Appended at the end so existing
        /// ordinals (and the positional type tables indexed by them) are unchanged.
        /// </summary>
        Float8E5M2,

        /// <summary>
        /// Represents a 4-bit float in OCP E2M1 (E2M1FN) layout (the NVFP4/MXFP4 element
        /// format; 1-byte storage, value in the low nibble). Appended at the end so existing
        /// ordinals (and the positional type tables indexed by them) are unchanged.
        /// </summary>
        Float4E2M1,

        /// <summary>
        /// Represents a 4-bit integer (QInt4 signed / QUInt4 unsigned; signedness carried by the
        /// <see cref="ArithmeticBasicValueType"/>). Packed 2 per byte in device buffers, value in
        /// the low nibble. Appended at the end so existing ordinals (and the positional type tables
        /// indexed by them) are unchanged.
        /// </summary>
        QInt4,
    }

    /// <summary>
    /// Represents an arithmetic basic value type.
    /// </summary>
    public enum ArithmeticBasicValueType
    {
        /// <summary>
        /// Represent a non-arithmetic value type.
        /// </summary>
        None,

        /// <summary>
        /// Represents an 1-bit integer.
        /// </summary>
        UInt1,

        /// <summary>
        /// Represents an 8-bit integer.
        /// </summary>
        Int8,

        /// <summary>
        /// Represents a 16-bit integer.
        /// </summary>
        Int16,

        /// <summary>
        /// Represents a 32-bit integer.
        /// </summary>
        Int32,

        /// <summary>
        /// Represents a 64-bit integer.
        /// </summary>
        Int64,

        /// <summary>
        /// Represents a 16-bit float.
        /// </summary>
        Float16,

        /// <summary>
        /// Represents a 32-bit float.
        /// </summary>
        Float32,

        /// <summary>
        /// Represents a 64-bit float.
        /// </summary>
        Float64,

        /// <summary>
        /// Represents an 8-bit unsigned integer.
        /// </summary>
        UInt8,

        /// <summary>
        /// Represents a 16-bit unsigned integer.
        /// </summary>
        UInt16,

        /// <summary>
        /// Represents a 32-bit unsigned integer.
        /// </summary>
        UInt32,

        /// <summary>
        /// Represents a 64-bit unsigned integer.
        /// </summary>
        UInt64,

        /// <summary>
        /// Represents a 16-bit brain float (bfloat16). Appended at the end so existing
        /// ordinals are unchanged.
        /// </summary>
        BFloat16,

        /// <summary>
        /// Represents an 8-bit float in OCP E4M3 (E4M3FN) layout. Appended at the end so
        /// existing ordinals are unchanged.
        /// </summary>
        Float8E4M3,

        /// <summary>
        /// Represents an 8-bit float in OCP E5M2 layout. Appended at the end so existing
        /// ordinals are unchanged.
        /// </summary>
        Float8E5M2,

        /// <summary>
        /// Represents a 4-bit float in OCP E2M1 (E2M1FN) layout. Appended at the end so
        /// existing ordinals are unchanged.
        /// </summary>
        Float4E2M1,

        /// <summary>
        /// Represents a 4-bit signed integer (QInt4, two's complement -8..7). Appended at the end
        /// so existing ordinals are unchanged.
        /// </summary>
        QInt4,

        /// <summary>
        /// Represents a 4-bit unsigned integer (QUInt4, 0..15). Appended at the end so existing
        /// ordinals are unchanged.
        /// </summary>
        QUInt4,
    }
}
