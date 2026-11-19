using System.Globalization;
using System.Numerics;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Whether a JavaScript number holds a JSON number exactly (ADR 0046 V9, spec 1228 FR-011, research R-2). The JSON text
/// is read exactly as a big-integer mantissa and a base-10 exponent and compared, as an exact value, with the double the
/// engine holds. <c>10.50</c> and <c>2.0</c> are exact; <c>9007199254740993</c> is not; a number that becomes
/// <c>Infinity</c>, or a non-zero number that becomes zero, is lossy.
/// </summary>
internal static class JavaScriptNumberPrecision
{
    /// <summary>The largest integer a double holds exactly together with all smaller ones: 2^53 − 1.</summary>
    private const long MaxSafeInteger = 9007199254740991;

    public static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>Checks one JSON number text. <paramref name="held"/> is the double a script holds.</summary>
    public static bool IsExact(string text, out double held, out bool isInteger)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        held = Parse(text);

        // The common case: a plain integer within ±(2^53 − 1).
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            isInteger = true;
            return integer is >= -MaxSafeInteger and <= MaxSafeInteger || new BigInteger(held) == integer;
        }

        var (mantissa, exponent) = ReadExact(text);
        isInteger = IsInteger(mantissa, exponent);
        if (double.IsInfinity(held))
            return false;
        if (held == 0)
            return mantissa.IsZero;
        if (mantissa.IsZero)
            return false;

        // The exact value of the double: m × 2^e with an integer m.
        var bits = BitConverter.DoubleToInt64Bits(held);
        var negative = bits < 0;
        var biased = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        long binaryMantissa;
        int binaryExponent;
        if (biased == 0)
        {
            binaryMantissa = fraction;
            binaryExponent = -1074;
        }
        else
        {
            binaryMantissa = fraction | (1L << 52);
            binaryExponent = biased - 1075;
        }

        if (negative)
            binaryMantissa = -binaryMantissa;

        // mantissa × 10^exponent == binaryMantissa × 2^binaryExponent, cross-multiplied to integers.
        var left = mantissa * (exponent > 0 ? BigInteger.Pow(10, exponent) : BigInteger.One);
        var leftDenominator = exponent < 0 ? BigInteger.Pow(10, -exponent) : BigInteger.One;
        var right = new BigInteger(binaryMantissa) * (binaryExponent > 0 ? BigInteger.Pow(2, binaryExponent) : BigInteger.One);
        var rightDenominator = binaryExponent < 0 ? BigInteger.Pow(2, -binaryExponent) : BigInteger.One;
        return left * rightDenominator == right * leftDenominator;
    }

    /// <summary>Reads JSON number text as an exact signed mantissa and base-10 exponent.</summary>
    private static (BigInteger Mantissa, int Exponent) ReadExact(string text)
    {
        var span = text.AsSpan();
        var negative = span[0] == '-';
        if (negative)
            span = span[1..];

        var exponentAt = span.IndexOfAny('e', 'E');
        var exponent = 0;
        if (exponentAt >= 0)
        {
            // A JSON exponent too large for an int already made the double infinite or zero, which decides it above;
            // clamping keeps the arithmetic finite for the mantissa's sign and integer test.
            exponent = int.TryParse(span[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : span[exponentAt + 1] == '-' ? -100_000 : 100_000;
            span = span[..exponentAt];
        }

        var dot = span.IndexOf('.');
        string digits;
        if (dot >= 0)
        {
            digits = string.Concat(span[..dot], span[(dot + 1)..]);
            exponent -= span.Length - dot - 1;
        }
        else
            digits = span.ToString();

        var mantissa = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return (negative ? -mantissa : mantissa, exponent);
    }

    private static bool IsInteger(BigInteger mantissa, int exponent)
    {
        if (exponent >= 0 || mantissa.IsZero)
            return true;
        if (exponent < -10_000)
            return false;
        return BigInteger.Remainder(mantissa, BigInteger.Pow(10, -exponent)).IsZero;
    }
}
