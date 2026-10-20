// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: Vec256Operations.NarrowTypes.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// Operations of the low precision types (see VelocityNarrowTypes), generated for every
// vector width by VecNarrowTypes.ttinclude

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

#if NET7_0_OR_GREATER

namespace ILGPU.Backends.Velocity.Vec256
{
    static partial class Vec256Operations
    {
        #region Low Precision Types

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> DecodeNarrowF16(Vector256<int> warp) =>
            Vector256.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(3))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(4))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(5))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(6))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> EncodeNarrowF16(Vector256<int> warp) =>
            Vector256.Create(VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(3))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(4))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(5))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(6))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> DecodeNarrowBF16(Vector256<int> warp) =>
            Vector256.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(3))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(4))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(5))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(6))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> EncodeNarrowBF16(Vector256<int> warp) =>
            Vector256.Create(VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(3))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(4))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(5))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(6))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> DecodeNarrowE4M3(Vector256<int> warp) =>
            Vector256.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(3))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(4))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(5))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(6))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> EncodeNarrowE4M3(Vector256<int> warp) =>
            Vector256.Create(VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(3))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(4))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(5))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(6))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> DecodeNarrowE5M2(Vector256<int> warp) =>
            Vector256.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(3))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(4))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(5))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(6))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> EncodeNarrowE5M2(Vector256<int> warp) =>
            Vector256.Create(VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(3))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(4))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(5))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(6))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> DecodeNarrowE2M1(Vector256<int> warp) =>
            Vector256.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(3))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(4))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(5))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(6))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> EncodeNarrowE2M1(Vector256<int> warp) =>
            Vector256.Create(VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(3))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(4))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(5))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(6))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(7))));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> SignExtendNibble(Vector256<int> warp) =>
            Vector256.ShiftRightArithmetic(Vector256.ShiftLeft(warp, 28), 28);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector256<int> MaskNibble(Vector256<int> warp) =>
            warp & Vector256.Create(0xF);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector256<int> LoadNibble(
            Vector256<int> mask,
            (Vector256<long>, Vector256<long>) address)
        {
            int result0 = mask.GetElement(0) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item1.GetElement(0))
                : 0;
            int result1 = mask.GetElement(1) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item1.GetElement(1))
                : 0;
            int result2 = mask.GetElement(2) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item1.GetElement(2))
                : 0;
            int result3 = mask.GetElement(3) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item1.GetElement(3))
                : 0;
            int result4 = mask.GetElement(4) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item2.GetElement(0))
                : 0;
            int result5 = mask.GetElement(5) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item2.GetElement(1))
                : 0;
            int result6 = mask.GetElement(6) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item2.GetElement(2))
                : 0;
            int result7 = mask.GetElement(7) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item2.GetElement(3))
                : 0;
            return Vector256.Create(result0, result1, result2, result3, result4, result5, result6, result7);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void StoreNibble(
            Vector256<int> mask,
            (Vector256<long>, Vector256<long>) address,
            Vector256<int> value)
        {
            if (mask.GetElement(0) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item1.GetElement(0), value.GetElement(0));
            if (mask.GetElement(1) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item1.GetElement(1), value.GetElement(1));
            if (mask.GetElement(2) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item1.GetElement(2), value.GetElement(2));
            if (mask.GetElement(3) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item1.GetElement(3), value.GetElement(3));
            if (mask.GetElement(4) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item2.GetElement(0), value.GetElement(4));
            if (mask.GetElement(5) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item2.GetElement(1), value.GetElement(5));
            if (mask.GetElement(6) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item2.GetElement(2), value.GetElement(6));
            if (mask.GetElement(7) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item2.GetElement(3), value.GetElement(7));
        }

        public static readonly MethodInfo SignExtendNibbleMethod =
            GetMethod(nameof(SignExtendNibble));
        public static readonly MethodInfo MaskNibbleMethod =
            GetMethod(nameof(MaskNibble));
        public static readonly MethodInfo LoadNibbleMethod =
            GetMethod(nameof(LoadNibble));
        public static readonly MethodInfo StoreNibbleMethod =
            GetMethod(nameof(StoreNibble));

        /// <summary>Gets the operation that converts the lanes of a low precision float from its bits.</summary>
        public static MethodInfo GetDecodeNarrowOperation(BasicValueType valueType) =>
            valueType switch
            {
                BasicValueType.Float16 => GetMethod(nameof(DecodeNarrowF16)),
                BasicValueType.BFloat16 => GetMethod(nameof(DecodeNarrowBF16)),
                BasicValueType.Float8E4M3 => GetMethod(nameof(DecodeNarrowE4M3)),
                BasicValueType.Float8E5M2 => GetMethod(nameof(DecodeNarrowE5M2)),
                BasicValueType.Float4E2M1 => GetMethod(nameof(DecodeNarrowE2M1)),
                _ => throw new NotSupportedException(valueType.ToString()),
            };

        /// <summary>Gets the operation that converts the lanes of a low precision float to its bits.</summary>
        public static MethodInfo GetEncodeNarrowOperation(BasicValueType valueType) =>
            valueType switch
            {
                BasicValueType.Float16 => GetMethod(nameof(EncodeNarrowF16)),
                BasicValueType.BFloat16 => GetMethod(nameof(EncodeNarrowBF16)),
                BasicValueType.Float8E4M3 => GetMethod(nameof(EncodeNarrowE4M3)),
                BasicValueType.Float8E5M2 => GetMethod(nameof(EncodeNarrowE5M2)),
                BasicValueType.Float4E2M1 => GetMethod(nameof(EncodeNarrowE2M1)),
                _ => throw new NotSupportedException(valueType.ToString()),
            };

        #endregion
    }
}

#endif
