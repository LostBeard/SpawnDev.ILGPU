// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: Float4E2M1.cs
//
// The kernel-native 4-bit floating-point type in the OCP "E2M1" layout (E2M1FN, the finite
// ML variant; the element format of NVFP4 / MXFP4): 1 sign / 2 exponent / 1 mantissa bits,
// exponent bias 1. ALL 16 codes are finite - NO infinities, NO NaN. The representable
// magnitudes are exactly {0, 0.5, 1, 1.5, 2, 3, 4, 6}; max is 6 (0x7 / 0xF).
//
// Bit-exact to `ml_dtypes.float4_e2m1fn` (PyTorch/JAX share it), verified by
// `DemoConsole -- fp4-oracle`:
//   code 0x0..0x7 = +{0,.5,1,1.5,2,3,4,6}; 0x8..0xF = the negatives (sign bit = bit 3).
//   encode is round-to-nearest-even among the 16 values; finite overflow AND +-Inf SATURATE
//   to +-6; NaN -> 0x8 (the format has no NaN encoding; ml_dtypes maps NaN -> -0, matched here).
//
// STORAGE = TRUE packed 4-bit ([PackedBits(4)]): an ArrayView<Float4E2M1> of N elements allocates
// ceil(N/2) bytes (2 nibbles per byte, 8 per 32-bit word) - the real NVFP4/MXFP4 memory footprint,
// like QInt4/QUInt4. The value lives in the low nibble of a 1-byte HOST struct; device buffers pack
// 2/byte and decode the E2M1 nibble to f32 in-register at the load (the data stays packed - no
// unpack-on-load). (Originally 1-byte-per-value, reusing the FP8 byte machinery; packed once the
// QInt4 nibble infra proved type-level 4-bit packing on every backend.)
//
// Modeled on ILGPU.Float8E4M3: FP32-based [MathIntrinsic]/[CompareIntrinisc]/[ConvertIntrinisc]
// operators (transpiled on every backend).
// ---------------------------------------------------------------------------------------

using ILGPU.Frontend.Intrinsic;
using ILGPU.IR.Values;
using ILGPU.Util;
using System;
#if !DEBUG
using System.Diagnostics;
#endif
using System.Runtime.CompilerServices;

