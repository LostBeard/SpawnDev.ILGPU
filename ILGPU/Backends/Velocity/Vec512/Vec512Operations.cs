// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: Vec512Operations.tt/Vec512Operations.cs
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
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

// ReSharper disable ArrangeMethodOrOperatorBody
// ReSharper disable RedundantCast
// disable: max_line_length

#if NET7_0_OR_GREATER

namespace ILGPU.Backends.Velocity.Vec512
{
    // Operation implementations

    static partial class Vec512Operations
    {
        #region Warp Types

        public static int WarpSize => Vector512<int>.Count;
        public static readonly Type WarpType32 = typeof(Vector512<int>);
        public static readonly Type WarpType64 = typeof((Vector512<long>, Vector512<long>));

        #endregion

        #region Initialization

        static Vec512Operations()
        {
            InitUnaryOperations();
            InitBinaryOperations();
            InitTernaryOperations();
            InitializeCompareOperations();
            InitializeConvertOperations();
            InitializeVectorConvertOperations();
            InitializeAtomicOperations();
        }

        private static readonly Vector512<int> WarpSizeM1Vector =
            Vector512.Create(WarpSize - 1);

        internal static MethodInfo GetMethod(string name) =>
            typeof(Vec512Operations).GetMethod(
                    name,
                    BindingFlags.NonPublic | BindingFlags.Static)
                .AsNotNull();

        #endregion

