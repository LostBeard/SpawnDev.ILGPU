// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RadixSortOperations.QUInt4.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0004 // Cast is redundant

namespace ILGPU.Algorithms.RadixSortOperations
{
    // QUInt4 (UNSIGNED packed 4-bit integer, 0..15) radix-sort operations. The unsigned companion to
    // AscendingQInt4/DescendingQInt4 - and simpler: an unsigned 4-bit value is ALREADY magnitude-
    // monotonic, so there is NO sign-bit flip (and no ones-complement). (int)value zero-extends the
    // nibble; & 0xF recovers the raw 4-bit pattern (a no-op for the in-range 0..15, but it pins the
    // key even if a backend's packed load were to sign-extend, since the radix only sorts the nibble).

    /// <summary>
    /// Represents an ascending radix sort operation of type <see cref="QUInt4"/>.
    /// </summary>
    public readonly struct AscendingQUInt4 : IRadixSortOperation<QUInt4>
    {
        /// <summary>
        /// Returns the number of bits to sort. QUInt4 is a 4-bit value (0..15), so 4 radix passes
        /// fully order it.
        /// </summary>
        public int NumBits => 4;

        /// <summary>
        /// The default element value.
        /// </summary>
        public QUInt4 DefaultValue => QUInt4.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(QUInt4 value, int shift, int bitMask)
        {
            // (int)value zero-extends the nibble; & 0xF recovers the raw 4-bit pattern (0..15). The
            // unsigned value is already monotonic - NO sign-bit flip (unlike QInt4), NO ones-complement.
            var key = (uint)((int)value & 0xF);
            return (int)(key >> shift) & bitMask;
        }
    }

    /// <summary>
    /// Represents a descending radix sort operation of type <see cref="QUInt4"/>.
    /// </summary>
    public readonly struct DescendingQUInt4 : IRadixSortOperation<QUInt4>
    {
        /// <summary>
        /// Returns the number of bits to sort (see <see cref="AscendingQUInt4.NumBits"/>).
        /// </summary>
        public int NumBits => 4;

        /// <summary>
        /// The default element value.
        /// </summary>
        public QUInt4 DefaultValue => QUInt4.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(QUInt4 value, int shift, int bitMask)
        {
            AscendingQUInt4 operation = default;
            return (~operation.ExtractRadixBits(value, shift, bitMask)) & bitMask;
        }
    }
}
