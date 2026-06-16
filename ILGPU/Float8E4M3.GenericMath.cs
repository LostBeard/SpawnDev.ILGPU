// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: Float8E4M3.GenericMath.cs
//
// C# 11 generic-math (System.Numerics.INumber) support for the kernel-native Float8E4M3 type.
//
// Float8E4M3's arithmetic/comparison operators are FP32-based and tagged
// [MathIntrinsic]/[CompareIntrinisc], so ILGPU's frontend transpiles them on all backends, and
// the frontend resolves static-abstract generic-math dispatch to these concrete operators. By
// implementing INumber<Float8E4M3>, a generic-math kernel (where T : INumber<T>) binds to THESE
// transpilable operators - the same approach that makes ILGPU.Half and BFloat16 work in
// generic-math kernels.
//
// Design mirrors BFloat16.GenericMath / Float8E5M2.GenericMath. The one structural difference:
// E4M3 (E4M3FN) has NO infinities - the only non-finite value is NaN. So the three Inf
// predicates INumberBase requires (IsInfinity / IsPositiveInfinity / IsNegativeInfinity) are
// always false and are supplied HERE (the base struct omits them by design), unlike E5M2/bf16
// which carry them as public static methods.
// ---------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    public readonly partial struct Float8E4M3 : INumber<Float8E4M3>, ISignedNumber<Float8E4M3>
    {
        #region IComparable (non-generic - required by INumber)

        /// <summary>Compares this value to another object (non-generic <see cref="IComparable"/>).</summary>
        public readonly int CompareTo(object? obj) =>
            obj is null ? 1
            : obj is Float8E4M3 other ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(Float8E4M3)}.", nameof(obj));

        #endregion

        #region Identities

        /// <summary>The additive identity (0).</summary>
        public static Float8E4M3 AdditiveIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E4M3)0.0f;
        }

        /// <summary>The multiplicative identity (1).</summary>
        public static Float8E4M3 MultiplicativeIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E4M3)1.0f;
        }

        /// <summary>The value negative one (-1).</summary>
        static Float8E4M3 ISignedNumber<Float8E4M3>.NegativeOne
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E4M3)(-1.0f);
        }

        static Float8E4M3 INumberBase<Float8E4M3>.One
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E4M3)1.0f;
        }

        static Float8E4M3 INumberBase<Float8E4M3>.Zero
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E4M3)0.0f;
        }

        static int INumberBase<Float8E4M3>.Radix => 2;

        #endregion

        #region Extra operators (the ones not already on Float8E4M3)

        /// <summary>Computes the remainder (a % b).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 operator %(Float8E4M3 left, Float8E4M3 right) =>
            (Float8E4M3)((float)left % (float)right);

        /// <summary>Returns the value unchanged (unary plus).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 operator +(Float8E4M3 value) => value;

        /// <summary>Increments the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 operator ++(Float8E4M3 value) => (Float8E4M3)((float)value + 1.0f);

        /// <summary>Decrements the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 operator --(Float8E4M3 value) => (Float8E4M3)((float)value - 1.0f);

        #endregion

        #region Inf predicates (E4M3 has NO infinities - always false; base struct omits these)

        /// <summary>Always false - E4M3 (E4M3FN) has no infinities.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(Float8E4M3 value) => false;

        /// <summary>Always false - E4M3 (E4M3FN) has no infinities.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPositiveInfinity(Float8E4M3 value) => false;

        /// <summary>Always false - E4M3 (E4M3FN) has no infinities.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(Float8E4M3 value) => false;

        #endregion

        #region INumberBase predicates (the ones not already on Float8E4M3 via static methods)

        static bool INumberBase<Float8E4M3>.IsCanonical(Float8E4M3 value) => true;
        static bool INumberBase<Float8E4M3>.IsComplexNumber(Float8E4M3 value) => false;
        static bool INumberBase<Float8E4M3>.IsImaginaryNumber(Float8E4M3 value) => false;
        static bool INumberBase<Float8E4M3>.IsRealNumber(Float8E4M3 value) => !IsNaN(value);
        static bool INumberBase<Float8E4M3>.IsInteger(Float8E4M3 value) => float.IsInteger((float)value);
        static bool INumberBase<Float8E4M3>.IsEvenInteger(Float8E4M3 value) => float.IsEvenInteger((float)value);
        static bool INumberBase<Float8E4M3>.IsOddInteger(Float8E4M3 value) => float.IsOddInteger((float)value);
        static bool INumberBase<Float8E4M3>.IsNegative(Float8E4M3 value) => float.IsNegative((float)value);
        static bool INumberBase<Float8E4M3>.IsPositive(Float8E4M3 value) => float.IsPositive((float)value);
        static bool INumberBase<Float8E4M3>.IsNormal(Float8E4M3 value) => float.IsNormal((float)value);
        static bool INumberBase<Float8E4M3>.IsSubnormal(Float8E4M3 value) => float.IsSubnormal((float)value);
        static bool INumberBase<Float8E4M3>.IsZero(Float8E4M3 value) => IsZero(value);

        #endregion

        #region INumberBase magnitude / INumber min-max-clamp-sign

        static Float8E4M3 INumberBase<Float8E4M3>.MaxMagnitude(Float8E4M3 x, Float8E4M3 y) =>
            (Float8E4M3)MathF.MaxMagnitude((float)x, (float)y);
        static Float8E4M3 INumberBase<Float8E4M3>.MaxMagnitudeNumber(Float8E4M3 x, Float8E4M3 y) =>
            (Float8E4M3)MaxMagnitudeNumberF((float)x, (float)y);
        static Float8E4M3 INumberBase<Float8E4M3>.MinMagnitude(Float8E4M3 x, Float8E4M3 y) =>
            (Float8E4M3)MathF.MinMagnitude((float)x, (float)y);
        static Float8E4M3 INumberBase<Float8E4M3>.MinMagnitudeNumber(Float8E4M3 x, Float8E4M3 y) =>
            (Float8E4M3)MinMagnitudeNumberF((float)x, (float)y);

        /// <summary>Clamps a value to the inclusive [min, max] range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 Clamp(Float8E4M3 value, Float8E4M3 min, Float8E4M3 max) =>
            (Float8E4M3)Math.Clamp((float)value, (float)min, (float)max);

        /// <summary>Copies the sign of <paramref name="sign"/> onto <paramref name="value"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 CopySign(Float8E4M3 value, Float8E4M3 sign) =>
            (Float8E4M3)MathF.CopySign((float)value, (float)sign);

        /// <summary>Returns the larger of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 Max(Float8E4M3 x, Float8E4M3 y) => (Float8E4M3)MathF.Max((float)x, (float)y);

        /// <summary>Returns the smaller of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E4M3 Min(Float8E4M3 x, Float8E4M3 y) => (Float8E4M3)MathF.Min((float)x, (float)y);

        static Float8E4M3 INumber<Float8E4M3>.MaxNumber(Float8E4M3 x, Float8E4M3 y) =>
            (Float8E4M3)MaxMagnitudeNumberF((float)x, (float)y) == x || (float)x >= (float)y ? x : y;
        static Float8E4M3 INumber<Float8E4M3>.MinNumber(Float8E4M3 x, Float8E4M3 y) =>
            (float)x <= (float)y || float.IsNaN((float)y) ? x : y;

        /// <summary>Returns the sign of the value (-1, 0, or +1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(Float8E4M3 value) => MathF.Sign((float)value);

        // Plain-float magnitude helpers matching INumberBase's "Number" (NaN-suppressing) semantics.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float MaxMagnitudeNumberF(float x, float y)
        {
            float ax = MathF.Abs(x), ay = MathF.Abs(y);
            if (ax > ay || float.IsNaN(ay)) return x;
            if (ax == ay) return float.IsNegative(x) ? y : x;
            return y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float MinMagnitudeNumberF(float x, float y)
        {
            float ax = MathF.Abs(x), ay = MathF.Abs(y);
            if (ax < ay || float.IsNaN(ay)) return x;
            if (ax == ay) return float.IsNegative(x) ? x : y;
            return y;
        }

        #endregion

        #region Conversions (host-only - never reached inside a kernel)

        static bool INumberBase<Float8E4M3>.TryConvertFromChecked<TOther>(TOther value, out Float8E4M3 result)
        {
            result = (Float8E4M3)(float)double.CreateChecked(value);
            return true;
        }

        static bool INumberBase<Float8E4M3>.TryConvertFromSaturating<TOther>(TOther value, out Float8E4M3 result)
        {
            result = (Float8E4M3)(float)double.CreateSaturating(value);
            return true;
        }

        static bool INumberBase<Float8E4M3>.TryConvertFromTruncating<TOther>(TOther value, out Float8E4M3 result)
        {
            result = (Float8E4M3)(float)double.CreateTruncating(value);
            return true;
        }

        static bool INumberBase<Float8E4M3>.TryConvertToChecked<TOther>(Float8E4M3 value, out TOther result)
        {
            result = TOther.CreateChecked((float)value);
            return true;
        }

        static bool INumberBase<Float8E4M3>.TryConvertToSaturating<TOther>(Float8E4M3 value, out TOther result)
        {
            result = TOther.CreateSaturating((float)value);
            return true;
        }

        static bool INumberBase<Float8E4M3>.TryConvertToTruncating<TOther>(Float8E4M3 value, out TOther result)
        {
            result = TOther.CreateTruncating((float)value);
            return true;
        }

        #endregion

        #region Parsing / formatting (host-only - never reached inside a kernel)

        /// <summary>Parses an E4M3 value from a string.</summary>
        public static Float8E4M3 Parse(string s, IFormatProvider? provider) =>
            (Float8E4M3)float.Parse(s, provider);

        /// <summary>Parses an E4M3 value from a span.</summary>
        public static Float8E4M3 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            (Float8E4M3)float.Parse(s, provider);

        static Float8E4M3 INumberBase<Float8E4M3>.Parse(string s, NumberStyles style, IFormatProvider? provider) =>
            (Float8E4M3)float.Parse(s, style, provider);

        static Float8E4M3 INumberBase<Float8E4M3>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) =>
            (Float8E4M3)float.Parse(s, style, provider);

        /// <summary>Tries to parse an E4M3 value from a string.</summary>
        public static bool TryParse(string? s, IFormatProvider? provider, out Float8E4M3 result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (Float8E4M3)f;
            return ok;
        }

        /// <summary>Tries to parse an E4M3 value from a span.</summary>
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Float8E4M3 result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (Float8E4M3)f;
            return ok;
        }

        static bool INumberBase<Float8E4M3>.TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Float8E4M3 result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (Float8E4M3)f;
            return ok;
        }

        static bool INumberBase<Float8E4M3>.TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out Float8E4M3 result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (Float8E4M3)f;
            return ok;
        }

        /// <summary>Formats this E4M3 value into a span.</summary>
        public bool TryFormat(
            Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
            ((float)this).TryFormat(destination, out charsWritten, format, provider);

        /// <summary>Formats this E4M3 value using the given format.</summary>
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            ((float)this).ToString(format, formatProvider);

        #endregion
    }
}
