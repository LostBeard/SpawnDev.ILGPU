// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: Float8E5M2.GenericMath.cs
//
// C# 11 generic-math (System.Numerics.INumber) support for the kernel-native Float8E5M2 type.
//
// Float8E5M2's arithmetic/comparison operators are FP32-based and tagged
// [MathIntrinsic]/[CompareIntrinisc], so ILGPU's frontend transpiles them on all backends, and
// the frontend resolves static-abstract generic-math dispatch to these concrete operators. By
// implementing INumber<Float8E5M2>, a generic-math kernel (where T : INumber<T>) binds to THESE
// transpilable operators - the same approach that makes ILGPU.Half and BFloat16 work in
// generic-math kernels.
//
// Design mirrors BFloat16.GenericMath: the computational members used inside kernels (arithmetic,
// identities, Abs, comparisons, Clamp/Max/Min/Sign, % / ++ / --) go through the FP32 path, which
// transpiles. The host-only members (Parse/TryParse/TryFormat/ToString(format)/TryConvert*)
// delegate to System.Single and are never reached inside a kernel. E5M2 is IEEE-754-style (it
// HAS infinities + NaNs), so the Inf/NaN predicates already live on the base struct as public
// static methods (which satisfy the matching INumberBase static-abstract members implicitly).
// ---------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    public readonly partial struct Float8E5M2 : INumber<Float8E5M2>, ISignedNumber<Float8E5M2>
    {
        #region IComparable (non-generic - required by INumber)

        /// <summary>Compares this value to another object (non-generic <see cref="IComparable"/>).</summary>
        public readonly int CompareTo(object? obj) =>
            obj is null ? 1
            : obj is Float8E5M2 other ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(Float8E5M2)}.", nameof(obj));

        #endregion

        #region Identities

        /// <summary>The additive identity (0).</summary>
        public static Float8E5M2 AdditiveIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E5M2)0.0f;
        }

        /// <summary>The multiplicative identity (1).</summary>
        public static Float8E5M2 MultiplicativeIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E5M2)1.0f;
        }

        /// <summary>The value negative one (-1).</summary>
        static Float8E5M2 ISignedNumber<Float8E5M2>.NegativeOne
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E5M2)(-1.0f);
        }

        static Float8E5M2 INumberBase<Float8E5M2>.One
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E5M2)1.0f;
        }

        static Float8E5M2 INumberBase<Float8E5M2>.Zero
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Float8E5M2)0.0f;
        }

        static int INumberBase<Float8E5M2>.Radix => 2;

        #endregion

        #region Extra operators (the ones not already on Float8E5M2)

        /// <summary>Computes the remainder (a % b).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 operator %(Float8E5M2 left, Float8E5M2 right) =>
            (Float8E5M2)((float)left % (float)right);

        /// <summary>Returns the value unchanged (unary plus).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 operator +(Float8E5M2 value) => value;

        /// <summary>Increments the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 operator ++(Float8E5M2 value) => (Float8E5M2)((float)value + 1.0f);

        /// <summary>Decrements the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 operator --(Float8E5M2 value) => (Float8E5M2)((float)value - 1.0f);

        #endregion

        #region INumberBase predicates (the ones not already on Float8E5M2 via static methods)

        static bool INumberBase<Float8E5M2>.IsCanonical(Float8E5M2 value) => true;
        static bool INumberBase<Float8E5M2>.IsComplexNumber(Float8E5M2 value) => false;
        static bool INumberBase<Float8E5M2>.IsImaginaryNumber(Float8E5M2 value) => false;
        static bool INumberBase<Float8E5M2>.IsRealNumber(Float8E5M2 value) => !IsNaN(value);
        static bool INumberBase<Float8E5M2>.IsInteger(Float8E5M2 value) => float.IsInteger((float)value);
        static bool INumberBase<Float8E5M2>.IsEvenInteger(Float8E5M2 value) => float.IsEvenInteger((float)value);
        static bool INumberBase<Float8E5M2>.IsOddInteger(Float8E5M2 value) => float.IsOddInteger((float)value);
        static bool INumberBase<Float8E5M2>.IsNegative(Float8E5M2 value) => float.IsNegative((float)value);
        static bool INumberBase<Float8E5M2>.IsPositive(Float8E5M2 value) => float.IsPositive((float)value);
        static bool INumberBase<Float8E5M2>.IsNormal(Float8E5M2 value) => float.IsNormal((float)value);
        static bool INumberBase<Float8E5M2>.IsSubnormal(Float8E5M2 value) => float.IsSubnormal((float)value);
        static bool INumberBase<Float8E5M2>.IsZero(Float8E5M2 value) => IsZero(value);

        #endregion

        #region INumberBase magnitude / INumber min-max-clamp-sign

        static Float8E5M2 INumberBase<Float8E5M2>.MaxMagnitude(Float8E5M2 x, Float8E5M2 y) =>
            (Float8E5M2)MathF.MaxMagnitude((float)x, (float)y);
        static Float8E5M2 INumberBase<Float8E5M2>.MaxMagnitudeNumber(Float8E5M2 x, Float8E5M2 y) =>
            (Float8E5M2)MaxMagnitudeNumberF((float)x, (float)y);
        static Float8E5M2 INumberBase<Float8E5M2>.MinMagnitude(Float8E5M2 x, Float8E5M2 y) =>
            (Float8E5M2)MathF.MinMagnitude((float)x, (float)y);
        static Float8E5M2 INumberBase<Float8E5M2>.MinMagnitudeNumber(Float8E5M2 x, Float8E5M2 y) =>
            (Float8E5M2)MinMagnitudeNumberF((float)x, (float)y);

        /// <summary>Clamps a value to the inclusive [min, max] range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 Clamp(Float8E5M2 value, Float8E5M2 min, Float8E5M2 max) =>
            (Float8E5M2)Math.Clamp((float)value, (float)min, (float)max);

        /// <summary>Copies the sign of <paramref name="sign"/> onto <paramref name="value"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 CopySign(Float8E5M2 value, Float8E5M2 sign) =>
            (Float8E5M2)MathF.CopySign((float)value, (float)sign);

        /// <summary>Returns the larger of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 Max(Float8E5M2 x, Float8E5M2 y) => (Float8E5M2)MathF.Max((float)x, (float)y);

        /// <summary>Returns the smaller of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E5M2 Min(Float8E5M2 x, Float8E5M2 y) => (Float8E5M2)MathF.Min((float)x, (float)y);

        static Float8E5M2 INumber<Float8E5M2>.MaxNumber(Float8E5M2 x, Float8E5M2 y) =>
            (Float8E5M2)MaxMagnitudeNumberF((float)x, (float)y) == x || (float)x >= (float)y ? x : y;
        static Float8E5M2 INumber<Float8E5M2>.MinNumber(Float8E5M2 x, Float8E5M2 y) =>
            (float)x <= (float)y || float.IsNaN((float)y) ? x : y;

        /// <summary>Returns the sign of the value (-1, 0, or +1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(Float8E5M2 value) => MathF.Sign((float)value);

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

        static bool INumberBase<Float8E5M2>.TryConvertFromChecked<TOther>(TOther value, out Float8E5M2 result)
        {
            result = (Float8E5M2)(float)double.CreateChecked(value);
            return true;
        }

        static bool INumberBase<Float8E5M2>.TryConvertFromSaturating<TOther>(TOther value, out Float8E5M2 result)
        {
            result = (Float8E5M2)(float)double.CreateSaturating(value);
            return true;
        }

        static bool INumberBase<Float8E5M2>.TryConvertFromTruncating<TOther>(TOther value, out Float8E5M2 result)
        {
            result = (Float8E5M2)(float)double.CreateTruncating(value);
            return true;
        }

        static bool INumberBase<Float8E5M2>.TryConvertToChecked<TOther>(Float8E5M2 value, out TOther result)
        {
            result = TOther.CreateChecked((float)value);
            return true;
        }

        static bool INumberBase<Float8E5M2>.TryConvertToSaturating<TOther>(Float8E5M2 value, out TOther result)
        {
            result = TOther.CreateSaturating((float)value);
            return true;
        }

        static bool INumberBase<Float8E5M2>.TryConvertToTruncating<TOther>(Float8E5M2 value, out TOther result)
        {
            result = TOther.CreateTruncating((float)value);
            return true;
        }

        #endregion

        #region Parsing / formatting (host-only - never reached inside a kernel)

        /// <summary>Parses an E5M2 value from a string.</summary>
        public static Float8E5M2 Parse(string s, IFormatProvider? provider) =>
            (Float8E5M2)float.Parse(s, provider);

        /// <summary>Parses an E5M2 value from a span.</summary>
        public static Float8E5M2 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            (Float8E5M2)float.Parse(s, provider);

        static Float8E5M2 INumberBase<Float8E5M2>.Parse(string s, NumberStyles style, IFormatProvider? provider) =>
            (Float8E5M2)float.Parse(s, style, provider);

        static Float8E5M2 INumberBase<Float8E5M2>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) =>
            (Float8E5M2)float.Parse(s, style, provider);

        /// <summary>Tries to parse an E5M2 value from a string.</summary>
        public static bool TryParse(string? s, IFormatProvider? provider, out Float8E5M2 result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (Float8E5M2)f;
            return ok;
        }

        /// <summary>Tries to parse an E5M2 value from a span.</summary>
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Float8E5M2 result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (Float8E5M2)f;
            return ok;
        }

        static bool INumberBase<Float8E5M2>.TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Float8E5M2 result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (Float8E5M2)f;
            return ok;
        }

        static bool INumberBase<Float8E5M2>.TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out Float8E5M2 result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (Float8E5M2)f;
            return ok;
        }

        /// <summary>Formats this E5M2 value into a span.</summary>
        public bool TryFormat(
            Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
            ((float)this).TryFormat(destination, out charsWritten, format, provider);

        /// <summary>Formats this E5M2 value using the given format.</summary>
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            ((float)this).ToString(format, formatProvider);

        #endregion
    }
}
