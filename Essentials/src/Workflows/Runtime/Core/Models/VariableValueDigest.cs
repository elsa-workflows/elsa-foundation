using System.Buffers.Text;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The value digest a variable version carries (spec 1221 FR-001, ADR 0092 decision 2): SHA-256 over a canonical JSON
/// form of the stored value, so two spellings of the same value (key order, number notation, string escaping) digest
/// the same. Computed once per applied write; a read never hashes.
/// </summary>
/// <remarks>
/// ADR 0091's canonical form is not on the initiative branch yet. Until it is, this applies the same normalization the
/// runtime already uses for intent identities: object keys ordered ordinally, numbers as coefficient + exponent, and
/// strings re-encoded by the writer. When ADR 0091 lands, this is the one place to align. A value that is not inline
/// digests its stored form: an external reference by its reference, a withheld value by its marker.
/// </remarks>
internal static class VariableValueDigest
{
    private const string Prefix = "sha256:";

    public static string Compute(ValueEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("presence", (int)envelope.Presence);
            if (envelope.InlineValue is { } inline)
            {
                writer.WritePropertyName("inline");
                WriteCanonical(inline, writer);
            }

            if (envelope.ExternalReference is { } external)
            {
                writer.WritePropertyName("external");
                WriteCanonical(JsonSerializer.SerializeToElement(external), writer);
            }

            if (envelope.WithheldValue is { } withheld)
            {
                writer.WritePropertyName("withheld");
                WriteCanonical(JsonSerializer.SerializeToElement(withheld), writer);
            }

            writer.WriteEndObject();
        }

        return Prefix + Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                writer.WriteStartObject();
                // Names are ordered by their unescaped UTF-8 bytes (code-point order). Most stored objects are already
                // in that order, so they are sorted only when they are not, and an unescaped name is used as stored.
                var properties = element.EnumerateObject()
                    .Select(static property => (Name: UnescapedName(property), property.Value))
                    .ToArray();
                var sorted = true;
                for (var index = 1; index < properties.Length && sorted; index++)
                    sorted = properties[index - 1].Name.AsSpan().SequenceCompareTo(properties[index].Name) <= 0;
                if (!sorted)
                    Array.Sort(properties, static (left, right) => left.Name.AsSpan().SequenceCompareTo(right.Name));
                foreach (var (name, value) in properties)
                {
                    writer.WritePropertyName(name);
                    WriteCanonical(value, writer);
                }

                writer.WriteEndObject();
                return;
            }
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in element.EnumerateArray())
                    WriteCanonical(value, writer);
                writer.WriteEndArray();
                return;
            case JsonValueKind.Number:
            {
                var raw = JsonMarshal.GetRawUtf8Value(element);
                if (raw.IndexOfAny((byte)'e', (byte)'E') < 0)
                {
                    WritePlainCanonicalNumber(raw, writer);
                    return;
                }

                writer.WriteRawValue(CanonicalNumber(element.GetRawText()), skipInputValidation: true);
                return;
            }
            case JsonValueKind.String:
            {
                // The raw token includes its quotes. Without an escape sequence its content is already the unescaped
                // UTF-8 text, so it is written without first decoding it to a string.
                var raw = JsonMarshal.GetRawUtf8Value(element);
                var content = raw.Length >= 2 ? raw[1..^1] : raw;
                if (content.IndexOf((byte)'\\') < 0)
                    writer.WriteStringValue(content);
                else
                    writer.WriteStringValue(element.GetString());
                return;
            }
            default:
                element.WriteTo(writer);
                return;
        }
    }

    private static byte[] UnescapedName(JsonProperty property)
    {
        var raw = JsonMarshal.GetRawUtf8PropertyName(property);
        return raw.IndexOf((byte)'\\') < 0 ? raw.ToArray() : System.Text.Encoding.UTF8.GetBytes(property.Name);
    }

    // The same canonical form as CanonicalNumber (coefficient without leading or trailing zeros, then "e" and the
    // exponent) for a number written without an exponent, computed on the raw bytes without allocating.
    private static void WritePlainCanonicalNumber(ReadOnlySpan<byte> raw, Utf8JsonWriter writer)
    {
        Span<byte> buffer = raw.Length + 24 <= 256 ? stackalloc byte[256] : new byte[raw.Length + 24];
        var length = 0;
        var negative = raw.Length > 0 && raw[0] == (byte)'-';
        var mantissa = negative ? raw[1..] : raw;
        var point = mantissa.IndexOf((byte)'.');
        var fractionDigits = point < 0 ? 0 : mantissa.Length - point - 1;

        if (negative)
            buffer[length++] = (byte)'-';
        var lastNonZero = -1;
        var digitCount = 0;
        var started = false;
        foreach (var digit in mantissa)
        {
            if (digit == (byte)'.')
                continue;
            if (!started && digit == (byte)'0')
                continue;
            started = true;
            buffer[length++] = digit;
            digitCount++;
            if (digit != (byte)'0')
                lastNonZero = length;
        }

        if (digitCount == 0)
        {
            writer.WriteRawValue("0"u8, skipInputValidation: true);
            return;
        }

        var trailingZeros = length - lastNonZero;
        length = lastNonZero;
        var exponent = (long)trailingZeros - fractionDigits;
        buffer[length++] = (byte)'e';
        Utf8Formatter.TryFormat(exponent, buffer[length..], out var written);
        length += written;
        writer.WriteRawValue(buffer[..length], skipInputValidation: true);
    }

    private static string CanonicalNumber(string token)
    {
        var exponentIndex = token.AsSpan().IndexOfAny('e', 'E');
        var mantissa = exponentIndex < 0 ? token : token[..exponentIndex];
        var negative = mantissa[0] == '-';
        if (negative) mantissa = mantissa[1..];
        var point = mantissa.IndexOf('.');
        var fractionDigits = point < 0 ? 0 : mantissa.Length - point - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return "0";
        var coefficient = digits.TrimEnd('0');
        var shift = digits.Length - coefficient.Length - fractionDigits;
        // Without an exponent the shift fits a long; only an explicit exponent can need arbitrary size.
        var exponent = exponentIndex < 0
            ? shift.ToString(CultureInfo.InvariantCulture)
            : (BigInteger.Parse(token.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) + shift)
                .ToString(CultureInfo.InvariantCulture);
        return (negative ? "-" : "") + coefficient + "e" + exponent;
    }
}
