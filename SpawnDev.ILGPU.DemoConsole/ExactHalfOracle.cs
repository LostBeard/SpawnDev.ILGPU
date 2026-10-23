using System.Numerics;

/// <summary>
/// Exact IEEE binary16 reference: computes a*b+c as a rational (BigInteger) and rounds it to
/// f16 once, to nearest even. Works on raw bit patterns. Any NaN result is returned as 0x7E00.
/// <c>System.Half</c> is not used because its FusedMultiplyAdd goes through f32 and rounds twice.
/// </summary>
public static class ExactHalfOracle
{
    public const ushort NaN = 0x7E00;
    private const ushort One = 0x3C00;
    private const ushort NegativeZero = 0x8000;

    public static bool IsNaN(ushort h) => (h & 0x7FFF) > 0x7C00;

    public static ushort Add(ushort a, ushort b, bool ftz = false) =>
        Fma(a, One, b, ftz);

    public static ushort Sub(ushort a, ushort b, bool ftz = false) =>
        Fma(a, One, (ushort)(b ^ 0x8000), ftz);

    /// <summary>a * b; the -0 addend leaves the sign of a zero product intact.</summary>
    public static ushort Mul(ushort a, ushort b, bool ftz = false) =>
        Fma(a, b, NegativeZero, ftz);

    /// <summary>PTX min.f16: a NaN operand yields the other; -0 orders below +0.</summary>
    public static ushort Min(ushort a, ushort b, bool ftz = false) => Select(a, b, ftz, true);

    /// <summary>PTX max.f16: a NaN operand yields the other; +0 orders above -0.</summary>
    public static ushort Max(ushort a, ushort b, bool ftz = false) => Select(a, b, ftz, false);

    private static int Order(ushort h) => (h & 0x8000) != 0 ? -(h & 0x7FFF) - 1 : h;

    private static ushort Select(ushort a, ushort b, bool ftz, bool min)
    {
        if (ftz)
        {
            a = Flush(a);
            b = Flush(b);
        }
        ushort result;
        if (IsNaN(a))
            result = b;
        else if (IsNaN(b))
            result = a;
        else
            result = (Order(a) <= Order(b)) == min ? a : b;
        return ftz && !IsNaN(result) ? Flush(result) : result;
    }

    public static ushort Fma(ushort a, ushort b, ushort c, bool ftz = false)
    {
        if (ftz)
        {
            a = Flush(a);
            b = Flush(b);
            c = Flush(c);
        }
        ushort result = Compute(a, b, c);
        return ftz && !IsNaN(result) ? Flush(result) : result;
    }

    /// <summary>Converts a double to f16 with a single exact rounding (nearest even).</summary>
    public static ushort FromDouble(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long fraction = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0x7FF)
            return fraction != 0 ? NaN : (negative ? (ushort)0xFC00 : (ushort)0x7C00);
        if (exponent == 0 && fraction == 0)
            return negative ? (ushort)0x8000 : (ushort)0;
        BigInteger mantissa = exponent == 0 ? fraction : fraction | (1L << 52);
        return Round(negative ? -mantissa : mantissa, exponent == 0 ? -1074 : exponent - 1075);
    }

    /// <summary>Flushes a subnormal to a zero of the same sign.</summary>
    public static ushort Flush(ushort h) => (h & 0x7C00) == 0 ? (ushort)(h & 0x8000) : h;

    private static bool IsInf(ushort h) => (h & 0x7FFF) == 0x7C00;

    private static bool IsZero(ushort h) => (h & 0x7FFF) == 0;

    private static (BigInteger Mantissa, int Exponent) Decode(ushort h)
    {
        int exponent = (h >> 10) & 0x1F;
        int fraction = h & 0x3FF;
        BigInteger mantissa = exponent == 0 ? fraction : fraction | 0x400;
        return ((h & 0x8000) != 0 ? -mantissa : mantissa, exponent == 0 ? -24 : exponent - 25);
    }

    private static ushort Compute(ushort a, ushort b, ushort c)
    {
        if (IsNaN(a) || IsNaN(b) || IsNaN(c))
            return NaN;
        bool productNegative = ((a ^ b) & 0x8000) != 0;
        bool addendNegative = (c & 0x8000) != 0;
        if (IsInf(a) || IsInf(b))
        {
            if (IsZero(a) || IsZero(b))
                return NaN;
            if (IsInf(c) && addendNegative != productNegative)
                return NaN;
            return productNegative ? (ushort)0xFC00 : (ushort)0x7C00;
        }
        if (IsInf(c))
            return c;

        var (ma, ea) = Decode(a);
        var (mb, eb) = Decode(b);
        var (mc, ec) = Decode(c);
        BigInteger product = ma * mb;
        int productExponent = ea + eb;
        int exponent = Math.Min(productExponent, ec);
        BigInteger sum = (product << (productExponent - exponent))
            + (mc << (ec - exponent));
        if (sum.IsZero)
        {
            // Exact zero: -0 only if both the product and the addend are -0.
            bool bothZero = product.IsZero && mc.IsZero;
            return bothZero && productNegative && addendNegative
                ? (ushort)0x8000
                : (ushort)0;
        }
        return Round(sum, exponent);
    }

    /// <summary>Rounds sum * 2^exponent (sum != 0) to f16, nearest even.</summary>
    private static ushort Round(BigInteger sum, int exponent)
    {
        ushort sign = sum.Sign < 0 ? (ushort)0x8000 : (ushort)0;
        BigInteger magnitude = BigInteger.Abs(sum);
        int top = (int)magnitude.GetBitLength() - 1 + exponent;
        // Exponent of the least significant result bit: 11 bits normal, floor at 2^-24.
        int quantum = Math.Max(top - 10, -24);
        int shift = quantum - exponent;
        BigInteger mantissa;
        if (shift <= 0)
        {
            mantissa = magnitude << -shift;
        }
        else
        {
            mantissa = magnitude >> shift;
            BigInteger remainder = magnitude - (mantissa << shift);
            BigInteger half = BigInteger.One << (shift - 1);
            if (remainder > half || (remainder == half && !mantissa.IsEven))
                mantissa += 1;
        }
        if (mantissa.IsZero)
            return sign;
        if (mantissa == 2048)
        {
            mantissa = 1024;
            ++quantum;
        }
        int m = (int)mantissa;
        if (quantum == -24 && m < 1024)
            return (ushort)(sign | m);
        int biased = quantum + 25;
        if (biased >= 31)
            return (ushort)(sign | 0x7C00);
        return (ushort)(sign | (biased << 10) | (m - 1024));
    }
}
