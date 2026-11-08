// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: IntrinsicMath.CPUOnly.tt/IntrinsicMath.CPUOnly.cs
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

using ILGPU.Frontend.Intrinsic;
using System;

// disable: max_line_length

namespace ILGPU
{
    partial class IntrinsicMath
    {
        /// <summary>
        /// Contains CPU-only math functions that are automatically mapped to IR nodes.
        /// </summary>
        public static class CPUOnly
        {
            #region Double Precision

            /// <summary>
            /// The reciprocal operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.RcpF)]
            public static double Rcp(double value) =>
                1.0 / value;

            /// <summary>
            /// The is-not-a-number operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.IsNaNF)]
            public static bool IsNaN(double value) =>
                double.IsNaN(value);

            /// <summary>
            /// The is-infinity operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.IsInfF)]
            public static bool IsInfinity(double value) =>
                double.IsInfinity(value);

            /// <summary>
            /// The is-finite operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.IsFinF)]
            public static bool IsFinite(double value) =>
                !IsNaN(value) && !IsInfinity(value);

            /// <summary>
            /// Computes sqrt(value).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.SqrtF)]
            public static double Sqrt(double value) =>
                Math.Sqrt(value);

            /// <summary>
            /// Computes 1/sqrt(value).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.RsqrtF)]
            public static double Rsqrt(double value) =>
                Rcp(Sqrt(value));

            /// <summary>
            /// Computes asin(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.AsinF)]
            public static double Asin(double value) =>
                Math.Asin(value);

            /// <summary>
            /// Computes sin(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.SinF)]
            public static double Sin(double value) =>
                Math.Sin(value);

            /// <summary>
            /// Computes sinh(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.SinhF)]
            public static double Sinh(double value) =>
                Math.Sinh(value);

            /// <summary>
            /// Computes acos(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.AcosF)]
            public static double Acos(double value) =>
                Math.Acos(value);

            /// <summary>
            /// Computes cos(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.CosF)]
            public static double Cos(double value) =>
                Math.Cos(value);

            /// <summary>
            /// Computes cosh(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.CoshF)]
            public static double Cosh(double value) =>
                Math.Cosh(value);

            /// <summary>
            /// Computes tan(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.TanF)]
            public static double Tan(double value) =>
                Math.Tan(value);

            /// <summary>
            /// Computes tanh(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.TanhF)]
            public static double Tanh(double value) =>
                Math.Tanh(value);

            /// <summary>
            /// Computes atan(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.AtanF)]
            public static double Atan(double value) =>
                Math.Atan(value);

            /// <summary>
            /// Computes exp(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.ExpF)]
            public static double Exp(double value) =>
                Math.Exp(value);

            /// <summary>
            /// Computes 2^x.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.Exp2F)]
            public static double Exp2(double value) =>
                Math.Pow(2.0, value);

            /// <summary>
            /// Computes floor(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.FloorF)]
            public static double Floor(double value) =>
                Math.Floor(value);

            /// <summary>
            /// Computes ceil(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.CeilingF)]
            public static double Ceiling(double value) =>
                Math.Ceiling(value);

            /// <summary>
            /// Computes log(x) to base e.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.LogF)]
            public static double Log(double value) =>
                Math.Log(value);

            /// <summary>
            /// Computes log(x) to base 2.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.Log2F)]
            public static double Log2(double value) =>
                Math.Log(value, 2.0);

            /// <summary>
            /// Computes log(x) to base 10.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.Log10F)]
            public static double Log10(double value) =>
                Math.Log10(value);

            /// <summary>
            /// The % operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Rem)]
            public static double Rem(double left, double right) =>
                left % right;

            /// <summary>
            /// The min operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Min)]
            public static double Min(double left, double right) =>
                Math.Min(left, right);

            /// <summary>
            /// The max operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Max)]
            public static double Max(double left, double right) =>
                Math.Max(left, right);

            /// <summary>
            /// The atan2 operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Atan2F)]
            public static double Atan2(double left, double right) =>
                Math.Atan2(left, right);

            /// <summary>
            /// The pow operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.PowF)]
            public static double Pow(double left, double right) =>
                Math.Pow(left, right);

