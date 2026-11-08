// FusedMultiplyAddF64 and its helpers are ported from Go's src/math/fma.go:
//   Copyright 2019 The Go Authors. All rights reserved.
//   Use of this source code is governed by a BSD-style license (https://go.dev/LICENSE).
using System;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// Correctly rounded fused multiply-add in software: (a * b) + c rounded ONCE, bit-identical to
    /// <see cref="MathF.FusedMultiplyAdd(float, float, float)"/> / <see cref="Math.FusedMultiplyAdd(double, double, double)"/>.
    /// For backends with no fused instruction (Wasm), which compile these methods into the kernel in
    /// place of the IR multiply-add.
    /// </summary>
    /// <remarks>
    /// ⚠️ Never call Math(F).FusedMultiplyAdd in here: kernels remap it to the multiply-add this replaces.
    /// No ref/out parameters (value tuples instead) so every browser backend can compile the helpers.
    /// </remarks>
    public static class SoftFma
    {
        /// <summary>
        /// float fused multiply-add through double: the product of two floats is exact in a double, the sum is
        /// rounded to ODD in double (Boldo and Melquiond: 53 >= 24 + 2 bits makes the final rounding correct),
        /// then rounded to nearest float once.
        /// </summary>
        public static float FusedMultiplyAddF32(float a, float b, float c)
        {
            double p = (double)a * (double)b;   // exact: 24 + 24 bits
            double dc = c;
            double s = p + dc;                  // rounded to nearest in double
            // TwoSum: err is the exact rounding error of s (NaN when an input is Inf/NaN - then s is final).
            double bb = s - p;
            double err = (p - (s - bb)) + (dc - bb);
            long bits = BitConverter.DoubleToInt64Bits(s);
            // Round to odd: an inexact sum with an even last bit moves one ulp toward the exact value.
            // s is never 0 when err != 0 (a non-zero exact sum of these magnitudes cannot round to 0 in double).
            if (err != 0.0 && !double.IsNaN(err) && (bits & 1L) == 0L)
                bits += (err < 0.0) == (s < 0.0) ? 1L : -1L;
            return (float)BitConverter.Int64BitsToDouble(bits);
        }

        /// <summary>
        /// float fused multiply-add with integer arithmetic only, for backends whose double is not IEEE (WebGL,
        /// WebGPU emulate f64) and whose shader compilers may contract float math: the 48-bit product is exact in
        /// one 64-bit word, the addend is aligned with a sticky bit, and the sum is rounded once to nearest-even.
        /// </summary>
        public static float FusedMultiplyAddF32Integer(float x, float y, float z)
        {
            uint bx = (uint)BitConverter.SingleToInt32Bits(x);
            uint by = (uint)BitConverter.SingleToInt32Bits(y);
            uint bz = (uint)BitConverter.SingleToInt32Bits(z);
            const uint ExpMask32 = 0x7F800000u;

            // Specials, as in FusedMultiplyAddF64: at most one rounding, so the plain expressions are exact.
            if (x == 0f || y == 0f || (bx & ExpMask32) == ExpMask32 || (by & ExpMask32) == ExpMask32)
                return x * y + z;
            if ((bz & ExpMask32) == ExpMask32)
                return z;
            if (z == 0f)
                return x * y;

            // value = mantissa * 2^(exp - 150), mantissa with its implicit bit (subnormals normalized).
            var (xs, xe, xm) = Split32(bx);
            var (ys, ye, ym) = Split32(by);
            var (zs, ze, zm) = Split32(bz);

            // Product: exact (24 x 24 = 48 bits). Both operands normalized to top bit 62: value = m * 2^E.
            ulong pm = xm * ym;
            int pe = xe + ye - 300;
            int shift = Lzc(pm) - 1;
            pm <<= shift;
            pe -= shift;
            ulong qm = zm;
            int qe = ze - 150;
            shift = Lzc(qm) - 1;
            qm <<= shift;
            qe -= shift;
            uint psign = xs ^ ys, qsign = zs;

            // |p| >= |q|
            if (pe < qe || (pe == qe && pm < qm))
            {
                (pm, qm) = (qm, pm);
                (pe, qe) = (qe, pe);
                (psign, qsign) = (qsign, psign);
            }
            // Exact cancellation: +0.
            if (psign != qsign && pe == qe && pm == qm)
                return 0f;

            // Align q with a sticky bit.
            int d = pe - qe;
            ulong qa = d == 0 ? qm : ShiftRight(qm, d) | NonZero(qm & (ShiftLeft(1UL, d) - 1UL));

            ulong m;
            int e = pe;
            if (psign == qsign)
            {
                m = pm + qa;                       // < 2^64: both < 2^63
                if ((m >> 63) != 0UL)
                {
                    m = (m >> 1) | (m & 1UL);
                    e += 1;
                }
            }
            else
            {
                m = pm - qa;
                shift = Lzc(m) - 1;                // more than one bit of cancellation only when d <= 1 (exact)
                m <<= shift;
                e -= shift;
            }

            // m has its top bit at 62: value = m * 2^e. Round to 24 bits (39 bits below them).
            int eb = e + 189;                      // the float's biased exponent
            if (eb <= 0)
            {
                // Subnormal (or underflow to zero): shift right with sticky, exponent field 0.
                int s = 1 - eb;
                m = ShiftRight(m, s) | NonZero(m & (ShiftLeft(1UL, s) - 1UL));
                eb = 1;
            }
            ulong q = m >> 39;
            ulong rem = m & ((1UL << 39) - 1UL);
            const ulong Half = 1UL << 38;
            if (rem > Half || (rem == Half && (q & 1UL) != 0UL))
                q += 1UL;
            // (eb - 1) << 23 plus q: q's implicit bit (or a rounding carry) lifts the exponent field.
            ulong magnitude = ((ulong)(uint)(eb - 1) << 23) + q;
            if (magnitude >= ExpMask32)
                magnitude = ExpMask32;              // overflow: Inf
            return BitConverter.Int32BitsToSingle((int)((psign << 31) | (uint)magnitude));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (uint Sign, int Exp, ulong Mantissa) Split32(uint b)
        {
            uint sign = b >> 31;
            int exp = (int)(b >> 23) & 0xFF;
            ulong mantissa = b & 0x7FFFFFu;
            if (exp == 0)
            {
                int shift = Lzc(mantissa) - 40;    // put the top bit at 23
                mantissa <<= shift;
                exp = 1 - shift;
            }
            else
            {
                mantissa |= 1UL << 23;
            }
            return (sign, exp, mantissa);
        }

        const int Bias = 1023;
        const ulong FracMask = (1UL << 52) - 1;
        const ulong ExpMask = 0x7FF0000000000000UL;

        /// <summary>
        /// double fused multiply-add with 64-bit integer arithmetic: the 106-bit product as two words, aligned
        /// with the addend, added with a sticky bit, rounded once to nearest-even. A port of Go's math.FMA
        /// (BSD licence), which is exact for every input, subnormals and overflow included.
        /// </summary>
        public static double FusedMultiplyAddF64(double x, double y, double z)
        {
            ulong bx = (ulong)BitConverter.DoubleToInt64Bits(x);
            ulong by = (ulong)BitConverter.DoubleToInt64Bits(y);
            ulong bz = (ulong)BitConverter.DoubleToInt64Bits(z);

            // Inf, NaN or a zero factor: at most one rounding happens, so the plain expression is exact.
            if (x == 0.0 || y == 0.0 || (bx & ExpMask) == ExpMask || (by & ExpMask) == ExpMask)
                return x * y + z;
            // Finite x * y plus an infinite (or NaN) z is z.
            if ((bz & ExpMask) == ExpMask)
                return z;
            // z == 0: the result is the product rounded once. NOT x * y + z: a product that underflows to -0
            // plus +0 gives +0, while the exact (tiny negative) sum rounds to -0.
            if (z == 0.0)
                return x * y;

            var (xs, xe, xm) = Split(bx);
            var (ys, ye, ym) = Split(by);
            var (zs, ze, zm) = Split(bz);

            // The product: sign, exponent, two-word mantissa (shifted left 21 so the top bit is bit 62 or 63).
            int pe = xe + ye - Bias + 1;
            var (pm1, pm2) = Mul64(xm << 10, ym << 11);
            ulong zm1 = zm << 10, zm2 = 0;
            uint ps = xs ^ ys;

            // Normalize to bit 62.
            int is62zero = (int)((~pm1 >> 62) & 1UL);
            (pm1, pm2) = Shl(pm1, pm2, is62zero);
            pe -= is62zero;

            // Swap so |p| >= |z|.
            if (pe < ze || (pe == ze && pm1 < zm1))
            {
                (ps, zs) = (zs, ps);
                (pe, ze) = (ze, pe);
                (pm1, zm1) = (zm1, pm1);
                (pm2, zm2) = (zm2, pm2);
            }

            // p == -z exactly: +0 (neither operand is zero).
            if (ps != zs && pe == ze && pm1 == zm1 && pm2 == zm2)
                return 0.0;

            // Align the addend, folding the bits shifted out into a sticky bit.
            (zm1, zm2) = ShrCompress(zm1, zm2, pe - ze);

            ulong m;
            if (ps == zs)
            {
                ulong lo = pm2 + zm2;
                ulong carry = lo < pm2 ? 1UL : 0UL;
                pm2 = lo;
                pm1 = pm1 + zm1 + carry;
                pe -= (int)(~pm1 >> 63);
                (pm1, m) = ShrCompress(pm1, pm2, 64 + (int)(pm1 >> 63));
            }
            else
            {
                ulong lo = pm2 - zm2;
                ulong borrow = pm2 < zm2 ? 1UL : 0UL;
                pm2 = lo;
                pm1 = pm1 - zm1 - borrow;
                int nz = Lz(pm1, pm2);
                pe -= nz;
                (m, pm2) = Shl(pm1, pm2, nz - 1);
                m |= NonZero(pm2);
            }

            // Round, ties to even.
            if (pe > 1022 + Bias || (pe == 1022 + Bias && ((m + (1UL << 9)) >> 63) == 1UL))
                return BitConverter.Int64BitsToDouble((long)(((ulong)ps << 63) | ExpMask));   // overflow: +-Inf
            if (pe < 0)
            {
                int n = -pe;
                m = ShiftRight(m, n) | NonZero(m & (ShiftLeft(1UL, n) - 1UL));
                pe = 0;
            }
            m = ((m + (1UL << 9)) >> 10) & ~Zero((m & ((1UL << 10) - 1UL)) ^ (1UL << 9));
            pe &= -(int)NonZero(m);
            return BitConverter.Int64BitsToDouble((long)(((ulong)ps << 63) + ((ulong)pe << 52) + m));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (uint Sign, int Exp, ulong Mantissa) Split(ulong b)
        {
            uint sign = (uint)(b >> 63);
            int exp = (int)(b >> 52) & 0x7FF;
            ulong mantissa = b & FracMask;
            if (exp == 0)
            {
                // Subnormal: normalize.
                int shift = Lzc(mantissa) - 11;
                mantissa <<= shift;
                exp = 1 - shift;
            }
            else
            {
                mantissa |= 1UL << 52;
            }
            return (sign, exp, mantissa);
        }

        // ⚠️ C# masks a shift count to 6 bits (x << 64 == x); Go's x << 64 is 0. These keep Go's meaning,
        // and a negative count (which Go's uint arithmetic turns into a huge one) also gives 0.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong ShiftLeft(ulong x, int n) => (uint)n >= 64u ? 0UL : x << n;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong ShiftRight(ulong x, int n) => (uint)n >= 64u ? 0UL : x >> n;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong Zero(ulong x) => x == 0UL ? 1UL : 0UL;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong NonZero(ulong x) => x != 0UL ? 1UL : 0UL;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (ulong, ulong) Shl(ulong u1, ulong u2, int n) =>
            (ShiftLeft(u1, n) | ShiftRight(u2, 64 - n) | ShiftLeft(u2, n - 64), ShiftLeft(u2, n));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (ulong, ulong) Shr(ulong u1, ulong u2, int n) =>
            (ShiftRight(u1, n), ShiftRight(u2, n) | ShiftLeft(u1, 64 - n) | ShiftRight(u1, n - 64));

        /// <summary>The two-word value shifted right by n, its lowest bit ORed with every bit shifted out.</summary>
        static (ulong, ulong) ShrCompress(ulong u1, ulong u2, int n)
        {
            if (n == 0)
                return (u1, u2);
            if (n == 64)
                return (0UL, u1 | NonZero(u2));
            if (n >= 128)
                return (0UL, NonZero(u1 | u2));
            ulong sticky = n < 64
                ? u2 & (ShiftLeft(1UL, n) - 1UL)
                : (u1 & (ShiftLeft(1UL, n - 64) - 1UL)) | u2;
            var (r1, r2) = Shr(u1, u2, n);
            return (r1, r2 | NonZero(sticky));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Lz(ulong u1, ulong u2)
        {
            int l = Lzc(u1);
            if (l == 64)
                l += Lzc(u2);
            return l;
        }

        /// <summary>Leading zero count (64 for 0), without BitOperations so every backend compiles it.</summary>
        static int Lzc(ulong x)
        {
            if (x == 0UL)
                return 64;
            int n = 0;
            if ((x >> 32) == 0UL) { n += 32; x <<= 32; }
            if ((x >> 48) == 0UL) { n += 16; x <<= 16; }
            if ((x >> 56) == 0UL) { n += 8; x <<= 8; }
            if ((x >> 60) == 0UL) { n += 4; x <<= 4; }
            if ((x >> 62) == 0UL) { n += 2; x <<= 2; }
            if ((x >> 63) == 0UL) { n += 1; }
            return n;
        }

        /// <summary>64 x 64 -> 128-bit unsigned product (high, low), from 32-bit halves.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (ulong Hi, ulong Lo) Mul64(ulong a, ulong b)
        {
            ulong a0 = a & 0xFFFFFFFFUL, a1 = a >> 32;
            ulong b0 = b & 0xFFFFFFFFUL, b1 = b >> 32;
            ulong w0 = a0 * b0;
            ulong t = a1 * b0 + (w0 >> 32);
            ulong w1 = t & 0xFFFFFFFFUL;
            ulong w2 = t >> 32;
            w1 += a0 * b1;
            ulong hi = a1 * b1 + w2 + (w1 >> 32);
            return (hi, a * b);
        }
    }
}
