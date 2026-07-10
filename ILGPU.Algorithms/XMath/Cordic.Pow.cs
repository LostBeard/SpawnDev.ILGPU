// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2020-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: Cordic.Pow.tt/Cordic.Pow.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2020-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: Cordic.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// disable: max_line_length

using System.Runtime.CompilerServices;

namespace ILGPU.Algorithms
{
    partial class XMath
    {
        /// <summary>
        /// Implementation of exponential functions using CORDIC approximation.
        /// </summary>
        partial class Cordic
        {
            #region Exponential

            /// <summary>
            /// Implementation of e raised to a specific power using CORDIC approximation.
            /// </summary>
            /// <param name="value">Specifies the power</param>
            /// <returns>The number e raised to the specified power</returns>
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static float Exp(float value)
            {
                // Deal with edge cases
                if (IsNaN(value) || value == float.PositiveInfinity)
                    return value;
                if (value == float.NegativeInfinity)
                    return 0.0f;

                // Deal with negative exponents. NB: call ExpImpl (NOT Exp) so this does not
                // recurse - a self-recursive Exp broke the GPU path (upstream ILGPU #1334;
                // GPU backends cannot recurse, and our browser backends use CORDIC for exp).
                if (value < 0)
                    return 1.0f /
                        ExpImpl(-1.0f * value);

                return ExpImpl(value);
            }

            /// <summary>
            /// Calculates the base-e exponential of a NON-NEGATIVE value. Separated from
            /// <see cref="Exp"/> so its negative-value handling does not recurse into Exp
            /// (GPU backends cannot recurse; upstream ILGPU #1334).
            /// </summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static float ExpImpl(float value)
            {
                // The exponential function is related to hyperbolic functions with the
                // identity:
                //  exp(x) = cosh(x) + sinh(x)
                //
                // Furthermore, the hyperbolic CORDIC algorithm cannot handle the full
                // range of input values, so we simplify the calculations using the
                // formula:
                //
                //  exp(x) = exp(quotient * ln(2) + remainder)
                //         = exp(quotient * ln(2)) * exp(remainder)
                //         = exp(ln(2)) ^ quotient * exp(remainder)
                //         = 2^quotient * exp(remainder)
                //
                // We can calculate 2^quotient easily using repeated multiplication, and
                // the remainder fits within the CORDIC input range.
                //
                // Reference:
                //  https://en.wikipedia.org/wiki/Hyperbolic_functions#Relationship_to_the_exponential_function
                //  https://en.wikipedia.org/wiki/Exponentiation#Identities_and_properties
                //
                var quotient = (int)Floor(value / Ln2);
                var remainder = value - quotient * Ln2;

                // Apply 24 iterations.
                RotateHyperbolicIterations(remainder, out var cosh, out var sinh);

                var twoPowQuotient = 1.0f;
                while (quotient > 0)
                {
                    twoPowQuotient *= 2.0f;
                    quotient -= 1;
                }

                return twoPowQuotient *
                    (cosh + sinh) *
                    HyperbolicGain;
            }

            /// <summary>
            /// Implementation of e raised to a specific power using CORDIC approximation.
            /// </summary>
            /// <param name="value">Specifies the power</param>
            /// <returns>The number e raised to the specified power</returns>
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static double Exp(double value)
            {
                // Deal with edge cases
                if (IsNaN(value) || value == double.PositiveInfinity)
                    return value;
                if (value == double.NegativeInfinity)
                    return 0.0;

                // Deal with negative exponents. NB: call ExpImpl (NOT Exp) so this does not
                // recurse - a self-recursive Exp broke the GPU path (upstream ILGPU #1334;
                // GPU backends cannot recurse, and our browser backends use CORDIC for exp).
                if (value < 0)
                    return 1.0 /
                        ExpImpl(-1.0 * value);

                return ExpImpl(value);
            }

            /// <summary>
            /// Calculates the base-e exponential of a NON-NEGATIVE value. Separated from
            /// <see cref="Exp"/> so its negative-value handling does not recurse into Exp
            /// (GPU backends cannot recurse; upstream ILGPU #1334).
            /// </summary>
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static double ExpImpl(double value)
            {
                // The exponential function is related to hyperbolic functions with the
                // identity:
                //  exp(x) = cosh(x) + sinh(x)
                //
                // Furthermore, the hyperbolic CORDIC algorithm cannot handle the full
                // range of input values, so we simplify the calculations using the
                // formula:
                //
                //  exp(x) = exp(quotient * ln(2) + remainder)
                //         = exp(quotient * ln(2)) * exp(remainder)
                //         = exp(ln(2)) ^ quotient * exp(remainder)
                //         = 2^quotient * exp(remainder)
                //
                // We can calculate 2^quotient easily using repeated multiplication, and
                // the remainder fits within the CORDIC input range.
                //
                // Reference:
                //  https://en.wikipedia.org/wiki/Hyperbolic_functions#Relationship_to_the_exponential_function
                //  https://en.wikipedia.org/wiki/Exponentiation#Identities_and_properties
                //
                var quotient = (int)Floor(value / Ln2D);
                var remainder = value - quotient * Ln2D;

                // Apply 53 iterations.
                RotateHyperbolicIterations(remainder, out var cosh, out var sinh);

                var twoPowQuotient = 1.0;
                while (quotient > 0)
                {
                    twoPowQuotient *= 2.0;
                    quotient -= 1;
                }

                return twoPowQuotient *
                    (cosh + sinh) *
                    HyperbolicGainD;
            }

            #endregion
        }
    }
}