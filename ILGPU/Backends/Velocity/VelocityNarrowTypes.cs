// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityNarrowTypes.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ILGPU.Backends.Velocity
{
    /// <summary>
    /// The low precision types Velocity keeps as 32bit floats in its lanes (the value is
    /// always exactly representable in the narrow type): Half, BFloat16, FP8 (E4M3,
    /// E5M2) and FP4 (E2M1). They are converted from and to their bits when they are
    /// loaded, stored, passed as kernel arguments or cast to integers, and rounded to the
    /// narrow type after every arithmetic operation and conversion. The conversions are
    /// the ones of the C# structs, so the results equal the ones of the CPU backend.
    /// The 4bit integers (QInt4, QUInt4) are kept as their raw nibble (0 to 15) in an
    /// integer lane and extended when converted.
    /// </summary>
    static class VelocityNarrowTypes
    {
        #region Type Classification

        /// <summary>
        /// Returns true if the given type is kept as a rounded 32bit float.
        /// </summary>
        public static bool IsNarrowFloat(this BasicValueType valueType) =>
            valueType switch
            {
                BasicValueType.Float16 => true,
                BasicValueType.BFloat16 => true,
                BasicValueType.Float8E4M3 => true,
                BasicValueType.Float8E5M2 => true,
                BasicValueType.Float4E2M1 => true,
                _ => false,
            };

        /// <summary>
        /// Returns true if the given type is kept as a rounded 32bit float.
        /// </summary>
        public static bool IsNarrowFloat(this ArithmeticBasicValueType valueType) =>
            valueType switch
            {
                ArithmeticBasicValueType.Float16 => true,
                ArithmeticBasicValueType.BFloat16 => true,
                ArithmeticBasicValueType.Float8E4M3 => true,
                ArithmeticBasicValueType.Float8E5M2 => true,
                ArithmeticBasicValueType.Float4E2M1 => true,
                _ => false,
            };

        /// <summary>
        /// Returns true if the given type is a packed 4bit integer.
        /// </summary>
        public static bool IsNibbleInt(this ArithmeticBasicValueType valueType) =>
            valueType == ArithmeticBasicValueType.QInt4 ||
            valueType == ArithmeticBasicValueType.QUInt4;

        /// <summary>
        /// Returns true if values of the given type are packed in nibbles (two per
        /// byte) when they are stored in views.
        /// </summary>
        public static bool IsPackedNibble(this BasicValueType valueType) =>
            valueType == BasicValueType.QInt4 ||
            valueType == BasicValueType.Float4E2M1;

        /// <summary>
        /// Returns the arithmetic type the lanes of the given type are treated like when
        /// they are converted.
        /// </summary>
        public static ArithmeticBasicValueType GetLaneType(
            this ArithmeticBasicValueType valueType) =>
            valueType.IsNarrowFloat()
            ? ArithmeticBasicValueType.Float32
            : valueType == ArithmeticBasicValueType.QInt4
            ? ArithmeticBasicValueType.Int32
            : valueType == ArithmeticBasicValueType.QUInt4
            ? ArithmeticBasicValueType.UInt32
            : valueType;

        #endregion

        #region Per Lane Conversions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DecodeF16(int bits) =>
            (float)Half.FromRawBits((ushort)bits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeF16(float value) =>
            ((Half)value).RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DecodeBF16(int bits) =>
            (float)BFloat16.FromRawBits((ushort)bits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeBF16(float value) =>
            ((BFloat16)value).RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DecodeE4M3(int bits) =>
            (float)Float8E4M3.FromRawBits((byte)bits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeE4M3(float value) =>
            ((Float8E4M3)value).RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DecodeE5M2(int bits) =>
            (float)Float8E5M2.FromRawBits((byte)bits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeE5M2(float value) =>
            ((Float8E5M2)value).RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DecodeE2M1(int bits) =>
            (float)Float4E2M1.FromRawBits((byte)(bits & 0xF));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeE2M1(float value) =>
            ((Float4E2M1)value).RawValue & 0xF;

        #endregion

        #region Kernel Parameters

        // Kernel arguments of these types arrive as the C# structs

        public static float ParameterF16(Half value) => (float)value;

        public static float ParameterBF16(BFloat16 value) => (float)value;

        public static float ParameterE4M3(Float8E4M3 value) => (float)value;

        public static float ParameterE5M2(Float8E5M2 value) => (float)value;

        public static float ParameterE2M1(Float4E2M1 value) => (float)value;

        public static int ParameterNibble(QInt4 value) => value.RawValue & 0xF;

        /// <summary>
        /// Returns the method that converts a kernel argument of the given type into the
        /// value of a lane.
        /// </summary>
        public static MethodInfo GetParameterMethod(BasicValueType valueType) =>
            typeof(VelocityNarrowTypes).GetMethod(
                valueType switch
                {
                    BasicValueType.Float16 => nameof(ParameterF16),
                    BasicValueType.BFloat16 => nameof(ParameterBF16),
                    BasicValueType.Float8E4M3 => nameof(ParameterE4M3),
                    BasicValueType.Float8E5M2 => nameof(ParameterE5M2),
                    BasicValueType.Float4E2M1 => nameof(ParameterE2M1),
                    BasicValueType.QInt4 => nameof(ParameterNibble),
                    _ => throw new NotSupportedException(valueType.ToString()),
                },
                BindingFlags.Public | BindingFlags.Static)!;

        #endregion

        #region Nibble Memory

        /// <summary>
        /// Reads the nibble with the given index (two per byte, low nibble first).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int LoadNibble(long nibbleAddress)
        {
            byte value = *(byte*)(nibbleAddress >> 1);
            return (value >> (int)((nibbleAddress & 1L) << 2)) & 0xF;
        }

        /// <summary>
        /// Writes the nibble with the given index. Neighboring nibbles (also the other
        /// one in the same byte) can be written by other threads at the same time: the
        /// 32bit word that contains the byte is replaced atomically.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void StoreNibble(long nibbleAddress, int value)
        {
            long byteAddress = nibbleAddress >> 1;
            var word = (int*)(byteAddress & ~3L);
            int shift = (int)(((byteAddress & 3L) << 3) + ((nibbleAddress & 1L) << 2));
            int mask = 0xF << shift;
            int bits = (value & 0xF) << shift;
            ref int target = ref *word;
            int current = System.Threading.Volatile.Read(ref target);
            while (true)
            {
                int updated = (current & ~mask) | bits;
                int previous = System.Threading.Interlocked.CompareExchange(
                    ref target,
                    updated,
                    current);
                if (previous == current)
                    return;
                current = previous;
            }
        }

        #endregion
    }
}
