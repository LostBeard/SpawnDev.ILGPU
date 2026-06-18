// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: QUInt4.cs
//
// An unsigned 4-bit integer (range 0..15), packed 2 per byte in device buffers ([PackedBits(4)]).
// The value lives in the low nibble of a 1-byte CLR struct in host memory; an ArrayView<QUInt4> of
// N elements allocates ceil(N/2) device bytes. Conversion to int/uint ZERO-EXTENDS the low nibble.
//
// Per-element STORAGE/CONVERT only - like QInt4/Int8 it is NOT exposed as INumber: load it,
// zero-extend to int / widen to float to compute, and store. The companion of QInt4.
// ---------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using ILGPU.Frontend.Intrinsic;

namespace ILGPU
{
    /// <summary>
    /// An unsigned 4-bit integer (0..15). Packed 2 per byte in device buffers; the value is the
    /// low nibble of a 1-byte host struct. The unsigned companion to <see cref="QInt4"/>.
    /// </summary>
    [Serializable]
    [PackedBits(4)]
    public readonly partial struct QUInt4 :
        IEquatable<QUInt4>, IComparable<QUInt4>
    {
        #region Constants

        /// <summary>The value zero (0x0).</summary>
        public static readonly QUInt4 Zero = new QUInt4(0);

        /// <summary>The value one (0x1).</summary>
        public static readonly QUInt4 One = new QUInt4(1);

        /// <summary>The smallest value (0).</summary>
        public static readonly QUInt4 MinValue = new QUInt4(0);

        /// <summary>The largest value (15, 0xF).</summary>
        public static readonly QUInt4 MaxValue = new QUInt4(0xF);

        #endregion

        #region Instance

        /// <summary>Constructs a new QUInt4 from a raw nibble (low 4 bits of the byte are kept).</summary>
        internal QUInt4(byte rawValue)
        {
            RawValue = (byte)(rawValue & 0x0F);
        }

        #endregion

        #region Properties

        /// <summary>The raw 4-bit value (stored in the low nibble of a byte).</summary>
        internal byte RawValue { get; }

        #endregion

        #region IEquatable / IComparable / Object

        /// <summary>Returns true if the given QUInt4 equals this value.</summary>
        public readonly bool Equals(QUInt4 other) => RawValue == other.RawValue;

        /// <summary>Compares this QUInt4 to another by unsigned value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int CompareTo(QUInt4 other) => RawValue.CompareTo(other.RawValue);

        /// <summary>Returns true if the given object equals this value.</summary>
        public readonly override bool Equals(object? obj) => obj is QUInt4 value && Equals(value);

        /// <summary>Returns the hash code of this value.</summary>
        public readonly override int GetHashCode() => RawValue;

        /// <summary>Returns the string representation (the unsigned integer value).</summary>
        public readonly override string ToString() => RawValue.ToString();

        #endregion

        #region Operators

        /// <summary>Returns true if the two values are equal.</summary>
        public static bool operator ==(QUInt4 first, QUInt4 second) => first.RawValue == second.RawValue;

        /// <summary>Returns true if the two values are not equal.</summary>
        public static bool operator !=(QUInt4 first, QUInt4 second) => first.RawValue != second.RawValue;

        /// <summary>Zero-extends the 4-bit value to a 32-bit int (0..15).</summary>
        // [ConvertIntrinisc]: in a kernel this is a ConvertValue node (identity - the QUInt4 value is
        // already zero-extended in an i32 register by the packed nibble LOAD), not an inlined body.
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(QUInt4 value) => value.RawValue & 0x0F;

        /// <summary>Zero-extends the 4-bit value to a 32-bit uint (0..15).</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator uint(QUInt4 value) => (uint)(value.RawValue & 0x0F);

        /// <summary>Converts a 32-bit int to QUInt4 (keeps the low 4 bits; 0..15 round-trip exactly).</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator QUInt4(int value) => new QUInt4((byte)(value & 0x0F));

        /// <summary>Widens the 4-bit value to float (0..15).</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator float(QUInt4 value) => value.RawValue & 0x0F;

        /// <summary>Converts a float to QUInt4 (truncates toward zero, keeps low 4 bits).</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator QUInt4(float value) => (QUInt4)(int)value;

        #endregion
    }
}