namespace ILGPU
{
    /// <summary>
    /// A 4-bit floating-point value in OCP E2M1 (E2M1FN) layout (1 sign, 2 exponent, 1 mantissa,
    /// bias 1). All 16 codes finite (NO Inf/NaN); magnitudes {0,.5,1,1.5,2,3,4,6}, max 6. The
    /// NVFP4/MXFP4 element format. 1-byte storage (value in the low nibble).
    /// </summary>
    [Serializable]
    [PackedBits(4)]
    public readonly partial struct Float4E2M1 :
        IEquatable<Float4E2M1>, IComparable<Float4E2M1>
    {
        #region Static

        /// <summary>Returns the absolute value of the given E2M1 value.</summary>
        [MathIntrinsic(MathIntrinsicKind.Abs)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 Abs(Float4E2M1 value) => Float4E2M1Extensions.Abs(value);

        /// <summary>Returns true if the given E2M1 value represents 0 (either sign).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(Float4E2M1 value) => Float4E2M1Extensions.IsZero(value);

        /// <summary>Returns true always - E2M1 has no Inf or NaN (every code is finite).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(Float4E2M1 value) => true;

        /// <summary>
        /// Converts a float to E2M1. Round-to-nearest-even among the 16 values; finite overflow and
        /// +-Inf saturate to +-6; NaN -> -0 (bit-exact to ml_dtypes float4_e2m1fn). Same as the cast.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 FromSingle(float value) =>
            Float4E2M1Extensions.ConvertFloatToFloat4E2M1(value);

        /// <summary>
        /// Constructs an E2M1 value directly from its raw 4-bit code (only the low nibble is used;
        /// the high nibble is ignored). The inverse of <see cref="RawValue"/>. HOST-side / desktop
        /// factory for round-tripping packed storage; it does NOT round a float (pass a raw code
        /// 0x0..0xF, not a numeric value). To decode a packed nibble to float INSIDE a kernel, call
        /// <see cref="Float4E2M1Extensions.RawBitsToFloat(int)"/> instead - building a sub-word value
        /// from raw bits does not lower on the browser backends (they hold sub-word floats decoded in
        /// registers), whereas RawBitsToFloat is pure arithmetic that transpiles everywhere.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 FromRawBits(byte rawBits) => new Float4E2M1(rawBits);

        #endregion

        #region Constants

        /// <summary>Positive zero (0x0).</summary>
        public static readonly Float4E2M1 Zero = new Float4E2M1(0x0);

        /// <summary>The value one (exp=1, mant=0 -&gt; 0x2).</summary>
        public static readonly Float4E2M1 One = new Float4E2M1(0x2);

        /// <summary>The smallest positive value (0.5 subnormal, 0x1).</summary>
        public static readonly Float4E2M1 Epsilon = new Float4E2M1(0x1);

        /// <summary>The largest finite value (6.0, 0x7). E2M1 has no Inf.</summary>
        public static readonly Float4E2M1 MaxValue = new Float4E2M1(0x7);

        /// <summary>The smallest finite value (-6.0, 0xF).</summary>
        public static readonly Float4E2M1 MinValue = new Float4E2M1(0xF);

        #endregion

        #region Instance

        /// <summary>Constructs a new E2M1 value from its raw 4-bit pattern (low nibble of the byte).</summary>
        internal Float4E2M1(byte rawValue)
        {
            RawValue = (byte)(rawValue & 0x0F);
        }

        #endregion

        #region Properties

        /// <summary>The raw 4-bit code (stored in the low nibble of a byte). Round-trips with
        /// <see cref="FromRawBits"/>; use to re-encode a decoded value back into packed storage.</summary>
#if !DEBUG
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        public byte RawValue { get; }

        #endregion

        #region IEquatable / IComparable / Object

        /// <summary>Returns true if the given E2M1 is equal to the current value (by float value).</summary>
        public readonly bool Equals(Float4E2M1 other) => (float)this == other;

        /// <summary>Compares this E2M1 value to the given one (by float value).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int CompareTo(Float4E2M1 other) => ((float)this).CompareTo(other);

        /// <summary>Returns true if the given object is equal to the current value.</summary>
        public readonly override bool Equals(object? obj) =>
            obj is Float4E2M1 value && Equals(value);

        /// <summary>Returns the hash code of this value.</summary>
        public readonly override int GetHashCode() => RawValue;

        /// <summary>Returns the string representation of this value.</summary>
        public readonly override string ToString() => ((float)this).ToString();

        #endregion

        #region Operators

        /// <summary>Negates the given E2M1 value (flip the sign bit).</summary>
        [MathIntrinsic(MathIntrinsicKind.Neg)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 operator -(Float4E2M1 value) => Float4E2M1Extensions.Neg(value);

        /// <summary>Adds two E2M1 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Add)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 operator +(Float4E2M1 first, Float4E2M1 second) =>
            (Float4E2M1)((float)first + second);

        /// <summary>Subtracts two E2M1 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Sub)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 operator -(Float4E2M1 first, Float4E2M1 second) =>
            (Float4E2M1)((float)first - second);

        /// <summary>Multiplies two E2M1 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Mul)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 operator *(Float4E2M1 first, Float4E2M1 second) =>
            (Float4E2M1)((float)first * second);

        /// <summary>Divides two E2M1 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Div)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 operator /(Float4E2M1 first, Float4E2M1 second) =>
            (Float4E2M1)((float)first / second);

        /// <summary>Returns true if the two values are equal.</summary>
        [CompareIntrinisc(CompareKind.Equal)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Float4E2M1 first, Float4E2M1 second) =>
            (float)first == second;

        /// <summary>Returns true if the two values are not equal.</summary>
        [CompareIntrinisc(CompareKind.NotEqual, CompareFlags.UnsignedOrUnordered)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Float4E2M1 first, Float4E2M1 second) =>
            (float)first != second;

        /// <summary>Returns true if the first value is smaller than the second.</summary>
        [CompareIntrinisc(CompareKind.LessThan)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Float4E2M1 first, Float4E2M1 second) =>
            (float)first < second;

        /// <summary>Returns true if the first value is smaller than or equal to the second.</summary>
        [CompareIntrinisc(CompareKind.LessEqual)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Float4E2M1 first, Float4E2M1 second) =>
            (float)first <= second;

        /// <summary>Returns true if the first value is greater than the second.</summary>
        [CompareIntrinisc(CompareKind.GreaterThan)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Float4E2M1 first, Float4E2M1 second) =>
            (float)first > second;

        /// <summary>Returns true if the first value is greater than or equal to the second.</summary>
        [CompareIntrinisc(CompareKind.GreaterEqual)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Float4E2M1 first, Float4E2M1 second) =>
            (float)first >= second;

        /// <summary>Implicitly converts an E2M1 to a float.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator float(Float4E2M1 value) =>
            Float4E2M1Extensions.ConvertFloat4E2M1ToFloat(value);

        /// <summary>Implicitly converts an E2M1 to a double.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator double(Float4E2M1 value) => (float)value;

        /// <summary>Explicitly converts a float to an E2M1.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Float4E2M1(float value) =>
            Float4E2M1Extensions.ConvertFloatToFloat4E2M1(value);

        /// <summary>Explicitly converts a double to an E2M1.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Float4E2M1(double value) =>
            (Float4E2M1)(float)value;

        #endregion
    }

    /// <summary>
    /// Extension/implementation methods for the <see cref="Float4E2M1"/> type.
    /// </summary>
    public static partial class Float4E2M1Extensions
    {
        #region Constants

        private const byte SignBitMask = 0x8;       // bit 3
        private const byte MagnitudeMask = 0x7;     // exp(2) + mantissa(1)
        private const byte MaxFiniteMagnitude = 0x7; // 6.0

        #endregion

        #region Conversion

        /// <summary>Converts an E2M1 value to a float (rebias 1 -&gt; 127; 1 mantissa bit; no Inf/NaN).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ConvertFloat4E2M1ToFloat(Float4E2M1 value) =>
            RawBitsToFloat(value.RawValue);

        /// <summary>
        /// Decodes a raw 4-bit E2M1 code (the low nibble of <paramref name="rawBits"/>; the rest is
        /// ignored) directly to a float. THIS is the kernel-safe primitive for decoding packed FP4
        /// storage: extract the nibble from your packed block with your own bit-math, then call this -
        /// it does the verified decode as pure int/float arithmetic and transpiles on EVERY backend.
        /// Unlike <c>(float)FromRawBits(code)</c>, it never constructs the sub-word struct, so it
        /// avoids the browser backends' decoded-in-register model (where building a sub-word value
        /// from raw bits does not lower). On host it is identical to <c>(float)FromRawBits(code)</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RawBitsToFloat(int rawBits)
        {
            // SINGLE-EXIT / branchless on purpose (value-identical to the old early-return form). Early
            // returns inline as control flow, and the WebGL/GLSL structurizer DUPLICATES a following loop's
            // continuation into every exit arm of an inlined multi-exit function - so this decode inside an
            // MXFP4 nibble loop already cost ~16k GLSL lines, and stacking another multi-exit decode before
            // the loop exploded it past WebGL's compile limit. Selects are expressions, nothing to duplicate.
            uint code = (uint)(rawBits & 0xF);
            uint sign = (code & 0x8u) << 28;        // f32 sign bit (bit 31)
            uint e = (code >> 1) & 0x3u;            // exponent field (2 bits)
            uint m = code & 0x1u;                   // mantissa (1 bit)

            // Normal (e in 1..3): value = 1.m * 2^(e-1); f32 exp = (e-1)+127, single mantissa bit -> bit 22.
            uint normal = sign | ((e - 1u + 127u) << 23) | (m << 22);
            // Denormal (e == 0): m==0 -> +-0 (sign only); m==1 -> subnormal 0.5 = 2^-1 (f32 exp 126, mant 0).
            uint denorm = sign | ((m == 0u) ? 0u : (126u << 23));
            return Interop.IntAsFloat((e == 0u) ? denorm : normal);
        }

        /// <summary>
        /// Converts a float to an E2M1 value using round-to-nearest-even. Finite overflow and +-Inf
        /// SATURATE to +-6; NaN -&gt; -0 (0x8). Bit-exact to ml_dtypes float4_e2m1fn.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 ConvertFloatToFloat4E2M1(float value)
        {
            uint bits = Interop.FloatAsInt(value);
            uint sign = (bits >> 28) & 0x8u;        // E2M1 sign bit (bit 3)
            uint rest = bits & 0x7FFFFFFFu;

            // NaN -> 0x8 (-0); the format has no NaN. (ml_dtypes convention.)
            if (rest > 0x7F800000u)
                return new Float4E2M1((byte)0x8);
            // +-Inf -> saturate to +-6.
            if (rest >= 0x7F800000u)
                return new Float4E2M1((byte)(sign | MaxFiniteMagnitude));

            int f32Exp = (int)((rest >> 23) & 0xFFu);
            uint f32Mant = rest & 0x7FFFFFu;
            int e = f32Exp - 127;                    // unbiased

            // E2M1 normal exponent range: 0..2 (bias 1); max finite 6 at e=2, mant=1.
            // Finite overflow (e>2, or e==2 with mantissa rounding past 1.5) -> saturate to +-6.
            if (e > 2)
                return new Float4E2M1((byte)(sign | MaxFiniteMagnitude));

            if (e < 0)
            {
                // Subnormal (only 0.5 = 2^-1) or zero. signif = 1.f32Mant scaled to the 0.5 grid.
                if (f32Exp == 0)
                    return new Float4E2M1((byte)sign);          // f32 zero/subnormal -> +-0
                uint signif = f32Mant | 0x800000u;              // implicit 1 (24-bit)
                // Smallest E2M1 step is 0.5 = 2^-1. value = signif * 2^(e-23); we want round(value / 2^-1)
                // = round(signif * 2^(e-23+1)) as the count of 0.5 units, clamped to {0,1} (mantissa bit).
                int shift = (-1 - e) + 23;                      // align signif to the 2^-1 unit
                if (shift > 31)
                    return new Float4E2M1((byte)sign);          // underflow -> +-0
                uint q = signif >> shift;                       // integer count of 0.5 units
                uint roundBit = (signif >> (shift - 1)) & 1u;
                uint sticky = (signif & ((1u << (shift - 1)) - 1u)) != 0u ? 1u : 0u;
                if (roundBit == 1u && (sticky == 1u || (q & 1u) == 1u))
                    q += 1u;                                    // RNE; q may carry 0->1 or 1->2(=1.0)
                // q==0 -> 0; q==1 -> 0.5 (0x1); q==2 -> 1.0 (0x2 = smallest normal).
                return new Float4E2M1((byte)(sign | (q & 0x7u)));
            }

            // Normal range (e in 0..2). Rebias and round the mantissa 23 -> 1 bit (RNE).
            uint mant1 = f32Mant >> 22;             // top mantissa bit
            uint round = (f32Mant >> 21) & 1u;      // first dropped bit
            uint stick = (f32Mant & 0x1FFFFFu) != 0u ? 1u : 0u;
            uint eField = (uint)(e + 1);            // bias 1
            uint outBits = (eField << 1) | mant1;
            if (round == 1u && (stick == 1u || (mant1 & 1u) == 1u))
                outBits += 1u;                      // ties-to-even; may carry into the exponent
            // A carry past the max magnitude (0x7 = 6) saturates to 6 (no larger finite, no Inf).
            if ((outBits & 0x7u) > MaxFiniteMagnitude || outBits > 0x7u)
                outBits = MaxFiniteMagnitude;
            return new Float4E2M1((byte)(sign | (outBits & 0x7u)));
        }

        #endregion

        #region Predicates

        /// <summary>Negates the given E2M1 value (flip the sign bit).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 Neg(Float4E2M1 value) =>
            new Float4E2M1((byte)(value.RawValue ^ SignBitMask));

        /// <summary>Returns the absolute value (clear the sign bit).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 Abs(Float4E2M1 value) =>
            new Float4E2M1((byte)(value.RawValue & MagnitudeMask));

        /// <summary>Returns true if the value is +-0 (magnitude == 0).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(Float4E2M1 value) =>
            (value.RawValue & MagnitudeMask) == 0;

        #endregion

        #region FP32 Implementation Methods

        /// <summary>Implements an E2M1 addition using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 AddFP32(Float4E2M1 first, Float4E2M1 second) =>
            (Float4E2M1)((float)first + second);

        /// <summary>Implements an E2M1 subtraction using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 SubFP32(Float4E2M1 first, Float4E2M1 second) =>
            (Float4E2M1)((float)first - second);

        /// <summary>Implements an E2M1 multiplication using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 MulFP32(Float4E2M1 first, Float4E2M1 second) =>
            (Float4E2M1)((float)first * second);

        /// <summary>Implements an E2M1 division using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 DivFP32(Float4E2M1 first, Float4E2M1 second) =>
            (Float4E2M1)((float)first / second);

        #endregion
    }
}
