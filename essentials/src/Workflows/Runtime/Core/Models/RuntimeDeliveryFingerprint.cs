using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Canonical identities for the new source protocol; historical checkpoint identities are unchanged.</summary>
internal static class RuntimeDeliveryFingerprint
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    internal static string Compute(string domain, object value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(domain);
            Write(JsonSerializer.SerializeToElement(value, Options), writer);
            writer.WriteEndArray();
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
                if (properties.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                    throw new ArgumentException("Canonical delivery JSON cannot contain duplicate property names.");
                foreach (var property in properties)
                {
                    Text(property.Name, nameof(value), allowEmpty: true);
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in value.EnumerateArray()) Write(child, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                Text(value.GetString()!, nameof(value), allowEmpty: true);
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(CanonicalNumber(value.GetRawText()));
                break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new ArgumentException("Delivery JSON must be defined.", nameof(value));
        }
    }

    // Normalize a JSON decimal token as an integer coefficient and arbitrary-size decimal exponent.
    // Decimal/Double parsing can round a valid source token before the hash sees it.
    private static string CanonicalNumber(string token)
    {
        var negative = token[0] == '-';
        var unsigned = negative ? token[1..] : token;
        var exponentAt = unsigned.IndexOfAny(['e', 'E']);
        var mantissa = exponentAt < 0 ? unsigned : unsigned[..exponentAt];
        var exponent = exponentAt < 0 ? BigInteger.Zero :
            BigInteger.Parse(unsigned.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var pointAt = mantissa.IndexOf('.');
        if (pointAt >= 0) exponent -= mantissa.Length - pointAt - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return "0";
        var coefficient = digits.TrimEnd('0');
        exponent += digits.Length - coefficient.Length;
        return (negative ? "-" : "") + coefficient +
            (exponent.IsZero ? "" : "e" + exponent.ToString(CultureInfo.InvariantCulture));
    }

    internal static void Text(string value, string name, bool allowEmpty = false)
    {
        if (allowEmpty) ArgumentNullException.ThrowIfNull(value, name);
        else ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index])) continue;
            if (!char.IsHighSurrogate(value[index]) || index + 1 == value.Length || !char.IsLowSurrogate(value[++index]))
                throw new ArgumentException("Delivery identities must preserve their exact Unicode representation.", name);
        }
    }

    internal static void Hash(string value, string name)
    {
        if (value is null || value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A complete lowercase SHA-256 fingerprint is required.", name);
    }
}
