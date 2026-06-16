// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: ArithmeticOperations.tt/ArithmeticOperations.cs
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
using System.Diagnostics;
using System.Runtime.CompilerServices;

// disable: max_line_length
#pragma warning disable IDE0046 // Convert to conditional expression
#pragma warning disable IDE0047 // Remove unnecessary parentheses
#pragma warning disable IDE0066 // Convert switch statement to expression

namespace ILGPU.IR.Construction
{
    partial class IRBuilder
    {
        private ValueReference UnaryArithmeticFoldConstants(
            Location location,
            PrimitiveValue value,
            UnaryArithmeticKind kind)
        {
            switch (kind)
            {
                case UnaryArithmeticKind.Neg:
                    return UnaryArithmeticFoldConstants_Neg(location, value);
                case UnaryArithmeticKind.Not:
                    return UnaryArithmeticFoldConstants_Not(location, value);
                case UnaryArithmeticKind.Abs:
                    return UnaryArithmeticFoldConstants_Abs(location, value);
                case UnaryArithmeticKind.PopC:
                    return UnaryArithmeticFoldConstants_PopC(location, value);
                case UnaryArithmeticKind.CLZ:
                    return UnaryArithmeticFoldConstants_CLZ(location, value);
                case UnaryArithmeticKind.CTZ:
                    return UnaryArithmeticFoldConstants_CTZ(location, value);
                case UnaryArithmeticKind.RcpF:
                    return UnaryArithmeticFoldConstants_RcpF(location, value);
                case UnaryArithmeticKind.IsNaNF:
                    return UnaryArithmeticFoldConstants_IsNaNF(location, value);
                case UnaryArithmeticKind.IsInfF:
                    return UnaryArithmeticFoldConstants_IsInfF(location, value);
                case UnaryArithmeticKind.IsFinF:
                    return UnaryArithmeticFoldConstants_IsFinF(location, value);
                case UnaryArithmeticKind.SqrtF:
                    return UnaryArithmeticFoldConstants_SqrtF(location, value);
                case UnaryArithmeticKind.RsqrtF:
                    return UnaryArithmeticFoldConstants_RsqrtF(location, value);
                case UnaryArithmeticKind.AsinF:
                    return UnaryArithmeticFoldConstants_AsinF(location, value);
                case UnaryArithmeticKind.SinF:
                    return UnaryArithmeticFoldConstants_SinF(location, value);
                case UnaryArithmeticKind.SinhF:
                    return UnaryArithmeticFoldConstants_SinhF(location, value);
                case UnaryArithmeticKind.AcosF:
                    return UnaryArithmeticFoldConstants_AcosF(location, value);
                case UnaryArithmeticKind.CosF:
                    return UnaryArithmeticFoldConstants_CosF(location, value);
                case UnaryArithmeticKind.CoshF:
                    return UnaryArithmeticFoldConstants_CoshF(location, value);
                case UnaryArithmeticKind.TanF:
                    return UnaryArithmeticFoldConstants_TanF(location, value);
                case UnaryArithmeticKind.TanhF:
                    return UnaryArithmeticFoldConstants_TanhF(location, value);
                case UnaryArithmeticKind.AtanF:
                    return UnaryArithmeticFoldConstants_AtanF(location, value);
                case UnaryArithmeticKind.ExpF:
                    return UnaryArithmeticFoldConstants_ExpF(location, value);
                case UnaryArithmeticKind.Exp2F:
                    return UnaryArithmeticFoldConstants_Exp2F(location, value);
                case UnaryArithmeticKind.FloorF:
                    return UnaryArithmeticFoldConstants_FloorF(location, value);
                case UnaryArithmeticKind.CeilingF:
                    return UnaryArithmeticFoldConstants_CeilingF(location, value);
                case UnaryArithmeticKind.LogF:
                    return UnaryArithmeticFoldConstants_LogF(location, value);
                case UnaryArithmeticKind.Log2F:
                    return UnaryArithmeticFoldConstants_Log2F(location, value);
                case UnaryArithmeticKind.Log10F:
                    return UnaryArithmeticFoldConstants_Log10F(location, value);
                default:
                    throw location.GetArgumentException(nameof(kind));
            }
        }

