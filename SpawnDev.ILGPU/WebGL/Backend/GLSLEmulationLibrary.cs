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

// --- IEEE 754 double bits to double-float conversion ---
vec2 f64_from_ieee754_bits(uint lo, uint hi) {
    uint sign_bit = (hi >> 31u) & 1u;
    uint exponent = (hi >> 20u) & 0x7FFu;
    uint mantissa_hi20 = hi & 0xFFFFFu;
    uint mantissa_lo32 = lo;

    // Zero (preserve sign of zero - f32 also has signed zero)
    if (exponent == 0u && mantissa_hi20 == 0u && mantissa_lo32 == 0u) {
        uint zero_bits = sign_bit << 31u;
        return vec2(uintBitsToFloat(zero_bits), 0.0);
    }
    // Inf / NaN: preserve f32 Inf or NaN encoding in the high lane so
    // f64_is_inf / f64_is_nan / f64_eq operate correctly. The pre-fix
    // collapsed every non-finite to vec2(0,0), causing
    // `double.PositiveInfinity == 0.0` to compare TRUE on WebGL (both
    // sides loaded as vec2(0,0)). Sign is preserved via sign_bit; quiet
    // NaN bit pattern is 0x7FC00000 with sign in bit 31.
    if (exponent == 0x7FFu) {
        bool is_nan = (mantissa_hi20 != 0u) || (mantissa_lo32 != 0u);
        uint hi_bits = (sign_bit << 31u) | (is_nan ? 0x7FC00000u : 0x7F800000u);
        return vec2(intBitsToFloat(int(hi_bits)), 0.0);
    }

    int exp_bias = 1023;
    int exp_val = int(exponent) - exp_bias;
    int f32_exp_bias = 127;
    int f32_exp = exp_val + f32_exp_bias;

    if (f32_exp <= 0 || f32_exp >= 255) {
        uint f32_bits_approx = (sign_bit << 31u) | (uint(clamp(f32_exp, 1, 254)) << 23u) | (mantissa_hi20 << 3u);
        float val_approx = intBitsToFloat(int(f32_bits_approx));
        return vec2(val_approx, 0.0);
    }

    uint top23 = (mantissa_hi20 << 3u) | (mantissa_lo32 >> 29u);
    uint f32_bits_h = (sign_bit << 31u) | (uint(f32_exp) << 23u) | top23;
    float val_hi = intBitsToFloat(int(f32_bits_h));

    uint remaining = mantissa_lo32 & 0x1FFFFFFFu;
    if (remaining == 0u) {
        return vec2(val_hi, 0.0);
    }

    int lo_exp = exp_val - 29 + f32_exp_bias;
    float val_lo;
    if (lo_exp > 0 && lo_exp < 255) {
        float rem_f = float(remaining);
        int scale_exp = exp_val - 23 + f32_exp_bias;
        if (scale_exp > 0 && scale_exp < 255) {
            uint scale_bits = uint(scale_exp) << 23u;
            float scale = intBitsToFloat(int(scale_bits));
            val_lo = (rem_f / 536870912.0) * scale;
        } else {
            val_lo = 0.0;
        }
    } else {
        val_lo = 0.0;
    }

    if (sign_bit != 0u) {
        val_lo = -val_lo;
    }

    // Inline two-sum normalization with u_one anti-optimization
    float _s = (val_hi + val_lo);
    float _v = (_s * u_one - val_hi) * u_one;
    float _e = (val_hi - (_s - _v) * u_one) * u_one * u_one * u_one + (val_lo - _v);
    return vec2(_s, _e);
}

