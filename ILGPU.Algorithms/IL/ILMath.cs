// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: ILMath.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;

namespace ILGPU.Algorithms.IL
{
    /// <summary>
    /// Implementations of the <see cref="XMath"/> functions that are intrinsic
    /// implementations for the Velocity (SIMD CPU) backend, which compiles kernels like
    /// the GPU backends do. Their managed bodies call System.Math functions that are
    /// remapped back to <see cref="XMath"/>, so a backend without its own
    /// implementation runs in a cycle (the Velocity kernels returned zero from
    /// MathF.Round).
    /// </summary>
    static class ILMath
    {
        /// <summary cref="XMath.RoundAwayFromZero(double)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double RoundAwayFromZero(double value) =>
            XMath.RoundingModes.RoundAwayFromZero(value);

        /// <summary cref="XMath.RoundAwayFromZero(float)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RoundAwayFromZero(float value) =>
            XMath.RoundingModes.RoundAwayFromZero(value);

        /// <summary cref="XMath.RoundToEven(double)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double RoundToEven(double value) =>
            XMath.RoundingModes.RoundToEven(value);

        /// <summary cref="XMath.RoundToEven(float)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RoundToEven(float value) =>
            XMath.RoundingModes.RoundToEven(value);

        /// <summary cref="XMath.IEEERemainder(double, double)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double IEEERemainder(double x, double y)
        {
            if (y == 0.0 ||
                XMath.IsInfinity(x) ||
                XMath.IsNaN(x) ||
                XMath.IsNaN(y))
                return double.NaN;

            if (XMath.IsInfinity(y))
                return x;

            return x - (y * XMath.RoundToEven(x * XMath.Rcp(y)));
        }

        /// <summary cref="XMath.IEEERemainder(float, float)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float IEEERemainder(float x, float y)
        {
            if (y == 0.0f ||
                XMath.IsInfinity(x) ||
                XMath.IsNaN(x) ||
                XMath.IsNaN(y))
                return float.NaN;

            if (XMath.IsInfinity(y))
                return x;

            return x - (y * XMath.RoundToEven(x * XMath.Rcp(y)));
        }
    }
}