        private ValueReference UnaryArithmeticFoldConstants_Neg(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        -value.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        -value.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        -value.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        -value.Float8E5M2Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        -value.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        -value.Float64Value);
                case BasicValueType.Int8:
                    return CreatePrimitiveValue(
                        location,
                        (sbyte)-value.Int8Value);
                case BasicValueType.Int16:
                    return CreatePrimitiveValue(
                        location,
                        (short)-value.Int16Value);
                case BasicValueType.Int32:
                    return CreatePrimitiveValue(
                        location,
                        -value.Int32Value);
                case BasicValueType.Int64:
                    return CreatePrimitiveValue(
                        location,
                        -value.Int64Value);
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_Not(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Int1:
                    return CreatePrimitiveValue(
                        location,
                        !value.Int1Value);
                case BasicValueType.Int8:
                    return CreatePrimitiveValue(
                        location,
                        (sbyte)~value.Int8Value);
                case BasicValueType.Int16:
                    return CreatePrimitiveValue(
                        location,
                        (short)~value.Int16Value);
                case BasicValueType.Int32:
                    return CreatePrimitiveValue(
                        location,
                        ~value.Int32Value);
                case BasicValueType.Int64:
                    return CreatePrimitiveValue(
                        location,
                        ~value.Int64Value);
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_Abs(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Abs(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.Abs((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.Abs((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.Abs((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Abs(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Abs(value.Float64Value));
                case BasicValueType.Int8:
                    return CreatePrimitiveValue(
                        location,
                        (sbyte)IntrinsicMath.Abs(value.Int8Value));
                case BasicValueType.Int16:
                    return CreatePrimitiveValue(
                        location,
                        (short)IntrinsicMath.Abs(value.Int16Value));
                case BasicValueType.Int32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Abs(value.Int32Value));
                case BasicValueType.Int64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Abs(value.Int64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_PopC(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Int8:
                    return CreatePrimitiveValue(
                        location,
                        (sbyte)IntrinsicMath.BitOperations.PopCount(value.Int8Value));
                case BasicValueType.Int16:
                    return CreatePrimitiveValue(
                        location,
                        (short)IntrinsicMath.BitOperations.PopCount(value.Int16Value));
                case BasicValueType.Int32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.BitOperations.PopCount(value.Int32Value));
                case BasicValueType.Int64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.BitOperations.PopCount(value.Int64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_CLZ(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Int8:
                    return CreatePrimitiveValue(
                        location,
                        (sbyte)IntrinsicMath.BitOperations.LeadingZeroCount(value.Int8Value));
                case BasicValueType.Int16:
                    return CreatePrimitiveValue(
                        location,
                        (short)IntrinsicMath.BitOperations.LeadingZeroCount(value.Int16Value));
                case BasicValueType.Int32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.BitOperations.LeadingZeroCount(value.Int32Value));
                case BasicValueType.Int64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.BitOperations.LeadingZeroCount(value.Int64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_CTZ(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Int8:
                    return CreatePrimitiveValue(
                        location,
                        (sbyte)IntrinsicMath.BitOperations.TrailingZeroCount(value.Int8Value));
                case BasicValueType.Int16:
                    return CreatePrimitiveValue(
                        location,
                        (short)IntrinsicMath.BitOperations.TrailingZeroCount(value.Int16Value));
                case BasicValueType.Int32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.BitOperations.TrailingZeroCount(value.Int32Value));
                case BasicValueType.Int64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.BitOperations.TrailingZeroCount(value.Int64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_RcpF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Rcp(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Rcp((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Rcp((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Rcp((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Rcp(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Rcp(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_IsNaNF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsNaN(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsNaN((float)value.BFloat16Value));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsNaN((float)value.Float8E4M3Value));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsNaN((float)value.Float8E5M2Value));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsNaN(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsNaN(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_IsInfF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsInfinity(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsInfinity((float)value.BFloat16Value));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsInfinity((float)value.Float8E4M3Value));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsInfinity((float)value.Float8E5M2Value));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsInfinity(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsInfinity(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_IsFinF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsFinite(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsFinite((float)value.BFloat16Value));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsFinite((float)value.Float8E4M3Value));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsFinite((float)value.Float8E5M2Value));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsFinite(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.IsFinite(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_SqrtF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Sqrt(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Sqrt((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Sqrt((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Sqrt((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Sqrt(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Sqrt(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_RsqrtF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Rsqrt(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Rsqrt((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Rsqrt((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Rsqrt((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Rsqrt(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Rsqrt(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_AsinF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Asin(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Asin((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Asin((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Asin((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Asin(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Asin(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_SinF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Sin(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Sin((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Sin((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Sin((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Sin(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Sin(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_SinhF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Sinh(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Sinh((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Sinh((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Sinh((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Sinh(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Sinh(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_AcosF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Acos(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Acos((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Acos((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Acos((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Acos(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Acos(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_CosF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Cos(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Cos((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Cos((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Cos((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Cos(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Cos(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_CoshF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Cosh(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Cosh((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Cosh((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Cosh((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Cosh(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Cosh(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_TanF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Tan(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Tan((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Tan((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Tan((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Tan(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Tan(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_TanhF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Tanh(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Tanh((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Tanh((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Tanh((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Tanh(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Tanh(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_AtanF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Atan(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Atan((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Atan((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Atan((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Atan(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Atan(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_ExpF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Exp(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Exp((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Exp((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Exp((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Exp(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Exp(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_Exp2F(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Exp2(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Exp2((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Exp2((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Exp2((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Exp2(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Exp2(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_FloorF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Floor(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Floor((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Floor((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Floor((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Floor(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Floor(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_CeilingF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Ceiling(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Ceiling((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Ceiling((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Ceiling((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Ceiling(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Ceiling(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_LogF(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Log((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Log((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Log((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_Log2F(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log2(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Log2((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Log2((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Log2((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log2(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log2(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference UnaryArithmeticFoldConstants_Log10F(
            Location location,
            PrimitiveValue value)
        {
            switch (value.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log10(value.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Log10((float)value.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Log10((float)value.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Log10((float)value.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log10(value.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log10(value.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Value? UnaryArithmeticSimplify(
            Location location,
            Value value,
            UnaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (kind)
            {
                case UnaryArithmeticKind.Neg:
                    if (value.BasicValueType == BasicValueType.Int1)
                        return CreateArithmetic(location,value,UnaryArithmeticKind.Not);
                    break;
                case UnaryArithmeticKind.Not:
                    {
                        if (value is UnaryArithmeticValue nested
                             && nested.Kind == UnaryArithmeticKind.Not)
                        {
                            return nested.Value;
                        }
                    }
                    {
                        if (value is BinaryArithmeticValue nested
                             && BinaryArithmeticValue.TryInvertLogical(nested.Kind, out var _))
                        {
                            return InvertBinaryArithmetic(location, nested);
                        }
                    }
                    {
                        if (value is CompareValue nested
                            )
                        {
                            return InvertCompareValue(location, nested);
                        }
                    }
                    break;
                case UnaryArithmeticKind.Abs:
                    if ((flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned)
                        return value;
                    break;
            }
            return null;
        }

        private ValueReference BinaryArithmeticFoldConstants(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (kind)
            {
                case BinaryArithmeticKind.Add:
                    return BinaryArithmeticFoldConstants_Add(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Sub:
                    return BinaryArithmeticFoldConstants_Sub(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Mul:
                    return BinaryArithmeticFoldConstants_Mul(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Div:
                    return BinaryArithmeticFoldConstants_Div(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Rem:
                    return BinaryArithmeticFoldConstants_Rem(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.And:
                    return BinaryArithmeticFoldConstants_And(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Or:
                    return BinaryArithmeticFoldConstants_Or(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Xor:
                    return BinaryArithmeticFoldConstants_Xor(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Shl:
                    return BinaryArithmeticFoldConstants_Shl(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Shr:
                    return BinaryArithmeticFoldConstants_Shr(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Min:
                    return BinaryArithmeticFoldConstants_Min(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Max:
                    return BinaryArithmeticFoldConstants_Max(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.Atan2F:
                    return BinaryArithmeticFoldConstants_Atan2F(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.PowF:
                    return BinaryArithmeticFoldConstants_PowF(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.BinaryLogF:
                    return BinaryArithmeticFoldConstants_BinaryLogF(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                case BinaryArithmeticKind.CopySignF:
                    return BinaryArithmeticFoldConstants_CopySignF(
                        location,
                        left,
                        right,
                        kind,
                        flags);
                default:
                    throw location.GetArgumentException(nameof(kind));
            }
        }

        /// <summary>
        /// Simplifies the LHS of a binary expression.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Value? BinaryArithmeticSimplify_LHS(
            Location location,
            PrimitiveValue left,
            Value right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (kind)
            {
                case BinaryArithmeticKind.Sub:
                    if (left.IsZero)
                        return CreateArithmetic(location,right,UnaryArithmeticKind.Neg,flags);
                    break;
                case BinaryArithmeticKind.Div:
                    if (left.HasIntValue(0))
                        return left;
                    if (left.HasFloatValue(1.0f, 1.0))
                        return CreateArithmetic(location,right,UnaryArithmeticKind.RcpF,flags);
                    break;
                case BinaryArithmeticKind.And:
                    if (left == right)
                        return left;
                    break;
                case BinaryArithmeticKind.Or:
                    if (left == right)
                        return left;
                    break;
                case BinaryArithmeticKind.Shl:
                    if (left.IsZero)
                        return left;
                    break;
                case BinaryArithmeticKind.Shr:
                    if (left.IsZero)
                        return left;
                    break;
            }
            return null;
        }

        /// <summary>
        /// Simplifies the RHS of a binary expression.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Value? BinaryArithmeticSimplify_RHS(
            Location location,
            Value left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (kind)
            {
                case BinaryArithmeticKind.Add:
                    if (right.IsZero)
                        return left;
                    break;
                case BinaryArithmeticKind.Sub:
                    if (right.IsZero)
                        return left;
                    break;
                case BinaryArithmeticKind.Mul:
                    if (right.HasIntValue(0))
                        return right;
                    if (right.HasIntValue(1))
                        return left;
                    if (right.IsInt && right.RawValue > 0 &&Utilities.IsPowerOf2(right.RawValue))
                        return CreateArithmetic(location,left,GetDivMulShiftAmount(location, right),BinaryArithmeticKind.Shl);
                    break;
                case BinaryArithmeticKind.Div:
                    if (right.HasIntValue(1))
                        return left;
                    if (right.IsInt && right.RawValue > 0 &&Utilities.IsPowerOf2(right.RawValue) &&(flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned)
                        return CreateArithmetic(location,left,GetDivMulShiftAmount(location, right),BinaryArithmeticKind.Shr);
                    break;
                case BinaryArithmeticKind.And:
                    if (left == right)
                        return left;
                    if (right.IsZero)
                        return right;
                    if (right.IsBool && right.HasIntValue(1))
                        return left;
                    break;
                case BinaryArithmeticKind.Or:
                    if (left == right)
                        return left;
                    if (right.IsZero)
                        return left;
                    if (right.IsBool && right.HasIntValue(1))
                        return right;
                    break;
                case BinaryArithmeticKind.Shl:
                    if (right.IsZero)
                        return left;
                    break;
                case BinaryArithmeticKind.Shr:
                    if (right.IsZero)
                        return left;
                    break;
            }
            return null;
        }

        /// <summary>
        /// Simplifies the LHS of a binary expression.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Value? BinaryArithmeticSimplify_LHS(
            Location location,
            BinaryArithmeticValue arithmeticValue,
            PrimitiveValue firstValue,
            PrimitiveValue secondValue,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (kind)
            {
                case BinaryArithmeticKind.Sub:
                    return CreateArithmetic(
                        location,
                        BinaryArithmeticFoldConstants(
                            location,
                            secondValue,
                            firstValue,
                            BinaryArithmeticKind.Sub,
                            flags),
                        arithmeticValue.Right,
                        BinaryArithmeticKind.Add,
                        flags);
            }
            return null;
        }

        /// <summary>
        /// Simplifies the RHS of a binary expression.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Value? BinaryArithmeticSimplify_RHS(
            Location location,
            BinaryArithmeticValue arithmeticValue,
            PrimitiveValue firstValue,
            PrimitiveValue secondValue,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (kind)
            {
                case BinaryArithmeticKind.Add:
                    return CreateArithmetic(
                        location,
                        arithmeticValue.Left,
                        BinaryArithmeticFoldConstants(
                            location,
                            firstValue,
                            secondValue,
                            BinaryArithmeticKind.Add,
                            flags),
                        kind,
                        flags);
                case BinaryArithmeticKind.Sub:
                    return CreateArithmetic(
                        location,
                        arithmeticValue.Left,
                        BinaryArithmeticFoldConstants(
                            location,
                            firstValue,
                            secondValue,
                            BinaryArithmeticKind.Add,
                            flags),
                        kind,
                        flags);
                case BinaryArithmeticKind.Mul:
                    return CreateArithmetic(
                        location,
                        arithmeticValue.Left,
                        BinaryArithmeticFoldConstants(
                            location,
                            firstValue,
                            secondValue,
                            BinaryArithmeticKind.Mul,
                            flags),
                        kind,
                        flags);
                case BinaryArithmeticKind.Min:
                    return CreateArithmetic(
                        location,
                        arithmeticValue.Left,
                        BinaryArithmeticFoldConstants(
                            location,
                            firstValue,
                            secondValue,
                            BinaryArithmeticKind.Min,
                            flags),
                        kind,
                        flags);
                case BinaryArithmeticKind.Max:
                    return CreateArithmetic(
                        location,
                        arithmeticValue.Left,
                        BinaryArithmeticFoldConstants(
                            location,
                            firstValue,
                            secondValue,
                            BinaryArithmeticKind.Max,
                            flags),
                        kind,
                        flags);
            }
            return null;
        }

        [Conditional("DEBUG")]
        private static void VerifyBinaryArithmeticOperands(
            Location location,
            Value left,
            Value right,
            BinaryArithmeticKind kind)
        {
            switch (kind)
            {
                case BinaryArithmeticKind.Add:
                    location.Assert(
                        left.BasicValueType.IsInt() || left.BasicValueType.IsFloat() || left.Type.IsPointerType
                    );
                    break;
                case BinaryArithmeticKind.Sub:
                    location.Assert(
                        left.BasicValueType.IsInt() || left.BasicValueType.IsFloat()
                    );
                    break;
                case BinaryArithmeticKind.Mul:
                    location.Assert(
                        left.BasicValueType.IsInt() || left.BasicValueType.IsFloat() || left.Type.IsPointerType
                    );
                    break;
                case BinaryArithmeticKind.Div:
                    location.Assert(
                        left.BasicValueType.IsInt() || left.BasicValueType.IsFloat() || left.Type.IsPointerType
                    );
                    break;
                case BinaryArithmeticKind.Rem:
                    location.Assert(
                        left.BasicValueType.IsInt() || left.BasicValueType.IsFloat()
                    );
                    break;
                case BinaryArithmeticKind.And:
                    location.Assert(
                        left.BasicValueType == BasicValueType.Int1 || left.BasicValueType.IsInt()
                    );
                    break;
                case BinaryArithmeticKind.Or:
                    location.Assert(
                        left.BasicValueType == BasicValueType.Int1 || left.BasicValueType.IsInt()
                    );
                    break;
                case BinaryArithmeticKind.Xor:
                    location.Assert(
                        left.BasicValueType == BasicValueType.Int1 || left.BasicValueType.IsInt()
                    );
                    break;
                case BinaryArithmeticKind.Shl:
                    location.Assert(
                        left.BasicValueType.IsInt() || left.Type.IsPointerType
                    );
                    break;
                case BinaryArithmeticKind.Shr:
                    location.Assert(
                        left.BasicValueType.IsInt() || left.Type.IsPointerType
                    );
                    break;
                case BinaryArithmeticKind.Min:
                    location.Assert(
                        left.BasicValueType.IsInt() || left.BasicValueType.IsFloat()
                    );
                    break;
                case BinaryArithmeticKind.Max:
                    location.Assert(
                        left.BasicValueType.IsInt() || left.BasicValueType.IsFloat()
                    );
                    break;
                case BinaryArithmeticKind.Atan2F:
                    location.Assert(
                        left.BasicValueType.IsFloat()
                    );
                    break;
                case BinaryArithmeticKind.PowF:
                    location.Assert(
                        left.BasicValueType.IsFloat()
                    );
                    break;
                case BinaryArithmeticKind.BinaryLogF:
                    location.Assert(
                        left.BasicValueType.IsFloat()
                    );
                    break;
                case BinaryArithmeticKind.CopySignF:
                    location.Assert(
                        left.BasicValueType.IsFloat()
                    );
                    break;
            }
        }

        private ValueReference BinaryArithmeticFoldConstants_Add(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        left.Float16Value + right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        left.BFloat16Value + right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E4M3Value + right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E5M2Value + right.Float8E5M2Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        left.Float32Value + right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        left.Float64Value + right.Float64Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(left.UInt8Value + right.UInt8Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(left.Int8Value + right.Int8Value));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(left.UInt16Value + right.UInt16Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(left.Int16Value + right.Int16Value));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt32Value + right.UInt32Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int32Value + right.Int32Value);
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt64Value + right.UInt64Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int64Value + right.Int64Value);
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Sub(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        left.Float16Value - right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        left.BFloat16Value - right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E4M3Value - right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E5M2Value - right.Float8E5M2Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        left.Float32Value - right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        left.Float64Value - right.Float64Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(left.UInt8Value - right.UInt8Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(left.Int8Value - right.Int8Value));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(left.UInt16Value - right.UInt16Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(left.Int16Value - right.Int16Value));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt32Value - right.UInt32Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int32Value - right.Int32Value);
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt64Value - right.UInt64Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int64Value - right.Int64Value);
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Mul(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        left.Float16Value * right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        left.BFloat16Value * right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E4M3Value * right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E5M2Value * right.Float8E5M2Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        left.Float32Value * right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        left.Float64Value * right.Float64Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(left.UInt8Value * right.UInt8Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(left.Int8Value * right.Int8Value));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(left.UInt16Value * right.UInt16Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(left.Int16Value * right.Int16Value));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt32Value * right.UInt32Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int32Value * right.Int32Value);
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt64Value * right.UInt64Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int64Value * right.Int64Value);
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Div(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        left.Float16Value / right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        left.BFloat16Value / right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E4M3Value / right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E5M2Value / right.Float8E5M2Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        left.Float32Value / right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        left.Float64Value / right.Float64Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(left.UInt8Value / right.UInt8Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(left.Int8Value / right.Int8Value));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(left.UInt16Value / right.UInt16Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(left.Int16Value / right.Int16Value));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt32Value / right.UInt32Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int32Value / right.Int32Value);
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt64Value / right.UInt64Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int64Value / right.Int64Value);
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Rem(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        left.Float16Value % right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        left.BFloat16Value % right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E4M3Value % right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        left.Float8E5M2Value % right.Float8E5M2Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        left.Float32Value % right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        left.Float64Value % right.Float64Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(left.UInt8Value % right.UInt8Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(left.Int8Value % right.Int8Value));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(left.UInt16Value % right.UInt16Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(left.Int16Value % right.Int16Value));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt32Value % right.UInt32Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int32Value % right.Int32Value);
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt64Value % right.UInt64Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int64Value % right.Int64Value);
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_And(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Int1:
                    return CreatePrimitiveValue(
                        location,
                        Bitwise.And(left.Int1Value, right.Int1Value));
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(Bitwise.And(left.UInt8Value, right.UInt8Value)));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(Bitwise.And(left.Int8Value, right.Int8Value)));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(Bitwise.And(left.UInt16Value, right.UInt16Value)));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(Bitwise.And(left.Int16Value, right.Int16Value)));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            Bitwise.And(left.UInt32Value, right.UInt32Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            Bitwise.And(left.Int32Value, right.Int32Value));
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            Bitwise.And(left.UInt64Value, right.UInt64Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            Bitwise.And(left.Int64Value, right.Int64Value));
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Or(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Int1:
                    return CreatePrimitiveValue(
                        location,
                        Bitwise.Or(left.Int1Value, right.Int1Value));
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(Bitwise.Or(left.UInt8Value, right.UInt8Value)));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(Bitwise.Or(left.Int8Value, right.Int8Value)));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(Bitwise.Or(left.UInt16Value, right.UInt16Value)));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(Bitwise.Or(left.Int16Value, right.Int16Value)));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            Bitwise.Or(left.UInt32Value, right.UInt32Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            Bitwise.Or(left.Int32Value, right.Int32Value));
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            Bitwise.Or(left.UInt64Value, right.UInt64Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            Bitwise.Or(left.Int64Value, right.Int64Value));
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Xor(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Int1:
                    return CreatePrimitiveValue(
                        location,
                        left.Int1Value ^ right.Int1Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(left.UInt8Value ^ right.UInt8Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(left.Int8Value ^ right.Int8Value));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(left.UInt16Value ^ right.UInt16Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(left.Int16Value ^ right.Int16Value));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt32Value ^ right.UInt32Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int32Value ^ right.Int32Value);
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt64Value ^ right.UInt64Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int64Value ^ right.Int64Value);
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Shl(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(left.UInt8Value << (int)right.UInt8Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(left.Int8Value << (int)right.Int8Value));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(left.UInt16Value << (int)right.UInt16Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(left.Int16Value << (int)right.Int16Value));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt32Value << (int)right.UInt32Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int32Value << (int)right.Int32Value);
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt64Value << (int)right.UInt64Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int64Value << (int)right.Int64Value);
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Shr(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(left.UInt8Value >> (int)right.UInt8Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(left.Int8Value >> (int)right.Int8Value));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(left.UInt16Value >> (int)right.UInt16Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(left.Int16Value >> (int)right.Int16Value));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt32Value >> (int)right.UInt32Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int32Value >> (int)right.Int32Value);
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.UInt64Value >> (int)right.UInt64Value);
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            left.Int64Value >> (int)right.Int64Value);
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Min(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Min(left.Float16Value, right.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.Min((float)left.BFloat16Value, (float)right.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.Min((float)left.Float8E4M3Value, (float)right.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.Min((float)left.Float8E5M2Value, (float)right.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Min(left.Float32Value, right.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Min(left.Float64Value, right.Float64Value));
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(IntrinsicMath.Min(left.UInt8Value, right.UInt8Value)));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(IntrinsicMath.Min(left.Int8Value, right.Int8Value)));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(IntrinsicMath.Min(left.UInt16Value, right.UInt16Value)));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(IntrinsicMath.Min(left.Int16Value, right.Int16Value)));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            IntrinsicMath.Min(left.UInt32Value, right.UInt32Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            IntrinsicMath.Min(left.Int32Value, right.Int32Value));
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            IntrinsicMath.Min(left.UInt64Value, right.UInt64Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            IntrinsicMath.Min(left.Int64Value, right.Int64Value));
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Max(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            var isUnsigned = (flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Max(left.Float16Value, right.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.Max((float)left.BFloat16Value, (float)right.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.Max((float)left.Float8E4M3Value, (float)right.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.Max((float)left.Float8E5M2Value, (float)right.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Max(left.Float32Value, right.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.Max(left.Float64Value, right.Float64Value));
                case BasicValueType.Int8:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (byte)(IntrinsicMath.Max(left.UInt8Value, right.UInt8Value)));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (sbyte)(IntrinsicMath.Max(left.Int8Value, right.Int8Value)));
                    }
                case BasicValueType.Int16:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            (ushort)(IntrinsicMath.Max(left.UInt16Value, right.UInt16Value)));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            (short)(IntrinsicMath.Max(left.Int16Value, right.Int16Value)));
                    }
                case BasicValueType.Int32:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            IntrinsicMath.Max(left.UInt32Value, right.UInt32Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            IntrinsicMath.Max(left.Int32Value, right.Int32Value));
                    }
                case BasicValueType.Int64:
                    if (isUnsigned)
                    {
                        return CreatePrimitiveValue(
                            location,
                            IntrinsicMath.Max(left.UInt64Value, right.UInt64Value));
                    }
                    else
                    {
                        return CreatePrimitiveValue(
                            location,
                            IntrinsicMath.Max(left.Int64Value, right.Int64Value));
                    }
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_Atan2F(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Atan2(left.Float16Value, right.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Atan2((float)left.BFloat16Value, (float)right.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Atan2((float)left.Float8E4M3Value, (float)right.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Atan2((float)left.Float8E5M2Value, (float)right.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Atan2(left.Float32Value, right.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Atan2(left.Float64Value, right.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_PowF(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Pow(left.Float16Value, right.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Pow((float)left.BFloat16Value, (float)right.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Pow((float)left.Float8E4M3Value, (float)right.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Pow((float)left.Float8E5M2Value, (float)right.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Pow(left.Float32Value, right.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Pow(left.Float64Value, right.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_BinaryLogF(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log(left.Float16Value, right.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CPUOnly.Log((float)left.BFloat16Value, (float)right.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CPUOnly.Log((float)left.Float8E4M3Value, (float)right.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CPUOnly.Log((float)left.Float8E5M2Value, (float)right.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log(left.Float32Value, right.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CPUOnly.Log(left.Float64Value, right.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }

        private ValueReference BinaryArithmeticFoldConstants_CopySignF(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            BinaryArithmeticKind kind,
            ArithmeticFlags flags)
        {
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CopySign(left.Float16Value, right.Float16Value));
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(
                        location,
                        (BFloat16)(IntrinsicMath.CopySign((float)left.BFloat16Value, (float)right.BFloat16Value)));
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E4M3)(IntrinsicMath.CopySign((float)left.Float8E4M3Value, (float)right.Float8E4M3Value)));
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(
                        location,
                        (Float8E5M2)(IntrinsicMath.CopySign((float)left.Float8E5M2Value, (float)right.Float8E5M2Value)));
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CopySign(left.Float32Value, right.Float32Value));
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(
                        location,
                        IntrinsicMath.CopySign(left.Float64Value, right.Float64Value));
            }
            throw location.GetInvalidOperationException();
        }



    }
}

#pragma warning restore IDE0066 // Convert switch statement to expression
#pragma warning restore IDE0046 // Convert to conditional expression
#pragma warning restore IDE0047 // Remove unnecessary parentheses