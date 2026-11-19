// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2021-2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: IntrinsicMath.Libm.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------
//
// Ported from musl libc (src/math/log1p.c, log1pf.c, cbrt.c, asinh.c, asinhf.c, acosh.c,
// acoshf.c, atanh.c, atanhf.c; the float Cbrt is our own, using cbrtf.c's bias constant). musl is licensed under the MIT license:
//
//   Copyright (c) 2005-2020 Rich Felker, et al.
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this
//   software and associated documentation files (the "Software"), to deal in the Software
//   without restriction, including without limitation the rights to use, copy, modify,
//   merge, publish, distribute, sublicense, and/or sell copies of the Software, and to
//   permit persons to whom the Software is furnished to do so, subject to the following
//   conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies
//   or substantial portions of the Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
//   INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
//   PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
//   HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
//   CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR
//   THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// log1p, log1pf, cbrt and cbrtf originate in FreeBSD msun (s_log1p.c, s_log1pf.c, s_cbrt.c,
// s_cbrtf.c) and carry this notice, preserved as it requires:
//
//   Copyright (C) 1993 by Sun Microsystems, Inc. All rights reserved.
//
//   Developed at SunPro, a Sun Microsystems, Inc. business.
//   Permission to use, copy, modify, and distribute this
//   software is freely granted, provided that this notice
//   is preserved.
//
//   (cbrt / cbrtf: optimized by Bruce D. Evans.)
// ---------------------------------------------------------------------------------------

using System;

namespace ILGPU
{
    // System.Math / MathF.Cbrt, Asinh, Acosh and Atanh have no IL body (the runtime supplies them), so a kernel
    // that called one compiled a call to a function no device has: CUDA failed in ptxas with "Unresolved extern
    // function", the other backends at compile time (Tuvok, 2026-11-18, with MathF.Log2; an audit of every Math /
    // MathF member found these four plus Log2). The frontend now remaps them to the implementations below
    // (RemappedIntrinsics.Generated.tt); they are plain arithmetic and bit casts, so every backend compiles them.
    // Test: BackendTestBase.LibmFunctions (ulp error against the host on every lane).
    partial class IntrinsicMath
    {
        #region log1p

        private const float Ln2HiF = 0.6931381225585938f;       // 0x3f317180
        private const float Ln2LoF = 9.05800061445916e-06f;     // 0x3717f7d1
        private const float Log1PLg1F = 0.6666666269302368f;    // 0x3f2aaaaa
        private const float Log1PLg2F = 0.40000972151756287f;   // 0x3eccce13
        private const float Log1PLg3F = 0.2849878668785095f;    // 0x3e91e9ee
        private const float Log1PLg4F = 0.24279078841209412f;   // 0x3e789e26

        private const double Ln2Hi = 6.93147180369123816490e-01; // 3fe62e42 fee00000
        private const double Ln2Lo = 1.90821492927058770002e-10; // 3dea39ef 35793c76
        private const double Log1PLg1 = 6.666666666666735130e-01; // 3FE55555 55555593
        private const double Log1PLg2 = 3.999999999940941908e-01; // 3FD99999 9997FA04
        private const double Log1PLg3 = 2.857142874366239149e-01; // 3FD24924 94229359
        private const double Log1PLg4 = 2.222219843214978396e-01; // 3FCC71C5 1D8E78AF
        private const double Log1PLg5 = 1.818357216161805012e-01; // 3FC74664 96CB03DE
        private const double Log1PLg6 = 1.531383769920937332e-01; // 3FC39A09 D078C69F
        private const double Log1PLg7 = 1.479819860511658591e-01; // 3FC2F112 DF3E5244

        /// <summary>
        /// Computes log(1 + x) accurately for x near 0.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>log(1 + x).</returns>
        public static float Log1P(float x)
        {
            uint ix = Interop.FloatAsInt(x);
            int k = 1;
            float c = 0.0f;
            float f = 0.0f;
            if (ix < 0x3ed413d0u || (ix >> 31) != 0)
            {
                // 1 + x < sqrt(2)+
                if (ix >= 0xbf800000u)
                {
                    // x <= -1.0 (or a negative NaN)
                    return x == -1.0f ? float.NegativeInfinity : float.NaN;
                }
                if ((ix << 1) < (0x33800000u << 1))
                {
                    // |x| < 2^-24
                    return x;
                }
                if (ix <= 0xbe95f619u)
                {
                    // sqrt(2)/2- <= 1 + x < sqrt(2)+
                    k = 0;
                    f = x;
                }
            }
            else if (ix >= 0x7f800000u)
            {
                return x;
            }
            if (k != 0)
            {
                float uf = 1.0f + x;
                uint iu = Interop.FloatAsInt(uf);
                iu += 0x3f800000u - 0x3f3504f3u;
                k = (int)(iu >> 23) - 0x7f;
                // correction term ~ log(1 + x) - log(u), avoiding underflow in c / u
                if (k < 25)
                {
                    c = k >= 2 ? 1.0f - (uf - x) : x - (uf - 1.0f);
                    c /= uf;
                }
                // reduce u into [sqrt(2)/2, sqrt(2)]
                iu = (iu & 0x007fffffu) + 0x3f3504f3u;
                f = Interop.IntAsFloat(iu) - 1.0f;
            }
            float s = f / (2.0f + f);
            float z = s * s;
            float w = z * z;
            float t1 = w * (Log1PLg2F + w * Log1PLg4F);
            float t2 = z * (Log1PLg1F + w * Log1PLg3F);
            float r = t2 + t1;
            float hfsq = 0.5f * f * f;
            float dk = k;
            return s * (hfsq + r) + (dk * Ln2LoF + c) - hfsq + f + dk * Ln2HiF;
        }

