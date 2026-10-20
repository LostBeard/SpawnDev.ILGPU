// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: Vec128Operations.NarrowTypes.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// Operations of the low precision types (see VelocityNarrowTypes): hand written, not
// generated from the T4 template, to keep the template free of the type specifics

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

#if NET7_0_OR_GREATER

namespace ILGPU.Backends.Velocity.Vec128
{
    static partial class Vec128Operations
    {
        #region Low Precision Types

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> DecodeNarrowF16(Vector128<int> warp)
        {
            return Vector128.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> EncodeNarrowF16(Vector128<int> warp)
        {
            return Vector128.Create(VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> DecodeNarrowBF16(Vector128<int> warp)
        {
            return Vector128.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> EncodeNarrowBF16(Vector128<int> warp)
        {
            return Vector128.Create(VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> DecodeNarrowE4M3(Vector128<int> warp)
        {
            return Vector128.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> EncodeNarrowE4M3(Vector128<int> warp)
        {
            return Vector128.Create(VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> DecodeNarrowE5M2(Vector128<int> warp)
        {
            return Vector128.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> EncodeNarrowE5M2(Vector128<int> warp)
        {
            return Vector128.Create(VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> DecodeNarrowE2M1(Vector128<int> warp)
        {
            return Vector128.Create(BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(0))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(1))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(2))), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> EncodeNarrowE2M1(Vector128<int> warp)
        {
            return Vector128.Create(VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(0))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(1))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(2))), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.GetElement(3))));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SignExtendNibble(Vector128<int> warp)
        {
            return Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(warp, 28), 28);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MaskNibble(Vector128<int> warp)
        {
            return warp & Vector128.Create(0xF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> LoadNibble(Vector128<int> mask, (Vector128<long>, Vector128<long>) address)
        {
            int result0 = mask.GetElement(0) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item1.GetElement(0))
                : 0;
            int result1 = mask.GetElement(1) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item1.GetElement(1))
                : 0;
            int result2 = mask.GetElement(2) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item2.GetElement(0))
                : 0;
            int result3 = mask.GetElement(3) != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item2.GetElement(1))
                : 0;
            return Vector128.Create(result0, result1, result2, result3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void StoreNibble(Vector128<int> mask, (Vector128<long>, Vector128<long>) address, Vector128<int> value)
        {
            if (mask.GetElement(0) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item1.GetElement(0), value.GetElement(0));
            if (mask.GetElement(1) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item1.GetElement(1), value.GetElement(1));
            if (mask.GetElement(2) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item2.GetElement(0), value.GetElement(2));
            if (mask.GetElement(3) != 0)
                VelocityNarrowTypes.StoreNibble(address.Item2.GetElement(1), value.GetElement(3));
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
