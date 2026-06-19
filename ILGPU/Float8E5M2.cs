// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: Float8E5M2.cs
//
// The kernel-native 8-bit floating-point type in the OCP "E5M2" layout: 1 sign / 5 exponent
// / 2 mantissa bits, exponent bias 15. It is IEEE-754-style: it HAS infinities (exp=0x1F,
// mant=0) and NaNs (exp=0x1F, mant!=0), exactly like fp16 but with 8 fewer mantissa bits.
// It is the gradient/backward-pass format of the standard FP8 training recipe (E4M3 forward,
// E5M2 backward) - it trades all but 2 mantissa bits for fp16-class dynamic range, which is
// what gradients need.
//
// Modeled on ILGPU.Half / ILGPU.BFloat16: arithmetic/comparison/conversion operators are
// FP32-based and tagged [MathIntrinsic]/[CompareIntrinisc]/[ConvertIntrinisc], so ILGPU's
// frontend transpiles them on every backend. Storage is a single byte (sibling of Half/bf16's
// 2-byte sub-word path, but 1 byte). Unlike bf16 (a truncated fp32 = trivial shift) the
// conversion needs exponent rebias + round + over/underflow handling, like fp16.
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
    /// An 8-bit floating-point value in OCP E5M2 layout (1 sign, 5 exponent, 2 mantissa bits,
    /// bias 15). IEEE-754-style: has infinities and NaNs. The FP8 backward/gradient format.
    /// </summary>
    [Serializable]
    public readonly partial struct Float8E5M2 :
        IEquatable<Float8E5M2>, IComparable<Float8E5M2>
    {
        #region Static

        /// <summary>Returns the absolute value of the given E5M2 value.</summary>
        [MathIntrinsic(MathIntrinsicKind.Abs)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 Abs(Float8E5M2 value) => Float8E5M2Extensions.Abs(value);

        /// <summary>Returns true if the given E5M2 value represents a NaN value.</summary>
        [MathIntrinsic(MathIntrinsicKind.IsNaNF)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(Float8E5M2 value) => Float8E5M2Extensions.IsNaN(value);

        /// <summary>Returns true if the given E5M2 value represents 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(Float8E5M2 value) => Float8E5M2Extensions.IsZero(value);

        /// <summary>Returns true if the given E5M2 value represents +infinity.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPositiveInfinity(Float8E5M2 value) =>
            Float8E5M2Extensions.IsPositiveInfinity(value);

        /// <summary>Returns true if the given E5M2 value represents -infinity.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(Float8E5M2 value) =>
            Float8E5M2Extensions.IsNegativeInfinity(value);

        /// <summary>Returns true if the given E5M2 value represents infinity.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(Float8E5M2 value) =>
            Float8E5M2Extensions.IsInfinity(value);

        /// <summary>Returns true if the given E5M2 value represents a finite number.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(Float8E5M2 value) => Float8E5M2Extensions.IsFinite(value);

        /// <summary>
        /// Converts a float to E5M2 with a selectable overflow convention. When
        /// <paramref name="saturate"/> is false (the DEFAULT, matching the cast operator): finite
        /// overflow -&gt; +-Inf (IEEE, bit-exact to ml_dtypes float8_e5m2). When true: finite overflow
        /// clamps to +-57344 (max normal), the NVIDIA Transformer Engine / OCP saturating cast.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 FromSingle(float value, bool saturate) =>
            saturate ? Float8E5M2Extensions.FromSingleSaturating(value)
                     : Float8E5M2Extensions.ConvertFloatToFloat8E5M2(value);

        /// <summary>
        /// Converts a float to E5M2 using the SATURATING convention: finite overflow clamps to
        /// +-57344 (max normal); +-Inf -&gt; +-Inf; NaN -&gt; NaN. NVIDIA Transformer Engine / OCP mode.
        /// Use when you want overflow clamped instead of producing Inf. NOT the default.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 FromSingleSaturating(float value) =>
            Float8E5M2Extensions.FromSingleSaturating(value);

        #endregion

        #region Constants

        /// <summary>Represents a positive zero value (0x00).</summary>
        public static readonly Float8E5M2 Zero = new Float8E5M2(0x00);

        /// <summary>Represents the value one (1.0). exp=15 (bias), mant=0 -&gt; 0x3C.</summary>
        public static readonly Float8E5M2 One = new Float8E5M2(0x3C);

        /// <summary>The smallest positive subnormal (2^-16, 0x01).</summary>
        public static readonly Float8E5M2 Epsilon = new Float8E5M2(0x01);

        /// <summary>The largest finite value (57344 = 1.75 * 2^15, exp=30 mant=3 -&gt; 0x7B).</summary>
        public static readonly Float8E5M2 MaxValue = new Float8E5M2(0x7B);

        /// <summary>The smallest finite value (-57344, 0xFB).</summary>
        public static readonly Float8E5M2 MinValue = new Float8E5M2(0xFB);

        /// <summary>Quiet NaN (exp all ones, top mantissa bit set -&gt; 0x7E).</summary>
        public static readonly Float8E5M2 NaN = new Float8E5M2(0x7E);

        /// <summary>Positive infinity (exp all ones, mant=0 -&gt; 0x7C).</summary>
        public static readonly Float8E5M2 PositiveInfinity = new Float8E5M2(0x7C);

        /// <summary>Negative infinity (0xFC).</summary>
        public static readonly Float8E5M2 NegativeInfinity = new Float8E5M2(0xFC);

        #endregion

        #region Instance

        /// <summary>Constructs a new E5M2 value from its raw 8-bit pattern.</summary>
        internal Float8E5M2(byte rawValue)
        {
            RawValue = rawValue;
        }

        /// <summary>
        /// Constructs an E5M2 value directly from its raw 8-bit code. The inverse of
        /// <see cref="RawValue"/>. HOST-side / desktop factory for round-tripping packed storage; it
        /// does NOT round a float (pass a raw 0x00..0xFF code, not a numeric value). To decode a
        /// packed byte to float INSIDE a kernel, call
        /// <see cref="Float8E5M2Extensions.RawBitsToFloat(int)"/> instead - building a sub-word value
        /// from raw bits does not lower on the browser backends, whereas RawBitsToFloat is pure
        /// arithmetic that transpiles everywhere.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 FromRawBits(byte rawBits) => new Float8E5M2(rawBits);

        #endregion

        #region Properties

        /// <summary>The raw 8-bit code. Round-trips with <see cref="FromRawBits"/>; use to re-encode
        /// a decoded value back into packed storage.</summary>
#if !DEBUG
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        public byte RawValue { get; }

        #endregion

        #region IEquatable

        /// <summary>Returns true if the given E5M2 is equal to the current value.</summary>
        public readonly bool Equals(Float8E5M2 other) => this == other;

        #endregion

        #region IComparable

        /// <summary>Compares this E5M2 value to the given one (by float value).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int CompareTo(Float8E5M2 other) => ((float)this).CompareTo(other);

        #endregion

        #region Object

        /// <summary>Returns true if the given object is equal to the current value.</summary>
        public readonly override bool Equals(object? obj) =>
            obj is Float8E5M2 value && Equals(value);

        /// <summary>Returns the hash code of this value.</summary>
        public readonly override int GetHashCode() => RawValue;

        /// <summary>Returns the string representation of this value.</summary>
        public readonly override string ToString() => ((float)this).ToString();

        #endregion

        #region Operators

        /// <summary>Negates the given E5M2 value.</summary>
        [MathIntrinsic(MathIntrinsicKind.Neg)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 operator -(Float8E5M2 value) => Float8E5M2Extensions.Neg(value);

        /// <summary>Adds two E5M2 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Add)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 operator +(Float8E5M2 first, Float8E5M2 second) =>
            Float8E5M2Extensions.AddFP32(first, second);

        /// <summary>Subtracts two E5M2 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Sub)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 operator -(Float8E5M2 first, Float8E5M2 second) =>
            Float8E5M2Extensions.SubFP32(first, second);

        /// <summary>Multiplies two E5M2 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Mul)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 operator *(Float8E5M2 first, Float8E5M2 second) =>
            Float8E5M2Extensions.MulFP32(first, second);

        /// <summary>Divides two E5M2 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Div)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 operator /(Float8E5M2 first, Float8E5M2 second) =>
            Float8E5M2Extensions.DivFP32(first, second);

        /// <summary>Returns true if the two values are equal.</summary>
        [CompareIntrinisc(CompareKind.Equal)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Float8E5M2 first, Float8E5M2 second) =>
            (float)first == second;

        /// <summary>Returns true if the two values are not equal.</summary>
        [CompareIntrinisc(CompareKind.NotEqual, CompareFlags.UnsignedOrUnordered)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Float8E5M2 first, Float8E5M2 second) =>
            (float)first != second;

        /// <summary>Returns true if the first value is smaller than the second.</summary>
        [CompareIntrinisc(CompareKind.LessThan)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Float8E5M2 first, Float8E5M2 second) =>
            (float)first < second;

        /// <summary>Returns true if the first value is smaller than or equal to the second.</summary>
        [CompareIntrinisc(CompareKind.LessEqual)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Float8E5M2 first, Float8E5M2 second) =>
            (float)first <= second;

        /// <summary>Returns true if the first value is greater than the second.</summary>
        [CompareIntrinisc(CompareKind.GreaterThan)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Float8E5M2 first, Float8E5M2 second) =>
            (float)first > second;

        /// <summary>Returns true if the first value is greater than or equal to the second.</summary>
        [CompareIntrinisc(CompareKind.GreaterEqual)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Float8E5M2 first, Float8E5M2 second) =>
            (float)first >= second;

        /// <summary>Implicitly converts an E5M2 to a float.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator float(Float8E5M2 value) =>
            Float8E5M2Extensions.ConvertFloat8E5M2ToFloat(value);

        /// <summary>Implicitly converts an E5M2 to a double.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator double(Float8E5M2 value) => (float)value;

        /// <summary>Explicitly converts a float to an E5M2.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Float8E5M2(float value) =>
            Float8E5M2Extensions.ConvertFloatToFloat8E5M2(value);

        /// <summary>Explicitly converts a double to an E5M2.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Float8E5M2(double value) =>
            (Float8E5M2)(float)value;

        #endregion
    }

    /// <summary>
    /// Extension/implementation methods for the <see cref="Float8E5M2"/> type.
    /// </summary>
    public static partial class Float8E5M2Extensions
    {
        #region Constants

        // E5M2: 1 sign / 5 exponent / 2 mantissa, bias 15. f32: bias 127.
        private const byte SignBitMask = 0x80;
        private const byte ExponentMask = 0x7C;        // bits 6..2
        private const byte MantissaMask = 0x03;        // bits 1..0
        private const byte ExponentMantissaMask = ExponentMask | MantissaMask; // 0x7F

        #endregion

        #region Conversion

        /// <summary>
        /// Converts an E5M2 value to a float. Decodes the 1/5/2 fields, rebiases the exponent
        /// (15 -> 127) and places the 2 mantissa bits into the top of the f32 mantissa. Handles
        /// zero/subnormal/Inf/NaN per IEEE.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ConvertFloat8E5M2ToFloat(Float8E5M2 value) =>
            RawBitsToFloat(value.RawValue);

        /// <summary>
        /// Decodes a raw 8-bit E5M2 code (the low byte of <paramref name="rawBits"/>) directly to a
        /// float. THIS is the kernel-safe primitive for decoding packed FP8 storage: read the byte
        /// from your packed buffer, then call this - it does the verified decode as pure int/float
        /// arithmetic and transpiles on EVERY backend. Unlike <c>(float)FromRawBits(code)</c>, it
        /// never constructs the sub-word struct, so it avoids the browser backends' decoded-in-
        /// register model. On host it is identical to <c>(float)FromRawBits(code)</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RawBitsToFloat(int rawBits)
        {
            uint bits = (uint)(rawBits & 0xFF);
            uint sign = (bits & 0x80u) << 24;          // f32 sign bit
            uint exp = (bits >> 2) & 0x1Fu;            // 5-bit exponent
            uint mant = bits & 0x03u;                  // 2-bit mantissa

            if (exp == 0u)
            {
                // Zero or subnormal. Subnormal value = mant * 2^(1-15) * 2^-2 = mant * 2^-16.
                if (mant == 0u)
                    return Interop.IntAsFloat(sign);   // +-0
                // Normalize the subnormal into an f32 normal.
                uint e = 127u - 15u + 1u;              // start exponent for 2^(1-bias)
                uint m = mant;
                while ((m & 0x04u) == 0u)              // shift until the implicit 1 (bit 2) is set
                {
                    m <<= 1;
                    e -= 1u;
                }
                m &= 0x03u;                            // drop the implicit bit
                return Interop.IntAsFloat(sign | (e << 23) | (m << 21));
            }
            if (exp == 0x1Fu)
            {
                // Inf (mant==0) or NaN. Set all f32 exponent bits; carry mantissa for NaN.
                return Interop.IntAsFloat(sign | (0xFFu << 23) | (mant << 21));
            }
            // Normal: rebias exponent, shift the 2 mantissa bits to the top of the f32 mantissa.
            uint f32Exp = exp - 15u + 127u;
            return Interop.IntAsFloat(sign | (f32Exp << 23) | (mant << 21));
        }

        /// <summary>
        /// Converts a float to an E5M2 value using round-to-nearest-even, with IEEE
        /// overflow (-&gt; Inf), underflow (-&gt; subnormal/0) and NaN handling.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 ConvertFloatToFloat8E5M2(float value)
        {
            uint bits = Interop.FloatAsInt(value);
            uint sign = (bits >> 24) & 0x80u;          // E5M2 sign bit in place
            uint rest = bits & 0x7FFFFFFFu;            // |value| bits

            // NaN -> a quiet E5M2 NaN (force a mantissa bit, keep sign).
            if (rest > 0x7F800000u)
                return new Float8E5M2((byte)(sign | 0x7Fu));
            // +-Inf -> E5M2 Inf.
            if (rest == 0x7F800000u)
                return new Float8E5M2((byte)(sign | 0x7Cu));

            int f32Exp = (int)((rest >> 23) & 0xFFu);
            uint f32Mant = rest & 0x7FFFFFu;
            // Unbiased exponent. E5M2 exponent range (normal): -14..15 (bias 15).
            int e = f32Exp - 127;

            if (e > 15)
            {
                // Overflow -> Inf (IEEE).
                return new Float8E5M2((byte)(sign | 0x7Cu));
            }
            if (e < -14)
            {
                // Subnormal or zero. Build the 23-bit significand (with implicit 1 for normals)
                // then shift right to the subnormal position and round-to-nearest-even.
                if (f32Exp == 0)
                    return new Float8E5M2((byte)sign); // f32 zero/subnormal -> E5M2 +-0
                uint signif = f32Mant | 0x800000u;      // implicit 1
                int shift = (-14 - e) + 21;             // align to the 2-bit subnormal field
                if (shift > 31)
                    return new Float8E5M2((byte)sign);  // underflows to +-0
                uint m = signif >> shift;
                // Round to nearest even using the bits shifted out.
                uint roundBit = (signif >> (shift - 1)) & 1u;
                uint sticky = (signif & ((1u << (shift - 1)) - 1u)) != 0u ? 1u : 0u;
                if (roundBit == 1u && (sticky == 1u || (m & 1u) == 1u))
                    m += 1u;                            // may carry into exp=1 (smallest normal) - correct
                return new Float8E5M2((byte)(sign | (m & 0x03u) | ((m >> 2) << 2)));
            }

            // Normal range. Rebias and round the mantissa from 23 to 2 bits (RNE).
            uint mant23 = f32Mant;
            uint mant2 = mant23 >> 21;                  // top 2 bits
            uint round = (mant23 >> 20) & 1u;           // first dropped bit
            uint stick = (mant23 & 0xFFFFFu) != 0u ? 1u : 0u;
            uint eField = (uint)(e + 15);
            uint outBits = (eField << 2) | mant2;
            if (round == 1u && (stick == 1u || (mant2 & 1u) == 1u))
            {
                outBits += 1u;                          // ties-to-even; may carry into the exponent
                // If mantissa carried past 2 bits, outBits already rolled the exponent up; if the
                // exponent overflowed to 0x1F it becomes Inf, which is the correct IEEE result.
            }
            return new Float8E5M2((byte)(sign | (outBits & 0x7Fu)));
        }

        /// <summary>
        /// Converts a float to E5M2 using the SATURATING convention: finite overflow clamps to
        /// +-57344 (max normal) instead of producing +-Inf; +-Inf -&gt; +-Inf; NaN -&gt; NaN. The
        /// NVIDIA Transformer Engine / OCP saturating cast. Composed of existing intrinsics (the
        /// default cast + a bit-level finite check + a max-finite-constant cast), so it transpiles
        /// with no per-backend codegen. The finite test is a BIT check (exponent != all-ones), NOT a
        /// float compare against Inf - those are unreliable on WebGL.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 FromSingleSaturating(float value)
        {
            // Clamp finite |value| above max-finite (57344) to +-57344; +-Inf and NaN fall through to
            // the default cast (-> Inf / NaN). Computed from the INPUT only (bit-level finite check +
            // finite-vs-finite threshold compare + max-finite-constant cast) - never reads the result's
            // storage bits (the value is f32 in-register on the GPU backends).
            bool finite = (Interop.FloatAsInt(value) & 0x7FFFFFFF) < 0x7F800000;
            if (finite && value > 57344f)
                return (Float8E5M2)57344f;
            if (finite && value < -57344f)
                return (Float8E5M2)(-57344f);
            return (Float8E5M2)value;
        }

        #endregion

        #region Predicates

        /// <summary>Negates the given E5M2 value (flip the sign bit).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 Neg(Float8E5M2 value) =>
            new Float8E5M2((byte)(value.RawValue ^ SignBitMask));

        /// <summary>Returns the absolute value (clear the sign bit).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 Abs(Float8E5M2 value) =>
            new Float8E5M2((byte)(value.RawValue & ExponentMantissaMask));

        /// <summary>Returns true if the value is NaN (exp all ones, mantissa != 0).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(Float8E5M2 value) =>
            (value.RawValue & ExponentMantissaMask) > ExponentMask;

        /// <summary>Returns true if the value is +-0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(Float8E5M2 value) =>
            (value.RawValue & ExponentMantissaMask) == 0;

        /// <summary>Returns true if the value is +infinity.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPositiveInfinity(Float8E5M2 value) =>
            value.RawValue == 0x7C;

        /// <summary>Returns true if the value is -infinity.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(Float8E5M2 value) =>
            value.RawValue == 0xFC;

        /// <summary>Returns true if the value is +-infinity.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(Float8E5M2 value) =>
            (value.RawValue & ExponentMantissaMask) == ExponentMask;

        /// <summary>Returns true if the value is finite (not NaN, not Inf).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(Float8E5M2 value) =>
            Bitwise.And(!IsNaN(value), !IsInfinity(value));

        #endregion

        #region FP32 Implementation Methods

        /// <summary>Implements an E5M2 addition using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 AddFP32(Float8E5M2 first, Float8E5M2 second) =>
            (Float8E5M2)((float)first + second);

        /// <summary>Implements an E5M2 subtraction using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 SubFP32(Float8E5M2 first, Float8E5M2 second) =>
            (Float8E5M2)((float)first - second);

        /// <summary>Implements an E5M2 multiplication using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 MulFP32(Float8E5M2 first, Float8E5M2 second) =>
            (Float8E5M2)((float)first * second);

        /// <summary>Implements an E5M2 division using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 DivFP32(Float8E5M2 first, Float8E5M2 second) =>
            (Float8E5M2)((float)first / second);

        /// <summary>Implements an E5M2 fused multiply-add using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 FmaFP32(Float8E5M2 first, Float8E5M2 second, Float8E5M2 third) =>
            (Float8E5M2)((float)first * second + third);

        #endregion
    }
}
