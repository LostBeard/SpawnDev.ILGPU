// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: ValueTuples.tt/ValueTuples.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------
using ILGPU.Util;
using System;
using System.Reflection;

namespace ILGPU.IR.Types
{
    /// <summary>
    /// Contains helper functions for supporting System.ValueTuple.
    /// </summary>
    internal static class ValueTuples
    {
        #region Static

        /// <summary>
        /// Retrieves the offsets for the fields in a value tuple that use the supplied
        /// generic type arguments.
        /// </summary>
        /// <param name="types">The generic type arguments of the value tuple.</param>
        /// <returns>Offsets for the fields of the value tuple.</returns>
        public static int[] GetOffsets(Type[] types)
        {
            var methodInfo = types.Length switch
            {
                1 => GetOffsetsMethod1,
                2 => GetOffsetsMethod2,
                3 => GetOffsetsMethod3,
                4 => GetOffsetsMethod4,
                5 => GetOffsetsMethod5,
                6 => GetOffsetsMethod6,
                7 => GetOffsetsMethod7,
                8 => GetOffsetsMethod8,
                _ => throw new NotImplementedException()
            };
            var method = methodInfo.MakeGenericMethod(types);
            return method.Invoke(null, null).AsNotNullCast<int[]>();
        }

        private static unsafe int CalculateOffset(byte* current, byte* baseline) =>
            (int)(current - baseline);

        /// <summary>
        /// Retrieves the offsets for the fields in the given value tuple.
        /// </summary>
        private static unsafe int[] GetOffsets1<
            T1>()
            where T1 : unmanaged
        {
            ValueTuple<T1> input;
            var offsets = new int[1];
            byte* baseline = (byte*)&input;
            offsets[0] = CalculateOffset((byte*)&input.Item1, baseline);
            return offsets;
        }

