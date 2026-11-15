// ---------------------------------------------------------------------------------------
//                                    ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: LowPrecisionRounding.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    /// <summary>
    /// Rounding helpers shared by the low-precision types (Half, BFloat16, FP8, FP4).
    /// </summary>
    internal static class LowPrecisionRounding
    {
        /// <summary>
        /// Demotes a double to a float with round-to-odd: the float is the double truncated toward zero, with its last
        /// bit set when that dropped anything. Rounding this float to any type with at most 22 significand bits
        /// (Half 11, BFloat16 8, FP8 4 / 3, FP4 2) is the same as rounding the double once, as System.Half does from a
        /// double. A plain (float)d rounds twice: a double just above a midpoint of the narrow type becomes that exact
        /// midpoint and then ties to even, the wrong way (LowPrecisionFromDouble_RoundsOnce).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DemoteRoundToOdd(double value)
        {
            float f = (float)value;
            if (double.IsNaN(value) || (double)f == value)
                return f;
            int bits = BitConverter.SingleToInt32Bits(f);
            // f rounded away from zero (|f| > |value|, including overflow to infinity): one step toward zero
            if (Math.Abs((double)f) > Math.Abs(value))
                bits--;
            return BitConverter.Int32BitsToSingle(bits | 1);
        }
    }
}