            /// <summary>
            /// The binary log operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.BinaryLogF)]
            public static double Log(double left, double right) =>
                Math.Log(left, right);

            /// <summary>
            /// The FMA operation.
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAdd)]
            public static double FMA(double first, double second, double third) =>
                Math.FusedMultiplyAdd(first, second, third);
            /// <summary>
            /// a * b + c: one hardware fma where there is one (CUDA, OpenCL, WebGPU, Velocity), multiply then add where not (Wasm, WebGL).
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAddEstimate)]
            public static double MultiplyAddEstimate(double first, double second, double third) =>
                double.MultiplyAddEstimate(first, second, third);

            /// <summary>
            /// <see cref="Math.FusedMultiplyAdd(double, double, double)"/> in a kernel: the
            /// MultiplyAdd intrinsic, which rounds once (RemappedIntrinsics maps the BCL call here).
            /// </summary>
            /// <param name="first">The first factor.</param>
            /// <param name="second">The second factor.</param>
            /// <param name="third">The addend.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAdd)]
            public static double FusedMultiplyAdd(double first, double second, double third) =>
                Math.FusedMultiplyAdd(first, second, third);
            #endregion

            #region Single Precision

            /// <summary>
            /// The reciprocal operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.RcpF)]
            public static float Rcp(float value) =>
                1.0f / value;

            /// <summary>
            /// The is-not-a-number operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.IsNaNF)]
            public static bool IsNaN(float value) =>
                float.IsNaN(value);

            /// <summary>
            /// The is-infinity operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.IsInfF)]
            public static bool IsInfinity(float value) =>
                float.IsInfinity(value);

            /// <summary>
            /// The is-finite operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.IsFinF)]
            public static bool IsFinite(float value) =>
                !IsNaN(value) && !IsInfinity(value);

            /// <summary>
            /// Computes sqrt(value).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.SqrtF)]
            public static float Sqrt(float value) =>
                MathF.Sqrt(value);

            /// <summary>
            /// Computes 1/sqrt(value).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.RsqrtF)]
            public static float Rsqrt(float value) =>
                Rcp(Sqrt(value));

            /// <summary>
            /// Computes asin(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.AsinF)]
            public static float Asin(float value) =>
                MathF.Asin(value);

            /// <summary>
            /// Computes sin(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.SinF)]
            public static float Sin(float value) =>
                MathF.Sin(value);

            /// <summary>
            /// Computes sinh(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.SinhF)]
            public static float Sinh(float value) =>
                MathF.Sinh(value);

            /// <summary>
            /// Computes acos(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.AcosF)]
            public static float Acos(float value) =>
                MathF.Acos(value);

            /// <summary>
            /// Computes cos(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.CosF)]
            public static float Cos(float value) =>
                MathF.Cos(value);

            /// <summary>
            /// Computes cosh(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.CoshF)]
            public static float Cosh(float value) =>
                MathF.Cosh(value);

            /// <summary>
            /// Computes tan(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.TanF)]
            public static float Tan(float value) =>
                MathF.Tan(value);

            /// <summary>
            /// Computes tanh(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.TanhF)]
            public static float Tanh(float value) =>
                MathF.Tanh(value);

            /// <summary>
            /// Computes atan(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.AtanF)]
            public static float Atan(float value) =>
                MathF.Atan(value);

            /// <summary>
            /// Computes exp(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.ExpF)]
            public static float Exp(float value) =>
                MathF.Exp(value);

            /// <summary>
            /// Computes 2^x.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.Exp2F)]
            public static float Exp2(float value) =>
                MathF.Pow(2.0f, value);

            /// <summary>
            /// Computes floor(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.FloorF)]
            public static float Floor(float value) =>
                MathF.Floor(value);

            /// <summary>
            /// Computes ceil(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.CeilingF)]
            public static float Ceiling(float value) =>
                MathF.Ceiling(value);

            /// <summary>
            /// Computes log(x) to base e.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.LogF)]
            public static float Log(float value) =>
                MathF.Log(value);

            /// <summary>
            /// Computes log(x) to base 2.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.Log2F)]
            public static float Log2(float value) =>
                MathF.Log(value, 2.0f);

            /// <summary>
            /// Computes log(x) to base 10.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.Log10F)]
            public static float Log10(float value) =>
                MathF.Log10(value);

            /// <summary>
            /// The % operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Rem)]
            public static float Rem(float left, float right) =>
                left % right;

            /// <summary>
            /// The min operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Min)]
            public static float Min(float left, float right) =>
                MathF.Min(left, right);

            /// <summary>
            /// The max operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Max)]
            public static float Max(float left, float right) =>
                MathF.Max(left, right);

            /// <summary>
            /// The atan2 operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Atan2F)]
            public static float Atan2(float left, float right) =>
                MathF.Atan2(left, right);

            /// <summary>
            /// The pow operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.PowF)]
            public static float Pow(float left, float right) =>
                MathF.Pow(left, right);

            /// <summary>
            /// The binary log operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.BinaryLogF)]
            public static float Log(float left, float right) =>
                MathF.Log(left, right);

            /// <summary>
            /// The FMA operation.
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAdd)]
            public static float FMA(float first, float second, float third) =>
                MathF.FusedMultiplyAdd(first, second, third);
            /// <summary>
            /// a * b + c: one hardware fma where there is one (CUDA, OpenCL, WebGPU, Velocity), multiply then add where not (Wasm, WebGL).
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAddEstimate)]
            public static float MultiplyAddEstimate(float first, float second, float third) =>
                float.MultiplyAddEstimate(first, second, third);

            /// <summary>
            /// <see cref="MathF.FusedMultiplyAdd(float, float, float)"/> in a kernel: the
            /// MultiplyAdd intrinsic, which rounds once (RemappedIntrinsics maps the BCL call here).
            /// </summary>
            /// <param name="first">The first factor.</param>
            /// <param name="second">The second factor.</param>
            /// <param name="third">The addend.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAdd)]
            public static float FusedMultiplyAdd(float first, float second, float third) =>
                MathF.FusedMultiplyAdd(first, second, third);

            #endregion

            #region Half Precision

            /// <summary>
            /// The reciprocal operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.RcpF)]
            public static Half Rcp(Half value) =>
                (Half)Rcp((float)value);

            /// <summary>
            /// The is-not-a-number operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.IsNaNF)]
            public static bool IsNaN(Half value) =>
                IsNaN((float)value);

            /// <summary>
            /// The is-infinity operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.IsInfF)]
            public static bool IsInfinity(Half value) =>
                IsInfinity((float)value);

            /// <summary>
            /// The is-finite operation.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.IsFinF)]
            public static bool IsFinite(Half value) =>
                IsFinite((float)value);

            /// <summary>
            /// Computes sqrt(value).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.SqrtF)]
            public static Half Sqrt(Half value) =>
                (Half)Sqrt((float)value);

            /// <summary>
            /// Computes 1/sqrt(value).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.RsqrtF)]
            public static Half Rsqrt(Half value) =>
                (Half)Rsqrt((float)value);

            /// <summary>
            /// Computes asin(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.AsinF)]
            public static Half Asin(Half value) =>
                (Half)Asin((float)value);

            /// <summary>
            /// Computes sin(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.SinF)]
            public static Half Sin(Half value) =>
                (Half)Sin((float)value);

            /// <summary>
            /// Computes sinh(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.SinhF)]
            public static Half Sinh(Half value) =>
                (Half)Sinh((float)value);

            /// <summary>
            /// Computes acos(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.AcosF)]
            public static Half Acos(Half value) =>
                (Half)Acos((float)value);

            /// <summary>
            /// Computes cos(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.CosF)]
            public static Half Cos(Half value) =>
                (Half)Cos((float)value);

            /// <summary>
            /// Computes cosh(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.CoshF)]
            public static Half Cosh(Half value) =>
                (Half)Cosh((float)value);

            /// <summary>
            /// Computes tan(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.TanF)]
            public static Half Tan(Half value) =>
                (Half)Tan((float)value);

            /// <summary>
            /// Computes tanh(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.TanhF)]
            public static Half Tanh(Half value) =>
                (Half)Tanh((float)value);

            /// <summary>
            /// Computes atan(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.AtanF)]
            public static Half Atan(Half value) =>
                (Half)Atan((float)value);

            /// <summary>
            /// Computes exp(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.ExpF)]
            public static Half Exp(Half value) =>
                (Half)Exp((float)value);

            /// <summary>
            /// Computes 2^x.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.Exp2F)]
            public static Half Exp2(Half value) =>
                (Half)Exp2((float)value);

            /// <summary>
            /// Computes floor(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.FloorF)]
            public static Half Floor(Half value) =>
                (Half)Floor((float)value);

            /// <summary>
            /// Computes ceil(x).
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.CeilingF)]
            public static Half Ceiling(Half value) =>
                (Half)Ceiling((float)value);

            /// <summary>
            /// Computes log(x) to base e.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.LogF)]
            public static Half Log(Half value) =>
                (Half)Log((float)value);

            /// <summary>
            /// Computes log(x) to base 2.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.Log2F)]
            public static Half Log2(Half value) =>
                (Half)Log2((float)value);

            /// <summary>
            /// Computes log(x) to base 10.
            /// </summary>
            /// <param name="value">The value.</param>
            [MathIntrinsic(MathIntrinsicKind.Log10F)]
            public static Half Log10(Half value) =>
                (Half)Log10((float)value);

            /// <summary>
            /// The % operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Rem)]
            public static Half Rem(Half left, Half right) =>
                HalfExtensions.RemFP32(left, right);

            /// <summary>
            /// The min operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Min)]
            public static Half Min(Half left, Half right) =>
                HalfExtensions.MinFP32(left, right);

            /// <summary>
            /// The max operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Max)]
            public static Half Max(Half left, Half right) =>
                HalfExtensions.MaxFP32(left, right);

            /// <summary>
            /// The atan2 operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.Atan2F)]
            public static Half Atan2(Half left, Half right) =>
                HalfExtensions.Atan2FP32(left, right);

            /// <summary>
            /// The pow operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.PowF)]
            public static Half Pow(Half left, Half right) =>
                HalfExtensions.PowFP32(left, right);

            /// <summary>
            /// The binary log operation.
            /// </summary>
            /// <param name="left">The left operand.</param>
            /// <param name="right">The right operand.</param>
            [MathIntrinsic(MathIntrinsicKind.BinaryLogF)]
            public static Half Log(Half left, Half right) =>
                HalfExtensions.LogFP32(left, right);

            #endregion

            #region Integers

            /// <summary>
            /// The FMA operation.
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAdd)]
            public static int FMA(int first, int second, int third) =>
                first * second + third;

            /// <summary>
            /// The FMA operation.
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAdd)]
            public static uint FMA(uint first, uint second, uint third) =>
                first * second + third;

            /// <summary>
            /// The FMA operation.
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAdd)]
            public static long FMA(long first, long second, long third) =>
                first * second + third;

            /// <summary>
            /// The FMA operation.
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAdd)]
            public static ulong FMA(ulong first, ulong second, ulong third) =>
                first * second + third;

            /// <summary>
            /// a * b + c: one hardware fma where there is one (CUDA, OpenCL, WebGPU, Velocity), multiply then add where not (Wasm, WebGL).
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAddEstimate)]
            public static int MultiplyAddEstimate(int first, int second, int third) =>
                first * second + third;

            /// <summary>
            /// a * b + c: one hardware fma where there is one (CUDA, OpenCL, WebGPU, Velocity), multiply then add where not (Wasm, WebGL).
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAddEstimate)]
            public static uint MultiplyAddEstimate(uint first, uint second, uint third) =>
                first * second + third;

            /// <summary>
            /// a * b + c: one hardware fma where there is one (CUDA, OpenCL, WebGPU, Velocity), multiply then add where not (Wasm, WebGL).
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAddEstimate)]
            public static long MultiplyAddEstimate(long first, long second, long third) =>
                first * second + third;

            /// <summary>
            /// a * b + c: one hardware fma where there is one (CUDA, OpenCL, WebGPU, Velocity), multiply then add where not (Wasm, WebGL).
            /// </summary>
            /// <param name="first">The first operand.</param>
            /// <param name="second">The second operand.</param>
            /// <param name="third">The third operand.</param>
            [MathIntrinsic(MathIntrinsicKind.MultiplyAddEstimate)]
            public static ulong MultiplyAddEstimate(ulong first, ulong second, ulong third) =>
                first * second + third;


            #endregion
        }
    }
}