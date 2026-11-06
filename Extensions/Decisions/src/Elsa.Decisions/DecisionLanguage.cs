using System.Globalization;
using System.Numerics;
using System.Text;
using Elsa.Decisions.Core;

namespace Elsa.Decisions;

internal sealed class DecisionLanguageException(string code) : Exception
{
    internal string Code { get; } = code;
}

internal readonly record struct ExactNumber(BigInteger Coefficient, int Scale) : IComparable<ExactNumber>
{
    internal static ExactNumber Parse(string text)
    {
        if (text.Length is 0 or > 4096) throw new DecisionLanguageException("InvalidNumber");
        var offset = text[0] == '-' ? 1 : 0;
        if (offset == text.Length) throw new DecisionLanguageException("InvalidNumber");
        var dot = -1;
        var digitCount = 0;
        for (var i = offset; i < text.Length; i++)
        {
            if (text[i] == '.' && dot < 0) dot = i;
            else if (text[i] is >= '0' and <= '9') digitCount++;
            else throw new DecisionLanguageException("InvalidNumber");
        }
        if (digitCount == 0 || dot == text.Length - 1) throw new DecisionLanguageException("InvalidNumber");
        var digits = text[offset..].Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        var scale = dot < 0 ? 0 : text.Length - dot - 1;
        if (digits.Length == 0) return new ExactNumber(BigInteger.Zero, 0);
        var end = digits.Length;
        while (end > 0 && digits[end - 1] == '0' && scale > -28) { end--; scale--; }
        digits = digits[..end];
        if (digits.Length > 34 || scale is < -28 or > 28) throw new DecisionLanguageException("NumberOutOfRange");
        var coefficient = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return new ExactNumber(offset == 1 ? -coefficient : coefficient, scale);
    }

    public int CompareTo(ExactNumber other)
    {
        var scale = Math.Max(Scale, other.Scale);
        return (Coefficient * BigInteger.Pow(10, scale - Scale)).CompareTo(other.Coefficient * BigInteger.Pow(10, scale - other.Scale));
    }

    public override string ToString()
    {
        var digits = BigInteger.Abs(Coefficient).ToString(CultureInfo.InvariantCulture);
        var body = Scale <= 0 ? digits + new string('0', -Scale)
            : Scale >= digits.Length ? "0." + new string('0', Scale - digits.Length) + digits
            : digits.Insert(digits.Length - Scale, ".");
        return Coefficient.Sign < 0 ? "-" + body : body;
    }
}

internal static class DecisionScalars
{
    internal static DecisionValue Normalize(DecisionValue? value)
    {
        if (value is null) throw new DecisionLanguageException("InvalidValue");
        return value.Kind switch
        {
            "null" when value.Value is null => new("null", null),
            "number" when value.Value is not null => new("number", ExactNumber.Parse(value.Value).ToString()),
            "boolean" when value.Value is "true" or "false" => value,
            "string" when value.Value is not null && ValidString(value.Value) => value,
            _ => throw new DecisionLanguageException("InvalidValue")
        };
    }

    internal static bool ValidString(string text)
    {
        if (text.Length > 4096) return false;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i])) return false;
            }
            else if (char.IsLowSurrogate(text[i])) return false;
        }
        return true;
    }

    internal static bool Equal(DecisionValue left, DecisionValue right) => left.Kind == right.Kind &&
        (left.Kind == "number" ? ExactNumber.Parse(left.Value!).CompareTo(ExactNumber.Parse(right.Value!)) == 0 : left.Value == right.Value);

    internal static string Format(DecisionValue value)
    {
        if (value.Kind != "string") return value.Value ?? "null";
        var result = new StringBuilder("\"");
        foreach (var rune in value.Value!.EnumerateRunes())
        {
            result.Append(rune.Value switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                < 32 => "\\u" + rune.Value.ToString("X4", CultureInfo.InvariantCulture),
                _ => rune.ToString()
            });
        }
        return result.Append('"').ToString();
    }
}

internal sealed record UnaryAtom(string Operator, DecisionValue Value, DecisionValue? Upper = null, bool IncludeLower = false, bool IncludeUpper = false)
{
    internal bool? Match(DecisionValue input)
    {
        if (Operator == "equal") return DecisionScalars.Equal(input, Value);
        if (input.Kind == "null" || Value.Kind == "null") return null;
        if (input.Kind != "number") return null;
        var comparison = ExactNumber.Parse(input.Value!).CompareTo(ExactNumber.Parse(Value.Value!));
        return Operator switch
        {
            "<" => comparison < 0, "<=" => comparison <= 0, ">" => comparison > 0, ">=" => comparison >= 0,
            "range" => (comparison > 0 || comparison == 0 && IncludeLower) &&
                (ExactNumber.Parse(input.Value!).CompareTo(ExactNumber.Parse(Upper!.Value!)) is var upper && (upper < 0 || upper == 0 && IncludeUpper)),
            _ => throw new InvalidOperationException("Unknown compiled unary operator.")
        };
    }

    internal string Format() => Operator == "equal" ? DecisionScalars.Format(Value)
        : Operator == "range" ? (IncludeLower ? "[" : "]") + DecisionScalars.Format(Value) + ".." + DecisionScalars.Format(Upper!) + (IncludeUpper ? "]" : "[")
        : Operator + " " + DecisionScalars.Format(Value);
}

internal sealed record UnaryTest(bool Wildcard, bool Negated, UnaryAtom[] Alternatives)
{
    internal string Format() => Wildcard ? "-" : (Negated ? "not(" : "") + string.Join(", ", Alternatives.Select(a => a.Format())) + (Negated ? ")" : "");
}

