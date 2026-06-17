// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: Int4.cs
//
// A signed 4-bit integer (two's complement, range -8..7), packed 2 per byte in device buffers
// ([PackedBits(4)]). The value lives in the low nibble of a 1-byte CLR struct in host memory;
// an ArrayView<Int4> of N elements allocates ceil(N/2) device bytes. Conversion to int
// SIGN-EXTENDS the 4-bit value (bit 3 = sign); conversion from int keeps the low 4 bits.
//
// Per-element STORAGE/CONVERT only - INT4 is a quantized storage type, not an in-kernel arithmetic
// type (like Int8/Int16 it is NOT exposed as INumber): you load it, sign-extend to int / widen to
// float to compute, and store. The 4-bit MEMORY SAVING is the packed buffer (this type), and the
// nibble load/store + radix keys + capability are wired alongside it.
// ---------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    /// <summary>
    /// A signed 4-bit integer (two's complement, -8..7). Packed 2 per byte in device buffers; the
    /// value is the low nibble of a 1-byte host struct. The NVFP4/MXFP4-era companion to
    /// <see cref="UInt4"/>; INT4 quantization's element type.
    /// </summary>
    [Serializable]
    [PackedBits(4)]
    public readonly partial struct Int4 :
        IEquatable<Int4>, IComparable<Int4>
    {
        #region Constants

        /// <summary>The value zero (0x0).</summary>
        public static readonly Int4 Zero = new Int4(0);

        /// <summary>The value one (0x1).</summary>
        public static readonly Int4 One = new Int4(1);

        /// <summary>The largest value (+7, 0x7).</summary>
        public static readonly Int4 MaxValue = new Int4(0x7);

        /// <summary>The smallest value (-8, 0x8 in 4-bit two's complement).</summary>
        public static readonly Int4 MinValue = new Int4(unchecked((byte)0x8));

        #endregion

        #region Instance

        /// <summary>Constructs a new Int4 from a raw nibble (low 4 bits of the byte are kept).</summary>
        internal Int4(byte rawValue)
        {
            RawValue = (byte)(rawValue & 0x0F);
        }

        #endregion

        #region Properties

        /// <summary>The raw 4-bit value (two's complement, stored in the low nibble of a byte).</summary>
        internal byte RawValue { get; }

        #endregion

        #region IEquatable / IComparable / Object

        /// <summary>Returns true if the given Int4 equals this value.</summary>
        public readonly bool Equals(Int4 other) => RawValue == other.RawValue;

        /// <summary>Compares this Int4 to another by signed value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int CompareTo(Int4 other) => ((int)this).CompareTo(other);

        /// <summary>Returns true if the given object equals this value.</summary>
        public readonly override bool Equals(object? obj) => obj is Int4 value && Equals(value);

        /// <summary>Returns the hash code of this value.</summary>
        public readonly override int GetHashCode() => RawValue;

        /// <summary>Returns the string representation (the signed integer value).</summary>
        public readonly override string ToString() => ((int)this).ToString();

        #endregion

        #region Operators

        /// <summary>Returns true if the two values are equal.</summary>
        public static bool operator ==(Int4 first, Int4 second) => first.RawValue == second.RawValue;

        /// <summary>Returns true if the two values are not equal.</summary>
        public static bool operator !=(Int4 first, Int4 second) => first.RawValue != second.RawValue;

        /// <summary>Sign-extends the 4-bit value to a 32-bit int (bit 3 = sign; -8..7).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(Int4 value) =>
            // (x ^ 0x8) - 0x8 sign-extends a 4-bit two's-complement value: 0..7 stay, 8..15 -> -8..-1.
            ((value.RawValue & 0x0F) ^ 0x8) - 0x8;

        /// <summary>Converts a 32-bit int to Int4 (keeps the low 4 bits; -8..7 round-trip exactly).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Int4(int value) => new Int4((byte)(value & 0x0F));

        /// <summary>Widens the 4-bit value to float (via the sign-extended int).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator float(Int4 value) => (int)value;

        /// <summary>Converts a float to Int4 (truncates toward zero, keeps low 4 bits).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Int4(float value) => (Int4)(int)value;

        #endregion
    }
}
