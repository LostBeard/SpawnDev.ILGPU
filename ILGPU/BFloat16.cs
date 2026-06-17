// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: BFloat16.cs
//
// The kernel-native brain-floating-point (bfloat16) type. 1 sign / 8 exponent / 7 mantissa
// bits: it is literally the top 16 bits of an IEEE-754 fp32, so it shares fp32's full
// dynamic range (same 8 exponent bits) while trading mantissa precision. That "range beats
// precision" trade is the right one for ML weights/activations, where fp16's tiny range
// overflows/underflows but bf16 does not.
//
// Modeled exactly on ILGPU.Half: the arithmetic/comparison/conversion operators are FP32-based
// and tagged [MathIntrinsic]/[CompareIntrinisc]/[ConvertIntrinisc], so ILGPU's frontend
// transpiles them on every backend. Storage is a packed 16-bit value (sibling of Half's
// 2-byte sub-word path). Conversion is trivial vs Half (no exponent rebias, no table): bf16
// is a truncated fp32, so bf16->f32 is an exact zero-extend shift and f32->bf16 is a
// round-to-nearest-even truncate.
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
    /// A brain-floating-point value (bfloat16) with 1 sign, 8 exponent and 7 mantissa bits.
    /// Shares fp32's dynamic range; the top 16 bits of an fp32.
    /// </summary>
    [Serializable]
    public readonly partial struct BFloat16 : IEquatable<BFloat16>, IComparable<BFloat16>
    {
        #region Static

        /// <summary>
        /// Returns the absolute value of the given bfloat16 value.
        /// </summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>The absolute value.</returns>
        [MathIntrinsic(MathIntrinsicKind.Abs)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Abs(BFloat16 value) => BFloat16Extensions.Abs(value);

        /// <summary>
        /// Returns true if the given bfloat16 value represents a NaN value.
        /// </summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents a NaN value.</returns>
        [MathIntrinsic(MathIntrinsicKind.IsNaNF)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(BFloat16 value) => BFloat16Extensions.IsNaN(value);

        /// <summary>
        /// Returns true if the given bfloat16 value represents 0.
        /// </summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents 0.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(BFloat16 value) => BFloat16Extensions.IsZero(value);

        /// <summary>
        /// Returns true if the given bfloat16 value represents +infinity.
        /// </summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents +infinity.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPositiveInfinity(BFloat16 value) =>
            BFloat16Extensions.IsPositiveInfinity(value);

        /// <summary>
        /// Returns true if the given bfloat16 value represents -infinity.
        /// </summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents -infinity.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(BFloat16 value) =>
            BFloat16Extensions.IsNegativeInfinity(value);

        /// <summary>
        /// Returns true if the given bfloat16 value represents infinity.
        /// </summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents infinity.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(BFloat16 value) =>
            BFloat16Extensions.IsInfinity(value);

        /// <summary>
        /// Returns true if the given bfloat16 value represents a finite number.
        /// </summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents a finite number.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(BFloat16 value) => BFloat16Extensions.IsFinite(value);

        /// <summary>
        /// Converts a float to BFloat16 with a selectable overflow convention. When
        /// <paramref name="saturate"/> is false (the DEFAULT, matching the cast operator): finite
        /// overflow -&gt; +-Inf (IEEE round-to-nearest, bit-exact to ml_dtypes.bfloat16). When true:
        /// finite overflow clamps to the max normal magnitude (0x7F7F), the NVIDIA Transformer Engine
        /// / OCP saturating cast. (bf16 shares fp32's exponent range, so finite f32 inputs essentially
        /// never overflow bf16 - this is for API parity + completeness.)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 FromSingle(float value, bool saturate) =>
            saturate ? BFloat16Extensions.FromSingleSaturating(value)
                     : BFloat16Extensions.ConvertFloatToBFloat16(value);

        /// <summary>
        /// Converts a float to BFloat16 using the SATURATING convention: finite overflow clamps to
        /// the max normal magnitude instead of producing +-Inf; +-Inf -&gt; +-Inf; NaN -&gt; NaN.
        /// NVIDIA Transformer Engine / OCP mode. Use when you want overflow clamped. NOT the default.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 FromSingleSaturating(float value) =>
            BFloat16Extensions.FromSingleSaturating(value);

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new bfloat16 value.
        /// </summary>
        /// <param name="rawValue">The underlying raw value.</param>
        internal BFloat16(ushort rawValue)
        {
            RawValue = rawValue;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Represents the raw value.
        /// </summary>
#if !DEBUG
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        internal ushort RawValue { get; }

        #endregion

        #region IEquatable

        /// <summary>
        /// Returns true if the given bfloat16 is equal to the current bfloat16.
        /// </summary>
        /// <param name="other">The other bfloat16.</param>
        /// <returns>True, if the given value is equal to the current value.</returns>
        public readonly bool Equals(BFloat16 other) => this == other;

        #endregion

        #region IComparable

        /// <summary>
        /// Compares this bfloat16 value to the given bfloat16.
        /// </summary>
        /// <param name="other">The other bfloat16.</param>
        /// <returns>The result of the comparison.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int CompareTo(BFloat16 other) => ((float)this).CompareTo(other);

        #endregion

        #region Object

        /// <summary>
        /// Returns true if the given object is equal to the current bfloat16.
        /// </summary>
        /// <param name="obj">The other object.</param>
        /// <returns>True, if the given object is equal to the current value.</returns>
        public readonly override bool Equals(object? obj) =>
            obj is BFloat16 value && Equals(value);

        /// <summary>
        /// Returns the hash code of this bfloat16.
        /// </summary>
        /// <returns>The hash code of this value.</returns>
        public readonly override int GetHashCode() => RawValue;

        /// <summary>
        /// Returns the string representation of this bfloat16.
        /// </summary>
        /// <returns>The string representation of this value.</returns>
        public readonly override string ToString() => ((float)this).ToString();

        #endregion

        #region Operators

        /// <summary>
        /// Negates the given bfloat16 value.
        /// </summary>
        /// <param name="value">The bfloat16 value to negate.</param>
        /// <returns>The negated value.</returns>
        [MathIntrinsic(MathIntrinsicKind.Neg)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator -(BFloat16 value) => BFloat16Extensions.Neg(value);

        /// <summary>
        /// Adds two bfloat16 values.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>The resulting value.</returns>
        [MathIntrinsic(MathIntrinsicKind.Add)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator +(BFloat16 first, BFloat16 second) =>
            BFloat16Extensions.AddFP32(first, second);

        /// <summary>
        /// Subtracts two bfloat16 values.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>The resulting value.</returns>
        [MathIntrinsic(MathIntrinsicKind.Sub)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator -(BFloat16 first, BFloat16 second) =>
            BFloat16Extensions.SubFP32(first, second);

        /// <summary>
        /// Multiplies two bfloat16 values.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>The resulting value.</returns>
        [MathIntrinsic(MathIntrinsicKind.Mul)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator *(BFloat16 first, BFloat16 second) =>
            BFloat16Extensions.MulFP32(first, second);

        /// <summary>
        /// Divides two bfloat16 values.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>The resulting value.</returns>
        [MathIntrinsic(MathIntrinsicKind.Div)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator /(BFloat16 first, BFloat16 second) =>
            BFloat16Extensions.DivFP32(first, second);

        /// <summary>
        /// Returns true if the first and second bfloat16 represent the same value.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>True, if the first and second value are the same.</returns>
        [CompareIntrinisc(CompareKind.Equal)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(BFloat16 first, BFloat16 second) =>
            (float)first == second;

        /// <summary>
        /// Returns true if the first and second bfloat16 represent not the same value.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>True, if the first and second value are not the same.</returns>
        [CompareIntrinisc(CompareKind.NotEqual, CompareFlags.UnsignedOrUnordered)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(BFloat16 first, BFloat16 second) =>
            (float)first != second;

        /// <summary>
        /// Returns true if the first bfloat16 is smaller than the second bfloat16.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>True, if the first value is smaller than the second value.</returns>
        [CompareIntrinisc(CompareKind.LessThan)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(BFloat16 first, BFloat16 second) =>
            (float)first < second;

        /// <summary>
        /// Returns true if the first bfloat16 is smaller than or equal to the second.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>
        /// True, if the first value is smaller than or equal to the second value.
        /// </returns>
        [CompareIntrinisc(CompareKind.LessEqual)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(BFloat16 first, BFloat16 second) =>
            (float)first <= second;

        /// <summary>
        /// Returns true if the first bfloat16 is greater than the second bfloat16.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>True, if the first value is greater than the second value.</returns>
        [CompareIntrinisc(CompareKind.GreaterThan)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(BFloat16 first, BFloat16 second) =>
            (float)first > second;

        /// <summary>
        /// Returns true if the first bfloat16 is greater than or equal to the second.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>
        /// True, if the first value is greater than or equal to the second value.
        /// </returns>
        [CompareIntrinisc(CompareKind.GreaterEqual)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(BFloat16 first, BFloat16 second) =>
            (float)first >= second;

        /// <summary>
        /// Implicitly converts a bfloat16 to a float.
        /// </summary>
        /// <param name="value">The bfloat16 to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator float(BFloat16 value) =>
            BFloat16Extensions.ConvertBFloat16ToFloat(value);

        /// <summary>
        /// Implicitly converts a bfloat16 to a double.
        /// </summary>
        /// <param name="value">The bfloat16 to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator double(BFloat16 value) =>
            (float)value;

        /// <summary>
        /// Explicitly converts a float to a bfloat16.
        /// </summary>
        /// <param name="value">The float to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(float value) =>
            BFloat16Extensions.ConvertFloatToBFloat16(value);

        /// <summary>
        /// Explicitly converts a double to a bfloat16.
        /// </summary>
        /// <param name="value">The double to convert.</param>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator BFloat16(double value) =>
            (BFloat16)(float)value;

        #endregion
    }

    /// <summary>
    /// Extension/implementation methods for the <see cref="BFloat16"/> type.
    /// </summary>
    public static partial class BFloat16Extensions
    {
        #region Constants

        /// <summary>The bit mask of the sign bit.</summary>
        private const ushort SignBitMask = 0x8000;

        /// <summary>The bit mask of the exponent.</summary>
        private const ushort ExponentMask = 0x7F80;

        /// <summary>The bit mask of the mantissa.</summary>
        private const ushort MantissaMask = 0x007F;

        /// <summary>The bit mask of the exponent and the mantissa.</summary>
        private const ushort ExponentMantissaMask = ExponentMask | MantissaMask;

        #endregion

        #region Static

        /// <summary>
        /// Converts a bfloat16 value to a float value. Exact: bfloat16 is the top 16 bits
        /// of an fp32, so a zero-extending left shift reconstructs the fp32 bit-pattern.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>The converted float value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ConvertBFloat16ToFloat(BFloat16 value) =>
            Interop.IntAsFloat((uint)value.RawValue << 16);

        /// <summary>
        /// Converts a float value to a bfloat16 value using round-to-nearest-even.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>The converted bfloat16 value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 ConvertFloatToBFloat16(float value)
        {
            uint bits = Interop.FloatAsInt(value);

            // NaN must stay NaN. A plain truncate of a float NaN whose low 16 mantissa
            // bits carry the NaN-ness would collapse to infinity, so force a high mantissa
            // bit (and preserve the sign) to keep it a (quiet) NaN.
            if ((bits & 0x7FFFFFFFu) > 0x7F800000u)
                return new BFloat16((ushort)((bits >> 16) | 0x0040u));

            // Round to nearest, ties to even: add 0x7FFF plus the low bit of the result.
            uint lsb = (bits >> 16) & 1u;
            bits += 0x7FFFu + lsb;
            return new BFloat16((ushort)(bits >> 16));
        }

        /// <summary>
        /// Converts a float to BFloat16 using the SATURATING convention: finite overflow clamps to
        /// the max normal magnitude (0x7F7F) instead of producing +-Inf; +-Inf -&gt; +-Inf; NaN -&gt; NaN.
        /// NVIDIA Transformer Engine / OCP saturating cast. Composed of existing intrinsics (the
        /// default RNE cast + a bit-level finite check + a max-finite-constant cast), so it transpiles
        /// with no per-backend codegen. The finite test is a BIT check (exponent != all-ones), NOT a
        /// float compare against Inf - those are unreliable on WebGL. (A finite f32 only overflows
        /// bf16 when the top of the f32 range rounds the bf16 exponent up to Inf.)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 FromSingleSaturating(float value)
        {
            // Clamp finite |value| above max-finite (0x7F7F0000 as f32) to +-max-finite; +-Inf and NaN
            // fall through to the default cast (-> Inf / NaN). Computed from the INPUT only (bit-level
            // finite check + finite-vs-finite threshold compare + max-finite-constant cast) - never
            // reads the result's storage bits (the value is f32 in-register on the GPU backends).
            float maxFinite = Interop.IntAsFloat(0x7F7F0000u);      // largest finite bf16, as f32
            bool finite = (Interop.FloatAsInt(value) & 0x7FFFFFFF) < 0x7F800000;
            if (finite && value > maxFinite)
                return (BFloat16)maxFinite;
            if (finite && value < -maxFinite)
                return (BFloat16)(-maxFinite);
            return (BFloat16)value;
        }

        #endregion

        #region Predicates

        /// <summary>Negates the given bfloat16 value.</summary>
        /// <param name="value">The bfloat16 value to negate.</param>
        /// <returns>The negated value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Neg(BFloat16 value) =>
            new BFloat16((ushort)(value.RawValue ^ SignBitMask));

        /// <summary>Returns the absolute value of the given bfloat16 value.</summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>The absolute value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Abs(BFloat16 value) =>
            new BFloat16((ushort)(value.RawValue & ExponentMantissaMask));

        /// <summary>Returns true if the given bfloat16 value represents a NaN value.</summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents a NaN value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(BFloat16 value) =>
            (value.RawValue & ExponentMantissaMask) > ExponentMask;

        /// <summary>Returns true if the given bfloat16 value represents 0.</summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents 0.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsZero(BFloat16 value) =>
            (value.RawValue & ExponentMantissaMask) == 0;

        /// <summary>Returns true if the given bfloat16 value represents +infinity.</summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents +infinity.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPositiveInfinity(BFloat16 value) =>
            value == BFloat16.PositiveInfinity;

        /// <summary>Returns true if the given bfloat16 value represents -infinity.</summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents -infinity.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(BFloat16 value) =>
            value == BFloat16.NegativeInfinity;

        /// <summary>Returns true if the given bfloat16 value represents infinity.</summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents infinity.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(BFloat16 value) =>
            (value.RawValue & ExponentMantissaMask) == ExponentMask;

        /// <summary>Returns true if the given bfloat16 value represents a finite number.</summary>
        /// <param name="value">The bfloat16 value.</param>
        /// <returns>True, if the given value represents a finite number.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(BFloat16 value) =>
            Bitwise.And(!IsNaN(value), !IsInfinity(value));

        #endregion

        #region FP32 Implementation Methods

        /// <summary>Implements a bfloat16 addition using FP32.</summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>The resulting value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 AddFP32(BFloat16 first, BFloat16 second) =>
            (BFloat16)((float)first + second);

        /// <summary>Implements a bfloat16 subtraction using FP32.</summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>The resulting value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 SubFP32(BFloat16 first, BFloat16 second) =>
            (BFloat16)((float)first - second);

        /// <summary>Implements a bfloat16 multiplication using FP32.</summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>The resulting value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 MulFP32(BFloat16 first, BFloat16 second) =>
            (BFloat16)((float)first * second);

        /// <summary>Implements a bfloat16 division using FP32.</summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <returns>The resulting value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 DivFP32(BFloat16 first, BFloat16 second) =>
            (BFloat16)((float)first / second);

        /// <summary>Implements a bfloat16 fused multiply-add using FP32.</summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <param name="third">The third value.</param>
        /// <returns>The resulting value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 FmaFP32(BFloat16 first, BFloat16 second, BFloat16 third) =>
            (BFloat16)((float)first * second + third);

        #endregion
    }
}