        #region Creation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<TTarget> CastWarp32<T, TTarget>(Vector512<T> source)
            where T : struct
            where TTarget : struct =>
            source.As<T, TTarget>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<TTarget>, Vector512<TTarget>) CastWarp64<T, TTarget>(
            (Vector512<T>, Vector512<T>) source)
            where T : struct
            where TTarget : struct =>
            (source.Item1.As<T, TTarget>(), source.Item2.As<T, TTarget>());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MaskTo64(Vector512<int> mask) =>
            Vector512.Widen(mask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CheckForAnyActiveLane(Vector512<int> warp) =>
            Vector512.EqualsAny(Vector512<int>.AllBitsSet, warp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CheckForNoActiveLane(Vector512<int> warp) =>
            !CheckForAnyActiveLane(warp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CheckForEqualMasks(
            Vector512<int> firstMask,
            Vector512<int> secondMask) =>
            Vector512.EqualsAll(firstMask, secondMask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetNumberOfActiveLanes(Vector512<int> warp) =>
            -Vector512.Sum(warp);

        public static readonly MethodInfo CheckForAnyActiveLaneMethod =
            GetMethod(nameof(CheckForAnyActiveLane));
        public static readonly MethodInfo CheckForNoActiveLaneMethod =
            GetMethod(nameof(CheckForNoActiveLane));
        public static readonly MethodInfo CheckForEqualMasksMethod =
            GetMethod(nameof(CheckForEqualMasks));
        public static readonly MethodInfo GetNumberOfActiveLanesMethod =
            GetMethod(nameof(GetNumberOfActiveLanes));

        private static readonly Vector512<int> LaneIndexVector32 =
            Vector512.Create(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> LoadLaneIndexVector32() => LaneIndexVector32;

        private static readonly (Vector512<long>, Vector512<long>) LaneIndexVector64 =
            (Vector512.Create(0L, 1L, 2L, 3L, 4L, 5L, 6L, 7L), Vector512.Create(8L, 9L, 10L, 11L, 12L, 13L, 14L, 15L));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) LoadLaneIndexVector64() => LaneIndexVector64;

        public static readonly MethodInfo LoadLaneIndexVector32Method =
            GetMethod(nameof(LoadLaneIndexVector32));
        public static readonly MethodInfo LoadLaneIndexVector64Method =
            GetMethod(nameof(LoadLaneIndexVector64));

        private static readonly Vector512<int> LaneLengthVector32 =
            Vector512.Create(WarpSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> LoadVectorLengthVector32() => LaneLengthVector32;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) LoadVectorLengthVector64()
        {
            long count = WarpSize;
            return (Vector512.Create(count), Vector512.Create(count));
        }

        public static readonly MethodInfo LoadVectorLengthVector32Method =
            GetMethod(nameof(LoadVectorLengthVector32));
        public static readonly MethodInfo LoadVectorLengthVector64Method =
            GetMethod(nameof(LoadVectorLengthVector64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> LoadAllLanesMask32() =>
            Vector512<int>.AllBitsSet;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) LoadAllLanesMask64() =>
            (Vector512<long>.AllBitsSet, Vector512<long>.AllBitsSet);

        public static readonly MethodInfo LoadAllLanesMask32Method =
            GetMethod(nameof(LoadAllLanesMask32));
        public static readonly MethodInfo LoadAllLanesMask64Method =
            GetMethod(nameof(LoadAllLanesMask64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> LoadNoLanesMask32() => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) LoadNoLanesMask64() => default;

        public static readonly MethodInfo LoadNoLanesMask32Method =
            GetMethod(nameof(LoadNoLanesMask32));
        public static readonly MethodInfo LoadNoLanesMask64Method =
            GetMethod(nameof(LoadNoLanesMask64));

        #endregion

        #region Generic Casts

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CastIToI32(
            Vector512<int> input) =>
            CastWarp32<int, int>(input);

        public static readonly MethodInfo CastIToI32Method =
            GetMethod(nameof(CastIToI32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CastUToI32(
            Vector512<uint> input) =>
            CastWarp32<uint, int>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<uint> CastIToU32(
            Vector512<int> input) =>
            CastWarp32<int, uint>(input);

        public static readonly MethodInfo CastUToI32Method =
            GetMethod(nameof(CastUToI32));

        public static readonly MethodInfo CastIToU32Method =
            GetMethod(nameof(CastIToU32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CastFToI32(
            Vector512<float> input) =>
            CastWarp32<float, int>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<float> CastIToF32(
            Vector512<int> input) =>
            CastWarp32<int, float>(input);

        public static readonly MethodInfo CastFToI32Method =
            GetMethod(nameof(CastFToI32));

        public static readonly MethodInfo CastIToF32Method =
            GetMethod(nameof(CastIToF32));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) CastIToI64(
            (Vector512<long>, Vector512<long>) input) =>
            CastWarp64<long, long>(input);


        public static readonly MethodInfo CastIToI64Method =
            GetMethod(nameof(CastIToI64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) CastUToI64(
            (Vector512<ulong>, Vector512<ulong>) input) =>
            CastWarp64<ulong, long>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<ulong>, Vector512<ulong>) CastIToU64(
            (Vector512<long>, Vector512<long>) input) =>
            CastWarp64<long, ulong>(input);

        public static readonly MethodInfo CastUToI64Method =
            GetMethod(nameof(CastUToI64));

        public static readonly MethodInfo CastIToU64Method =
            GetMethod(nameof(CastIToU64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) CastFToI64(
            (Vector512<double>, Vector512<double>) input) =>
            CastWarp64<double, long>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<double>, Vector512<double>) CastIToF64(
            (Vector512<long>, Vector512<long>) input) =>
            CastWarp64<long, double>(input);

        public static readonly MethodInfo CastFToI64Method =
            GetMethod(nameof(CastFToI64));

        public static readonly MethodInfo CastIToF64Method =
            GetMethod(nameof(CastIToF64));


        #endregion

        #region Scalar Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> FromScalarI32(int scalar)
        {
            var result = Vector512.Create(scalar);
            return result;
        }

        public static readonly MethodInfo FromScalarI32Method =
            GetMethod(nameof(FromScalarI32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> FromScalarU32(uint scalar)
        {
            var result = Vector512.Create(scalar);
            return CastUToI32(result);
        }

        public static readonly MethodInfo FromScalarU32Method =
            GetMethod(nameof(FromScalarU32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> FromScalarF32(float scalar)
        {
            var result = Vector512.Create(scalar);
            return CastFToI32(result);
        }

        public static readonly MethodInfo FromScalarF32Method =
            GetMethod(nameof(FromScalarF32));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) FromScalarI64(long scalar)
        {
            var result = Vector512.Create(scalar);
            return (result, result);
        }

        public static readonly MethodInfo FromScalarI64Method =
            GetMethod(nameof(FromScalarI64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) FromScalarU64(ulong scalar)
        {
            var result = Vector512.Create(scalar);
            return CastUToI64((result, result));
        }

        public static readonly MethodInfo FromScalarU64Method =
            GetMethod(nameof(FromScalarU64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) FromScalarF64(double scalar)
        {
            var result = Vector512.Create(scalar);
            return CastFToI64((result, result));
        }

        public static readonly MethodInfo FromScalarF64Method =
            GetMethod(nameof(FromScalarF64));


        #endregion

        #region Select Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> Select32(
            Vector512<int> mask,
            Vector512<int> left,
            Vector512<int> right) =>
            Vector512.ConditionalSelect(mask, right, left);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Select64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) left,
            (Vector512<long>, Vector512<long>) right)
        {
            var mask64 = MaskTo64(mask);
            return (
                Vector512.ConditionalSelect(mask64.Item1, right.Item1, left.Item1),
                Vector512.ConditionalSelect(mask64.Item2, right.Item2, left.Item2));
        }

        public static readonly MethodInfo Select32Method = GetMethod(nameof(Select32));
        public static readonly MethodInfo Select64Method = GetMethod(nameof(Select64));

        #endregion

        #region Unary Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> NegI32(
            Vector512<int> warp)
        {
            var value = warp;
            var result = -value;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> NegU32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            var result = -value;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> NegF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = -value;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) NegI64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = warp;
            var result = (
                    -value.Item1,
                    -value.Item2);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) NegU64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = (
                    -value.Item1,
                    -value.Item2);
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) NegF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    -value.Item1,
                    -value.Item2);
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> NotI32(
            Vector512<int> warp)
        {
            var value = warp;
            var result = ~value;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> NotU32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            var result = ~value;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) NotI64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = warp;
            var result = (
                    ~value.Item1,
                    ~value.Item2);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) NotU64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = (
                    ~value.Item1,
                    ~value.Item2);
            return CastUToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AbsI32(
            Vector512<int> warp)
        {
            var value = warp;
            var result = Vector512.Abs(value);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AbsU32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector512.Abs(value);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AbsF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Abs(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AbsI64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = warp;
            var result = (
                    Vector512.Abs(value.Item1),
                    Vector512.Abs(value.Item2));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AbsU64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = (
                    Vector512.Abs(value.Item1),
                    Vector512.Abs(value.Item2));
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AbsF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    Vector512.Abs(value.Item1),
                    Vector512.Abs(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> PopCI32(
            Vector512<int> warp)
        {
            var value = warp;
            var result = Vector512.Create(IntrinsicMath.BitOperations.PopCount(value.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.GetElement(2)), IntrinsicMath.BitOperations.PopCount(value.GetElement(3)), IntrinsicMath.BitOperations.PopCount(value.GetElement(4)), IntrinsicMath.BitOperations.PopCount(value.GetElement(5)), IntrinsicMath.BitOperations.PopCount(value.GetElement(6)), IntrinsicMath.BitOperations.PopCount(value.GetElement(7)), IntrinsicMath.BitOperations.PopCount(value.GetElement(8)), IntrinsicMath.BitOperations.PopCount(value.GetElement(9)), IntrinsicMath.BitOperations.PopCount(value.GetElement(10)), IntrinsicMath.BitOperations.PopCount(value.GetElement(11)), IntrinsicMath.BitOperations.PopCount(value.GetElement(12)), IntrinsicMath.BitOperations.PopCount(value.GetElement(13)), IntrinsicMath.BitOperations.PopCount(value.GetElement(14)), IntrinsicMath.BitOperations.PopCount(value.GetElement(15)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> PopCU32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector512.Create(IntrinsicMath.BitOperations.PopCount(value.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.GetElement(2)), IntrinsicMath.BitOperations.PopCount(value.GetElement(3)), IntrinsicMath.BitOperations.PopCount(value.GetElement(4)), IntrinsicMath.BitOperations.PopCount(value.GetElement(5)), IntrinsicMath.BitOperations.PopCount(value.GetElement(6)), IntrinsicMath.BitOperations.PopCount(value.GetElement(7)), IntrinsicMath.BitOperations.PopCount(value.GetElement(8)), IntrinsicMath.BitOperations.PopCount(value.GetElement(9)), IntrinsicMath.BitOperations.PopCount(value.GetElement(10)), IntrinsicMath.BitOperations.PopCount(value.GetElement(11)), IntrinsicMath.BitOperations.PopCount(value.GetElement(12)), IntrinsicMath.BitOperations.PopCount(value.GetElement(13)), IntrinsicMath.BitOperations.PopCount(value.GetElement(14)), IntrinsicMath.BitOperations.PopCount(value.GetElement(15)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> PopCI64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = warp;
            var result = Vector512.Create(IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(2)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(3)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(4)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(5)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(6)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(7)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(2)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(3)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(4)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(5)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(6)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(7)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> PopCU64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = Vector512.Create(IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(2)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(3)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(4)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(5)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(6)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(7)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(2)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(3)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(4)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(5)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(6)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(7)));
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CLZI32(
            Vector512<int> warp)
        {
            var value = warp;
            var result = Vector512.Create(IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(2)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(3)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(4)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(5)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(6)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(7)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(8)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(9)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(10)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(11)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(12)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(13)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(14)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(15)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CLZU32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector512.Create(IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(2)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(3)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(4)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(5)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(6)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(7)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(8)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(9)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(10)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(11)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(12)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(13)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(14)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(15)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CLZI64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = warp;
            var result = Vector512.Create(IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(2)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(3)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(4)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(5)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(6)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(7)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(2)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(3)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(4)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(5)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(6)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(7)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CLZU64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = Vector512.Create(IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(2)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(3)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(4)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(5)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(6)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(7)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(2)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(3)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(4)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(5)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(6)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(7)));
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CTZI32(
            Vector512<int> warp)
        {
            var value = warp;
            var result = Vector512.Create(IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(2)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(3)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(4)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(5)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(6)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(7)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(8)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(9)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(10)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(11)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(12)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(13)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(14)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(15)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CTZU32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector512.Create(IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(2)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(3)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(4)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(5)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(6)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(7)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(8)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(9)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(10)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(11)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(12)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(13)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(14)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(15)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CTZI64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = warp;
            var result = Vector512.Create(IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(2)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(3)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(4)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(5)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(6)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(7)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(2)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(3)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(4)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(5)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(6)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(7)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CTZU64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = Vector512.Create(IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(2)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(3)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(4)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(5)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(6)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(7)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(2)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(3)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(4)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(5)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(6)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(7)));
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> RcpFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = RcpImpl(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) RcpFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    RcpImpl(value.Item1),
                    RcpImpl(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> IsNaNFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.IsNaN(value.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(3)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(4)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(5)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(6)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(7)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(8)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(9)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(10)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(11)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(12)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(13)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(14)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(15)) ? -1 : 0);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> IsNaNFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(3)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(4)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(5)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(6)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(7)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(3)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(4)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(5)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(6)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(7)) ? -1 : 0);
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> IsInfFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(3)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(4)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(5)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(6)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(7)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(8)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(9)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(10)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(11)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(12)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(13)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(14)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(15)) ? -1 : 0);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> IsInfFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(3)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(4)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(5)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(6)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(7)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(3)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(4)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(5)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(6)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(7)) ? -1 : 0);
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> IsFinFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.IsFinite(value.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(3)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(4)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(5)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(6)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(7)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(8)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(9)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(10)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(11)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(12)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(13)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(14)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(15)) ? -1 : 0);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> IsFinFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(3)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(4)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(5)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(6)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(7)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(3)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(4)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(5)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(6)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(7)) ? -1 : 0);
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SqrtFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Sqrt(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SqrtFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    Vector512.Sqrt(value.Item1),
                    Vector512.Sqrt(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> RsqrtFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = RcpImpl(Vector512.Sqrt(value));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) RsqrtFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    RcpImpl(Vector512.Sqrt(value.Item1)),
                    RcpImpl(Vector512.Sqrt(value.Item2)));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AsinFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Asin(value.GetElement(0)), IntrinsicMath.CPUOnly.Asin(value.GetElement(1)), IntrinsicMath.CPUOnly.Asin(value.GetElement(2)), IntrinsicMath.CPUOnly.Asin(value.GetElement(3)), IntrinsicMath.CPUOnly.Asin(value.GetElement(4)), IntrinsicMath.CPUOnly.Asin(value.GetElement(5)), IntrinsicMath.CPUOnly.Asin(value.GetElement(6)), IntrinsicMath.CPUOnly.Asin(value.GetElement(7)), IntrinsicMath.CPUOnly.Asin(value.GetElement(8)), IntrinsicMath.CPUOnly.Asin(value.GetElement(9)), IntrinsicMath.CPUOnly.Asin(value.GetElement(10)), IntrinsicMath.CPUOnly.Asin(value.GetElement(11)), IntrinsicMath.CPUOnly.Asin(value.GetElement(12)), IntrinsicMath.CPUOnly.Asin(value.GetElement(13)), IntrinsicMath.CPUOnly.Asin(value.GetElement(14)), IntrinsicMath.CPUOnly.Asin(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AsinFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SinFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Sin(value.GetElement(0)), IntrinsicMath.CPUOnly.Sin(value.GetElement(1)), IntrinsicMath.CPUOnly.Sin(value.GetElement(2)), IntrinsicMath.CPUOnly.Sin(value.GetElement(3)), IntrinsicMath.CPUOnly.Sin(value.GetElement(4)), IntrinsicMath.CPUOnly.Sin(value.GetElement(5)), IntrinsicMath.CPUOnly.Sin(value.GetElement(6)), IntrinsicMath.CPUOnly.Sin(value.GetElement(7)), IntrinsicMath.CPUOnly.Sin(value.GetElement(8)), IntrinsicMath.CPUOnly.Sin(value.GetElement(9)), IntrinsicMath.CPUOnly.Sin(value.GetElement(10)), IntrinsicMath.CPUOnly.Sin(value.GetElement(11)), IntrinsicMath.CPUOnly.Sin(value.GetElement(12)), IntrinsicMath.CPUOnly.Sin(value.GetElement(13)), IntrinsicMath.CPUOnly.Sin(value.GetElement(14)), IntrinsicMath.CPUOnly.Sin(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SinFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SinhFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Sinh(value.GetElement(0)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(1)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(2)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(3)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(4)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(5)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(6)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(7)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(8)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(9)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(10)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(11)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(12)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(13)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(14)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SinhFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AcosFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Acos(value.GetElement(0)), IntrinsicMath.CPUOnly.Acos(value.GetElement(1)), IntrinsicMath.CPUOnly.Acos(value.GetElement(2)), IntrinsicMath.CPUOnly.Acos(value.GetElement(3)), IntrinsicMath.CPUOnly.Acos(value.GetElement(4)), IntrinsicMath.CPUOnly.Acos(value.GetElement(5)), IntrinsicMath.CPUOnly.Acos(value.GetElement(6)), IntrinsicMath.CPUOnly.Acos(value.GetElement(7)), IntrinsicMath.CPUOnly.Acos(value.GetElement(8)), IntrinsicMath.CPUOnly.Acos(value.GetElement(9)), IntrinsicMath.CPUOnly.Acos(value.GetElement(10)), IntrinsicMath.CPUOnly.Acos(value.GetElement(11)), IntrinsicMath.CPUOnly.Acos(value.GetElement(12)), IntrinsicMath.CPUOnly.Acos(value.GetElement(13)), IntrinsicMath.CPUOnly.Acos(value.GetElement(14)), IntrinsicMath.CPUOnly.Acos(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AcosFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CosFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Cos(value.GetElement(0)), IntrinsicMath.CPUOnly.Cos(value.GetElement(1)), IntrinsicMath.CPUOnly.Cos(value.GetElement(2)), IntrinsicMath.CPUOnly.Cos(value.GetElement(3)), IntrinsicMath.CPUOnly.Cos(value.GetElement(4)), IntrinsicMath.CPUOnly.Cos(value.GetElement(5)), IntrinsicMath.CPUOnly.Cos(value.GetElement(6)), IntrinsicMath.CPUOnly.Cos(value.GetElement(7)), IntrinsicMath.CPUOnly.Cos(value.GetElement(8)), IntrinsicMath.CPUOnly.Cos(value.GetElement(9)), IntrinsicMath.CPUOnly.Cos(value.GetElement(10)), IntrinsicMath.CPUOnly.Cos(value.GetElement(11)), IntrinsicMath.CPUOnly.Cos(value.GetElement(12)), IntrinsicMath.CPUOnly.Cos(value.GetElement(13)), IntrinsicMath.CPUOnly.Cos(value.GetElement(14)), IntrinsicMath.CPUOnly.Cos(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) CosFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CoshFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Cosh(value.GetElement(0)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(1)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(2)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(3)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(4)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(5)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(6)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(7)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(8)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(9)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(10)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(11)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(12)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(13)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(14)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) CoshFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> TanFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Tan(value.GetElement(0)), IntrinsicMath.CPUOnly.Tan(value.GetElement(1)), IntrinsicMath.CPUOnly.Tan(value.GetElement(2)), IntrinsicMath.CPUOnly.Tan(value.GetElement(3)), IntrinsicMath.CPUOnly.Tan(value.GetElement(4)), IntrinsicMath.CPUOnly.Tan(value.GetElement(5)), IntrinsicMath.CPUOnly.Tan(value.GetElement(6)), IntrinsicMath.CPUOnly.Tan(value.GetElement(7)), IntrinsicMath.CPUOnly.Tan(value.GetElement(8)), IntrinsicMath.CPUOnly.Tan(value.GetElement(9)), IntrinsicMath.CPUOnly.Tan(value.GetElement(10)), IntrinsicMath.CPUOnly.Tan(value.GetElement(11)), IntrinsicMath.CPUOnly.Tan(value.GetElement(12)), IntrinsicMath.CPUOnly.Tan(value.GetElement(13)), IntrinsicMath.CPUOnly.Tan(value.GetElement(14)), IntrinsicMath.CPUOnly.Tan(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) TanFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> TanhFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Tanh(value.GetElement(0)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(1)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(2)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(3)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(4)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(5)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(6)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(7)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(8)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(9)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(10)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(11)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(12)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(13)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(14)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) TanhFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AtanFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Atan(value.GetElement(0)), IntrinsicMath.CPUOnly.Atan(value.GetElement(1)), IntrinsicMath.CPUOnly.Atan(value.GetElement(2)), IntrinsicMath.CPUOnly.Atan(value.GetElement(3)), IntrinsicMath.CPUOnly.Atan(value.GetElement(4)), IntrinsicMath.CPUOnly.Atan(value.GetElement(5)), IntrinsicMath.CPUOnly.Atan(value.GetElement(6)), IntrinsicMath.CPUOnly.Atan(value.GetElement(7)), IntrinsicMath.CPUOnly.Atan(value.GetElement(8)), IntrinsicMath.CPUOnly.Atan(value.GetElement(9)), IntrinsicMath.CPUOnly.Atan(value.GetElement(10)), IntrinsicMath.CPUOnly.Atan(value.GetElement(11)), IntrinsicMath.CPUOnly.Atan(value.GetElement(12)), IntrinsicMath.CPUOnly.Atan(value.GetElement(13)), IntrinsicMath.CPUOnly.Atan(value.GetElement(14)), IntrinsicMath.CPUOnly.Atan(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AtanFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ExpFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Exp(value.GetElement(0)), IntrinsicMath.CPUOnly.Exp(value.GetElement(1)), IntrinsicMath.CPUOnly.Exp(value.GetElement(2)), IntrinsicMath.CPUOnly.Exp(value.GetElement(3)), IntrinsicMath.CPUOnly.Exp(value.GetElement(4)), IntrinsicMath.CPUOnly.Exp(value.GetElement(5)), IntrinsicMath.CPUOnly.Exp(value.GetElement(6)), IntrinsicMath.CPUOnly.Exp(value.GetElement(7)), IntrinsicMath.CPUOnly.Exp(value.GetElement(8)), IntrinsicMath.CPUOnly.Exp(value.GetElement(9)), IntrinsicMath.CPUOnly.Exp(value.GetElement(10)), IntrinsicMath.CPUOnly.Exp(value.GetElement(11)), IntrinsicMath.CPUOnly.Exp(value.GetElement(12)), IntrinsicMath.CPUOnly.Exp(value.GetElement(13)), IntrinsicMath.CPUOnly.Exp(value.GetElement(14)), IntrinsicMath.CPUOnly.Exp(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ExpFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> Exp2FF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Exp2(value.GetElement(0)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(1)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(2)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(3)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(4)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(5)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(6)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(7)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(8)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(9)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(10)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(11)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(12)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(13)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(14)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Exp2FF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> FloorFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Floor(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) FloorFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    Vector512.Floor(value.Item1),
                    Vector512.Floor(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CeilingFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Ceiling(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) CeilingFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    Vector512.Ceiling(value.Item1),
                    Vector512.Ceiling(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> LogFF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Log(value.GetElement(0)), IntrinsicMath.CPUOnly.Log(value.GetElement(1)), IntrinsicMath.CPUOnly.Log(value.GetElement(2)), IntrinsicMath.CPUOnly.Log(value.GetElement(3)), IntrinsicMath.CPUOnly.Log(value.GetElement(4)), IntrinsicMath.CPUOnly.Log(value.GetElement(5)), IntrinsicMath.CPUOnly.Log(value.GetElement(6)), IntrinsicMath.CPUOnly.Log(value.GetElement(7)), IntrinsicMath.CPUOnly.Log(value.GetElement(8)), IntrinsicMath.CPUOnly.Log(value.GetElement(9)), IntrinsicMath.CPUOnly.Log(value.GetElement(10)), IntrinsicMath.CPUOnly.Log(value.GetElement(11)), IntrinsicMath.CPUOnly.Log(value.GetElement(12)), IntrinsicMath.CPUOnly.Log(value.GetElement(13)), IntrinsicMath.CPUOnly.Log(value.GetElement(14)), IntrinsicMath.CPUOnly.Log(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) LogFF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> Log2FF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Log2(value.GetElement(0)), IntrinsicMath.CPUOnly.Log2(value.GetElement(1)), IntrinsicMath.CPUOnly.Log2(value.GetElement(2)), IntrinsicMath.CPUOnly.Log2(value.GetElement(3)), IntrinsicMath.CPUOnly.Log2(value.GetElement(4)), IntrinsicMath.CPUOnly.Log2(value.GetElement(5)), IntrinsicMath.CPUOnly.Log2(value.GetElement(6)), IntrinsicMath.CPUOnly.Log2(value.GetElement(7)), IntrinsicMath.CPUOnly.Log2(value.GetElement(8)), IntrinsicMath.CPUOnly.Log2(value.GetElement(9)), IntrinsicMath.CPUOnly.Log2(value.GetElement(10)), IntrinsicMath.CPUOnly.Log2(value.GetElement(11)), IntrinsicMath.CPUOnly.Log2(value.GetElement(12)), IntrinsicMath.CPUOnly.Log2(value.GetElement(13)), IntrinsicMath.CPUOnly.Log2(value.GetElement(14)), IntrinsicMath.CPUOnly.Log2(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Log2FF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(7))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> Log10FF32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Create(IntrinsicMath.CPUOnly.Log10(value.GetElement(0)), IntrinsicMath.CPUOnly.Log10(value.GetElement(1)), IntrinsicMath.CPUOnly.Log10(value.GetElement(2)), IntrinsicMath.CPUOnly.Log10(value.GetElement(3)), IntrinsicMath.CPUOnly.Log10(value.GetElement(4)), IntrinsicMath.CPUOnly.Log10(value.GetElement(5)), IntrinsicMath.CPUOnly.Log10(value.GetElement(6)), IntrinsicMath.CPUOnly.Log10(value.GetElement(7)), IntrinsicMath.CPUOnly.Log10(value.GetElement(8)), IntrinsicMath.CPUOnly.Log10(value.GetElement(9)), IntrinsicMath.CPUOnly.Log10(value.GetElement(10)), IntrinsicMath.CPUOnly.Log10(value.GetElement(11)), IntrinsicMath.CPUOnly.Log10(value.GetElement(12)), IntrinsicMath.CPUOnly.Log10(value.GetElement(13)), IntrinsicMath.CPUOnly.Log10(value.GetElement(14)), IntrinsicMath.CPUOnly.Log10(value.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Log10FF64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(7))));
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

        /// <summary>
        /// The divisor of an integer division or remainder with every zero lane replaced by one. A SIMD warp executes ALL of its lanes, also the ones whose
        /// result is never used (the tail of the last warp, lanes switched off by divergent control flow): their operands are whatever the registers held, often
        /// zero, and a zero divisor in any lane makes the vector division throw <see cref="DivideByZeroException"/> for the whole warp. The lanes that matter are
        /// not changed (an active lane dividing by zero is undefined on every GPU backend).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<T> NonZeroDivisor<T>(Vector512<T> value)
            where T : struct =>
            Vector512.ConditionalSelect(
                Vector512.Equals(value, Vector512<T>.Zero),
                Vector512<T>.One,
                value);

        /// <summary>The 64-bit lane pair form of <see cref="NonZeroDivisor{T}(Vector512{T})"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<T>, Vector512<T>) NonZeroDivisor<T>(
            (Vector512<T>, Vector512<T>) value)
            where T : struct =>
            (NonZeroDivisor(value.Item1), NonZeroDivisor(value.Item2));

        /// <summary>
        /// The divisor of a signed integer division or remainder with every zero lane, and every -1 lane whose dividend is MinValue, replaced by one: a
        /// vector division throws <see cref="OverflowException"/> for MinValue / -1 in any lane, the unused ones included (see
        /// <see cref="NonZeroDivisor{T}(Vector512{T})"/>). MinValue / 1 = MinValue and MinValue % 1 = 0 are the two's complement results of MinValue / -1.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SafeSignedDivisor(Vector512<int> left, Vector512<int> right) =>
            Vector512.ConditionalSelect(
                Vector512.Equals(right, Vector512<int>.Zero) |
                    (Vector512.Equals(right, Vector512<int>.AllBitsSet) &
                    Vector512.Equals(left, Vector512.Create(int.MinValue))),
                Vector512<int>.One,
                right);

        /// <summary>The 64-bit form of <see cref="SafeSignedDivisor(Vector512{int}, Vector512{int})"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<long> SafeSignedDivisor(Vector512<long> left, Vector512<long> right) =>
            Vector512.ConditionalSelect(
                Vector512.Equals(right, Vector512<long>.Zero) |
                    (Vector512.Equals(right, Vector512<long>.AllBitsSet) &
                    Vector512.Equals(left, Vector512.Create(long.MinValue))),
                Vector512<long>.One,
                right);

        /// <summary>The 64-bit lane pair form of <see cref="SafeSignedDivisor(Vector512{long}, Vector512{long})"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SafeSignedDivisor(
            (Vector512<long>, Vector512<long>) left,
            (Vector512<long>, Vector512<long>) right) =>
            (SafeSignedDivisor(left.Item1, right.Item1), SafeSignedDivisor(left.Item2, right.Item2));

        #region Binary Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AddI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = left + right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AddU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = left + right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AddF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = left + right;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AddI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                left.Item1 + right.Item1,
                left.Item2 + right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AddU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                left.Item1 + right.Item1,
                left.Item2 + right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AddF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                left.Item1 + right.Item1,
                left.Item2 + right.Item2);

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SubI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = left - right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SubU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = left - right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SubF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = left - right;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SubI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                left.Item1 - right.Item1,
                left.Item2 - right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SubU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                left.Item1 - right.Item1,
                left.Item2 - right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SubF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                left.Item1 - right.Item1,
                left.Item2 - right.Item2);

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MulI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = left * right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MulU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = left * right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MulF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = left * right;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MulI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                left.Item1 * right.Item1,
                left.Item2 * right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MulU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                left.Item1 * right.Item1,
                left.Item2 * right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MulF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                left.Item1 * right.Item1,
                left.Item2 * right.Item2);

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> DivI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = SafeSignedDivisor(left, second);

            var result = left / right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> DivU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = NonZeroDivisor(CastIToU32(second));

            var result = left / right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> DivF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = left / right;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) DivI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = SafeSignedDivisor(left, second);

            var result = (
                left.Item1 / right.Item1,
                left.Item2 / right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) DivU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = NonZeroDivisor(CastIToU64(second));

            var result = (
                left.Item1 / right.Item1,
                left.Item2 / right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) DivF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                left.Item1 / right.Item1,
                left.Item2 / right.Item2);

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> RemI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = SafeSignedDivisor(left, second);

            var result = left - left / right * right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> RemU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = NonZeroDivisor(CastIToU32(second));

            var result = left - left / right * right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> RemF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            // x - x / y * y has no truncation: a float remainder is the IEEE fmod of every lane (what the CPU backend computes)
            var result = Vector512.Create(left.GetElement(0) % right.GetElement(0), left.GetElement(1) % right.GetElement(1), left.GetElement(2) % right.GetElement(2), left.GetElement(3) % right.GetElement(3), left.GetElement(4) % right.GetElement(4), left.GetElement(5) % right.GetElement(5), left.GetElement(6) % right.GetElement(6), left.GetElement(7) % right.GetElement(7), left.GetElement(8) % right.GetElement(8), left.GetElement(9) % right.GetElement(9), left.GetElement(10) % right.GetElement(10), left.GetElement(11) % right.GetElement(11), left.GetElement(12) % right.GetElement(12), left.GetElement(13) % right.GetElement(13), left.GetElement(14) % right.GetElement(14), left.GetElement(15) % right.GetElement(15));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) RemI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = SafeSignedDivisor(left, second);

            var result = (
                left.Item1 - left.Item1 / right.Item1 * right.Item1,
                left.Item2 - left.Item2 / right.Item2 * right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) RemU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = NonZeroDivisor(CastIToU64(second));

            var result = (
                left.Item1 - left.Item1 / right.Item1 * right.Item1,
                left.Item2 - left.Item2 / right.Item2 * right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) RemF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            // x - x / y * y has no truncation: a float remainder is the IEEE fmod of every lane (what the CPU backend computes)
            var result = (Vector512.Create(left.Item1.GetElement(0) % right.Item1.GetElement(0), left.Item1.GetElement(1) % right.Item1.GetElement(1), left.Item1.GetElement(2) % right.Item1.GetElement(2), left.Item1.GetElement(3) % right.Item1.GetElement(3), left.Item1.GetElement(4) % right.Item1.GetElement(4), left.Item1.GetElement(5) % right.Item1.GetElement(5), left.Item1.GetElement(6) % right.Item1.GetElement(6), left.Item1.GetElement(7) % right.Item1.GetElement(7)), Vector512.Create(left.Item2.GetElement(0) % right.Item2.GetElement(0), left.Item2.GetElement(1) % right.Item2.GetElement(1), left.Item2.GetElement(2) % right.Item2.GetElement(2), left.Item2.GetElement(3) % right.Item2.GetElement(3), left.Item2.GetElement(4) % right.Item2.GetElement(4), left.Item2.GetElement(5) % right.Item2.GetElement(5), left.Item2.GetElement(6) % right.Item2.GetElement(6), left.Item2.GetElement(7) % right.Item2.GetElement(7)));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AndI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector512.BitwiseAnd(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> AndU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector512.BitwiseAnd(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AndI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector512.BitwiseAnd(left.Item1, right.Item1),
                Vector512.BitwiseAnd(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) AndU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector512.BitwiseAnd(left.Item1, right.Item1),
                Vector512.BitwiseAnd(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> OrI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector512.BitwiseOr(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> OrU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector512.BitwiseOr(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) OrI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector512.BitwiseOr(left.Item1, right.Item1),
                Vector512.BitwiseOr(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) OrU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector512.BitwiseOr(left.Item1, right.Item1),
                Vector512.BitwiseOr(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> XorI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector512.Xor(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> XorU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector512.Xor(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) XorI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector512.Xor(left.Item1, right.Item1),
                Vector512.Xor(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) XorU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector512.Xor(left.Item1, right.Item1),
                Vector512.Xor(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ShlI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector512.Create(left.GetElement(0) << (int)right.GetElement(0), left.GetElement(1) << (int)right.GetElement(1), left.GetElement(2) << (int)right.GetElement(2), left.GetElement(3) << (int)right.GetElement(3), left.GetElement(4) << (int)right.GetElement(4), left.GetElement(5) << (int)right.GetElement(5), left.GetElement(6) << (int)right.GetElement(6), left.GetElement(7) << (int)right.GetElement(7), left.GetElement(8) << (int)right.GetElement(8), left.GetElement(9) << (int)right.GetElement(9), left.GetElement(10) << (int)right.GetElement(10), left.GetElement(11) << (int)right.GetElement(11), left.GetElement(12) << (int)right.GetElement(12), left.GetElement(13) << (int)right.GetElement(13), left.GetElement(14) << (int)right.GetElement(14), left.GetElement(15) << (int)right.GetElement(15));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ShlU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector512.Create(left.GetElement(0) << (int)right.GetElement(0), left.GetElement(1) << (int)right.GetElement(1), left.GetElement(2) << (int)right.GetElement(2), left.GetElement(3) << (int)right.GetElement(3), left.GetElement(4) << (int)right.GetElement(4), left.GetElement(5) << (int)right.GetElement(5), left.GetElement(6) << (int)right.GetElement(6), left.GetElement(7) << (int)right.GetElement(7), left.GetElement(8) << (int)right.GetElement(8), left.GetElement(9) << (int)right.GetElement(9), left.GetElement(10) << (int)right.GetElement(10), left.GetElement(11) << (int)right.GetElement(11), left.GetElement(12) << (int)right.GetElement(12), left.GetElement(13) << (int)right.GetElement(13), left.GetElement(14) << (int)right.GetElement(14), left.GetElement(15) << (int)right.GetElement(15));
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ShlI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (Vector512.Create(left.Item1.GetElement(0) << (int)right.Item1.GetElement(0), left.Item1.GetElement(1) << (int)right.Item1.GetElement(1), left.Item1.GetElement(2) << (int)right.Item1.GetElement(2), left.Item1.GetElement(3) << (int)right.Item1.GetElement(3), left.Item1.GetElement(4) << (int)right.Item1.GetElement(4), left.Item1.GetElement(5) << (int)right.Item1.GetElement(5), left.Item1.GetElement(6) << (int)right.Item1.GetElement(6), left.Item1.GetElement(7) << (int)right.Item1.GetElement(7)), Vector512.Create(left.Item2.GetElement(0) << (int)right.Item2.GetElement(0), left.Item2.GetElement(1) << (int)right.Item2.GetElement(1), left.Item2.GetElement(2) << (int)right.Item2.GetElement(2), left.Item2.GetElement(3) << (int)right.Item2.GetElement(3), left.Item2.GetElement(4) << (int)right.Item2.GetElement(4), left.Item2.GetElement(5) << (int)right.Item2.GetElement(5), left.Item2.GetElement(6) << (int)right.Item2.GetElement(6), left.Item2.GetElement(7) << (int)right.Item2.GetElement(7)));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ShlU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (Vector512.Create(left.Item1.GetElement(0) << (int)right.Item1.GetElement(0), left.Item1.GetElement(1) << (int)right.Item1.GetElement(1), left.Item1.GetElement(2) << (int)right.Item1.GetElement(2), left.Item1.GetElement(3) << (int)right.Item1.GetElement(3), left.Item1.GetElement(4) << (int)right.Item1.GetElement(4), left.Item1.GetElement(5) << (int)right.Item1.GetElement(5), left.Item1.GetElement(6) << (int)right.Item1.GetElement(6), left.Item1.GetElement(7) << (int)right.Item1.GetElement(7)), Vector512.Create(left.Item2.GetElement(0) << (int)right.Item2.GetElement(0), left.Item2.GetElement(1) << (int)right.Item2.GetElement(1), left.Item2.GetElement(2) << (int)right.Item2.GetElement(2), left.Item2.GetElement(3) << (int)right.Item2.GetElement(3), left.Item2.GetElement(4) << (int)right.Item2.GetElement(4), left.Item2.GetElement(5) << (int)right.Item2.GetElement(5), left.Item2.GetElement(6) << (int)right.Item2.GetElement(6), left.Item2.GetElement(7) << (int)right.Item2.GetElement(7)));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ShrI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector512.Create(left.GetElement(0) >> (int)right.GetElement(0), left.GetElement(1) >> (int)right.GetElement(1), left.GetElement(2) >> (int)right.GetElement(2), left.GetElement(3) >> (int)right.GetElement(3), left.GetElement(4) >> (int)right.GetElement(4), left.GetElement(5) >> (int)right.GetElement(5), left.GetElement(6) >> (int)right.GetElement(6), left.GetElement(7) >> (int)right.GetElement(7), left.GetElement(8) >> (int)right.GetElement(8), left.GetElement(9) >> (int)right.GetElement(9), left.GetElement(10) >> (int)right.GetElement(10), left.GetElement(11) >> (int)right.GetElement(11), left.GetElement(12) >> (int)right.GetElement(12), left.GetElement(13) >> (int)right.GetElement(13), left.GetElement(14) >> (int)right.GetElement(14), left.GetElement(15) >> (int)right.GetElement(15));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ShrU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector512.Create(left.GetElement(0) >> (int)right.GetElement(0), left.GetElement(1) >> (int)right.GetElement(1), left.GetElement(2) >> (int)right.GetElement(2), left.GetElement(3) >> (int)right.GetElement(3), left.GetElement(4) >> (int)right.GetElement(4), left.GetElement(5) >> (int)right.GetElement(5), left.GetElement(6) >> (int)right.GetElement(6), left.GetElement(7) >> (int)right.GetElement(7), left.GetElement(8) >> (int)right.GetElement(8), left.GetElement(9) >> (int)right.GetElement(9), left.GetElement(10) >> (int)right.GetElement(10), left.GetElement(11) >> (int)right.GetElement(11), left.GetElement(12) >> (int)right.GetElement(12), left.GetElement(13) >> (int)right.GetElement(13), left.GetElement(14) >> (int)right.GetElement(14), left.GetElement(15) >> (int)right.GetElement(15));
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ShrI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (Vector512.Create(left.Item1.GetElement(0) >> (int)right.Item1.GetElement(0), left.Item1.GetElement(1) >> (int)right.Item1.GetElement(1), left.Item1.GetElement(2) >> (int)right.Item1.GetElement(2), left.Item1.GetElement(3) >> (int)right.Item1.GetElement(3), left.Item1.GetElement(4) >> (int)right.Item1.GetElement(4), left.Item1.GetElement(5) >> (int)right.Item1.GetElement(5), left.Item1.GetElement(6) >> (int)right.Item1.GetElement(6), left.Item1.GetElement(7) >> (int)right.Item1.GetElement(7)), Vector512.Create(left.Item2.GetElement(0) >> (int)right.Item2.GetElement(0), left.Item2.GetElement(1) >> (int)right.Item2.GetElement(1), left.Item2.GetElement(2) >> (int)right.Item2.GetElement(2), left.Item2.GetElement(3) >> (int)right.Item2.GetElement(3), left.Item2.GetElement(4) >> (int)right.Item2.GetElement(4), left.Item2.GetElement(5) >> (int)right.Item2.GetElement(5), left.Item2.GetElement(6) >> (int)right.Item2.GetElement(6), left.Item2.GetElement(7) >> (int)right.Item2.GetElement(7)));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ShrU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (Vector512.Create(left.Item1.GetElement(0) >> (int)right.Item1.GetElement(0), left.Item1.GetElement(1) >> (int)right.Item1.GetElement(1), left.Item1.GetElement(2) >> (int)right.Item1.GetElement(2), left.Item1.GetElement(3) >> (int)right.Item1.GetElement(3), left.Item1.GetElement(4) >> (int)right.Item1.GetElement(4), left.Item1.GetElement(5) >> (int)right.Item1.GetElement(5), left.Item1.GetElement(6) >> (int)right.Item1.GetElement(6), left.Item1.GetElement(7) >> (int)right.Item1.GetElement(7)), Vector512.Create(left.Item2.GetElement(0) >> (int)right.Item2.GetElement(0), left.Item2.GetElement(1) >> (int)right.Item2.GetElement(1), left.Item2.GetElement(2) >> (int)right.Item2.GetElement(2), left.Item2.GetElement(3) >> (int)right.Item2.GetElement(3), left.Item2.GetElement(4) >> (int)right.Item2.GetElement(4), left.Item2.GetElement(5) >> (int)right.Item2.GetElement(5), left.Item2.GetElement(6) >> (int)right.Item2.GetElement(6), left.Item2.GetElement(7) >> (int)right.Item2.GetElement(7)));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MinI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector512.Min(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MinU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector512.Min(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MinF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector512.Min(left, right);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MinI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector512.Min(left.Item1, right.Item1),
                Vector512.Min(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MinU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector512.Min(left.Item1, right.Item1),
                Vector512.Min(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MinF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                Vector512.Min(left.Item1, right.Item1),
                Vector512.Min(left.Item2, right.Item2));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MaxI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector512.Max(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MaxU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector512.Max(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MaxF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector512.Max(left, right);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MaxI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector512.Max(left.Item1, right.Item1),
                Vector512.Max(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MaxU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector512.Max(left.Item1, right.Item1),
                Vector512.Max(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MaxF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                Vector512.Max(left.Item1, right.Item1),
                Vector512.Max(left.Item2, right.Item2));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> Atan2FF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector512.Create(IntrinsicMath.CPUOnly.Atan2(left.GetElement(0), right.GetElement(0)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(1), right.GetElement(1)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(2), right.GetElement(2)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(3), right.GetElement(3)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(4), right.GetElement(4)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(5), right.GetElement(5)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(6), right.GetElement(6)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(7), right.GetElement(7)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(8), right.GetElement(8)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(9), right.GetElement(9)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(10), right.GetElement(10)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(11), right.GetElement(11)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(12), right.GetElement(12)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(13), right.GetElement(13)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(14), right.GetElement(14)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(15), right.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Atan2FF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(0), right.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(1), right.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(2), right.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(3), right.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(4), right.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(5), right.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(6), right.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(7), right.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(0), right.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(1), right.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(2), right.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(3), right.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(4), right.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(5), right.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(6), right.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(7), right.Item2.GetElement(7))));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> PowFF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector512.Create(IntrinsicMath.CPUOnly.Pow(left.GetElement(0), right.GetElement(0)), IntrinsicMath.CPUOnly.Pow(left.GetElement(1), right.GetElement(1)), IntrinsicMath.CPUOnly.Pow(left.GetElement(2), right.GetElement(2)), IntrinsicMath.CPUOnly.Pow(left.GetElement(3), right.GetElement(3)), IntrinsicMath.CPUOnly.Pow(left.GetElement(4), right.GetElement(4)), IntrinsicMath.CPUOnly.Pow(left.GetElement(5), right.GetElement(5)), IntrinsicMath.CPUOnly.Pow(left.GetElement(6), right.GetElement(6)), IntrinsicMath.CPUOnly.Pow(left.GetElement(7), right.GetElement(7)), IntrinsicMath.CPUOnly.Pow(left.GetElement(8), right.GetElement(8)), IntrinsicMath.CPUOnly.Pow(left.GetElement(9), right.GetElement(9)), IntrinsicMath.CPUOnly.Pow(left.GetElement(10), right.GetElement(10)), IntrinsicMath.CPUOnly.Pow(left.GetElement(11), right.GetElement(11)), IntrinsicMath.CPUOnly.Pow(left.GetElement(12), right.GetElement(12)), IntrinsicMath.CPUOnly.Pow(left.GetElement(13), right.GetElement(13)), IntrinsicMath.CPUOnly.Pow(left.GetElement(14), right.GetElement(14)), IntrinsicMath.CPUOnly.Pow(left.GetElement(15), right.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) PowFF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(0), right.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(1), right.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(2), right.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(3), right.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(4), right.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(5), right.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(6), right.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(7), right.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(0), right.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(1), right.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(2), right.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(3), right.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(4), right.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(5), right.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(6), right.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(7), right.Item2.GetElement(7))));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> BinaryLogFF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector512.Create(IntrinsicMath.CPUOnly.Log(left.GetElement(0), right.GetElement(0)), IntrinsicMath.CPUOnly.Log(left.GetElement(1), right.GetElement(1)), IntrinsicMath.CPUOnly.Log(left.GetElement(2), right.GetElement(2)), IntrinsicMath.CPUOnly.Log(left.GetElement(3), right.GetElement(3)), IntrinsicMath.CPUOnly.Log(left.GetElement(4), right.GetElement(4)), IntrinsicMath.CPUOnly.Log(left.GetElement(5), right.GetElement(5)), IntrinsicMath.CPUOnly.Log(left.GetElement(6), right.GetElement(6)), IntrinsicMath.CPUOnly.Log(left.GetElement(7), right.GetElement(7)), IntrinsicMath.CPUOnly.Log(left.GetElement(8), right.GetElement(8)), IntrinsicMath.CPUOnly.Log(left.GetElement(9), right.GetElement(9)), IntrinsicMath.CPUOnly.Log(left.GetElement(10), right.GetElement(10)), IntrinsicMath.CPUOnly.Log(left.GetElement(11), right.GetElement(11)), IntrinsicMath.CPUOnly.Log(left.GetElement(12), right.GetElement(12)), IntrinsicMath.CPUOnly.Log(left.GetElement(13), right.GetElement(13)), IntrinsicMath.CPUOnly.Log(left.GetElement(14), right.GetElement(14)), IntrinsicMath.CPUOnly.Log(left.GetElement(15), right.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) BinaryLogFF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (Vector512.Create(IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(0), right.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(1), right.Item1.GetElement(1)), IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(2), right.Item1.GetElement(2)), IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(3), right.Item1.GetElement(3)), IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(4), right.Item1.GetElement(4)), IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(5), right.Item1.GetElement(5)), IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(6), right.Item1.GetElement(6)), IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(7), right.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(0), right.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(1), right.Item2.GetElement(1)), IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(2), right.Item2.GetElement(2)), IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(3), right.Item2.GetElement(3)), IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(4), right.Item2.GetElement(4)), IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(5), right.Item2.GetElement(5)), IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(6), right.Item2.GetElement(6)), IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(7), right.Item2.GetElement(7))));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CopySignFF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector512.Create(IntrinsicMath.CopySign(left.GetElement(0), right.GetElement(0)), IntrinsicMath.CopySign(left.GetElement(1), right.GetElement(1)), IntrinsicMath.CopySign(left.GetElement(2), right.GetElement(2)), IntrinsicMath.CopySign(left.GetElement(3), right.GetElement(3)), IntrinsicMath.CopySign(left.GetElement(4), right.GetElement(4)), IntrinsicMath.CopySign(left.GetElement(5), right.GetElement(5)), IntrinsicMath.CopySign(left.GetElement(6), right.GetElement(6)), IntrinsicMath.CopySign(left.GetElement(7), right.GetElement(7)), IntrinsicMath.CopySign(left.GetElement(8), right.GetElement(8)), IntrinsicMath.CopySign(left.GetElement(9), right.GetElement(9)), IntrinsicMath.CopySign(left.GetElement(10), right.GetElement(10)), IntrinsicMath.CopySign(left.GetElement(11), right.GetElement(11)), IntrinsicMath.CopySign(left.GetElement(12), right.GetElement(12)), IntrinsicMath.CopySign(left.GetElement(13), right.GetElement(13)), IntrinsicMath.CopySign(left.GetElement(14), right.GetElement(14)), IntrinsicMath.CopySign(left.GetElement(15), right.GetElement(15)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) CopySignFF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (Vector512.Create(IntrinsicMath.CopySign(left.Item1.GetElement(0), right.Item1.GetElement(0)), IntrinsicMath.CopySign(left.Item1.GetElement(1), right.Item1.GetElement(1)), IntrinsicMath.CopySign(left.Item1.GetElement(2), right.Item1.GetElement(2)), IntrinsicMath.CopySign(left.Item1.GetElement(3), right.Item1.GetElement(3)), IntrinsicMath.CopySign(left.Item1.GetElement(4), right.Item1.GetElement(4)), IntrinsicMath.CopySign(left.Item1.GetElement(5), right.Item1.GetElement(5)), IntrinsicMath.CopySign(left.Item1.GetElement(6), right.Item1.GetElement(6)), IntrinsicMath.CopySign(left.Item1.GetElement(7), right.Item1.GetElement(7))), Vector512.Create(IntrinsicMath.CopySign(left.Item2.GetElement(0), right.Item2.GetElement(0)), IntrinsicMath.CopySign(left.Item2.GetElement(1), right.Item2.GetElement(1)), IntrinsicMath.CopySign(left.Item2.GetElement(2), right.Item2.GetElement(2)), IntrinsicMath.CopySign(left.Item2.GetElement(3), right.Item2.GetElement(3)), IntrinsicMath.CopySign(left.Item2.GetElement(4), right.Item2.GetElement(4)), IntrinsicMath.CopySign(left.Item2.GetElement(5), right.Item2.GetElement(5)), IntrinsicMath.CopySign(left.Item2.GetElement(6), right.Item2.GetElement(6)), IntrinsicMath.CopySign(left.Item2.GetElement(7), right.Item2.GetElement(7))));

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
        internal static Vector512<int> MultiplyAddI32(
            Vector512<int> first,
            Vector512<int> second,
            Vector512<int> third)
        {
            var a = first;
            var b = second;
            var c = third;

            var result = Vector512.Add(Vector512.Multiply(a, b), c);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MultiplyAddU32(
            Vector512<int> first,
            Vector512<int> second,
            Vector512<int> third)
        {
            var a = CastIToU32(first);
            var b = CastIToU32(second);
            var c = CastIToU32(third);

            var result = Vector512.Add(Vector512.Multiply(a, b), c);

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> MultiplyAddF32(
            Vector512<int> first,
            Vector512<int> second,
            Vector512<int> third)
        {
            var a = CastIToF32(first);
            var b = CastIToF32(second);
            var c = CastIToF32(third);

            var result = Vector512.Add(Vector512.Multiply(a, b), c);

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MultiplyAddI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second,
            (Vector512<long>, Vector512<long>) third)
        {
            var a = first;
            var b = second;
            var c = third;

            var result1 = Vector512.Add(Vector512.Multiply(a.Item1, b.Item1), c.Item1);
            var result2 = Vector512.Add(Vector512.Multiply(a.Item2, b.Item2), c.Item2);

            return (result1, result2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MultiplyAddU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second,
            (Vector512<long>, Vector512<long>) third)
        {
            var a = CastIToU64(first);
            var b = CastIToU64(second);
            var c = CastIToU64(third);

            var result1 = Vector512.Add(Vector512.Multiply(a.Item1, b.Item1), c.Item1);
            var result2 = Vector512.Add(Vector512.Multiply(a.Item2, b.Item2), c.Item2);

            return CastUToI64((result1, result2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) MultiplyAddF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second,
            (Vector512<long>, Vector512<long>) third)
        {
            var a = CastIToF64(first);
            var b = CastIToF64(second);
            var c = CastIToF64(third);

            var result1 = Vector512.Add(Vector512.Multiply(a.Item1, b.Item1), c.Item1);
            var result2 = Vector512.Add(Vector512.Multiply(a.Item2, b.Item2), c.Item2);

            return CastFToI64((result1, result2));
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
        internal static Vector512<int> CompareEqualI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            return Vector512.Equals(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareEqualU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector512.Equals(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareEqualF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector512.Equals(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareEqualI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector512.Equals(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.Equals(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareEqualU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector512.Equals(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.Equals(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareEqualF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector512.Equals(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.Equals(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareNotEqualI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            return NotI32(Vector512.Equals(left, right).AsInt32());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareNotEqualU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return NotI32(Vector512.Equals(left, right).AsInt32());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareNotEqualF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return NotI32(Vector512.Equals(left, right).AsInt32());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareNotEqualI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  NotI32(Vector512.Equals(left.Item1, right.Item1).AsInt32());
            var result2 =  NotI32(Vector512.Equals(left.Item2, right.Item2).AsInt32());

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareNotEqualU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  NotI32(Vector512.Equals(left.Item1, right.Item1).AsInt32());
            var result2 =  NotI32(Vector512.Equals(left.Item2, right.Item2).AsInt32());

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareNotEqualF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  NotI32(Vector512.Equals(left.Item1, right.Item1).AsInt32());
            var result2 =  NotI32(Vector512.Equals(left.Item2, right.Item2).AsInt32());

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessThanI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            return Vector512.LessThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessThanU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector512.LessThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessThanF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector512.LessThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessThanI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector512.LessThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.LessThan(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessThanU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector512.LessThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.LessThan(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessThanF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector512.LessThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.LessThan(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessEqualI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            return Vector512.LessThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessEqualU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector512.LessThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessEqualF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector512.LessThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessEqualI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector512.LessThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.LessThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessEqualU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector512.LessThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.LessThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareLessEqualF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector512.LessThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.LessThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterThanI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            return Vector512.GreaterThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterThanU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector512.GreaterThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterThanF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector512.GreaterThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterThanI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector512.GreaterThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.GreaterThan(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterThanU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector512.GreaterThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.GreaterThan(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterThanF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector512.GreaterThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.GreaterThan(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterEqualI32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = first;
            var right = second;

            return Vector512.GreaterThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterEqualU32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector512.GreaterThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterEqualF32(
            Vector512<int> first,
            Vector512<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector512.GreaterThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterEqualI64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector512.GreaterThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.GreaterThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterEqualU64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector512.GreaterThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.GreaterThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> CompareGreaterEqualF64(
            (Vector512<long>, Vector512<long>) first,
            (Vector512<long>, Vector512<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector512.GreaterThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector512.GreaterThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector512.Narrow(result1.AsInt64(), result2.AsInt64());
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
        internal static Vector512<int> ConvertInt8ToInt8_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt8ToInt16_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((short)(sbyte)value.GetElement(0), (short)(sbyte)value.GetElement(1), (short)(sbyte)value.GetElement(2), (short)(sbyte)value.GetElement(3), (short)(sbyte)value.GetElement(4), (short)(sbyte)value.GetElement(5), (short)(sbyte)value.GetElement(6), (short)(sbyte)value.GetElement(7), (short)(sbyte)value.GetElement(8), (short)(sbyte)value.GetElement(9), (short)(sbyte)value.GetElement(10), (short)(sbyte)value.GetElement(11), (short)(sbyte)value.GetElement(12), (short)(sbyte)value.GetElement(13), (short)(sbyte)value.GetElement(14), (short)(sbyte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt8ToInt32_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((int)(sbyte)value.GetElement(0), (int)(sbyte)value.GetElement(1), (int)(sbyte)value.GetElement(2), (int)(sbyte)value.GetElement(3), (int)(sbyte)value.GetElement(4), (int)(sbyte)value.GetElement(5), (int)(sbyte)value.GetElement(6), (int)(sbyte)value.GetElement(7), (int)(sbyte)value.GetElement(8), (int)(sbyte)value.GetElement(9), (int)(sbyte)value.GetElement(10), (int)(sbyte)value.GetElement(11), (int)(sbyte)value.GetElement(12), (int)(sbyte)value.GetElement(13), (int)(sbyte)value.GetElement(14), (int)(sbyte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt8ToUInt8_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt8ToUInt16_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((ushort)(sbyte)value.GetElement(0), (ushort)(sbyte)value.GetElement(1), (ushort)(sbyte)value.GetElement(2), (ushort)(sbyte)value.GetElement(3), (ushort)(sbyte)value.GetElement(4), (ushort)(sbyte)value.GetElement(5), (ushort)(sbyte)value.GetElement(6), (ushort)(sbyte)value.GetElement(7), (ushort)(sbyte)value.GetElement(8), (ushort)(sbyte)value.GetElement(9), (ushort)(sbyte)value.GetElement(10), (ushort)(sbyte)value.GetElement(11), (ushort)(sbyte)value.GetElement(12), (ushort)(sbyte)value.GetElement(13), (ushort)(sbyte)value.GetElement(14), (ushort)(sbyte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt8ToUInt32_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((uint)(sbyte)value.GetElement(0), (uint)(sbyte)value.GetElement(1), (uint)(sbyte)value.GetElement(2), (uint)(sbyte)value.GetElement(3), (uint)(sbyte)value.GetElement(4), (uint)(sbyte)value.GetElement(5), (uint)(sbyte)value.GetElement(6), (uint)(sbyte)value.GetElement(7), (uint)(sbyte)value.GetElement(8), (uint)(sbyte)value.GetElement(9), (uint)(sbyte)value.GetElement(10), (uint)(sbyte)value.GetElement(11), (uint)(sbyte)value.GetElement(12), (uint)(sbyte)value.GetElement(13), (uint)(sbyte)value.GetElement(14), (uint)(sbyte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt8ToHalf_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((Half)(sbyte)value.GetElement(0), (Half)(sbyte)value.GetElement(1), (Half)(sbyte)value.GetElement(2), (Half)(sbyte)value.GetElement(3), (Half)(sbyte)value.GetElement(4), (Half)(sbyte)value.GetElement(5), (Half)(sbyte)value.GetElement(6), (Half)(sbyte)value.GetElement(7), (Half)(sbyte)value.GetElement(8), (Half)(sbyte)value.GetElement(9), (Half)(sbyte)value.GetElement(10), (Half)(sbyte)value.GetElement(11), (Half)(sbyte)value.GetElement(12), (Half)(sbyte)value.GetElement(13), (Half)(sbyte)value.GetElement(14), (Half)(sbyte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt8ToFloat_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((float)(sbyte)value.GetElement(0), (float)(sbyte)value.GetElement(1), (float)(sbyte)value.GetElement(2), (float)(sbyte)value.GetElement(3), (float)(sbyte)value.GetElement(4), (float)(sbyte)value.GetElement(5), (float)(sbyte)value.GetElement(6), (float)(sbyte)value.GetElement(7), (float)(sbyte)value.GetElement(8), (float)(sbyte)value.GetElement(9), (float)(sbyte)value.GetElement(10), (float)(sbyte)value.GetElement(11), (float)(sbyte)value.GetElement(12), (float)(sbyte)value.GetElement(13), (float)(sbyte)value.GetElement(14), (float)(sbyte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt16ToInt8_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((sbyte)(short)value.GetElement(0), (sbyte)(short)value.GetElement(1), (sbyte)(short)value.GetElement(2), (sbyte)(short)value.GetElement(3), (sbyte)(short)value.GetElement(4), (sbyte)(short)value.GetElement(5), (sbyte)(short)value.GetElement(6), (sbyte)(short)value.GetElement(7), (sbyte)(short)value.GetElement(8), (sbyte)(short)value.GetElement(9), (sbyte)(short)value.GetElement(10), (sbyte)(short)value.GetElement(11), (sbyte)(short)value.GetElement(12), (sbyte)(short)value.GetElement(13), (sbyte)(short)value.GetElement(14), (sbyte)(short)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt16ToInt16_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt16ToInt32_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((int)(short)value.GetElement(0), (int)(short)value.GetElement(1), (int)(short)value.GetElement(2), (int)(short)value.GetElement(3), (int)(short)value.GetElement(4), (int)(short)value.GetElement(5), (int)(short)value.GetElement(6), (int)(short)value.GetElement(7), (int)(short)value.GetElement(8), (int)(short)value.GetElement(9), (int)(short)value.GetElement(10), (int)(short)value.GetElement(11), (int)(short)value.GetElement(12), (int)(short)value.GetElement(13), (int)(short)value.GetElement(14), (int)(short)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt16ToUInt8_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((byte)(short)value.GetElement(0), (byte)(short)value.GetElement(1), (byte)(short)value.GetElement(2), (byte)(short)value.GetElement(3), (byte)(short)value.GetElement(4), (byte)(short)value.GetElement(5), (byte)(short)value.GetElement(6), (byte)(short)value.GetElement(7), (byte)(short)value.GetElement(8), (byte)(short)value.GetElement(9), (byte)(short)value.GetElement(10), (byte)(short)value.GetElement(11), (byte)(short)value.GetElement(12), (byte)(short)value.GetElement(13), (byte)(short)value.GetElement(14), (byte)(short)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt16ToUInt16_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt16ToUInt32_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((uint)(short)value.GetElement(0), (uint)(short)value.GetElement(1), (uint)(short)value.GetElement(2), (uint)(short)value.GetElement(3), (uint)(short)value.GetElement(4), (uint)(short)value.GetElement(5), (uint)(short)value.GetElement(6), (uint)(short)value.GetElement(7), (uint)(short)value.GetElement(8), (uint)(short)value.GetElement(9), (uint)(short)value.GetElement(10), (uint)(short)value.GetElement(11), (uint)(short)value.GetElement(12), (uint)(short)value.GetElement(13), (uint)(short)value.GetElement(14), (uint)(short)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt16ToHalf_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((Half)(short)value.GetElement(0), (Half)(short)value.GetElement(1), (Half)(short)value.GetElement(2), (Half)(short)value.GetElement(3), (Half)(short)value.GetElement(4), (Half)(short)value.GetElement(5), (Half)(short)value.GetElement(6), (Half)(short)value.GetElement(7), (Half)(short)value.GetElement(8), (Half)(short)value.GetElement(9), (Half)(short)value.GetElement(10), (Half)(short)value.GetElement(11), (Half)(short)value.GetElement(12), (Half)(short)value.GetElement(13), (Half)(short)value.GetElement(14), (Half)(short)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt16ToFloat_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((float)(short)value.GetElement(0), (float)(short)value.GetElement(1), (float)(short)value.GetElement(2), (float)(short)value.GetElement(3), (float)(short)value.GetElement(4), (float)(short)value.GetElement(5), (float)(short)value.GetElement(6), (float)(short)value.GetElement(7), (float)(short)value.GetElement(8), (float)(short)value.GetElement(9), (float)(short)value.GetElement(10), (float)(short)value.GetElement(11), (float)(short)value.GetElement(12), (float)(short)value.GetElement(13), (float)(short)value.GetElement(14), (float)(short)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt32ToInt8_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((sbyte)(int)value.GetElement(0), (sbyte)(int)value.GetElement(1), (sbyte)(int)value.GetElement(2), (sbyte)(int)value.GetElement(3), (sbyte)(int)value.GetElement(4), (sbyte)(int)value.GetElement(5), (sbyte)(int)value.GetElement(6), (sbyte)(int)value.GetElement(7), (sbyte)(int)value.GetElement(8), (sbyte)(int)value.GetElement(9), (sbyte)(int)value.GetElement(10), (sbyte)(int)value.GetElement(11), (sbyte)(int)value.GetElement(12), (sbyte)(int)value.GetElement(13), (sbyte)(int)value.GetElement(14), (sbyte)(int)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt32ToInt16_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((short)(int)value.GetElement(0), (short)(int)value.GetElement(1), (short)(int)value.GetElement(2), (short)(int)value.GetElement(3), (short)(int)value.GetElement(4), (short)(int)value.GetElement(5), (short)(int)value.GetElement(6), (short)(int)value.GetElement(7), (short)(int)value.GetElement(8), (short)(int)value.GetElement(9), (short)(int)value.GetElement(10), (short)(int)value.GetElement(11), (short)(int)value.GetElement(12), (short)(int)value.GetElement(13), (short)(int)value.GetElement(14), (short)(int)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt32ToInt32_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt32ToUInt8_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((byte)(int)value.GetElement(0), (byte)(int)value.GetElement(1), (byte)(int)value.GetElement(2), (byte)(int)value.GetElement(3), (byte)(int)value.GetElement(4), (byte)(int)value.GetElement(5), (byte)(int)value.GetElement(6), (byte)(int)value.GetElement(7), (byte)(int)value.GetElement(8), (byte)(int)value.GetElement(9), (byte)(int)value.GetElement(10), (byte)(int)value.GetElement(11), (byte)(int)value.GetElement(12), (byte)(int)value.GetElement(13), (byte)(int)value.GetElement(14), (byte)(int)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt32ToUInt16_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((ushort)(int)value.GetElement(0), (ushort)(int)value.GetElement(1), (ushort)(int)value.GetElement(2), (ushort)(int)value.GetElement(3), (ushort)(int)value.GetElement(4), (ushort)(int)value.GetElement(5), (ushort)(int)value.GetElement(6), (ushort)(int)value.GetElement(7), (ushort)(int)value.GetElement(8), (ushort)(int)value.GetElement(9), (ushort)(int)value.GetElement(10), (ushort)(int)value.GetElement(11), (ushort)(int)value.GetElement(12), (ushort)(int)value.GetElement(13), (ushort)(int)value.GetElement(14), (ushort)(int)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt32ToUInt32_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt32ToHalf_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.Create((Half)(int)value.GetElement(0), (Half)(int)value.GetElement(1), (Half)(int)value.GetElement(2), (Half)(int)value.GetElement(3), (Half)(int)value.GetElement(4), (Half)(int)value.GetElement(5), (Half)(int)value.GetElement(6), (Half)(int)value.GetElement(7), (Half)(int)value.GetElement(8), (Half)(int)value.GetElement(9), (Half)(int)value.GetElement(10), (Half)(int)value.GetElement(11), (Half)(int)value.GetElement(12), (Half)(int)value.GetElement(13), (Half)(int)value.GetElement(14), (Half)(int)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertInt32ToFloat_32(
            Vector512<int> warp)
        {
            var value = warp;
            return Vector512.ConvertToSingle(value).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt8ToInt8_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt8ToInt16_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((short)(byte)value.GetElement(0), (short)(byte)value.GetElement(1), (short)(byte)value.GetElement(2), (short)(byte)value.GetElement(3), (short)(byte)value.GetElement(4), (short)(byte)value.GetElement(5), (short)(byte)value.GetElement(6), (short)(byte)value.GetElement(7), (short)(byte)value.GetElement(8), (short)(byte)value.GetElement(9), (short)(byte)value.GetElement(10), (short)(byte)value.GetElement(11), (short)(byte)value.GetElement(12), (short)(byte)value.GetElement(13), (short)(byte)value.GetElement(14), (short)(byte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt8ToInt32_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((int)(byte)value.GetElement(0), (int)(byte)value.GetElement(1), (int)(byte)value.GetElement(2), (int)(byte)value.GetElement(3), (int)(byte)value.GetElement(4), (int)(byte)value.GetElement(5), (int)(byte)value.GetElement(6), (int)(byte)value.GetElement(7), (int)(byte)value.GetElement(8), (int)(byte)value.GetElement(9), (int)(byte)value.GetElement(10), (int)(byte)value.GetElement(11), (int)(byte)value.GetElement(12), (int)(byte)value.GetElement(13), (int)(byte)value.GetElement(14), (int)(byte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt8ToUInt8_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt8ToUInt16_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((ushort)(byte)value.GetElement(0), (ushort)(byte)value.GetElement(1), (ushort)(byte)value.GetElement(2), (ushort)(byte)value.GetElement(3), (ushort)(byte)value.GetElement(4), (ushort)(byte)value.GetElement(5), (ushort)(byte)value.GetElement(6), (ushort)(byte)value.GetElement(7), (ushort)(byte)value.GetElement(8), (ushort)(byte)value.GetElement(9), (ushort)(byte)value.GetElement(10), (ushort)(byte)value.GetElement(11), (ushort)(byte)value.GetElement(12), (ushort)(byte)value.GetElement(13), (ushort)(byte)value.GetElement(14), (ushort)(byte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt8ToUInt32_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((uint)(byte)value.GetElement(0), (uint)(byte)value.GetElement(1), (uint)(byte)value.GetElement(2), (uint)(byte)value.GetElement(3), (uint)(byte)value.GetElement(4), (uint)(byte)value.GetElement(5), (uint)(byte)value.GetElement(6), (uint)(byte)value.GetElement(7), (uint)(byte)value.GetElement(8), (uint)(byte)value.GetElement(9), (uint)(byte)value.GetElement(10), (uint)(byte)value.GetElement(11), (uint)(byte)value.GetElement(12), (uint)(byte)value.GetElement(13), (uint)(byte)value.GetElement(14), (uint)(byte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt8ToHalf_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((Half)(byte)value.GetElement(0), (Half)(byte)value.GetElement(1), (Half)(byte)value.GetElement(2), (Half)(byte)value.GetElement(3), (Half)(byte)value.GetElement(4), (Half)(byte)value.GetElement(5), (Half)(byte)value.GetElement(6), (Half)(byte)value.GetElement(7), (Half)(byte)value.GetElement(8), (Half)(byte)value.GetElement(9), (Half)(byte)value.GetElement(10), (Half)(byte)value.GetElement(11), (Half)(byte)value.GetElement(12), (Half)(byte)value.GetElement(13), (Half)(byte)value.GetElement(14), (Half)(byte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt8ToFloat_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((float)(byte)value.GetElement(0), (float)(byte)value.GetElement(1), (float)(byte)value.GetElement(2), (float)(byte)value.GetElement(3), (float)(byte)value.GetElement(4), (float)(byte)value.GetElement(5), (float)(byte)value.GetElement(6), (float)(byte)value.GetElement(7), (float)(byte)value.GetElement(8), (float)(byte)value.GetElement(9), (float)(byte)value.GetElement(10), (float)(byte)value.GetElement(11), (float)(byte)value.GetElement(12), (float)(byte)value.GetElement(13), (float)(byte)value.GetElement(14), (float)(byte)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt16ToInt8_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((sbyte)(ushort)value.GetElement(0), (sbyte)(ushort)value.GetElement(1), (sbyte)(ushort)value.GetElement(2), (sbyte)(ushort)value.GetElement(3), (sbyte)(ushort)value.GetElement(4), (sbyte)(ushort)value.GetElement(5), (sbyte)(ushort)value.GetElement(6), (sbyte)(ushort)value.GetElement(7), (sbyte)(ushort)value.GetElement(8), (sbyte)(ushort)value.GetElement(9), (sbyte)(ushort)value.GetElement(10), (sbyte)(ushort)value.GetElement(11), (sbyte)(ushort)value.GetElement(12), (sbyte)(ushort)value.GetElement(13), (sbyte)(ushort)value.GetElement(14), (sbyte)(ushort)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt16ToInt16_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt16ToInt32_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((int)(ushort)value.GetElement(0), (int)(ushort)value.GetElement(1), (int)(ushort)value.GetElement(2), (int)(ushort)value.GetElement(3), (int)(ushort)value.GetElement(4), (int)(ushort)value.GetElement(5), (int)(ushort)value.GetElement(6), (int)(ushort)value.GetElement(7), (int)(ushort)value.GetElement(8), (int)(ushort)value.GetElement(9), (int)(ushort)value.GetElement(10), (int)(ushort)value.GetElement(11), (int)(ushort)value.GetElement(12), (int)(ushort)value.GetElement(13), (int)(ushort)value.GetElement(14), (int)(ushort)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt16ToUInt8_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((byte)(ushort)value.GetElement(0), (byte)(ushort)value.GetElement(1), (byte)(ushort)value.GetElement(2), (byte)(ushort)value.GetElement(3), (byte)(ushort)value.GetElement(4), (byte)(ushort)value.GetElement(5), (byte)(ushort)value.GetElement(6), (byte)(ushort)value.GetElement(7), (byte)(ushort)value.GetElement(8), (byte)(ushort)value.GetElement(9), (byte)(ushort)value.GetElement(10), (byte)(ushort)value.GetElement(11), (byte)(ushort)value.GetElement(12), (byte)(ushort)value.GetElement(13), (byte)(ushort)value.GetElement(14), (byte)(ushort)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt16ToUInt16_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt16ToUInt32_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((uint)(ushort)value.GetElement(0), (uint)(ushort)value.GetElement(1), (uint)(ushort)value.GetElement(2), (uint)(ushort)value.GetElement(3), (uint)(ushort)value.GetElement(4), (uint)(ushort)value.GetElement(5), (uint)(ushort)value.GetElement(6), (uint)(ushort)value.GetElement(7), (uint)(ushort)value.GetElement(8), (uint)(ushort)value.GetElement(9), (uint)(ushort)value.GetElement(10), (uint)(ushort)value.GetElement(11), (uint)(ushort)value.GetElement(12), (uint)(ushort)value.GetElement(13), (uint)(ushort)value.GetElement(14), (uint)(ushort)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt16ToHalf_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((Half)(ushort)value.GetElement(0), (Half)(ushort)value.GetElement(1), (Half)(ushort)value.GetElement(2), (Half)(ushort)value.GetElement(3), (Half)(ushort)value.GetElement(4), (Half)(ushort)value.GetElement(5), (Half)(ushort)value.GetElement(6), (Half)(ushort)value.GetElement(7), (Half)(ushort)value.GetElement(8), (Half)(ushort)value.GetElement(9), (Half)(ushort)value.GetElement(10), (Half)(ushort)value.GetElement(11), (Half)(ushort)value.GetElement(12), (Half)(ushort)value.GetElement(13), (Half)(ushort)value.GetElement(14), (Half)(ushort)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt16ToFloat_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((float)(ushort)value.GetElement(0), (float)(ushort)value.GetElement(1), (float)(ushort)value.GetElement(2), (float)(ushort)value.GetElement(3), (float)(ushort)value.GetElement(4), (float)(ushort)value.GetElement(5), (float)(ushort)value.GetElement(6), (float)(ushort)value.GetElement(7), (float)(ushort)value.GetElement(8), (float)(ushort)value.GetElement(9), (float)(ushort)value.GetElement(10), (float)(ushort)value.GetElement(11), (float)(ushort)value.GetElement(12), (float)(ushort)value.GetElement(13), (float)(ushort)value.GetElement(14), (float)(ushort)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt32ToInt8_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((sbyte)(uint)value.GetElement(0), (sbyte)(uint)value.GetElement(1), (sbyte)(uint)value.GetElement(2), (sbyte)(uint)value.GetElement(3), (sbyte)(uint)value.GetElement(4), (sbyte)(uint)value.GetElement(5), (sbyte)(uint)value.GetElement(6), (sbyte)(uint)value.GetElement(7), (sbyte)(uint)value.GetElement(8), (sbyte)(uint)value.GetElement(9), (sbyte)(uint)value.GetElement(10), (sbyte)(uint)value.GetElement(11), (sbyte)(uint)value.GetElement(12), (sbyte)(uint)value.GetElement(13), (sbyte)(uint)value.GetElement(14), (sbyte)(uint)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt32ToInt16_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((short)(uint)value.GetElement(0), (short)(uint)value.GetElement(1), (short)(uint)value.GetElement(2), (short)(uint)value.GetElement(3), (short)(uint)value.GetElement(4), (short)(uint)value.GetElement(5), (short)(uint)value.GetElement(6), (short)(uint)value.GetElement(7), (short)(uint)value.GetElement(8), (short)(uint)value.GetElement(9), (short)(uint)value.GetElement(10), (short)(uint)value.GetElement(11), (short)(uint)value.GetElement(12), (short)(uint)value.GetElement(13), (short)(uint)value.GetElement(14), (short)(uint)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt32ToInt32_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt32ToUInt8_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((byte)(uint)value.GetElement(0), (byte)(uint)value.GetElement(1), (byte)(uint)value.GetElement(2), (byte)(uint)value.GetElement(3), (byte)(uint)value.GetElement(4), (byte)(uint)value.GetElement(5), (byte)(uint)value.GetElement(6), (byte)(uint)value.GetElement(7), (byte)(uint)value.GetElement(8), (byte)(uint)value.GetElement(9), (byte)(uint)value.GetElement(10), (byte)(uint)value.GetElement(11), (byte)(uint)value.GetElement(12), (byte)(uint)value.GetElement(13), (byte)(uint)value.GetElement(14), (byte)(uint)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt32ToUInt16_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((ushort)(uint)value.GetElement(0), (ushort)(uint)value.GetElement(1), (ushort)(uint)value.GetElement(2), (ushort)(uint)value.GetElement(3), (ushort)(uint)value.GetElement(4), (ushort)(uint)value.GetElement(5), (ushort)(uint)value.GetElement(6), (ushort)(uint)value.GetElement(7), (ushort)(uint)value.GetElement(8), (ushort)(uint)value.GetElement(9), (ushort)(uint)value.GetElement(10), (ushort)(uint)value.GetElement(11), (ushort)(uint)value.GetElement(12), (ushort)(uint)value.GetElement(13), (ushort)(uint)value.GetElement(14), (ushort)(uint)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt32ToUInt32_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt32ToHalf_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.Create((Half)(uint)value.GetElement(0), (Half)(uint)value.GetElement(1), (Half)(uint)value.GetElement(2), (Half)(uint)value.GetElement(3), (Half)(uint)value.GetElement(4), (Half)(uint)value.GetElement(5), (Half)(uint)value.GetElement(6), (Half)(uint)value.GetElement(7), (Half)(uint)value.GetElement(8), (Half)(uint)value.GetElement(9), (Half)(uint)value.GetElement(10), (Half)(uint)value.GetElement(11), (Half)(uint)value.GetElement(12), (Half)(uint)value.GetElement(13), (Half)(uint)value.GetElement(14), (Half)(uint)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertUInt32ToFloat_32(
            Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector512.ConvertToSingle(value).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertHalfToInt8_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((sbyte)(Half)value.GetElement(0), (sbyte)(Half)value.GetElement(1), (sbyte)(Half)value.GetElement(2), (sbyte)(Half)value.GetElement(3), (sbyte)(Half)value.GetElement(4), (sbyte)(Half)value.GetElement(5), (sbyte)(Half)value.GetElement(6), (sbyte)(Half)value.GetElement(7), (sbyte)(Half)value.GetElement(8), (sbyte)(Half)value.GetElement(9), (sbyte)(Half)value.GetElement(10), (sbyte)(Half)value.GetElement(11), (sbyte)(Half)value.GetElement(12), (sbyte)(Half)value.GetElement(13), (sbyte)(Half)value.GetElement(14), (sbyte)(Half)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertHalfToInt16_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((short)(Half)value.GetElement(0), (short)(Half)value.GetElement(1), (short)(Half)value.GetElement(2), (short)(Half)value.GetElement(3), (short)(Half)value.GetElement(4), (short)(Half)value.GetElement(5), (short)(Half)value.GetElement(6), (short)(Half)value.GetElement(7), (short)(Half)value.GetElement(8), (short)(Half)value.GetElement(9), (short)(Half)value.GetElement(10), (short)(Half)value.GetElement(11), (short)(Half)value.GetElement(12), (short)(Half)value.GetElement(13), (short)(Half)value.GetElement(14), (short)(Half)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertHalfToInt32_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((int)(Half)value.GetElement(0), (int)(Half)value.GetElement(1), (int)(Half)value.GetElement(2), (int)(Half)value.GetElement(3), (int)(Half)value.GetElement(4), (int)(Half)value.GetElement(5), (int)(Half)value.GetElement(6), (int)(Half)value.GetElement(7), (int)(Half)value.GetElement(8), (int)(Half)value.GetElement(9), (int)(Half)value.GetElement(10), (int)(Half)value.GetElement(11), (int)(Half)value.GetElement(12), (int)(Half)value.GetElement(13), (int)(Half)value.GetElement(14), (int)(Half)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertHalfToUInt8_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((byte)(Half)value.GetElement(0), (byte)(Half)value.GetElement(1), (byte)(Half)value.GetElement(2), (byte)(Half)value.GetElement(3), (byte)(Half)value.GetElement(4), (byte)(Half)value.GetElement(5), (byte)(Half)value.GetElement(6), (byte)(Half)value.GetElement(7), (byte)(Half)value.GetElement(8), (byte)(Half)value.GetElement(9), (byte)(Half)value.GetElement(10), (byte)(Half)value.GetElement(11), (byte)(Half)value.GetElement(12), (byte)(Half)value.GetElement(13), (byte)(Half)value.GetElement(14), (byte)(Half)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertHalfToUInt16_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((ushort)(Half)value.GetElement(0), (ushort)(Half)value.GetElement(1), (ushort)(Half)value.GetElement(2), (ushort)(Half)value.GetElement(3), (ushort)(Half)value.GetElement(4), (ushort)(Half)value.GetElement(5), (ushort)(Half)value.GetElement(6), (ushort)(Half)value.GetElement(7), (ushort)(Half)value.GetElement(8), (ushort)(Half)value.GetElement(9), (ushort)(Half)value.GetElement(10), (ushort)(Half)value.GetElement(11), (ushort)(Half)value.GetElement(12), (ushort)(Half)value.GetElement(13), (ushort)(Half)value.GetElement(14), (ushort)(Half)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertHalfToUInt32_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((uint)(Half)value.GetElement(0), (uint)(Half)value.GetElement(1), (uint)(Half)value.GetElement(2), (uint)(Half)value.GetElement(3), (uint)(Half)value.GetElement(4), (uint)(Half)value.GetElement(5), (uint)(Half)value.GetElement(6), (uint)(Half)value.GetElement(7), (uint)(Half)value.GetElement(8), (uint)(Half)value.GetElement(9), (uint)(Half)value.GetElement(10), (uint)(Half)value.GetElement(11), (uint)(Half)value.GetElement(12), (uint)(Half)value.GetElement(13), (uint)(Half)value.GetElement(14), (uint)(Half)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertHalfToHalf_32(
            Vector512<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertHalfToFloat_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((float)(Half)value.GetElement(0), (float)(Half)value.GetElement(1), (float)(Half)value.GetElement(2), (float)(Half)value.GetElement(3), (float)(Half)value.GetElement(4), (float)(Half)value.GetElement(5), (float)(Half)value.GetElement(6), (float)(Half)value.GetElement(7), (float)(Half)value.GetElement(8), (float)(Half)value.GetElement(9), (float)(Half)value.GetElement(10), (float)(Half)value.GetElement(11), (float)(Half)value.GetElement(12), (float)(Half)value.GetElement(13), (float)(Half)value.GetElement(14), (float)(Half)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertFloatToInt8_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((sbyte)(float)value.GetElement(0), (sbyte)(float)value.GetElement(1), (sbyte)(float)value.GetElement(2), (sbyte)(float)value.GetElement(3), (sbyte)(float)value.GetElement(4), (sbyte)(float)value.GetElement(5), (sbyte)(float)value.GetElement(6), (sbyte)(float)value.GetElement(7), (sbyte)(float)value.GetElement(8), (sbyte)(float)value.GetElement(9), (sbyte)(float)value.GetElement(10), (sbyte)(float)value.GetElement(11), (sbyte)(float)value.GetElement(12), (sbyte)(float)value.GetElement(13), (sbyte)(float)value.GetElement(14), (sbyte)(float)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertFloatToInt16_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((short)(float)value.GetElement(0), (short)(float)value.GetElement(1), (short)(float)value.GetElement(2), (short)(float)value.GetElement(3), (short)(float)value.GetElement(4), (short)(float)value.GetElement(5), (short)(float)value.GetElement(6), (short)(float)value.GetElement(7), (short)(float)value.GetElement(8), (short)(float)value.GetElement(9), (short)(float)value.GetElement(10), (short)(float)value.GetElement(11), (short)(float)value.GetElement(12), (short)(float)value.GetElement(13), (short)(float)value.GetElement(14), (short)(float)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertFloatToInt32_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.ConvertToInt32(value).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertFloatToUInt8_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((byte)(float)value.GetElement(0), (byte)(float)value.GetElement(1), (byte)(float)value.GetElement(2), (byte)(float)value.GetElement(3), (byte)(float)value.GetElement(4), (byte)(float)value.GetElement(5), (byte)(float)value.GetElement(6), (byte)(float)value.GetElement(7), (byte)(float)value.GetElement(8), (byte)(float)value.GetElement(9), (byte)(float)value.GetElement(10), (byte)(float)value.GetElement(11), (byte)(float)value.GetElement(12), (byte)(float)value.GetElement(13), (byte)(float)value.GetElement(14), (byte)(float)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertFloatToUInt16_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((ushort)(float)value.GetElement(0), (ushort)(float)value.GetElement(1), (ushort)(float)value.GetElement(2), (ushort)(float)value.GetElement(3), (ushort)(float)value.GetElement(4), (ushort)(float)value.GetElement(5), (ushort)(float)value.GetElement(6), (ushort)(float)value.GetElement(7), (ushort)(float)value.GetElement(8), (ushort)(float)value.GetElement(9), (ushort)(float)value.GetElement(10), (ushort)(float)value.GetElement(11), (ushort)(float)value.GetElement(12), (ushort)(float)value.GetElement(13), (ushort)(float)value.GetElement(14), (ushort)(float)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertFloatToUInt32_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.ConvertToUInt32(value).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertFloatToHalf_32(
            Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector512.Create((Half)(float)value.GetElement(0), (Half)(float)value.GetElement(1), (Half)(float)value.GetElement(2), (Half)(float)value.GetElement(3), (Half)(float)value.GetElement(4), (Half)(float)value.GetElement(5), (Half)(float)value.GetElement(6), (Half)(float)value.GetElement(7), (Half)(float)value.GetElement(8), (Half)(float)value.GetElement(9), (Half)(float)value.GetElement(10), (Half)(float)value.GetElement(11), (Half)(float)value.GetElement(12), (Half)(float)value.GetElement(13), (Half)(float)value.GetElement(14), (Half)(float)value.GetElement(15))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ConvertFloatToFloat_32(
            Vector512<int> warp)
        {
            return warp;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ConvertInt64ToInt64_64(
            (Vector512<long>, Vector512<long>) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ConvertInt64ToUInt64_64(
            (Vector512<long>, Vector512<long>) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ConvertInt64ToDouble_64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = warp;
            return (
                Vector512.ConvertToDouble(value.Item1).AsInt64(),
                Vector512.ConvertToDouble(value.Item2).AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ConvertUInt64ToInt64_64(
            (Vector512<long>, Vector512<long>) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ConvertUInt64ToUInt64_64(
            (Vector512<long>, Vector512<long>) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ConvertUInt64ToDouble_64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToU64(warp);
            return (
                Vector512.ConvertToDouble(value.Item1).AsInt64(),
                Vector512.ConvertToDouble(value.Item2).AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ConvertDoubleToInt64_64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            return (
                Vector512.ConvertToInt64(value.Item1).AsInt64(),
                Vector512.ConvertToInt64(value.Item2).AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ConvertDoubleToUInt64_64(
            (Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            return (
                Vector512.ConvertToUInt64(value.Item1).AsInt64(),
                Vector512.ConvertToUInt64(value.Item2).AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ConvertDoubleToDouble_64(
            (Vector512<long>, Vector512<long>) warp)
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
        internal static Vector512<int> Convert64To32I((Vector512<long>, Vector512<long>) warp)
        {
            var value = warp;
            var result = Vector512.Narrow(value.Item1, value.Item2);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> Convert64To32U((Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = Vector512.Narrow(value.Item1, value.Item2);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> Convert64To32F((Vector512<long>, Vector512<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = Vector512.Narrow(value.Item1, value.Item2);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Convert32To64I(Vector512<int> warp)
        {
            var value = warp;
            var result = Vector512.Widen(value);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Convert32To64U(Vector512<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector512.Widen(value);
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) Convert32To64F(Vector512<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector512.Widen(value);
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
        internal static unsafe Vector512<int> AtomicCompareExchange32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> compare,
            Vector512<int> value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            int result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(0)),
                    compare.GetElement(0),
                    value.GetElement(0));
            }
            int result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(1)),
                    compare.GetElement(1),
                    value.GetElement(1));
            }
            int result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(2)),
                    compare.GetElement(2),
                    value.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(3)),
                    compare.GetElement(3),
                    value.GetElement(3));
            }
            int result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(4)),
                    compare.GetElement(4),
                    value.GetElement(4));
            }
            int result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(5)),
                    compare.GetElement(5),
                    value.GetElement(5));
            }
            int result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(6)),
                    compare.GetElement(6),
                    value.GetElement(6));
            }
            int result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(7)),
                    compare.GetElement(7),
                    value.GetElement(7));
            }
            int result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    compare.GetElement(8),
                    value.GetElement(8));
            }
            int result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    compare.GetElement(9),
                    value.GetElement(9));
            }
            int result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(2)),
                    compare.GetElement(10),
                    value.GetElement(10));
            }
            int result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(3)),
                    compare.GetElement(11),
                    value.GetElement(11));
            }
            int result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(4)),
                    compare.GetElement(12),
                    value.GetElement(12));
            }
            int result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(5)),
                    compare.GetElement(13),
                    value.GetElement(13));
            }
            int result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(6)),
                    compare.GetElement(14),
                    value.GetElement(14));
            }
            int result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(7)),
                    compare.GetElement(15),
                    value.GetElement(15));
            }
            return Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15);
        }

        public static readonly MethodInfo AtomicCompareExchange32Method =
            GetMethod(nameof(AtomicCompareExchange32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicCompareExchange64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) compare,
            (Vector512<long>, Vector512<long>) value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            long result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(0)),
                    compare.Item1.GetElement(0),
                    value.Item1.GetElement(0));
            }
            long result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(1)),
                    compare.Item1.GetElement(1),
                    value.Item1.GetElement(1));
            }
            long result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(2)),
                    compare.Item1.GetElement(2),
                    value.Item1.GetElement(2));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(3)),
                    compare.Item1.GetElement(3),
                    value.Item1.GetElement(3));
            }
            long result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(4)),
                    compare.Item1.GetElement(4),
                    value.Item1.GetElement(4));
            }
            long result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(5)),
                    compare.Item1.GetElement(5),
                    value.Item1.GetElement(5));
            }
            long result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(6)),
                    compare.Item1.GetElement(6),
                    value.Item1.GetElement(6));
            }
            long result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(7)),
                    compare.Item1.GetElement(7),
                    value.Item1.GetElement(7));
            }
            long result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    compare.Item2.GetElement(0),
                    value.Item2.GetElement(0));
            }
            long result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    compare.Item2.GetElement(1),
                    value.Item2.GetElement(1));
            }
            long result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(2)),
                    compare.Item2.GetElement(2),
                    value.Item2.GetElement(2));
            }
            long result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(3)),
                    compare.Item2.GetElement(3),
                    value.Item2.GetElement(3));
            }
            long result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(4)),
                    compare.Item2.GetElement(4),
                    value.Item2.GetElement(4));
            }
            long result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(5)),
                    compare.Item2.GetElement(5),
                    value.Item2.GetElement(5));
            }
            long result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(6)),
                    compare.Item2.GetElement(6),
                    value.Item2.GetElement(6));
            }
            long result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(7)),
                    compare.Item2.GetElement(7),
                    value.Item2.GetElement(7));
            }
            return (Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15));
        }

        public static readonly MethodInfo AtomicCompareExchange64Method =
            GetMethod(nameof(AtomicCompareExchange64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicExchangeI32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            int result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            int result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            int result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            int result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            int result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            int result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            int result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            int result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            int result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            int result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            int result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            int result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            int result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            int result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            int result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicExchangeU32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicExchangeF32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToF32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            float result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            float result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            float result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            float result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            float result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            float result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            float result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            float result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            float result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            float result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            float result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            float result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            float result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            float result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            float result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            float result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastFToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicExchangeI64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            long result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            long result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            long result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            long result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            long result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            long result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            long result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            long result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            long result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            long result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            long result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            long result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            long result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            long result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            long result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return (Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicExchangeU64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicExchangeF64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToF64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            double result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            double result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            double result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            double result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            double result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            double result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            double result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            double result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            double result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            double result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            double result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            double result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            double result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            double result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            double result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            double result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastFToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicAddI32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            int result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            int result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            int result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            int result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            int result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            int result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            int result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            int result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            int result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            int result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            int result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            int result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            int result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            int result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            int result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicAddU32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicAddF32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToF32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            float result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            float result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            float result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            float result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            float result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            float result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            float result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            float result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            float result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            float result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            float result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            float result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            float result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            float result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            float result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            float result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastFToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicAddI64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            long result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            long result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            long result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            long result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            long result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            long result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            long result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            long result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            long result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            long result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            long result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            long result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            long result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            long result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            long result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return (Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicAddU64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicAddF64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToF64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            double result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            double result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            double result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            double result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            double result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            double result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            double result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            double result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            double result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            double result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            double result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            double result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            double result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            double result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            double result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            double result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastFToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicMaxI32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            int result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            int result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            int result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            int result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            int result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            int result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            int result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            int result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            int result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            int result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            int result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            int result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            int result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            int result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            int result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicMaxU32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicMaxF32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToF32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            float result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            float result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            float result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            float result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            float result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            float result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            float result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            float result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            float result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            float result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            float result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            float result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            float result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            float result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            float result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            float result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastFToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicMaxI64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            long result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            long result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            long result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            long result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            long result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            long result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            long result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            long result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            long result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            long result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            long result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            long result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            long result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            long result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            long result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return (Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicMaxU64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicMaxF64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToF64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            double result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            double result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            double result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            double result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            double result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            double result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            double result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            double result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            double result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            double result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            double result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            double result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            double result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            double result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            double result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            double result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastFToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicMinI32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            int result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            int result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            int result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            int result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            int result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            int result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            int result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            int result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            int result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            int result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            int result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            int result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            int result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            int result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            int result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicMinU32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicMinF32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToF32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            float result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            float result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            float result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            float result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            float result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            float result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            float result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            float result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            float result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            float result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            float result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            float result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            float result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            float result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            float result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            float result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastFToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicMinI64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            long result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            long result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            long result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            long result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            long result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            long result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            long result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            long result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            long result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            long result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            long result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            long result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            long result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            long result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            long result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return (Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicMinU64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicMinF64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToF64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            double result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            double result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            double result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            double result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            double result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            double result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            double result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            double result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            double result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            double result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            double result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            double result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            double result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            double result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            double result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            double result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastFToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicAndI32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicAndU32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicAndF32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicAndI64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicAndU64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicAndF64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicOrI32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicOrU32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicOrF32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicOrI64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicOrU64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicOrF64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicXorI32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicXorU32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> AtomicXorF32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            Vector512<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            uint result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(0)),
                    sourceValue.GetElement(0));
            }
            uint result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(1)),
                    sourceValue.GetElement(1));
            }
            uint result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(2)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(3)),
                    sourceValue.GetElement(3));
            }
            uint result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(4)),
                    sourceValue.GetElement(4));
            }
            uint result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(5)),
                    sourceValue.GetElement(5));
            }
            uint result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(6)),
                    sourceValue.GetElement(6));
            }
            uint result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item1.GetElement(7)),
                    sourceValue.GetElement(7));
            }
            uint result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(8));
            }
            uint result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(9));
            }
            uint result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(2)),
                    sourceValue.GetElement(10));
            }
            uint result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(3)),
                    sourceValue.GetElement(11));
            }
            uint result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(4)),
                    sourceValue.GetElement(12));
            }
            uint result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(5)),
                    sourceValue.GetElement(13));
            }
            uint result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(6)),
                    sourceValue.GetElement(14));
            }
            uint result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(7)),
                    sourceValue.GetElement(15));
            }
            return CastUToI32(Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicXorI64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicXorU64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) AtomicXorF64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) target,
            (Vector512<long>, Vector512<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            ulong result0 = default;
            if (mask0 != 0)
            {
                result0 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(0)),
                    sourceValue.Item1.GetElement(0));
            }
            ulong result1 = default;
            if (mask1 != 0)
            {
                result1 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(1)),
                    sourceValue.Item1.GetElement(1));
            }
            ulong result2 = default;
            if (mask2 != 0)
            {
                result2 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(2)),
                    sourceValue.Item1.GetElement(2));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(3)),
                    sourceValue.Item1.GetElement(3));
            }
            ulong result4 = default;
            if (mask4 != 0)
            {
                result4 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(4)),
                    sourceValue.Item1.GetElement(4));
            }
            ulong result5 = default;
            if (mask5 != 0)
            {
                result5 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(5)),
                    sourceValue.Item1.GetElement(5));
            }
            ulong result6 = default;
            if (mask6 != 0)
            {
                result6 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(6)),
                    sourceValue.Item1.GetElement(6));
            }
            ulong result7 = default;
            if (mask7 != 0)
            {
                result7 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item1.GetElement(7)),
                    sourceValue.Item1.GetElement(7));
            }
            ulong result8 = default;
            if (mask8 != 0)
            {
                result8 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result9 = default;
            if (mask9 != 0)
            {
                result9 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            ulong result10 = default;
            if (mask10 != 0)
            {
                result10 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(2)),
                    sourceValue.Item2.GetElement(2));
            }
            ulong result11 = default;
            if (mask11 != 0)
            {
                result11 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(3)),
                    sourceValue.Item2.GetElement(3));
            }
            ulong result12 = default;
            if (mask12 != 0)
            {
                result12 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(4)),
                    sourceValue.Item2.GetElement(4));
            }
            ulong result13 = default;
            if (mask13 != 0)
            {
                result13 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(5)),
                    sourceValue.Item2.GetElement(5));
            }
            ulong result14 = default;
            if (mask14 != 0)
            {
                result14 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(6)),
                    sourceValue.Item2.GetElement(6));
            }
            ulong result15 = default;
            if (mask15 != 0)
            {
                result15 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(7)),
                    sourceValue.Item2.GetElement(7));
            }
            return CastUToI64((Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15)));
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
        internal static void ComputeShuffleConfig(
            Vector512<int> width,
            out Vector512<int> lane,
            out Vector512<int> offset)
        {
            lane = RemI32(LoadLaneIndexVector32(), width);
            offset = MulI32(DivI32(lane, width), width);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ShuffleUp32(
            Vector512<int> mask,
            Vector512<int> warp,
            Vector512<int> delta)
        {
            var lane = SubI32(LoadLaneIndexVector32(), delta);
            return Shuffle32(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SubShuffleUp32(
            Vector512<int> mask,
            Vector512<int> warp,
            Vector512<int> delta,
            Vector512<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = SubI32(lane, delta);
            return Shuffle32(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ShuffleUp64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp,
            Vector512<int> delta,
            Vector512<int> width)
        {
            var lane = SubI32(LoadLaneIndexVector32(), delta);
            return Shuffle64(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SubShuffleUp64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp,
            Vector512<int> delta,
            Vector512<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = SubI32(lane, delta);
            return Shuffle64(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ShuffleDown32(
            Vector512<int> mask,
            Vector512<int> warp,
            Vector512<int> delta)
        {
            var lane = AddI32(LoadLaneIndexVector32(), delta);
            return Shuffle32(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SubShuffleDown32(
            Vector512<int> mask,
            Vector512<int> warp,
            Vector512<int> delta,
            Vector512<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = AddI32(lane, delta);
            return Shuffle32(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ShuffleDown64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp,
            Vector512<int> delta)
        {
            var lane = AddI32(LoadLaneIndexVector32(), delta);
            return Shuffle64(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SubShuffleDown64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp,
            Vector512<int> delta,
            Vector512<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = AddI32(lane, delta);
            return Shuffle64(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> ShuffleXor32(
            Vector512<int> mask,
            Vector512<int> warp,
            Vector512<int> laneMask)
        {
            var lane = XorU32(LoadLaneIndexVector32(), laneMask);
            return Shuffle32(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector512<int> SubShuffleXor32(
            Vector512<int> mask,
            Vector512<int> warp,
            Vector512<int> laneMask,
            Vector512<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = XorU32(lane, laneMask);
            return Shuffle32(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) ShuffleXor64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp,
            Vector512<int> laneMask)
        {
            var lane = XorU32(LoadLaneIndexVector32(), laneMask);
            return Shuffle64(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector512<long>, Vector512<long>) SubShuffleXor64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) warp,
            Vector512<int> laneMask,
            Vector512<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = XorU32(lane, laneMask);
            return Shuffle64(mask, warp, AddI32(adjustedLane, offset));
        }

        public static readonly MethodInfo BarrierPopCount32Method =
            GetMethod(nameof(BarrierPopCount32));
        public static readonly MethodInfo BarrierPopCount64Method =
            GetMethod(nameof(BarrierPopCount64));
        public static readonly MethodInfo BarrierAnd32Method =
            GetMethod(nameof(BarrierAnd32));
        public static readonly MethodInfo BarrierAnd64Method =
            GetMethod(nameof(BarrierAnd64));
        public static readonly MethodInfo BarrierOr32Method =
            GetMethod(nameof(BarrierOr32));
        public static readonly MethodInfo BarrierOr64Method =
            GetMethod(nameof(BarrierOr64));
        public static readonly MethodInfo Broadcast32Method =
            GetMethod(nameof(Broadcast32));
        public static readonly MethodInfo Broadcast64Method =
            GetMethod(nameof(Broadcast64));
        public static readonly MethodInfo Shuffle32Method =
            GetMethod(nameof(Shuffle32));
        public static readonly MethodInfo Shuffle64Method =
            GetMethod(nameof(Shuffle64));
        public static readonly MethodInfo ShuffleUp32Method =
            GetMethod(nameof(ShuffleUp32));
        public static readonly MethodInfo SubShuffleUp32Method =
            GetMethod(nameof(SubShuffleUp32));
        public static readonly MethodInfo ShuffleUp64Method =
            GetMethod(nameof(ShuffleUp64));
        public static readonly MethodInfo SubShuffleUp64Method =
            GetMethod(nameof(SubShuffleUp64));
        public static readonly MethodInfo ShuffleDown32Method =
            GetMethod(nameof(ShuffleDown32));
        public static readonly MethodInfo SubShuffleDown32Method =
            GetMethod(nameof(SubShuffleDown32));
        public static readonly MethodInfo ShuffleDown64Method =
            GetMethod(nameof(ShuffleDown64));
        public static readonly MethodInfo SubShuffleDown64Method =
            GetMethod(nameof(SubShuffleDown64));
        public static readonly MethodInfo ShuffleXor32Method =
            GetMethod(nameof(ShuffleXor32));
        public static readonly MethodInfo SubShuffleXor32Method =
            GetMethod(nameof(SubShuffleXor32));
        public static readonly MethodInfo ShuffleXor64Method =
            GetMethod(nameof(ShuffleXor64));
        public static readonly MethodInfo SubShuffleXor64Method =
            GetMethod(nameof(SubShuffleXor64));

        #endregion

        #region IO

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> Load8(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) address)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            var result0 = mask0 != 0
                ? (uint)*(byte*)address.Item1.GetElement(0)
                : 0;
            var result1 = mask1 != 0
                ? (uint)*(byte*)address.Item1.GetElement(1)
                : 0;
            var result2 = mask2 != 0
                ? (uint)*(byte*)address.Item1.GetElement(2)
                : 0;
            var result3 = mask3 != 0
                ? (uint)*(byte*)address.Item1.GetElement(3)
                : 0;
            var result4 = mask4 != 0
                ? (uint)*(byte*)address.Item1.GetElement(4)
                : 0;
            var result5 = mask5 != 0
                ? (uint)*(byte*)address.Item1.GetElement(5)
                : 0;
            var result6 = mask6 != 0
                ? (uint)*(byte*)address.Item1.GetElement(6)
                : 0;
            var result7 = mask7 != 0
                ? (uint)*(byte*)address.Item1.GetElement(7)
                : 0;
            var result8 = mask8 != 0
                ? (uint)*(byte*)address.Item2.GetElement(0)
                : 0;
            var result9 = mask9 != 0
                ? (uint)*(byte*)address.Item2.GetElement(1)
                : 0;
            var result10 = mask10 != 0
                ? (uint)*(byte*)address.Item2.GetElement(2)
                : 0;
            var result11 = mask11 != 0
                ? (uint)*(byte*)address.Item2.GetElement(3)
                : 0;
            var result12 = mask12 != 0
                ? (uint)*(byte*)address.Item2.GetElement(4)
                : 0;
            var result13 = mask13 != 0
                ? (uint)*(byte*)address.Item2.GetElement(5)
                : 0;
            var result14 = mask14 != 0
                ? (uint)*(byte*)address.Item2.GetElement(6)
                : 0;
            var result15 = mask15 != 0
                ? (uint)*(byte*)address.Item2.GetElement(7)
                : 0;
            return Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector512<int> Load16(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) address)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            var result0 = mask0 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(0)
                : 0;
            var result1 = mask1 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(1)
                : 0;
            var result2 = mask2 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(2)
                : 0;
            var result3 = mask3 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(3)
                : 0;
            var result4 = mask4 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(4)
                : 0;
            var result5 = mask5 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(5)
                : 0;
            var result6 = mask6 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(6)
                : 0;
            var result7 = mask7 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(7)
                : 0;
            var result8 = mask8 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(0)
                : 0;
            var result9 = mask9 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(1)
                : 0;
            var result10 = mask10 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(2)
                : 0;
            var result11 = mask11 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(3)
                : 0;
            var result12 = mask12 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(4)
                : 0;
            var result13 = mask13 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(5)
                : 0;
            var result14 = mask14 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(6)
                : 0;
            var result15 = mask15 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(7)
                : 0;
            return Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        private static unsafe Vector512<int> Load32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) address)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            int result0 = mask0 != 0
                ? *(int*)address.Item1.GetElement(0)
                : 0;
            int result1 = mask1 != 0
                ? *(int*)address.Item1.GetElement(1)
                : 0;
            int result2 = mask2 != 0
                ? *(int*)address.Item1.GetElement(2)
                : 0;
            int result3 = mask3 != 0
                ? *(int*)address.Item1.GetElement(3)
                : 0;
            int result4 = mask4 != 0
                ? *(int*)address.Item1.GetElement(4)
                : 0;
            int result5 = mask5 != 0
                ? *(int*)address.Item1.GetElement(5)
                : 0;
            int result6 = mask6 != 0
                ? *(int*)address.Item1.GetElement(6)
                : 0;
            int result7 = mask7 != 0
                ? *(int*)address.Item1.GetElement(7)
                : 0;
            int result8 = mask8 != 0
                ? *(int*)address.Item2.GetElement(0)
                : 0;
            int result9 = mask9 != 0
                ? *(int*)address.Item2.GetElement(1)
                : 0;
            int result10 = mask10 != 0
                ? *(int*)address.Item2.GetElement(2)
                : 0;
            int result11 = mask11 != 0
                ? *(int*)address.Item2.GetElement(3)
                : 0;
            int result12 = mask12 != 0
                ? *(int*)address.Item2.GetElement(4)
                : 0;
            int result13 = mask13 != 0
                ? *(int*)address.Item2.GetElement(5)
                : 0;
            int result14 = mask14 != 0
                ? *(int*)address.Item2.GetElement(6)
                : 0;
            int result15 = mask15 != 0
                ? *(int*)address.Item2.GetElement(7)
                : 0;
            return Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7, result8, result9, result10, result11, result12, result13, result14, result15);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector512<long>, Vector512<long>) Load64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) address)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            long result0 = mask0 != 0
                ? *(long*)address.Item1.GetElement(0)
                : 0;
            long result1 = mask1 != 0
                ? *(long*)address.Item1.GetElement(1)
                : 0;
            long result2 = mask2 != 0
                ? *(long*)address.Item1.GetElement(2)
                : 0;
            long result3 = mask3 != 0
                ? *(long*)address.Item1.GetElement(3)
                : 0;
            long result4 = mask4 != 0
                ? *(long*)address.Item1.GetElement(4)
                : 0;
            long result5 = mask5 != 0
                ? *(long*)address.Item1.GetElement(5)
                : 0;
            long result6 = mask6 != 0
                ? *(long*)address.Item1.GetElement(6)
                : 0;
            long result7 = mask7 != 0
                ? *(long*)address.Item1.GetElement(7)
                : 0;
            long result8 = mask8 != 0
                ? *(long*)address.Item2.GetElement(0)
                : 0;
            long result9 = mask9 != 0
                ? *(long*)address.Item2.GetElement(1)
                : 0;
            long result10 = mask10 != 0
                ? *(long*)address.Item2.GetElement(2)
                : 0;
            long result11 = mask11 != 0
                ? *(long*)address.Item2.GetElement(3)
                : 0;
            long result12 = mask12 != 0
                ? *(long*)address.Item2.GetElement(4)
                : 0;
            long result13 = mask13 != 0
                ? *(long*)address.Item2.GetElement(5)
                : 0;
            long result14 = mask14 != 0
                ? *(long*)address.Item2.GetElement(6)
                : 0;
            long result15 = mask15 != 0
                ? *(long*)address.Item2.GetElement(7)
                : 0;
            return (Vector512.Create(result0, result1, result2, result3, result4, result5, result6, result7), Vector512.Create(result8, result9, result10, result11, result12, result13, result14, result15));
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store8(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) address,
            Vector512<int> value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            byte* addr0 = (byte*)address.Item1.GetElement(0);
            byte* addr1 = (byte*)address.Item1.GetElement(1);
            byte* addr2 = (byte*)address.Item1.GetElement(2);
            byte* addr3 = (byte*)address.Item1.GetElement(3);
            byte* addr4 = (byte*)address.Item1.GetElement(4);
            byte* addr5 = (byte*)address.Item1.GetElement(5);
            byte* addr6 = (byte*)address.Item1.GetElement(6);
            byte* addr7 = (byte*)address.Item1.GetElement(7);
            byte* addr8 = (byte*)address.Item2.GetElement(0);
            byte* addr9 = (byte*)address.Item2.GetElement(1);
            byte* addr10 = (byte*)address.Item2.GetElement(2);
            byte* addr11 = (byte*)address.Item2.GetElement(3);
            byte* addr12 = (byte*)address.Item2.GetElement(4);
            byte* addr13 = (byte*)address.Item2.GetElement(5);
            byte* addr14 = (byte*)address.Item2.GetElement(6);
            byte* addr15 = (byte*)address.Item2.GetElement(7);
            var value0 = (byte)(value.GetElement(0) & 0xff);
            var value1 = (byte)(value.GetElement(1) & 0xff);
            var value2 = (byte)(value.GetElement(2) & 0xff);
            var value3 = (byte)(value.GetElement(3) & 0xff);
            var value4 = (byte)(value.GetElement(4) & 0xff);
            var value5 = (byte)(value.GetElement(5) & 0xff);
            var value6 = (byte)(value.GetElement(6) & 0xff);
            var value7 = (byte)(value.GetElement(7) & 0xff);
            var value8 = (byte)(value.GetElement(8) & 0xff);
            var value9 = (byte)(value.GetElement(9) & 0xff);
            var value10 = (byte)(value.GetElement(10) & 0xff);
            var value11 = (byte)(value.GetElement(11) & 0xff);
            var value12 = (byte)(value.GetElement(12) & 0xff);
            var value13 = (byte)(value.GetElement(13) & 0xff);
            var value14 = (byte)(value.GetElement(14) & 0xff);
            var value15 = (byte)(value.GetElement(15) & 0xff);
            if (mask0 != 0)
                *addr0 = value0;
            if (mask1 != 0)
                *addr1 = value1;
            if (mask2 != 0)
                *addr2 = value2;
            if (mask3 != 0)
                *addr3 = value3;
            if (mask4 != 0)
                *addr4 = value4;
            if (mask5 != 0)
                *addr5 = value5;
            if (mask6 != 0)
                *addr6 = value6;
            if (mask7 != 0)
                *addr7 = value7;
            if (mask8 != 0)
                *addr8 = value8;
            if (mask9 != 0)
                *addr9 = value9;
            if (mask10 != 0)
                *addr10 = value10;
            if (mask11 != 0)
                *addr11 = value11;
            if (mask12 != 0)
                *addr12 = value12;
            if (mask13 != 0)
                *addr13 = value13;
            if (mask14 != 0)
                *addr14 = value14;
            if (mask15 != 0)
                *addr15 = value15;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store16(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) address,
            Vector512<int> value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            short* addr0 = (short*)address.Item1.GetElement(0);
            short* addr1 = (short*)address.Item1.GetElement(1);
            short* addr2 = (short*)address.Item1.GetElement(2);
            short* addr3 = (short*)address.Item1.GetElement(3);
            short* addr4 = (short*)address.Item1.GetElement(4);
            short* addr5 = (short*)address.Item1.GetElement(5);
            short* addr6 = (short*)address.Item1.GetElement(6);
            short* addr7 = (short*)address.Item1.GetElement(7);
            short* addr8 = (short*)address.Item2.GetElement(0);
            short* addr9 = (short*)address.Item2.GetElement(1);
            short* addr10 = (short*)address.Item2.GetElement(2);
            short* addr11 = (short*)address.Item2.GetElement(3);
            short* addr12 = (short*)address.Item2.GetElement(4);
            short* addr13 = (short*)address.Item2.GetElement(5);
            short* addr14 = (short*)address.Item2.GetElement(6);
            short* addr15 = (short*)address.Item2.GetElement(7);
            var value0 = (short)(value.GetElement(0) & 0xffff);
            var value1 = (short)(value.GetElement(1) & 0xffff);
            var value2 = (short)(value.GetElement(2) & 0xffff);
            var value3 = (short)(value.GetElement(3) & 0xffff);
            var value4 = (short)(value.GetElement(4) & 0xffff);
            var value5 = (short)(value.GetElement(5) & 0xffff);
            var value6 = (short)(value.GetElement(6) & 0xffff);
            var value7 = (short)(value.GetElement(7) & 0xffff);
            var value8 = (short)(value.GetElement(8) & 0xffff);
            var value9 = (short)(value.GetElement(9) & 0xffff);
            var value10 = (short)(value.GetElement(10) & 0xffff);
            var value11 = (short)(value.GetElement(11) & 0xffff);
            var value12 = (short)(value.GetElement(12) & 0xffff);
            var value13 = (short)(value.GetElement(13) & 0xffff);
            var value14 = (short)(value.GetElement(14) & 0xffff);
            var value15 = (short)(value.GetElement(15) & 0xffff);
            if (mask0 != 0)
                *addr0 = value0;
            if (mask1 != 0)
                *addr1 = value1;
            if (mask2 != 0)
                *addr2 = value2;
            if (mask3 != 0)
                *addr3 = value3;
            if (mask4 != 0)
                *addr4 = value4;
            if (mask5 != 0)
                *addr5 = value5;
            if (mask6 != 0)
                *addr6 = value6;
            if (mask7 != 0)
                *addr7 = value7;
            if (mask8 != 0)
                *addr8 = value8;
            if (mask9 != 0)
                *addr9 = value9;
            if (mask10 != 0)
                *addr10 = value10;
            if (mask11 != 0)
                *addr11 = value11;
            if (mask12 != 0)
                *addr12 = value12;
            if (mask13 != 0)
                *addr13 = value13;
            if (mask14 != 0)
                *addr14 = value14;
            if (mask15 != 0)
                *addr15 = value15;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store32(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) address,
            Vector512<int> value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            int* addr0 = (int*)address.Item1.GetElement(0);
            int* addr1 = (int*)address.Item1.GetElement(1);
            int* addr2 = (int*)address.Item1.GetElement(2);
            int* addr3 = (int*)address.Item1.GetElement(3);
            int* addr4 = (int*)address.Item1.GetElement(4);
            int* addr5 = (int*)address.Item1.GetElement(5);
            int* addr6 = (int*)address.Item1.GetElement(6);
            int* addr7 = (int*)address.Item1.GetElement(7);
            int* addr8 = (int*)address.Item2.GetElement(0);
            int* addr9 = (int*)address.Item2.GetElement(1);
            int* addr10 = (int*)address.Item2.GetElement(2);
            int* addr11 = (int*)address.Item2.GetElement(3);
            int* addr12 = (int*)address.Item2.GetElement(4);
            int* addr13 = (int*)address.Item2.GetElement(5);
            int* addr14 = (int*)address.Item2.GetElement(6);
            int* addr15 = (int*)address.Item2.GetElement(7);
            var value0 = value.GetElement(0);
            var value1 = value.GetElement(1);
            var value2 = value.GetElement(2);
            var value3 = value.GetElement(3);
            var value4 = value.GetElement(4);
            var value5 = value.GetElement(5);
            var value6 = value.GetElement(6);
            var value7 = value.GetElement(7);
            var value8 = value.GetElement(8);
            var value9 = value.GetElement(9);
            var value10 = value.GetElement(10);
            var value11 = value.GetElement(11);
            var value12 = value.GetElement(12);
            var value13 = value.GetElement(13);
            var value14 = value.GetElement(14);
            var value15 = value.GetElement(15);
            if (mask0 != 0)
                *addr0 = value0;
            if (mask1 != 0)
                *addr1 = value1;
            if (mask2 != 0)
                *addr2 = value2;
            if (mask3 != 0)
                *addr3 = value3;
            if (mask4 != 0)
                *addr4 = value4;
            if (mask5 != 0)
                *addr5 = value5;
            if (mask6 != 0)
                *addr6 = value6;
            if (mask7 != 0)
                *addr7 = value7;
            if (mask8 != 0)
                *addr8 = value8;
            if (mask9 != 0)
                *addr9 = value9;
            if (mask10 != 0)
                *addr10 = value10;
            if (mask11 != 0)
                *addr11 = value11;
            if (mask12 != 0)
                *addr12 = value12;
            if (mask13 != 0)
                *addr13 = value13;
            if (mask14 != 0)
                *addr14 = value14;
            if (mask15 != 0)
                *addr15 = value15;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store64(
            Vector512<int> mask,
            (Vector512<long>, Vector512<long>) address,
            (Vector512<long>, Vector512<long>) value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int mask4 = mask.GetElement(4);
            int mask5 = mask.GetElement(5);
            int mask6 = mask.GetElement(6);
            int mask7 = mask.GetElement(7);
            int mask8 = mask.GetElement(8);
            int mask9 = mask.GetElement(9);
            int mask10 = mask.GetElement(10);
            int mask11 = mask.GetElement(11);
            int mask12 = mask.GetElement(12);
            int mask13 = mask.GetElement(13);
            int mask14 = mask.GetElement(14);
            int mask15 = mask.GetElement(15);
            long* addr0 = (long*)address.Item1.GetElement(0);
            long* addr1 = (long*)address.Item1.GetElement(1);
            long* addr2 = (long*)address.Item1.GetElement(2);
            long* addr3 = (long*)address.Item1.GetElement(3);
            long* addr4 = (long*)address.Item1.GetElement(4);
            long* addr5 = (long*)address.Item1.GetElement(5);
            long* addr6 = (long*)address.Item1.GetElement(6);
            long* addr7 = (long*)address.Item1.GetElement(7);
            long* addr8 = (long*)address.Item2.GetElement(0);
            long* addr9 = (long*)address.Item2.GetElement(1);
            long* addr10 = (long*)address.Item2.GetElement(2);
            long* addr11 = (long*)address.Item2.GetElement(3);
            long* addr12 = (long*)address.Item2.GetElement(4);
            long* addr13 = (long*)address.Item2.GetElement(5);
            long* addr14 = (long*)address.Item2.GetElement(6);
            long* addr15 = (long*)address.Item2.GetElement(7);
            var value0 = value.Item1.GetElement(0);
            var value1 = value.Item1.GetElement(1);
            var value2 = value.Item1.GetElement(2);
            var value3 = value.Item1.GetElement(3);
            var value4 = value.Item1.GetElement(4);
            var value5 = value.Item1.GetElement(5);
            var value6 = value.Item1.GetElement(6);
            var value7 = value.Item1.GetElement(7);
            var value8 = value.Item2.GetElement(0);
            var value9 = value.Item2.GetElement(1);
            var value10 = value.Item2.GetElement(2);
            var value11 = value.Item2.GetElement(3);
            var value12 = value.Item2.GetElement(4);
            var value13 = value.Item2.GetElement(5);
            var value14 = value.Item2.GetElement(6);
            var value15 = value.Item2.GetElement(7);
            if (mask0 != 0)
                *addr0 = value0;
            if (mask1 != 0)
                *addr1 = value1;
            if (mask2 != 0)
                *addr2 = value2;
            if (mask3 != 0)
                *addr3 = value3;
            if (mask4 != 0)
                *addr4 = value4;
            if (mask5 != 0)
                *addr5 = value5;
            if (mask6 != 0)
                *addr6 = value6;
            if (mask7 != 0)
                *addr7 = value7;
            if (mask8 != 0)
                *addr8 = value8;
            if (mask9 != 0)
                *addr9 = value9;
            if (mask10 != 0)
                *addr10 = value10;
            if (mask11 != 0)
                *addr11 = value11;
            if (mask12 != 0)
                *addr12 = value12;
            if (mask13 != 0)
                *addr13 = value13;
            if (mask14 != 0)
                *addr14 = value14;
            if (mask15 != 0)
                *addr15 = value15;
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
            Vector512<int> mask,
            Vector512<int> value,
            string message,
            string fileName,
            int line,
            string method)
        {
            // Check if any lane failed the check
            var failedAssertionMask = XorU32(LoadAllLanesMask32(), value);
            if (BarrierPopCount32Scalar(mask, failedAssertionMask) != 0)
                Trace.Assert(false, message, $"@ {fileName}:{line} in {method}");
        }

        public static readonly MethodInfo DebugAssertFailedMethod =
            GetMethod(nameof(DebugAssertFailed));

        [SuppressMessage(
            "Globalization",
            "CA1303:Do not pass literals as localized parameters",
            Justification = "Basic invariant string")]
        internal static void DumpWarp32(Vector512<int> value, string label)
        {
            Console.Write(label);
            Console.WriteLine(value.ToString());
        }

        public static readonly MethodInfo DumpWarp32Method =
            GetMethod(nameof(DumpWarp32));

        [SuppressMessage(
            "Globalization",
            "CA1303:Do not pass literals as localized parameters",
            Justification = "Basic invariant string")]
        internal static void DumpWarp64((Vector512<long>, Vector512<long>) value, string label)
        {
            Console.Write(label);
            Console.Write(value.Item1.ToString());
            Console.Write(", ");
            Console.WriteLine(value.Item2.ToString());
        }

        public static readonly MethodInfo DumpWarp64Method =
            GetMethod(nameof(DumpWarp64));

        #endregion
    }
}

#endif