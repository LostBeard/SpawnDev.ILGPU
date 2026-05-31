// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RemappedIntrinsics.Generated.tt/RemappedIntrinsics.Generated.cs
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

using System;

namespace ILGPU.Frontend.Intrinsic
{
    partial class RemappedIntrinsics
    {
        private static void RegisterMathRemappings()
        {
            var systemMathType = typeof(Math);

            AddRemapping(
                systemMathType,
                MathType,
                "Abs",
                typeof(sbyte));
            AddRemapping(
                systemMathType,
                MathType,
                "Abs",
                typeof(short));
            AddRemapping(
                systemMathType,
                MathType,
                "Abs",
                typeof(int));
            AddRemapping(
                systemMathType,
                MathType,
                "Abs",
                typeof(long));
            AddRemapping(
                systemMathType,
                MathType,
                "Abs",
                typeof(float));
            AddRemapping(
                systemMathType,
                MathType,
                "Abs",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Sqrt",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Sin",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Sinh",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Asin",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Cos",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Cosh",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Acos",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Tan",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Tanh",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Atan",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Exp",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Floor",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Ceiling",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Log",
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Log10",
                typeof(double));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(sbyte),
                typeof(sbyte));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(short),
                typeof(short));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(int),
                typeof(int));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(long),
                typeof(long));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(byte),
                typeof(byte));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(ushort),
                typeof(ushort));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(uint),
                typeof(uint));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(ulong),
                typeof(ulong));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(float),
                typeof(float));
            AddRemapping(
                systemMathType,
                MathType,
                "Min",
                typeof(double),
                typeof(double));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(sbyte),
                typeof(sbyte));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(short),
                typeof(short));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(int),
                typeof(int));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(long),
                typeof(long));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(byte),
                typeof(byte));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(ushort),
                typeof(ushort));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(uint),
                typeof(uint));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(ulong),
                typeof(ulong));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(float),
                typeof(float));
            AddRemapping(
                systemMathType,
                MathType,
                "Max",
                typeof(double),
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Atan2",
                typeof(double),
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Pow",
                typeof(double),
                typeof(double));
            AddRemapping(
                systemMathType,
                CPUMathType,
                "Log",
                typeof(double),
                typeof(double));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(sbyte),
                typeof(sbyte),
                typeof(sbyte));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(short),
                typeof(short),
                typeof(short));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(int),
                typeof(int),
                typeof(int));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(long),
                typeof(long),
                typeof(long));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(byte),
                typeof(byte),
                typeof(byte));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(ushort),
                typeof(ushort),
                typeof(ushort));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(uint),
                typeof(uint),
                typeof(uint));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(ulong),
                typeof(ulong),
                typeof(ulong));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(float),
                typeof(float),
                typeof(float));
            AddRemapping(
                systemMathType,
                MathType,
                "Clamp",
                typeof(double),
                typeof(double),
                typeof(double));
            var systemMathFType = typeof(MathF);

                AddRemapping(
                systemMathFType,
                MathType,
                "Abs",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Sqrt",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Sin",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Sinh",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Asin",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Cos",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Cosh",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Acos",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Tan",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Tanh",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Atan",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Exp",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Floor",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Ceiling",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Log",
                typeof(float));
                AddRemapping(
                systemMathFType,
                CPUMathType,
                "Log10",
                typeof(float));
            AddRemapping(
                systemMathFType,
                MathType,
                "Min",
                typeof(float),
                typeof(float));
            AddRemapping(
                systemMathFType,
                MathType,
                "Max",
                typeof(float),
                typeof(float));
            AddRemapping(
                systemMathFType,
                CPUMathType,
                "Atan2",
                typeof(float),
                typeof(float));
            AddRemapping(
                systemMathFType,
                CPUMathType,
                "Pow",
                typeof(float),
                typeof(float));
            AddRemapping(
                systemMathFType,
                CPUMathType,
                "Log",
                typeof(float),
                typeof(float));
        }

        private static void RegisterInterlockedRemappings()
        {
            var sourceType = typeof(System.Threading.Interlocked);
            var targetType = typeof(Atomic);
            var customTargetType = typeof(Interlocked);

            AddRemapping(
                sourceType,
                targetType,
                "Add",
                required: true,
                typeof(System.Int32).MakeByRefType(), typeof(System.Int32));
            AddRemapping(
                sourceType,
                targetType,
                "Add",
                required: true,
                typeof(System.Int64).MakeByRefType(), typeof(System.Int64));
            AddRemapping(
                sourceType,
                targetType,
                "CompareExchange",
                required: true,
                typeof(System.Int32).MakeByRefType(), typeof(System.Int32), typeof(System.Int32));
            AddRemapping(
                sourceType,
                targetType,
                "CompareExchange",
                required: true,
                typeof(System.Int64).MakeByRefType(), typeof(System.Int64), typeof(System.Int64));
            AddRemapping(
                sourceType,
                targetType,
                "CompareExchange",
                required: true,
                typeof(System.Single).MakeByRefType(), typeof(System.Single), typeof(System.Single));
            AddRemapping(
                sourceType,
                targetType,
                "CompareExchange",
                required: true,
                typeof(System.Double).MakeByRefType(), typeof(System.Double), typeof(System.Double));
            AddRemapping(
                sourceType,
                customTargetType,
                "Decrement",
                required: true,
                typeof(System.Int32).MakeByRefType());
            AddRemapping(
                sourceType,
                customTargetType,
                "Decrement",
                required: true,
                typeof(System.Int64).MakeByRefType());
            AddRemapping(
                sourceType,
                targetType,
                "Exchange",
                required: true,
                typeof(System.Int32).MakeByRefType(), typeof(System.Int32));
            AddRemapping(
                sourceType,
                targetType,
                "Exchange",
                required: true,
                typeof(System.Int64).MakeByRefType(), typeof(System.Int64));
            AddRemapping(
                sourceType,
                targetType,
                "Exchange",
                required: true,
                typeof(System.Single).MakeByRefType(), typeof(System.Single));
            AddRemapping(
                sourceType,
                targetType,
                "Exchange",
                required: true,
                typeof(System.Double).MakeByRefType(), typeof(System.Double));
            AddRemapping(
                sourceType,
                customTargetType,
                "Increment",
                required: true,
                typeof(System.Int32).MakeByRefType());
            AddRemapping(
                sourceType,
                customTargetType,
                "Increment",
                required: true,
                typeof(System.Int64).MakeByRefType());
            AddRemapping(
                sourceType,
                targetType,
                "Add",
                required: false,
                typeof(System.UInt32).MakeByRefType(), typeof(System.UInt32));
            AddRemapping(
                sourceType,
                targetType,
                "Add",
                required: false,
                typeof(System.UInt64).MakeByRefType(), typeof(System.UInt64));
            AddRemapping(
                sourceType,
                targetType,
                "And",
                required: false,
                typeof(System.Int32).MakeByRefType(), typeof(System.Int32));
            AddRemapping(
                sourceType,
                targetType,
                "And",
                required: false,
                typeof(System.Int64).MakeByRefType(), typeof(System.Int64));
            AddRemapping(
                sourceType,
                targetType,
                "And",
                required: false,
                typeof(System.UInt32).MakeByRefType(), typeof(System.UInt32));
            AddRemapping(
                sourceType,
                targetType,
                "And",
                required: false,
                typeof(System.UInt64).MakeByRefType(), typeof(System.UInt64));
            AddRemapping(
                sourceType,
                targetType,
                "CompareExchange",
                required: false,
                typeof(System.UInt32).MakeByRefType(), typeof(System.UInt32), typeof(System.UInt32));
            AddRemapping(
                sourceType,
                targetType,
                "CompareExchange",
                required: false,
                typeof(System.UInt64).MakeByRefType(), typeof(System.UInt64), typeof(System.UInt64));
            AddRemapping(
                sourceType,
                customTargetType,
                "Decrement",
                required: false,
                typeof(System.UInt32).MakeByRefType());
            AddRemapping(
                sourceType,
                customTargetType,
                "Decrement",
                required: false,
                typeof(System.UInt64).MakeByRefType());
            AddRemapping(
                sourceType,
                targetType,
                "Exchange",
                required: false,
                typeof(System.UInt32).MakeByRefType(), typeof(System.UInt32));
            AddRemapping(
                sourceType,
                targetType,
                "Exchange",
                required: false,
                typeof(System.UInt64).MakeByRefType(), typeof(System.UInt64));
            AddRemapping(
                sourceType,
                customTargetType,
                "Increment",
                required: false,
                typeof(System.UInt32).MakeByRefType());
            AddRemapping(
                sourceType,
                customTargetType,
                "Increment",
                required: false,
                typeof(System.UInt64).MakeByRefType());
            AddRemapping(
                sourceType,
                targetType,
                "Or",
                required: false,
                typeof(System.Int32).MakeByRefType(), typeof(System.Int32));
            AddRemapping(
                sourceType,
                targetType,
                "Or",
                required: false,
                typeof(System.Int64).MakeByRefType(), typeof(System.Int64));
            AddRemapping(
                sourceType,
                targetType,
                "Or",
                required: false,
                typeof(System.UInt32).MakeByRefType(), typeof(System.UInt32));
            AddRemapping(
                sourceType,
                targetType,
                "Or",
                required: false,
                typeof(System.UInt64).MakeByRefType(), typeof(System.UInt64));
        }
    }
}