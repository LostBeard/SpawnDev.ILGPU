// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: Vec512Extensions.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

#if NET7_0_OR_GREATER

namespace ILGPU.Backends.Velocity.Vec512
{
    partial class Vec512Operations
    {
        #region Rcp

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<float> RcpImpl(Vector512<float> value) =>
            Vector512.Create(1.0f) / value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<double> RcpImpl(Vector512<double> value) =>
            Vector512.Create(1.0) / value;

        #endregion

        #region Thread Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int BarrierPopCount32Scalar(
            Vector512<int> mask,
            Vector512<int> warp) =>
            -Vector512.Sum(AndI32(mask, warp));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> BarrierPopCount32(
            Vector512<int> mask,
            Vector512<int> warp) =>
            Vector512.Create(BarrierPopCount32Scalar(mask, warp));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int BarrierPopCount64Scalar(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp)
        {
            var parts = AndI64(Convert32To64I(mask), warp);
            return -(int)(
                Vector512.Sum(parts.Item1) +
                Vector512.Sum(parts.Item2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) BarrierPopCount64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp) =>
            FromScalarI64(BarrierPopCount64Scalar(mask, warp));

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static Vector512<int> BarrierAnd32(
            Vector512<int> mask,
            Vector512<int> warp,
            int groupSize) =>
            BarrierPopCount32Scalar(mask, warp) == groupSize
                ? Vector512<int>.AllBitsSet
                : Vector512<int>.Zero;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static (Vector512<long>, Vector512<long>) BarrierAnd64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp,
            int groupSize) =>
            BarrierPopCount64Scalar(mask, warp) == groupSize
                ? (Vector512<long>.AllBitsSet, Vector512<long>.AllBitsSet)
                : (Vector512<long>.Zero, Vector512<long>.Zero);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> BarrierOr32(
            Vector512<int> mask,
            Vector512<int> warp) =>
            BarrierPopCount32Scalar(mask, warp) != 0
                ? Vector512<int>.AllBitsSet
                : Vector512<int>.Zero;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) BarrierOr64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp) =>
            BarrierPopCount64Scalar(mask, warp) != 0
                ? (Vector512<long>.AllBitsSet, Vector512<long>.AllBitsSet)
                : (Vector512<long>.Zero, Vector512<long>.Zero);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> Broadcast32(
            Vector512<int> _,
            Vector512<int> value,
            Vector512<int> sourceLane)
        {
            // Extract base source lane
            int sourceLaneIndex = sourceLane.GetElement(0);

            // Broadcast without referring to the current mask
            return Broadcast32Internal(value, sourceLaneIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector512<int> Broadcast32Internal(
            Vector512<int> value,
            int sourceLaneIndex) =>
            Vector512.Create(value[sourceLaneIndex]);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Broadcast64(
            Vector512<int> _,
            (Vector512<long>, Vector512<long>) value,
            (Vector512<long>, Vector512<long>) sourceLane) =>
            throw new NotImplementedException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> Shuffle32(
            Vector512<int> _,
            Vector512<int> value,
            Vector512<int> sourceLanes)
        {
            var lanes = MinI32(
                MaxI32(sourceLanes, Vector512<int>.Zero),
                WarpSizeM1Vector);

            int value0 = value.GetElement(lanes.GetElement(0));
            int value1 = value.GetElement(lanes.GetElement(1));
            int value2 = value.GetElement(lanes.GetElement(2));
            int value3 = value.GetElement(lanes.GetElement(3));
            int value4 = value.GetElement(lanes.GetElement(4));
            int value5 = value.GetElement(lanes.GetElement(5));
            int value6 = value.GetElement(lanes.GetElement(6));
            int value7 = value.GetElement(lanes.GetElement(7));
            int value8 = value.GetElement(lanes.GetElement(8));
            int value9 = value.GetElement(lanes.GetElement(9));
            int value10 = value.GetElement(lanes.GetElement(10));
            int value11 = value.GetElement(lanes.GetElement(11));
            int value12 = value.GetElement(lanes.GetElement(12));
            int value13 = value.GetElement(lanes.GetElement(13));
            int value14 = value.GetElement(lanes.GetElement(14));
            int value15 = value.GetElement(lanes.GetElement(15));

            return Vector512.Create(value0, value1, value2, value3, value4, value5, value6, value7, value8, value9, value10, value11, value12, value13, value14, value15);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Shuffle64(
            Vector512<int> _,
            (Vector512<long>, Vector512<long>) value,
            Vector512<int> sourceLanes) =>
            throw new NotImplementedException();

        #endregion
    }
}

#endif
