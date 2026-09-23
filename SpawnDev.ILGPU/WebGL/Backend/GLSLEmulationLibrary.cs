// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGL
//                        Copyright (c) 2024 SpawnDev Project
//
// File: GLSLEmulationLibrary.cs
//
// Provides GLSL ES 3.0 helper functions for emu_f64 and emu_i64 emulation.
// emu_f64: Double-float technique using vec2 (high + low)
// emu_i64: Double-word technique using uvec2 (low + high)
//
// Ported from WGSLEmulationLibrary.cs — same algorithms, GLSL syntax.
// ---------------------------------------------------------------------------------------

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// Provides GLSL ES 3.0 code strings for 64-bit type emulation functions.
    /// These functions are prepended to the vertex shader when emulation is used.
    /// Ported from <see cref="WebGPU.Backend.WGSLEmulationLibrary"/>.
    /// </summary>
    public static class GLSLEmulationLibrary
    {
        #region emu_f64 Emulation (Double-Float using vec2)

        /// <summary>
        /// GLSL helper functions for emu_f64 emulation.
        /// Uses the double-float technique where emu_f64 = vec2(high, low).
        /// </summary>
        public const string F64Functions = @"
// ============================================================================
// emu_f64 Emulation Functions (Double-Float: vec2 where x=high, y=low)
// ============================================================================
// ANTI-OPTIMIZATION: Uses the luma.gl/deck.gl 'ONE' technique to prevent
// GLSL compilers (especially ANGLE/D3D11) from optimizing away the error
// terms in double-float arithmetic. The uniform u_one is set to 1.0 at
// runtime but is unknown to the compiler at compile time, so the compiler
// cannot simplify expressions like (s * u_one - a) into (s - a).
// Without this, ANGLE collapses the two-sum error computation, reducing
// ~48-bit precision back to ~24-bit (regular float).
// ============================================================================

// Runtime constant: always 1.0, but opaque to the compiler
uniform highp float u_one;

// ---- exact f64 <-> bits core (same text in the Dekker and Ozaki families) ----
uvec2 _f64u_add(uvec2 a, uvec2 b) {
    uint lo = a.x + b.x;
    return uvec2(lo, a.y + b.y + (lo < a.x ? 1u : 0u));
}

uvec2 _f64u_sub(uvec2 a, uvec2 b) {
    return uvec2(a.x - b.x, a.y - b.y - (a.x < b.x ? 1u : 0u));
}

uvec2 _f64u_shl(uvec2 a, uint n) {
    uint s = n & 31u;
    uint carry = a.x >> ((32u - s) & 31u);
    uvec2 small = uvec2(a.x << s, (a.y << s) | (s == 0u ? 0u : carry));
    uvec2 big = uvec2(0u, a.x << s);
    uvec2 r = n >= 32u ? big : small;
    return n >= 64u ? uvec2(0u, 0u) : r;
}

uvec2 _f64u_shr(uvec2 a, uint n) {
    uint s = n & 31u;
    uint carry = a.y << ((32u - s) & 31u);
    uvec2 small = uvec2((a.x >> s) | (s == 0u ? 0u : carry), a.y >> s);
    uvec2 big = uvec2(a.y >> s, 0u);
    uvec2 r = n >= 32u ? big : small;
    return n >= 64u ? uvec2(0u, 0u) : r;
}

// Highest set bit of a NON-ZERO uint. GLSL ES 3.00 has no findMSB: a value below 2^24 converts
// to float exactly, so its exponent IS the bit index (larger values are shifted down first).
uint _f64_msb32(uint x) {
    uint high = 8u + ((floatBitsToUint(float(x >> 8u)) >> 23u) - 127u);
    uint low = (floatBitsToUint(float(x)) >> 23u) - 127u;
    return x >= 0x1000000u ? high : low;
}

uint _f64u_msb(uvec2 a) {
    uint high = 32u + _f64_msb32(a.y);
    uint low = _f64_msb32(a.x);
    return a.y != 0u ? high : low;
}

// a >> n (n >= 1) rounded to nearest-even; sticky = true value strictly above a.
uvec2 _f64u_shr_rne(uvec2 a, uint n, bool sticky) {
    uvec2 q = _f64u_shr(a, n);
    uvec2 h = _f64u_shr(a, n - 1u);
    uvec2 below = _f64u_shl(h, n - 1u);
    bool rest = sticky || below.x != a.x || below.y != a.y;
    bool up = (h.x & 1u) == 1u && (rest || (q.x & 1u) == 1u);
    uvec2 q1 = _f64u_add(q, uvec2(1u, 0u));
    return up ? q1 : q;
}

// One lower f32 component into the magnitude accumulator (x, y) = |hi| with the hi LSB at bit
// 39; z = sticky. Same-sign components add, opposite-sign subtract; bits below the LSB are
// floored + sticky.
uvec3 _f64_acc_component(uvec3 st, float c, uint hi_sign, uint ehn) {
    uint b = floatBitsToUint(c);
    uint e = (b >> 23u) & 0xFFu;
    uint en = max(e, 1u);
    uint m = (e != 0u) ? ((b & 0x7FFFFFu) | 0x800000u) : (b & 0x7FFFFFu);
    int pos = 39 - (int(ehn) - int(en));
    bool left = pos >= 0;
    bool mid = !left && -pos < 24;
    uint rs = uint(clamp(-pos, 0, 31));
    uvec2 shifted = _f64u_shl(uvec2(m, 0u), uint(max(pos, 0)));
    uvec2 right = mid ? uvec2(m >> rs, 0u) : uvec2(0u, 0u);
    uvec2 part = left ? shifted : right;
    bool lost = (m & ((1u << rs) - 1u)) != 0u;
    bool dropped = !left && (!mid || lost);
    uvec2 acc = st.xy;
    uvec2 added = _f64u_add(acc, part);
    uvec2 subbed = _f64u_sub(_f64u_sub(acc, part), uvec2(dropped ? 1u : 0u, 0u));
    uvec2 next = ((b & 0x80000000u) == hi_sign) ? added : subbed;
    bool skip = (b & 0x7FFFFFFFu) == 0u || (st.z != 0u && dropped && part.x == 0u && part.y == 0u);
    uvec3 r = uvec3(next, dropped ? 1u : st.z);
    return skip ? st : r;
}

// Exact sum of up to four f32 components (hi first) as IEEE-754 double bits (lo, hi), RNE.
// BRANCH-FREE: inlined into every emulated-double store, and ANGLE's D3D backend compiles with
// FXC, whose compile time grows superlinearly with branches (see the WGSL twin).
uvec2 _f64_components_to_bits(vec4 c) {
    uint hb = floatBitsToUint(c.x);
    uint sign = hb & 0x80000000u;
    uint eh = (hb >> 23u) & 0xFFu;
    uint ehn = max(eh, 1u);
    uint mh = (eh != 0u) ? ((hb & 0x7FFFFFu) | 0x800000u) : (hb & 0x7FFFFFu);
    uvec3 st = uvec3(0u, mh << 7u, 0u);
    st = _f64_acc_component(st, c.y, sign, ehn);
    st = _f64_acc_component(st, c.z, sign, ehn);
    st = _f64_acc_component(st, c.w, sign, ehn);
    uvec2 acc = st.xy;
    uint p = _f64u_msb(acc);
    bool big = p > 52u;
    uvec2 mr = _f64u_shr_rne(acc, p - 52u, st.z != 0u);
    bool carry = (mr.y >> 21u) != 0u;
    uvec2 mr1 = _f64u_shr(mr, 1u);
    uvec2 mbig = carry ? mr1 : mr;
    uvec2 msmall = _f64u_shl(acc, 52u - p);
    uvec2 m = big ? mbig : msmall;
    uint pf = p + ((big && carry) ? 1u : 0u);
    uint biased = pf + ehn + 834u;
    uvec2 normal = uvec2(m.x, sign | (biased << 20u) | (m.y & 0xFFFFFu));
    bool zero = (acc.x | acc.y) == 0u || (hb & 0x7FFFFFFFu) == 0u;
    uvec2 finite = zero ? uvec2(0u, sign) : normal;
    uvec2 special = uvec2(0u, sign | (((hb & 0x7FFFFFu) != 0u) ? 0x7FF80000u : 0x7FF00000u));
    return eh == 0xFFu ? special : finite;
}

// x * 2^k for -178 <= k <= 127 (GLSL ES 3.00 has no ldexp).
float _f64_scale(float x, int k) {
    float lowk = x * uintBitsToFloat(uint(clamp(k + 253, 1, 254)) << 23u) * uintBitsToFloat(1u << 23u);
    float normk = x * uintBitsToFloat(uint(clamp(k + 127, 1, 254)) << 23u);
    return k < -126 ? lowk : normk;
}

// Exact split of IEEE double bits into non-overlapping f32 components (24 + 24 + 5 bits).
vec4 _f64_bits_to_components(uint lo, uint hi) {
    uint sign = hi & 0x80000000u;
    uint exponent = (hi >> 20u) & 0x7FFu;
    uint m_hi20 = hi & 0xFFFFFu;
    int e = int(exponent) - 1023;
    int f32_exp = e + 127;
    uint fe = uint(clamp(f32_exp, 1, 254));
    uint c0 = 0x800000u | (m_hi20 << 3u) | (lo >> 29u);
    uint c1 = (lo >> 5u) & 0xFFFFFFu;
    uint c2 = lo & 0x1Fu;
    float s = (sign != 0u) ? -1.0 : 1.0;
    float f0 = uintBitsToFloat(sign | (fe << 23u) | (c0 & 0x7FFFFFu));
    float f1 = s * _f64_scale(float(c1), e - 47);
    float f2 = s * _f64_scale(float(c2), e - 52);
    vec4 normal = vec4(f0, f1, f2, 0.0);
    vec4 approx = vec4(uintBitsToFloat(sign | (fe << 23u) | (m_hi20 << 3u)), 0.0, 0.0, 0.0);
    vec4 special = vec4(uintBitsToFloat(sign | (((m_hi20 | lo) != 0u) ? 0x7FC00000u : 0x7F800000u)), 0.0, 0.0, 0.0);
    vec4 zero = vec4(uintBitsToFloat(sign), 0.0, 0.0, 0.0);
    vec4 r = (f32_exp <= 0 || f32_exp >= 255) ? approx : normal;
    r = exponent == 0x7FFu ? special : r;
    return (exponent == 0u && m_hi20 == 0u && lo == 0u) ? zero : r;
}

// Unsigned 64-bit integer -> double bits, rounded to nearest-even at 53 bits.
uvec2 _f64_u64_to_bits(uvec2 v) {
    uint p = _f64u_msb(v);
    bool big = p > 52u;
    uvec2 mr = _f64u_shr_rne(v, p - 52u, false);
    bool carry = (mr.y >> 21u) != 0u;
    uvec2 mr1 = _f64u_shr(mr, 1u);
    uvec2 mbig = carry ? mr1 : mr;
    uvec2 msmall = _f64u_shl(v, 52u - p);
    uvec2 m = big ? mbig : msmall;
    uint pf = p + ((big && carry) ? 1u : 0u);
    uvec2 r = uvec2(m.x, ((pf + 1023u) << 20u) | (m.y & 0xFFFFFu));
    return (v.x == 0u && v.y == 0u) ? uvec2(0u, 0u) : r;
}

bool _f64_bits_is_nan(uvec2 b) {
    return ((b.y >> 20u) & 0x7FFu) == 0x7FFu && ((b.y & 0xFFFFFu) | b.x) != 0u;
}

// |value| truncated toward zero; z = 1 when |value| >= 2^64.
uvec3 _f64_bits_trunc_mag(uvec2 b) {
    uint exponent = (b.y >> 20u) & 0x7FFu;
    uint e = exponent - 1023u;
    uvec2 m = uvec2(b.x, (b.y & 0xFFFFFu) | 0x100000u);
    uvec2 up = _f64u_shl(m, e - 52u);
    uvec2 down = _f64u_shr(m, 52u - e);
    uvec3 r = uvec3(e >= 52u ? up : down, 0u);
    uvec3 sat = e >= 64u ? uvec3(0xFFFFFFFFu, 0xFFFFFFFFu, 1u) : r;
    return exponent < 1023u ? uvec3(0u, 0u, 0u) : sat;
}

vec2 f64_from_ieee754_bits(uint lo, uint hi) {
    vec4 c = _f64_bits_to_components(lo, hi);
    float l = c.y + c.z;
    float s = c.x + l;
    vec2 pair = vec2(s, l - (s * u_one - c.x));
    // c.x alone when exact, zero, Inf or NaN (the error term of an Inf would be NaN).
    return (c.y == 0.0 && c.z == 0.0) ? vec2(c.x, 0.0) : pair;
}

// Exact IEEE-754 bits of hi + lo (lo of EITHER sign), rounded to nearest-even: the bits of hi
// as a double, then lo as a whole number of double ULPs added to the magnitude bits. STRAIGHT-
// LINE on purpose: inlined into every store, and ANGLE compiles with D3D FXC, whose compile
// time grows superlinearly with branches (a 19-op kernel with a NoInlining helper hung).
uvec2 f64_to_ieee754_bits(vec2 v) {
    // TwoSum renormalisation (u_one keeps ANGLE from folding the error term away).
    float s = v.x + v.y;
    float bb = (s * u_one - v.x) * u_one;
    float e = (v.x - (s - bb) * u_one) * u_one + (v.y - bb);
    bool s_finite = (floatBitsToUint(s) & 0x7F800000u) != 0x7F800000u;
    float hi = s_finite ? s : v.x;
    float lo = s_finite ? e : 0.0;
    uint hb = floatBitsToUint(hi);
    uint sign = hb & 0x80000000u;
    uint eh = (hb >> 23u) & 0xFFu;
    uint mant = hb & 0x7FFFFFu;
    // hi as a double. A subnormal hi (eh == 0) normalises: value = mant * 2^-149.
    uint p = _f64_msb32(mant);
    uint sub_shift = 52u - p;
    uint sh_up = mant << ((sub_shift - 32u) & 31u);
    uint sh_dn = mant >> ((32u - sub_shift) & 31u);
    uint sub_w_hi = sub_shift >= 32u ? sh_up : sh_dn;
    uint sh_lo = mant << (sub_shift & 31u);
    uint sub_w_lo = sub_shift >= 32u ? 0u : sh_lo;
    bool is_sub = eh == 0u;
    uint w_hi = is_sub ? (((p + 874u) << 20u) | (sub_w_hi & 0xFFFFFu)) : (((eh + 896u) << 20u) | (mant >> 3u));
    uint w_lo = is_sub ? sub_w_lo : ((mant & 7u) << 29u);
    // lo as a whole number of double ULPs of hi (a power-of-two scale, so exact in f32), added to
    // or subtracted from the magnitude bits: consecutive doubles have consecutive bit patterns.
    // Below an exact power of two the ULP halves, so a shrinking lo counts half-size ULPs.
    bool down = lo != 0.0 && ((floatBitsToUint(lo) ^ hb) & 0x80000000u) != 0u;
    int sc = ((down && mant == 0u) ? 180 : 179) - int(eh);
    int sc1 = sc / 2;
    float lm = abs(lo) * uintBitsToFloat(uint(sc1 + 127) << 23u) * uintBitsToFloat(uint(sc - sc1 + 127) << 23u);
    float li = floor(lm);
    float frac = lm - li;
    uint liu = uint(li);
    // Round to nearest, ties to even (w_lo +/- liu has the parity of w_lo + liu).
    bool inc = frac > 0.5 || (frac == 0.5 && ((w_lo + liu) & 1u) == 1u);
    uint k = liu + (inc ? 1u : 0u);
    uint up_lo = w_lo + k;
    uvec2 up = uvec2(up_lo, w_hi + (up_lo < w_lo ? 1u : 0u));
    uvec2 dn = uvec2(w_lo - k, w_hi - (w_lo < k ? 1u : 0u));
    uvec2 mag = down ? dn : up;
    uint special = (mant != 0u) ? 0x7FF80000u : 0x7FF00000u;
    // A zero keeps v.x's sign when v.x is itself the zero (-0.0 loads as (-0, +0), and TwoSum
    // would make that +0); a zero from x + (-x) is +0.
    uint zero_sign = (v.x == 0.0) ? (floatBitsToUint(v.x) & 0x80000000u) : sign;
    uvec2 finite = ((hb & 0x7FFFFFFFu) == 0u) ? uvec2(0u, zero_sign) : uvec2(mag.x, sign | mag.y);
    return eh == 0xFFu ? uvec2(0u, sign | special) : finite;
}


vec2 f64_from_f32(float v) { return vec2(v, 0.0); }
float f64_to_f32(vec2 v) { return v.x + v.y; }
vec2 f64_new(float hi, float lo) { return vec2(hi, lo); }
vec2 f64_neg(vec2 a) { return vec2(-a.x, -a.y); }

// ============================================================================
// Double-float arithmetic with ANGLE code-elimination workaround.
// u_one = 1.0 at runtime but opaque to compiler, preventing simplification.
// Based on the battle-tested luma.gl/deck.gl fp64 implementation.
// ============================================================================

// Dekker split: a = a_hi + a_lo, a_hi has <= 12 significant bits
vec2 f64_split(float a) {
    float c = 4097.0 * a;
    float a_hi = c * u_one - (c - a);
    float a_lo = a * u_one - a_hi;
    return vec2(a_hi, a_lo);
}

// Two-sum: s = a + b exactly as (sum, error) — Knuth's algorithm
vec2 f64_two_sum(float a, float b) {
    float s = (a + b);
    float v = (s * u_one - a) * u_one;
    float err = (a - (s - v) * u_one) * u_one * u_one * u_one + (b - v);
    return vec2(s, err);
}

// Quick two-sum: assumes |a| >= |b|
vec2 f64_quick_two_sum(float a, float b) {
    float s = (a + b) * u_one;
    float err = b - (s - a) * u_one;
    return vec2(s, err);
}

// Two-product: p = a * b exactly as (product, error)
vec2 f64_two_prod(float a, float b) {
    // (a * b) * u_one: the product is ROUNDED on its own before any add sees it. ANGLE compiles
    // without IEEE strictness, so D3D may fuse a bare a * b into a following add (mad); the
    // two-sum error terms downstream then describe a sum that was never computed (Ozaki
    // sqrt(x * x) came out 1 ULP low). Multiplying the rounded product by 1 cannot fuse wrongly.
    float p = (a * b) * u_one;
    vec2 a_s = f64_split(a);
    vec2 b_s = f64_split(b);
    float err = ((a_s.x * b_s.x - p) * u_one + a_s.x * b_s.y * u_one * u_one
        + a_s.y * b_s.x) + a_s.y * b_s.y * u_one * u_one * u_one;
    return vec2(p, err);
}

vec2 f64_add(vec2 a, vec2 b) {
    // A non-finite sum returns the IEEE result (the error terms would compute Inf - Inf = NaN).
    // A select, not an early return: FXC compiles branches superlinearly.
    float _nf = a.x + b.x;
    vec2 s = f64_two_sum(a.x, b.x);
    vec2 t = f64_two_sum(a.y, b.y);
    s.y += t.x;
    s = f64_quick_two_sum(s.x, s.y);
    s.y += t.y;
    s = f64_quick_two_sum(s.x, s.y);
    return ((floatBitsToUint(_nf) & 0x7F800000u) == 0x7F800000u) ? vec2(_nf, 0.0) : s;
}

// emu_f64 subtraction
vec2 f64_sub(vec2 a, vec2 b) {
    return f64_add(a, f64_neg(b));
}

vec2 f64_mul(vec2 a, vec2 b) {
    // Non-finite product: the IEEE result (see f64_add).
    float _nf = a.x * b.x;
    vec2 p = f64_two_prod(a.x, b.x);
    p.y += a.x * b.y;
    p = f64_quick_two_sum(p.x, p.y);
    p.y += a.y * b.x;
    p = f64_quick_two_sum(p.x, p.y);
    return ((floatBitsToUint(_nf) & 0x7F800000u) == 0x7F800000u) ? vec2(_nf, 0.0) : p;
}

vec2 f64_div(vec2 a, vec2 b) {
    // Non-finite quotient or divisor: the IEEE result (see f64_add).
    float _nf = a.x / b.x;
    float xn = 1.0 / b.x;
    vec2 yn = a * xn;
    float diff = (f64_sub(a, f64_mul(b, yn))).x;
    vec2 prod = f64_two_prod(xn, diff);
    vec2 q = f64_add(yn, prod);
    bool non_finite = (floatBitsToUint(_nf) & 0x7F800000u) == 0x7F800000u || (floatBitsToUint(b.x) & 0x7F800000u) == 0x7F800000u;
    return non_finite ? vec2(_nf, 0.0) : q;
}

// IEEE-strict NaN detection by f32 bit pattern. GLSL ES 3.0 `<`, `>`, `==`
// on float operands have been observed to return TRUE in the presence of
// NaN on some implementations (likely unordered-compare semantics). The
// comparison helpers below explicitly exclude NaN via this bit-pattern
// check before the `<` / `>` / `==` to guarantee IEEE-ordered behaviour.
bool _f32_is_nan_bits(float v) {
    uint bits = uint(floatBitsToInt(v));
    return ((bits & 0x7F800000u) == 0x7F800000u) && ((bits & 0x007FFFFFu) != 0u);
}

bool f64_lt(vec2 a, vec2 b) {
    bool na = _f32_is_nan_bits(a.x);
    bool nb = _f32_is_nan_bits(b.x);
    return !(na || nb) && ((a.x < b.x) || (a.x == b.x && a.y < b.y));
}

bool f64_le(vec2 a, vec2 b) {
    bool na = _f32_is_nan_bits(a.x);
    bool nb = _f32_is_nan_bits(b.x);
    return !(na || nb) && ((a.x < b.x) || (a.x == b.x && a.y <= b.y));
}

bool f64_gt(vec2 a, vec2 b) {
    bool na = _f32_is_nan_bits(a.x);
    bool nb = _f32_is_nan_bits(b.x);
    return !(na || nb) && ((a.x > b.x) || (a.x == b.x && a.y > b.y));
}

bool f64_ge(vec2 a, vec2 b) {
    bool na = _f32_is_nan_bits(a.x);
    bool nb = _f32_is_nan_bits(b.x);
    return !(na || nb) && ((a.x > b.x) || (a.x == b.x && a.y >= b.y));
}

bool f64_eq(vec2 a, vec2 b) {
    bool na = _f32_is_nan_bits(a.x);
    bool nb = _f32_is_nan_bits(b.x);
    return !(na || nb) && (a.x == b.x && a.y == b.y);
}

bool f64_ne(vec2 a, vec2 b) {
    bool na = _f32_is_nan_bits(a.x);
    bool nb = _f32_is_nan_bits(b.x);
    return (na || nb) || (a.x != b.x || a.y != b.y);
}

// emu_f64 IEEE IsNaN: bit-pattern check (avoid relying on `isnan` which
// some GLSL implementations short-circuit to FALSE for unordered compare).
bool f64_is_nan(vec2 v) {
    return _f32_is_nan_bits(v.x);
}

// emu_f64 IEEE IsInfinity: high lane carries f32 +/-Inf bit pattern
// (exp == 0xFF, mantissa == 0). Bit-pattern check avoids unordered-compare
// platform quirks.
bool f64_is_inf(vec2 v) {
    uint bits = uint(floatBitsToInt(v.x));
    return (bits & 0x7FFFFFFFu) == 0x7F800000u;
}

vec2 f64_abs(vec2 a) {
    vec2 n = f64_neg(a);
    return (a.x < 0.0 || (a.x == 0.0 && a.y < 0.0)) ? n : a;
}

vec2 f64_min(vec2 a, vec2 b) {
    bool lt = f64_lt(a, b);
    return lt ? a : b;
}

vec2 f64_max(vec2 a, vec2 b) {
    bool gt = f64_gt(a, b);
    return gt ? a : b;
}

// ---- exact integer <-> f64 conversions (integer domain; saturating like .NET 9+) ----
vec2 f64_from_u64(uvec2 v) {
    uvec2 b = _f64_u64_to_bits(v);
    return f64_from_ieee754_bits(b.x, b.y);
}

vec2 f64_from_i64(uvec2 v) {
    bool neg = (v.y & 0x80000000u) != 0u;
    uvec2 negv = _f64u_sub(uvec2(0u, 0u), v);
    uvec2 b = _f64_u64_to_bits(neg ? negv : v);
    return f64_from_ieee754_bits(b.x, b.y | (neg ? 0x80000000u : 0u));
}

vec2 f64_from_u32(uint v) { return f64_from_u64(uvec2(v, 0u)); }

vec2 f64_from_i32(int v) { return f64_from_i64(uvec2(uint(v), v < 0 ? 0xFFFFFFFFu : 0u)); }

uvec2 f64_to_i64(vec2 v) {
    uvec2 b = f64_to_ieee754_bits(v);
    uvec3 t = _f64_bits_trunc_mag(b);
    bool neg_ovf = t.z != 0u || t.y > 0x80000000u || (t.y == 0x80000000u && t.x != 0u);
    uvec2 negt = _f64u_sub(uvec2(0u, 0u), t.xy);
    uvec2 neg = neg_ovf ? uvec2(0u, 0x80000000u) : negt;
    uvec2 pos = (t.z != 0u || t.y >= 0x80000000u) ? uvec2(0xFFFFFFFFu, 0x7FFFFFFFu) : t.xy;
    uvec2 r = ((b.y & 0x80000000u) != 0u) ? neg : pos;
    bool nan = _f64_bits_is_nan(b);
    return nan ? uvec2(0u, 0u) : r;
}

uvec2 f64_to_u64(vec2 v) {
    uvec2 b = f64_to_ieee754_bits(v);
    uvec3 t = _f64_bits_trunc_mag(b);
    bool nan = _f64_bits_is_nan(b);
    return (nan || (b.y & 0x80000000u) != 0u) ? uvec2(0u, 0u) : t.xy;
}

int f64_to_i32(vec2 v) {
    uvec2 r = f64_to_i64(v);
    int hi = int(r.y);
    int r32 = (hi > 0 || (hi == 0 && r.x > 0x7FFFFFFFu)) ? 2147483647 : int(r.x);
    return (hi < -1 || (hi == -1 && r.x < 0x80000000u)) ? int(0x80000000u) : r32;
}

uint f64_to_u32(vec2 v) {
    uvec2 r = f64_to_u64(v);
    return r.y != 0u ? 0xFFFFFFFFu : r.x;
}


// ---- exact Floor / Ceiling / Round on the IEEE bit pattern ----
uvec2 _f64_bits_round_int(uvec2 b, bool ceil_mode) {
    uint exponent = (b.y >> 20u) & 0x7FFu;
    bool neg = (b.y & 0x80000000u) != 0u;
    uvec2 small_neg = ceil_mode ? uvec2(0u, 0x80000000u) : uvec2(0u, 0xBFF00000u);
    uvec2 small_pos = ceil_mode ? uvec2(0u, 0x3FF00000u) : uvec2(0u, 0u);
    uvec2 small = neg ? small_neg : small_pos;
    uvec2 small_r = ((b.x | (b.y & 0x7FFFFFFFu)) == 0u) ? b : small;
    uint fracbits = 1075u - exponent;
    uvec2 kept = _f64u_shl(_f64u_shr(b, fracbits), fracbits);
    uvec2 plus = _f64u_add(kept, _f64u_shl(uvec2(1u, 0u), fracbits));
    uvec2 bumped = (neg != ceil_mode) ? plus : kept;
    uvec2 mid = (kept.x == b.x && kept.y == b.y) ? b : bumped;
    uvec2 r = exponent < 1023u ? small_r : mid;
    return (exponent == 0x7FFu || exponent >= 1075u) ? b : r;
}

// Integer rounding of a normalized pair (|lo| <= ulp(hi) / 2) in f32 arithmetic: a NON-integral
// hi (|hi| < 2^23) is at least one ULP from every integer, so lo cannot carry the value across
// one - the result is hi's own; an integral hi carries the whole fraction in lo. Straight-line
// (see f64_to_ieee754_bits); the bit-pattern versions were the largest inlined functions.
bool _f64_is_finite_hi(vec2 v) {
    return (floatBitsToUint(v.x) & 0x7F800000u) != 0x7F800000u;
}

// hi + k for an integral hi and an integral k (k == 0 keeps hi's zero sign).
vec2 _f64_int_plus(float hi, float k) {
    vec2 sum = f64_add(vec2(hi, 0.0), vec2(k, 0.0));
    return k != 0.0 ? sum : vec2(hi, 0.0);
}

// Nearest integer; a tie goes to EVEN or AWAY from zero (roundEven, not round(): GLSL round()
// leaves .5 to the implementation).
vec2 _f64_round_nearest(vec2 v, bool away) {
    float hi = v.x;
    float lo = v.y;
    // hi integral: round lo (ties to even on lo alone; d = lo - rl is exact). On a tie the
    // other candidate is rl + 2d, chosen when hi + rl is odd (even) or nearer zero (away).
    float rl = roundEven(lo);
    float dl = lo - rl;
    bool hi_odd = hi - 2.0 * floor(hi * 0.5) != 0.0;
    bool rl_odd = rl - 2.0 * floor(rl * 0.5) != 0.0;
    float other_l = rl + 2.0 * dl;
    bool total_neg = hi < 0.0 || (hi == 0.0 && lo < 0.0);
    bool away_l = total_neg ? (other_l < rl) : (other_l > rl);
    bool pick_l = abs(dl) == 0.5 && (away ? away_l : (hi_odd != rl_odd));
    vec2 int_hi = _f64_int_plus(hi, pick_l ? other_l : rl);
    // hi not integral: a .5 tie in hi is decided by lo's sign, then by the tie rule.
    float rh = roundEven(hi);
    float dh = hi - rh;
    float other_h = rh + 2.0 * dh;
    bool away_h = abs(other_h) > abs(rh);
    bool pick_h = abs(dh) == 0.5 && ((dh > 0.0 && lo > 0.0) || (dh < 0.0 && lo < 0.0) || (lo == 0.0 && away && away_h));
    vec2 frac_hi = vec2(pick_h ? other_h : rh, 0.0);
    bool fin = _f64_is_finite_hi(v);
    return (floor(hi) == hi && fin) ? int_hi : frac_hi;
}

vec2 f64_floor(vec2 v) {
    float fh = floor(v.x);
    vec2 with_lo = _f64_int_plus(fh, floor(v.y));
    bool fin = _f64_is_finite_hi(v);
    return (fh == v.x && fin) ? with_lo : vec2(fh, 0.0);
}

vec2 f64_ceil(vec2 v) {
    float ch = ceil(v.x);
    vec2 with_lo = _f64_int_plus(ch, ceil(v.y));
    bool fin = _f64_is_finite_hi(v);
    return (ch == v.x && fin) ? with_lo : vec2(ch, 0.0);
}

// Nearest integer on the IEEE bit pattern; a tie goes to EVEN or AWAY from zero (rounding the
// components separately is wrong: 2.5000000001 = 2.5 + 1e-10 -> 2).
uvec2 _f64_bits_round_nearest(uvec2 b, bool away) {
    uint exponent = (b.y >> 20u) & 0x7FFu;
    uvec2 sign_only = uvec2(0u, b.y & 0x80000000u);
    uvec2 half_range = (!away && b.x == 0u && (b.y & 0xFFFFFu) == 0u) ? sign_only : uvec2(0u, sign_only.y | 0x3FF00000u);
    uint fracbits = 1075u - exponent;
    uvec2 one = _f64u_shl(uvec2(1u, 0u), fracbits);
    uvec2 kept = _f64u_shl(_f64u_shr(b, fracbits), fracbits);
    uvec2 frac = _f64u_sub(b, kept);
    uvec2 half_v = _f64u_shl(uvec2(1u, 0u), fracbits - 1u);
    bool above = frac.y > half_v.y || (frac.y == half_v.y && frac.x > half_v.x);
    bool tie = frac.x == half_v.x && frac.y == half_v.y;
    bool odd = ((kept.x & one.x) | (kept.y & one.y)) != 0u;
    uvec2 plus = _f64u_add(kept, one);
    uvec2 mid = (above || (tie && (away || odd))) ? plus : kept;
    uvec2 r = exponent == 1022u ? half_range : mid;
    r = exponent < 1022u ? sign_only : r;
    return (exponent == 0x7FFu || exponent >= 1075u) ? b : r;
}

vec2 f64_round_even(vec2 v) {
    return _f64_round_nearest(v, false);
}

vec2 f64_round_away(vec2 v) {
    return _f64_round_nearest(v, true);
}

vec2 f64_ieee_rem(vec2 a, vec2 b) {
    return f64_sub(a, f64_mul(b, f64_round_even(f64_div(a, b))));
}

vec2 f64_sqrt(vec2 a) {
    float hi = a.x;
    uint hb = floatBitsToUint(hi);
    bool ok = hi > 0.0 && (hb & 0x7FFFFFFFu) != 0x7F800000u;
    vec2 x = ok ? a : vec2(1.0, 0.0);
    vec2 half_v = f64_from_f32(0.5);
    vec2 y = f64_from_f32(sqrt(x.x));
    y = f64_mul(f64_add(y, f64_div(x, y)), half_v);
    y = f64_mul(f64_add(y, f64_div(x, y)), half_v);
    // Negative -> NaN; +-0, +Inf and NaN are their own roots.
    vec2 nan_v = f64_from_f32(uintBitsToFloat(0xFFC00000u | (hb & 0u)));
    vec2 special = hi < 0.0 ? nan_v : a;
    return ok ? y : special;
}


vec2 f64_trunc(vec2 v) {
    vec2 f = f64_floor(v);
    vec2 c = f64_ceil(v);
    return v.x < 0.0 ? c : f;
}

vec2 f64_copysign(vec2 a, vec2 b) {
    vec2 m = f64_abs(a);
    vec2 n = f64_neg(m);
    return ((floatBitsToUint(b.x) >> 31u) != 0u) ? n : m;
}

vec2 f64_rem(vec2 a, vec2 b) {
    return f64_sub(a, f64_mul(b, f64_trunc(f64_div(a, b))));
}

vec2 f64_pow(vec2 a, vec2 b) {
    float x = f64_to_f32(a);
    float y = f64_to_f32(b);
    return f64_from_f32(pow(abs(x), y) * ((x < 0.0 && mod(abs(y), 2.0) >= 1.0) ? -1.0 : 1.0));
}

vec2 f64_atan2(vec2 a, vec2 b) {
    return f64_from_f32(atan(f64_to_f32(a), f64_to_f32(b)));
}

";

        #endregion

        #region emu_i64 Emulation (Double-Word using uvec2)

        /// <summary>
        /// GLSL helper functions for emu_i64/emu_u64 emulation.
        /// Uses double-word technique where emu_i64 = uvec2(low, high).
        /// </summary>
        public const string I64Functions = @"
// ============================================================================
// emu_i64/emu_u64 Emulation Functions (Double-Word: uvec2 where x=low, y=high)
// ============================================================================

// Create emu_i64 from int (sign-extend)
uvec2 i64_from_i32(int v) {
    uint lo = uint(v);
    uint hi = v < 0 ? 0xFFFFFFFFu : 0u;
    return uvec2(lo, hi);
}

// Create emu_u64 from uint
uvec2 u64_from_u32(uint v) {
    return uvec2(v, 0u);
}

// Convert emu_i64 to int (truncate)
int i64_to_i32(uvec2 v) {
    return int(v.x);
}

// Convert emu_u64 to uint (truncate)
uint u64_to_u32(uvec2 v) {
    return v.x;
}

// Create emu_i64 from low and high
uvec2 i64_new(uint lo, uint hi) {
    return uvec2(lo, hi);
}

// emu_i64/emu_u64 addition with carry. The carry bit is computed with the bitwise
// generate/propagate identity `(a & b) | ((a | b) & ~sum)` (top bit only) instead of the
// textbook `sum < a` unsigned-overflow comparison: on WebGL/ANGLE (observed on Chrome/Windows,
// D3D backend, 2026-09-22) a `uint` `<` comparison feeding a carry into a SECOND uvec2 add a
// few calls later in the same shader was empirically found to drop the carry bit for specific
// operand values - bit-exact in a pure C# port of this exact algorithm, wrong on the actual
// GPU. Root-caused via NoInliningBlakeShapedRawVPerCallBisectTest (Bug #5): a chain of 8
// NoInlining Blake2b-G-shaped calls reusing the same 16 ulong locals was correct, and the 9th
// call (first call of a second round reusing v0/v4/v8/v12) produced a v0 wrong by exactly
// 1 bit at position 32 - the lo/hi carry boundary. Reproduced identically whether G takes
// ref/inout params, returns a struct by value, or uses varying vs. constant x/y - ruled out
// codegen shape, ruled in a driver-level `<` comparison miscompilation. This bitwise formula
// avoids emitting `<` for carry/borrow detection entirely, sidestepping the bug family.
uvec2 i64_add(uvec2 a, uvec2 b) {
    uint lo = a.x + b.x;
    uint carry = ((a.x & b.x) | ((a.x | b.x) & ~lo)) >> 31u;
    uint hi = a.y + b.y + carry;
    return uvec2(lo, hi);
}

// emu_i64/emu_u64 subtraction with borrow. See i64_add's comment: borrow uses the same
// bitwise generate/propagate identity (applied to a - b = a + ~b + 1) instead of a `<`
// comparison, for the same ANGLE-miscompilation defense.
uvec2 i64_sub(uvec2 a, uvec2 b) {
    uint lo = a.x - b.x;
    uint borrow = ((~a.x & b.x) | ((~a.x | b.x) & lo)) >> 31u;
    uint hi = a.y - b.y - borrow;
    return uvec2(lo, hi);
}

// emu_i64 negation (two's complement)
uvec2 i64_neg(uvec2 a) {
    uvec2 inv = uvec2(~a.x, ~a.y);
    return i64_add(inv, uvec2(1u, 0u));
}

// emu_u64 multiplication
uvec2 u64_mul(uvec2 a, uvec2 b) {
    uint a_lo = a.x & 0xFFFFu;
    uint a_hi = a.x >> 16u;
    uint b_lo = b.x & 0xFFFFu;
    uint b_hi = b.x >> 16u;

    uint p0 = a_lo * b_lo;
    uint p1 = a_lo * b_hi;
    uint p2 = a_hi * b_lo;
    uint p3 = a_hi * b_hi;

    uint mid = (p0 >> 16u) + (p1 & 0xFFFFu) + (p2 & 0xFFFFu);
    uint lo = (p0 & 0xFFFFu) | ((mid & 0xFFFFu) << 16u);

    uint hi = p3 + (p1 >> 16u) + (p2 >> 16u) + (mid >> 16u) + a.x * b.y + a.y * b.x;

    return uvec2(lo, hi);
}

// emu_i64 multiplication
uvec2 i64_mul(uvec2 a, uvec2 b) {
    bool neg_a = (a.y & 0x80000000u) != 0u;
    bool neg_b = (b.y & 0x80000000u) != 0u;
    uvec2 abs_a = neg_a ? i64_neg(a) : a;
    uvec2 abs_b = neg_b ? i64_neg(b) : b;
    uvec2 result = u64_mul(abs_a, abs_b);
    if (neg_a != neg_b) { result = i64_neg(result); }
    return result;
}

// Bitwise operations
uvec2 i64_and(uvec2 a, uvec2 b) {
    return uvec2(a.x & b.x, a.y & b.y);
}

uvec2 i64_or(uvec2 a, uvec2 b) {
    return uvec2(a.x | b.x, a.y | b.y);
}

uvec2 i64_xor(uvec2 a, uvec2 b) {
    return uvec2(a.x ^ b.x, a.y ^ b.y);
}

uvec2 i64_not(uvec2 a) {
    return uvec2(~a.x, ~a.y);
}

// Left shift
uvec2 i64_shl(uvec2 a, uint shift) {
    if (shift == 0u) { return a; }
    if (shift >= 64u) { return uvec2(0u, 0u); }
    if (shift >= 32u) {
        return uvec2(0u, a.x << (shift - 32u));
    }
    uint lo = a.x << shift;
    uint hi = (a.y << shift) | (a.x >> (32u - shift));
    return uvec2(lo, hi);
}

// Logical right shift
uvec2 u64_shr(uvec2 a, uint shift) {
    if (shift == 0u) { return a; }
    if (shift >= 64u) { return uvec2(0u, 0u); }
    if (shift >= 32u) {
        return uvec2(a.y >> (shift - 32u), 0u);
    }
    uint lo = (a.x >> shift) | (a.y << (32u - shift));
    uint hi = a.y >> shift;
    return uvec2(lo, hi);
}

// Arithmetic right shift
uvec2 i64_shr(uvec2 a, uint shift) {
    if (shift == 0u) { return a; }
    uint sign = a.y & 0x80000000u;
    if (shift >= 64u) {
        uint fill = sign != 0u ? 0xFFFFFFFFu : 0u;
        return uvec2(fill, fill);
    }
    if (shift >= 32u) {
        int signed_y = int(a.y);
        uint shift_amt = shift - 32u;
        int shifted = signed_y >> int(shift_amt);
        uint lo = uint(shifted);
        uint hi = sign != 0u ? 0xFFFFFFFFu : 0u;
        return uvec2(lo, hi);
    }
    uint lo_r = (a.x >> shift) | (a.y << (32u - shift));
    int signed_y_r = int(a.y);
    int shifted_r = signed_y_r >> int(shift);
    uint hi_r = uint(shifted_r);
    return uvec2(lo_r, hi_r);
}

// Signed comparisons
bool i64_eq(uvec2 a, uvec2 b) {
    return a.x == b.x && a.y == b.y;
}

bool i64_ne(uvec2 a, uvec2 b) {
    return a.x != b.x || a.y != b.y;
}

bool i64_lt(uvec2 a, uvec2 b) {
    bool a_neg = (a.y & 0x80000000u) != 0u;
    bool b_neg = (b.y & 0x80000000u) != 0u;
    if (a_neg && !b_neg) { return true; }
    if (!a_neg && b_neg) { return false; }
    if (a.y != b.y) { return a.y < b.y; }
    return a.x < b.x;
}

bool i64_le(uvec2 a, uvec2 b) {
    return i64_lt(a, b) || i64_eq(a, b);
}

bool i64_gt(uvec2 a, uvec2 b) {
    return i64_lt(b, a);
}

bool i64_ge(uvec2 a, uvec2 b) {
    return !i64_lt(a, b);
}

// Unsigned comparisons
bool u64_lt(uvec2 a, uvec2 b) {
    if (a.y != b.y) { return a.y < b.y; }
    return a.x < b.x;
}

bool u64_le(uvec2 a, uvec2 b) {
    return u64_lt(a, b) || i64_eq(a, b);
}

bool u64_gt(uvec2 a, uvec2 b) {
    return u64_lt(b, a);
}

bool u64_ge(uvec2 a, uvec2 b) {
    return !u64_lt(a, b);
}

// emu_i64 absolute value
uvec2 i64_abs(uvec2 a) {
    if ((a.y & 0x80000000u) != 0u) {
        return i64_neg(a);
    }
    return a;
}

// 64-bit min / max (declared by the helper prototypes in GLSLFunctionGenerator but never
// defined). GLSL min()/max() on a uvec2 compare the two words independently.
uvec2 i64_min(uvec2 a, uvec2 b) { return i64_lt(b, a) ? b : a; }
uvec2 i64_max(uvec2 a, uvec2 b) { return i64_gt(b, a) ? b : a; }
uvec2 u64_min(uvec2 a, uvec2 b) { return u64_lt(b, a) ? b : a; }
uvec2 u64_max(uvec2 a, uvec2 b) { return u64_gt(b, a) ? b : a; }

uint _u64_msb32(uint x) {
    if (x >= 0x1000000u) { return 8u + ((floatBitsToUint(float(x >> 8u)) >> 23u) - 127u); }
    return (floatBitsToUint(float(x)) >> 23u) - 127u;
}

// ulong -> float, rounded to nearest-even over all 64 bits (was: low 32 bits only).
float u64_to_f32(uvec2 v) {
    if (v.x == 0u && v.y == 0u) { return 0.0; }
    uint p = (v.y != 0u) ? 32u + _u64_msb32(v.y) : _u64_msb32(v.x);
    if (p <= 23u) { return float(v.x); }
    uint n = p - 23u;
    uint mant = u64_shr(v, n).x;
    uint half_bit = u64_shr(v, n - 1u).x & 1u;
    uvec2 below = i64_shl(u64_shr(v, n - 1u), n - 1u);
    bool rest = below.x != v.x || below.y != v.y;
    if (half_bit == 1u && (rest || (mant & 1u) == 1u)) {
        mant = mant + 1u;
        if (mant == 0x1000000u) { mant = 0x800000u; p = p + 1u; }
    }
    return uintBitsToFloat(((p + 127u) << 23u) | (mant & 0x7FFFFFu));
}

float i64_to_f32(uvec2 v) {
    if ((v.y & 0x80000000u) != 0u) { return -u64_to_f32(i64_neg(v)); }
    return u64_to_f32(v);
}

// float -> long, truncating, saturating like .NET 9+ (NaN -> 0).
uvec2 f32_to_i64(float f) {
    uint b = floatBitsToUint(f);
    uint exponent = (b >> 23u) & 0xFFu;
    if (exponent == 0xFFu && (b & 0x7FFFFFu) != 0u) { return uvec2(0u, 0u); }
    bool neg = (b & 0x80000000u) != 0u;
    if (exponent < 127u) { return uvec2(0u, 0u); }
    uint e = exponent - 127u;
    if (e >= 63u) { return neg ? uvec2(0u, 0x80000000u) : uvec2(0xFFFFFFFFu, 0x7FFFFFFFu); }
    uvec2 m = uvec2((b & 0x7FFFFFu) | 0x800000u, 0u);
    uvec2 r = (e >= 23u) ? i64_shl(m, e - 23u) : u64_shr(m, 23u - e);
    return neg ? i64_neg(r) : r;
}

// float -> ulong, truncating, saturating (NaN and negatives -> 0).
uvec2 f32_to_u64(float f) {
    uint b = floatBitsToUint(f);
    uint exponent = (b >> 23u) & 0xFFu;
    if ((exponent == 0xFFu && (b & 0x7FFFFFu) != 0u) || (b & 0x80000000u) != 0u || exponent < 127u) { return uvec2(0u, 0u); }
    uint e = exponent - 127u;
    if (e >= 64u) { return uvec2(0xFFFFFFFFu, 0xFFFFFFFFu); }
    uvec2 m = uvec2((b & 0x7FFFFFu) | 0x800000u, 0u);
    return (e >= 23u) ? i64_shl(m, e - 23u) : u64_shr(m, 23u - e);
}

";

        #endregion

        #region emu_f64 Emulation (Ozaki Scheme using vec4)

        /// <summary>
        /// GLSL ES 3.0 helper functions for Ozaki emu_f64 emulation.
        /// Uses quad-double arithmetic (vec4 = 4x float) with u_one anti-optimization.
        /// Ported from WGSLEmulationLibrary.OzakiF64Functions.
        /// </summary>
        public const string OzakiF64Functions = @"
// ============================================================================
// emu_f64 Emulation Functions (Ozaki Scheme: vec4)
// Implementing Quad-Double arithmetic based on Hida, Li, and Bailey's qd library.
// Uses u_one anti-optimization barrier to prevent ANGLE/D3D11 from collapsing
// error terms in double-float arithmetic.
// ============================================================================

// Anti-optimization barrier: u_one is set to 1.0 at runtime but is opaque to the
// compiler, preventing it from simplifying expressions like (s * u_one - a).
uniform highp float u_one;

vec2 f32_two_sum(float a, float b) {
    float s = (a + b);
    float v = (s * u_one - a) * u_one;
    float e = (a - (s - v) * u_one) * u_one * u_one * u_one + (b - v);
    return vec2(s, e);
}

vec2 f32_quick_two_sum(float a, float b) {
    float s = (a + b) * u_one;
    float e = b - (s - a) * u_one;
    return vec2(s, e);
}

vec3 f32_three_sum(float a, float b, float c) {
    vec2 ts1 = f32_two_sum(a, b);
    float t1 = ts1.x; float t2 = ts1.y;

    vec2 ts2 = f32_two_sum(c, t1);
    float out_a = ts2.x; float t3 = ts2.y;

    vec2 ts3 = f32_two_sum(t2, t3);
    float out_b = ts3.x; float out_c = ts3.y;

    return vec3(out_a, out_b, out_c);
}

vec3 f32_three_sum2(float a, float b, float c) {
    vec2 ts1 = f32_two_sum(a, b);
    float t1 = ts1.x; float t2 = ts1.y;

    vec2 ts2 = f32_two_sum(c, t1);
    float out_a = ts2.x; float t3 = ts2.y;

    float out_b = t2 + t3;
    return vec3(out_a, out_b, ts2.y);
}

vec4 f32_quick_renorm(vec4 c_in, float e) {
    float c0 = c_in.x, c1 = c_in.y, c2 = c_in.z, c3 = c_in.w, c4 = e;

    vec2 ts1 = f32_quick_two_sum(c3, c4);
    float s = ts1.x; float t3 = ts1.y;

    vec2 ts2 = f32_quick_two_sum(c2, s);
    s = ts2.x; float t2 = ts2.y;

    vec2 ts3 = f32_quick_two_sum(c1, s);
    s = ts3.x; float t1 = ts3.y;

    vec2 ts4 = f32_quick_two_sum(c0, s);
    c0 = ts4.x; float t0 = ts4.y;

    vec2 ts5 = f32_quick_two_sum(t2, t3);
    s = ts5.x; t2 = ts5.y;

    vec2 ts6 = f32_quick_two_sum(t1, s);
    s = ts6.x; t1 = ts6.y;

    vec2 ts7 = f32_quick_two_sum(t0, s);
    c1 = ts7.x; t0 = ts7.y;

    vec2 ts8 = f32_quick_two_sum(t1, t2);
    s = ts8.x; t1 = ts8.y;

    vec2 ts9 = f32_quick_two_sum(t0, s);
    c2 = ts9.x; t0 = ts9.y;

    c3 = t0 + t1;

    return vec4(c0, c1, c2, c3);
}

// ---- exact f64 <-> bits core (same text in the Dekker and Ozaki families) ----
uvec2 _f64u_add(uvec2 a, uvec2 b) {
    uint lo = a.x + b.x;
    return uvec2(lo, a.y + b.y + (lo < a.x ? 1u : 0u));
}

uvec2 _f64u_sub(uvec2 a, uvec2 b) {
    return uvec2(a.x - b.x, a.y - b.y - (a.x < b.x ? 1u : 0u));
}

uvec2 _f64u_shl(uvec2 a, uint n) {
    uint s = n & 31u;
    uint carry = a.x >> ((32u - s) & 31u);
    uvec2 small = uvec2(a.x << s, (a.y << s) | (s == 0u ? 0u : carry));
    uvec2 big = uvec2(0u, a.x << s);
    uvec2 r = n >= 32u ? big : small;
    return n >= 64u ? uvec2(0u, 0u) : r;
}

uvec2 _f64u_shr(uvec2 a, uint n) {
    uint s = n & 31u;
    uint carry = a.y << ((32u - s) & 31u);
    uvec2 small = uvec2((a.x >> s) | (s == 0u ? 0u : carry), a.y >> s);
    uvec2 big = uvec2(a.y >> s, 0u);
    uvec2 r = n >= 32u ? big : small;
    return n >= 64u ? uvec2(0u, 0u) : r;
}

// Highest set bit of a NON-ZERO uint. GLSL ES 3.00 has no findMSB: a value below 2^24 converts
// to float exactly, so its exponent IS the bit index (larger values are shifted down first).
uint _f64_msb32(uint x) {
    uint high = 8u + ((floatBitsToUint(float(x >> 8u)) >> 23u) - 127u);
    uint low = (floatBitsToUint(float(x)) >> 23u) - 127u;
    return x >= 0x1000000u ? high : low;
}

uint _f64u_msb(uvec2 a) {
    uint high = 32u + _f64_msb32(a.y);
    uint low = _f64_msb32(a.x);
    return a.y != 0u ? high : low;
}

// a >> n (n >= 1) rounded to nearest-even; sticky = true value strictly above a.
uvec2 _f64u_shr_rne(uvec2 a, uint n, bool sticky) {
    uvec2 q = _f64u_shr(a, n);
    uvec2 h = _f64u_shr(a, n - 1u);
    uvec2 below = _f64u_shl(h, n - 1u);
    bool rest = sticky || below.x != a.x || below.y != a.y;
    bool up = (h.x & 1u) == 1u && (rest || (q.x & 1u) == 1u);
    uvec2 q1 = _f64u_add(q, uvec2(1u, 0u));
    return up ? q1 : q;
}

// One lower f32 component into the magnitude accumulator (x, y) = |hi| with the hi LSB at bit
// 39; z = sticky. Same-sign components add, opposite-sign subtract; bits below the LSB are
// floored + sticky.
uvec3 _f64_acc_component(uvec3 st, float c, uint hi_sign, uint ehn) {
    uint b = floatBitsToUint(c);
    uint e = (b >> 23u) & 0xFFu;
    uint en = max(e, 1u);
    uint m = (e != 0u) ? ((b & 0x7FFFFFu) | 0x800000u) : (b & 0x7FFFFFu);
    int pos = 39 - (int(ehn) - int(en));
    bool left = pos >= 0;
    bool mid = !left && -pos < 24;
    uint rs = uint(clamp(-pos, 0, 31));
    uvec2 shifted = _f64u_shl(uvec2(m, 0u), uint(max(pos, 0)));
    uvec2 right = mid ? uvec2(m >> rs, 0u) : uvec2(0u, 0u);
    uvec2 part = left ? shifted : right;
    bool lost = (m & ((1u << rs) - 1u)) != 0u;
    bool dropped = !left && (!mid || lost);
    uvec2 acc = st.xy;
    uvec2 added = _f64u_add(acc, part);
    uvec2 subbed = _f64u_sub(_f64u_sub(acc, part), uvec2(dropped ? 1u : 0u, 0u));
    uvec2 next = ((b & 0x80000000u) == hi_sign) ? added : subbed;
    bool skip = (b & 0x7FFFFFFFu) == 0u || (st.z != 0u && dropped && part.x == 0u && part.y == 0u);
    uvec3 r = uvec3(next, dropped ? 1u : st.z);
    return skip ? st : r;
}

// Exact sum of up to four f32 components (hi first) as IEEE-754 double bits (lo, hi), RNE.
// BRANCH-FREE: inlined into every emulated-double store, and ANGLE's D3D backend compiles with
// FXC, whose compile time grows superlinearly with branches (see the WGSL twin).
uvec2 _f64_components_to_bits(vec4 c) {
    uint hb = floatBitsToUint(c.x);
    uint sign = hb & 0x80000000u;
    uint eh = (hb >> 23u) & 0xFFu;
    uint ehn = max(eh, 1u);
    uint mh = (eh != 0u) ? ((hb & 0x7FFFFFu) | 0x800000u) : (hb & 0x7FFFFFu);
    uvec3 st = uvec3(0u, mh << 7u, 0u);
    st = _f64_acc_component(st, c.y, sign, ehn);
    st = _f64_acc_component(st, c.z, sign, ehn);
    st = _f64_acc_component(st, c.w, sign, ehn);
    uvec2 acc = st.xy;
    uint p = _f64u_msb(acc);
    bool big = p > 52u;
    uvec2 mr = _f64u_shr_rne(acc, p - 52u, st.z != 0u);
    bool carry = (mr.y >> 21u) != 0u;
    uvec2 mr1 = _f64u_shr(mr, 1u);
    uvec2 mbig = carry ? mr1 : mr;
    uvec2 msmall = _f64u_shl(acc, 52u - p);
    uvec2 m = big ? mbig : msmall;
    uint pf = p + ((big && carry) ? 1u : 0u);
    uint biased = pf + ehn + 834u;
    uvec2 normal = uvec2(m.x, sign | (biased << 20u) | (m.y & 0xFFFFFu));
    bool zero = (acc.x | acc.y) == 0u || (hb & 0x7FFFFFFFu) == 0u;
    uvec2 finite = zero ? uvec2(0u, sign) : normal;
    uvec2 special = uvec2(0u, sign | (((hb & 0x7FFFFFu) != 0u) ? 0x7FF80000u : 0x7FF00000u));
    return eh == 0xFFu ? special : finite;
}

// x * 2^k for -178 <= k <= 127 (GLSL ES 3.00 has no ldexp).
float _f64_scale(float x, int k) {
    float lowk = x * uintBitsToFloat(uint(clamp(k + 253, 1, 254)) << 23u) * uintBitsToFloat(1u << 23u);
    float normk = x * uintBitsToFloat(uint(clamp(k + 127, 1, 254)) << 23u);
    return k < -126 ? lowk : normk;
}

// Exact split of IEEE double bits into non-overlapping f32 components (24 + 24 + 5 bits).
vec4 _f64_bits_to_components(uint lo, uint hi) {
    uint sign = hi & 0x80000000u;
    uint exponent = (hi >> 20u) & 0x7FFu;
    uint m_hi20 = hi & 0xFFFFFu;
    int e = int(exponent) - 1023;
    int f32_exp = e + 127;
    uint fe = uint(clamp(f32_exp, 1, 254));
    uint c0 = 0x800000u | (m_hi20 << 3u) | (lo >> 29u);
    uint c1 = (lo >> 5u) & 0xFFFFFFu;
    uint c2 = lo & 0x1Fu;
    float s = (sign != 0u) ? -1.0 : 1.0;
    float f0 = uintBitsToFloat(sign | (fe << 23u) | (c0 & 0x7FFFFFu));
    float f1 = s * _f64_scale(float(c1), e - 47);
    float f2 = s * _f64_scale(float(c2), e - 52);
    vec4 normal = vec4(f0, f1, f2, 0.0);
    vec4 approx = vec4(uintBitsToFloat(sign | (fe << 23u) | (m_hi20 << 3u)), 0.0, 0.0, 0.0);
    vec4 special = vec4(uintBitsToFloat(sign | (((m_hi20 | lo) != 0u) ? 0x7FC00000u : 0x7F800000u)), 0.0, 0.0, 0.0);
    vec4 zero = vec4(uintBitsToFloat(sign), 0.0, 0.0, 0.0);
    vec4 r = (f32_exp <= 0 || f32_exp >= 255) ? approx : normal;
    r = exponent == 0x7FFu ? special : r;
    return (exponent == 0u && m_hi20 == 0u && lo == 0u) ? zero : r;
}

// Unsigned 64-bit integer -> double bits, rounded to nearest-even at 53 bits.
uvec2 _f64_u64_to_bits(uvec2 v) {
    uint p = _f64u_msb(v);
    bool big = p > 52u;
    uvec2 mr = _f64u_shr_rne(v, p - 52u, false);
    bool carry = (mr.y >> 21u) != 0u;
    uvec2 mr1 = _f64u_shr(mr, 1u);
    uvec2 mbig = carry ? mr1 : mr;
    uvec2 msmall = _f64u_shl(v, 52u - p);
    uvec2 m = big ? mbig : msmall;
    uint pf = p + ((big && carry) ? 1u : 0u);
    uvec2 r = uvec2(m.x, ((pf + 1023u) << 20u) | (m.y & 0xFFFFFu));
    return (v.x == 0u && v.y == 0u) ? uvec2(0u, 0u) : r;
}

bool _f64_bits_is_nan(uvec2 b) {
    return ((b.y >> 20u) & 0x7FFu) == 0x7FFu && ((b.y & 0xFFFFFu) | b.x) != 0u;
}

// |value| truncated toward zero; z = 1 when |value| >= 2^64.
uvec3 _f64_bits_trunc_mag(uvec2 b) {
    uint exponent = (b.y >> 20u) & 0x7FFu;
    uint e = exponent - 1023u;
    uvec2 m = uvec2(b.x, (b.y & 0xFFFFFu) | 0x100000u);
    uvec2 up = _f64u_shl(m, e - 52u);
    uvec2 down = _f64u_shr(m, 52u - e);
    uvec3 r = uvec3(e >= 52u ? up : down, 0u);
    uvec3 sat = e >= 64u ? uvec3(0xFFFFFFFFu, 0xFFFFFFFFu, 1u) : r;
    return exponent < 1023u ? uvec3(0u, 0u, 0u) : sat;
}

vec4 f64_from_ieee754_bits(uint lo, uint hi) {
    vec4 c = _f64_bits_to_components(lo, hi);
    vec4 n = f32_quick_renorm(c, 0.0);
    return (c.y == 0.0 && c.z == 0.0) ? c : n;
}

uvec2 f64_to_ieee754_bits(vec4 v) {
    return _f64_components_to_bits(v);
}


vec4 f64_from_f32(float v) { return vec4(v, 0.0, 0.0, 0.0); }
float f64_to_f32(vec4 v) { return v.x + v.y + v.z + v.w; }
vec4 f64_new(float hi, float lo) { return vec4(hi, lo, 0.0, 0.0); }
vec4 f64_neg(vec4 a) { return vec4(-a.x, -a.y, -a.z, -a.w); }

vec4 f64_add(vec4 a, vec4 b) {
    // A non-finite sum returns the IEEE result (the error terms would compute Inf - Inf = NaN).
    // A select, not an early return: FXC compiles branches superlinearly.
    float _nf = a.x + b.x;
    // Component-wise two-sums through f32_two_sum: its u_one barriers keep ANGLE/D3D from folding
    // the error terms away. The inline form (v = s - a; u = s - v; ...) had none, and D3D reduced
    // it to zero error - 12345678.75 + 0.25 came out 12345678.75.
    vec2 c0 = f32_two_sum(a.x, b.x);
    vec2 c1 = f32_two_sum(a.y, b.y);
    vec2 c2 = f32_two_sum(a.z, b.z);
    vec2 c3 = f32_two_sum(a.w, b.w);
    float s0 = c0.x; float t0 = c0.y;
    float s1 = c1.x; float t1 = c1.y;
    float s2 = c2.x; float t2 = c2.y;
    float s3 = c3.x; float t3 = c3.y;
    vec2 ts1 = f32_two_sum(s1, t0);
    s1 = ts1.x; t0 = ts1.y;
    vec3 ts2 = f32_three_sum(s2, t0, t1);
    s2 = ts2.x; t0 = ts2.y; t1 = ts2.z;
    vec3 ts3 = f32_three_sum2(s3, t0, t2);
    s3 = ts3.x; t0 = ts3.y; t2 = ts3.z;
    t0 = t0 + t1 + t3;
    vec4 r = f32_quick_renorm(vec4(s0, s1, s2, s3), t0);
    return ((floatBitsToUint(_nf) & 0x7F800000u) == 0x7F800000u) ? vec4(_nf, 0.0, 0.0, 0.0) : r;
}

vec4 f64_sub(vec4 a, vec4 b) {
    return f64_add(a, f64_neg(b));
}

vec2 f64_split_oz(float a) {
    float c = 4097.0 * a;
    float a_hi = c * u_one - (c - a);
    float a_lo = a * u_one - a_hi;
    return vec2(a_hi, a_lo);
}

vec2 f64_two_prod_oz(float a, float b) {
    // (a * b) * u_one: the product is ROUNDED on its own before any add sees it. ANGLE compiles
    // without IEEE strictness, so D3D may fuse a bare a * b into a following add (mad); the
    // two-sum error terms downstream then describe a sum that was never computed (Ozaki
    // sqrt(x * x) came out 1 ULP low). Multiplying the rounded product by 1 cannot fuse wrongly.
    float p = (a * b) * u_one;
    vec2 a_s = f64_split_oz(a);
    vec2 b_s = f64_split_oz(b);
    float e = ((a_s.x * b_s.x - p) * u_one + a_s.x * b_s.y * u_one * u_one
        + a_s.y * b_s.x) + a_s.y * b_s.y * u_one * u_one * u_one;
    return vec2(p, e);
}

vec4 f64_mul(vec4 a, vec4 b) {
    // Non-finite product: the IEEE result (see f64_add).
    float _nf = a.x * b.x;
    vec2 pt0 = f64_two_prod_oz(a.x, b.x); float p0 = pt0.x; float q0 = pt0.y;
    vec2 pt1 = f64_two_prod_oz(a.x, b.y); float p1 = pt1.x; float q1 = pt1.y;
    vec2 pt2 = f64_two_prod_oz(a.y, b.x); float p2 = pt2.x; float q2 = pt2.y;
    vec2 pt3 = f64_two_prod_oz(a.x, b.z); float p3 = pt3.x; float q3 = pt3.y;
    vec2 pt4 = f64_two_prod_oz(a.y, b.y); float p4 = pt4.x; float q4 = pt4.y;
    vec2 pt5 = f64_two_prod_oz(a.z, b.x); float p5 = pt5.x; float q5 = pt5.y;
    vec3 ts1 = f32_three_sum(p1, p2, q0);
    float np1 = ts1.x; p2 = ts1.y; float nq0 = ts1.z;
    vec3 ts2 = f32_three_sum(p2, q1, q2);
    float np2 = ts2.x; q1 = ts2.y; q2 = ts2.z;
    vec3 ts3 = f32_three_sum(p3, p4, p5);
    p3 = ts3.x; p4 = ts3.y; p5 = ts3.z;
    vec2 ts4 = f32_two_sum(np2, p3);
    float s0 = ts4.x; float ot0 = ts4.y;
    vec2 ts5 = f32_two_sum(q1, p4);
    float s1 = ts5.x; float ot1 = ts5.y;
    float s2 = q2 + p5;
    vec2 ts6 = f32_two_sum(s1, ot0);
    s1 = ts6.x; ot0 = ts6.y;
    s2 += (ot0 + ot1);
    s1 += a.x*b.w + a.y*b.z + a.z*b.y + a.w*b.x + nq0 + q3 + q4 + q5;
    vec4 r = f32_quick_renorm(vec4(p0, np1, s0, s1), s2);
    return ((floatBitsToUint(_nf) & 0x7F800000u) == 0x7F800000u) ? vec4(_nf, 0.0, 0.0, 0.0) : r;
}

// Quad-float times a single f32 (the QD library's qd_real * double): three two_prods instead of
// the full product's six plus its three_sum tree. Division's correction steps multiply by one
// f32 quotient digit each.
vec4 _f64_mul_f32(vec4 a, float b) {
    vec2 t0 = f64_two_prod_oz(a.x, b);
    vec2 t1 = f64_two_prod_oz(a.y, b);
    vec2 t2 = f64_two_prod_oz(a.z, b);
    float p3 = (a.w * b) * u_one; // rounded alone (see f64_two_prod_oz)
    vec2 ts = f32_two_sum(t0.y, t1.x);
    vec3 th = f32_three_sum(ts.y, t1.y, t2.x);
    vec2 u1 = f32_two_sum(th.y, t2.y);
    vec2 u2 = f32_two_sum(p3, u1.x);
    return f32_quick_renorm(vec4(t0.x, ts.x, th.x, u2.x), (u1.y + u2.y) + th.z);
}

vec4 f64_div(vec4 a, vec4 b) {
    // Non-finite quotient or divisor: the IEEE result (see f64_add).
    float _nf = a.x / b.x;
    float q0_d = a.x / b.x;
    vec4 r = f64_sub(a, _f64_mul_f32(b, q0_d));
    float q1_d = r.x / b.x;
    r = f64_sub(r, _f64_mul_f32(b, q1_d));
    float q2_d = r.x / b.x;
    r = f64_sub(r, _f64_mul_f32(b, q2_d));
    float q3_d = r.x / b.x;
    vec4 qs1 = f64_add(f64_from_f32(q0_d), f64_from_f32(q1_d));
    vec4 qs2 = f64_add(f64_from_f32(q2_d), f64_from_f32(q3_d));
    vec4 q = f64_add(qs1, qs2);
    bool non_finite = (floatBitsToUint(_nf) & 0x7F800000u) == 0x7F800000u || (floatBitsToUint(b.x) & 0x7F800000u) == 0x7F800000u;
    return non_finite ? vec4(_nf, 0.0, 0.0, 0.0) : q;
}

bool f64_lt(vec4 a, vec4 b) {
    return (a.x < b.x) || (a.x == b.x && a.y < b.y);
}

bool f64_le(vec4 a, vec4 b) {
    return (a.x < b.x) || (a.x == b.x && a.y <= b.y);
}

bool f64_gt(vec4 a, vec4 b) {
    return (a.x > b.x) || (a.x == b.x && a.y > b.y);
}

bool f64_ge(vec4 a, vec4 b) {
    return (a.x > b.x) || (a.x == b.x && a.y >= b.y);
}

bool f64_eq(vec4 a, vec4 b) {
    return a.x == b.x && a.y == b.y;
}

bool f64_ne(vec4 a, vec4 b) {
    return a.x != b.x || a.y != b.y;
}

vec4 f64_abs(vec4 a) {
    vec4 n = f64_neg(a);
    return (a.x < 0.0 || (a.x == 0.0 && a.y < 0.0)) ? n : a;
}

vec4 f64_min(vec4 a, vec4 b) {
    bool lt = f64_lt(a, b);
    return lt ? a : b;
}

vec4 f64_max(vec4 a, vec4 b) {
    bool gt = f64_gt(a, b);
    return gt ? a : b;
}

// ---- exact integer <-> f64 conversions (integer domain; saturating like .NET 9+) ----
vec4 f64_from_u64(uvec2 v) {
    uvec2 b = _f64_u64_to_bits(v);
    return f64_from_ieee754_bits(b.x, b.y);
}

vec4 f64_from_i64(uvec2 v) {
    bool neg = (v.y & 0x80000000u) != 0u;
    uvec2 negv = _f64u_sub(uvec2(0u, 0u), v);
    uvec2 b = _f64_u64_to_bits(neg ? negv : v);
    return f64_from_ieee754_bits(b.x, b.y | (neg ? 0x80000000u : 0u));
}

vec4 f64_from_u32(uint v) { return f64_from_u64(uvec2(v, 0u)); }

vec4 f64_from_i32(int v) { return f64_from_i64(uvec2(uint(v), v < 0 ? 0xFFFFFFFFu : 0u)); }

uvec2 f64_to_i64(vec4 v) {
    uvec2 b = f64_to_ieee754_bits(v);
    uvec3 t = _f64_bits_trunc_mag(b);
    bool neg_ovf = t.z != 0u || t.y > 0x80000000u || (t.y == 0x80000000u && t.x != 0u);
    uvec2 negt = _f64u_sub(uvec2(0u, 0u), t.xy);
    uvec2 neg = neg_ovf ? uvec2(0u, 0x80000000u) : negt;
    uvec2 pos = (t.z != 0u || t.y >= 0x80000000u) ? uvec2(0xFFFFFFFFu, 0x7FFFFFFFu) : t.xy;
    uvec2 r = ((b.y & 0x80000000u) != 0u) ? neg : pos;
    bool nan = _f64_bits_is_nan(b);
    return nan ? uvec2(0u, 0u) : r;
}

uvec2 f64_to_u64(vec4 v) {
    uvec2 b = f64_to_ieee754_bits(v);
    uvec3 t = _f64_bits_trunc_mag(b);
    bool nan = _f64_bits_is_nan(b);
    return (nan || (b.y & 0x80000000u) != 0u) ? uvec2(0u, 0u) : t.xy;
}

int f64_to_i32(vec4 v) {
    uvec2 r = f64_to_i64(v);
    int hi = int(r.y);
    int r32 = (hi > 0 || (hi == 0 && r.x > 0x7FFFFFFFu)) ? 2147483647 : int(r.x);
    return (hi < -1 || (hi == -1 && r.x < 0x80000000u)) ? int(0x80000000u) : r32;
}

uint f64_to_u32(vec4 v) {
    uvec2 r = f64_to_u64(v);
    return r.y != 0u ? 0xFFFFFFFFu : r.x;
}


// ---- exact Floor / Ceiling / Round on the IEEE bit pattern ----
uvec2 _f64_bits_round_int(uvec2 b, bool ceil_mode) {
    uint exponent = (b.y >> 20u) & 0x7FFu;
    bool neg = (b.y & 0x80000000u) != 0u;
    uvec2 small_neg = ceil_mode ? uvec2(0u, 0x80000000u) : uvec2(0u, 0xBFF00000u);
    uvec2 small_pos = ceil_mode ? uvec2(0u, 0x3FF00000u) : uvec2(0u, 0u);
    uvec2 small = neg ? small_neg : small_pos;
    uvec2 small_r = ((b.x | (b.y & 0x7FFFFFFFu)) == 0u) ? b : small;
    uint fracbits = 1075u - exponent;
    uvec2 kept = _f64u_shl(_f64u_shr(b, fracbits), fracbits);
    uvec2 plus = _f64u_add(kept, _f64u_shl(uvec2(1u, 0u), fracbits));
    uvec2 bumped = (neg != ceil_mode) ? plus : kept;
    uvec2 mid = (kept.x == b.x && kept.y == b.y) ? b : bumped;
    uvec2 r = exponent < 1023u ? small_r : mid;
    return (exponent == 0x7FFu || exponent >= 1075u) ? b : r;
}

vec4 f64_floor(vec4 v) {
    uvec2 r = _f64_bits_round_int(f64_to_ieee754_bits(v), false);
    return f64_from_ieee754_bits(r.x, r.y);
}

vec4 f64_ceil(vec4 v) {
    uvec2 r = _f64_bits_round_int(f64_to_ieee754_bits(v), true);
    return f64_from_ieee754_bits(r.x, r.y);
}

// Nearest integer on the IEEE bit pattern; a tie goes to EVEN or AWAY from zero (rounding the
// components separately is wrong: 2.5000000001 = 2.5 + 1e-10 -> 2).
uvec2 _f64_bits_round_nearest(uvec2 b, bool away) {
    uint exponent = (b.y >> 20u) & 0x7FFu;
    uvec2 sign_only = uvec2(0u, b.y & 0x80000000u);
    uvec2 half_range = (!away && b.x == 0u && (b.y & 0xFFFFFu) == 0u) ? sign_only : uvec2(0u, sign_only.y | 0x3FF00000u);
    uint fracbits = 1075u - exponent;
    uvec2 one = _f64u_shl(uvec2(1u, 0u), fracbits);
    uvec2 kept = _f64u_shl(_f64u_shr(b, fracbits), fracbits);
    uvec2 frac = _f64u_sub(b, kept);
    uvec2 half_v = _f64u_shl(uvec2(1u, 0u), fracbits - 1u);
    bool above = frac.y > half_v.y || (frac.y == half_v.y && frac.x > half_v.x);
    bool tie = frac.x == half_v.x && frac.y == half_v.y;
    bool odd = ((kept.x & one.x) | (kept.y & one.y)) != 0u;
    uvec2 plus = _f64u_add(kept, one);
    uvec2 mid = (above || (tie && (away || odd))) ? plus : kept;
    uvec2 r = exponent == 1022u ? half_range : mid;
    r = exponent < 1022u ? sign_only : r;
    return (exponent == 0x7FFu || exponent >= 1075u) ? b : r;
}

vec4 f64_round_even(vec4 v) {
    uvec2 r = _f64_bits_round_nearest(f64_to_ieee754_bits(v), false);
    return f64_from_ieee754_bits(r.x, r.y);
}

vec4 f64_round_away(vec4 v) {
    uvec2 r = _f64_bits_round_nearest(f64_to_ieee754_bits(v), true);
    return f64_from_ieee754_bits(r.x, r.y);
}

vec4 f64_ieee_rem(vec4 a, vec4 b) {
    return f64_sub(a, f64_mul(b, f64_round_even(f64_div(a, b))));
}

// Square root: the f32 estimate y0 corrected once by (a - y0^2) / (2 y0) with y0^2 exact
// (two_prod) - about 48 bits, no divide - then one Newton step y = (y + a / y) / 2 in the full
// representation. Correctly rounded like the two divide-based Newton steps it replaces, in
// half the code (FXC compile time grows superlinearly in inlined code).
vec4 f64_sqrt(vec4 a) {
    float hi = a.x;
    uint hb = floatBitsToUint(hi);
    bool ok = hi > 0.0 && (hb & 0x7FFFFFFFu) != 0x7F800000u;
    vec4 x = ok ? a : vec4(1.0, 0.0, 0.0, 0.0);
    float y0 = sqrt(x.x);
    vec2 p = f64_two_prod_oz(y0, y0);
    vec4 r0 = f64_sub(x, vec4(p.x, p.y, 0.0, 0.0));
    vec4 y1 = f64_add(f64_from_f32(y0), f64_from_f32((r0.x * (0.5 / y0)) * u_one)); // rounded alone (see f64_two_prod_oz)
    vec4 y = f64_mul(f64_add(y1, f64_div(x, y1)), f64_from_f32(0.5));
    // Negative -> NaN; +-0, +Inf and NaN are their own roots.
    vec4 nan_v = f64_from_f32(uintBitsToFloat(0xFFC00000u | (hb & 0u)));
    vec4 special = hi < 0.0 ? nan_v : a;
    return ok ? y : special;
}

// Ozaki IEEE IsNaN / IsInfinity: the high component carries the f32 NaN / Inf pattern.
bool f64_is_nan(vec4 v) {
    uint b = floatBitsToUint(v.x);
    return (b & 0x7F800000u) == 0x7F800000u && (b & 0x007FFFFFu) != 0u;
}

bool f64_is_inf(vec4 v) {
    return (floatBitsToUint(v.x) & 0x7FFFFFFFu) == 0x7F800000u;
}


vec4 f64_trunc(vec4 v) {
    uvec2 b = f64_to_ieee754_bits(v);
    uvec2 r = _f64_bits_round_int(b, (b.y & 0x80000000u) != 0u);
    return f64_from_ieee754_bits(r.x, r.y);
}

vec4 f64_copysign(vec4 a, vec4 b) {
    vec4 m = f64_abs(a);
    vec4 n = f64_neg(m);
    return ((floatBitsToUint(b.x) >> 31u) != 0u) ? n : m;
}

vec4 f64_rem(vec4 a, vec4 b) {
    return f64_sub(a, f64_mul(b, f64_trunc(f64_div(a, b))));
}

vec4 f64_pow(vec4 a, vec4 b) {
    float x = f64_to_f32(a);
    float y = f64_to_f32(b);
    return f64_from_f32(pow(abs(x), y) * ((x < 0.0 && mod(abs(y), 2.0) >= 1.0) ? -1.0 : 1.0));
}

vec4 f64_atan2(vec4 a, vec4 b) {
    return f64_from_f32(atan(f64_to_f32(a), f64_to_f32(b)));
}

";

        #endregion

        #region f16 Emulation (Bit Conversion Helpers)

        /// <summary>
        /// GLSL helper functions for emulated Float16. Arithmetic happens in native
        /// <c>float</c>; the helpers convert between the 16-bit IEEE 754 bit pattern
        /// (held in a <c>uint</c>) and <c>float</c> at buffer load/store boundaries.
        /// Storage layout is packed: 2 halves per u32, same as WebGPU's emulation path.
        ///
        /// Behaviour matches the WGSL emulation in <see cref="WebGPU.Backend.WGSLEmulationLibrary.F16Functions"/>
        /// which itself matches the Wasm reference (<c>WasmKernelFunctionGenerator.EmitF16ToF32</c> /
        /// <c>EmitF32ToF16</c>). All three emulated backends produce identical results for
        /// identical inputs.
        /// </summary>
        public const string F16Functions = @"
// ============================================================================
// Float16 Emulation Functions (16-bit IEEE 754 in uint, float arithmetic)
// ============================================================================

// Expand a 16-bit Float16 bit pattern (held in the low 16 bits of a uint)
// into a native float value. Denormals flush to signed zero.
float _f16_to_f32(uint h) {
    uint sign = (h >> 15u) & 1u;
    uint exp  = (h >> 10u) & 0x1Fu;
    uint mant = h & 0x3FFu;
    if (exp == 0u) {
        return uintBitsToFloat(sign << 31u);
    }
    if (exp == 31u) {
        return uintBitsToFloat((sign << 31u) | (0xFFu << 23u) | (mant << 13u));
    }
    return uintBitsToFloat((sign << 31u) | ((exp + 112u) << 23u) | (mant << 13u));
}

// Compress a native float into the 16-bit Float16 bit pattern (returned in low 16
// bits of the uint). Underflow clamps to signed zero; overflow clamps to signed
// Inf while preserving mantissa bits so NaNs stay NaN.
uint _f32_to_f16(float f) {
    // IEEE round-to-nearest-even f32 -> f16, incl subnormals + overflow-to-Inf. Bit-exact to
    // numpy/PyTorch/CUDA/OpenCL and the managed HalfConversion (von der Zijp truncation replaced).
    uint bits = floatBitsToUint(f);
    uint sign = (bits >> 16u) & 0x8000u;
    uint rest = bits & 0x7FFFFFFFu;
    if (rest >= 0x7F800000u) {
        return (rest > 0x7F800000u) ? (sign | 0x7E00u) : (sign | 0x7C00u);   // NaN : Inf
    }
    int e = int((rest >> 23u) & 0xFFu) - 127;
    uint f32Mant = rest & 0x7FFFFFu;
    if (e > 15) { return sign | 0x7C00u; }                    // overflow -> Inf
    if (e < -14) {
        if (e < -25) { return sign; }                          // -> +-0
        uint signif = f32Mant | 0x800000u;
        uint shift = uint((-14 - e) + 13);
        uint m = signif >> shift;
        uint roundBit = (signif >> (shift - 1u)) & 1u;
        uint sticky = ((signif & ((1u << (shift - 1u)) - 1u)) != 0u) ? 1u : 0u;
        if (roundBit == 1u && (sticky == 1u || (m & 1u) == 1u)) { m = m + 1u; }
        return sign | m;
    }
    uint mant10 = f32Mant >> 13u;
    uint roundB = (f32Mant >> 12u) & 1u;
    uint stick = ((f32Mant & 0xFFFu) != 0u) ? 1u : 0u;
    uint outBits = (uint(e + 15) << 10u) | mant10;
    if (roundB == 1u && (stick == 1u || (mant10 & 1u) == 1u)) { outBits = outBits + 1u; }
    return sign | outBits;
}
";

        /// <summary>
        /// GLSL helpers that convert between the 16-bit bfloat16 bit pattern (low 16 bits of
        /// a uint) and float. bfloat16 is the top 16 bits of an fp32, so conversion is pure
        /// bit-shifting - no rebias or table. Matches the WGSL / managed BFloat16 byte-for-byte.
        /// </summary>
        public const string BF16Functions = @"
// ============================================================================
// BFloat16 Emulation Functions (top-16-bits-of-fp32 in uint, float arithmetic)
// ============================================================================

// Expand a 16-bit bfloat16 bit pattern (low 16 bits of a uint) into float (exact).
float _bf16_to_f32(uint h) {
    return uintBitsToFloat((h & 0xFFFFu) << 16u);
}

// Compress a native float into the 16-bit bfloat16 bit pattern (low 16 bits of the
// uint) using round-to-nearest-even. NaN preserved (force a mantissa bit).
uint _f32_to_bf16(float f) {
    uint bits = floatBitsToUint(f);
    if ((bits & 0x7FFFFFFFu) > 0x7F800000u) {
        return ((bits >> 16u) | 0x0040u) & 0xFFFFu;
    }
    uint lsb = (bits >> 16u) & 1u;
    uint rounded = bits + 0x7FFFu + lsb;
    return (rounded >> 16u) & 0xFFFFu;
}
";

        /// <summary>
        /// FP8 (OCP E5M2 + E4M3FN) bit-conversion helpers (GLSL ES 3.0). Always emulated (no native
        /// GLSL fp8); direct ports of the managed / OpenCL / WGSL conversion (CPU-verified 0/256,
        /// WebGPU PMT-verified), so every representable value round-trips bit-identically. The raw
        /// 8-bit pattern is in the low 8 bits of a uint; values compute as float in-register.
        /// </summary>
        public const string FP8Functions = @"
// ============================================================================
// FP8 Emulation Functions (E5M2 = 1/5/2 bias 15 IEEE-style; E4M3FN = 1/4/3 bias 7, no Inf, sat 448)
// ============================================================================

float _e5m2_to_f32(uint raw) {
    uint bits = raw & 0xFFu;
    uint sign = (bits & 0x80u) << 24u;
    uint expo = (bits >> 2u) & 0x1Fu;
    uint mant = bits & 0x03u;
    if (expo == 0u) {
        if (mant == 0u) { return uintBitsToFloat(sign); }
        uint e = 127u - 15u + 1u;
        uint m = mant;
        while ((m & 0x04u) == 0u) { m = m << 1u; e = e - 1u; }
        m = m & 0x03u;
        return uintBitsToFloat(sign | (e << 23u) | (m << 21u));
    }
    if (expo == 0x1Fu) { return uintBitsToFloat(sign | (0xFFu << 23u) | (mant << 21u)); }
    uint f32Exp = expo - 15u + 127u;
    return uintBitsToFloat(sign | (f32Exp << 23u) | (mant << 21u));
}

uint _f32_to_e5m2(float f) {
    uint bits = floatBitsToUint(f);
    uint sign = (bits >> 24u) & 0x80u;
    uint rest = bits & 0x7FFFFFFFu;
    if (rest > 0x7F800000u) { return sign | 0x7Fu; }
    if (rest == 0x7F800000u) { return sign | 0x7Cu; }
    int f32Exp = int((rest >> 23u) & 0xFFu);
    uint f32Mant = rest & 0x7FFFFFu;
    int e = f32Exp - 127;
    if (e > 15) { return sign | 0x7Cu; }
    if (e < -14) {
        if (f32Exp == 0) { return sign; }
        uint signif = f32Mant | 0x800000u;
        int shift = (-14 - e) + 21;
        if (shift > 31) { return sign; }
        uint m = signif >> uint(shift);
        uint roundBit = (signif >> uint(shift - 1)) & 1u;
        uint sticky = ((signif & ((1u << uint(shift - 1)) - 1u)) != 0u) ? 1u : 0u;
        if (roundBit == 1u && (sticky == 1u || (m & 1u) == 1u)) { m = m + 1u; }
        return sign | (m & 0x03u) | ((m >> 2u) << 2u);
    }
    uint mant2 = f32Mant >> 21u;
    uint roundB = (f32Mant >> 20u) & 1u;
    uint stick = ((f32Mant & 0xFFFFFu) != 0u) ? 1u : 0u;
    uint eField = uint(e + 15);
    uint outBits = (eField << 2u) | mant2;
    if (roundB == 1u && (stick == 1u || (mant2 & 1u) == 1u)) { outBits = outBits + 1u; }
    return sign | (outBits & 0x7Fu);
}

float _e4m3_to_f32(uint raw) {
    uint bits = raw & 0xFFu;
    uint sign = (bits & 0x80u) << 24u;
    uint expo = (bits >> 3u) & 0x0Fu;
    uint mant = bits & 0x07u;
    if ((bits & 0x7Fu) == 0x7Fu) { return uintBitsToFloat(sign | 0x7FC00000u); }
    if (expo == 0u) {
        if (mant == 0u) { return uintBitsToFloat(sign); }
        uint e = 127u - 7u + 1u;
        uint m = mant;
        while ((m & 0x08u) == 0u) { m = m << 1u; e = e - 1u; }
        m = m & 0x07u;
        return uintBitsToFloat(sign | (e << 23u) | (m << 20u));
    }
    uint f32Exp = expo - 7u + 127u;
    return uintBitsToFloat(sign | (f32Exp << 23u) | (mant << 20u));
}

uint _f32_to_e4m3(float f) {
    uint bits = floatBitsToUint(f);
    uint sign = (bits >> 24u) & 0x80u;
    uint rest = bits & 0x7FFFFFFFu;
    if (rest >= 0x7F800000u) { return sign | 0x7Fu; }
    int f32Exp = int((rest >> 23u) & 0xFFu);
    uint f32Mant = rest & 0x7FFFFFu;
    int e = f32Exp - 127;
    if (e > 8) { return sign | 0x7Fu; } // fn: e>8 unconditional overflow -> NaN; e==8 rounds below
    if (e < -6) {
        if (f32Exp == 0) { return sign; }
        uint signif = f32Mant | 0x800000u;
        int shift = (-6 - e) + 20;
        if (shift > 31) { return sign; }
        uint m = signif >> uint(shift);
        uint roundBit = (signif >> uint(shift - 1)) & 1u;
        uint sticky = ((signif & ((1u << uint(shift - 1)) - 1u)) != 0u) ? 1u : 0u;
        if (roundBit == 1u && (sticky == 1u || (m & 1u) == 1u)) { m = m + 1u; }
        return sign | (m & 0x7Fu);
    }
    uint mant3 = f32Mant >> 20u;
    uint roundB = (f32Mant >> 19u) & 1u;
    uint stick = ((f32Mant & 0x7FFFFu) != 0u) ? 1u : 0u;
    uint eField = uint(e + 7);
    uint outBits = (eField << 3u) | mant3;
    if (roundB == 1u && (stick == 1u || (mant3 & 1u) == 1u)) { outBits = outBits + 1u; }
    if (outBits >= 0x7Fu) { outBits = 0x7Fu; } // fn: full outBits (incl 0x80 carry) reaching the 0x7F slot -> NaN
    return sign | (outBits & 0x7Fu);
}
";

        /// <summary>
        /// FP4 (OCP E2M1FN, the NVFP4/MXFP4 element format) bit-conversion helpers (GLSL ES 3.0).
        /// Always emulated (no native GLSL fp4); direct port of the managed / OpenCL / CUDA conversion
        /// (CPU-verified bit-exact to ml_dtypes.float4_e2m1fn), so every representable value round-trips
        /// bit-identically. 1 sign / 2 exp / 1 mantissa, bias 1; 16 finite codes, NO Inf, NO NaN;
        /// magnitudes {0,.5,1,1.5,2,3,4,6}, max 6; finite overflow + +-Inf saturate to +-6; NaN -> -0
        /// (0x8). The 4-bit value lives in the LOW NIBBLE of the low 8 bits of a uint; values compute
        /// as float in-register. Mirrors the OpenCL _e2m1_bits_to_f32 / _f32_to_e2m1_bits.
        /// </summary>
        public const string FP4Functions = @"
// ============================================================================
// FP4 Emulation Functions (E2M1FN = 1/2/1 bias 1, no Inf, no NaN; magnitudes {0,.5,1,1.5,2,3,4,6})
// ============================================================================

float _e2m1_to_f32(uint raw) {
    uint code = raw & 0x0Fu;
    uint sign = (code & 0x8u) << 28u;
    uint e = (code >> 1u) & 0x3u;
    uint m = code & 0x1u;
    if (e == 0u) {
        if (m == 0u) { return uintBitsToFloat(sign); }
        return uintBitsToFloat(sign | (126u << 23u)); // subnormal 0.5
    }
    uint f32Exp = e - 1u + 127u;
    return uintBitsToFloat(sign | (f32Exp << 23u) | (m << 22u));
}

uint _f32_to_e2m1(float f) {
    uint bits = floatBitsToUint(f);
    uint sign = (bits >> 28u) & 0x8u;
    uint rest = bits & 0x7FFFFFFFu;
    if (rest > 0x7F800000u) { return 0x8u; } // NaN -> -0
    if (rest >= 0x7F800000u) { return sign | 0x7u; } // +-Inf -> +-6
    int f32Exp = int((rest >> 23u) & 0xFFu);
    uint f32Mant = rest & 0x7FFFFFu;
    int e = f32Exp - 127;
    if (e > 2) { return sign | 0x7u; } // finite overflow -> +-6
    if (e < 0) {
        if (f32Exp == 0) { return sign; } // +-0
        uint signif = f32Mant | 0x800000u;
        int shift = (-1 - e) + 23;
        if (shift > 31) { return sign; } // underflow -> +-0
        uint q = signif >> uint(shift);
        uint roundBit = (signif >> uint(shift - 1)) & 1u;
        uint sticky = ((signif & ((1u << uint(shift - 1)) - 1u)) != 0u) ? 1u : 0u;
        if (roundBit == 1u && (sticky == 1u || (q & 1u) == 1u)) { q = q + 1u; }
        return sign | (q & 0x7u);
    }
    uint mant1 = f32Mant >> 22u;
    uint round = (f32Mant >> 21u) & 1u;
    uint stick = ((f32Mant & 0x1FFFFFu) != 0u) ? 1u : 0u;
    uint eField = uint(e + 1);
    uint outBits = (eField << 1u) | mant1;
    if (round == 1u && (stick == 1u || (mant1 & 1u) == 1u)) { outBits = outBits + 1u; }
    if (outBits > 0x7u) { outBits = 0x7u; } // carry past +-6 saturates (no larger finite/Inf)
    return sign | (outBits & 0x7u);
}
";

        #endregion

        #region Combined Library

        /// <summary>
        /// Gets the full emulation library based on which features are enabled.
        /// Overload that defaults <c>includeF16</c> to false for source compatibility.
        /// </summary>
        public static string GetEmulationLibrary(bool includeF64, bool useOzakiF64, bool includeI64)
            => GetEmulationLibrary(includeF64, useOzakiF64, includeI64, includeF16: false);

        /// <summary>
        /// Gets the full emulation library based on which features are enabled.
        /// </summary>
        /// <param name="includeF16">When true, emits the Float16 bit-conversion helpers
        /// (<c>_f16_to_f32</c>, <c>_f32_to_f16</c>). WebGL has no native f16, so this is
        /// the only f16 path available on this backend.</param>
        public static string GetEmulationLibrary(bool includeF64, bool useOzakiF64, bool includeI64, bool includeF16, bool includeBF16 = false, bool includeFP8 = false, bool includeFP4 = false)
        {
            var sb = new System.Text.StringBuilder();

            if (includeF64)
            {
                if (useOzakiF64)
                {
                    sb.AppendLine(OzakiF64Functions);
                }
                else
                {
                    sb.AppendLine(F64Functions);
                }
            }

            if (includeI64)
            {
                sb.AppendLine(I64Functions);
            }

            if (includeF16)
            {
                sb.AppendLine(F16Functions);
            }

            if (includeBF16)
            {
                sb.AppendLine(BF16Functions);
            }

            if (includeFP8)
            {
                sb.AppendLine(FP8Functions);
            }

            if (includeFP4)
            {
                sb.AppendLine(FP4Functions);
            }

            return sb.ToString();
        }

        #endregion
    }
}
