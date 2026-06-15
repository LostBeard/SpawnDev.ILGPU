// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: BFloat16Conversion.cs
//
// Constants and integer conversion operators for the BFloat16 type. Unlike HalfConversion
// (which needs van der Zijp lookup tables for the fp16 5/10 format), bfloat16 conversion is
// pure bit-shifting (see BFloat16Extensions.Convert* in BFloat16.cs), so no T4 template is
// required. The integer conversions route through float — the bfloat16 range is fp32's range,
// so widening to float first is exact for the conversion's purpose.
// ---------------------------------------------------------------------------------------

using ILGPU.Frontend.Intrinsic;
using ILGPU.IR.Values;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    partial struct BFloat16
    {
        #region Constants

        /// <summary>
        /// Represents the smallest positive <see cref="BFloat16"/> value that is greater
        /// than zero (the smallest positive subnormal).
        /// </summary>
        public static readonly BFloat16 Epsilon = new BFloat16(0x0001);

        /// <summary>
        /// Represents the largest possible <see cref="BFloat16"/> value
        /// (~3.3895e38, exponent 0xFE, full mantissa).
        /// </summary>
        public static readonly BFloat16 MaxValue = new BFloat16(0x7F7F);

        /// <summary>
        /// Represents the smallest possible <see cref="BFloat16"/> value (negative max).
        /// </summary>
        public static readonly BFloat16 MinValue = new BFloat16(0xFF7F);

        /// <summary>
        /// Represents not a number (quiet NaN).
        /// </summary>
        public static readonly BFloat16 NaN = new BFloat16(0x7FC0);

        /// <summary>
        /// Represents positive infinity.
        /// </summary>
        public static readonly BFloat16 PositiveInfinity = new BFloat16(0x7F80);

        /// <summary>
        /// Represents negative infinity.
        /// </summary>
        public static readonly BFloat16 NegativeInfinity = new BFloat16(0xFF80);

        /// <summary>
        /// Represents a positive zero <see cref="BFloat16"/> value.
        /// </summary>
        public static readonly BFloat16 Zero = new BFloat16(0x0000);

        /// <summary>
        /// Represents the value one (1.0). bfloat16 shares fp32's exponent bias of 127,
        /// so 2^0 with a zero mantissa is the raw value 0x3F80.
        /// </summary>
        public static readonly BFloat16 One = new BFloat16(0x3F80);

        #endregion

        #region Integer Operators

        /// <summary>Explicitly converts a bfloat16 to a signed byte.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator sbyte(BFloat16 value) => (sbyte)(float)value;

        /// <summary>Explicitly converts a signed byte to a bfloat16.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(sbyte value) => (BFloat16)(float)value;

        /// <summary>Explicitly converts a bfloat16 to a byte.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc(ConvertFlags.TargetUnsigned)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator byte(BFloat16 value) => (byte)(float)value;

        /// <summary>Explicitly converts a byte to a bfloat16.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc(ConvertFlags.SourceUnsigned)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(byte value) => (BFloat16)(float)value;

        /// <summary>Explicitly converts a bfloat16 to a short.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator short(BFloat16 value) => (short)(float)value;

        /// <summary>Explicitly converts a short to a bfloat16.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(short value) => (BFloat16)(float)value;

        /// <summary>Explicitly converts a bfloat16 to an unsigned short.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc(ConvertFlags.TargetUnsigned)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator ushort(BFloat16 value) => (ushort)(float)value;

        /// <summary>Explicitly converts an unsigned short to a bfloat16.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc(ConvertFlags.SourceUnsigned)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(ushort value) => (BFloat16)(float)value;

        /// <summary>Explicitly converts a bfloat16 to an int.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator int(BFloat16 value) => (int)(float)value;

        /// <summary>Explicitly converts an int to a bfloat16.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(int value) => (BFloat16)(float)value;

        /// <summary>Explicitly converts a bfloat16 to an unsigned int.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc(ConvertFlags.TargetUnsigned)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator uint(BFloat16 value) => (uint)(float)value;

        /// <summary>Explicitly converts an unsigned int to a bfloat16.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc(ConvertFlags.SourceUnsigned)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(uint value) => (BFloat16)(float)value;

        /// <summary>Explicitly converts a bfloat16 to a long.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator long(BFloat16 value) => (long)(float)value;

        /// <summary>Explicitly converts a long to a bfloat16.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(long value) => (BFloat16)(float)value;

        /// <summary>Explicitly converts a bfloat16 to an unsigned long.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc(ConvertFlags.TargetUnsigned)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator ulong(BFloat16 value) => (ulong)(float)value;

        /// <summary>Explicitly converts an unsigned long to a bfloat16.</summary>
        /// <param name="value">The value to convert.</param>
        [ConvertIntrinisc(ConvertFlags.SourceUnsigned)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(ulong value) => (BFloat16)(float)value;

        #endregion
    }
}
