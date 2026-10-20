// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: ScalarOperations.NarrowTypes.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// Operations of the low precision types (see VelocityNarrowTypes): hand written, not
// generated from the T4 template, to keep the template free of the type specifics

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ILGPU.Backends.Velocity.Scalar
{
    static partial class ScalarOperations2
    {
        #region Low Precision Types

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) DecodeNarrowF16((int, int) warp)
        {
            return (BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.Item1)), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeF16(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) EncodeNarrowF16((int, int) warp)
        {
            return (VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.Item1)), VelocityNarrowTypes.EncodeF16(BitConverter.Int32BitsToSingle(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) DecodeNarrowBF16((int, int) warp)
        {
            return (BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.Item1)), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeBF16(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) EncodeNarrowBF16((int, int) warp)
        {
            return (VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.Item1)), VelocityNarrowTypes.EncodeBF16(BitConverter.Int32BitsToSingle(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) DecodeNarrowE4M3((int, int) warp)
        {
            return (BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.Item1)), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE4M3(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) EncodeNarrowE4M3((int, int) warp)
        {
            return (VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.Item1)), VelocityNarrowTypes.EncodeE4M3(BitConverter.Int32BitsToSingle(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) DecodeNarrowE5M2((int, int) warp)
        {
            return (BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.Item1)), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE5M2(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) EncodeNarrowE5M2((int, int) warp)
        {
            return (VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.Item1)), VelocityNarrowTypes.EncodeE5M2(BitConverter.Int32BitsToSingle(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) DecodeNarrowE2M1((int, int) warp)
        {
            return (BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.Item1)), BitConverter.SingleToInt32Bits(VelocityNarrowTypes.DecodeE2M1(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) EncodeNarrowE2M1((int, int) warp)
        {
            return (VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.Item1)), VelocityNarrowTypes.EncodeE2M1(BitConverter.Int32BitsToSingle(warp.Item2)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SignExtendNibble((int, int) warp)
        {
            return ((warp.Item1 << 28) >> 28, (warp.Item2 << 28) >> 28);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MaskNibble((int, int) warp)
        {
            return (warp.Item1 & 0xF, warp.Item2 & 0xF);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) LoadNibble((int, int) mask, (long, long) address)
        {
            int result0 = mask.Item1 != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item1)
                : 0;
            int result1 = mask.Item2 != 0
                ? VelocityNarrowTypes.LoadNibble(address.Item2)
                : 0;
            return (result0, result1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void StoreNibble((int, int) mask, (long, long) address, (int, int) value)
        {
            if (mask.Item1 != 0)
                VelocityNarrowTypes.StoreNibble(address.Item1, value.Item1);
            if (mask.Item2 != 0)
                VelocityNarrowTypes.StoreNibble(address.Item2, value.Item2);
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
