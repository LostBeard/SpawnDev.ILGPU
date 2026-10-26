// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: Vec128Operations.tt/Vec128Operations.cs
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

namespace ILGPU.Backends.Velocity.Vec128
{
    // Operation implementations

    static partial class Vec128Operations
    {
        #region Warp Types

        public static int WarpSize => Vector128<int>.Count;
        public static readonly Type WarpType32 = typeof(Vector128<int>);
        public static readonly Type WarpType64 = typeof((Vector128<long>, Vector128<long>));

        #endregion

        #region Initialization

        static Vec128Operations()
        {
            InitUnaryOperations();
            InitBinaryOperations();
            InitTernaryOperations();
            InitializeCompareOperations();
            InitializeConvertOperations();
            InitializeVectorConvertOperations();
            InitializeAtomicOperations();
        }

        private static readonly Vector128<int> WarpSizeM1Vector =
            Vector128.Create(WarpSize - 1);

        internal static MethodInfo GetMethod(string name) =>
            typeof(Vec128Operations).GetMethod(
                    name,
                    BindingFlags.NonPublic | BindingFlags.Static)
                .AsNotNull();

        #endregion

        #region Creation

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<TTarget> CastWarp32<T, TTarget>(Vector128<T> source)
            where T : struct
            where TTarget : struct =>
            source.As<T, TTarget>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<TTarget>, Vector128<TTarget>) CastWarp64<T, TTarget>(
            (Vector128<T>, Vector128<T>) source)
            where T : struct
            where TTarget : struct =>
            (source.Item1.As<T, TTarget>(), source.Item2.As<T, TTarget>());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MaskTo64(Vector128<int> mask) =>
            Vector128.Widen(mask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CheckForAnyActiveLane(Vector128<int> warp) =>
            Vector128.EqualsAny(Vector128<int>.AllBitsSet, warp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CheckForNoActiveLane(Vector128<int> warp) =>
            !CheckForAnyActiveLane(warp);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CheckForEqualMasks(
            Vector128<int> firstMask,
            Vector128<int> secondMask) =>
            Vector128.EqualsAll(firstMask, secondMask);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetNumberOfActiveLanes(Vector128<int> warp) =>
            -Vector128.Sum(warp);

        public static readonly MethodInfo CheckForAnyActiveLaneMethod =
            GetMethod(nameof(CheckForAnyActiveLane));
        public static readonly MethodInfo CheckForNoActiveLaneMethod =
            GetMethod(nameof(CheckForNoActiveLane));
        public static readonly MethodInfo CheckForEqualMasksMethod =
            GetMethod(nameof(CheckForEqualMasks));
        public static readonly MethodInfo GetNumberOfActiveLanesMethod =
            GetMethod(nameof(GetNumberOfActiveLanes));

        private static readonly Vector128<int> LaneIndexVector32 =
            Vector128.Create(0, 1, 2, 3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> LoadLaneIndexVector32() => LaneIndexVector32;

        private static readonly (Vector128<long>, Vector128<long>) LaneIndexVector64 =
            (Vector128.Create(0L, 1L), Vector128.Create(2L, 3L));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) LoadLaneIndexVector64() => LaneIndexVector64;

        public static readonly MethodInfo LoadLaneIndexVector32Method =
            GetMethod(nameof(LoadLaneIndexVector32));
        public static readonly MethodInfo LoadLaneIndexVector64Method =
            GetMethod(nameof(LoadLaneIndexVector64));

        private static readonly Vector128<int> LaneLengthVector32 =
            Vector128.Create(WarpSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> LoadVectorLengthVector32() => LaneLengthVector32;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) LoadVectorLengthVector64()
        {
            long count = WarpSize;
            return (Vector128.Create(count), Vector128.Create(count));
        }

        public static readonly MethodInfo LoadVectorLengthVector32Method =
            GetMethod(nameof(LoadVectorLengthVector32));
        public static readonly MethodInfo LoadVectorLengthVector64Method =
            GetMethod(nameof(LoadVectorLengthVector64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> LoadAllLanesMask32() =>
            Vector128<int>.AllBitsSet;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) LoadAllLanesMask64() =>
            (Vector128<long>.AllBitsSet, Vector128<long>.AllBitsSet);

        public static readonly MethodInfo LoadAllLanesMask32Method =
            GetMethod(nameof(LoadAllLanesMask32));
        public static readonly MethodInfo LoadAllLanesMask64Method =
            GetMethod(nameof(LoadAllLanesMask64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> LoadNoLanesMask32() => default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) LoadNoLanesMask64() => default;

        public static readonly MethodInfo LoadNoLanesMask32Method =
            GetMethod(nameof(LoadNoLanesMask32));
        public static readonly MethodInfo LoadNoLanesMask64Method =
            GetMethod(nameof(LoadNoLanesMask64));

        #endregion

        #region Generic Casts

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CastIToI32(
            Vector128<int> input) =>
            CastWarp32<int, int>(input);

        public static readonly MethodInfo CastIToI32Method =
            GetMethod(nameof(CastIToI32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CastUToI32(
            Vector128<uint> input) =>
            CastWarp32<uint, int>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<uint> CastIToU32(
            Vector128<int> input) =>
            CastWarp32<int, uint>(input);

        public static readonly MethodInfo CastUToI32Method =
            GetMethod(nameof(CastUToI32));

        public static readonly MethodInfo CastIToU32Method =
            GetMethod(nameof(CastIToU32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CastFToI32(
            Vector128<float> input) =>
            CastWarp32<float, int>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<float> CastIToF32(
            Vector128<int> input) =>
            CastWarp32<int, float>(input);

        public static readonly MethodInfo CastFToI32Method =
            GetMethod(nameof(CastFToI32));

        public static readonly MethodInfo CastIToF32Method =
            GetMethod(nameof(CastIToF32));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) CastIToI64(
            (Vector128<long>, Vector128<long>) input) =>
            CastWarp64<long, long>(input);


        public static readonly MethodInfo CastIToI64Method =
            GetMethod(nameof(CastIToI64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) CastUToI64(
            (Vector128<ulong>, Vector128<ulong>) input) =>
            CastWarp64<ulong, long>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<ulong>, Vector128<ulong>) CastIToU64(
            (Vector128<long>, Vector128<long>) input) =>
            CastWarp64<long, ulong>(input);

        public static readonly MethodInfo CastUToI64Method =
            GetMethod(nameof(CastUToI64));

        public static readonly MethodInfo CastIToU64Method =
            GetMethod(nameof(CastIToU64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) CastFToI64(
            (Vector128<double>, Vector128<double>) input) =>
            CastWarp64<double, long>(input);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<double>, Vector128<double>) CastIToF64(
            (Vector128<long>, Vector128<long>) input) =>
            CastWarp64<long, double>(input);

        public static readonly MethodInfo CastFToI64Method =
            GetMethod(nameof(CastFToI64));

        public static readonly MethodInfo CastIToF64Method =
            GetMethod(nameof(CastIToF64));


        #endregion

        #region Scalar Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> FromScalarI32(int scalar)
        {
            var result = Vector128.Create(scalar);
            return result;
        }

        public static readonly MethodInfo FromScalarI32Method =
            GetMethod(nameof(FromScalarI32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> FromScalarU32(uint scalar)
        {
            var result = Vector128.Create(scalar);
            return CastUToI32(result);
        }

        public static readonly MethodInfo FromScalarU32Method =
            GetMethod(nameof(FromScalarU32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> FromScalarF32(float scalar)
        {
            var result = Vector128.Create(scalar);
            return CastFToI32(result);
        }

        public static readonly MethodInfo FromScalarF32Method =
            GetMethod(nameof(FromScalarF32));


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) FromScalarI64(long scalar)
        {
            var result = Vector128.Create(scalar);
            return (result, result);
        }

        public static readonly MethodInfo FromScalarI64Method =
            GetMethod(nameof(FromScalarI64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) FromScalarU64(ulong scalar)
        {
            var result = Vector128.Create(scalar);
            return CastUToI64((result, result));
        }

        public static readonly MethodInfo FromScalarU64Method =
            GetMethod(nameof(FromScalarU64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) FromScalarF64(double scalar)
        {
            var result = Vector128.Create(scalar);
            return CastFToI64((result, result));
        }

        public static readonly MethodInfo FromScalarF64Method =
            GetMethod(nameof(FromScalarF64));


        #endregion

        #region Select Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> Select32(
            Vector128<int> mask,
            Vector128<int> left,
            Vector128<int> right) =>
            Vector128.ConditionalSelect(mask, right, left);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) Select64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) left,
            (Vector128<long>, Vector128<long>) right)
        {
            var mask64 = MaskTo64(mask);
            return (
                Vector128.ConditionalSelect(mask64.Item1, right.Item1, left.Item1),
                Vector128.ConditionalSelect(mask64.Item2, right.Item2, left.Item2));
        }

        public static readonly MethodInfo Select32Method = GetMethod(nameof(Select32));
        public static readonly MethodInfo Select64Method = GetMethod(nameof(Select64));

        #endregion

        #region Unary Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> NegI32(
            Vector128<int> warp)
        {
            var value = warp;
            var result = -value;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> NegU32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            var result = -value;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> NegF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = -value;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) NegI64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = warp;
            var result = (
                    -value.Item1,
                    -value.Item2);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) NegU64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = (
                    -value.Item1,
                    -value.Item2);
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) NegF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    -value.Item1,
                    -value.Item2);
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> NotI32(
            Vector128<int> warp)
        {
            var value = warp;
            var result = ~value;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> NotU32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            var result = ~value;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) NotI64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = warp;
            var result = (
                    ~value.Item1,
                    ~value.Item2);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) NotU64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = (
                    ~value.Item1,
                    ~value.Item2);
            return CastUToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AbsI32(
            Vector128<int> warp)
        {
            var value = warp;
            var result = Vector128.Abs(value);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AbsU32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector128.Abs(value);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AbsF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Abs(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AbsI64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = warp;
            var result = (
                    Vector128.Abs(value.Item1),
                    Vector128.Abs(value.Item2));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AbsU64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = (
                    Vector128.Abs(value.Item1),
                    Vector128.Abs(value.Item2));
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AbsF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    Vector128.Abs(value.Item1),
                    Vector128.Abs(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> PopCI32(
            Vector128<int> warp)
        {
            var value = warp;
            var result = Vector128.Create(IntrinsicMath.BitOperations.PopCount(value.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.GetElement(2)), IntrinsicMath.BitOperations.PopCount(value.GetElement(3)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> PopCU32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector128.Create(IntrinsicMath.BitOperations.PopCount(value.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.GetElement(2)), IntrinsicMath.BitOperations.PopCount(value.GetElement(3)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> PopCI64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = warp;
            var result = Vector128.Create(IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(1)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> PopCU64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = Vector128.Create(IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.PopCount(value.Item2.GetElement(1)));
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CLZI32(
            Vector128<int> warp)
        {
            var value = warp;
            var result = Vector128.Create(IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(2)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(3)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CLZU32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector128.Create(IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(2)), IntrinsicMath.BitOperations.LeadingZeroCount(value.GetElement(3)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CLZI64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = warp;
            var result = Vector128.Create(IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(1)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CLZU64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = Vector128.Create(IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.LeadingZeroCount(value.Item2.GetElement(1)));
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CTZI32(
            Vector128<int> warp)
        {
            var value = warp;
            var result = Vector128.Create(IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(2)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(3)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CTZU32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector128.Create(IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(2)), IntrinsicMath.BitOperations.TrailingZeroCount(value.GetElement(3)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CTZI64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = warp;
            var result = Vector128.Create(IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(1)));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CTZU64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = Vector128.Create(IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item1.GetElement(1)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(0)), IntrinsicMath.BitOperations.TrailingZeroCount(value.Item2.GetElement(1)));
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> RcpFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = RcpImpl(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) RcpFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    RcpImpl(value.Item1),
                    RcpImpl(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> IsNaNFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.IsNaN(value.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.GetElement(3)) ? -1 : 0);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> IsNaNFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item1.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsNaN(value.Item2.GetElement(1)) ? -1 : 0);
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> IsInfFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.GetElement(3)) ? -1 : 0);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> IsInfFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item1.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsInfinity(value.Item2.GetElement(1)) ? -1 : 0);
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> IsFinFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.IsFinite(value.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(2)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.GetElement(3)) ? -1 : 0);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> IsFinFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item1.GetElement(1)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(0)) ? -1 : 0, IntrinsicMath.CPUOnly.IsFinite(value.Item2.GetElement(1)) ? -1 : 0);
            return result;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SqrtFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Sqrt(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SqrtFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    Vector128.Sqrt(value.Item1),
                    Vector128.Sqrt(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> RsqrtFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = RcpImpl(Vector128.Sqrt(value));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) RsqrtFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    RcpImpl(Vector128.Sqrt(value.Item1)),
                    RcpImpl(Vector128.Sqrt(value.Item2)));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AsinFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Asin(value.GetElement(0)), IntrinsicMath.CPUOnly.Asin(value.GetElement(1)), IntrinsicMath.CPUOnly.Asin(value.GetElement(2)), IntrinsicMath.CPUOnly.Asin(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AsinFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Asin(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Asin(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SinFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Sin(value.GetElement(0)), IntrinsicMath.CPUOnly.Sin(value.GetElement(1)), IntrinsicMath.CPUOnly.Sin(value.GetElement(2)), IntrinsicMath.CPUOnly.Sin(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SinFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Sin(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Sin(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SinhFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Sinh(value.GetElement(0)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(1)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(2)), IntrinsicMath.CPUOnly.Sinh(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SinhFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Sinh(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Sinh(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AcosFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Acos(value.GetElement(0)), IntrinsicMath.CPUOnly.Acos(value.GetElement(1)), IntrinsicMath.CPUOnly.Acos(value.GetElement(2)), IntrinsicMath.CPUOnly.Acos(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AcosFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Acos(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Acos(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CosFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Cos(value.GetElement(0)), IntrinsicMath.CPUOnly.Cos(value.GetElement(1)), IntrinsicMath.CPUOnly.Cos(value.GetElement(2)), IntrinsicMath.CPUOnly.Cos(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) CosFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Cos(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Cos(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CoshFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Cosh(value.GetElement(0)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(1)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(2)), IntrinsicMath.CPUOnly.Cosh(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) CoshFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Cosh(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Cosh(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> TanFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Tan(value.GetElement(0)), IntrinsicMath.CPUOnly.Tan(value.GetElement(1)), IntrinsicMath.CPUOnly.Tan(value.GetElement(2)), IntrinsicMath.CPUOnly.Tan(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) TanFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Tan(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Tan(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> TanhFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Tanh(value.GetElement(0)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(1)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(2)), IntrinsicMath.CPUOnly.Tanh(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) TanhFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Tanh(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Tanh(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AtanFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Atan(value.GetElement(0)), IntrinsicMath.CPUOnly.Atan(value.GetElement(1)), IntrinsicMath.CPUOnly.Atan(value.GetElement(2)), IntrinsicMath.CPUOnly.Atan(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AtanFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Atan(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Atan(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ExpFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Exp(value.GetElement(0)), IntrinsicMath.CPUOnly.Exp(value.GetElement(1)), IntrinsicMath.CPUOnly.Exp(value.GetElement(2)), IntrinsicMath.CPUOnly.Exp(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ExpFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Exp(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Exp(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> Exp2FF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Exp2(value.GetElement(0)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(1)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(2)), IntrinsicMath.CPUOnly.Exp2(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) Exp2FF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Exp2(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Exp2(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> FloorFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Floor(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) FloorFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    Vector128.Floor(value.Item1),
                    Vector128.Floor(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CeilingFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Ceiling(value);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) CeilingFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (
                    Vector128.Ceiling(value.Item1),
                    Vector128.Ceiling(value.Item2));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> LogFF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Log(value.GetElement(0)), IntrinsicMath.CPUOnly.Log(value.GetElement(1)), IntrinsicMath.CPUOnly.Log(value.GetElement(2)), IntrinsicMath.CPUOnly.Log(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) LogFF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Log(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Log(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> Log2FF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Log2(value.GetElement(0)), IntrinsicMath.CPUOnly.Log2(value.GetElement(1)), IntrinsicMath.CPUOnly.Log2(value.GetElement(2)), IntrinsicMath.CPUOnly.Log2(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) Log2FF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Log2(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Log2(value.Item2.GetElement(1))));
            return CastFToI64(result);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> Log10FF32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Create(IntrinsicMath.CPUOnly.Log10(value.GetElement(0)), IntrinsicMath.CPUOnly.Log10(value.GetElement(1)), IntrinsicMath.CPUOnly.Log10(value.GetElement(2)), IntrinsicMath.CPUOnly.Log10(value.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) Log10FF64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Log10(value.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Log10(value.Item2.GetElement(1))));
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
        internal static Vector128<T> NonZeroDivisor<T>(Vector128<T> value)
            where T : struct =>
            Vector128.ConditionalSelect(
                Vector128.Equals(value, Vector128<T>.Zero),
                Vector128<T>.One,
                value);

        /// <summary>The 64-bit lane pair form of <see cref="NonZeroDivisor{T}(Vector128{T})"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<T>, Vector128<T>) NonZeroDivisor<T>(
            (Vector128<T>, Vector128<T>) value)
            where T : struct =>
            (NonZeroDivisor(value.Item1), NonZeroDivisor(value.Item2));

        /// <summary>
        /// The divisor of a signed integer division or remainder with every zero lane, and every -1 lane whose dividend is MinValue, replaced by one: a
        /// vector division throws <see cref="OverflowException"/> for MinValue / -1 in any lane, the unused ones included (see
        /// <see cref="NonZeroDivisor{T}(Vector128{T})"/>). MinValue / 1 = MinValue and MinValue % 1 = 0 are the two's complement results of MinValue / -1.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SafeSignedDivisor(Vector128<int> left, Vector128<int> right) =>
            Vector128.ConditionalSelect(
                Vector128.Equals(right, Vector128<int>.Zero) |
                    (Vector128.Equals(right, Vector128<int>.AllBitsSet) &
                    Vector128.Equals(left, Vector128.Create(int.MinValue))),
                Vector128<int>.One,
                right);

        /// <summary>The 64-bit form of <see cref="SafeSignedDivisor(Vector128{int}, Vector128{int})"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<long> SafeSignedDivisor(Vector128<long> left, Vector128<long> right) =>
            Vector128.ConditionalSelect(
                Vector128.Equals(right, Vector128<long>.Zero) |
                    (Vector128.Equals(right, Vector128<long>.AllBitsSet) &
                    Vector128.Equals(left, Vector128.Create(long.MinValue))),
                Vector128<long>.One,
                right);

        /// <summary>The 64-bit lane pair form of <see cref="SafeSignedDivisor(Vector128{long}, Vector128{long})"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SafeSignedDivisor(
            (Vector128<long>, Vector128<long>) left,
            (Vector128<long>, Vector128<long>) right) =>
            (SafeSignedDivisor(left.Item1, right.Item1), SafeSignedDivisor(left.Item2, right.Item2));

        #region Binary Operations

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AddI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = left + right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AddU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = left + right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AddF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = left + right;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AddI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                left.Item1 + right.Item1,
                left.Item2 + right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AddU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                left.Item1 + right.Item1,
                left.Item2 + right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AddF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                left.Item1 + right.Item1,
                left.Item2 + right.Item2);

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SubI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = left - right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SubU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = left - right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SubF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = left - right;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SubI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                left.Item1 - right.Item1,
                left.Item2 - right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SubU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                left.Item1 - right.Item1,
                left.Item2 - right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SubF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                left.Item1 - right.Item1,
                left.Item2 - right.Item2);

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MulI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = left * right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MulU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = left * right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MulF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = left * right;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MulI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                left.Item1 * right.Item1,
                left.Item2 * right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MulU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                left.Item1 * right.Item1,
                left.Item2 * right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MulF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                left.Item1 * right.Item1,
                left.Item2 * right.Item2);

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> DivI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = SafeSignedDivisor(left, second);

            var result = left / right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> DivU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = NonZeroDivisor(CastIToU32(second));

            var result = left / right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> DivF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = left / right;
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) DivI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = SafeSignedDivisor(left, second);

            var result = (
                left.Item1 / right.Item1,
                left.Item2 / right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) DivU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = NonZeroDivisor(CastIToU64(second));

            var result = (
                left.Item1 / right.Item1,
                left.Item2 / right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) DivF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                left.Item1 / right.Item1,
                left.Item2 / right.Item2);

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> RemI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = SafeSignedDivisor(left, second);

            var result = left - left / right * right;
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> RemU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = NonZeroDivisor(CastIToU32(second));

            var result = left - left / right * right;
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> RemF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            // x - x / y * y has no truncation: a float remainder is the IEEE fmod of every lane (what the CPU backend computes)
            var result = Vector128.Create(left.GetElement(0) % right.GetElement(0), left.GetElement(1) % right.GetElement(1), left.GetElement(2) % right.GetElement(2), left.GetElement(3) % right.GetElement(3));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) RemI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = SafeSignedDivisor(left, second);

            var result = (
                left.Item1 - left.Item1 / right.Item1 * right.Item1,
                left.Item2 - left.Item2 / right.Item2 * right.Item2);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) RemU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = NonZeroDivisor(CastIToU64(second));

            var result = (
                left.Item1 - left.Item1 / right.Item1 * right.Item1,
                left.Item2 - left.Item2 / right.Item2 * right.Item2);

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) RemF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            // x - x / y * y has no truncation: a float remainder is the IEEE fmod of every lane (what the CPU backend computes)
            var result = (Vector128.Create(left.Item1.GetElement(0) % right.Item1.GetElement(0), left.Item1.GetElement(1) % right.Item1.GetElement(1)), Vector128.Create(left.Item2.GetElement(0) % right.Item2.GetElement(0), left.Item2.GetElement(1) % right.Item2.GetElement(1)));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AndI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector128.BitwiseAnd(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> AndU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector128.BitwiseAnd(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AndI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector128.BitwiseAnd(left.Item1, right.Item1),
                Vector128.BitwiseAnd(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) AndU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector128.BitwiseAnd(left.Item1, right.Item1),
                Vector128.BitwiseAnd(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> OrI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector128.BitwiseOr(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> OrU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector128.BitwiseOr(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) OrI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector128.BitwiseOr(left.Item1, right.Item1),
                Vector128.BitwiseOr(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) OrU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector128.BitwiseOr(left.Item1, right.Item1),
                Vector128.BitwiseOr(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> XorI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector128.Xor(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> XorU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector128.Xor(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) XorI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector128.Xor(left.Item1, right.Item1),
                Vector128.Xor(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) XorU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector128.Xor(left.Item1, right.Item1),
                Vector128.Xor(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ShlI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector128.Create(left.GetElement(0) << (int)right.GetElement(0), left.GetElement(1) << (int)right.GetElement(1), left.GetElement(2) << (int)right.GetElement(2), left.GetElement(3) << (int)right.GetElement(3));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ShlU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector128.Create(left.GetElement(0) << (int)right.GetElement(0), left.GetElement(1) << (int)right.GetElement(1), left.GetElement(2) << (int)right.GetElement(2), left.GetElement(3) << (int)right.GetElement(3));
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ShlI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (Vector128.Create(left.Item1.GetElement(0) << (int)right.Item1.GetElement(0), left.Item1.GetElement(1) << (int)right.Item1.GetElement(1)), Vector128.Create(left.Item2.GetElement(0) << (int)right.Item2.GetElement(0), left.Item2.GetElement(1) << (int)right.Item2.GetElement(1)));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ShlU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (Vector128.Create(left.Item1.GetElement(0) << (int)right.Item1.GetElement(0), left.Item1.GetElement(1) << (int)right.Item1.GetElement(1)), Vector128.Create(left.Item2.GetElement(0) << (int)right.Item2.GetElement(0), left.Item2.GetElement(1) << (int)right.Item2.GetElement(1)));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ShrI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector128.Create(left.GetElement(0) >> (int)right.GetElement(0), left.GetElement(1) >> (int)right.GetElement(1), left.GetElement(2) >> (int)right.GetElement(2), left.GetElement(3) >> (int)right.GetElement(3));
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ShrU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector128.Create(left.GetElement(0) >> (int)right.GetElement(0), left.GetElement(1) >> (int)right.GetElement(1), left.GetElement(2) >> (int)right.GetElement(2), left.GetElement(3) >> (int)right.GetElement(3));
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ShrI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (Vector128.Create(left.Item1.GetElement(0) >> (int)right.Item1.GetElement(0), left.Item1.GetElement(1) >> (int)right.Item1.GetElement(1)), Vector128.Create(left.Item2.GetElement(0) >> (int)right.Item2.GetElement(0), left.Item2.GetElement(1) >> (int)right.Item2.GetElement(1)));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ShrU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (Vector128.Create(left.Item1.GetElement(0) >> (int)right.Item1.GetElement(0), left.Item1.GetElement(1) >> (int)right.Item1.GetElement(1)), Vector128.Create(left.Item2.GetElement(0) >> (int)right.Item2.GetElement(0), left.Item2.GetElement(1) >> (int)right.Item2.GetElement(1)));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MinI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector128.Min(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MinU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector128.Min(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MinF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector128.Min(left, right);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MinI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector128.Min(left.Item1, right.Item1),
                Vector128.Min(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MinU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector128.Min(left.Item1, right.Item1),
                Vector128.Min(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MinF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                Vector128.Min(left.Item1, right.Item1),
                Vector128.Min(left.Item2, right.Item2));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MaxI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            var result = Vector128.Max(left, right);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MaxU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            var result = Vector128.Max(left, right);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MaxF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector128.Max(left, right);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MaxI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result = (
                Vector128.Max(left.Item1, right.Item1),
                Vector128.Max(left.Item2, right.Item2));

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MaxU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result = (
                Vector128.Max(left.Item1, right.Item1),
                Vector128.Max(left.Item2, right.Item2));

            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MaxF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (
                Vector128.Max(left.Item1, right.Item1),
                Vector128.Max(left.Item2, right.Item2));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> Atan2FF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector128.Create(IntrinsicMath.CPUOnly.Atan2(left.GetElement(0), right.GetElement(0)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(1), right.GetElement(1)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(2), right.GetElement(2)), IntrinsicMath.CPUOnly.Atan2(left.GetElement(3), right.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) Atan2FF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(0), right.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Atan2(left.Item1.GetElement(1), right.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(0), right.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Atan2(left.Item2.GetElement(1), right.Item2.GetElement(1))));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> PowFF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector128.Create(IntrinsicMath.CPUOnly.Pow(left.GetElement(0), right.GetElement(0)), IntrinsicMath.CPUOnly.Pow(left.GetElement(1), right.GetElement(1)), IntrinsicMath.CPUOnly.Pow(left.GetElement(2), right.GetElement(2)), IntrinsicMath.CPUOnly.Pow(left.GetElement(3), right.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) PowFF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(0), right.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Pow(left.Item1.GetElement(1), right.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(0), right.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Pow(left.Item2.GetElement(1), right.Item2.GetElement(1))));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> BinaryLogFF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector128.Create(IntrinsicMath.CPUOnly.Log(left.GetElement(0), right.GetElement(0)), IntrinsicMath.CPUOnly.Log(left.GetElement(1), right.GetElement(1)), IntrinsicMath.CPUOnly.Log(left.GetElement(2), right.GetElement(2)), IntrinsicMath.CPUOnly.Log(left.GetElement(3), right.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) BinaryLogFF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (Vector128.Create(IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(0), right.Item1.GetElement(0)), IntrinsicMath.CPUOnly.Log(left.Item1.GetElement(1), right.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(0), right.Item2.GetElement(0)), IntrinsicMath.CPUOnly.Log(left.Item2.GetElement(1), right.Item2.GetElement(1))));

            return CastFToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CopySignFF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            var result = Vector128.Create(IntrinsicMath.CopySign(left.GetElement(0), right.GetElement(0)), IntrinsicMath.CopySign(left.GetElement(1), right.GetElement(1)), IntrinsicMath.CopySign(left.GetElement(2), right.GetElement(2)), IntrinsicMath.CopySign(left.GetElement(3), right.GetElement(3)));
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) CopySignFF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result = (Vector128.Create(IntrinsicMath.CopySign(left.Item1.GetElement(0), right.Item1.GetElement(0)), IntrinsicMath.CopySign(left.Item1.GetElement(1), right.Item1.GetElement(1))), Vector128.Create(IntrinsicMath.CopySign(left.Item2.GetElement(0), right.Item2.GetElement(0)), IntrinsicMath.CopySign(left.Item2.GetElement(1), right.Item2.GetElement(1))));

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
        internal static Vector128<int> MultiplyAddI32(
            Vector128<int> first,
            Vector128<int> second,
            Vector128<int> third)
        {
            var a = first;
            var b = second;
            var c = third;

            var result = Vector128.Add(Vector128.Multiply(a, b), c);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MultiplyAddU32(
            Vector128<int> first,
            Vector128<int> second,
            Vector128<int> third)
        {
            var a = CastIToU32(first);
            var b = CastIToU32(second);
            var c = CastIToU32(third);

            var result = Vector128.Add(Vector128.Multiply(a, b), c);

            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> MultiplyAddF32(
            Vector128<int> first,
            Vector128<int> second,
            Vector128<int> third)
        {
            var a = CastIToF32(first);
            var b = CastIToF32(second);
            var c = CastIToF32(third);

            var result = Vector128.Add(Vector128.Multiply(a, b), c);

            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MultiplyAddI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second,
            (Vector128<long>, Vector128<long>) third)
        {
            var a = first;
            var b = second;
            var c = third;

            var result1 = Vector128.Add(Vector128.Multiply(a.Item1, b.Item1), c.Item1);
            var result2 = Vector128.Add(Vector128.Multiply(a.Item2, b.Item2), c.Item2);

            return (result1, result2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MultiplyAddU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second,
            (Vector128<long>, Vector128<long>) third)
        {
            var a = CastIToU64(first);
            var b = CastIToU64(second);
            var c = CastIToU64(third);

            var result1 = Vector128.Add(Vector128.Multiply(a.Item1, b.Item1), c.Item1);
            var result2 = Vector128.Add(Vector128.Multiply(a.Item2, b.Item2), c.Item2);

            return CastUToI64((result1, result2));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) MultiplyAddF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second,
            (Vector128<long>, Vector128<long>) third)
        {
            var a = CastIToF64(first);
            var b = CastIToF64(second);
            var c = CastIToF64(third);

            var result1 = Vector128.Add(Vector128.Multiply(a.Item1, b.Item1), c.Item1);
            var result2 = Vector128.Add(Vector128.Multiply(a.Item2, b.Item2), c.Item2);

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
        internal static Vector128<int> CompareEqualI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            return Vector128.Equals(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareEqualU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector128.Equals(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareEqualF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector128.Equals(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareEqualI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector128.Equals(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.Equals(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareEqualU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector128.Equals(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.Equals(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareEqualF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector128.Equals(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.Equals(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareNotEqualI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            return NotI32(Vector128.Equals(left, right).AsInt32());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareNotEqualU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return NotI32(Vector128.Equals(left, right).AsInt32());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareNotEqualF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return NotI32(Vector128.Equals(left, right).AsInt32());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareNotEqualI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  NotI32(Vector128.Equals(left.Item1, right.Item1).AsInt32());
            var result2 =  NotI32(Vector128.Equals(left.Item2, right.Item2).AsInt32());

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareNotEqualU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  NotI32(Vector128.Equals(left.Item1, right.Item1).AsInt32());
            var result2 =  NotI32(Vector128.Equals(left.Item2, right.Item2).AsInt32());

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareNotEqualF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  NotI32(Vector128.Equals(left.Item1, right.Item1).AsInt32());
            var result2 =  NotI32(Vector128.Equals(left.Item2, right.Item2).AsInt32());

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessThanI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            return Vector128.LessThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessThanU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector128.LessThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessThanF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector128.LessThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessThanI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector128.LessThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.LessThan(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessThanU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector128.LessThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.LessThan(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessThanF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector128.LessThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.LessThan(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessEqualI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            return Vector128.LessThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessEqualU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector128.LessThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessEqualF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector128.LessThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessEqualI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector128.LessThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.LessThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessEqualU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector128.LessThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.LessThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareLessEqualF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector128.LessThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.LessThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterThanI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            return Vector128.GreaterThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterThanU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector128.GreaterThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterThanF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector128.GreaterThan(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterThanI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector128.GreaterThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.GreaterThan(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterThanU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector128.GreaterThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.GreaterThan(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterThanF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector128.GreaterThan(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.GreaterThan(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterEqualI32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = first;
            var right = second;

            return Vector128.GreaterThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterEqualU32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToU32(first);
            var right = CastIToU32(second);

            return Vector128.GreaterThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterEqualF32(
            Vector128<int> first,
            Vector128<int> second)
        {
            var left = CastIToF32(first);
            var right = CastIToF32(second);

            return Vector128.GreaterThanOrEqual(left, right).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterEqualI64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = first;
            var right = second;

            var result1 =  Vector128.GreaterThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.GreaterThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterEqualU64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToU64(first);
            var right = CastIToU64(second);

            var result1 =  Vector128.GreaterThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.GreaterThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> CompareGreaterEqualF64(
            (Vector128<long>, Vector128<long>) first,
            (Vector128<long>, Vector128<long>) second)
        {
            var left = CastIToF64(first);
            var right = CastIToF64(second);

            var result1 =  Vector128.GreaterThanOrEqual(left.Item1, right.Item1).AsInt32();
            var result2 =  Vector128.GreaterThanOrEqual(left.Item2, right.Item2).AsInt32();

            return Vector128.Narrow(result1.AsInt64(), result2.AsInt64());
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
        internal static Vector128<int> ConvertInt8ToInt8_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt8ToInt16_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((short)(sbyte)value.GetElement(0), (short)(sbyte)value.GetElement(1), (short)(sbyte)value.GetElement(2), (short)(sbyte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt8ToInt32_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((int)(sbyte)value.GetElement(0), (int)(sbyte)value.GetElement(1), (int)(sbyte)value.GetElement(2), (int)(sbyte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt8ToUInt8_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt8ToUInt16_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((ushort)(sbyte)value.GetElement(0), (ushort)(sbyte)value.GetElement(1), (ushort)(sbyte)value.GetElement(2), (ushort)(sbyte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt8ToUInt32_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((uint)(sbyte)value.GetElement(0), (uint)(sbyte)value.GetElement(1), (uint)(sbyte)value.GetElement(2), (uint)(sbyte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt8ToHalf_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((Half)(sbyte)value.GetElement(0), (Half)(sbyte)value.GetElement(1), (Half)(sbyte)value.GetElement(2), (Half)(sbyte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt8ToFloat_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((float)(sbyte)value.GetElement(0), (float)(sbyte)value.GetElement(1), (float)(sbyte)value.GetElement(2), (float)(sbyte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt16ToInt8_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((sbyte)(short)value.GetElement(0), (sbyte)(short)value.GetElement(1), (sbyte)(short)value.GetElement(2), (sbyte)(short)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt16ToInt16_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt16ToInt32_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((int)(short)value.GetElement(0), (int)(short)value.GetElement(1), (int)(short)value.GetElement(2), (int)(short)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt16ToUInt8_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((byte)(short)value.GetElement(0), (byte)(short)value.GetElement(1), (byte)(short)value.GetElement(2), (byte)(short)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt16ToUInt16_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt16ToUInt32_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((uint)(short)value.GetElement(0), (uint)(short)value.GetElement(1), (uint)(short)value.GetElement(2), (uint)(short)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt16ToHalf_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((Half)(short)value.GetElement(0), (Half)(short)value.GetElement(1), (Half)(short)value.GetElement(2), (Half)(short)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt16ToFloat_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((float)(short)value.GetElement(0), (float)(short)value.GetElement(1), (float)(short)value.GetElement(2), (float)(short)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt32ToInt8_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((sbyte)(int)value.GetElement(0), (sbyte)(int)value.GetElement(1), (sbyte)(int)value.GetElement(2), (sbyte)(int)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt32ToInt16_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((short)(int)value.GetElement(0), (short)(int)value.GetElement(1), (short)(int)value.GetElement(2), (short)(int)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt32ToInt32_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt32ToUInt8_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((byte)(int)value.GetElement(0), (byte)(int)value.GetElement(1), (byte)(int)value.GetElement(2), (byte)(int)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt32ToUInt16_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((ushort)(int)value.GetElement(0), (ushort)(int)value.GetElement(1), (ushort)(int)value.GetElement(2), (ushort)(int)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt32ToUInt32_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt32ToHalf_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.Create((Half)(int)value.GetElement(0), (Half)(int)value.GetElement(1), (Half)(int)value.GetElement(2), (Half)(int)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertInt32ToFloat_32(
            Vector128<int> warp)
        {
            var value = warp;
            return Vector128.ConvertToSingle(value).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt8ToInt8_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt8ToInt16_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((short)(byte)value.GetElement(0), (short)(byte)value.GetElement(1), (short)(byte)value.GetElement(2), (short)(byte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt8ToInt32_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((int)(byte)value.GetElement(0), (int)(byte)value.GetElement(1), (int)(byte)value.GetElement(2), (int)(byte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt8ToUInt8_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt8ToUInt16_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((ushort)(byte)value.GetElement(0), (ushort)(byte)value.GetElement(1), (ushort)(byte)value.GetElement(2), (ushort)(byte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt8ToUInt32_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((uint)(byte)value.GetElement(0), (uint)(byte)value.GetElement(1), (uint)(byte)value.GetElement(2), (uint)(byte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt8ToHalf_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((Half)(byte)value.GetElement(0), (Half)(byte)value.GetElement(1), (Half)(byte)value.GetElement(2), (Half)(byte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt8ToFloat_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((float)(byte)value.GetElement(0), (float)(byte)value.GetElement(1), (float)(byte)value.GetElement(2), (float)(byte)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt16ToInt8_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((sbyte)(ushort)value.GetElement(0), (sbyte)(ushort)value.GetElement(1), (sbyte)(ushort)value.GetElement(2), (sbyte)(ushort)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt16ToInt16_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt16ToInt32_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((int)(ushort)value.GetElement(0), (int)(ushort)value.GetElement(1), (int)(ushort)value.GetElement(2), (int)(ushort)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt16ToUInt8_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((byte)(ushort)value.GetElement(0), (byte)(ushort)value.GetElement(1), (byte)(ushort)value.GetElement(2), (byte)(ushort)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt16ToUInt16_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt16ToUInt32_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((uint)(ushort)value.GetElement(0), (uint)(ushort)value.GetElement(1), (uint)(ushort)value.GetElement(2), (uint)(ushort)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt16ToHalf_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((Half)(ushort)value.GetElement(0), (Half)(ushort)value.GetElement(1), (Half)(ushort)value.GetElement(2), (Half)(ushort)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt16ToFloat_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((float)(ushort)value.GetElement(0), (float)(ushort)value.GetElement(1), (float)(ushort)value.GetElement(2), (float)(ushort)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt32ToInt8_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((sbyte)(uint)value.GetElement(0), (sbyte)(uint)value.GetElement(1), (sbyte)(uint)value.GetElement(2), (sbyte)(uint)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt32ToInt16_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((short)(uint)value.GetElement(0), (short)(uint)value.GetElement(1), (short)(uint)value.GetElement(2), (short)(uint)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt32ToInt32_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt32ToUInt8_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((byte)(uint)value.GetElement(0), (byte)(uint)value.GetElement(1), (byte)(uint)value.GetElement(2), (byte)(uint)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt32ToUInt16_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((ushort)(uint)value.GetElement(0), (ushort)(uint)value.GetElement(1), (ushort)(uint)value.GetElement(2), (ushort)(uint)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt32ToUInt32_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt32ToHalf_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.Create((Half)(uint)value.GetElement(0), (Half)(uint)value.GetElement(1), (Half)(uint)value.GetElement(2), (Half)(uint)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertUInt32ToFloat_32(
            Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            return Vector128.ConvertToSingle(value).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertHalfToInt8_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((sbyte)(Half)value.GetElement(0), (sbyte)(Half)value.GetElement(1), (sbyte)(Half)value.GetElement(2), (sbyte)(Half)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertHalfToInt16_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((short)(Half)value.GetElement(0), (short)(Half)value.GetElement(1), (short)(Half)value.GetElement(2), (short)(Half)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertHalfToInt32_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((int)(Half)value.GetElement(0), (int)(Half)value.GetElement(1), (int)(Half)value.GetElement(2), (int)(Half)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertHalfToUInt8_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((byte)(Half)value.GetElement(0), (byte)(Half)value.GetElement(1), (byte)(Half)value.GetElement(2), (byte)(Half)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertHalfToUInt16_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((ushort)(Half)value.GetElement(0), (ushort)(Half)value.GetElement(1), (ushort)(Half)value.GetElement(2), (ushort)(Half)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertHalfToUInt32_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((uint)(Half)value.GetElement(0), (uint)(Half)value.GetElement(1), (uint)(Half)value.GetElement(2), (uint)(Half)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertHalfToHalf_32(
            Vector128<int> warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertHalfToFloat_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((float)(Half)value.GetElement(0), (float)(Half)value.GetElement(1), (float)(Half)value.GetElement(2), (float)(Half)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertFloatToInt8_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((sbyte)(float)value.GetElement(0), (sbyte)(float)value.GetElement(1), (sbyte)(float)value.GetElement(2), (sbyte)(float)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertFloatToInt16_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((short)(float)value.GetElement(0), (short)(float)value.GetElement(1), (short)(float)value.GetElement(2), (short)(float)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertFloatToInt32_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.ConvertToInt32(value).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertFloatToUInt8_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((byte)(float)value.GetElement(0), (byte)(float)value.GetElement(1), (byte)(float)value.GetElement(2), (byte)(float)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertFloatToUInt16_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((ushort)(float)value.GetElement(0), (ushort)(float)value.GetElement(1), (ushort)(float)value.GetElement(2), (ushort)(float)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertFloatToUInt32_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.ConvertToUInt32(value).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertFloatToHalf_32(
            Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            return Vector128.Create((Half)(float)value.GetElement(0), (Half)(float)value.GetElement(1), (Half)(float)value.GetElement(2), (Half)(float)value.GetElement(3))
                .AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ConvertFloatToFloat_32(
            Vector128<int> warp)
        {
            return warp;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ConvertInt64ToInt64_64(
            (Vector128<long>, Vector128<long>) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ConvertInt64ToUInt64_64(
            (Vector128<long>, Vector128<long>) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ConvertInt64ToDouble_64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = warp;
            return (
                Vector128.ConvertToDouble(value.Item1).AsInt64(),
                Vector128.ConvertToDouble(value.Item2).AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ConvertUInt64ToInt64_64(
            (Vector128<long>, Vector128<long>) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ConvertUInt64ToUInt64_64(
            (Vector128<long>, Vector128<long>) warp)
        {
            return warp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ConvertUInt64ToDouble_64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToU64(warp);
            return (
                Vector128.ConvertToDouble(value.Item1).AsInt64(),
                Vector128.ConvertToDouble(value.Item2).AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ConvertDoubleToInt64_64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            return (
                Vector128.ConvertToInt64(value.Item1).AsInt64(),
                Vector128.ConvertToInt64(value.Item2).AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ConvertDoubleToUInt64_64(
            (Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            return (
                Vector128.ConvertToUInt64(value.Item1).AsInt64(),
                Vector128.ConvertToUInt64(value.Item2).AsInt64());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ConvertDoubleToDouble_64(
            (Vector128<long>, Vector128<long>) warp)
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
        internal static Vector128<int> Convert64To32I((Vector128<long>, Vector128<long>) warp)
        {
            var value = warp;
            var result = Vector128.Narrow(value.Item1, value.Item2);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> Convert64To32U((Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToU64(warp);
            var result = Vector128.Narrow(value.Item1, value.Item2);
            return CastUToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> Convert64To32F((Vector128<long>, Vector128<long>) warp)
        {
            var value = CastIToF64(warp);
            var result = Vector128.Narrow(value.Item1, value.Item2);
            return CastFToI32(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) Convert32To64I(Vector128<int> warp)
        {
            var value = warp;
            var result = Vector128.Widen(value);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) Convert32To64U(Vector128<int> warp)
        {
            var value = CastIToU32(warp);
            var result = Vector128.Widen(value);
            return CastUToI64(result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) Convert32To64F(Vector128<int> warp)
        {
            var value = CastIToF32(warp);
            var result = Vector128.Widen(value);
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
        internal static unsafe Vector128<int> AtomicCompareExchange32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> compare,
            Vector128<int> value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    compare.GetElement(2),
                    value.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    compare.GetElement(3),
                    value.GetElement(3));
            }
            return Vector128.Create(result0, result1, result2, result3);
        }

        public static readonly MethodInfo AtomicCompareExchange32Method =
            GetMethod(nameof(AtomicCompareExchange32));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicCompareExchange64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) compare,
            (Vector128<long>, Vector128<long>) value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    compare.Item2.GetElement(0),
                    value.Item2.GetElement(0));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.CompareExchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    compare.Item2.GetElement(1),
                    value.Item2.GetElement(1));
            }
            return (Vector128.Create(result0, result1), Vector128.Create(result2, result3));
        }

        public static readonly MethodInfo AtomicCompareExchange64Method =
            GetMethod(nameof(AtomicCompareExchange64));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicExchangeI32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return Vector128.Create(result0, result1, result2, result3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicExchangeU32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicExchangeF32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToF32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            float result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastFToI32(Vector128.Create(result0, result1, result2, result3));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicExchangeI64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return (Vector128.Create(result0, result1), Vector128.Create(result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicExchangeU64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicExchangeF64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToF64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            double result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Exchange(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastFToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicAddI32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return Vector128.Create(result0, result1, result2, result3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicAddU32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicAddF32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToF32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            float result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastFToI32(Vector128.Create(result0, result1, result2, result3));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicAddI64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return (Vector128.Create(result0, result1), Vector128.Create(result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicAddU64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicAddF64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToF64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            double result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Add(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastFToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicMaxI32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return Vector128.Create(result0, result1, result2, result3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicMaxU32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicMaxF32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToF32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            float result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastFToI32(Vector128.Create(result0, result1, result2, result3));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicMaxI64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return (Vector128.Create(result0, result1), Vector128.Create(result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicMaxU64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicMaxF64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToF64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            double result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Max(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastFToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicMinI32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            int result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<int>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return Vector128.Create(result0, result1, result2, result3);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicMinU32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicMinF32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToF32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            float result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<float>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastFToI32(Vector128.Create(result0, result1, result2, result3));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicMinI64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = value;
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            long result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<long>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return (Vector128.Create(result0, result1), Vector128.Create(result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicMinU64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicMinF64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToF64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            double result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Min(
                    ref Unsafe.AsRef<double>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastFToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicAndI32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicAndU32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicAndF32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicAndI64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicAndU64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicAndF64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.And(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicOrI32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicOrU32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicOrF32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicOrI64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicOrU64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicOrF64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Or(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicXorI32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicXorU32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> AtomicXorF32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            Vector128<int> value)
        {
            var sourceValue = CastIToU32(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(0)),
                    sourceValue.GetElement(2));
            }
            uint result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<uint>((void*)target.Item2.GetElement(1)),
                    sourceValue.GetElement(3));
            }
            return CastUToI32(Vector128.Create(result0, result1, result2, result3));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicXorI64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicXorU64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) AtomicXorF64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) target,
            (Vector128<long>, Vector128<long>) value)
        {
            var sourceValue = CastIToU64(value);
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
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
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(0)),
                    sourceValue.Item2.GetElement(0));
            }
            ulong result3 = default;
            if (mask3 != 0)
            {
                result3 = Atomic.Xor(
                    ref Unsafe.AsRef<ulong>((void*)target.Item2.GetElement(1)),
                    sourceValue.Item2.GetElement(1));
            }
            return CastUToI64((Vector128.Create(result0, result1), Vector128.Create(result2, result3)));
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
            Vector128<int> width,
            out Vector128<int> lane,
            out Vector128<int> offset)
        {
            lane = RemI32(LoadLaneIndexVector32(), width);
            offset = MulI32(DivI32(lane, width), width);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ShuffleUp32(
            Vector128<int> mask,
            Vector128<int> warp,
            Vector128<int> delta)
        {
            var lane = SubI32(LoadLaneIndexVector32(), delta);
            return Shuffle32(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SubShuffleUp32(
            Vector128<int> mask,
            Vector128<int> warp,
            Vector128<int> delta,
            Vector128<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = SubI32(lane, delta);
            return Shuffle32(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ShuffleUp64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) warp,
            Vector128<int> delta,
            Vector128<int> width)
        {
            var lane = SubI32(LoadLaneIndexVector32(), delta);
            return Shuffle64(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SubShuffleUp64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) warp,
            Vector128<int> delta,
            Vector128<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = SubI32(lane, delta);
            return Shuffle64(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ShuffleDown32(
            Vector128<int> mask,
            Vector128<int> warp,
            Vector128<int> delta)
        {
            var lane = AddI32(LoadLaneIndexVector32(), delta);
            return Shuffle32(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SubShuffleDown32(
            Vector128<int> mask,
            Vector128<int> warp,
            Vector128<int> delta,
            Vector128<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = AddI32(lane, delta);
            return Shuffle32(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ShuffleDown64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) warp,
            Vector128<int> delta)
        {
            var lane = AddI32(LoadLaneIndexVector32(), delta);
            return Shuffle64(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SubShuffleDown64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) warp,
            Vector128<int> delta,
            Vector128<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = AddI32(lane, delta);
            return Shuffle64(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> ShuffleXor32(
            Vector128<int> mask,
            Vector128<int> warp,
            Vector128<int> laneMask)
        {
            var lane = XorU32(LoadLaneIndexVector32(), laneMask);
            return Shuffle32(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vector128<int> SubShuffleXor32(
            Vector128<int> mask,
            Vector128<int> warp,
            Vector128<int> laneMask,
            Vector128<int> width)
        {
            ComputeShuffleConfig(width, out var lane, out var offset);
            var adjustedLane = XorU32(lane, laneMask);
            return Shuffle32(mask, warp, AddI32(adjustedLane, offset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) ShuffleXor64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) warp,
            Vector128<int> laneMask)
        {
            var lane = XorU32(LoadLaneIndexVector32(), laneMask);
            return Shuffle64(mask, warp, lane);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static (Vector128<long>, Vector128<long>) SubShuffleXor64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) warp,
            Vector128<int> laneMask,
            Vector128<int> width)
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
        internal static unsafe Vector128<int> Load8(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) address)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            var result0 = mask0 != 0
                ? (uint)*(byte*)address.Item1.GetElement(0)
                : 0;
            var result1 = mask1 != 0
                ? (uint)*(byte*)address.Item1.GetElement(1)
                : 0;
            var result2 = mask2 != 0
                ? (uint)*(byte*)address.Item2.GetElement(0)
                : 0;
            var result3 = mask3 != 0
                ? (uint)*(byte*)address.Item2.GetElement(1)
                : 0;
            return Vector128.Create(result0, result1, result2, result3).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe Vector128<int> Load16(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) address)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            var result0 = mask0 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(0)
                : 0;
            var result1 = mask1 != 0
                ? (uint)*(ushort*)address.Item1.GetElement(1)
                : 0;
            var result2 = mask2 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(0)
                : 0;
            var result3 = mask3 != 0
                ? (uint)*(ushort*)address.Item2.GetElement(1)
                : 0;
            return Vector128.Create(result0, result1, result2, result3).AsInt32();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        private static unsafe Vector128<int> Load32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) address)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int result0 = mask0 != 0
                ? *(int*)address.Item1.GetElement(0)
                : 0;
            int result1 = mask1 != 0
                ? *(int*)address.Item1.GetElement(1)
                : 0;
            int result2 = mask2 != 0
                ? *(int*)address.Item2.GetElement(0)
                : 0;
            int result3 = mask3 != 0
                ? *(int*)address.Item2.GetElement(1)
                : 0;
            return Vector128.Create(result0, result1, result2, result3);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe (Vector128<long>, Vector128<long>) Load64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) address)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            long result0 = mask0 != 0
                ? *(long*)address.Item1.GetElement(0)
                : 0;
            long result1 = mask1 != 0
                ? *(long*)address.Item1.GetElement(1)
                : 0;
            long result2 = mask2 != 0
                ? *(long*)address.Item2.GetElement(0)
                : 0;
            long result3 = mask3 != 0
                ? *(long*)address.Item2.GetElement(1)
                : 0;
            return (Vector128.Create(result0, result1), Vector128.Create(result2, result3));
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store8(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) address,
            Vector128<int> value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            byte* addr0 = (byte*)address.Item1.GetElement(0);
            byte* addr1 = (byte*)address.Item1.GetElement(1);
            byte* addr2 = (byte*)address.Item2.GetElement(0);
            byte* addr3 = (byte*)address.Item2.GetElement(1);
            var value0 = (byte)(value.GetElement(0) & 0xff);
            var value1 = (byte)(value.GetElement(1) & 0xff);
            var value2 = (byte)(value.GetElement(2) & 0xff);
            var value3 = (byte)(value.GetElement(3) & 0xff);
            if (mask0 != 0)
                *addr0 = value0;
            if (mask1 != 0)
                *addr1 = value1;
            if (mask2 != 0)
                *addr2 = value2;
            if (mask3 != 0)
                *addr3 = value3;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store16(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) address,
            Vector128<int> value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            short* addr0 = (short*)address.Item1.GetElement(0);
            short* addr1 = (short*)address.Item1.GetElement(1);
            short* addr2 = (short*)address.Item2.GetElement(0);
            short* addr3 = (short*)address.Item2.GetElement(1);
            var value0 = (short)(value.GetElement(0) & 0xffff);
            var value1 = (short)(value.GetElement(1) & 0xffff);
            var value2 = (short)(value.GetElement(2) & 0xffff);
            var value3 = (short)(value.GetElement(3) & 0xffff);
            if (mask0 != 0)
                *addr0 = value0;
            if (mask1 != 0)
                *addr1 = value1;
            if (mask2 != 0)
                *addr2 = value2;
            if (mask3 != 0)
                *addr3 = value3;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store32(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) address,
            Vector128<int> value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            int* addr0 = (int*)address.Item1.GetElement(0);
            int* addr1 = (int*)address.Item1.GetElement(1);
            int* addr2 = (int*)address.Item2.GetElement(0);
            int* addr3 = (int*)address.Item2.GetElement(1);
            var value0 = value.GetElement(0);
            var value1 = value.GetElement(1);
            var value2 = value.GetElement(2);
            var value3 = value.GetElement(3);
            if (mask0 != 0)
                *addr0 = value0;
            if (mask1 != 0)
                *addr1 = value1;
            if (mask2 != 0)
                *addr2 = value2;
            if (mask3 != 0)
                *addr3 = value3;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization |
                    MethodImplOptions.AggressiveInlining)]
        internal static unsafe void Store64(
            Vector128<int> mask,
            (Vector128<long>, Vector128<long>) address,
            (Vector128<long>, Vector128<long>) value)
        {
            int mask0 = mask.GetElement(0);
            int mask1 = mask.GetElement(1);
            int mask2 = mask.GetElement(2);
            int mask3 = mask.GetElement(3);
            long* addr0 = (long*)address.Item1.GetElement(0);
            long* addr1 = (long*)address.Item1.GetElement(1);
            long* addr2 = (long*)address.Item2.GetElement(0);
            long* addr3 = (long*)address.Item2.GetElement(1);
            var value0 = value.Item1.GetElement(0);
            var value1 = value.Item1.GetElement(1);
            var value2 = value.Item2.GetElement(0);
            var value3 = value.Item2.GetElement(1);
            if (mask0 != 0)
                *addr0 = value0;
            if (mask1 != 0)
                *addr1 = value1;
            if (mask2 != 0)
                *addr2 = value2;
            if (mask3 != 0)
                *addr3 = value3;
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
            Vector128<int> mask,
            Vector128<int> value,
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
        internal static void DumpWarp32(Vector128<int> value, string label)
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
        internal static void DumpWarp64((Vector128<long>, Vector128<long>) value, string label)
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