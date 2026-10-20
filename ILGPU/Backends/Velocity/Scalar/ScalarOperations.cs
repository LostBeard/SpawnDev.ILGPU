// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2023-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: ScalarOperations.tt/ScalarOperations.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityOperations.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: TypeInformation.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Values;
using ILGPU.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

// ReSharper disable ArrangeMethodOrOperatorBody
// ReSharper disable RedundantCast
// disable: max_line_length

namespace ILGPU.Backends.Velocity.Scalar
{
    static partial class ScalarOperations2
    {
        #region Warp Types

        public const int WarpSize = 2;
        public static readonly Type WarpType32 = typeof((int, int));
        public static readonly Type WarpType64 = typeof((long, long));

        #endregion

        #region Initialization

        static ScalarOperations2()
        {
            InitUnaryOperations();
            InitBinaryOperations();
            InitTernaryOperations();
            InitializeCompareOperations();
            InitializeConvertOperations();
            InitializeVectorConvertOperations();
            InitializeAtomicOperations();
        }

        internal static MethodInfo GetMethod(string name) =>
            typeof(ScalarOperations2).GetMethod(
                    name,
                    BindingFlags.NonPublic | BindingFlags.Static)
                .AsNotNull();

        #endregion

        #region Creation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (TTarget, TTarget) CastWarp<T, TTarget>((T, T) source)
            where T : struct =>
            Unsafe.As<(T, T), (TTarget, TTarget)>(ref source);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CheckForAnyActiveLane((int, int) warp)
        {
            bool result = false;
            result |= warp.Item1 != 0;
            result |= warp.Item2 != 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CheckForNoActiveLane((int, int) warp) =>
            !CheckForAnyActiveLane(warp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CheckForEqualMasks(
            (int, int) firstMask,
            (int, int) secondMask)
        {
            bool result = true;
            result &= firstMask.Item1 != 0 & secondMask.Item1 != 0;
            result &= firstMask.Item2 != 0 & secondMask.Item2 != 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetNumberOfActiveLanes((int, int) warp)
        {
            int result = 0;
            result += warp.Item1 != 0 ? 1 : 0;
            result += warp.Item2 != 0 ? 1 : 0;
            return result;
        }

        public static readonly MethodInfo CheckForAnyActiveLaneMethod =
            GetMethod(nameof(CheckForAnyActiveLane));
        public static readonly MethodInfo CheckForNoActiveLaneMethod =
            GetMethod(nameof(CheckForNoActiveLane));
        public static readonly MethodInfo CheckForEqualMasksMethod =
            GetMethod(nameof(CheckForEqualMasks));
        public static readonly MethodInfo GetNumberOfActiveLanesMethod =
            GetMethod(nameof(GetNumberOfActiveLanes));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) LoadLaneIndexVector32()
        {
            Unsafe.SkipInit(out (int, int) result);
            result.Item1 = 0;
            result.Item2 = 1;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) LoadLaneIndexVector64()
        {
            Unsafe.SkipInit(out (long, long) result);
            result.Item1 = 0;
            result.Item2 = 1;
            return result;
        }

        public static readonly MethodInfo LoadLaneIndexVector32Method =
            GetMethod(nameof(LoadLaneIndexVector32));
        public static readonly MethodInfo LoadLaneIndexVector64Method =
            GetMethod(nameof(LoadLaneIndexVector64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) LoadVectorLengthVector32()
        {
            Unsafe.SkipInit(out (int, int) result);
            result.Item1 = 2;
            result.Item2 = 2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) LoadVectorLengthVector64()
        {
            Unsafe.SkipInit(out (long, long) result);
            result.Item1 = 2;
            result.Item2 = 2;
            return result;
        }

        public static readonly MethodInfo LoadVectorLengthVector32Method =
            GetMethod(nameof(LoadVectorLengthVector32));
        public static readonly MethodInfo LoadVectorLengthVector64Method =
            GetMethod(nameof(LoadVectorLengthVector64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) LoadAllLanesMask32()
        {
            Unsafe.SkipInit(out (int, int) result);
            result.Item1 = 1;
            result.Item2 = 1;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) LoadAllLanesMask64()
        {
            Unsafe.SkipInit(out (long, long) result);
            result.Item1 = 1L;
            result.Item2 = 1L;
            return result;
        }

        public static readonly MethodInfo LoadAllLanesMask32Method =
            GetMethod(nameof(LoadAllLanesMask32));
        public static readonly MethodInfo LoadAllLanesMask64Method =
            GetMethod(nameof(LoadAllLanesMask64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) LoadNoLanesMask32() => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) LoadNoLanesMask64() => default;

        public static readonly MethodInfo LoadNoLanesMask32Method =
            GetMethod(nameof(LoadNoLanesMask32));
        public static readonly MethodInfo LoadNoLanesMask64Method =
            GetMethod(nameof(LoadNoLanesMask64));

        #endregion

        #region Generic Casts

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CastIToI32(
            (int, int) input) =>
            input;

        public static readonly MethodInfo CastIToI32Method =
            GetMethod(nameof(CastIToI32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CastUToI32(
            (uint, uint) input) =>
            CastWarp<uint, int>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (uint, uint) CastIToU32(
            (int, int) input) =>
            CastWarp<int, uint>(input);

        public static readonly MethodInfo CastUToI32Method =
            GetMethod(nameof(CastUToI32));

        public static readonly MethodInfo CastIToU32Method =
            GetMethod(nameof(CastIToU32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CastFToI32(
            (float, float) input) =>
            CastWarp<float, int>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (float, float) CastIToF32(
            (int, int) input) =>
            CastWarp<int, float>(input);

        public static readonly MethodInfo CastFToI32Method =
            GetMethod(nameof(CastFToI32));

        public static readonly MethodInfo CastIToF32Method =
            GetMethod(nameof(CastIToF32));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) CastIToI64(
            (long, long) input) =>
            input;


        public static readonly MethodInfo CastIToI64Method =
            GetMethod(nameof(CastIToI64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) CastUToI64(
            (ulong, ulong) input) =>
            CastWarp<ulong, long>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (ulong, ulong) CastIToU64(
            (long, long) input) =>
            CastWarp<long, ulong>(input);

        public static readonly MethodInfo CastUToI64Method =
            GetMethod(nameof(CastUToI64));

        public static readonly MethodInfo CastIToU64Method =
            GetMethod(nameof(CastIToU64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) CastFToI64(
            (double, double) input) =>
            CastWarp<double, long>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (double, double) CastIToF64(
            (long, long) input) =>
            CastWarp<long, double>(input);

        public static readonly MethodInfo CastFToI64Method =
            GetMethod(nameof(CastFToI64));

        public static readonly MethodInfo CastIToF64Method =
            GetMethod(nameof(CastIToF64));


        #endregion

        #region Scalar Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) FromScalarI32(int scalar)
        {
            Unsafe.SkipInit(out (int, int) result);
            result.Item1 = scalar;
            result.Item2 = scalar;
            return CastIToI32(result);
        }

        public static readonly MethodInfo FromScalarI32Method =
            GetMethod(nameof(FromScalarI32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) FromScalarU32(uint scalar)
        {
            Unsafe.SkipInit(out (uint, uint) result);
            result.Item1 = scalar;
            result.Item2 = scalar;
            return CastUToI32(result);
        }

        public static readonly MethodInfo FromScalarU32Method =
            GetMethod(nameof(FromScalarU32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) FromScalarF32(float scalar)
        {
            Unsafe.SkipInit(out (float, float) result);
            result.Item1 = scalar;
            result.Item2 = scalar;
            return CastFToI32(result);
        }

        public static readonly MethodInfo FromScalarF32Method =
            GetMethod(nameof(FromScalarF32));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) FromScalarI64(long scalar)
        {
            Unsafe.SkipInit(out (long, long) result);
            result.Item1 = scalar;
            result.Item2 = scalar;
            return CastIToI64(result);
        }

        public static readonly MethodInfo FromScalarI64Method =
            GetMethod(nameof(FromScalarI64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) FromScalarU64(ulong scalar)
        {
            Unsafe.SkipInit(out (ulong, ulong) result);
            result.Item1 = scalar;
            result.Item2 = scalar;
            return CastUToI64(result);
        }

        public static readonly MethodInfo FromScalarU64Method =
            GetMethod(nameof(FromScalarU64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) FromScalarF64(double scalar)
        {
            Unsafe.SkipInit(out (double, double) result);
            result.Item1 = scalar;
            result.Item2 = scalar;
            return CastFToI64(result);
        }

        public static readonly MethodInfo FromScalarF64Method =
            GetMethod(nameof(FromScalarF64));


        #endregion

        #region Select Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) Select32(
            (int, int) mask,
            (int, int) left,
            (int, int) right)
        {
            Unsafe.SkipInit(out (int, int) result);
            result.Item1 = mask.Item1 == 0 ? left.Item1 : right.Item1;
            result.Item2 = mask.Item2 == 0 ? left.Item2 : right.Item2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) Select64(
            (int, int) mask,
            (long, long) left,
            (long, long) right)
        {
            Unsafe.SkipInit(out (long, long) result);
            result.Item1 = mask.Item1 == 0 ? left.Item1 : right.Item1;
            result.Item2 = mask.Item2 == 0 ? left.Item2 : right.Item2;
            return result;
        }

        public static readonly MethodInfo Select32Method = GetMethod(nameof(Select32));
        public static readonly MethodInfo Select64Method = GetMethod(nameof(Select64));

        #endregion

        #region Unary Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) NegI32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = -value.Item1;
            result.Item1 = (int)result1;
            var result2 = -value.Item2;
            result.Item2 = (int)result2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) NegU32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);
            var result1 = ~value.Item1;
            result.Item1 = (uint)result1;
            var result2 = ~value.Item2;
            result.Item2 = (uint)result2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) NegF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = -value.Item1;
            result.Item1 = (float)result1;
            var result2 = -value.Item2;
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) NegI64(
            (long, long) warp)
        {
            var value = CastIToI64(warp);
            Unsafe.SkipInit(out (long, long) result);
            var result1 = -value.Item1;
            result.Item1 = (long)result1;
            var result2 = -value.Item2;
            result.Item2 = (long)result2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) NegU64(
            (long, long) warp)
        {
            var value = CastIToU64(warp);
            Unsafe.SkipInit(out (ulong, ulong) result);
            var result1 = ~value.Item1;
            result.Item1 = (ulong)result1;
            var result2 = ~value.Item2;
            result.Item2 = (ulong)result2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) NegF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = -value.Item1;
            result.Item1 = (double)result1;
            var result2 = -value.Item2;
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) NotI32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = ~value.Item1;
            result.Item1 = (int)result1;
            var result2 = ~value.Item2;
            result.Item2 = (int)result2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) NotU32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);
            var result1 = ~value.Item1;
            result.Item1 = (uint)result1;
            var result2 = ~value.Item2;
            result.Item2 = (uint)result2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) NotI64(
            (long, long) warp)
        {
            var value = CastIToI64(warp);
            Unsafe.SkipInit(out (long, long) result);
            var result1 = ~value.Item1;
            result.Item1 = (long)result1;
            var result2 = ~value.Item2;
            result.Item2 = (long)result2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) NotU64(
            (long, long) warp)
        {
            var value = CastIToU64(warp);
            Unsafe.SkipInit(out (ulong, ulong) result);
            var result1 = ~value.Item1;
            result.Item1 = (ulong)result1;
            var result2 = ~value.Item2;
            result.Item2 = (ulong)result2;
            return CastUToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AbsI32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.Abs(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.Abs(value.Item2);
            result.Item2 = (int)result2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AbsU32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);
            var result1 = IntrinsicMath.Abs(value.Item1);
            result.Item1 = (uint)result1;
            var result2 = IntrinsicMath.Abs(value.Item2);
            result.Item2 = (uint)result2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AbsF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.Abs(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.Abs(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AbsI64(
            (long, long) warp)
        {
            var value = CastIToI64(warp);
            Unsafe.SkipInit(out (long, long) result);
            var result1 = IntrinsicMath.Abs(value.Item1);
            result.Item1 = (long)result1;
            var result2 = IntrinsicMath.Abs(value.Item2);
            result.Item2 = (long)result2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AbsU64(
            (long, long) warp)
        {
            var value = CastIToU64(warp);
            Unsafe.SkipInit(out (ulong, ulong) result);
            var result1 = IntrinsicMath.Abs(value.Item1);
            result.Item1 = (ulong)result1;
            var result2 = IntrinsicMath.Abs(value.Item2);
            result.Item2 = (ulong)result2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AbsF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.Abs(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.Abs(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) PopCI32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.BitOperations.PopCount(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.BitOperations.PopCount(value.Item2);
            result.Item2 = (int)result2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) PopCU32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);
            var result1 = IntrinsicMath.BitOperations.PopCount(value.Item1);
            result.Item1 = (uint)result1;
            var result2 = IntrinsicMath.BitOperations.PopCount(value.Item2);
            result.Item2 = (uint)result2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) PopCI64(
            (long, long) warp)
        {
            var value = CastIToI64(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.BitOperations.PopCount(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.BitOperations.PopCount(value.Item2);
            result.Item2 = (int)result2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) PopCU64(
            (long, long) warp)
        {
            var value = CastIToU64(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.BitOperations.PopCount(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.BitOperations.PopCount(value.Item2);
            result.Item2 = (int)result2;
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CLZI32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2);
            result.Item2 = (int)result2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CLZU32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);
            var result1 = IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1);
            result.Item1 = (uint)result1;
            var result2 = IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2);
            result.Item2 = (uint)result2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CLZI64(
            (long, long) warp)
        {
            var value = CastIToI64(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2);
            result.Item2 = (int)result2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CLZU64(
            (long, long) warp)
        {
            var value = CastIToU64(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2);
            result.Item2 = (int)result2;
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CTZI32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2);
            result.Item2 = (int)result2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CTZU32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);
            var result1 = IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1);
            result.Item1 = (uint)result1;
            var result2 = IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2);
            result.Item2 = (uint)result2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CTZI64(
            (long, long) warp)
        {
            var value = CastIToI64(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2);
            result.Item2 = (int)result2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CTZU64(
            (long, long) warp)
        {
            var value = CastIToU64(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1);
            result.Item1 = (int)result1;
            var result2 = IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2);
            result.Item2 = (int)result2;
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) RcpFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Rcp(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Rcp(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) RcpFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Rcp(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Rcp(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) IsNaNFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.CPUOnly.IsNaN(value.Item1);
            result.Item1 = result1 ? 1 : 0;
            var result2 = IntrinsicMath.CPUOnly.IsNaN(value.Item2);
            result.Item2 = result2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) IsNaNFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.CPUOnly.IsNaN(value.Item1);
            result.Item1 = result1 ? 1 : 0;
            var result2 = IntrinsicMath.CPUOnly.IsNaN(value.Item2);
            result.Item2 = result2 ? 1 : 0;
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) IsInfFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.CPUOnly.IsInfinity(value.Item1);
            result.Item1 = result1 ? 1 : 0;
            var result2 = IntrinsicMath.CPUOnly.IsInfinity(value.Item2);
            result.Item2 = result2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) IsInfFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.CPUOnly.IsInfinity(value.Item1);
            result.Item1 = result1 ? 1 : 0;
            var result2 = IntrinsicMath.CPUOnly.IsInfinity(value.Item2);
            result.Item2 = result2 ? 1 : 0;
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) IsFinFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.CPUOnly.IsFinite(value.Item1);
            result.Item1 = result1 ? 1 : 0;
            var result2 = IntrinsicMath.CPUOnly.IsFinite(value.Item2);
            result.Item2 = result2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) IsFinFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (int, int) result);
            var result1 = IntrinsicMath.CPUOnly.IsFinite(value.Item1);
            result.Item1 = result1 ? 1 : 0;
            var result2 = IntrinsicMath.CPUOnly.IsFinite(value.Item2);
            result.Item2 = result2 ? 1 : 0;
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SqrtFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Sqrt(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Sqrt(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) SqrtFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Sqrt(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Sqrt(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) RsqrtFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Rsqrt(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Rsqrt(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) RsqrtFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Rsqrt(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Rsqrt(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AsinFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Asin(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Asin(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AsinFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Asin(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Asin(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SinFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Sin(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Sin(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) SinFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Sin(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Sin(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SinhFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Sinh(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Sinh(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) SinhFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Sinh(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Sinh(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AcosFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Acos(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Acos(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AcosFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Acos(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Acos(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CosFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Cos(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Cos(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) CosFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Cos(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Cos(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CoshFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Cosh(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Cosh(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) CoshFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Cosh(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Cosh(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) TanFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Tan(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Tan(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) TanFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Tan(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Tan(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) TanhFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Tanh(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Tanh(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) TanhFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Tanh(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Tanh(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AtanFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Atan(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Atan(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AtanFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Atan(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Atan(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ExpFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Exp(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Exp(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ExpFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Exp(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Exp(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) Exp2FF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Exp2(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Exp2(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) Exp2FF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Exp2(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Exp2(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) FloorFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Floor(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Floor(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) FloorFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Floor(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Floor(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CeilingFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Ceiling(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Ceiling(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) CeilingFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Ceiling(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Ceiling(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) LogFF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Log(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Log(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) LogFF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Log(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Log(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) Log2FF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Log2(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Log2(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) Log2FF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Log2(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Log2(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) Log10FF32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);
            var result1 = IntrinsicMath.CPUOnly.Log10(value.Item1);
            result.Item1 = (float)result1;
            var result2 = IntrinsicMath.CPUOnly.Log10(value.Item2);
            result.Item2 = (float)result2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) Log10FF64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (double, double) result);
            var result1 = IntrinsicMath.CPUOnly.Log10(value.Item1);
            result.Item1 = (double)result1;
            var result2 = IntrinsicMath.CPUOnly.Log10(value.Item2);
            result.Item2 = (double)result2;
            return CastFToI64(result);
        }



        private static readonly Dictionary<
            (UnaryArithmeticKind, VelocityWarpOperationMode), MethodInfo>
            UnaryOperations32 = new();
        private static readonly Dictionary<
            (UnaryArithmeticKind, VelocityWarpOperationMode), MethodInfo>
            UnaryOperations64 = new();

        private static void InitUnaryOperations()
        {
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Neg, VelocityWarpOperationMode.I),
                GetMethod(nameof(NegI32)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Neg, VelocityWarpOperationMode.U),
                GetMethod(nameof(NegU32)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Neg, VelocityWarpOperationMode.F),
                GetMethod(nameof(NegF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.Neg, VelocityWarpOperationMode.I),
                GetMethod(nameof(NegI64)));
            UnaryOperations64.Add(
                (UnaryArithmeticKind.Neg, VelocityWarpOperationMode.U),
                GetMethod(nameof(NegU64)));
            UnaryOperations64.Add(
                (UnaryArithmeticKind.Neg, VelocityWarpOperationMode.F),
                GetMethod(nameof(NegF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Not, VelocityWarpOperationMode.I),
                GetMethod(nameof(NotI32)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Not, VelocityWarpOperationMode.U),
                GetMethod(nameof(NotU32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.Not, VelocityWarpOperationMode.I),
                GetMethod(nameof(NotI64)));
            UnaryOperations64.Add(
                (UnaryArithmeticKind.Not, VelocityWarpOperationMode.U),
                GetMethod(nameof(NotU64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Abs, VelocityWarpOperationMode.I),
                GetMethod(nameof(AbsI32)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Abs, VelocityWarpOperationMode.U),
                GetMethod(nameof(AbsU32)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Abs, VelocityWarpOperationMode.F),
                GetMethod(nameof(AbsF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.Abs, VelocityWarpOperationMode.I),
                GetMethod(nameof(AbsI64)));
            UnaryOperations64.Add(
                (UnaryArithmeticKind.Abs, VelocityWarpOperationMode.U),
                GetMethod(nameof(AbsU64)));
            UnaryOperations64.Add(
                (UnaryArithmeticKind.Abs, VelocityWarpOperationMode.F),
                GetMethod(nameof(AbsF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.PopC, VelocityWarpOperationMode.I),
                GetMethod(nameof(PopCI32)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.PopC, VelocityWarpOperationMode.U),
                GetMethod(nameof(PopCU32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.PopC, VelocityWarpOperationMode.I),
                GetMethod(nameof(PopCI64)));
            UnaryOperations64.Add(
                (UnaryArithmeticKind.PopC, VelocityWarpOperationMode.U),
                GetMethod(nameof(PopCU64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.CLZ, VelocityWarpOperationMode.I),
                GetMethod(nameof(CLZI32)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.CLZ, VelocityWarpOperationMode.U),
                GetMethod(nameof(CLZU32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.CLZ, VelocityWarpOperationMode.I),
                GetMethod(nameof(CLZI64)));
            UnaryOperations64.Add(
                (UnaryArithmeticKind.CLZ, VelocityWarpOperationMode.U),
                GetMethod(nameof(CLZU64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.CTZ, VelocityWarpOperationMode.I),
                GetMethod(nameof(CTZI32)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.CTZ, VelocityWarpOperationMode.U),
                GetMethod(nameof(CTZU32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.CTZ, VelocityWarpOperationMode.I),
                GetMethod(nameof(CTZI64)));
            UnaryOperations64.Add(
                (UnaryArithmeticKind.CTZ, VelocityWarpOperationMode.U),
                GetMethod(nameof(CTZU64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.RcpF, VelocityWarpOperationMode.F),
                GetMethod(nameof(RcpFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.RcpF, VelocityWarpOperationMode.F),
                GetMethod(nameof(RcpFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.IsNaNF, VelocityWarpOperationMode.F),
                GetMethod(nameof(IsNaNFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.IsNaNF, VelocityWarpOperationMode.F),
                GetMethod(nameof(IsNaNFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.IsInfF, VelocityWarpOperationMode.F),
                GetMethod(nameof(IsInfFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.IsInfF, VelocityWarpOperationMode.F),
                GetMethod(nameof(IsInfFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.IsFinF, VelocityWarpOperationMode.F),
                GetMethod(nameof(IsFinFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.IsFinF, VelocityWarpOperationMode.F),
                GetMethod(nameof(IsFinFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.SqrtF, VelocityWarpOperationMode.F),
                GetMethod(nameof(SqrtFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.SqrtF, VelocityWarpOperationMode.F),
                GetMethod(nameof(SqrtFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.RsqrtF, VelocityWarpOperationMode.F),
                GetMethod(nameof(RsqrtFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.RsqrtF, VelocityWarpOperationMode.F),
                GetMethod(nameof(RsqrtFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.AsinF, VelocityWarpOperationMode.F),
                GetMethod(nameof(AsinFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.AsinF, VelocityWarpOperationMode.F),
                GetMethod(nameof(AsinFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.SinF, VelocityWarpOperationMode.F),
                GetMethod(nameof(SinFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.SinF, VelocityWarpOperationMode.F),
                GetMethod(nameof(SinFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.SinhF, VelocityWarpOperationMode.F),
                GetMethod(nameof(SinhFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.SinhF, VelocityWarpOperationMode.F),
                GetMethod(nameof(SinhFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.AcosF, VelocityWarpOperationMode.F),
                GetMethod(nameof(AcosFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.AcosF, VelocityWarpOperationMode.F),
                GetMethod(nameof(AcosFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.CosF, VelocityWarpOperationMode.F),
                GetMethod(nameof(CosFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.CosF, VelocityWarpOperationMode.F),
                GetMethod(nameof(CosFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.CoshF, VelocityWarpOperationMode.F),
                GetMethod(nameof(CoshFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.CoshF, VelocityWarpOperationMode.F),
                GetMethod(nameof(CoshFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.TanF, VelocityWarpOperationMode.F),
                GetMethod(nameof(TanFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.TanF, VelocityWarpOperationMode.F),
                GetMethod(nameof(TanFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.TanhF, VelocityWarpOperationMode.F),
                GetMethod(nameof(TanhFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.TanhF, VelocityWarpOperationMode.F),
                GetMethod(nameof(TanhFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.AtanF, VelocityWarpOperationMode.F),
                GetMethod(nameof(AtanFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.AtanF, VelocityWarpOperationMode.F),
                GetMethod(nameof(AtanFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.ExpF, VelocityWarpOperationMode.F),
                GetMethod(nameof(ExpFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.ExpF, VelocityWarpOperationMode.F),
                GetMethod(nameof(ExpFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Exp2F, VelocityWarpOperationMode.F),
                GetMethod(nameof(Exp2FF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.Exp2F, VelocityWarpOperationMode.F),
                GetMethod(nameof(Exp2FF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.FloorF, VelocityWarpOperationMode.F),
                GetMethod(nameof(FloorFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.FloorF, VelocityWarpOperationMode.F),
                GetMethod(nameof(FloorFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.CeilingF, VelocityWarpOperationMode.F),
                GetMethod(nameof(CeilingFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.CeilingF, VelocityWarpOperationMode.F),
                GetMethod(nameof(CeilingFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.LogF, VelocityWarpOperationMode.F),
                GetMethod(nameof(LogFF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.LogF, VelocityWarpOperationMode.F),
                GetMethod(nameof(LogFF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Log2F, VelocityWarpOperationMode.F),
                GetMethod(nameof(Log2FF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.Log2F, VelocityWarpOperationMode.F),
                GetMethod(nameof(Log2FF64)));
            UnaryOperations32.Add(
                (UnaryArithmeticKind.Log10F, VelocityWarpOperationMode.F),
                GetMethod(nameof(Log10FF32)));

            UnaryOperations64.Add(
                (UnaryArithmeticKind.Log10F, VelocityWarpOperationMode.F),
                GetMethod(nameof(Log10FF64)));
        }

        public static MethodInfo GetUnaryOperation32(
            UnaryArithmeticKind kind,
            VelocityWarpOperationMode mode) => UnaryOperations32[(kind, mode)];
        public static MethodInfo GetUnaryOperation64(
            UnaryArithmeticKind kind,
            VelocityWarpOperationMode mode) => UnaryOperations64[(kind, mode)];

        #endregion

        #region Binary Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AddI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 + right.Item1;
            result.Item2 = left.Item2 + right.Item2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AddU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = left.Item1 + right.Item1;
            result.Item2 = left.Item2 + right.Item2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AddF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = left.Item1 + right.Item1;
            result.Item2 = left.Item2 + right.Item2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AddI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = left.Item1 + right.Item1;
            result.Item2 = left.Item2 + right.Item2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AddU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = left.Item1 + right.Item1;
            result.Item2 = left.Item2 + right.Item2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AddF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = left.Item1 + right.Item1;
            result.Item2 = left.Item2 + right.Item2;
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SubI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 - right.Item1;
            result.Item2 = left.Item2 - right.Item2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SubU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = left.Item1 - right.Item1;
            result.Item2 = left.Item2 - right.Item2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SubF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = left.Item1 - right.Item1;
            result.Item2 = left.Item2 - right.Item2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) SubI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = left.Item1 - right.Item1;
            result.Item2 = left.Item2 - right.Item2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) SubU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = left.Item1 - right.Item1;
            result.Item2 = left.Item2 - right.Item2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) SubF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = left.Item1 - right.Item1;
            result.Item2 = left.Item2 - right.Item2;
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MulI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 * right.Item1;
            result.Item2 = left.Item2 * right.Item2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MulU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = left.Item1 * right.Item1;
            result.Item2 = left.Item2 * right.Item2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MulF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = left.Item1 * right.Item1;
            result.Item2 = left.Item2 * right.Item2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MulI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = left.Item1 * right.Item1;
            result.Item2 = left.Item2 * right.Item2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MulU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = left.Item1 * right.Item1;
            result.Item2 = left.Item2 * right.Item2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MulF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = left.Item1 * right.Item1;
            result.Item2 = left.Item2 * right.Item2;
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) DivI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 / right.Item1;
            result.Item2 = left.Item2 / right.Item2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) DivU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = left.Item1 / right.Item1;
            result.Item2 = left.Item2 / right.Item2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) DivF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = left.Item1 / right.Item1;
            result.Item2 = left.Item2 / right.Item2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) DivI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = left.Item1 / right.Item1;
            result.Item2 = left.Item2 / right.Item2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) DivU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = left.Item1 / right.Item1;
            result.Item2 = left.Item2 / right.Item2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) DivF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = left.Item1 / right.Item1;
            result.Item2 = left.Item2 / right.Item2;
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) RemI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 % right.Item1;
            result.Item2 = left.Item2 % right.Item2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) RemU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = left.Item1 % right.Item1;
            result.Item2 = left.Item2 % right.Item2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) RemF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = left.Item1 % right.Item1;
            result.Item2 = left.Item2 % right.Item2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) RemI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = left.Item1 % right.Item1;
            result.Item2 = left.Item2 % right.Item2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) RemU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = left.Item1 % right.Item1;
            result.Item2 = left.Item2 % right.Item2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) RemF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = left.Item1 % right.Item1;
            result.Item2 = left.Item2 % right.Item2;
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AndI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = Bitwise.And(left.Item1, right.Item1);
            result.Item2 = Bitwise.And(left.Item2, right.Item2);
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) AndU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = Bitwise.And(left.Item1, right.Item1);
            result.Item2 = Bitwise.And(left.Item2, right.Item2);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AndI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = Bitwise.And(left.Item1, right.Item1);
            result.Item2 = Bitwise.And(left.Item2, right.Item2);
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) AndU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = Bitwise.And(left.Item1, right.Item1);
            result.Item2 = Bitwise.And(left.Item2, right.Item2);
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) OrI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = Bitwise.Or(left.Item1, right.Item1);
            result.Item2 = Bitwise.Or(left.Item2, right.Item2);
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) OrU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = Bitwise.Or(left.Item1, right.Item1);
            result.Item2 = Bitwise.Or(left.Item2, right.Item2);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) OrI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = Bitwise.Or(left.Item1, right.Item1);
            result.Item2 = Bitwise.Or(left.Item2, right.Item2);
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) OrU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = Bitwise.Or(left.Item1, right.Item1);
            result.Item2 = Bitwise.Or(left.Item2, right.Item2);
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) XorI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 ^ right.Item1;
            result.Item2 = left.Item2 ^ right.Item2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) XorU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = left.Item1 ^ right.Item1;
            result.Item2 = left.Item2 ^ right.Item2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) XorI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = left.Item1 ^ right.Item1;
            result.Item2 = left.Item2 ^ right.Item2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) XorU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = left.Item1 ^ right.Item1;
            result.Item2 = left.Item2 ^ right.Item2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ShlI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 << (int)right.Item1;
            result.Item2 = left.Item2 << (int)right.Item2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ShlU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = left.Item1 << (int)right.Item1;
            result.Item2 = left.Item2 << (int)right.Item2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ShlI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = left.Item1 << (int)right.Item1;
            result.Item2 = left.Item2 << (int)right.Item2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ShlU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = left.Item1 << (int)right.Item1;
            result.Item2 = left.Item2 << (int)right.Item2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ShrI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 >> (int)right.Item1;
            result.Item2 = left.Item2 >> (int)right.Item2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ShrU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = left.Item1 >> (int)right.Item1;
            result.Item2 = left.Item2 >> (int)right.Item2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ShrI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = left.Item1 >> (int)right.Item1;
            result.Item2 = left.Item2 >> (int)right.Item2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ShrU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = left.Item1 >> (int)right.Item1;
            result.Item2 = left.Item2 >> (int)right.Item2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MinI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = IntrinsicMath.Min(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Min(left.Item2, right.Item2);
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MinU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = IntrinsicMath.Min(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Min(left.Item2, right.Item2);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MinF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = IntrinsicMath.Min(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Min(left.Item2, right.Item2);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MinI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = IntrinsicMath.Min(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Min(left.Item2, right.Item2);
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MinU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = IntrinsicMath.Min(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Min(left.Item2, right.Item2);
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MinF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = IntrinsicMath.Min(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Min(left.Item2, right.Item2);
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MaxI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = IntrinsicMath.Max(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Max(left.Item2, right.Item2);
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MaxU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = IntrinsicMath.Max(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Max(left.Item2, right.Item2);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MaxF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = IntrinsicMath.Max(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Max(left.Item2, right.Item2);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MaxI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = IntrinsicMath.Max(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Max(left.Item2, right.Item2);
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MaxU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = IntrinsicMath.Max(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Max(left.Item2, right.Item2);
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MaxF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = IntrinsicMath.Max(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.Max(left.Item2, right.Item2);
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) Atan2FF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = IntrinsicMath.CPUOnly.Atan2(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.Atan2(left.Item2, right.Item2);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) Atan2FF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = IntrinsicMath.CPUOnly.Atan2(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.Atan2(left.Item2, right.Item2);
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) PowFF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = IntrinsicMath.CPUOnly.Pow(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.Pow(left.Item2, right.Item2);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) PowFF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = IntrinsicMath.CPUOnly.Pow(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.Pow(left.Item2, right.Item2);
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) BinaryLogFF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = IntrinsicMath.CPUOnly.Log(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.Log(left.Item2, right.Item2);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) BinaryLogFF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = IntrinsicMath.CPUOnly.Log(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.Log(left.Item2, right.Item2);
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CopySignFF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = IntrinsicMath.CopySign(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.CopySign(left.Item2, right.Item2);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) CopySignFF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = IntrinsicMath.CopySign(left.Item1, right.Item1);
            result.Item2 = IntrinsicMath.CopySign(left.Item2, right.Item2);
            return CastFToI64(result);
        }


        private static readonly Dictionary<
            (BinaryArithmeticKind, VelocityWarpOperationMode), MethodInfo>
            BinaryOperations32 = new();
        private static readonly Dictionary<
            (BinaryArithmeticKind, VelocityWarpOperationMode), MethodInfo>
            BinaryOperations64 = new();

        private static void InitBinaryOperations()
        {
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Add, VelocityWarpOperationMode.I),
                GetMethod(nameof(AddI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Add, VelocityWarpOperationMode.U),
                GetMethod(nameof(AddU32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Add, VelocityWarpOperationMode.F),
                GetMethod(nameof(AddF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Add, VelocityWarpOperationMode.I),
                GetMethod(nameof(AddI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Add, VelocityWarpOperationMode.U),
                GetMethod(nameof(AddU64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Add, VelocityWarpOperationMode.F),
                GetMethod(nameof(AddF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Sub, VelocityWarpOperationMode.I),
                GetMethod(nameof(SubI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Sub, VelocityWarpOperationMode.U),
                GetMethod(nameof(SubU32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Sub, VelocityWarpOperationMode.F),
                GetMethod(nameof(SubF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Sub, VelocityWarpOperationMode.I),
                GetMethod(nameof(SubI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Sub, VelocityWarpOperationMode.U),
                GetMethod(nameof(SubU64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Sub, VelocityWarpOperationMode.F),
                GetMethod(nameof(SubF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Mul, VelocityWarpOperationMode.I),
                GetMethod(nameof(MulI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Mul, VelocityWarpOperationMode.U),
                GetMethod(nameof(MulU32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Mul, VelocityWarpOperationMode.F),
                GetMethod(nameof(MulF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Mul, VelocityWarpOperationMode.I),
                GetMethod(nameof(MulI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Mul, VelocityWarpOperationMode.U),
                GetMethod(nameof(MulU64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Mul, VelocityWarpOperationMode.F),
                GetMethod(nameof(MulF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Div, VelocityWarpOperationMode.I),
                GetMethod(nameof(DivI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Div, VelocityWarpOperationMode.U),
                GetMethod(nameof(DivU32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Div, VelocityWarpOperationMode.F),
                GetMethod(nameof(DivF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Div, VelocityWarpOperationMode.I),
                GetMethod(nameof(DivI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Div, VelocityWarpOperationMode.U),
                GetMethod(nameof(DivU64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Div, VelocityWarpOperationMode.F),
                GetMethod(nameof(DivF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Rem, VelocityWarpOperationMode.I),
                GetMethod(nameof(RemI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Rem, VelocityWarpOperationMode.U),
                GetMethod(nameof(RemU32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Rem, VelocityWarpOperationMode.F),
                GetMethod(nameof(RemF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Rem, VelocityWarpOperationMode.I),
                GetMethod(nameof(RemI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Rem, VelocityWarpOperationMode.U),
                GetMethod(nameof(RemU64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Rem, VelocityWarpOperationMode.F),
                GetMethod(nameof(RemF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.And, VelocityWarpOperationMode.I),
                GetMethod(nameof(AndI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.And, VelocityWarpOperationMode.U),
                GetMethod(nameof(AndU32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.And, VelocityWarpOperationMode.I),
                GetMethod(nameof(AndI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.And, VelocityWarpOperationMode.U),
                GetMethod(nameof(AndU64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Or, VelocityWarpOperationMode.I),
                GetMethod(nameof(OrI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Or, VelocityWarpOperationMode.U),
                GetMethod(nameof(OrU32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Or, VelocityWarpOperationMode.I),
                GetMethod(nameof(OrI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Or, VelocityWarpOperationMode.U),
                GetMethod(nameof(OrU64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Xor, VelocityWarpOperationMode.I),
                GetMethod(nameof(XorI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Xor, VelocityWarpOperationMode.U),
                GetMethod(nameof(XorU32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Xor, VelocityWarpOperationMode.I),
                GetMethod(nameof(XorI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Xor, VelocityWarpOperationMode.U),
                GetMethod(nameof(XorU64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Shl, VelocityWarpOperationMode.I),
                GetMethod(nameof(ShlI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Shl, VelocityWarpOperationMode.U),
                GetMethod(nameof(ShlU32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Shl, VelocityWarpOperationMode.I),
                GetMethod(nameof(ShlI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Shl, VelocityWarpOperationMode.U),
                GetMethod(nameof(ShlU64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Shr, VelocityWarpOperationMode.I),
                GetMethod(nameof(ShrI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Shr, VelocityWarpOperationMode.U),
                GetMethod(nameof(ShrU32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Shr, VelocityWarpOperationMode.I),
                GetMethod(nameof(ShrI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Shr, VelocityWarpOperationMode.U),
                GetMethod(nameof(ShrU64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Min, VelocityWarpOperationMode.I),
                GetMethod(nameof(MinI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Min, VelocityWarpOperationMode.U),
                GetMethod(nameof(MinU32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Min, VelocityWarpOperationMode.F),
                GetMethod(nameof(MinF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Min, VelocityWarpOperationMode.I),
                GetMethod(nameof(MinI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Min, VelocityWarpOperationMode.U),
                GetMethod(nameof(MinU64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Min, VelocityWarpOperationMode.F),
                GetMethod(nameof(MinF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Max, VelocityWarpOperationMode.I),
                GetMethod(nameof(MaxI32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Max, VelocityWarpOperationMode.U),
                GetMethod(nameof(MaxU32)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Max, VelocityWarpOperationMode.F),
                GetMethod(nameof(MaxF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Max, VelocityWarpOperationMode.I),
                GetMethod(nameof(MaxI64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Max, VelocityWarpOperationMode.U),
                GetMethod(nameof(MaxU64)));
            BinaryOperations64.Add(
                (BinaryArithmeticKind.Max, VelocityWarpOperationMode.F),
                GetMethod(nameof(MaxF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.Atan2F, VelocityWarpOperationMode.F),
                GetMethod(nameof(Atan2FF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.Atan2F, VelocityWarpOperationMode.F),
                GetMethod(nameof(Atan2FF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.PowF, VelocityWarpOperationMode.F),
                GetMethod(nameof(PowFF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.PowF, VelocityWarpOperationMode.F),
                GetMethod(nameof(PowFF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.BinaryLogF, VelocityWarpOperationMode.F),
                GetMethod(nameof(BinaryLogFF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.BinaryLogF, VelocityWarpOperationMode.F),
                GetMethod(nameof(BinaryLogFF64)));
            BinaryOperations32.Add(
                (BinaryArithmeticKind.CopySignF, VelocityWarpOperationMode.F),
                GetMethod(nameof(CopySignFF32)));

            BinaryOperations64.Add(
                (BinaryArithmeticKind.CopySignF, VelocityWarpOperationMode.F),
                GetMethod(nameof(CopySignFF64)));
        }

        public static MethodInfo GetBinaryOperation32(
            BinaryArithmeticKind kind,
            VelocityWarpOperationMode mode) => BinaryOperations32[(kind, mode)];
        public static MethodInfo GetBinaryOperation64(
            BinaryArithmeticKind kind,
            VelocityWarpOperationMode mode) => BinaryOperations64[(kind, mode)];

        #endregion

        #region Ternary Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MultiplyAddI32(
            (int, int) first,
            (int, int) second,
            (int, int) third)
        {
            var source = CastIToI32(first);
            var add = CastIToI32(second);
            var mul = CastIToI32(third);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = IntrinsicMath.CPUOnly.FMA(source.Item1, add.Item1, mul.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.FMA(source.Item2, add.Item2, mul.Item2);
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MultiplyAddU32(
            (int, int) first,
            (int, int) second,
            (int, int) third)
        {
            var source = CastIToU32(first);
            var add = CastIToU32(second);
            var mul = CastIToU32(third);
            Unsafe.SkipInit(out (uint, uint) result);

            result.Item1 = IntrinsicMath.CPUOnly.FMA(source.Item1, add.Item1, mul.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.FMA(source.Item2, add.Item2, mul.Item2);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) MultiplyAddF32(
            (int, int) first,
            (int, int) second,
            (int, int) third)
        {
            var source = CastIToF32(first);
            var add = CastIToF32(second);
            var mul = CastIToF32(third);
            Unsafe.SkipInit(out (float, float) result);

            result.Item1 = IntrinsicMath.CPUOnly.FMA(source.Item1, add.Item1, mul.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.FMA(source.Item2, add.Item2, mul.Item2);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MultiplyAddI64(
            (long, long) first,
            (long, long) second,
            (long, long) third)
        {
            var source = CastIToI64(first);
            var add = CastIToI64(second);
            var mul = CastIToI64(third);
            Unsafe.SkipInit(out (long, long) result);

            result.Item1 = IntrinsicMath.CPUOnly.FMA(source.Item1, add.Item1, mul.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.FMA(source.Item2, add.Item2, mul.Item2);
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MultiplyAddU64(
            (long, long) first,
            (long, long) second,
            (long, long) third)
        {
            var source = CastIToU64(first);
            var add = CastIToU64(second);
            var mul = CastIToU64(third);
            Unsafe.SkipInit(out (ulong, ulong) result);

            result.Item1 = IntrinsicMath.CPUOnly.FMA(source.Item1, add.Item1, mul.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.FMA(source.Item2, add.Item2, mul.Item2);
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) MultiplyAddF64(
            (long, long) first,
            (long, long) second,
            (long, long) third)
        {
            var source = CastIToF64(first);
            var add = CastIToF64(second);
            var mul = CastIToF64(third);
            Unsafe.SkipInit(out (double, double) result);

            result.Item1 = IntrinsicMath.CPUOnly.FMA(source.Item1, add.Item1, mul.Item1);
            result.Item2 = IntrinsicMath.CPUOnly.FMA(source.Item2, add.Item2, mul.Item2);
            return CastFToI64(result);
        }


        private static readonly Dictionary<
            (TernaryArithmeticKind, VelocityWarpOperationMode), MethodInfo>
            TernaryOperations32 = new();
        private static readonly Dictionary<
            (TernaryArithmeticKind, VelocityWarpOperationMode), MethodInfo>
            TernaryOperations64 = new();

        private static void InitTernaryOperations()
        {
            TernaryOperations32.Add(
                (TernaryArithmeticKind.MultiplyAdd, VelocityWarpOperationMode.I),
                GetMethod(nameof(MultiplyAddI32)));
            TernaryOperations32.Add(
                (TernaryArithmeticKind.MultiplyAdd, VelocityWarpOperationMode.U),
                GetMethod(nameof(MultiplyAddU32)));
            TernaryOperations32.Add(
                (TernaryArithmeticKind.MultiplyAdd, VelocityWarpOperationMode.F),
                GetMethod(nameof(MultiplyAddF32)));

            TernaryOperations64.Add(
                (TernaryArithmeticKind.MultiplyAdd, VelocityWarpOperationMode.I),
                GetMethod(nameof(MultiplyAddI64)));
            TernaryOperations64.Add(
                (TernaryArithmeticKind.MultiplyAdd, VelocityWarpOperationMode.U),
                GetMethod(nameof(MultiplyAddU64)));
            TernaryOperations64.Add(
                (TernaryArithmeticKind.MultiplyAdd, VelocityWarpOperationMode.F),
                GetMethod(nameof(MultiplyAddF64)));
        }

        public static MethodInfo GetTernaryOperation32(
            TernaryArithmeticKind kind,
            VelocityWarpOperationMode mode) => TernaryOperations32[(kind, mode)];
        public static MethodInfo GetTernaryOperation64(
            TernaryArithmeticKind kind,
            VelocityWarpOperationMode mode) => TernaryOperations64[(kind, mode)];

        #endregion

        #region Compare Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareEqualI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 == right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 == right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareEqualU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 == right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 == right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareEqualF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 == right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 == right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareEqualI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 == right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 == right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareEqualU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 == right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 == right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareEqualF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 == right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 == right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareNotEqualI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 != right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 != right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareNotEqualU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 != right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 != right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareNotEqualF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 != right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 != right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareNotEqualI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 != right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 != right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareNotEqualU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 != right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 != right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareNotEqualF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 != right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 != right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessThanI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 < right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 < right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessThanU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 < right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 < right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessThanF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 < right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 < right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessThanI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 < right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 < right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessThanU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 < right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 < right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessThanF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 < right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 < right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessEqualI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 <= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 <= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessEqualU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 <= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 <= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessEqualF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 <= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 <= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessEqualI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 <= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 <= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessEqualU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 <= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 <= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareLessEqualF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 <= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 <= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterThanI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 > right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 > right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterThanU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 > right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 > right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterThanF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 > right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 > right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterThanI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 > right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 > right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterThanU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 > right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 > right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterThanF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 > right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 > right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterEqualI32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToI32(first);
            var right = CastIToI32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 >= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 >= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterEqualU32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 >= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 >= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterEqualF32(
            (int, int) first,
            (int, int) second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 >= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 >= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterEqualI64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToI64(first);
            var right = CastIToI64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 >= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 >= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterEqualU64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 >= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 >= right.Item2 ? 1 : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) CompareGreaterEqualF64(
            (long, long) first,
            (long, long) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);
            Unsafe.SkipInit(out (int, int) result);

            result.Item1 = left.Item1 >= right.Item1 ? 1 : 0;
            result.Item2 = left.Item2 >= right.Item2 ? 1 : 0;
            return result;
        }

        private static readonly Dictionary<
            (CompareKind, VelocityWarpOperationMode, bool),
            MethodInfo> CompareOperations = new();

        private static void InitializeCompareOperations()
        {
            CompareOperations.Add(
                (CompareKind.Equal, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(CompareEqualI32)));
            CompareOperations.Add(
                (CompareKind.Equal, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(CompareEqualU32)));
            CompareOperations.Add(
                (CompareKind.Equal, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(CompareEqualF32)));
            CompareOperations.Add(
                (CompareKind.Equal, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(CompareEqualI64)));
            CompareOperations.Add(
                (CompareKind.Equal, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(CompareEqualU64)));
            CompareOperations.Add(
                (CompareKind.Equal, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(CompareEqualF64)));
            CompareOperations.Add(
                (CompareKind.NotEqual, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(CompareNotEqualI32)));
            CompareOperations.Add(
                (CompareKind.NotEqual, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(CompareNotEqualU32)));
            CompareOperations.Add(
                (CompareKind.NotEqual, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(CompareNotEqualF32)));
            CompareOperations.Add(
                (CompareKind.NotEqual, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(CompareNotEqualI64)));
            CompareOperations.Add(
                (CompareKind.NotEqual, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(CompareNotEqualU64)));
            CompareOperations.Add(
                (CompareKind.NotEqual, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(CompareNotEqualF64)));
            CompareOperations.Add(
                (CompareKind.LessThan, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(CompareLessThanI32)));
            CompareOperations.Add(
                (CompareKind.LessThan, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(CompareLessThanU32)));
            CompareOperations.Add(
                (CompareKind.LessThan, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(CompareLessThanF32)));
            CompareOperations.Add(
                (CompareKind.LessThan, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(CompareLessThanI64)));
            CompareOperations.Add(
                (CompareKind.LessThan, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(CompareLessThanU64)));
            CompareOperations.Add(
                (CompareKind.LessThan, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(CompareLessThanF64)));
            CompareOperations.Add(
                (CompareKind.LessEqual, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(CompareLessEqualI32)));
            CompareOperations.Add(
                (CompareKind.LessEqual, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(CompareLessEqualU32)));
            CompareOperations.Add(
                (CompareKind.LessEqual, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(CompareLessEqualF32)));
            CompareOperations.Add(
                (CompareKind.LessEqual, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(CompareLessEqualI64)));
            CompareOperations.Add(
                (CompareKind.LessEqual, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(CompareLessEqualU64)));
            CompareOperations.Add(
                (CompareKind.LessEqual, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(CompareLessEqualF64)));
            CompareOperations.Add(
                (CompareKind.GreaterThan, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(CompareGreaterThanI32)));
            CompareOperations.Add(
                (CompareKind.GreaterThan, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(CompareGreaterThanU32)));
            CompareOperations.Add(
                (CompareKind.GreaterThan, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(CompareGreaterThanF32)));
            CompareOperations.Add(
                (CompareKind.GreaterThan, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(CompareGreaterThanI64)));
            CompareOperations.Add(
                (CompareKind.GreaterThan, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(CompareGreaterThanU64)));
            CompareOperations.Add(
                (CompareKind.GreaterThan, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(CompareGreaterThanF64)));
            CompareOperations.Add(
                (CompareKind.GreaterEqual, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(CompareGreaterEqualI32)));
            CompareOperations.Add(
                (CompareKind.GreaterEqual, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(CompareGreaterEqualU32)));
            CompareOperations.Add(
                (CompareKind.GreaterEqual, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(CompareGreaterEqualF32)));
            CompareOperations.Add(
                (CompareKind.GreaterEqual, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(CompareGreaterEqualI64)));
            CompareOperations.Add(
                (CompareKind.GreaterEqual, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(CompareGreaterEqualU64)));
            CompareOperations.Add(
                (CompareKind.GreaterEqual, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(CompareGreaterEqualF64)));
        }

        public static MethodInfo GetCompareOperation32(
            CompareKind kind,
            VelocityWarpOperationMode mode) =>
            CompareOperations[(kind, mode, false)];

        public static MethodInfo GetCompareOperation64(
            CompareKind kind,
            VelocityWarpOperationMode mode) =>
            CompareOperations[(kind, mode, true)];

        #endregion

        #region Convert Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt8ToInt8_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt8ToInt16_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (short)(sbyte)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (short)(sbyte)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt8ToInt32_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (int)(sbyte)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (int)(sbyte)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt8ToUInt8_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt8ToUInt16_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (ushort)(sbyte)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (ushort)(sbyte)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt8ToUInt32_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (uint)(sbyte)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (uint)(sbyte)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt8ToHalf_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (Half)(sbyte)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (Half)(sbyte)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt8ToFloat_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (float)(sbyte)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (float)(sbyte)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt16ToInt8_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (sbyte)(short)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (sbyte)(short)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt16ToInt16_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt16ToInt32_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (int)(short)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (int)(short)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt16ToUInt8_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (byte)(short)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (byte)(short)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt16ToUInt16_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt16ToUInt32_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (uint)(short)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (uint)(short)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt16ToHalf_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (Half)(short)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (Half)(short)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt16ToFloat_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (float)(short)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (float)(short)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt32ToInt8_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (sbyte)(int)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (sbyte)(int)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt32ToInt16_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (short)(int)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (short)(int)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt32ToInt32_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt32ToUInt8_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (byte)(int)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (byte)(int)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt32ToUInt16_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (ushort)(int)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (ushort)(int)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt32ToUInt32_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt32ToHalf_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (Half)(int)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (Half)(int)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertInt32ToFloat_32(
            (int, int) warp)
        {
            var value = CastIToI32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (float)(int)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (float)(int)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt8ToInt8_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt8ToInt16_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (short)(byte)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (short)(byte)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt8ToInt32_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (int)(byte)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (int)(byte)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt8ToUInt8_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt8ToUInt16_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (ushort)(byte)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (ushort)(byte)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt8ToUInt32_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (uint)(byte)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (uint)(byte)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt8ToHalf_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (Half)(byte)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (Half)(byte)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt8ToFloat_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (float)(byte)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (float)(byte)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt16ToInt8_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (sbyte)(ushort)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (sbyte)(ushort)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt16ToInt16_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt16ToInt32_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (int)(ushort)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (int)(ushort)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt16ToUInt8_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (byte)(ushort)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (byte)(ushort)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt16ToUInt16_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt16ToUInt32_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (uint)(ushort)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (uint)(ushort)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt16ToHalf_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (Half)(ushort)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (Half)(ushort)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt16ToFloat_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (float)(ushort)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (float)(ushort)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt32ToInt8_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (sbyte)(uint)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (sbyte)(uint)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt32ToInt16_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (short)(uint)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (short)(uint)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt32ToInt32_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt32ToUInt8_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (byte)(uint)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (byte)(uint)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt32ToUInt16_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (ushort)(uint)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (ushort)(uint)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt32ToUInt32_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt32ToHalf_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (Half)(uint)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (Half)(uint)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertUInt32ToFloat_32(
            (int, int) warp)
        {
            var value = CastIToU32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (float)(uint)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (float)(uint)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertHalfToInt8_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (sbyte)(Half)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (sbyte)(Half)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertHalfToInt16_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (short)(Half)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (short)(Half)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertHalfToInt32_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (int)(Half)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (int)(Half)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertHalfToUInt8_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (byte)(Half)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (byte)(Half)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertHalfToUInt16_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (ushort)(Half)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (ushort)(Half)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertHalfToUInt32_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (uint)(Half)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (uint)(Half)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertHalfToHalf_32(
            (int, int) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertHalfToFloat_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (float)(Half)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (float)(Half)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertFloatToInt8_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (sbyte)(float)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (sbyte)(float)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertFloatToInt16_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (short)(float)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (short)(float)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertFloatToInt32_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (int, int) result);

            var item1 = (int)(float)value.Item1;
            result.Item1 = (int)item1;
            var item2 = (int)(float)value.Item2;
            result.Item2 = (int)item2;

            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertFloatToUInt8_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (byte)(float)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (byte)(float)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertFloatToUInt16_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (ushort)(float)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (ushort)(float)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertFloatToUInt32_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (uint, uint) result);

            var item1 = (uint)(float)value.Item1;
            result.Item1 = (uint)item1;
            var item2 = (uint)(float)value.Item2;
            result.Item2 = (uint)item2;

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertFloatToHalf_32(
            (int, int) warp)
        {
            var value = CastIToF32(warp);
            Unsafe.SkipInit(out (float, float) result);

            var item1 = (Half)(float)value.Item1;
            result.Item1 = (float)item1;
            var item2 = (Half)(float)value.Item2;
            result.Item2 = (float)item2;

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ConvertFloatToFloat_32(
            (int, int) warp)
        {
            return warp;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ConvertInt64ToInt64_64(
            (long, long) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ConvertInt64ToUInt64_64(
            (long, long) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ConvertInt64ToDouble_64(
            (long, long) warp)
        {
            var value = CastIToI64(warp);
            Unsafe.SkipInit(out (double, double) result);

            var item1 = (double)(long)value.Item1;
            result.Item1 = (double)item1;
            var item2 = (double)(long)value.Item2;
            result.Item2 = (double)item2;

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ConvertUInt64ToInt64_64(
            (long, long) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ConvertUInt64ToUInt64_64(
            (long, long) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ConvertUInt64ToDouble_64(
            (long, long) warp)
        {
            var value = CastIToU64(warp);
            Unsafe.SkipInit(out (double, double) result);

            var item1 = (double)(ulong)value.Item1;
            result.Item1 = (double)item1;
            var item2 = (double)(ulong)value.Item2;
            result.Item2 = (double)item2;

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ConvertDoubleToInt64_64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (long, long) result);

            var item1 = (long)(double)value.Item1;
            result.Item1 = (long)item1;
            var item2 = (long)(double)value.Item2;
            result.Item2 = (long)item2;

            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ConvertDoubleToUInt64_64(
            (long, long) warp)
        {
            var value = CastIToF64(warp);
            Unsafe.SkipInit(out (ulong, ulong) result);

            var item1 = (ulong)(double)value.Item1;
            result.Item1 = (ulong)item1;
            var item2 = (ulong)(double)value.Item2;
            result.Item2 = (ulong)item2;

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) ConvertDoubleToDouble_64(
            (long, long) warp)
        {
            return warp;
        }


        private static readonly Dictionary<
            (ArithmeticBasicValueType, ArithmeticBasicValueType, bool),
            MethodInfo> ConvertOperations = new();

        private static void InitializeConvertOperations()
        {
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int8,
                ArithmeticBasicValueType.Int8,
                false),
                GetMethod(nameof(ConvertInt8ToInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int8,
                ArithmeticBasicValueType.Int16,
                false),
                GetMethod(nameof(ConvertInt8ToInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int8,
                ArithmeticBasicValueType.Int32,
                false),
                GetMethod(nameof(ConvertInt8ToInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int8,
                ArithmeticBasicValueType.UInt8,
                false),
                GetMethod(nameof(ConvertInt8ToUInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int8,
                ArithmeticBasicValueType.UInt16,
                false),
                GetMethod(nameof(ConvertInt8ToUInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int8,
                ArithmeticBasicValueType.UInt32,
                false),
                GetMethod(nameof(ConvertInt8ToUInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int8,
                ArithmeticBasicValueType.Float16,
                false),
                GetMethod(nameof(ConvertInt8ToHalf_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int8,
                ArithmeticBasicValueType.Float32,
                false),
                GetMethod(nameof(ConvertInt8ToFloat_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int16,
                ArithmeticBasicValueType.Int8,
                false),
                GetMethod(nameof(ConvertInt16ToInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int16,
                ArithmeticBasicValueType.Int16,
                false),
                GetMethod(nameof(ConvertInt16ToInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int16,
                ArithmeticBasicValueType.Int32,
                false),
                GetMethod(nameof(ConvertInt16ToInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int16,
                ArithmeticBasicValueType.UInt8,
                false),
                GetMethod(nameof(ConvertInt16ToUInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int16,
                ArithmeticBasicValueType.UInt16,
                false),
                GetMethod(nameof(ConvertInt16ToUInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int16,
                ArithmeticBasicValueType.UInt32,
                false),
                GetMethod(nameof(ConvertInt16ToUInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int16,
                ArithmeticBasicValueType.Float16,
                false),
                GetMethod(nameof(ConvertInt16ToHalf_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int16,
                ArithmeticBasicValueType.Float32,
                false),
                GetMethod(nameof(ConvertInt16ToFloat_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int32,
                ArithmeticBasicValueType.Int8,
                false),
                GetMethod(nameof(ConvertInt32ToInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int32,
                ArithmeticBasicValueType.Int16,
                false),
                GetMethod(nameof(ConvertInt32ToInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int32,
                ArithmeticBasicValueType.Int32,
                false),
                GetMethod(nameof(ConvertInt32ToInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int32,
                ArithmeticBasicValueType.UInt8,
                false),
                GetMethod(nameof(ConvertInt32ToUInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int32,
                ArithmeticBasicValueType.UInt16,
                false),
                GetMethod(nameof(ConvertInt32ToUInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int32,
                ArithmeticBasicValueType.UInt32,
                false),
                GetMethod(nameof(ConvertInt32ToUInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int32,
                ArithmeticBasicValueType.Float16,
                false),
                GetMethod(nameof(ConvertInt32ToHalf_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int32,
                ArithmeticBasicValueType.Float32,
                false),
                GetMethod(nameof(ConvertInt32ToFloat_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt8,
                ArithmeticBasicValueType.Int8,
                false),
                GetMethod(nameof(ConvertUInt8ToInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt8,
                ArithmeticBasicValueType.Int16,
                false),
                GetMethod(nameof(ConvertUInt8ToInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt8,
                ArithmeticBasicValueType.Int32,
                false),
                GetMethod(nameof(ConvertUInt8ToInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt8,
                ArithmeticBasicValueType.UInt8,
                false),
                GetMethod(nameof(ConvertUInt8ToUInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt8,
                ArithmeticBasicValueType.UInt16,
                false),
                GetMethod(nameof(ConvertUInt8ToUInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt8,
                ArithmeticBasicValueType.UInt32,
                false),
                GetMethod(nameof(ConvertUInt8ToUInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt8,
                ArithmeticBasicValueType.Float16,
                false),
                GetMethod(nameof(ConvertUInt8ToHalf_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt8,
                ArithmeticBasicValueType.Float32,
                false),
                GetMethod(nameof(ConvertUInt8ToFloat_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt16,
                ArithmeticBasicValueType.Int8,
                false),
                GetMethod(nameof(ConvertUInt16ToInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt16,
                ArithmeticBasicValueType.Int16,
                false),
                GetMethod(nameof(ConvertUInt16ToInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt16,
                ArithmeticBasicValueType.Int32,
                false),
                GetMethod(nameof(ConvertUInt16ToInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt16,
                ArithmeticBasicValueType.UInt8,
                false),
                GetMethod(nameof(ConvertUInt16ToUInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt16,
                ArithmeticBasicValueType.UInt16,
                false),
                GetMethod(nameof(ConvertUInt16ToUInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt16,
                ArithmeticBasicValueType.UInt32,
                false),
                GetMethod(nameof(ConvertUInt16ToUInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt16,
                ArithmeticBasicValueType.Float16,
                false),
                GetMethod(nameof(ConvertUInt16ToHalf_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt16,
                ArithmeticBasicValueType.Float32,
                false),
                GetMethod(nameof(ConvertUInt16ToFloat_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt32,
                ArithmeticBasicValueType.Int8,
                false),
                GetMethod(nameof(ConvertUInt32ToInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt32,
                ArithmeticBasicValueType.Int16,
                false),
                GetMethod(nameof(ConvertUInt32ToInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt32,
                ArithmeticBasicValueType.Int32,
                false),
                GetMethod(nameof(ConvertUInt32ToInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt32,
                ArithmeticBasicValueType.UInt8,
                false),
                GetMethod(nameof(ConvertUInt32ToUInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt32,
                ArithmeticBasicValueType.UInt16,
                false),
                GetMethod(nameof(ConvertUInt32ToUInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt32,
                ArithmeticBasicValueType.UInt32,
                false),
                GetMethod(nameof(ConvertUInt32ToUInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt32,
                ArithmeticBasicValueType.Float16,
                false),
                GetMethod(nameof(ConvertUInt32ToHalf_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt32,
                ArithmeticBasicValueType.Float32,
                false),
                GetMethod(nameof(ConvertUInt32ToFloat_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float16,
                ArithmeticBasicValueType.Int8,
                false),
                GetMethod(nameof(ConvertHalfToInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float16,
                ArithmeticBasicValueType.Int16,
                false),
                GetMethod(nameof(ConvertHalfToInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float16,
                ArithmeticBasicValueType.Int32,
                false),
                GetMethod(nameof(ConvertHalfToInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float16,
                ArithmeticBasicValueType.UInt8,
                false),
                GetMethod(nameof(ConvertHalfToUInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float16,
                ArithmeticBasicValueType.UInt16,
                false),
                GetMethod(nameof(ConvertHalfToUInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float16,
                ArithmeticBasicValueType.UInt32,
                false),
                GetMethod(nameof(ConvertHalfToUInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float16,
                ArithmeticBasicValueType.Float16,
                false),
                GetMethod(nameof(ConvertHalfToHalf_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float16,
                ArithmeticBasicValueType.Float32,
                false),
                GetMethod(nameof(ConvertHalfToFloat_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float32,
                ArithmeticBasicValueType.Int8,
                false),
                GetMethod(nameof(ConvertFloatToInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float32,
                ArithmeticBasicValueType.Int16,
                false),
                GetMethod(nameof(ConvertFloatToInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float32,
                ArithmeticBasicValueType.Int32,
                false),
                GetMethod(nameof(ConvertFloatToInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float32,
                ArithmeticBasicValueType.UInt8,
                false),
                GetMethod(nameof(ConvertFloatToUInt8_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float32,
                ArithmeticBasicValueType.UInt16,
                false),
                GetMethod(nameof(ConvertFloatToUInt16_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float32,
                ArithmeticBasicValueType.UInt32,
                false),
                GetMethod(nameof(ConvertFloatToUInt32_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float32,
                ArithmeticBasicValueType.Float16,
                false),
                GetMethod(nameof(ConvertFloatToHalf_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float32,
                ArithmeticBasicValueType.Float32,
                false),
                GetMethod(nameof(ConvertFloatToFloat_32)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int64,
                ArithmeticBasicValueType.Int64,
                true),
                GetMethod(nameof(ConvertInt64ToInt64_64)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int64,
                ArithmeticBasicValueType.UInt64,
                true),
                GetMethod(nameof(ConvertInt64ToUInt64_64)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Int64,
                ArithmeticBasicValueType.Float64,
                true),
                GetMethod(nameof(ConvertInt64ToDouble_64)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt64,
                ArithmeticBasicValueType.Int64,
                true),
                GetMethod(nameof(ConvertUInt64ToInt64_64)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt64,
                ArithmeticBasicValueType.UInt64,
                true),
                GetMethod(nameof(ConvertUInt64ToUInt64_64)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.UInt64,
                ArithmeticBasicValueType.Float64,
                true),
                GetMethod(nameof(ConvertUInt64ToDouble_64)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float64,
                ArithmeticBasicValueType.Int64,
                true),
                GetMethod(nameof(ConvertDoubleToInt64_64)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float64,
                ArithmeticBasicValueType.UInt64,
                true),
                GetMethod(nameof(ConvertDoubleToUInt64_64)));
            ConvertOperations.Add(
                (ArithmeticBasicValueType.Float64,
                ArithmeticBasicValueType.Float64,
                true),
                GetMethod(nameof(ConvertDoubleToDouble_64)));
        }

        public static MethodInfo GetConvertOperation32(
            ArithmeticBasicValueType source,
            ArithmeticBasicValueType target) =>
            ConvertOperations[(source, target, false)];

        public static MethodInfo GetConvertOperation64(
            ArithmeticBasicValueType source,
            ArithmeticBasicValueType target) =>
            ConvertOperations[(source, target, true)];

        #endregion

        #region Vector Convert Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) Convert64To32I((long, long) warp)
        {
            Unsafe.SkipInit(out (int, int) result);
            var value = CastIToI64(warp);
            result.Item1 = (int)value.Item1;
            result.Item2 = (int)value.Item2;
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) Convert64To32U((long, long) warp)
        {
            Unsafe.SkipInit(out (uint, uint) result);
            var value = CastIToU64(warp);
            result.Item1 = (uint)value.Item1;
            result.Item2 = (uint)value.Item2;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) Convert64To32F((long, long) warp)
        {
            Unsafe.SkipInit(out (float, float) result);
            var value = CastIToF64(warp);
            result.Item1 = (float)value.Item1;
            result.Item2 = (float)value.Item2;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) Convert32To64I((int, int) warp)
        {
            Unsafe.SkipInit(out (long, long) result);
            var value = CastIToI32(warp);
            result.Item1 = (long)value.Item1;
            result.Item2 = (long)value.Item2;
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) Convert32To64U((int, int) warp)
        {
            Unsafe.SkipInit(out (ulong, ulong) result);
            var value = CastIToU32(warp);
            result.Item1 = (ulong)value.Item1;
            result.Item2 = (ulong)value.Item2;
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (long, long) Convert32To64F((int, int) warp)
        {
            Unsafe.SkipInit(out (double, double) result);
            var value = CastIToF32(warp);
            result.Item1 = (double)value.Item1;
            result.Item2 = (double)value.Item2;
            return CastFToI64(result);
        }

        internal static readonly Dictionary<
            (VelocityWarpOperationMode, bool),
            MethodInfo> VectorConvertOperations = new();

        internal static void InitializeVectorConvertOperations()
        {
            VectorConvertOperations.Add(
                (VelocityWarpOperationMode.I, false),
                GetMethod(nameof(Convert64To32I)));
            VectorConvertOperations.Add(
                (VelocityWarpOperationMode.U, false),
                GetMethod(nameof(Convert64To32U)));
            VectorConvertOperations.Add(
                (VelocityWarpOperationMode.F, false),
                GetMethod(nameof(Convert64To32F)));
            VectorConvertOperations.Add(
                (VelocityWarpOperationMode.I, true),
                GetMethod(nameof(Convert32To64I)));
            VectorConvertOperations.Add(
                (VelocityWarpOperationMode.U, true),
                GetMethod(nameof(Convert32To64U)));
            VectorConvertOperations.Add(
                (VelocityWarpOperationMode.F, true),
                GetMethod(nameof(Convert32To64F)));
        }

        public static MethodInfo GetConvert32To64Operation(
            VelocityWarpOperationMode mode) =>
            VectorConvertOperations[(mode, true)];

        public static MethodInfo GetConvert64To32Operation(
            VelocityWarpOperationMode mode) =>
            VectorConvertOperations[(mode, false)];

        #endregion

        #region Atomic Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicCompareExchange32(
            (int, int) mask,
            (long, long) target,
            (int, int) compare,
            (int, int) value)
        {
            var result = value;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1),
                    compare.Item1,
                    value.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2),
                    compare.Item2,
                    value.Item2);
            }
            return result;
        }

        public static readonly MethodInfo AtomicCompareExchange32Method =
            GetMethod(nameof(AtomicCompareExchange32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicCompareExchange64(
            (int, int) mask,
            (long, long) target,
            (long, long) compare,
            (long, long) value)
        {
            var result = value;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1),
                    compare.Item1,
                    value.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2),
                    compare.Item2,
                    value.Item2);
            }
            return result;
        }

        public static readonly MethodInfo AtomicCompareExchange64Method =
            GetMethod(nameof(AtomicCompareExchange64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicExchangeI32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToI32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicExchangeU32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicExchangeF32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToF32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastFToI32(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicExchangeI64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToI64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicExchangeU64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicExchangeF64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToF64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicAddI32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToI32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicAddU32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicAddF32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToF32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastFToI32(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicAddI64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToI64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicAddU64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicAddF64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToF64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicMaxI32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToI32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicMaxU32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicMaxF32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToF32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastFToI32(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicMaxI64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToI64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicMaxU64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicMaxF64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToF64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicMinI32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToI32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastIToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicMinU32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicMinF32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToF32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastFToI32(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicMinI64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToI64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastIToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicMinU64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicMinF64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToF64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicAndI32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicAndU32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicAndF32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicAndI64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicAndU64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicAndF64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicOrI32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicOrU32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicOrF32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicOrI64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicOrU64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicOrF64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicXorI32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicXorU32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) AtomicXorF32(
            (int, int) mask,
            (long, long) target,
            (int, int) value)
        {
            var sourceValue = CastIToU32(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI32(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicXorI64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicXorU64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) AtomicXorF64(
            (int, int) mask,
            (long, long) target,
            (long, long) value)
        {
            var sourceValue = CastIToU64(value);
            var result = sourceValue;
            if (mask.Item1 != 0)
            {
                result.Item1 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1),
                    sourceValue.Item1);
            }
            if (mask.Item2 != 0)
            {
                result.Item2 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2),
                    sourceValue.Item2);
            }
            return CastUToI64(result);
        }


        internal static readonly Dictionary<
            (AtomicKind, VelocityWarpOperationMode, bool),
            MethodInfo> AtomicOperations = new();

        internal static void InitializeAtomicOperations()
        {
            AtomicOperations.Add(
                (AtomicKind.Exchange, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(AtomicExchangeI32)));
            AtomicOperations.Add(
                (AtomicKind.Exchange, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(AtomicExchangeU32)));
            AtomicOperations.Add(
                (AtomicKind.Exchange, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(AtomicExchangeF32)));
            AtomicOperations.Add(
                (AtomicKind.Exchange, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(AtomicExchangeI64)));
            AtomicOperations.Add(
                (AtomicKind.Exchange, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(AtomicExchangeU64)));
            AtomicOperations.Add(
                (AtomicKind.Exchange, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(AtomicExchangeF64)));
            AtomicOperations.Add(
                (AtomicKind.Add, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(AtomicAddI32)));
            AtomicOperations.Add(
                (AtomicKind.Add, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(AtomicAddU32)));
            AtomicOperations.Add(
                (AtomicKind.Add, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(AtomicAddF32)));
            AtomicOperations.Add(
                (AtomicKind.Add, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(AtomicAddI64)));
            AtomicOperations.Add(
                (AtomicKind.Add, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(AtomicAddU64)));
            AtomicOperations.Add(
                (AtomicKind.Add, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(AtomicAddF64)));
            AtomicOperations.Add(
                (AtomicKind.Max, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(AtomicMaxI32)));
            AtomicOperations.Add(
                (AtomicKind.Max, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(AtomicMaxU32)));
            AtomicOperations.Add(
                (AtomicKind.Max, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(AtomicMaxF32)));
            AtomicOperations.Add(
                (AtomicKind.Max, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(AtomicMaxI64)));
            AtomicOperations.Add(
                (AtomicKind.Max, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(AtomicMaxU64)));
            AtomicOperations.Add(
                (AtomicKind.Max, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(AtomicMaxF64)));
            AtomicOperations.Add(
                (AtomicKind.Min, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(AtomicMinI32)));
            AtomicOperations.Add(
                (AtomicKind.Min, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(AtomicMinU32)));
            AtomicOperations.Add(
                (AtomicKind.Min, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(AtomicMinF32)));
            AtomicOperations.Add(
                (AtomicKind.Min, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(AtomicMinI64)));
            AtomicOperations.Add(
                (AtomicKind.Min, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(AtomicMinU64)));
            AtomicOperations.Add(
                (AtomicKind.Min, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(AtomicMinF64)));
            AtomicOperations.Add(
                (AtomicKind.And, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(AtomicAndI32)));
            AtomicOperations.Add(
                (AtomicKind.And, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(AtomicAndU32)));
            AtomicOperations.Add(
                (AtomicKind.And, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(AtomicAndF32)));
            AtomicOperations.Add(
                (AtomicKind.And, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(AtomicAndI64)));
            AtomicOperations.Add(
                (AtomicKind.And, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(AtomicAndU64)));
            AtomicOperations.Add(
                (AtomicKind.And, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(AtomicAndF64)));
            AtomicOperations.Add(
                (AtomicKind.Or, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(AtomicOrI32)));
            AtomicOperations.Add(
                (AtomicKind.Or, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(AtomicOrU32)));
            AtomicOperations.Add(
                (AtomicKind.Or, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(AtomicOrF32)));
            AtomicOperations.Add(
                (AtomicKind.Or, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(AtomicOrI64)));
            AtomicOperations.Add(
                (AtomicKind.Or, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(AtomicOrU64)));
            AtomicOperations.Add(
                (AtomicKind.Or, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(AtomicOrF64)));
            AtomicOperations.Add(
                (AtomicKind.Xor, VelocityWarpOperationMode.I, false),
                GetMethod(nameof(AtomicXorI32)));
            AtomicOperations.Add(
                (AtomicKind.Xor, VelocityWarpOperationMode.U, false),
                GetMethod(nameof(AtomicXorU32)));
            AtomicOperations.Add(
                (AtomicKind.Xor, VelocityWarpOperationMode.F, false),
                GetMethod(nameof(AtomicXorF32)));
            AtomicOperations.Add(
                (AtomicKind.Xor, VelocityWarpOperationMode.I, true),
                GetMethod(nameof(AtomicXorI64)));
            AtomicOperations.Add(
                (AtomicKind.Xor, VelocityWarpOperationMode.U, true),
                GetMethod(nameof(AtomicXorU64)));
            AtomicOperations.Add(
                (AtomicKind.Xor, VelocityWarpOperationMode.F, true),
                GetMethod(nameof(AtomicXorF64)));
        }

        public static MethodInfo GetAtomicOperation32(
            AtomicKind kind,
            VelocityWarpOperationMode mode) =>
            AtomicOperations[(kind, mode, false)];

        public static MethodInfo GetAtomicOperation64(
            AtomicKind kind,
            VelocityWarpOperationMode mode) =>
            AtomicOperations[(kind, mode, true)];

        #endregion

        #region Thread Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) BarrierPopCount32(
            (int, int) mask,
            (int, int) warp)
        {
            int count = 0;
            count += mask.Item1 != 0 ? (warp.Item1 != 0 ? 1 : 0) : 0;
            count += mask.Item2 != 0 ? (warp.Item2 != 0 ? 1 : 0) : 0;
            return FromScalarI32(count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) BarrierAnd32(
            (int, int) mask,
            (int, int) warp)
        {
            int andMask = 1;
            andMask &= mask.Item1 != 0 ? warp.Item1 : 0;
            andMask &= mask.Item2 != 0 ? warp.Item2 : 0;
            return FromScalarI32(andMask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) BarrierOr32(
            (int, int) mask,
            (int, int) warp)
        {
            int orMask = 0;
            orMask |= mask.Item1 != 0 ? warp.Item1 : 0;
            orMask |= mask.Item2 != 0 ? warp.Item2 : 0;
            return FromScalarI32(orMask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetShuffledLane32(
            (int, int) value,
            int sourceLane)
        {
            switch (sourceLane)
            {
            case 0:
                return value.Item1;
            default:
                return value.Item2;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) Shuffle32(
            (int, int) mask,
            (int, int) value,
            (int, int) sourceLanes)
        {
            // Mask is unused at the moment
            Unsafe.SkipInit(out (int, int) result);
            result.Item1 = mask.Item1 != 0
                ? GetShuffledLane32(value, sourceLanes.Item1)
                : value.Item1;
            result.Item2 = mask.Item2 != 0
                ? GetShuffledLane32(value, sourceLanes.Item2)
                : value.Item2;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void ComputeShuffleConfig(
            (int, int) width,
            out (int, int) lane,
            out (int, int) offset)
        {
            lane = RemI32(LoadLaneIndexVector32(), width);
            offset = MulI32(DivI32(lane, width), width);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ShuffleUp32(
            (int, int) mask,
            (int, int) warp,
            (int, int) delta)
        {
            var lane = SubI32(LoadLaneIndexVector32(), delta);
            return Shuffle32(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SubShuffleUp32(
            (int, int) mask,
            (int, int) warp,
            (int, int) delta,
            (int, int) width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = SubI32(lane, delta);
            return Shuffle32(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ShuffleDown32(
            (int, int) mask,
            (int, int) warp,
            (int, int) delta)
        {
            var lane = AddI32(LoadLaneIndexVector32(), delta);
            return Shuffle32(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SubShuffleDown32(
            (int, int) mask,
            (int, int) warp,
            (int, int) delta,
            (int, int) width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = AddI32(lane, delta);
            return Shuffle32(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) ShuffleXor32(
            (int, int) mask,
            (int, int) warp,
            (int, int) laneMask)
        {
            var lane = XorU32(LoadLaneIndexVector32(), laneMask);
            return Shuffle32(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (int, int) SubShuffleXor32(
            (int, int) mask,
            (int, int) warp,
            (int, int) laneMask,
            (int, int) width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = XorU32(lane, laneMask);
            return Shuffle32(mask, warp, AddI32(adjustedLane, offset));
        }

        public static readonly MethodInfo BarrierPopCount32Method =
            GetMethod(nameof(BarrierPopCount32));
        public static readonly MethodInfo BarrierAnd32Method =
            GetMethod(nameof(BarrierAnd32));
        public static readonly MethodInfo BarrierOr32Method =
            GetMethod(nameof(BarrierOr32));
        public static readonly MethodInfo Shuffle32Method =
            GetMethod(nameof(Shuffle32));
        public static readonly MethodInfo ShuffleUp32Method =
            GetMethod(nameof(ShuffleUp32));
        public static readonly MethodInfo SubShuffleUp32Method =
            GetMethod(nameof(SubShuffleUp32));
        public static readonly MethodInfo ShuffleDown32Method =
            GetMethod(nameof(ShuffleDown32));
        public static readonly MethodInfo SubShuffleDown32Method =
            GetMethod(nameof(SubShuffleDown32));
        public static readonly MethodInfo ShuffleXor32Method =
            GetMethod(nameof(ShuffleXor32));
        public static readonly MethodInfo SubShuffleXor32Method =
            GetMethod(nameof(SubShuffleXor32));

        #endregion

        #region IO

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) Load8(
            (int, int) mask,
            (long, long) address)
        {
            Unsafe.SkipInit(out (int, int) result);
            result.Item1 = mask.Item1 != 0
                ? Unsafe.AsRef<byte>((void*)address.Item1)
                : 0;
            result.Item2 = mask.Item2 != 0
                ? Unsafe.AsRef<byte>((void*)address.Item2)
                : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) Load16(
            (int, int) mask,
            (long, long) address)
        {
            Unsafe.SkipInit(out (int, int) result);
            result.Item1 = mask.Item1 != 0
                ? Unsafe.AsRef<ushort>((void*)address.Item1)
                : 0;
            result.Item2 = mask.Item2 != 0
                ? Unsafe.AsRef<ushort>((void*)address.Item2)
                : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (int, int) Load32(
            (int, int) mask,
            (long, long) address)
        {
            Unsafe.SkipInit(out (int, int) result);
            result.Item1 = mask.Item1 != 0
                ? Unsafe.AsRef<int>((void*)address.Item1)
                : 0;
            result.Item2 = mask.Item2 != 0
                ? Unsafe.AsRef<int>((void*)address.Item2)
                : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (long, long) Load64(
            (int, int) mask,
            (long, long) address)
        {
            Unsafe.SkipInit(out (long, long) result);
            result.Item1 = mask.Item1 != 0
                ? Unsafe.AsRef<long>((void*)address.Item1)
                : 0;
            result.Item2 = mask.Item2 != 0
                ? Unsafe.AsRef<long>((void*)address.Item2)
                : 0;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store8(
            (int, int) mask,
            (long, long) address,
            (int, int) value)
        {
            if (mask.Item1 != 0)
                Unsafe.AsRef<byte>((void*)address.Item1) = (byte)(value.Item1 & 0xff);
            if (mask.Item2 != 0)
                Unsafe.AsRef<byte>((void*)address.Item2) = (byte)(value.Item2 & 0xff);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store16(
            (int, int) mask,
            (long, long) address,
            (int, int) value)
        {
            if (mask.Item1 != 0)
                Unsafe.AsRef<ushort>((void*)address.Item1) = (ushort)(value.Item1 & 0xffff);
            if (mask.Item2 != 0)
                Unsafe.AsRef<ushort>((void*)address.Item2) = (ushort)(value.Item2 & 0xffff);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store32(
            (int, int) mask,
            (long, long) address,
            (int, int) value)
        {
            if (mask.Item1 != 0)
                Unsafe.AsRef<int>((void*)address.Item1) = value.Item1;
            if (mask.Item2 != 0)
                Unsafe.AsRef<int>((void*)address.Item2) = value.Item2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store64(
            (int, int) mask,
            (long, long) address,
            (long, long) value)
        {
            if (mask.Item1 != 0)
                Unsafe.AsRef<long>((void*)address.Item1) = value.Item1;
            if (mask.Item2 != 0)
                Unsafe.AsRef<long>((void*)address.Item2) = value.Item2;
        }

        public static readonly MethodInfo Load8Method =
            GetMethod(nameof(Load8));
        public static readonly MethodInfo Load16Method =
            GetMethod(nameof(Load16));
        public static readonly MethodInfo Load32Method =
            GetMethod(nameof(Load32));
        public static readonly MethodInfo Load64Method =
            GetMethod(nameof(Load64));

        public static readonly MethodInfo Store8Method =
            GetMethod(nameof(Store8));
        public static readonly MethodInfo Store16Method =
            GetMethod(nameof(Store16));
        public static readonly MethodInfo Store32Method =
            GetMethod(nameof(Store32));
        public static readonly MethodInfo Store64Method =
            GetMethod(nameof(Store64));

        #endregion

        #region Misc

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void DebugAssertFailed(
            (int, int) mask,
            (int, int) value,
            string message,
            string fileName,
            int line,
            string method)
        {
            // Check if any lane failed the check
            var failedAssertionMask = XorU32(FromScalarU32(1), value);
            if (BarrierOr32(mask, failedAssertionMask).Item1 != 0)
                Trace.Assert(false, message, $"@ {fileName}:{line} in {method}");
        }

        public static readonly MethodInfo DebugAssertFailedMethod =
            GetMethod(nameof(DebugAssertFailed));

        [SuppressMessage(
            "Globalization",
            "CA1303:Do not pass literals as localized parameters",
            Justification = "Basic invariant string")]
        internal static void DumpWarp32((int, int) value, string label)
        {
            Console.Write(label);
            Console.Write(value.Item1);
            Console.Write(", ");
            Console.Write(value.Item2);
            Console.WriteLine();
        }

        public static readonly MethodInfo DumpWarp32Method =
            GetMethod(nameof(DumpWarp32));

        [SuppressMessage(
            "Globalization",
            "CA1303:Do not pass literals as localized parameters",
            Justification = "Basic invariant string")]
        internal static void DumpWarp64((long, long) value, string label)
        {
            Console.Write(label);
            Console.Write(value.Item1);
            Console.Write(", ");
            Console.Write(value.Item2);
            Console.WriteLine();
        }

        public static readonly MethodInfo DumpWarp64Method =
            GetMethod(nameof(DumpWarp64));

        #endregion
    }
}