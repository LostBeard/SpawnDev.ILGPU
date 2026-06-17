// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: Float4E2M1.GenericMath.cs
//
// C# 11 generic-math (System.Numerics.INumber) support for the kernel-native Float4E2M1 type.
//
// Float4E2M1's arithmetic/comparison operators are FP32-based and tagged
// [MathIntrinsic]/[CompareIntrinisc], so ILGPU's frontend transpiles them on all backends, and
// the frontend resolves static-abstract generic-math dispatch to these concrete operators. By
// implementing INumber<Float4E2M1>, a generic-math kernel (where T : INumber<T>) binds to THESE
// transpilable operators - the same approach that makes ILGPU.Half, BFloat16, and the FP8 types
// work in generic-math kernels.
//
// Design mirrors Float8E4M3.GenericMath. The structural difference: E2M1 (E2M1FN) has NEITHER
// infinities NOR NaN - every one of its 16 codes is finite. So the INumberBase non-finite
// predicates (IsNaN / IsInfinity / IsPositiveInfinity / IsNegativeInfinity) are always false and
// are ALL supplied HERE (the base struct carries only IsFinite => true), unlike E4M3 (which still
// has NaN) or E5M2/bf16 (which have both Inf and NaN).
// ---------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    public readonly partial struct Float4E2M1 : INumber<Float4E2M1>, ISignedNumber<Float4E2M1>
    {
        #region IComparable (non-generic - required by INumber)

        /// <summary>Compares this value to another object (non-generic <see cref="IComparable"/>).</summary>
        public readonly int CompareTo(object? obj) =>
            obj is null ? 1
            : obj is Float4E2M1 other ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(Float4E2M1)}.", nameof(obj));

        #endregion

        #region Identities

        /// <summary>The additive identity (0).</summary>
        public static Float4E2M1 AdditiveIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float4E2M1)0.0f;
        }

        /// <summary>The multiplicative identity (1).</summary>
        public static Float4E2M1 MultiplicativeIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float4E2M1)1.0f;
        }

        /// <summary>The value negative one (-1).</summary>
        static Float4E2M1 ISignedNumber<Float4E2M1>.NegativeOne
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float4E2M1)(-1.0f);
        }

        static Float4E2M1 INumberBase<Float4E2M1>.One
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float4E2M1)1.0f;
        }

        static Float4E2M1 INumberBase<Float4E2M1>.Zero
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float4E2M1)0.0f;
        }

        static int INumberBase<Float4E2M1>.Radix => 2;

        #endregion

        #region Extra operators (the ones not already on Float4E2M1)

        /// <summary>Computes the remainder (a % b).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 operator %(Float4E2M1 left, Float4E2M1 right) =>
            (Float4E2M1)((float)left % (float)right);

        /// <summary>Returns the value unchanged (unary plus).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 operator +(Float4E2M1 value) => value;

        /// <summary>Increments the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 operator ++(Float4E2M1 value) => (Float4E2M1)((float)value + 1.0f);

        /// <summary>Decrements the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 operator --(Float4E2M1 value) => (Float4E2M1)((float)value - 1.0f);

        #endregion

        #region Non-finite predicates (E2M1 has NO NaN and NO infinities - always false; base struct omits these)

        /// <summary>Always false - E2M1 (E2M1FN) has no NaN encoding (every code is finite).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNaN(Float4E2M1 value) => false;

        /// <summary>Always false - E2M1 (E2M1FN) has no infinities.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInfinity(Float4E2M1 value) => false;

        /// <summary>Always false - E2M1 (E2M1FN) has no infinities.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPositiveInfinity(Float4E2M1 value) => false;

        /// <summary>Always false - E2M1 (E2M1FN) has no infinities.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNegativeInfinity(Float4E2M1 value) => false;

        #endregion

        #region INumberBase predicates (the ones not already on Float4E2M1 via static methods)

        static bool INumberBase<Float4E2M1>.IsCanonical(Float4E2M1 value) => true;
        static bool INumberBase<Float4E2M1>.IsComplexNumber(Float4E2M1 value) => false;
        static bool INumberBase<Float4E2M1>.IsImaginaryNumber(Float4E2M1 value) => false;
        static bool INumberBase<Float4E2M1>.IsRealNumber(Float4E2M1 value) => true;
        static bool INumberBase<Float4E2M1>.IsInteger(Float4E2M1 value) => float.IsInteger((float)value);
        static bool INumberBase<Float4E2M1>.IsEvenInteger(Float4E2M1 value) => float.IsEvenInteger((float)value);
        static bool INumberBase<Float4E2M1>.IsOddInteger(Float4E2M1 value) => float.IsOddInteger((float)value);
        static bool INumberBase<Float4E2M1>.IsNegative(Float4E2M1 value) => float.IsNegative((float)value);
        static bool INumberBase<Float4E2M1>.IsPositive(Float4E2M1 value) => float.IsPositive((float)value);
        static bool INumberBase<Float4E2M1>.IsNormal(Float4E2M1 value) => float.IsNormal((float)value);
        static bool INumberBase<Float4E2M1>.IsSubnormal(Float4E2M1 value) => float.IsSubnormal((float)value);
        static bool INumberBase<Float4E2M1>.IsZero(Float4E2M1 value) => IsZero(value);

        #endregion

        #region INumberBase magnitude / INumber min-max-clamp-sign

        static Float4E2M1 INumberBase<Float4E2M1>.MaxMagnitude(Float4E2M1 x, Float4E2M1 y) =>
            (Float4E2M1)MathF.MaxMagnitude((float)x, (float)y);
        static Float4E2M1 INumberBase<Float4E2M1>.MaxMagnitudeNumber(Float4E2M1 x, Float4E2M1 y) =>
            (Float4E2M1)MaxMagnitudeNumberF((float)x, (float)y);
        static Float4E2M1 INumberBase<Float4E2M1>.MinMagnitude(Float4E2M1 x, Float4E2M1 y) =>
            (Float4E2M1)MathF.MinMagnitude((float)x, (float)y);
        static Float4E2M1 INumberBase<Float4E2M1>.MinMagnitudeNumber(Float4E2M1 x, Float4E2M1 y) =>
            (Float4E2M1)MinMagnitudeNumberF((float)x, (float)y);

        /// <summary>Clamps a value to the inclusive [min, max] range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 Clamp(Float4E2M1 value, Float4E2M1 min, Float4E2M1 max) =>
            (Float4E2M1)Math.Clamp((float)value, (float)min, (float)max);

        /// <summary>Copies the sign of <paramref name="sign"/> onto <paramref name="value"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 CopySign(Float4E2M1 value, Float4E2M1 sign) =>
            (Float4E2M1)MathF.CopySign((float)value, (float)sign);

        /// <summary>Returns the larger of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 Max(Float4E2M1 x, Float4E2M1 y) => (Float4E2M1)MathF.Max((float)x, (float)y);

        /// <summary>Returns the smaller of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float4E2M1 Min(Float4E2M1 x, Float4E2M1 y) => (Float4E2M1)MathF.Min((float)x, (float)y);

        static Float4E2M1 INumber<Float4E2M1>.MaxNumber(Float4E2M1 x, Float4E2M1 y) =>
            (Float4E2M1)MaxMagnitudeNumberF((float)x, (float)y) == x || (float)x >= (float)y ? x : y;
        static Float4E2M1 INumber<Float4E2M1>.MinNumber(Float4E2M1 x, Float4E2M1 y) =>
            (float)x <= (float)y || float.IsNaN((float)y) ? x : y;

        /// <summary>Returns the sign of the value (-1, 0, or +1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(Float4E2M1 value) => MathF.Sign((float)value);

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

        static bool INumberBase<Float4E2M1>.TryConvertFromChecked<TOther>(TOther value, out Float4E2M1 result)
        {
            result = (Float4E2M1)(float)double.CreateChecked(value);
            return true;
        }

        static bool INumberBase<Float4E2M1>.TryConvertFromSaturating<TOther>(TOther value, out Float4E2M1 result)
        {
            result = (Float4E2M1)(float)double.CreateSaturating(value);
            return true;
        }

        static bool INumberBase<Float4E2M1>.TryConvertFromTruncating<TOther>(TOther value, out Float4E2M1 result)
        {
            result = (Float4E2M1)(float)double.CreateTruncating(value);
            return true;
        }

        static bool INumberBase<Float4E2M1>.TryConvertToChecked<TOther>(Float4E2M1 value, out TOther result)
        {
            result = TOther.CreateChecked((float)value);
            return true;
        }

        static bool INumberBase<Float4E2M1>.TryConvertToSaturating<TOther>(Float4E2M1 value, out TOther result)
        {
            result = TOther.CreateSaturating((float)value);
            return true;
        }

        static bool INumberBase<Float4E2M1>.TryConvertToTruncating<TOther>(Float4E2M1 value, out TOther result)
        {
            result = TOther.CreateTruncating((float)value);
            return true;
        }

        #endregion

        #region Parsing / formatting (host-only - never reached inside a kernel)

        /// <summary>Parses an E2M1 value from a string.</summary>
        public static Float4E2M1 Parse(string s, IFormatProvider? provider) =>
            (Float4E2M1)float.Parse(s, provider);

        /// <summary>Parses an E2M1 value from a span.</summary>
        public static Float4E2M1 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            (Float4E2M1)float.Parse(s, provider);

        static Float4E2M1 INumberBase<Float4E2M1>.Parse(string s, NumberStyles style, IFormatProvider? provider) =>
            (Float4E2M1)float.Parse(s, style, provider);

        static Float4E2M1 INumberBase<Float4E2M1>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) =>
            (Float4E2M1)float.Parse(s, style, provider);

        /// <summary>Tries to parse an E2M1 value from a string.</summary>
        public static bool TryParse(string? s, IFormatProvider? provider, out Float4E2M1 result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (Float4E2M1)f;
            return ok;
        }

        /// <summary>Tries to parse an E2M1 value from a span.</summary>
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Float4E2M1 result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (Float4E2M1)f;
            return ok;
        }

        static bool INumberBase<Float4E2M1>.TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Float4E2M1 result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (Float4E2M1)f;
            return ok;
        }

        static bool INumberBase<Float4E2M1>.TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out Float4E2M1 result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (Float4E2M1)f;
            return ok;
        }

        /// <summary>Formats this E2M1 value into a span.</summary>
        public bool TryFormat(
            Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
            ((float)this).TryFormat(destination, out charsWritten, format, provider);

        /// <summary>Formats this E2M1 value using the given format.</summary>
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            ((float)this).ToString(format, formatProvider);

        #endregion
    }
}