        /// <summary>
        /// Computes log(1 + x) accurately for x near 0.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>log(1 + x).</returns>
        public static double Log1P(double x)
        {
            ulong bits = Interop.FloatAsInt(x);
            uint hx = (uint)(bits >> 32);
            int k = 1;
            double c = 0.0;
            double f = 0.0;
            if (hx < 0x3fda827au || (hx >> 31) != 0)
            {
                // 1 + x < sqrt(2)+
                if (hx >= 0xbff00000u)
                {
                    // x <= -1.0 (or a negative NaN)
                    return x == -1.0 ? double.NegativeInfinity : double.NaN;
                }
                if ((hx << 1) < (0x3ca00000u << 1))
                {
                    // |x| < 2^-53
                    return x;
                }
                if (hx <= 0xbfd2bec4u)
                {
                    // sqrt(2)/2- <= 1 + x < sqrt(2)+
                    k = 0;
                    f = x;
                }
            }
            else if (hx >= 0x7ff00000u)
            {
                return x;
            }
            if (k != 0)
            {
                double uf = 1.0 + x;
                ulong ubits = Interop.FloatAsInt(uf);
                uint hu = (uint)(ubits >> 32);
                hu += 0x3ff00000u - 0x3fe6a09eu;
                k = (int)(hu >> 20) - 0x3ff;
                // correction term ~ log(1 + x) - log(u), avoiding underflow in c / u
                if (k < 54)
                {
                    c = k >= 2 ? 1.0 - (uf - x) : x - (uf - 1.0);
                    c /= uf;
                }
                // reduce u into [sqrt(2)/2, sqrt(2)]
                hu = (hu & 0x000fffffu) + 0x3fe6a09eu;
                ubits = ((ulong)hu << 32) | (ubits & 0xffffffffUL);
                f = Interop.IntAsFloat(ubits) - 1.0;
            }
            double hfsq = 0.5 * f * f;
            double s = f / (2.0 + f);
            double z = s * s;
            double w = z * z;
            double t1 = w * (Log1PLg2 + w * (Log1PLg4 + w * Log1PLg6));
            double t2 = z * (Log1PLg1 + w * (Log1PLg3 + w * (Log1PLg5 + w * Log1PLg7)));
            double r = t2 + t1;
            double dk = k;
            return s * (hfsq + r) + (dk * Ln2Lo + c) - hfsq + f + dk * Ln2Hi;
        }

        #endregion

        #region cbrt

        private const double CbrtP0 = 1.87595182427177009643;   // 0x3ffe03e6, 0x0f61e692
        private const double CbrtP1 = -1.88497979543377169875;  // 0xbffe28e0, 0x92f02420
        private const double CbrtP2 = 1.621429720105354466140;  // 0x3ff9f160, 0x4a49d6c2
        private const double CbrtP3 = -0.758397934778766047437; // 0xbfe844cb, 0xbee751d9
        private const double CbrtP4 = 0.145996192886612446982;  // 0x3fc2b000, 0xd4e4edd7

