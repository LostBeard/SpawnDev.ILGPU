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
// CONVENTION (flagged for ML-oracle confirmation, plan §9 risk #2 - confirm vs PyTorch
// float8_e4m3fn / NVIDIA Transformer Engine when wired into the ML lane): finite overflow
// SATURATES to +-448; a real +-Inf input maps to NaN (E4M3 has no Inf); NaN -> NaN. This
// matches the OCP/TE saturating-forward convention. Only the out-of-range INPUT behavior is
// convention-dependent; every REPRESENTABLE value round-trips exactly (verified by the CPU
// idempotence harness, `DemoConsole -- fp8-verify`).
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

        #endregion

        #region Properties

        /// <summary>Represents the raw 8-bit value.</summary>
#if !DEBUG
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
#endif
        internal byte RawValue { get; }

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
        private const byte NaNMagnitude = 0x7F;         // exp=0xF, mant=0x7 (the only NaN)
        private const byte MaxFiniteMagnitude = 0x7E;   // exp=0xF, mant=0x6 = 448

        #endregion

        #region Conversion

        /// <summary>Converts an E4M3 value to a float (rebias 7 -&gt; 127; 3 mantissa bits; no Inf).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ConvertFloat8E4M3ToFloat(Float8E4M3 value)
        {
            uint bits = value.RawValue;
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
        /// Converts a float to an E4M3 value using round-to-nearest-even. Finite overflow
        /// SATURATES to +-448; +-Inf -&gt; NaN (E4M3 has no Inf); NaN -&gt; NaN. (Convention flagged
        /// for ML-oracle confirmation - see the file header.)
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
            if (e > 8 || (e == 8 && f32Mant > 0x600000u))
            {
                // Finite overflow -> saturate to +-448 (no Inf in E4M3).
                return new Float8E4M3((byte)(sign | MaxFiniteMagnitude));
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
            // A carry that reaches 0x7F would be NaN; clamp finite overflow to 448 instead.
            if ((outBits & 0x7Fu) >= NaNMagnitude)
                outBits = MaxFiniteMagnitude;
            return new Float8E4M3((byte)(sign | (outBits & 0x7Fu)));
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
