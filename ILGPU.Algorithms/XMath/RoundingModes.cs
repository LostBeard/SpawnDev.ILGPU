// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2020-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: RoundingModes.tt/RoundingModes.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Runtime.CompilerServices;


namespace ILGPU.Algorithms
{
    partial class XMath
    {
        /// <summary>
        /// Provides shared implementations for math rounding functions.
        /// </summary>
        internal class RoundingModes
        {
            internal const ulong DoubleSignMask = 0x8000_0000_0000_0000;
            internal const ulong DoubleExponentMask = 0x7FF0_0000_0000_0000;
            internal const ulong DoubleMantissaMask = 0x000F_FFFF_FFFF_FFFF;
            internal const int DoubleExponentBias = 1023;
            internal const int DoubleNumMantissaBits = 52;

            internal const uint FloatSignMask = 0x8000_0000;
            internal const uint FloatExponentMask = 0x7F80_0000;
            internal const uint FloatMantissaMask = 0x007F_FFFF;
            internal const int FloatExponentBias = 127;
            internal const int FloatNumMantissaBits = 23;

            /// <summary>
            /// Rounds the given value towards negative infinity.
            /// If the value is positive, discard any fractional bits.
            /// If the value is negative, the fractional bits will round to the next
            /// smaller integer.
            /// </summary>
            /// <param name="value">The value to round.</param>
            /// <returns>The rounded value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float RoundToNegativeInfinity(
                float value)
            {
                uint bits = Interop.FloatAsInt(value);

                // If the value is +/- zero, return the original value.
                if ((bits & ~FloatSignMask) == 0)
                    return value;

                // If exponent is negative, the result will be fractional.
                var exponent =
                    (int)((bits & FloatExponentMask)
                        >> FloatNumMantissaBits)
                    - FloatExponentBias;
                if (exponent < 0)
                {
                    return Utilities.Select(
                        (bits & FloatSignMask) == 0,
                        0.0f,
                        -1.0f);
                }
                else if (exponent >= FloatNumMantissaBits)
                {
                    // If the exponent is large enough, there will be no significant bits
                    // to represent fractions. Return the original value because it will
                    // round to itself.
                    return value;
                }
                else
                {
                    // The value has fractional bits, so we need to determine the
                    // rounding.
                    var mantissa = bits & FloatMantissaMask;
                    var midpoint = 1U
                        << (FloatNumMantissaBits - exponent - 1);
                    var fractionalMask = (midpoint << 1) - 1;
                    var fractional = mantissa & fractionalMask;
                    var truncatedValue = bits & ~fractionalMask;

                    // If the value is positive, discard any fractional bits.
                    // If the value is negative, the fractional bits will round to the
                    // next smaller integer.
                    return Utilities.Select(
                        (bits & FloatSignMask) == 0
                        || fractional <= 0,
                        Interop.IntAsFloat(truncatedValue),
                        Interop.IntAsFloat(truncatedValue + (midpoint << 1)));
                }
            }

            /// <summary>
            /// Rounds the given value towards negative infinity.
            /// If the value is positive, discard any fractional bits.
            /// If the value is negative, the fractional bits will round to the next
            /// smaller integer.
            /// </summary>
            /// <param name="value">The value to round.</param>
            /// <returns>The rounded value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static double RoundToNegativeInfinity(
                double value)
            {
                ulong bits = Interop.FloatAsInt(value);

                // If the value is +/- zero, return the original value.
                if ((bits & ~DoubleSignMask) == 0)
                    return value;

                // If exponent is negative, the result will be fractional.
                var exponent =
                    (int)((bits & DoubleExponentMask)
                        >> DoubleNumMantissaBits)
                    - DoubleExponentBias;
                if (exponent < 0)
                {
                    return Utilities.Select(
                        (bits & DoubleSignMask) == 0,
                        0.0,
                        -1.0);
                }
                else if (exponent >= DoubleNumMantissaBits)
                {
                    // If the exponent is large enough, there will be no significant bits
                    // to represent fractions. Return the original value because it will
                    // round to itself.
                    return value;
                }
                else
                {
                    // The value has fractional bits, so we need to determine the
                    // rounding.
                    var mantissa = bits & DoubleMantissaMask;
                    var midpoint = 1UL
                        << (DoubleNumMantissaBits - exponent - 1);
                    var fractionalMask = (midpoint << 1) - 1;
                    var fractional = mantissa & fractionalMask;
                    var truncatedValue = bits & ~fractionalMask;

                    // If the value is positive, discard any fractional bits.
                    // If the value is negative, the fractional bits will round to the
                    // next smaller integer.
                    return Utilities.Select(
                        (bits & DoubleSignMask) == 0
                        || fractional <= 0,
                        Interop.IntAsFloat(truncatedValue),
                        Interop.IntAsFloat(truncatedValue + (midpoint << 1)));
                }
            }