        /// <summary>
        /// Computes the cube root of x.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>The cube root of x.</returns>
        public static float Cbrt(float x)
        {
            // Not musl's cbrtf: its Newton steps run in double, which WebGPU / WebGL emulate (slow, and with f32's
            // exponent range in the default modes: cbrt(1e-30f) came out 0). Instead x = y * 2^(3k) with y in [1, 8),
            // three Newton steps in float on y (no overflow or underflow possible), then an exact scale by 2^k.
            // Measured on every positive finite float: at most 1 ulp from the correctly rounded result (.NET's own
            // MathF.Cbrt: 2).
            uint bits = Interop.FloatAsInt(x);
            uint hx = bits & 0x7fffffffu;
            if (hx >= 0x7f800000u)
            {
                // cbrt(NaN, Inf) is itself
                return x + x;
            }
            if (hx == 0)
                return x;
            int adjust = 0;
            if (hx < 0x00800000u)
            {
                // subnormal: scale by 2^24, whose cube root 2^8 comes off the result
                hx = Interop.FloatAsInt(x * 16777216.0f) & 0x7fffffffu;
                adjust = -8;
                // a GPU that flushes subnormals (D3D) saw x as a signed zero, and cbrt(+-0) is +-0
                if (hx < 0x00800000u)
                    return (bits >> 31) != 0 ? -0.0f : 0.0f;
            }
            int e = (int)(hx >> 23) - 127;
            int k = (e + 129) / 3 - 43; // floor(e / 3) without a negative division
            int r = e - 3 * k;          // 0, 1 or 2
            float y = Interop.IntAsFloat(((uint)(127 + r) << 23) | (hx & 0x007fffffu));
            // cbrt(y) to about 5 bits by dividing the bits by 3 (musl's B1 bias), then three Newton steps
            float t = Interop.IntAsFloat(Interop.FloatAsInt(y) / 3 + 709958130u);
            t -= (t - y / (t * t)) / 3.0f;
            t -= (t - y / (t * t)) / 3.0f;
            t -= (t - y / (t * t)) / 3.0f;
            float result = t * Interop.IntAsFloat((uint)(127 + k + adjust) << 23);
            return (bits >> 31) != 0 ? -result : result;
        }

        /// <summary>
        /// Computes the cube root of x.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>The cube root of x.</returns>
        public static double Cbrt(double x)
        {
            ulong bits = Interop.FloatAsInt(x);
            uint hx = (uint)(bits >> 32) & 0x7fffffffu;
            if (hx >= 0x7ff00000u)
            {
                // cbrt(NaN, Inf) is itself
                return x + x;
            }
            // Rough cbrt to 5 bits: integer division of the exponent-biased bits by 3 (see musl cbrt.c).
            if (hx < 0x00100000u)
            {
                // zero or subnormal
                ulong scaled = Interop.FloatAsInt(x * 18014398509481984.0); // x * 2^54
                hx = (uint)(scaled >> 32) & 0x7fffffffu;
                if (hx == 0)
                    return x;
                hx = hx / 3 + 696219795u;
            }
            else
            {
                hx = hx / 3 + 715094163u;
            }
            double t = Interop.IntAsFloat((bits & 0x8000000000000000UL) | ((ulong)hx << 32));
            // cbrt to 23 bits: t * P(t^3 / x), P approximating 1/cbrt(r) to 2^-23.5 for |r - 1| < 1/10
            double r = (t * t) * (t / x);
            t = t * ((CbrtP0 + r * (CbrtP1 + r * CbrtP2)) + ((r * r) * r) * (CbrtP3 + r * CbrtP4));
            // round t away from zero to 23 bits, so that t * t below is exact
            t = Interop.IntAsFloat((Interop.FloatAsInt(t) + 0x80000000UL) & 0xffffffffc0000000UL);
            // one Newton step to 53 bits, error < 0.667 ulp
            double s = t * t;
            r = x / s;
            double w = t + t;
            r = (r - t) / (w + r);
            return t + t * r;
        }

        #endregion

        #region asinh / acosh / atanh

        private const float Ln2F = 0.693147180559945309417232121458176568f;
        private const double Ln2 = 0.693147180559945309417232121458176568;

        /// <summary>
        /// Computes the inverse hyperbolic sine of x.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>asinh(x).</returns>
        public static float Asinh(float x)
        {
            uint bits = Interop.FloatAsInt(x);
            uint i = bits & 0x7fffffffu;
            float a = Interop.IntAsFloat(i);
            if (i >= 0x3f800000u + (12u << 23))
            {
                // |x| >= 2^12, Inf or NaN
                a = MathF.Log(a) + Ln2F;
            }
            else if (i >= 0x3f800000u + (1u << 23))
            {
                // |x| >= 2
                a = MathF.Log(2.0f * a + 1.0f / (MathF.Sqrt(a * a + 1.0f) + a));
            }
            else if (i >= 0x3f800000u - (12u << 23))
            {
                // |x| >= 2^-12
                a = Log1P(a + a * a / (MathF.Sqrt(a * a + 1.0f) + 1.0f));
            }
            // else |x| < 2^-12: asinh(x) = x to float precision
            return (bits >> 31) != 0 ? -a : a;
        }

