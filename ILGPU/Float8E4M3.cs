// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: Float8E4M3.cs
//
// The kernel-native 8-bit floating-point type in the OCP "E4M3" layout (E4M3FN, the "finite"
// ML variant): 1 sign / 4 exponent / 3 mantissa bits, exponent bias 7. UNLIKE IEEE it has NO
// infinities - the only non-finite value is NaN at 0x7F / 0xFF (S.1111.111). Max finite
// magnitude is 448 (S.1111.110). It is the forward/inference format of the standard FP8
// training recipe (E4M3 forward, E5M2 backward): it trades dynamic range for an extra mantissa
// bit vs E5M2, which is what forward activations/weights want.
//
// OVERFLOW CONVENTION (verified vs the ml_dtypes reference, `DemoConsole -- fp8-oracle` -
// ml_dtypes is the impl PyTorch / JAX float8_e4m3fn share). E4M3 has two real-world conventions;
// the conversion is otherwise bit-exact to the reference (decode 0/256, encode rounding/subnormal
// 0 divergences across 1099 probes):
//   * fn / non-saturating = the DEFAULT (the cast operator, FromSingleFn, the IR-level convert all
//     FP8 paths share): finite overflow AND +-Inf -> NaN; NaN -> NaN. Bit-exact to PyTorch/JAX/
//     ml_dtypes float8_e4m3fn - the dtype this layout is named after, so this is the correct
//     default for reference-matching ML.
//   * SATURATING (opt-in via FromSingleSaturating / FromSingle(x, saturate:true)): finite overflow
//     clamps to +-448; +-Inf -> NaN; NaN -> NaN. Matches the NVIDIA Transformer Engine saturating
//     cast / OCP saturating-forward mode - use it when you want overflow clamped instead of NaN.
// The two agree everywhere except |x|>464 (the region that rounds up past the 448 slot): fn gives
// NaN, saturating gives +-448. Every REPRESENTABLE value round-trips exactly under both (fp8-verify).
//
// Modeled on ILGPU.Half / BFloat16 / Float8E5M2: FP32-based [MathIntrinsic]/[CompareIntrinisc]/
// [ConvertIntrinisc] operators (transpiled on every backend). 1-byte storage.
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
    /// An 8-bit floating-point value in OCP E4M3 (E4M3FN) layout (1 sign, 4 exponent, 3 mantissa
    /// bits, bias 7). Finite: NO infinities, the only NaN is 0x7F/0xFF, max magnitude 448. The
    /// FP8 forward/inference format.
    /// </summary>
    [Serializable]
    public readonly partial struct Float8E4M3 :
        IEquatable<Float8E4M3>, IComparable<Float8E4M3>
    {
        #region Static

        /// <summary>Returns the absolute value of the given E4M3 value.</summary>
        [MathIntrinsic(MathIntrinsicKind.Abs)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 Abs(Float8E4M3 value) => Float8E4M3Extensions.Abs(value);

        /// <summary>Returns true if the given E4M3 value represents a NaN value.</summary>
        [MathIntrinsic(MathIntrinsicKind.IsNaNF)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(Float8E4M3 value) => Float8E4M3Extensions.IsNaN(value);

        /// <summary>Returns true if the given E4M3 value represents 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(Float8E4M3 value) => Float8E4M3Extensions.IsZero(value);

        /// <summary>Returns true if the given E4M3 value represents a finite number (always, unless NaN).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(Float8E4M3 value) => Float8E4M3Extensions.IsFinite(value);

        /// <summary>
        /// Converts a float to E4M3 with a selectable overflow convention. When
        /// <paramref name="saturate"/> is false (the DEFAULT behavior, matching the cast operator):
        /// finite overflow and +-Inf map to NaN - bit-exact to PyTorch/JAX/ml_dtypes float8_e4m3fn.
        /// When true: finite overflow clamps to +-448 (NVIDIA Transformer Engine / OCP saturating cast).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 FromSingle(float value, bool saturate) =>
            saturate ? Float8E4M3Extensions.FromSingleSaturating(value)
                     : Float8E4M3Extensions.ConvertFloatToFloat8E4M3(value);

        /// <summary>
        /// Converts a float to E4M3 using the fn convention - finite overflow AND +-Inf map to NaN;
        /// NaN -> NaN. This is what the explicit cast operator does. Bit-exact to PyTorch/JAX/
        /// ml_dtypes float8_e4m3fn (the layout this type is named after). Use the explicit name when
        /// reading reference FP8 (PyTorch checkpoints, oracles) and you want it unambiguous.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 FromSingleFn(float value) =>
            Float8E4M3Extensions.ConvertFloatToFloat8E4M3(value);

        /// <summary>
        /// Converts a float to E4M3 using the SATURATING convention: finite overflow clamps to
        /// +-448; +-Inf -> NaN; NaN -> NaN. Matches the NVIDIA Transformer Engine saturating cast /
        /// OCP saturating-forward mode. Use this when you want overflow clamped rather than
        /// NaN-poisoning a downstream reduction. NOT the default - the cast operator is fn.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 FromSingleSaturating(float value) =>
            Float8E4M3Extensions.FromSingleSaturating(value);

        #endregion

        #region Constants

        /// <summary>Represents a positive zero value (0x00).</summary>
        public static readonly Float8E4M3 Zero = new Float8E4M3(0x00);

        /// <summary>Represents the value one (1.0). exp=7 (bias), mant=0 -&gt; 0x38.</summary>
        public static readonly Float8E4M3 One = new Float8E4M3(0x38);

        /// <summary>The smallest positive subnormal (2^-9, 0x01).</summary>
        public static readonly Float8E4M3 Epsilon = new Float8E4M3(0x01);

        /// <summary>The largest finite value (448 = 1.75 * 2^8, exp=15 mant=6 -&gt; 0x7E). E4M3 has no Inf.</summary>
        public static readonly Float8E4M3 MaxValue = new Float8E4M3(0x7E);

        /// <summary>The smallest finite value (-448, 0xFE).</summary>
        public static readonly Float8E4M3 MinValue = new Float8E4M3(0xFE);

        /// <summary>The single NaN (exp all ones, mant all ones -&gt; 0x7F). E4M3 has no Inf.</summary>
        public static readonly Float8E4M3 NaN = new Float8E4M3(0x7F);

        #endregion

        #region Instance

        /// <summary>Constructs a new E4M3 value from its raw 8-bit pattern.</summary>
        internal Float8E4M3(byte rawValue)
        {
            RawValue = rawValue;
        }

        /// <summary>
        /// Constructs an E4M3 value directly from its raw 8-bit code. The inverse of
        /// <see cref="RawValue"/>. HOST-side / desktop factory for round-tripping packed storage; it
        /// does NOT round a float (pass a raw 0x00..0xFF code, not a numeric value). To decode a
        /// packed byte to float INSIDE a kernel, call
        /// <see cref="Float8E4M3Extensions.RawBitsToFloat(int)"/> instead - building a sub-word value
        /// from raw bits does not lower on the browser backends, whereas RawBitsToFloat is pure
        /// arithmetic that transpiles everywhere.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 FromRawBits(byte rawBits) => new Float8E4M3(rawBits);

        #endregion

        #region Properties

        /// <summary>The raw 8-bit code. Round-trips with <see cref="FromRawBits"/>; use to re-encode
        /// a decoded value back into packed storage.</summary>
#if !DEBUG
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        public byte RawValue { get; }

        #endregion

        #region IEquatable / IComparable / Object

        /// <summary>Returns true if the given E4M3 is equal to the current value.</summary>
        public readonly bool Equals(Float8E4M3 other) => this == other;

        /// <summary>Compares this E4M3 value to the given one (by float value).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int CompareTo(Float8E4M3 other) => ((float)this).CompareTo(other);

        /// <summary>Returns true if the given object is equal to the current value.</summary>
        public readonly override bool Equals(object? obj) =>
            obj is Float8E4M3 value && Equals(value);

        /// <summary>Returns the hash code of this value.</summary>
        public readonly override int GetHashCode() => RawValue;

        /// <summary>Returns the string representation of this value.</summary>
        public readonly override string ToString() => ((float)this).ToString();

        #endregion

        #region Operators

        /// <summary>Negates the given E4M3 value.</summary>
        [MathIntrinsic(MathIntrinsicKind.Neg)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 operator -(Float8E4M3 value) => Float8E4M3Extensions.Neg(value);

        /// <summary>Adds two E4M3 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Add)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 operator +(Float8E4M3 first, Float8E4M3 second) =>
            Float8E4M3Extensions.AddFP32(first, second);

        /// <summary>Subtracts two E4M3 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Sub)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 operator -(Float8E4M3 first, Float8E4M3 second) =>
            Float8E4M3Extensions.SubFP32(first, second);

        /// <summary>Multiplies two E4M3 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Mul)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 operator *(Float8E4M3 first, Float8E4M3 second) =>
            Float8E4M3Extensions.MulFP32(first, second);

        /// <summary>Divides two E4M3 values.</summary>
        [MathIntrinsic(MathIntrinsicKind.Div)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 operator /(Float8E4M3 first, Float8E4M3 second) =>
            Float8E4M3Extensions.DivFP32(first, second);

        /// <summary>Returns true if the two values are equal.</summary>
        [CompareIntrinisc(CompareKind.Equal)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Float8E4M3 first, Float8E4M3 second) =>
            (float)first == second;

        /// <summary>Returns true if the two values are not equal.</summary>
        [CompareIntrinisc(CompareKind.NotEqual, CompareFlags.UnsignedOrUnordered)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Float8E4M3 first, Float8E4M3 second) =>
            (float)first != second;

        /// <summary>Returns true if the first value is smaller than the second.</summary>
        [CompareIntrinisc(CompareKind.LessThan)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Float8E4M3 first, Float8E4M3 second) =>
            (float)first < second;

        /// <summary>Returns true if the first value is smaller than or equal to the second.</summary>
        [CompareIntrinisc(CompareKind.LessEqual)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Float8E4M3 first, Float8E4M3 second) =>
            (float)first <= second;

        /// <summary>Returns true if the first value is greater than the second.</summary>
        [CompareIntrinisc(CompareKind.GreaterThan)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Float8E4M3 first, Float8E4M3 second) =>
            (float)first > second;

        /// <summary>Returns true if the first value is greater than or equal to the second.</summary>
        [CompareIntrinisc(CompareKind.GreaterEqual)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Float8E4M3 first, Float8E4M3 second) =>
            (float)first >= second;

        /// <summary>Implicitly converts an E4M3 to a float.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator float(Float8E4M3 value) =>
            Float8E4M3Extensions.ConvertFloat8E4M3ToFloat(value);

        /// <summary>Implicitly converts an E4M3 to a double.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator double(Float8E4M3 value) => (float)value;

        /// <summary>Explicitly converts a float to an E4M3.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Float8E4M3(float value) =>
            Float8E4M3Extensions.ConvertFloatToFloat8E4M3(value);

        /// <summary>Explicitly converts a double to an E4M3.</summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Float8E4M3(double value) =>
            (Float8E4M3)(float)value;

        #endregion
    }

    /// <summary>
    /// Extension/implementation methods for the <see cref="Float8E4M3"/> type.
    /// </summary>
    public static partial class Float8E4M3Extensions
    {
        #region Constants

        // E4M3: 1 sign / 4 exponent / 3 mantissa, bias 7. NaN = 0x7F/0xFF. Max finite = 448 (0x7E).
        private const byte SignBitMask = 0x80;
        private const byte MagnitudeMask = 0x7F;        // exponent+mantissa
        private const byte NaNMagnitude = 0x7F;         // exp=0xF, mant=0x7 (the only NaN); fn overflow target

        #endregion

        #region Conversion

        /// <summary>Converts an E4M3 value to a float (rebias 7 -&gt; 127; 3 mantissa bits; no Inf).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ConvertFloat8E4M3ToFloat(Float8E4M3 value) =>
            RawBitsToFloat(value.RawValue);

        /// <summary>
        /// Decodes a raw 8-bit E4M3 code (the low byte of <paramref name="rawBits"/>) directly to a
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
            uint sign = (bits & 0x80u) << 24;
            uint exp = (bits >> 3) & 0x0Fu;
            uint mant = bits & 0x07u;

            if ((bits & 0x7Fu) == 0x7Fu)                // the single NaN (exp=0xF, mant=0x7)
                return Interop.IntAsFloat(sign | 0x7FC00000u);
            if (exp == 0u)
            {
                if (mant == 0u)
                    return Interop.IntAsFloat(sign);    // +-0
                // Subnormal: value = mant * 2^(1-7) * 2^-3 = mant * 2^-9. Normalize into an f32 normal.
                uint e = 127u - 7u + 1u;
                uint m = mant;
                while ((m & 0x08u) == 0u)               // shift until the implicit 1 (bit 3) is set
                {
                    m <<= 1;
                    e -= 1u;
                }
                m &= 0x07u;
                return Interop.IntAsFloat(sign | (e << 23) | (m << 20));
            }
            // Normal: rebias, place the 3 mantissa bits at the top of the f32 mantissa.
            uint f32Exp = exp - 7u + 127u;
            return Interop.IntAsFloat(sign | (f32Exp << 23) | (mant << 20));
        }

        /// <summary>
        /// Converts a float to an E4M3 value using round-to-nearest-even, the fn (float8_e4m3fn)
        /// convention: finite overflow AND +-Inf map to NaN; NaN -&gt; NaN. This is the DEFAULT
        /// (what the cast operator does) and is bit-exact to PyTorch/JAX/ml_dtypes float8_e4m3fn
        /// (verified, <c>DemoConsole -- fp8-oracle</c>). For the saturating (clamp-to-+-448)
        /// convention use <see cref="FromSingleSaturating"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 ConvertFloatToFloat8E4M3(float value)
        {
            uint bits = Interop.FloatAsInt(value);
            uint sign = (bits >> 24) & 0x80u;
            uint rest = bits & 0x7FFFFFFFu;

            // NaN or Inf input -> NaN (E4M3 has no Inf).
            if (rest >= 0x7F800000u)
                return new Float8E4M3((byte)(sign | NaNMagnitude));

            int f32Exp = (int)((rest >> 23) & 0xFFu);
            uint f32Mant = rest & 0x7FFFFFu;
            int e = f32Exp - 127;                        // unbiased

            // E4M3 normal exponent range: -6..8 (bias 7); max finite magnitude 448 at e=8, mant=6.
            // fn: only e>8 is unconditional overflow -> NaN. e==8 falls through to RNE rounding,
            // which gives 448 for (448,464] (rounds down) and NaN for >464 (rounds up past the 448
            // slot) - handled by the post-round clamp below. (Saturating used e==8 && mant>0x600000
            // to clamp everything above 448 to 448; fn must NOT - 449 rounds to 448, not NaN.)
            if (e > 8)
            {
                return new Float8E4M3((byte)(sign | NaNMagnitude));
            }
            if (e < -6)
            {
                // Subnormal or zero.
                if (f32Exp == 0)
                    return new Float8E4M3((byte)sign);   // f32 zero/subnormal -> +-0
                uint signif = f32Mant | 0x800000u;       // implicit 1
                int shift = (-6 - e) + 20;               // align to the 3-bit subnormal field
                if (shift > 31)
                    return new Float8E4M3((byte)sign);   // underflow -> +-0
                uint m = signif >> shift;
                uint roundBit = (signif >> (shift - 1)) & 1u;
                uint sticky = (signif & ((1u << (shift - 1)) - 1u)) != 0u ? 1u : 0u;
                if (roundBit == 1u && (sticky == 1u || (m & 1u) == 1u))
                    m += 1u;                             // may carry up to the smallest normal - correct
                return new Float8E4M3((byte)(sign | (m & 0x7Fu)));
            }

            // Normal range. Rebias and round the mantissa 23 -> 3 bits (RNE).
            uint mant3 = f32Mant >> 20;                  // top 3 bits
            uint round = (f32Mant >> 19) & 1u;           // first dropped bit
            uint stick = (f32Mant & 0x7FFFFu) != 0u ? 1u : 0u;
            uint eField = (uint)(e + 7);
            uint outBits = (eField << 3) | mant3;
            if (round == 1u && (stick == 1u || (mant3 & 1u) == 1u))
                outBits += 1u;                           // ties-to-even; may carry into the exponent
            // fn: a rounded result that reaches the 0x7F slot (480) or carries past it (0x80) is
            // overflow -> NaN. Check the FULL outBits (not masked) so the 0x80 carry is caught;
            // 256..448 (0x78..0x7E) stay finite, 0x7F/0x80 -> NaN.
            if (outBits >= NaNMagnitude)
                outBits = NaNMagnitude;
            return new Float8E4M3((byte)(sign | (outBits & 0x7Fu)));
        }

        /// <summary>
        /// Converts a float to E4M3 using the SATURATING convention: finite overflow clamps to
        /// +-448; +-Inf -> NaN; NaN -> NaN. Matches the NVIDIA Transformer Engine saturating cast /
        /// OCP saturating-forward mode. Composed only of existing intrinsics (compare + the fn cast
        /// operator + cast-of-448-constant) so it transpiles on every backend with no per-backend
        /// conversion codegen. The fn cast and this agree everywhere except |value|>464 (finite):
        /// fn gives NaN, this gives +-448.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 FromSingleSaturating(float value)
        {
            // Redirect FINITE overflow (|value|>464, the region the fn cast maps to NaN) to +-448;
            // +-Inf and NaN fall through to the fn cast (-> NaN). The finite test is a BIT check
            // (exponent != all-ones) NOT a float compare against MaxValue - float Inf/NaN compares are
            // unreliable on WebGL (its GLSL clamps the 3.4e38 literal so Inf <= MaxValue read true ->
            // +-448 instead of NaN). 448 is exactly representable, so (Float8E4M3)448f = 0x7E.
            bool finite = (Interop.FloatAsInt(value) & 0x7FFFFFFF) < 0x7F800000;
            if (finite && value > 464.0f)
                return (Float8E4M3)448.0f;
            if (finite && value < -464.0f)
                return (Float8E4M3)(-448.0f);
            return (Float8E4M3)value;
        }

        #endregion

        #region Predicates

        /// <summary>Negates the given E4M3 value (flip the sign bit).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 Neg(Float8E4M3 value) =>
            new Float8E4M3((byte)(value.RawValue ^ SignBitMask));

        /// <summary>Returns the absolute value (clear the sign bit).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 Abs(Float8E4M3 value) =>
            new Float8E4M3((byte)(value.RawValue & MagnitudeMask));

        /// <summary>Returns true if the value is NaN (magnitude == 0x7F).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(Float8E4M3 value) =>
            (value.RawValue & MagnitudeMask) == NaNMagnitude;

        /// <summary>Returns true if the value is +-0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(Float8E4M3 value) =>
            (value.RawValue & MagnitudeMask) == 0;

        /// <summary>Returns true if the value is finite (E4M3 has no Inf, so finite == not NaN).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(Float8E4M3 value) => !IsNaN(value);

        #endregion

        #region FP32 Implementation Methods

        /// <summary>Implements an E4M3 addition using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 AddFP32(Float8E4M3 first, Float8E4M3 second) =>
            (Float8E4M3)((float)first + second);

        /// <summary>Implements an E4M3 subtraction using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 SubFP32(Float8E4M3 first, Float8E4M3 second) =>
            (Float8E4M3)((float)first - second);

        /// <summary>Implements an E4M3 multiplication using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 MulFP32(Float8E4M3 first, Float8E4M3 second) =>
            (Float8E4M3)((float)first * second);

        /// <summary>Implements an E4M3 division using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 DivFP32(Float8E4M3 first, Float8E4M3 second) =>
            (Float8E4M3)((float)first / second);

        /// <summary>Implements an E4M3 fused multiply-add using FP32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 FmaFP32(Float8E4M3 first, Float8E4M3 second, Float8E4M3 third) =>
            (Float8E4M3)((float)first * second + third);

        #endregion
    }
}
