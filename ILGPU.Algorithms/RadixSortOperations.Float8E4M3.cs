// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RadixSortOperations.Float8E4M3.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0004 // Cast is redundant

namespace ILGPU.Algorithms.RadixSortOperations
{
    // Float8E4M3 (E4M3FN) radix-sort operations. Hand-written (not emitted by
    // RadixSortOperations.tt) for the same reason as BFloat16: keeping FP8 out of the
    // NumericTypes loop avoids cascading FP8 variants through every .tt in the project.
    // E4M3FN is an 8-bit IEEE-style float (1 sign / 4 exponent / 3 mantissa) with the
    // sign at bit 7 and the exponent above the mantissa, so the magnitude is monotonic in
    // the bit pattern for every finite value (max finite = 0x7E = 448; 0x7F/0xFF = NaN,
    // which sorts at the extremes exactly as it does for Half/bf16). The same sign-flip +
    // ones-complement key transform that Half and BFloat16 use therefore applies, scaled
    // from 16 to 8 bits.

    /// <summary>
    /// Represents an ascending radix sort operation of type Float8E4M3.
    /// </summary>
    public readonly struct AscendingFloat8E4M3 :
        IRadixSortOperation<Float8E4M3>
    {
        /// <summary>
        /// Returns the number of bits to sort.
        /// </summary>
        public int NumBits => sizeof(byte) * 8;

        /// <summary>
        /// The default element value.
        /// </summary>
        public Float8E4M3 DefaultValue => Float8E4M3.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(Float8E4M3 value, int shift, int bitMask)
        {
            // Identical transform to AscendingBFloat16 at 8-bit width: flip the sign-bit so
            // negatives order before positives, and ones-complement the exponent+mantissa of
            // negatives (mask built by sign-extending the sign-bit across the low 8 bits) so
            // larger negatives order before smaller negatives.
            var signMask = 1U << (NumBits - 1);
            var onesComplementMask =
                ((uint)((sbyte)(Interop.FloatAsInt(value)) >> (NumBits - 1))) & 0xFFu;
            var bits = Interop.FloatAsInt(value) ^ (signMask | onesComplementMask);
            return (int)(bits >> shift) & bitMask;
        }
    }

    /// <summary>
    /// Represents a descending radix sort operation of type Float8E4M3.
    /// </summary>
    public readonly struct DescendingFloat8E4M3 :
        IRadixSortOperation<Float8E4M3>
    {
        /// <summary>
        /// Returns the number of bits to sort.
        /// </summary>
        public int NumBits => sizeof(byte) * 8;

        /// <summary>
        /// The default element value.
        /// </summary>
        public Float8E4M3 DefaultValue => Float8E4M3.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(Float8E4M3 value, int shift, int bitMask)
        {
            AscendingFloat8E4M3 operation = default;
            return (~operation.ExtractRadixBits(value, shift, bitMask)) & bitMask;
        }
    }
}
