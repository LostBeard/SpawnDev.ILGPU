// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: CompareOperations.tt/CompareOperations.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------


using ILGPU.IR.Values;
using ILGPU.Resources;
using System;
using System.Diagnostics.CodeAnalysis;

// disable: max_line_length
#pragma warning disable IDE0046 // Convert to conditional expression
#pragma warning disable IDE0047 // Remove unnecessary parentheses
#pragma warning disable IDE0066 // Convert switch statement to expression

namespace ILGPU.IR.Construction
{
    partial class IRBuilder
    {
        private ValueReference CompareFoldConstants(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            CompareKind kind,
            CompareFlags flags)
        {
            switch (kind)
            {
                case CompareKind.Equal:
                    return CompareFoldConstants_Equal(location, left, right, kind, flags);
                case CompareKind.NotEqual:
                    return CompareFoldConstants_NotEqual(location, left, right, kind, flags);
                case CompareKind.LessThan:
                    return CompareFoldConstants_LessThan(location, left, right, kind, flags);
                case CompareKind.LessEqual:
                    return CompareFoldConstants_LessEqual(location, left, right, kind, flags);
                case CompareKind.GreaterThan:
                    return CompareFoldConstants_GreaterThan(location, left, right, kind, flags);
                case CompareKind.GreaterEqual:
                    return CompareFoldConstants_GreaterEqual(location, left, right, kind, flags);
                default:
                    throw location.GetArgumentException(nameof(kind));
            }
        }