internal sealed class DecisionCellParser(string text, CancellationToken cancellationToken)
{
    private int position;
    internal static UnaryTest ParseTest(string text, CancellationToken token)
    {
        var parser = new DecisionCellParser(text, token);
        parser.CheckLength();
        parser.Space();
        if (parser.Remaining == "-") return new(true, false, []);
        var negated = parser.Remaining.StartsWith("not", StringComparison.Ordinal);
        if (negated) { parser.position += 3; parser.Space(); parser.Expect('('); }
        var alternatives = new List<UnaryAtom>();
        do
        {
            parser.Space();
            if (alternatives.Count >= 128) throw new DecisionLanguageException("TooManyAlternatives");
            alternatives.Add(parser.Atom());
            parser.Space();
            if (!parser.Take(',')) break;
        } while (true);
        if (negated) { parser.Space(); parser.Expect(')'); }
        parser.End();
        return new(false, negated, alternatives.ToArray());
    }

    internal static DecisionValue ParseOutput(string text, CancellationToken token)
    {
        var parser = new DecisionCellParser(text, token);
        parser.CheckLength(); parser.Space();
        var value = parser.Literal();
        parser.End();
        return value;
    }

    private string Remaining => text[position..].TrimEnd();
    private void CheckLength() { if (text.Length is 0 or > 4096) throw new DecisionLanguageException("InvalidCellSize"); }
    private void Space()
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
    }
    private bool Take(char value) { if (position >= text.Length || text[position] != value) return false; position++; return true; }
    private void Expect(char value) { if (!Take(value)) throw new DecisionLanguageException("UnsupportedExpression"); }
    private void End() { Space(); if (position != text.Length) throw new DecisionLanguageException("UnsupportedExpression"); }

    private UnaryAtom Atom()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (position >= text.Length) throw new DecisionLanguageException("UnsupportedExpression");
        if (text[position] is '[' or '(' or ']')
        {
            var includeLower = text[position++] == '[';
            Space(); var lower = Literal(); Space();
            Expect('.'); Expect('.'); Space(); var upper = Literal(); Space();
            if (position >= text.Length || text[position] is not (']' or ')' or '[')) throw new DecisionLanguageException("UnsupportedExpression");
            var includeUpper = text[position++] == ']';
            if (lower.Kind != "number" || upper.Kind != "number") throw new DecisionLanguageException("InvalidRangeType");
            if (ExactNumber.Parse(lower.Value!).CompareTo(ExactNumber.Parse(upper.Value!)) > 0) throw new DecisionLanguageException("ReversedRange");
            return new("range", lower, upper, includeLower, includeUpper);
        }
        var operation = "equal";
        if (text[position] is '<' or '>')
        {
            operation = text[position++].ToString();
            if (Take('=')) operation += "=";
            Space();
        }
        var value = Literal();
        if (operation != "equal" && value.Kind != "number" && value.Kind != "null") throw new DecisionLanguageException("InvalidComparisonType");
        // Relational literal null is admitted and remains unknown for every numeric input.
        return new(operation, value);
    }

    private DecisionValue Literal()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (position >= text.Length) throw new DecisionLanguageException("UnsupportedExpression");
        if (Take('"')) return new("string", String());
        foreach (var keyword in new[] { "null", "true", "false" })
        {
            if (!text.AsSpan(position).StartsWith(keyword, StringComparison.Ordinal)) continue;
            position += keyword.Length;
            return keyword == "null" ? new("null", null) : new("boolean", keyword);
        }
        var start = position;
        if (text[position] == '-') position++;
        while (position < text.Length)
        {
            var c = text[position];
            if (c is >= '0' and <= '9') { position++; continue; }
            if (c == '.' && (position + 1 >= text.Length || text[position + 1] != '.')) { position++; continue; }
            break;
        }
        if (position == start) throw new DecisionLanguageException("UnsupportedExpression");
        return new("number", ExactNumber.Parse(text[start..position]).ToString());
    }

    private string String()
    {
        var result = new StringBuilder();
        while (position < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var c = text[position++];
            if (c == '"')
            {
                var value = result.ToString();
                if (!DecisionScalars.ValidString(value)) throw new DecisionLanguageException("InvalidString");
                return value;
            }
            if (c < 32) throw new DecisionLanguageException("InvalidString");
            if (c != '\\') { result.Append(c); continue; }
            if (position >= text.Length) throw new DecisionLanguageException("InvalidEscape");
            var escape = text[position++];
            if (escape is '\'' or '"' or '\\') result.Append(escape);
            else if (escape == 'n') result.Append('\n');
            else if (escape == 'r') result.Append('\r');
            else if (escape == 't') result.Append('\t');
            else if (escape is 'u' or 'U')
            {
                var count = escape == 'u' ? 4 : 6;
                if (position + count > text.Length) throw new DecisionLanguageException("InvalidEscape");
                var value = 0;
                for (var i = 0; i < count; i++)
                {
                    var digit = text[position++];
                    var hex = digit switch { >= '0' and <= '9' => digit - '0', >= 'a' and <= 'f' => digit - 'a' + 10, >= 'A' and <= 'F' => digit - 'A' + 10, _ => -1 };
                    if (hex < 0) throw new DecisionLanguageException("InvalidEscape");
                    value = value * 16 + hex;
                }
                if (!Rune.IsValid(value)) throw new DecisionLanguageException("InvalidUnicode");
                result.Append(char.ConvertFromUtf32(value));
            }
            else throw new DecisionLanguageException("InvalidEscape");
        }
        throw new DecisionLanguageException("UnterminatedString");
    }
}
