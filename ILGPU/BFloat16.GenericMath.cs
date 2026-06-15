// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: BFloat16.GenericMath.cs
//
// C# 11 generic-math (System.Numerics.INumber) support for the kernel-native BFloat16 type.
//
// BFloat16's arithmetic/comparison operators are FP32-based and tagged
// [MathIntrinsic]/[CompareIntrinisc], so ILGPU's frontend transpiles them on all backends, and
// the frontend resolves static-abstract generic-math dispatch to these concrete operators. By
// implementing INumber<BFloat16>, a generic-math kernel (where T : INumber<T>) binds to THESE
// transpilable operators — the same approach that makes ILGPU.Half work in generic-math kernels.
//
// Design (mirrors Half.GenericMath): the computational members used inside kernels (arithmetic,
// identities, Abs, comparisons, Clamp/Max/Min/Sign, % / ++ / --) go through the FP32 path, which
// transpiles. The host-only members (Parse/TryParse/TryFormat/ToString(format)/TryConvert*)
// delegate to System.Single and are never reached inside a kernel.
// ---------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    public readonly partial struct BFloat16 : INumber<BFloat16>, ISignedNumber<BFloat16>
    {
        #region IComparable (non-generic — required by INumber)

        /// <summary>Compares this value to another object (non-generic <see cref="IComparable"/>).</summary>
        public readonly int CompareTo(object? obj) =>
            obj is null ? 1
            : obj is BFloat16 other ? CompareTo(other)
            : throw new ArgumentException($"Object must be of type {nameof(BFloat16)}.", nameof(obj));

        #endregion

        #region Identities

        /// <summary>The additive identity (0).</summary>
        public static BFloat16 AdditiveIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BFloat16)0.0f;
        }

        /// <summary>The multiplicative identity (1).</summary>
        public static BFloat16 MultiplicativeIdentity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BFloat16)1.0f;
        }

        /// <summary>The value negative one (-1).</summary>
        static BFloat16 ISignedNumber<BFloat16>.NegativeOne
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BFloat16)(-1.0f);
        }

        static BFloat16 INumberBase<BFloat16>.One
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BFloat16)1.0f;
        }

        static BFloat16 INumberBase<BFloat16>.Zero
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (BFloat16)0.0f;
        }

        static int INumberBase<BFloat16>.Radix => 2;

        #endregion

        #region Extra operators (the ones not already on BFloat16)

        /// <summary>Computes the remainder (a % b).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator %(BFloat16 left, BFloat16 right) =>
            (BFloat16)((float)left % (float)right);

        /// <summary>Returns the value unchanged (unary plus).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator +(BFloat16 value) => value;

        /// <summary>Increments the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator ++(BFloat16 value) => (BFloat16)((float)value + 1.0f);

        /// <summary>Decrements the value by one.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 operator --(BFloat16 value) => (BFloat16)((float)value - 1.0f);

        #endregion

        #region INumberBase predicates (the ones not already on BFloat16 via static methods)

        static bool INumberBase<BFloat16>.IsCanonical(BFloat16 value) => true;
        static bool INumberBase<BFloat16>.IsComplexNumber(BFloat16 value) => false;
        static bool INumberBase<BFloat16>.IsImaginaryNumber(BFloat16 value) => false;
        static bool INumberBase<BFloat16>.IsRealNumber(BFloat16 value) => !IsNaN(value);
        static bool INumberBase<BFloat16>.IsInteger(BFloat16 value) => float.IsInteger((float)value);
        static bool INumberBase<BFloat16>.IsEvenInteger(BFloat16 value) => float.IsEvenInteger((float)value);
        static bool INumberBase<BFloat16>.IsOddInteger(BFloat16 value) => float.IsOddInteger((float)value);
        static bool INumberBase<BFloat16>.IsNegative(BFloat16 value) => float.IsNegative((float)value);
        static bool INumberBase<BFloat16>.IsPositive(BFloat16 value) => float.IsPositive((float)value);
        static bool INumberBase<BFloat16>.IsNormal(BFloat16 value) => float.IsNormal((float)value);
        static bool INumberBase<BFloat16>.IsSubnormal(BFloat16 value) => float.IsSubnormal((float)value);
        static bool INumberBase<BFloat16>.IsZero(BFloat16 value) => IsZero(value);

        #endregion

        #region INumberBase magnitude / INumber min-max-clamp-sign

        static BFloat16 INumberBase<BFloat16>.MaxMagnitude(BFloat16 x, BFloat16 y) =>
            (BFloat16)MathF.MaxMagnitude((float)x, (float)y);
        static BFloat16 INumberBase<BFloat16>.MaxMagnitudeNumber(BFloat16 x, BFloat16 y) =>
            (BFloat16)MaxMagnitudeNumberF((float)x, (float)y);
        static BFloat16 INumberBase<BFloat16>.MinMagnitude(BFloat16 x, BFloat16 y) =>
            (BFloat16)MathF.MinMagnitude((float)x, (float)y);
        static BFloat16 INumberBase<BFloat16>.MinMagnitudeNumber(BFloat16 x, BFloat16 y) =>
            (BFloat16)MinMagnitudeNumberF((float)x, (float)y);

        /// <summary>Clamps a value to the inclusive [min, max] range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Clamp(BFloat16 value, BFloat16 min, BFloat16 max) =>
            (BFloat16)Math.Clamp((float)value, (float)min, (float)max);

        /// <summary>Copies the sign of <paramref name="sign"/> onto <paramref name="value"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 CopySign(BFloat16 value, BFloat16 sign) =>
            (BFloat16)MathF.CopySign((float)value, (float)sign);

        /// <summary>Returns the larger of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Max(BFloat16 x, BFloat16 y) => (BFloat16)MathF.Max((float)x, (float)y);

        /// <summary>Returns the smaller of two values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BFloat16 Min(BFloat16 x, BFloat16 y) => (BFloat16)MathF.Min((float)x, (float)y);

        static BFloat16 INumber<BFloat16>.MaxNumber(BFloat16 x, BFloat16 y) =>
            (BFloat16)MaxMagnitudeNumberF((float)x, (float)y) == x || (float)x >= (float)y ? x : y;
        static BFloat16 INumber<BFloat16>.MinNumber(BFloat16 x, BFloat16 y) =>
            (float)x <= (float)y || float.IsNaN((float)y) ? x : y;

        /// <summary>Returns the sign of the value (-1, 0, or +1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Sign(BFloat16 value) => MathF.Sign((float)value);

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

        static bool INumberBase<BFloat16>.TryConvertFromChecked<TOther>(TOther value, out BFloat16 result)
        {
            result = (BFloat16)(float)double.CreateChecked(value);
            return true;
        }

        static bool INumberBase<BFloat16>.TryConvertFromSaturating<TOther>(TOther value, out BFloat16 result)
        {
            result = (BFloat16)(float)double.CreateSaturating(value);
            return true;
        }

        static bool INumberBase<BFloat16>.TryConvertFromTruncating<TOther>(TOther value, out BFloat16 result)
        {
            result = (BFloat16)(float)double.CreateTruncating(value);
            return true;
        }

        static bool INumberBase<BFloat16>.TryConvertToChecked<TOther>(BFloat16 value, out TOther result)
        {
            result = TOther.CreateChecked((float)value);
            return true;
        }

        static bool INumberBase<BFloat16>.TryConvertToSaturating<TOther>(BFloat16 value, out TOther result)
        {
            result = TOther.CreateSaturating((float)value);
            return true;
        }

        static bool INumberBase<BFloat16>.TryConvertToTruncating<TOther>(BFloat16 value, out TOther result)
        {
            result = TOther.CreateTruncating((float)value);
            return true;
        }

        #endregion

        #region Parsing / formatting (host-only — never reached inside a kernel)

        /// <summary>Parses a bfloat16 value from a string.</summary>
        public static BFloat16 Parse(string s, IFormatProvider? provider) =>
            (BFloat16)float.Parse(s, provider);

        /// <summary>Parses a bfloat16 value from a span.</summary>
        public static BFloat16 Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            (BFloat16)float.Parse(s, provider);

        static BFloat16 INumberBase<BFloat16>.Parse(string s, NumberStyles style, IFormatProvider? provider) =>
            (BFloat16)float.Parse(s, style, provider);

        static BFloat16 INumberBase<BFloat16>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) =>
            (BFloat16)float.Parse(s, style, provider);

        /// <summary>Tries to parse a bfloat16 value from a string.</summary>
        public static bool TryParse(string? s, IFormatProvider? provider, out BFloat16 result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (BFloat16)f;
            return ok;
        }

        /// <summary>Tries to parse a bfloat16 value from a span.</summary>
        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out BFloat16 result)
        {
            bool ok = float.TryParse(s, provider, out float f);
            result = (BFloat16)f;
            return ok;
        }

        static bool INumberBase<BFloat16>.TryParse(string? s, NumberStyles style, IFormatProvider? provider, out BFloat16 result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (BFloat16)f;
            return ok;
        }

        static bool INumberBase<BFloat16>.TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out BFloat16 result)
        {
            bool ok = float.TryParse(s, style, provider, out float f);
            result = (BFloat16)f;
            return ok;
        }

        /// <summary>Formats this bfloat16 value into a span.</summary>
        public bool TryFormat(
            Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
            ((float)this).TryFormat(destination, out charsWritten, format, provider);

        /// <summary>Formats this bfloat16 value using the given format.</summary>
        public string ToString(string? format, IFormatProvider? formatProvider) =>
            ((float)this).ToString(format, formatProvider);

        #endregion
    }
}