        private static readonly MethodInfo GetOffsetsMethod1 =
            typeof(ValueTuples).GetMethod(
                nameof(GetOffsets1),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// Retrieves the offsets for the fields in the given value tuple.
        /// </summary>
        private static unsafe int[] GetOffsets2<
            T1, T2>()
            where T1 : unmanaged
            where T2 : unmanaged
        {
            ValueTuple<T1, T2> input;
            var offsets = new int[2];
            byte* baseline = (byte*)&input;
            offsets[0] = CalculateOffset((byte*)&input.Item1, baseline);
            offsets[1] = CalculateOffset((byte*)&input.Item2, baseline);
            return offsets;
        }

        private static readonly MethodInfo GetOffsetsMethod2 =
            typeof(ValueTuples).GetMethod(
                nameof(GetOffsets2),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// Retrieves the offsets for the fields in the given value tuple.
        /// </summary>
        private static unsafe int[] GetOffsets3<
            T1, T2, T3>()
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
        {
            ValueTuple<T1, T2, T3> input;
            var offsets = new int[3];
            byte* baseline = (byte*)&input;
            offsets[0] = CalculateOffset((byte*)&input.Item1, baseline);
            offsets[1] = CalculateOffset((byte*)&input.Item2, baseline);
            offsets[2] = CalculateOffset((byte*)&input.Item3, baseline);
            return offsets;
        }

        private static readonly MethodInfo GetOffsetsMethod3 =
            typeof(ValueTuples).GetMethod(
                nameof(GetOffsets3),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// Retrieves the offsets for the fields in the given value tuple.
        /// </summary>
        private static unsafe int[] GetOffsets4<
            T1, T2, T3, T4>()
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
        {
            ValueTuple<T1, T2, T3, T4> input;
            var offsets = new int[4];
            byte* baseline = (byte*)&input;
            offsets[0] = CalculateOffset((byte*)&input.Item1, baseline);
            offsets[1] = CalculateOffset((byte*)&input.Item2, baseline);
            offsets[2] = CalculateOffset((byte*)&input.Item3, baseline);
            offsets[3] = CalculateOffset((byte*)&input.Item4, baseline);
            return offsets;
        }

        private static readonly MethodInfo GetOffsetsMethod4 =
            typeof(ValueTuples).GetMethod(
                nameof(GetOffsets4),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// Retrieves the offsets for the fields in the given value tuple.
        /// </summary>
        private static unsafe int[] GetOffsets5<
            T1, T2, T3, T4, T5>()
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
        {
            ValueTuple<T1, T2, T3, T4, T5> input;
            var offsets = new int[5];
            byte* baseline = (byte*)&input;
            offsets[0] = CalculateOffset((byte*)&input.Item1, baseline);
            offsets[1] = CalculateOffset((byte*)&input.Item2, baseline);
            offsets[2] = CalculateOffset((byte*)&input.Item3, baseline);
            offsets[3] = CalculateOffset((byte*)&input.Item4, baseline);
            offsets[4] = CalculateOffset((byte*)&input.Item5, baseline);
            return offsets;
        }

        private static readonly MethodInfo GetOffsetsMethod5 =
            typeof(ValueTuples).GetMethod(
                nameof(GetOffsets5),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// Retrieves the offsets for the fields in the given value tuple.
        /// </summary>
        private static unsafe int[] GetOffsets6<
            T1, T2, T3, T4, T5, T6>()
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
            where T6 : unmanaged
        {
            ValueTuple<T1, T2, T3, T4, T5, T6> input;
            var offsets = new int[6];
            byte* baseline = (byte*)&input;
            offsets[0] = CalculateOffset((byte*)&input.Item1, baseline);
            offsets[1] = CalculateOffset((byte*)&input.Item2, baseline);
            offsets[2] = CalculateOffset((byte*)&input.Item3, baseline);
            offsets[3] = CalculateOffset((byte*)&input.Item4, baseline);
            offsets[4] = CalculateOffset((byte*)&input.Item5, baseline);
            offsets[5] = CalculateOffset((byte*)&input.Item6, baseline);
            return offsets;
        }

        private static readonly MethodInfo GetOffsetsMethod6 =
            typeof(ValueTuples).GetMethod(
                nameof(GetOffsets6),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// Retrieves the offsets for the fields in the given value tuple.
        /// </summary>
        private static unsafe int[] GetOffsets7<
            T1, T2, T3, T4, T5, T6, T7>()
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
            where T6 : unmanaged
            where T7 : unmanaged
        {
            ValueTuple<T1, T2, T3, T4, T5, T6, T7> input;
            var offsets = new int[7];
            byte* baseline = (byte*)&input;
            offsets[0] = CalculateOffset((byte*)&input.Item1, baseline);
            offsets[1] = CalculateOffset((byte*)&input.Item2, baseline);
            offsets[2] = CalculateOffset((byte*)&input.Item3, baseline);
            offsets[3] = CalculateOffset((byte*)&input.Item4, baseline);
            offsets[4] = CalculateOffset((byte*)&input.Item5, baseline);
            offsets[5] = CalculateOffset((byte*)&input.Item6, baseline);
            offsets[6] = CalculateOffset((byte*)&input.Item7, baseline);
            return offsets;
        }

        private static readonly MethodInfo GetOffsetsMethod7 =
            typeof(ValueTuples).GetMethod(
                nameof(GetOffsets7),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// Retrieves the offsets for the fields in the given value tuple.
        /// </summary>
        private static unsafe int[] GetOffsets8<
            T1, T2, T3, T4, T5, T6, T7, T8>()
            where T1 : unmanaged
            where T2 : unmanaged
            where T3 : unmanaged
            where T4 : unmanaged
            where T5 : unmanaged
            where T6 : unmanaged
            where T7 : unmanaged
            where T8 : unmanaged
        {
            ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8> input;
            var offsets = new int[8];
            byte* baseline = (byte*)&input;
            offsets[0] = CalculateOffset((byte*)&input.Item1, baseline);
            offsets[1] = CalculateOffset((byte*)&input.Item2, baseline);
            offsets[2] = CalculateOffset((byte*)&input.Item3, baseline);
            offsets[3] = CalculateOffset((byte*)&input.Item4, baseline);
            offsets[4] = CalculateOffset((byte*)&input.Item5, baseline);
            offsets[5] = CalculateOffset((byte*)&input.Item6, baseline);
            offsets[6] = CalculateOffset((byte*)&input.Item7, baseline);
            offsets[7] = CalculateOffset((byte*)&input.Rest, baseline);
            return offsets;
        }

        private static readonly MethodInfo GetOffsetsMethod8 =
            typeof(ValueTuples).GetMethod(
                nameof(GetOffsets8),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        #endregion
    }
}