            /// <summary>
            /// Rounds the given value towards positive infinity.
            /// If the value is negative, discard any fractional bits.
            /// If the value is positive, the fractional bits will round to the next
            /// larger integer.
            /// </summary>
            /// <param name="value">The value to round.</param>
            /// <returns>The rounded value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float RoundToPositiveInfinity(
                float value)
            {
                uint bits = Interop.FloatAsInt(value);

                // If the value is +/- zero, return the original value.
                if ((bits & ~FloatSignMask) == 0)
                    return value;

                // If exponent is negative, the result will be fractional.
                var exponent =
                    (int)((bits & FloatExponentMask)
                        >> FloatNumMantissaBits)
                    - FloatExponentBias;
                if (exponent < 0)
                {
                    return Utilities.Select(
                        (bits & FloatSignMask) != 0,
                        0.0f,
                        1.0f);
                }
                else if (exponent >= FloatNumMantissaBits)
                {
                    // If the exponent is large enough, there will be no significant bits
                    // to represent fractions. Return the original value because it will
                    // round to itself.
                    return value;
                }
                else
                {
                    // The value has fractional bits, so we need to determine the
                    // rounding.
                    var mantissa = bits & FloatMantissaMask;
                    var midpoint = 1U
                        << (FloatNumMantissaBits - exponent - 1);
                    var fractionalMask = (midpoint << 1) - 1;
                    var fractional = mantissa & fractionalMask;
                    var truncatedValue = bits & ~fractionalMask;

                    // If the value is negative, discard any fractional bits.
                    // If the value is positive, the fractional bits will round to the
                    // next larger integer.
                    return Utilities.Select(
                        (bits & FloatSignMask) != 0
                        || fractional <= 0,
                        Interop.IntAsFloat(truncatedValue),
                        Interop.IntAsFloat(truncatedValue + (midpoint << 1)));
                }
            }

            /// <summary>
            /// Rounds the given value towards positive infinity.
            /// If the value is negative, discard any fractional bits.
            /// If the value is positive, the fractional bits will round to the next
            /// larger integer.
            /// </summary>
            /// <param name="value">The value to round.</param>
            /// <returns>The rounded value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static double RoundToPositiveInfinity(
                double value)
            {
                ulong bits = Interop.FloatAsInt(value);

                // If the value is +/- zero, return the original value.
                if ((bits & ~DoubleSignMask) == 0)
                    return value;

                // If exponent is negative, the result will be fractional.
                var exponent =
                    (int)((bits & DoubleExponentMask)
                        >> DoubleNumMantissaBits)
                    - DoubleExponentBias;
                if (exponent < 0)
                {
                    return Utilities.Select(
                        (bits & DoubleSignMask) != 0,
                        0.0,
                        1.0);
                }
                else if (exponent >= DoubleNumMantissaBits)
                {
                    // If the exponent is large enough, there will be no significant bits
                    // to represent fractions. Return the original value because it will
                    // round to itself.
                    return value;
                }
                else
                {
                    // The value has fractional bits, so we need to determine the
                    // rounding.
                    var mantissa = bits & DoubleMantissaMask;
                    var midpoint = 1UL
                        << (DoubleNumMantissaBits - exponent - 1);
                    var fractionalMask = (midpoint << 1) - 1;
                    var fractional = mantissa & fractionalMask;
                    var truncatedValue = bits & ~fractionalMask;

                    // If the value is negative, discard any fractional bits.
                    // If the value is positive, the fractional bits will round to the
                    // next larger integer.
                    return Utilities.Select(
                        (bits & DoubleSignMask) != 0
                        || fractional <= 0,
                        Interop.IntAsFloat(truncatedValue),
                        Interop.IntAsFloat(truncatedValue + (midpoint << 1)));
                }
            }

            /// <summary>
            /// Rounds the given value towards the nearest whole number.
            /// If the value is halfway, it is rounded away from zero.
            /// </summary>
            /// <param name="value">The value to round.</param>
            /// <returns>The rounded value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float RoundAwayFromZero(
                float value)
            {
                uint bits = Interop.FloatAsInt(value);

                // If the value is +/- zero, return the original value.
                if ((bits & ~FloatSignMask) == 0)
                    return value;

                // If exponent is negative, the result will be fractional.
                var exponent =
                    (int)((bits & FloatExponentMask)
                        >> FloatNumMantissaBits)
                    - FloatExponentBias;
                if (exponent < 0)
                {
                    // If the exponent is -1, it rounds to one.
                    // i.e. 2^(-1) = 0.5 (returns 1, round away from zero).
                    // Otherwise, the value is less than 0.5, round to zero.
                    //
                    // NB: Preserve the original +/- sign.
                    uint roundedValue = Utilities.Select(
                        exponent == -1,
                        Interop.FloatAsInt(1.0f),
                        Interop.FloatAsInt(0.0f));
                    return Interop.IntAsFloat(
                        (bits & FloatSignMask) | roundedValue);
                }
                else if (exponent >= FloatNumMantissaBits)
                {
                    // If the exponent is large enough, there will be no significant bits
                    // to represent fractions. Return the original value because it will
                    // round to itself.
                    return value;
                }
                else
                {
                    // The value has fractional bits, so we need to determine the
                    // rounding.
                    var mantissa = bits & FloatMantissaMask;
                    var midpoint = 1U
                        << (FloatNumMantissaBits - exponent - 1);
                    var fractionalMask = (midpoint << 1) - 1;
                    var fractional = mantissa & fractionalMask;
                    var truncatedValue = bits & ~fractionalMask;

                    // If the fraction is < 0.5, round towards zero by discarding the
                    // fractional bits. Otherwise, the fraction is >= 0.5, so it should
                    // be rounded away from zero by adding "one".
                    return Utilities.Select(
                        fractional < midpoint,
                        Interop.IntAsFloat(truncatedValue),
                        Interop.IntAsFloat(truncatedValue + (midpoint << 1)));
                }
            }

            /// <summary>
            /// Rounds the given value towards the nearest whole number.
            /// If the value is halfway, it is rounded away from zero.
            /// </summary>
            /// <param name="value">The value to round.</param>
            /// <returns>The rounded value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static double RoundAwayFromZero(
                double value)
            {
                ulong bits = Interop.FloatAsInt(value);

                // If the value is +/- zero, return the original value.
                if ((bits & ~DoubleSignMask) == 0)
                    return value;

                // If exponent is negative, the result will be fractional.
                var exponent =
                    (int)((bits & DoubleExponentMask)
                        >> DoubleNumMantissaBits)
                    - DoubleExponentBias;
                if (exponent < 0)
                {
                    // If the exponent is -1, it rounds to one.
                    // i.e. 2^(-1) = 0.5 (returns 1, round away from zero).
                    // Otherwise, the value is less than 0.5, round to zero.
                    //
                    // NB: Preserve the original +/- sign.
                    ulong roundedValue = Utilities.Select(
                        exponent == -1,
                        Interop.FloatAsInt(1.0),
                        Interop.FloatAsInt(0.0));
                    return Interop.IntAsFloat(
                        (bits & DoubleSignMask) | roundedValue);
                }
                else if (exponent >= DoubleNumMantissaBits)
                {
                    // If the exponent is large enough, there will be no significant bits
                    // to represent fractions. Return the original value because it will
                    // round to itself.
                    return value;
                }
                else
                {
                    // The value has fractional bits, so we need to determine the
                    // rounding.
                    var mantissa = bits & DoubleMantissaMask;
                    var midpoint = 1UL
                        << (DoubleNumMantissaBits - exponent - 1);
                    var fractionalMask = (midpoint << 1) - 1;
                    var fractional = mantissa & fractionalMask;
                    var truncatedValue = bits & ~fractionalMask;

                    // If the fraction is < 0.5, round towards zero by discarding the
                    // fractional bits. Otherwise, the fraction is >= 0.5, so it should
                    // be rounded away from zero by adding "one".
                    return Utilities.Select(
                        fractional < midpoint,
                        Interop.IntAsFloat(truncatedValue),
                        Interop.IntAsFloat(truncatedValue + (midpoint << 1)));
                }
            }

            /// <summary>
            /// Rounds the given value towards the nearest whole number.
            /// If the value is halfway, it is rounded to the even number.
            /// </summary>
            /// <param name="value">The value to round.</param>
            /// <returns>The rounded value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float RoundToEven(
                float value)
            {
                uint bits = Interop.FloatAsInt(value);

                // If the value is +/- zero, return the original value.
                if ((bits & ~FloatSignMask) == 0)
                    return value;

                // If exponent is negative, the result will be fractional.
                var exponent =
                    (int)((bits & FloatExponentMask)
                        >> FloatNumMantissaBits)
                    - FloatExponentBias;
                if (exponent < 0)
                {
                    // If the exponent is -1, with no mantissa, it rounds
                    // to zero. i.e. 2^(-1) = 0.5 (returns 0, round to even).
                    //
                    // If there are mantissa bits, the value is greater than 0.5,
                    // so it rounds to one.
                    //
                    // NB: Preserve the original +/- sign.
                    var mantissa = bits & FloatMantissaMask;
                    uint roundedValue = Utilities.Select(
                        exponent == -1 && mantissa != 0,
                        Interop.FloatAsInt(1.0f),
                        Interop.FloatAsInt(0.0f));
                    return Interop.IntAsFloat(
                        (bits & FloatSignMask) | roundedValue);
                }
                else if (exponent >= FloatNumMantissaBits)
                {
                    // If the exponent is large enough, there will be no significant bits
                    // to represent fractions. Return the original value because it will
                    // round to itself.
                    return value;
                }
                else
                {
                    // The value has fractional bits, so we need to determine the
                    // rounding.
                    var mantissa = bits & FloatMantissaMask;
                    var midpoint = 1U
                        << (FloatNumMantissaBits - exponent - 1);
                    var fractionalMask = (midpoint << 1) - 1;
                    var fractional = mantissa & fractionalMask;
                    var truncatedValue = bits & ~fractionalMask;

                    // If the fraction is < 0.5, discarding the fractional bits.
                    // Otherwise, the fraction is > 0.5, so it should be rounded up by
                    // adding "one".
                    //
                    // If the fraction == 0.5, and the value is already even, discard the
                    // fractional bits. Otherwise, the number is odd, so round it to the
                    // next even number by adding "one".
                    if (fractional < midpoint)
                    {
                        return Interop.IntAsFloat(truncatedValue);
                    }
                    else if (fractional > midpoint)
                    {
                        return Interop.IntAsFloat(truncatedValue + (midpoint << 1));
                    }
                    else
                    {
                        // The fractional part is exactly x.5 - Round To Even.
                        var evenMask = midpoint << 1;
                        return Utilities.Select(
                            (bits & evenMask) == 0, // bits, not mantissa: for 1 <= |x| < 2 the integer part's
                            // low bit is the IMPLICIT 1 (the exponent field's low bit here), which the
                            // mantissa mask drops - 1.5 rounded to 1 instead of 2.
                            Interop.IntAsFloat(truncatedValue),
                            Interop.IntAsFloat(truncatedValue + (midpoint << 1)));
                    }
                }
            }

            /// <summary>
            /// Rounds the given value towards the nearest whole number.
            /// If the value is halfway, it is rounded to the even number.
            /// </summary>
            /// <param name="value">The value to round.</param>
            /// <returns>The rounded value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static double RoundToEven(
                double value)
            {
                ulong bits = Interop.FloatAsInt(value);

                // If the value is +/- zero, return the original value.
                if ((bits & ~DoubleSignMask) == 0)
                    return value;

                // If exponent is negative, the result will be fractional.
                var exponent =
                    (int)((bits & DoubleExponentMask)
                        >> DoubleNumMantissaBits)
                    - DoubleExponentBias;
                if (exponent < 0)
                {
                    // If the exponent is -1, with no mantissa, it rounds
                    // to zero. i.e. 2^(-1) = 0.5 (returns 0, round to even).
                    //
                    // If there are mantissa bits, the value is greater than 0.5,
                    // so it rounds to one.
                    //
                    // NB: Preserve the original +/- sign.
                    var mantissa = bits & DoubleMantissaMask;
                    ulong roundedValue = Utilities.Select(
                        exponent == -1 && mantissa != 0,
                        Interop.FloatAsInt(1.0),
                        Interop.FloatAsInt(0.0));
                    return Interop.IntAsFloat(
                        (bits & DoubleSignMask) | roundedValue);
                }
                else if (exponent >= DoubleNumMantissaBits)
                {
                    // If the exponent is large enough, there will be no significant bits
                    // to represent fractions. Return the original value because it will
                    // round to itself.
                    return value;
                }
                else
                {
                    // The value has fractional bits, so we need to determine the
                    // rounding.
                    var mantissa = bits & DoubleMantissaMask;
                    var midpoint = 1UL
                        << (DoubleNumMantissaBits - exponent - 1);
                    var fractionalMask = (midpoint << 1) - 1;
                    var fractional = mantissa & fractionalMask;
                    var truncatedValue = bits & ~fractionalMask;

                    // If the fraction is < 0.5, discarding the fractional bits.
                    // Otherwise, the fraction is > 0.5, so it should be rounded up by
                    // adding "one".
                    //
                    // If the fraction == 0.5, and the value is already even, discard the
                    // fractional bits. Otherwise, the number is odd, so round it to the
                    // next even number by adding "one".
                    if (fractional < midpoint)
                    {
                        return Interop.IntAsFloat(truncatedValue);
                    }
                    else if (fractional > midpoint)
                    {
                        return Interop.IntAsFloat(truncatedValue + (midpoint << 1));
                    }
                    else
                    {
                        // The fractional part is exactly x.5 - Round To Even.
                        var evenMask = midpoint << 1;
                        return Utilities.Select(
                            (bits & evenMask) == 0, // bits, not mantissa: for 1 <= |x| < 2 the integer part's
                            // low bit is the IMPLICIT 1 (the exponent field's low bit here), which the
                            // mantissa mask drops - 1.5 rounded to 1 instead of 2.
                            Interop.IntAsFloat(truncatedValue),
                            Interop.IntAsFloat(truncatedValue + (midpoint << 1)));
                    }
                }
            }

            /// <summary>
            /// Rounds the value to the nearest value (halfway cases are rounded to even).
            /// </summary>
            /// <param name="value">The value.</param>
            /// <returns>The nearest value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Round(
                float value) =>
                Round(value, MidpointRounding.ToEven);

            /// <summary>
            /// Rounds the value to the nearest value (halfway cases are rounded to even).
            /// </summary>
            /// <param name="value">The value.</param>
            /// <param name="digits">
            /// The number of fractional digits in the return value.
            /// </param>
            /// <returns>The nearest value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Round(
                float value,
                int digits) =>
                Round(value, digits, MidpointRounding.ToEven);

            /// <summary>
            /// Rounds the value to the nearest value.
            /// </summary>
            /// <param name="value">The value.</param>
            /// <param name="mode">
            /// Specifiies how to round value if it is midway between two numbers.
            /// </param>
            /// <returns>The nearest value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Round(
                float value,
                MidpointRounding mode) =>
                Round(value, 0, mode);

            /// <summary>
            /// Rounds the value to the nearest value.
            /// </summary>
            /// <param name="value">The value.</param>
            /// <param name="digits">
            /// The number of fractional digits in the return value.
            /// </param>
            /// <param name="mode">
            /// Specifiies how to round value if it is midway between two numbers.
            /// </param>
            /// <returns>The nearest value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static float Round(
                float value,
                int digits,
                MidpointRounding mode)
            {
                var result = value;

                // Only need to do work if the supplied value is smaller than the minimum
                // that we can round.
                if (XMath.Abs(result) <
                    1e7f)
                {
                    // Shift the input value by the number of digits requested.
                    // If digits < 0, fallback to 0 digits.
                    // If digits > 6, fallback to 6.
                    float multiplier = digits switch
                        {
                            int d when d <= 0 => 1e0f,
                            1 => 1e1f,
                            2 => 1e2f,
                            3 => 1e3f,
                            4 => 1e4f,
                            5 => 1e5f,
                            _ => 1e6f
                        };

                    // NOTE: net471 and netstandard2.1 only support the first two modes:
                    // In order to fully support netcoreapp3.1 and net5.0, we convert
                    // the mode to an integer.
                    //
                    // This is also a workaround for the T4 template not supporting the
                    // newer rounding modes.
                    //
                    // This has the side effect of adding extra functionality to net471
                    // and netstandard2.1.
                    //
                    // 0 - MidpointRounding.ToEven
                    // 1 - MidpointRounding.AwayFromZero
                    // 2 - MidpointRounding.ToZero
                    // 3 - MidpointRounding.ToNegativeInfinity
                    // 4 - MidpointRounding.ToPositiveInfinity
                    //
                    // If the rounding mode is out of range, fallback to the
                    // default MidpointRounding.ToEven.
                    result *= multiplier;
                    result = (int)mode switch
                    {
                        0 => XMath.RoundToEven(result),
                        1 => XMath.RoundAwayFromZero(result),
                        2 => RoundToZero(result),
                        3 => RoundToNegativeInfinity(result),
                        4 => RoundToPositiveInfinity(result),
                        _ => XMath.RoundToEven(result)
                    };
                    result /= multiplier;
                }

                return result;
            }

            /// <summary>
            /// Rounds the value to the nearest value (halfway cases are rounded to even).
            /// </summary>
            /// <param name="value">The value.</param>
            /// <returns>The nearest value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static double Round(
                double value) =>
                Round(value, MidpointRounding.ToEven);

            /// <summary>
            /// Rounds the value to the nearest value (halfway cases are rounded to even).
            /// </summary>
            /// <param name="value">The value.</param>
            /// <param name="digits">
            /// The number of fractional digits in the return value.
            /// </param>
            /// <returns>The nearest value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static double Round(
                double value,
                int digits) =>
                Round(value, digits, MidpointRounding.ToEven);

            /// <summary>
            /// Rounds the value to the nearest value.
            /// </summary>
            /// <param name="value">The value.</param>
            /// <param name="mode">
            /// Specifiies how to round value if it is midway between two numbers.
            /// </param>
            /// <returns>The nearest value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static double Round(
                double value,
                MidpointRounding mode) =>
                Round(value, 0, mode);

            /// <summary>
            /// Rounds the value to the nearest value.
            /// </summary>
            /// <param name="value">The value.</param>
            /// <param name="digits">
            /// The number of fractional digits in the return value.
            /// </param>
            /// <param name="mode">
            /// Specifiies how to round value if it is midway between two numbers.
            /// </param>
            /// <returns>The nearest value.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static double Round(
                double value,
                int digits,
                MidpointRounding mode)
            {
                var result = value;

                // Only need to do work if the supplied value is smaller than the minimum
                // that we can round.
                if (XMath.Abs(result) <
                    1e16)
                {
                    // Shift the input value by the number of digits requested.
                    // If digits < 0, fallback to 0 digits.
                    // If digits > 15, fallback to 15.
                    double multiplier = digits switch
                        {
                            int d when d <= 0 => 1e0,
                            1 => 1e1,
                            2 => 1e2,
                            3 => 1e3,
                            4 => 1e4,
                            5 => 1e5,
                            6 => 1e6,
                            7 => 1e7,
                            8 => 1e8,
                            9 => 1e9,
                            10 => 1e10,
                            11 => 1e11,
                            12 => 1e12,
                            13 => 1e13,
                            14 => 1e14,
                            _ => 1e15
                        };

                    // NOTE: net471 and netstandard2.1 only support the first two modes:
                    // In order to fully support netcoreapp3.1 and net5.0, we convert
                    // the mode to an integer.
                    //
                    // This is also a workaround for the T4 template not supporting the
                    // newer rounding modes.
                    //
                    // This has the side effect of adding extra functionality to net471
                    // and netstandard2.1.
                    //
                    // 0 - MidpointRounding.ToEven
                    // 1 - MidpointRounding.AwayFromZero
                    // 2 - MidpointRounding.ToZero
                    // 3 - MidpointRounding.ToNegativeInfinity
                    // 4 - MidpointRounding.ToPositiveInfinity
                    //
                    // If the rounding mode is out of range, fallback to the
                    // default MidpointRounding.ToEven.
                    result *= multiplier;
                    result = (int)mode switch
                    {
                        0 => XMath.RoundToEven(result),
                        1 => XMath.RoundAwayFromZero(result),
                        2 => RoundToZero(result),
                        3 => RoundToNegativeInfinity(result),
                        4 => RoundToPositiveInfinity(result),
                        _ => XMath.RoundToEven(result)
                    };
                    result /= multiplier;
                }

                return result;
            }

            /// <summary>
            /// Rounds the value to the nearest value (halfway cases are rounded to zero).
            /// </summary>
            /// <param name="value">The value.</param>
            /// <returns>The nearest value.</returns>
            public static double RoundToZero(double value) =>
                Truncate(value);

            /// <summary>
            /// Rounds the value to the nearest value (halfway cases are rounded to zero).
            /// </summary>
            /// <param name="value">The value.</param>
            /// <returns>The nearest value.</returns>
            public static float RoundToZero(float value) =>
                Truncate(value);
        }
    }
}
