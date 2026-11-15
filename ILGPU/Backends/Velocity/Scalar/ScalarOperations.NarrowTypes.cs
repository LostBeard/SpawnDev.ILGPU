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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) RoundToOddAtFloatPrecision64((long, long) warp)
        {
            return (RoundToOddAtFloatPrecision(warp.Item1), RoundToOddAtFloatPrecision(warp.Item2));
        }

        /// <summary>
        /// The double (bits) truncated toward zero to float precision, its last float bit set when that dropped
        /// anything (see the specializer). Float precision is 24 bits down to 2^-126 (the 29 low bits cleared, bit
        /// 29 the sticky one) and the fixed 2^-149 below it: there y = |x| + 2^-97 rounds |x| to a multiple of
        /// 2^-149 (y's ulp), a step back when that rounded up, the last bit set when inexact, y - 2^-97 exact.
        /// With bit 29 alone, a bf16 subnormal result rounded twice (10 of 4,096 midpoints; 2026-11-15).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long RoundToOddAtFloatPrecision(long bits)
        {
            if ((bits & long.MaxValue) >= FloatMinNormalBits)
                return (bits & ~0x1FFFFFFFL) | ((bits & 0x1FFFFFFFL) != 0 ? 0x20000000L : 0L);
            double c = BitConverter.Int64BitsToDouble(FloatSubnormalOffsetBits);
            double mag = BitConverter.Int64BitsToDouble(bits & long.MaxValue);
            double y = mag + c, r = y - c;
            long yb = BitConverter.DoubleToInt64Bits(y);
            if (r != mag)
                yb = (r > mag ? yb - 1 : yb) | 1L;
            return BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(yb) - c) | (bits & long.MinValue);
        }

        /// <summary>2^-126 as double bits: below it float precision is the fixed 2^-149.</summary>
        internal const long FloatMinNormalBits = 0x3810000000000000L;
        /// <summary>2^-97 as double bits: a double in [2^-97, 2^-96) has an ulp of 2^-149.</summary>
        internal const long FloatSubnormalOffsetBits = 0x39E0000000000000L;

        public static readonly MethodInfo RoundToOddAtFloatPrecision64Method =
            GetMethod(nameof(RoundToOddAtFloatPrecision64));
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
