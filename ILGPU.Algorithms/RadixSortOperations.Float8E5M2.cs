// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RadixSortOperations.Float8E5M2.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0004 // Cast is redundant

namespace ILGPU.Algorithms.RadixSortOperations
{
    // Float8E5M2 radix-sort operations. Hand-written (not emitted by RadixSortOperations.tt)
    // for the same reason as BFloat16 / Float8E4M3. E5M2 is an 8-bit IEEE-style float
    // (1 sign / 5 exponent / 2 mantissa) with IEEE Inf/NaN, sign at bit 7, exponent above
    // the mantissa - so the magnitude is monotonic in the bit pattern and the exact same
    // sign-flip + ones-complement key transform that Half and BFloat16 use applies, scaled
    // from 16 to 8 bits.

    /// <summary>
    /// Represents an ascending radix sort operation of type Float8E5M2.
    /// </summary>
    public readonly struct AscendingFloat8E5M2 :
        IRadixSortOperation<Float8E5M2>
    {
        /// <summary>
        /// Returns the number of bits to sort.
        /// </summary>
        public int NumBits => sizeof(byte) * 8;

        /// <summary>
        /// The default element value.
        /// </summary>
        public Float8E5M2 DefaultValue => Float8E5M2.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(Float8E5M2 value, int shift, int bitMask)
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
    /// Represents a descending radix sort operation of type Float8E5M2.
    /// </summary>
    public readonly struct DescendingFloat8E5M2 :
        IRadixSortOperation<Float8E5M2>
    {
        /// <summary>
        /// Returns the number of bits to sort.
        /// </summary>
        public int NumBits => sizeof(byte) * 8;

        /// <summary>
        /// The default element value.
        /// </summary>
        public Float8E5M2 DefaultValue => Float8E5M2.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(Float8E5M2 value, int shift, int bitMask)
        {
            AscendingFloat8E5M2 operation = default;
            return (~operation.ExtractRadixBits(value, shift, bitMask)) & bitMask;
        }
    }
}
