// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RadixSortOperations.Float4E2M1.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0004 // Cast is redundant

namespace ILGPU.Algorithms.RadixSortOperations
{
    // Float4E2M1 (E2M1FN, the NVFP4/MXFP4 element format) radix-sort operations. Hand-written
    // (not emitted by RadixSortOperations.tt) for the same reason as BFloat16/FP8: keeping FP4
    // out of the NumericTypes loop avoids cascading FP4 variants through every .tt. E2M1FN is a
    // 4-bit float (1 sign / 2 exponent / 1 mantissa) with the sign at bit 3 and the exponent above
    // the mantissa, so the magnitude is monotonic in the bit pattern for every finite value
    // (16 finite codes, NO Inf, NO NaN). The same sign-flip + ones-complement key transform that
    // Half / bf16 / FP8 use therefore applies, scaled DOWN to 4 bits: the value lives in the low
    // nibble of the 1-byte storage (FloatAsInt(Float4E2M1) returns it masked to 0..15), the sign is
    // bit 3, and the ones-complement mask spans the low 3 magnitude bits.

    /// <summary>
    /// Represents an ascending radix sort operation of type Float4E2M1.
    /// </summary>
    public readonly struct AscendingFloat4E2M1 :
        IRadixSortOperation<Float4E2M1>
    {
        /// <summary>
        /// Returns the number of bits to sort. FP4 is stored as a 1-byte element (the 4-bit value in
        /// the low nibble, high nibble always 0), so we sort the full BYTE (8 bits) - the same proven
        /// 1-byte radix path the FP8 types use. The transform below produces a key in 0..15, so the
        /// upper 4 radix passes are trivially zero and the lower 4 do the real ordering; the
        /// high-nibble-zero key stays monotonic over all 8 bits.
        /// </summary>
        public int NumBits => sizeof(byte) * 8;

        /// <summary>
        /// The default element value.
        /// </summary>
        public Float4E2M1 DefaultValue => Float4E2M1.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(Float4E2M1 value, int shift, int bitMask)
        {
            // Same sign-flip + ones-complement transform as AscendingFloat8E4M3, but the FP4 value
            // lives in the LOW NIBBLE (sign at bit 3, NOT bit 7), so the masks are hardcoded to the
            // nibble (independent of NumBits): flip the sign-bit (bit 3, 0x8) so negatives order
            // before positives, and ones-complement the exponent+mantissa of negatives (the low 3
            // magnitude bits, 0x7) so larger negatives order before smaller negatives. The produced
            // key is in 0..15, so sorting it over the full 8-bit byte (NumBits) is monotonic - the
            // high nibble is always 0.
            var raw = Interop.FloatAsInt(value) & 0xFu;       // 4-bit pattern (low nibble)
            const uint signMask = 0x8U;                        // FP4 sign bit (bit 3)
            var sign = (raw >> 3) & 1U;
            var onesComplementMask = (0U - sign) & 0x7U;       // 0x7 if negative, else 0
            var bits = raw ^ (signMask | onesComplementMask);
            return (int)(bits >> shift) & bitMask;
        }
    }

    /// <summary>
    /// Represents a descending radix sort operation of type Float4E2M1.
    /// </summary>
    public readonly struct DescendingFloat4E2M1 :
        IRadixSortOperation<Float4E2M1>
    {
        /// <summary>
        /// Returns the number of bits to sort (8 - the full 1-byte FP4 storage element; see
        /// <see cref="AscendingFloat4E2M1.NumBits"/>).
        /// </summary>
        public int NumBits => sizeof(byte) * 8;

        /// <summary>
        /// The default element value.
        /// </summary>
        public Float4E2M1 DefaultValue => Float4E2M1.Zero;

        /// <summary>
        /// Converts the given value to a radix-sort compatible value.
        /// </summary>
        /// <param name="value">The value to map.</param>
        /// <param name="shift">The shift amount in bits.</param>
        /// <param name="bitMask">The lower bit mask bit use.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ExtractRadixBits(Float4E2M1 value, int shift, int bitMask)
        {
            AscendingFloat4E2M1 operation = default;
            return (~operation.ExtractRadixBits(value, shift, bitMask)) & bitMask;
        }
    }
}
