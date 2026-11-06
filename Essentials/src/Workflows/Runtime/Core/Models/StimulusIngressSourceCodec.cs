using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Lossless, checked ingress headers shared by source providers and Runtime family consumers.</summary>
/// <remarks>
/// Encoding preserves opaque UTF-16 identifiers, while input/schema JsonElement values remain ordinary JSON.
/// Decoding verifies protocol shape and constructs immutable data. It does not establish source, claim or target authority.
/// </remarks>
public static class StimulusIngressSourceCodec
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Encode the family header with the same wire format as canonical ingress persistence.</summary>
    public static JsonElement Encode(StimulusIngressSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return JsonSerializer.SerializeToElement(source, Options);
    }

    /// <summary>Read one complete header; reject missing, duplicated, unknown or contradictory protocol data.</summary>
    public static StimulusIngressSource Decode(JsonElement header)
    {
        if (header.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("An ingress source header must be an object.");
        try
        {
            var source = JsonSerializer.Deserialize<StimulusIngressSource>(header, Options)
                ?? throw new InvalidDataException("The ingress source header was empty.");
            CheckShape(header, Encode(source), "$", false);
            return source;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException)
        {
            throw new InvalidDataException("The ingress source header does not use its declared protocol.", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new OpaqueStringConverter());
        return options;
    }

    private sealed class OpaqueStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadString(ref reader);
        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => ReadString(ref reader);
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(EncodeString(value));
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WritePropertyName(EncodeString(value));

        private static string ReadString(ref Utf8JsonReader reader)
        {
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName))
                throw new JsonException("An opaque ingress string must use its encoded string form.");
            var encoded = reader.GetString() ?? throw new JsonException("An opaque ingress string was null.");
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length % sizeof(char) != 0)
                throw new JsonException("An opaque ingress string contains incomplete UTF-16 code units.");
            var chars = new char[bytes.Length / sizeof(char)];
            for (var index = 0; index < chars.Length; index++)
                chars[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)));
            return new string(chars);
        }

        private static string EncodeString(string value)
        {
            var bytes = new byte[checked(value.Length * sizeof(char))];
            for (var index = 0; index < value.Length; index++)
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)), value[index]);
            return Convert.ToBase64String(bytes);
        }
    }

    private static void CheckShape(JsonElement original, JsonElement known, string path, bool dictionary)
    {
        // JsonElement payload/schema are ordinary Elsa data, not a protocol-extension channel. Their
        // content survives deserialization as-is and participates in the semantic request fingerprint.
        if (path is "$.request.input" or "$.request.payloadType.schema") return;
        if (original.ValueKind != known.ValueKind)
            throw new InvalidDataException("An ingress field does not use its declared wire shape.");
        if (known.ValueKind == JsonValueKind.Object)
        {
            var comparer = dictionary ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            var properties = original.EnumerateObject().ToArray();
            var expected = known.EnumerateObject().ToArray();
            if (properties.Length != expected.Length ||
                properties.GroupBy(property => property.Name, comparer).Any(group => group.Count() != 1))
                throw new InvalidDataException("Ingress protocol fields are missing, duplicated or unknown.");
            foreach (var field in expected)
            {
                var matches = properties.Where(property => comparer.Equals(property.Name, field.Name)).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException("Ingress protocol fields are missing or unknown.");
                CheckShape(matches[0].Value, field.Value, path + "." + field.Name,
                    field.Name.Equals("metadata", StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (known.ValueKind == JsonValueKind.Array)
        {
            var elements = original.EnumerateArray().ToArray();
            var expected = known.EnumerateArray().ToArray();
            if (elements.Length != expected.Length)
                throw new InvalidDataException("Ingress protocol collections changed while reading.");
            for (var index = 0; index < elements.Length; index++)
                CheckShape(elements[index], expected[index], path + "[]", false);
        }
    }
}
