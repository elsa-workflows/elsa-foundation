using System.Globalization;
using System.Numerics;
using Jint.Runtime;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Whether a JavaScript number holds a JSON number exactly (ADR 0046 V9, spec 1228 FR-011, research R-2): a number has
/// lost precision when the JavaScript number, written as JSON, differs in value from the stored one. Both texts are read
/// exactly, as a big-integer mantissa and a base-10 exponent, so the comparison holds for any length and any exponent.
/// <c>10.50</c> → <c>10.5</c>, <c>2.0</c> → <c>2</c>, <c>0.1</c> → <c>0.1</c> and <c>1e30</c> → <c>1e+30</c> keep their
/// value; <c>9007199254740993</c> → <c>9007199254740992</c> does not, and a number that becomes <c>Infinity</c> or a
/// non-zero number that becomes <c>0</c> is lossy.
/// </summary>
internal static class JavaScriptNumberPrecision
{
    /// <summary>The largest integer a double holds exactly together with all smaller ones: 2^53 − 1.</summary>
    private const long MaxSafeInteger = 9007199254740991;

    public static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>The JSON text JavaScript writes for a number (<c>Number.prototype.toString</c>).</summary>
    public static string JavaScriptText(double value) => TypeConverter.ToString(value);

    /// <summary>Checks one JSON number text. <paramref name="held"/> is the double a script holds.</summary>
    public static bool IsExact(string text, out double held, out bool isInteger)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        held = Parse(text);

        // The common case: a plain integer within ±(2^53 − 1).
        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer) &&
            integer is >= -MaxSafeInteger and <= MaxSafeInteger)
        {
            isInteger = true;
            return true;
        }

        var stored = Read(text);
        isInteger = stored.IsInteger;
        if (double.IsInfinity(held) || double.IsNaN(held))
            return false;
        return stored.Equals(Read(JavaScriptText(held)));
    }

    /// <summary>Reads JSON number text as an exact decimal: a signed mantissa with no trailing zeros, and an exponent.</summary>
    private static ExactDecimal Read(string text)
    {
        var span = text.AsSpan();
        var negative = span[0] == '-';
        if (negative || span[0] == '+')
            span = span[1..];

        var exponentAt = span.IndexOfAny('e', 'E');
        BigInteger exponent = BigInteger.Zero;
        if (exponentAt >= 0)
        {
            exponent = BigInteger.Parse(span[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
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

        digits = digits.TrimStart('0');
        if (digits.Length == 0)
            return new ExactDecimal(BigInteger.Zero, BigInteger.Zero);

        var trailing = digits.Length - digits.TrimEnd('0').Length;
        digits = digits[..^trailing];
        exponent += trailing;
        var mantissa = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return new ExactDecimal(negative ? -mantissa : mantissa, exponent);
    }

    /// <summary>A normalized exact decimal: equal values have equal fields (zero has exponent zero).</summary>
    private readonly record struct ExactDecimal(BigInteger Mantissa, BigInteger Exponent)
    {
        public bool IsInteger => Mantissa.IsZero || Exponent >= 0;
    }
}
