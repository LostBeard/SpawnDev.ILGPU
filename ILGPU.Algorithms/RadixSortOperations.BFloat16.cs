// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RadixSortOperations.BFloat16.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0004 // Cast is redundant

namespace ILGPU.Algorithms.RadixSortOperations
{
    // bfloat16 radix-sort operations. Hand-written (not emitted by RadixSortOperations.tt)
    // to avoid adding BFloat16 to the NumericTypes loop, which would cascade bf16 variants
    // through every .tt in the project. bf16 is structurally identical to Half for sorting:
    // a 16-bit IEEE-like float (1 sign / 8 exponent / 7 mantissa), sign at bit 15, magnitude
    // monotonic - so the exact same sign-flip + ones-complement key transform applies.

    /// <summary>
    /// Represents an ascending radix sort operation of type BFloat16.
    /// </summary>
    public readonly struct AscendingBFloat16 :
        IRadixSortOperation<BFloat16>
    {
        /// <summary>
        /// Returns the number of bits to sort.
        /// </summary>
        public int NumBits => sizeof(ushort) * 8;

        /// <summary>
        /// The default element value.
        /// </summary>
        public BFloat16 DefaultValue => BFloat16.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(BFloat16 value, int shift, int bitMask)
        {
            // Identical transform to AscendingHalf: flip the sign-bit so negatives order
            // before positives, and ones-complement the exponent+mantissa of negatives
            // (mask built by sign-extending the sign-bit) so larger negatives order before
            // smaller negatives.
            var signMask = 1U << (NumBits - 1);
            var onesComplementMask =
                ((uint)((short)(Interop.FloatAsInt(value)) >> (NumBits - 1))) & 0xFFFFu;
            var bits = Interop.FloatAsInt(value) ^ (signMask | onesComplementMask);
            return (int)(bits >> shift) & bitMask;
        }
    }

    /// <summary>
    /// Represents a descending radix sort operation of type BFloat16.
    /// </summary>
    public readonly struct DescendingBFloat16 :
        IRadixSortOperation<BFloat16>
    {
        /// <summary>
        /// Returns the number of bits to sort.
        /// </summary>
        public int NumBits => sizeof(ushort) * 8;

        /// <summary>
        /// The default element value.
        /// </summary>
        public BFloat16 DefaultValue => BFloat16.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(BFloat16 value, int shift, int bitMask)
        {
            AscendingBFloat16 operation = default;
            return (~operation.ExtractRadixBits(value, shift, bitMask)) & bitMask;
        }
    }
}
