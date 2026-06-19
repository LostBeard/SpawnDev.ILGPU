// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RadixSortOperations.QInt4.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0004 // Cast is redundant

namespace ILGPU.Algorithms.RadixSortOperations
{
    // QInt4 (signed packed 4-bit integer, -8..7) radix-sort operations. Hand-written (not emitted by
    // RadixSortOperations.tt) for the same reason as BFloat16/FP8/FP4: keeping QInt4 out of the
    // NumericTypes loop avoids cascading variants through every .tt. Unlike the low-precision FLOATS,
    // a signed integer sorts ascending as unsigned by flipping ONLY the sign bit (two's complement is
    // already magnitude-monotonic within each sign) - there is no ones-complement step. The 4-bit value
    // lives in the low nibble; (int)value sign-extends, so masking & 0xF recovers the raw 4-bit pattern.

    /// <summary>
    /// Represents an ascending radix sort operation of type <see cref="QInt4"/>.
    /// </summary>
    public readonly struct AscendingQInt4 : IRadixSortOperation<QInt4>
    {
        /// <summary>
        /// Returns the number of bits to sort. QInt4 is a 4-bit value; the key transform below
        /// produces a 0..15 key, so 4 radix passes fully order it.
        /// </summary>
        public int NumBits => 4;

        /// <summary>
        /// The default element value.
        /// </summary>
        public QInt4 DefaultValue => QInt4.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(QInt4 value, int shift, int bitMask)
        {
            // (int)value sign-extends the nibble; & 0xF recovers the raw 4-bit two's-complement pattern
            // (the sign bits above bit 3 are masked off). Flip the sign bit (bit 3, 0x8) so negatives
            // order before positives: -8(0x8)->0x0, -1(0xF)->0x7, 0(0x0)->0x8, 7(0x7)->0xF -> monotonic
            // 0..15. NO ones-complement (integer two's complement is already magnitude-ordered per sign,
            // unlike the float magnitude+mantissa layout the Half/bf16/FP8/FP4 ops invert).
            var raw = (uint)((int)value & 0xF);
            var key = raw ^ 0x8U;
            return (int)(key >> shift) & bitMask;
        }
    }

    /// <summary>
    /// Represents a descending radix sort operation of type <see cref="QInt4"/>.
    /// </summary>
    public readonly struct DescendingQInt4 : IRadixSortOperation<QInt4>
    {
        /// <summary>
        /// Returns the number of bits to sort (see <see cref="AscendingQInt4.NumBits"/>).
        /// </summary>
        public int NumBits => 4;

        /// <summary>
        /// The default element value.
        /// </summary>
        public QInt4 DefaultValue => QInt4.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(QInt4 value, int shift, int bitMask)
        {
            AscendingQInt4 operation = default;
            return (~operation.ExtractRadixBits(value, shift, bitMask)) & bitMask;
        }
    }
}