// Store emu_f64 back to IEEE 754 bits
uvec2 f64_to_ieee754_bits(vec2 v) {
    float val_hi = v.x;
    float val_lo = v.y;

    // Zero check via bit pattern (preserves -0.0 vs +0.0).
    // floatBitsToUint preserves sign of zero where some drivers normalise
    // through the floatBitsToInt+uint cast path.
    uint f32_bits_h_check = floatBitsToUint(val_hi);
    if ((f32_bits_h_check & 0x7FFFFFFFu) == 0u && val_lo == 0.0) {
        return uvec2(0u, f32_bits_h_check & 0x80000000u);
    }

    uint f32_bits_h = floatBitsToUint(val_hi);
    uint sign = (f32_bits_h >> 31u) & 1u;
    uint f32_exp = (f32_bits_h >> 23u) & 0xFFu;
    uint f32_mantissa = f32_bits_h & 0x7FFFFFu;

    int f32_bias = 127;
    int f64_bias = 1023;
    int exp_val = int(f32_exp) - f32_bias;
    uint f64_exp = uint(exp_val + f64_bias);

    uint mantissa_hi20 = f32_mantissa >> 3u;
    uint mantissa_lo32 = (f32_mantissa & 0x7u) << 29u;

    if (val_lo != 0.0) {
        int scale_exp = exp_val - 23 + f32_bias;
        if (scale_exp > 0 && scale_exp < 255) {
            uint scale_bits = uint(scale_exp) << 23u;
            float scale = intBitsToFloat(int(scale_bits));
            float abs_lo = abs(val_lo);
            float rem_f = (abs_lo / scale) * 536870912.0;
            uint remaining = uint(clamp(rem_f + 0.5, 0.0, 536870911.0));
            mantissa_lo32 = mantissa_lo32 | (remaining & 0x1FFFFFFFu);
        }
    }

    uint out_hi = (sign << 31u) | (f64_exp << 20u) | mantissa_hi20;
    uint out_lo = mantissa_lo32;
    return uvec2(out_lo, out_hi);
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
    float p = a * b;
    vec2 a_s = f64_split(a);
    vec2 b_s = f64_split(b);
    float err = ((a_s.x * b_s.x - p) * u_one + a_s.x * b_s.y * u_one * u_one
        + a_s.y * b_s.x) + a_s.y * b_s.y * u_one * u_one * u_one;
    return vec2(p, err);
}

// emu_f64 addition (full error propagation from both hi and lo parts)
vec2 f64_add(vec2 a, vec2 b) {
    vec2 s = f64_two_sum(a.x, b.x);
    vec2 t = f64_two_sum(a.y, b.y);
    s.y += t.x;
    s = f64_quick_two_sum(s.x, s.y);
    s.y += t.y;
    s = f64_quick_two_sum(s.x, s.y);
    return s;
}

// emu_f64 subtraction
vec2 f64_sub(vec2 a, vec2 b) {
    return f64_add(a, f64_neg(b));
}

// emu_f64 multiplication
vec2 f64_mul(vec2 a, vec2 b) {
    vec2 p = f64_two_prod(a.x, b.x);
    p.y += a.x * b.y;
    p = f64_quick_two_sum(p.x, p.y);
    p.y += a.y * b.x;
    p = f64_quick_two_sum(p.x, p.y);
    return p;
}

