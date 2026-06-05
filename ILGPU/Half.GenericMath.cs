// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: Half.GenericMath.cs
//
// C# 11 generic-math (System.Numerics.INumber) support for the kernel-native Half type.
//
// ILGPU.Half's arithmetic/comparison operators are FP32-based and tagged
// [MathIntrinsic]/[CompareIntrinisc], so ILGPU's frontend transpiles them on all 6 backends, and the
// frontend resolves static-abstract generic-math dispatch to these concrete operators (verified). By
// implementing INumber<Half>, a generic-math kernel (where T : INumber<T>) binds to THESE transpilable
// operators instead of being forced onto System.Half — whose INumber members lower to a BitCast that
// fails codegen (the "generic-math kernels fail everywhere with BitCast" report).
//
// Design: the computational members used inside kernels (arithmetic, identities, Abs, comparisons,
// Clamp/Max/Min/Sign, the new % / ++ / -- operators) go through the FP32 path, which transpiles. The
// host-only members (Parse/TryParse/TryFormat/ToString(format)/TryConvert*) delegate to System.Single
// and are never reached inside a kernel.
// ---------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    public readonly partial struct Half : INumber<Half>, ISignedNumber<Half>
    {
        #region IComparable (non-generic — required by INumber)

        /// <summary>Compares this half to another object (non-generic <see cref="IComparable"/>).</summary>
        public readonly int CompareTo(object? obj) =>
            obj is null ? 1
            : obj is Half other ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(Half)}.", nameof(obj));

        #endregion

        #region Identities

        /// <summary>The additive identity (0).</summary>
        public static Half AdditiveIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Half)0.0f;
        }

        /// <summary>The multiplicative identity (1).</summary>
        public static Half MultiplicativeIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Half)1.0f;
        }

        /// <summary>The value negative one (-1).</summary>
        static Half ISignedNumber<Half>.NegativeOne
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Half)(-1.0f);
        }

        static Half INumberBase<Half>.One
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Half)1.0f;
        }

        static Half INumberBase<Half>.Zero
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Half)0.0f;
        }

        static int INumberBase<Half>.Radix => 2;

        #endregion

        #region Extra operators (the ones not already on Half)

        /// <summary>Computes the remainder (a % b).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half operator %(Half left, Half right) => (Half)((float)left % (float)right);

        /// <summary>Returns the value unchanged (unary plus).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half operator +(Half value) => value;

        /// <summary>Increments the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half operator ++(Half value) => (Half)((float)value + 1.0f);

        /// <summary>Decrements the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half operator --(Half value) => (Half)((float)value - 1.0f);

        #endregion

        #region INumberBase predicates (the ones not already on Half via static methods)

        static bool INumberBase<Half>.IsCanonical(Half value) => true;
        static bool INumberBase<Half>.IsComplexNumber(Half value) => false;
        static bool INumberBase<Half>.IsImaginaryNumber(Half value) => false;
        static bool INumberBase<Half>.IsRealNumber(Half value) => !IsNaN(value);
        static bool INumberBase<Half>.IsInteger(Half value) => float.IsInteger((float)value);
        static bool INumberBase<Half>.IsEvenInteger(Half value) => float.IsEvenInteger((float)value);
        static bool INumberBase<Half>.IsOddInteger(Half value) => float.IsOddInteger((float)value);
        static bool INumberBase<Half>.IsNegative(Half value) => float.IsNegative((float)value);
        static bool INumberBase<Half>.IsPositive(Half value) => float.IsPositive((float)value);
        static bool INumberBase<Half>.IsNormal(Half value) => float.IsNormal((float)value);
        static bool INumberBase<Half>.IsSubnormal(Half value) => float.IsSubnormal((float)value);
        static bool INumberBase<Half>.IsZero(Half value) => IsZero(value);

        #endregion

        #region INumberBase magnitude / INumber min-max-clamp-sign

        static Half INumberBase<Half>.MaxMagnitude(Half x, Half y) =>
            (Half)MathF.MaxMagnitude((float)x, (float)y);
        static Half INumberBase<Half>.MaxMagnitudeNumber(Half x, Half y) =>
            (Half)MaxMagnitudeNumberF((float)x, (float)y);
        static Half INumberBase<Half>.MinMagnitude(Half x, Half y) =>
            (Half)MathF.MinMagnitude((float)x, (float)y);
        static Half INumberBase<Half>.MinMagnitudeNumber(Half x, Half y) =>
            (Half)MinMagnitudeNumberF((float)x, (float)y);

        /// <summary>Clamps a value to the inclusive [min, max] range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half Clamp(Half value, Half min, Half max) =>
            (Half)Math.Clamp((float)value, (float)min, (float)max);

        /// <summary>Copies the sign of <paramref name="sign"/> onto <paramref name="value"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half CopySign(Half value, Half sign) =>
            (Half)MathF.CopySign((float)value, (float)sign);

        /// <summary>Returns the larger of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half Max(Half x, Half y) => (Half)MathF.Max((float)x, (float)y);

        /// <summary>Returns the smaller of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half Min(Half x, Half y) => (Half)MathF.Min((float)x, (float)y);

        static Half INumber<Half>.MaxNumber(Half x, Half y) => (Half)MaxMagnitudeNumberF((float)x, (float)y) == x || (float)x >= (float)y ? x : y;
        static Half INumber<Half>.MinNumber(Half x, Half y) => (float)x <= (float)y || float.IsNaN((float)y) ? x : y;

        /// <summary>Returns the sign of the value (-1, 0, or +1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(Half value) => MathF.Sign((float)value);

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

        #region Conversions (host-only — never reached inside a kernel)

        static bool INumberBase<Half>.TryConvertFromChecked<TOther>(TOther value, out Half result)
        {
            result = (Half)(float)double.CreateChecked(value);
            return true;
        }

        static bool INumberBase<Half>.TryConvertFromSaturating<TOther>(TOther value, out Half result)
        {
            result = (Half)(float)double.CreateSaturating(value);
            return true;
        }

        static bool INumberBase<Half>.TryConvertFromTruncating<TOther>(TOther value, out Half result)
        {
            result = (Half)(float)double.CreateTruncating(value);
            return true;
        }

        static bool INumberBase<Half>.TryConvertToChecked<TOther>(Half value, out TOther result)
        {
            result = TOther.CreateChecked((float)value);
            return true;
        }

        static bool INumberBase<Half>.TryConvertToSaturating<TOther>(Half value, out TOther result)
        {
            result = TOther.CreateSaturating((float)value);
            return true;
        }

        static bool INumberBase<Half>.TryConvertToTruncating<TOther>(Half value, out TOther result)
        {
            result = TOther.CreateTruncating((float)value);
            return true;
        }

        #endregion

        #region Parsing / formatting (host-only — never reached inside a kernel)

        /// <summary>Parses a half value from a string.</summary>
        public static Half Parse(string s, IFormatProvider? provider) =>
            (Half)float.Parse(s, provider);

        /// <summary>Parses a half value from a span.</summary>
        public static Half Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            (Half)float.Parse(s, provider);

        static Half INumberBase<Half>.Parse(string s, NumberStyles style, IFormatProvider? provider) =>
            (Half)float.Parse(s, style, provider);

        static Half INumberBase<Half>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) =>
            (Half)float.Parse(s, style, provider);

        /// <summary>Tries to parse a half value from a string.</summary>
        public static bool TryParse(string? s, IFormatProvider? provider, out Half result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (Half)f;
            return ok;
        }

        /// <summary>Tries to parse a half value from a span.</summary>
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Half result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (Half)f;
            return ok;
        }

        static bool INumberBase<Half>.TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Half result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (Half)f;
            return ok;
        }

        static bool INumberBase<Half>.TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out Half result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (Half)f;
            return ok;
        }

        /// <summary>Formats this half value into a span.</summary>
        public bool TryFormat(
            Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
            ((float)this).TryFormat(destination, out charsWritten, format, provider);

        /// <summary>Formats this half value using the given format.</summary>
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            ((float)this).ToString(format, formatProvider);

        #endregion
    }
}
