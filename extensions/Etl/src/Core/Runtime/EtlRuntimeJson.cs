using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Etl.Core.Runtime;

/// <summary>
/// Serializes private ETL runtime documents without losing opaque UTF-16 code units.
/// </summary>
internal static class EtlRuntimeJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static JsonElement WriteElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    public static string ResultEnvelopeHash(string invocationJson, string resultJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("etl-result-envelope-v1|" + invocationJson + "|" + resultJson))).ToLowerInvariant();

    public static T Read<T>(string json, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(json);
        ValidateMaximumBytes(maximumBytes);
        EnsureWithinBound<T>(json, maximumBytes);

        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidDataException($"The ETL {typeof(T).Name} document is empty.");
    }

    public static T Read<T>(JsonElement element, int maximumBytes)
    {
        ValidateMaximumBytes(maximumBytes);
        if (element.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("The ETL runtime document is missing.");
        if (System.Runtime.InteropServices.JsonMarshal.GetRawUtf8Value(element).Length > maximumBytes)
            throw new InvalidDataException($"The ETL {typeof(T).Name} document exceeds its size bound.");

        return element.Deserialize<T>(Options)
            ?? throw new InvalidDataException($"The ETL {typeof(T).Name} document is empty.");
    }

    private static void EnsureWithinBound<T>(string json, int maximumBytes)
    {
        if (json.Length > maximumBytes || Encoding.UTF8.GetByteCount(json) > maximumBytes)
            throw new InvalidDataException($"The ETL {typeof(T).Name} document exceeds its size bound.");
    }

    private static void ValidateMaximumBytes(int maximumBytes)
    {
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "The ETL document size bound must be positive.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            PropertyNameCaseInsensitive = false
        };
        options.Converters.Add(new LosslessStringJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    // Fixed property names and authored JsonElement content are not transformed. No ordinary
    // JSON string fallback is accepted for CLR string values.
    private sealed class LosslessStringJsonConverter : JsonConverter<string>
    {
        private const string Prefix = "u16:";

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("The ETL string representation is invalid.");
            var value = reader.GetString();
            if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
                throw new JsonException("The ETL string representation is invalid.");

            try
            {
                var encoded = value[Prefix.Length..];
                var bytes = Convert.FromBase64String(encoded);
                if ((bytes.Length & 1) != 0 || !StringComparer.Ordinal.Equals(Convert.ToBase64String(bytes), encoded))
                    throw new FormatException("The ETL UTF-16 representation is not canonical.");

                var chars = new char[bytes.Length / sizeof(char)];
                for (var index = 0; index < chars.Length; index++)
                    chars[index] = (char)(bytes[index * sizeof(char)] | (bytes[index * sizeof(char) + 1] << 8));
                return new string(chars);
            }
            catch (FormatException exception)
            {
                throw new JsonException("The ETL string representation is invalid.", exception);
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            var bytes = new byte[checked(value.Length * sizeof(char))];
            for (var index = 0; index < value.Length; index++)
            {
                var codeUnit = value[index];
                bytes[index * sizeof(char)] = (byte)codeUnit;
                bytes[index * sizeof(char) + 1] = (byte)(codeUnit >> 8);
            }

            writer.WriteStringValue(Prefix + Convert.ToBase64String(bytes));
        }
    }
}