        /// <summary>
        /// Computes the inverse hyperbolic sine of x.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>asinh(x).</returns>
        public static double Asinh(double x)
        {
            ulong bits = Interop.FloatAsInt(x);
            uint e = (uint)(bits >> 52) & 0x7ffu;
            double a = Interop.IntAsFloat(bits & 0x7fffffffffffffffUL);
            if (e >= 0x3ffu + 26)
            {
                // |x| >= 2^26, Inf or NaN
                a = Math.Log(a) + Ln2;
            }
            else if (e >= 0x3ffu + 1)
            {
                // |x| >= 2
                a = Math.Log(2.0 * a + 1.0 / (Math.Sqrt(a * a + 1.0) + a));
            }
            else if (e >= 0x3ffu - 26)
            {
                // |x| >= 2^-26
                a = Log1P(a + a * a / (Math.Sqrt(a * a + 1.0) + 1.0));
            }
            // else |x| < 2^-26: asinh(x) = x to double precision
            return (bits >> 63) != 0 ? -a : a;
        }

        /// <summary>
        /// Computes the inverse hyperbolic cosine of x.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>acosh(x); NaN when x &lt; 1.</returns>
        public static float Acosh(float x)
        {
            uint bits = Interop.FloatAsInt(x);
            if (bits < 0x3f800000u + (1u << 23))
            {
                // 0 <= x < 2 (NaN when x < 1)
                return Log1P(x - 1.0f + MathF.Sqrt((x - 1.0f) * (x - 1.0f) + 2.0f * (x - 1.0f)));
            }
            if (bits < 0x3f800000u + (12u << 23))
            {
                // 2 <= x < 2^12
                return MathF.Log(2.0f * x - 1.0f / (x + MathF.Sqrt(x * x - 1.0f)));
            }
            if ((bits & 0x80000000u) != 0)
            {
                // x < 0 or -0
                return float.NaN;
            }
            // x >= 2^12, Inf or NaN
            return MathF.Log(x) + Ln2F;
        }

        /// <summary>
        /// Computes the inverse hyperbolic cosine of x.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>acosh(x); NaN when x &lt; 1.</returns>
        public static double Acosh(double x)
        {
            ulong bits = Interop.FloatAsInt(x);
            uint e = (uint)(bits >> 52); // includes the sign bit
            if (e < 0x3ffu + 1)
            {
                // 0 <= x < 2 (NaN when x < 1)
                return Log1P(x - 1.0 + Math.Sqrt((x - 1.0) * (x - 1.0) + 2.0 * (x - 1.0)));
            }
            if (e < 0x3ffu + 26)
            {
                // 2 <= x < 2^26
                return Math.Log(2.0 * x - 1.0 / (x + Math.Sqrt(x * x - 1.0)));
            }
            if ((e & 0x800u) != 0)
            {
                // x < 0 or -0
                return double.NaN;
            }
            // x >= 2^26, Inf or NaN
            return Math.Log(x) + Ln2;
        }

        /// <summary>
        /// Computes the inverse hyperbolic tangent of x.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>atanh(x); +-Inf at +-1, NaN when |x| &gt; 1.</returns>
        public static float Atanh(float x)
        {
            uint bits = Interop.FloatAsInt(x);
            uint i = bits & 0x7fffffffu;
            float y = Interop.IntAsFloat(i);
            if (i < 0x3f800000u - (1u << 23))
            {
                // |x| < 0.5; below 2^-32 atanh(x) = x to float precision
                if (i >= 0x3f800000u - (32u << 23))
                    y = 0.5f * Log1P(2.0f * y + 2.0f * y * y / (1.0f - y));
            }
            else
            {
                // written to avoid overflow
                y = 0.5f * Log1P(2.0f * (y / (1.0f - y)));
            }
            return (bits >> 31) != 0 ? -y : y;
        }

        /// <summary>
        /// Computes the inverse hyperbolic tangent of x.
        /// </summary>
        /// <param name="x">The value.</param>
        /// <returns>atanh(x); +-Inf at +-1, NaN when |x| &gt; 1.</returns>
        public static double Atanh(double x)
        {
            ulong bits = Interop.FloatAsInt(x);
            uint e = (uint)(bits >> 52) & 0x7ffu;
            double y = Interop.IntAsFloat(bits & 0x7fffffffffffffffUL);
            if (e < 0x3ffu - 1)
            {
                // |x| < 0.5; below 2^-32 atanh(x) = x to double precision
                if (e >= 0x3ffu - 32)
                    y = 0.5 * Log1P(2.0 * y + 2.0 * y * y / (1.0 - y));
            }
            else
            {
                // written to avoid overflow
                y = 0.5 * Log1P(2.0 * (y / (1.0 - y)));
            }
            return (bits >> 63) != 0 ? -y : y;
        }

        #endregion
    }
}