        private ValueReference CompareFoldConstants_Equal(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            CompareKind kind,
            CompareFlags flags)
        {
            var isUnsigned = (flags & CompareFlags.UnsignedOrUnordered) == CompareFlags.UnsignedOrUnordered;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(location, left.Float16Value == right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(location, left.BFloat16Value == right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(location, (float)left.Float8E4M3Value == (float)right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(location, (float)left.Float8E5M2Value == (float)right.Float8E5M2Value);
                case BasicValueType.Float4E2M1:
                    return CreatePrimitiveValue(location, (float)left.Float4E2M1Value == (float)right.Float4E2M1Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(location, left.Float32Value == right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(location, left.Float64Value == right.Float64Value);
                case BasicValueType.Int1:
                    return CreatePrimitiveValue(location, left.Int1Value == right.Int1Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt8Value == right.UInt8Value);
                    else
                        return CreatePrimitiveValue(location, left.Int8Value == right.Int8Value);
                case BasicValueType.Int16:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt16Value == right.UInt16Value);
                    else
                        return CreatePrimitiveValue(location, left.Int16Value == right.Int16Value);
                case BasicValueType.Int32:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt32Value == right.UInt32Value);
                    else
                        return CreatePrimitiveValue(location, left.Int32Value == right.Int32Value);
                case BasicValueType.Int64:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt64Value == right.UInt64Value);
                    else
                        return CreatePrimitiveValue(location, left.Int64Value == right.Int64Value);
            }
            throw location.GetNotSupportedException(
                ErrorMessages.NotSupportedCompareArgumentType,
                left.BasicValueType);
        }

        private ValueReference CompareFoldConstants_NotEqual(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            CompareKind kind,
            CompareFlags flags)
        {
            var isUnsigned = (flags & CompareFlags.UnsignedOrUnordered) == CompareFlags.UnsignedOrUnordered;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(location, left.Float16Value != right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(location, left.BFloat16Value != right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(location, (float)left.Float8E4M3Value != (float)right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(location, (float)left.Float8E5M2Value != (float)right.Float8E5M2Value);
                case BasicValueType.Float4E2M1:
                    return CreatePrimitiveValue(location, (float)left.Float4E2M1Value != (float)right.Float4E2M1Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(location, left.Float32Value != right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(location, left.Float64Value != right.Float64Value);
                case BasicValueType.Int1:
                    return CreatePrimitiveValue(location, left.Int1Value != right.Int1Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt8Value != right.UInt8Value);
                    else
                        return CreatePrimitiveValue(location, left.Int8Value != right.Int8Value);
                case BasicValueType.Int16:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt16Value != right.UInt16Value);
                    else
                        return CreatePrimitiveValue(location, left.Int16Value != right.Int16Value);
                case BasicValueType.Int32:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt32Value != right.UInt32Value);
                    else
                        return CreatePrimitiveValue(location, left.Int32Value != right.Int32Value);
                case BasicValueType.Int64:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt64Value != right.UInt64Value);
                    else
                        return CreatePrimitiveValue(location, left.Int64Value != right.Int64Value);
            }
            throw location.GetNotSupportedException(
                ErrorMessages.NotSupportedCompareArgumentType,
                left.BasicValueType);
        }

        private ValueReference CompareFoldConstants_LessThan(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            CompareKind kind,
            CompareFlags flags)
        {
            var isUnsigned = (flags & CompareFlags.UnsignedOrUnordered) == CompareFlags.UnsignedOrUnordered;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(location, left.Float16Value < right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(location, left.BFloat16Value < right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(location, (float)left.Float8E4M3Value < (float)right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(location, (float)left.Float8E5M2Value < (float)right.Float8E5M2Value);
                case BasicValueType.Float4E2M1:
                    return CreatePrimitiveValue(location, (float)left.Float4E2M1Value < (float)right.Float4E2M1Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(location, left.Float32Value < right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(location, left.Float64Value < right.Float64Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt8Value < right.UInt8Value);
                    else
                        return CreatePrimitiveValue(location, left.Int8Value < right.Int8Value);
                case BasicValueType.Int16:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt16Value < right.UInt16Value);
                    else
                        return CreatePrimitiveValue(location, left.Int16Value < right.Int16Value);
                case BasicValueType.Int32:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt32Value < right.UInt32Value);
                    else
                        return CreatePrimitiveValue(location, left.Int32Value < right.Int32Value);
                case BasicValueType.Int64:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt64Value < right.UInt64Value);
                    else
                        return CreatePrimitiveValue(location, left.Int64Value < right.Int64Value);
            }
            throw location.GetNotSupportedException(
                ErrorMessages.NotSupportedCompareArgumentType,
                left.BasicValueType);
        }

        private ValueReference CompareFoldConstants_LessEqual(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            CompareKind kind,
            CompareFlags flags)
        {
            var isUnsigned = (flags & CompareFlags.UnsignedOrUnordered) == CompareFlags.UnsignedOrUnordered;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(location, left.Float16Value <= right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(location, left.BFloat16Value <= right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(location, (float)left.Float8E4M3Value <= (float)right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(location, (float)left.Float8E5M2Value <= (float)right.Float8E5M2Value);
                case BasicValueType.Float4E2M1:
                    return CreatePrimitiveValue(location, (float)left.Float4E2M1Value <= (float)right.Float4E2M1Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(location, left.Float32Value <= right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(location, left.Float64Value <= right.Float64Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt8Value <= right.UInt8Value);
                    else
                        return CreatePrimitiveValue(location, left.Int8Value <= right.Int8Value);
                case BasicValueType.Int16:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt16Value <= right.UInt16Value);
                    else
                        return CreatePrimitiveValue(location, left.Int16Value <= right.Int16Value);
                case BasicValueType.Int32:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt32Value <= right.UInt32Value);
                    else
                        return CreatePrimitiveValue(location, left.Int32Value <= right.Int32Value);
                case BasicValueType.Int64:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt64Value <= right.UInt64Value);
                    else
                        return CreatePrimitiveValue(location, left.Int64Value <= right.Int64Value);
            }
            throw location.GetNotSupportedException(
                ErrorMessages.NotSupportedCompareArgumentType,
                left.BasicValueType);
        }

        private ValueReference CompareFoldConstants_GreaterThan(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            CompareKind kind,
            CompareFlags flags)
        {
            var isUnsigned = (flags & CompareFlags.UnsignedOrUnordered) == CompareFlags.UnsignedOrUnordered;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(location, left.Float16Value > right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(location, left.BFloat16Value > right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(location, (float)left.Float8E4M3Value > (float)right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(location, (float)left.Float8E5M2Value > (float)right.Float8E5M2Value);
                case BasicValueType.Float4E2M1:
                    return CreatePrimitiveValue(location, (float)left.Float4E2M1Value > (float)right.Float4E2M1Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(location, left.Float32Value > right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(location, left.Float64Value > right.Float64Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt8Value > right.UInt8Value);
                    else
                        return CreatePrimitiveValue(location, left.Int8Value > right.Int8Value);
                case BasicValueType.Int16:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt16Value > right.UInt16Value);
                    else
                        return CreatePrimitiveValue(location, left.Int16Value > right.Int16Value);
                case BasicValueType.Int32:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt32Value > right.UInt32Value);
                    else
                        return CreatePrimitiveValue(location, left.Int32Value > right.Int32Value);
                case BasicValueType.Int64:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt64Value > right.UInt64Value);
                    else
                        return CreatePrimitiveValue(location, left.Int64Value > right.Int64Value);
            }
            throw location.GetNotSupportedException(
                ErrorMessages.NotSupportedCompareArgumentType,
                left.BasicValueType);
        }

        private ValueReference CompareFoldConstants_GreaterEqual(
            Location location,
            PrimitiveValue left,
            PrimitiveValue right,
            CompareKind kind,
            CompareFlags flags)
        {
            var isUnsigned = (flags & CompareFlags.UnsignedOrUnordered) == CompareFlags.UnsignedOrUnordered;
            switch (left.BasicValueType)
            {
                case BasicValueType.Float16:
                    return CreatePrimitiveValue(location, left.Float16Value >= right.Float16Value);
                case BasicValueType.BFloat16:
                    return CreatePrimitiveValue(location, left.BFloat16Value >= right.BFloat16Value);
                case BasicValueType.Float8E4M3:
                    return CreatePrimitiveValue(location, (float)left.Float8E4M3Value >= (float)right.Float8E4M3Value);
                case BasicValueType.Float8E5M2:
                    return CreatePrimitiveValue(location, (float)left.Float8E5M2Value >= (float)right.Float8E5M2Value);
                case BasicValueType.Float4E2M1:
                    return CreatePrimitiveValue(location, (float)left.Float4E2M1Value >= (float)right.Float4E2M1Value);
                case BasicValueType.Float32:
                    return CreatePrimitiveValue(location, left.Float32Value >= right.Float32Value);
                case BasicValueType.Float64:
                    return CreatePrimitiveValue(location, left.Float64Value >= right.Float64Value);
                case BasicValueType.Int8:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt8Value >= right.UInt8Value);
                    else
                        return CreatePrimitiveValue(location, left.Int8Value >= right.Int8Value);
                case BasicValueType.Int16:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt16Value >= right.UInt16Value);
                    else
                        return CreatePrimitiveValue(location, left.Int16Value >= right.Int16Value);
                case BasicValueType.Int32:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt32Value >= right.UInt32Value);
                    else
                        return CreatePrimitiveValue(location, left.Int32Value >= right.Int32Value);
                case BasicValueType.Int64:
                    if (isUnsigned)
                        return CreatePrimitiveValue(location, left.UInt64Value >= right.UInt64Value);
                    else
                        return CreatePrimitiveValue(location, left.Int64Value >= right.Int64Value);
            }
            throw location.GetNotSupportedException(
                ErrorMessages.NotSupportedCompareArgumentType,
                left.BasicValueType);
        }

    }
}

#pragma warning restore IDE0066 // Convert switch statement to expression
#pragma warning restore IDE0046 // Convert to conditional expression
#pragma warning restore IDE0047 // Remove unnecessary parentheses