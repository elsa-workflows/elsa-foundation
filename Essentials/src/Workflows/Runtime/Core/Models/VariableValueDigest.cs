using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
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
public static class VariableValueDigest
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
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }

                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in element.EnumerateArray())
                    WriteCanonical(value, writer);
                writer.WriteEndArray();
                return;
            case JsonValueKind.Number:
                writer.WriteRawValue(CanonicalNumber(element.GetRawText()), skipInputValidation: true);
                return;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                return;
            default:
                element.WriteTo(writer);
                return;
        }
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
        var exponent = exponentIndex < 0 ? BigInteger.Zero :
            BigInteger.Parse(token.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        exponent += digits.Length - coefficient.Length - fractionDigits;
        return (negative ? "-" : "") + coefficient + "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }
}