// emu_f64 division
vec2 f64_div(vec2 a, vec2 b) {
    float xn = 1.0 / b.x;
    vec2 yn = a * xn;
    float diff = (f64_sub(a, f64_mul(b, yn))).x;
    vec2 prod = f64_two_prod(xn, diff);
    return f64_add(yn, prod);
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

// emu_f64 comparisons - IEEE-strict NaN handling via bit pattern.
bool f64_lt(vec2 a, vec2 b) {
    if (_f32_is_nan_bits(a.x) || _f32_is_nan_bits(b.x)) { return false; }
    return (a.x < b.x) || (a.x == b.x && a.y < b.y);
}

bool f64_le(vec2 a, vec2 b) {
    if (_f32_is_nan_bits(a.x) || _f32_is_nan_bits(b.x)) { return false; }
    return (a.x < b.x) || (a.x == b.x && a.y <= b.y);
}

bool f64_gt(vec2 a, vec2 b) {
    if (_f32_is_nan_bits(a.x) || _f32_is_nan_bits(b.x)) { return false; }
    return (a.x > b.x) || (a.x == b.x && a.y > b.y);
}

bool f64_ge(vec2 a, vec2 b) {
    if (_f32_is_nan_bits(a.x) || _f32_is_nan_bits(b.x)) { return false; }
    return (a.x > b.x) || (a.x == b.x && a.y >= b.y);
}

bool f64_eq(vec2 a, vec2 b) {
    if (_f32_is_nan_bits(a.x) || _f32_is_nan_bits(b.x)) { return false; }
    return a.x == b.x && a.y == b.y;
}

bool f64_ne(vec2 a, vec2 b) {
    if (_f32_is_nan_bits(a.x) || _f32_is_nan_bits(b.x)) { return true; }
    return a.x != b.x || a.y != b.y;
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

// emu_f64 absolute value
vec2 f64_abs(vec2 a) {
    if (a.x < 0.0 || (a.x == 0.0 && a.y < 0.0)) {
        return f64_neg(a);
    }
    return a;
}

// emu_f64 min/max
vec2 f64_min(vec2 a, vec2 b) {
    if (f64_lt(a, b)) { return a; }
    return b;
}

vec2 f64_max(vec2 a, vec2 b) {
    if (f64_gt(a, b)) { return a; }
    return b;
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

// emu_i64/emu_u64 addition with carry
uvec2 i64_add(uvec2 a, uvec2 b) {
    uint lo = a.x + b.x;
    uint carry = lo < a.x ? 1u : 0u;
    uint hi = a.y + b.y + carry;
    return uvec2(lo, hi);
}

// emu_i64/emu_u64 subtraction with borrow
uvec2 i64_sub(uvec2 a, uvec2 b) {
    uint borrow = a.x < b.x ? 1u : 0u;
    uint lo = a.x - b.x;
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

// --- IEEE 754 double bits to quad-float conversion ---
vec4 f64_from_ieee754_bits(uint lo, uint hi) {
    uint sign_bit = (hi >> 31u) & 1u;
    uint exponent = (hi >> 20u) & 0x7FFu;
    uint mantissa_hi20 = hi & 0xFFFFFu;
    uint mantissa_lo32 = lo;

    // Zero (preserve sign of zero)
    if (exponent == 0u && mantissa_hi20 == 0u && mantissa_lo32 == 0u) {
        uint zero_bits = sign_bit << 31u;
        return vec4(uintBitsToFloat(zero_bits), 0.0, 0.0, 0.0);
    }
    if (exponent == 0x7FFu) {
        return vec4(0.0, 0.0, 0.0, 0.0);
    }

    int exp_bias = 1023;
    int exp_val = int(exponent) - exp_bias;
    int f32_exp_bias = 127;
    int f32_exp = exp_val + f32_exp_bias;

    if (f32_exp <= 0 || f32_exp >= 255) {
        uint f32_bits_approx = (sign_bit << 31u) | (uint(clamp(f32_exp, 1, 254)) << 23u) | (mantissa_hi20 << 3u);
        float val_approx = intBitsToFloat(int(f32_bits_approx));
        return vec4(val_approx, 0.0, 0.0, 0.0);
    }

    uint top23 = (mantissa_hi20 << 3u) | (mantissa_lo32 >> 29u);
    uint f32_bits_h = (sign_bit << 31u) | (uint(f32_exp) << 23u) | top23;
    float val_hi = intBitsToFloat(int(f32_bits_h));

    uint remaining = mantissa_lo32 & 0x1FFFFFFFu;
    if (remaining == 0u) {
        return vec4(val_hi, 0.0, 0.0, 0.0);
    }

    int lo_exp = exp_val - 29 + f32_exp_bias;
    float val_lo = 0.0;
    if (lo_exp > 0 && lo_exp < 255) {
        float rem_f = float(remaining);
        int scale_exp = exp_val - 23 + f32_exp_bias;
        if (scale_exp > 0 && scale_exp < 255) {
            uint scale_bits = uint(scale_exp) << 23u;
            float scale = intBitsToFloat(int(scale_bits));
            val_lo = (rem_f / 536870912.0) * scale;
        }
    }

    if (sign_bit != 0u) {
        val_lo = -val_lo;
    }

    vec2 ts = f32_quick_two_sum(val_hi, val_lo);
    return f32_quick_renorm(vec4(ts.x, ts.y, 0.0, 0.0), 0.0);
}

// Store emu_f64 back to IEEE 754 bits for buffer write
uvec2 f64_to_ieee754_bits(vec4 v) {
    float val_hi = v.x;
    float val_lo = v.y;

    // Zero check via bit pattern (preserves -0.0 vs +0.0)
    uint f32_bits_h_check = uint(floatBitsToInt(val_hi));
    if ((f32_bits_h_check & 0x7FFFFFFFu) == 0u && val_lo == 0.0) {
        return uvec2(0u, f32_bits_h_check & 0x80000000u);
    }

    uint f32_bits_h = uint(floatBitsToInt(val_hi));
    uint sign = (f32_bits_h >> 31u) & 1u;
    uint f32_exp = (f32_bits_h >> 23u) & 0xFFu;
    uint f32_mantissa = f32_bits_h & 0x7FFFFFu;

    int f32_bias = 127;
    int f64_bias = 1023;
    int exp_val = int(f32_exp) - f32_bias;
    uint f64_exp = uint(exp_val + f64_bias);

    uint mantissa_hi20 = f32_mantissa >> 3u;
    uint mantissa_lo32 = (f32_mantissa & 0x7u) << 29u;

    if (val_lo != 0.0) {
        int scale_exp = exp_val - 23 + f32_bias;
        if (scale_exp > 0 && scale_exp < 255) {
            uint scale_bits = uint(scale_exp) << 23u;
            float scale = intBitsToFloat(int(scale_bits));
            float abs_lo = abs(val_lo);
            float rem_f = (abs_lo / scale) * 536870912.0;
            uint rem_u = uint(clamp(rem_f + 0.5, 0.0, 536870911.0));
            mantissa_lo32 = mantissa_lo32 | (rem_u & 0x1FFFFFFFu);
        }
    }

    uint out_hi = (sign << 31u) | (f64_exp << 20u) | mantissa_hi20;
    uint out_lo = mantissa_lo32;
    return uvec2(out_lo, out_hi);
}

vec4 f64_from_f32(float v) { return vec4(v, 0.0, 0.0, 0.0); }
float f64_to_f32(vec4 v) { return v.x + v.y + v.z + v.w; }
vec4 f64_new(float hi, float lo) { return vec4(hi, lo, 0.0, 0.0); }
vec4 f64_neg(vec4 a) { return vec4(-a.x, -a.y, -a.z, -a.w); }

vec4 f64_add(vec4 a, vec4 b) {
    float s0 = a.x + b.x;
    float s1 = a.y + b.y;
    float s2 = a.z + b.z;
    float s3 = a.w + b.w;

    float v0 = s0 - a.x;
    float v1 = s1 - a.y;
    float v2 = s2 - a.z;
    float v3 = s3 - a.w;

    float u0 = s0 - v0;
    float u1 = s1 - v1;
    float u2 = s2 - v2;
    float u3 = s3 - v3;

    float w0 = a.x - u0;
    float w1 = a.y - u1;
    float w2 = a.z - u2;
    float w3 = a.w - u3;

    float uu0 = b.x - v0;
    float uu1 = b.y - v1;
    float uu2 = b.z - v2;
    float uu3 = b.w - v3;

    float t0 = w0 + uu0;
    float t1 = w1 + uu1;
    float t2 = w2 + uu2;
    float t3 = w3 + uu3;

    vec2 ts1 = f32_two_sum(s1, t0);
    s1 = ts1.x; t0 = ts1.y;

    vec3 ts2 = f32_three_sum(s2, t0, t1);
    s2 = ts2.x; t0 = ts2.y; t1 = ts2.z;

    vec3 ts3 = f32_three_sum2(s3, t0, t2);
    s3 = ts3.x; t0 = ts3.y; t2 = ts3.z;

    t0 = t0 + t1 + t3;

    return f32_quick_renorm(vec4(s0, s1, s2, s3), t0);
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
    float p = a * b;
    vec2 a_s = f64_split_oz(a);
    vec2 b_s = f64_split_oz(b);
    float e = ((a_s.x * b_s.x - p) * u_one + a_s.x * b_s.y * u_one * u_one
        + a_s.y * b_s.x) + a_s.y * b_s.y * u_one * u_one * u_one;
    return vec2(p, e);
}

vec4 f64_mul(vec4 a, vec4 b) {
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

    return f32_quick_renorm(vec4(p0, np1, s0, s1), s2);
}

vec4 f64_div(vec4 a, vec4 b) {
    float q0_d = a.x / b.x;
    vec4 r = f64_sub(a, f64_mul(b, f64_from_f32(q0_d)));

    float q1_d = r.x / b.x;
    r = f64_sub(r, f64_mul(b, f64_from_f32(q1_d)));

    float q2_d = r.x / b.x;
    r = f64_sub(r, f64_mul(b, f64_from_f32(q2_d)));

    float q3_d = r.x / b.x;
    vec4 qs1 = f64_add(f64_from_f32(q0_d), f64_from_f32(q1_d));
    vec4 qs2 = f64_add(f64_from_f32(q2_d), f64_from_f32(q3_d));
    return f64_add(qs1, qs2);
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
    if (a.x < 0.0 || (a.x == 0.0 && a.y < 0.0)) {
        return f64_neg(a);
    }
    return a;
}

vec4 f64_min(vec4 a, vec4 b) {
    if (f64_lt(a, b)) { return a; }
    return b;
}

vec4 f64_max(vec4 a, vec4 b) {
    if (f64_gt(a, b)) { return a; }
    return b;
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
    uint bits = floatBitsToUint(f);
    uint sign = (bits >> 31u) & 1u;
    int exp_i = int((bits >> 23u) & 0xFFu) - 112;
    uint mant = (bits >> 13u) & 0x3FFu;
    if (exp_i < 0) {
        exp_i = 0;
        mant = 0u;
    }
    if (exp_i > 31) {
        exp_i = 31;
    }
    return (sign << 15u) | (uint(exp_i) << 10u) | mant;
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
        public static string GetEmulationLibrary(bool includeF64, bool useOzakiF64, bool includeI64, bool includeF16, bool includeBF16 = false, bool includeFP8 = false)
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

            return sb.ToString();
        }

        #endregion
    }
}
