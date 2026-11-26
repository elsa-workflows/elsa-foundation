using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Http.Serialization;

/// <summary>
/// Reads a header map whose values are either one string or an array of strings, so <c>{"X-Name": "v"}</c> reads as
/// <c>{"X-Name": ["v"]}</c>. Elsa 3 header maps accepted both forms. Writing always uses arrays.
/// </summary>
/// <remarks>
/// Handles <see cref="Dictionary{TKey,TValue}"/>, <see cref="IDictionary{TKey,TValue}"/> and
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> of <see cref="string"/> to <see cref="string"/> arrays. An array value
/// and any other token read exactly as they would without this converter.
/// </remarks>
public sealed class HttpHeaderValuesJsonConverter : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert == typeof(Dictionary<string, string[]>) ||
        typeToConvert == typeof(IDictionary<string, string[]>) ||
        typeToConvert == typeof(IReadOnlyDictionary<string, string[]>);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(HeaderMapConverter<>).MakeGenericType(typeToConvert))!;

    // Every key is a header name, so the unmatched-member scan has nothing to report and inspects each value as an array.
    private sealed class HeaderMapConverter<T> : JsonConverter<T>, IJsonConversionMemberMatcher
        where T : class, IEnumerable<KeyValuePair<string, string[]>>
    {
        public bool TryGetMemberType(string name, out Type memberType)
        {
            memberType = typeof(string[]);
            return true;
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected a JSON object of header values, but found '{reader.TokenType}'.");

            var headers = new Dictionary<string, string[]>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString()!;
                reader.Read();
                var values = reader.TokenType == JsonTokenType.String
                    ? [reader.GetString()!]
                    : JsonSerializer.Deserialize<string[]>(ref reader, options)!;
                // Duplicate names follow the options, as an ordinary dictionary read does.
                if (!headers.TryAdd(name, values))
                {
                    if (!options.AllowDuplicateProperties)
                        throw new JsonException($"Header '{name}' appears more than once.");
                    headers[name] = values;
                }
            }

            return (T)(object)headers;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (name, values) in value)
            {
                writer.WritePropertyName(name);
                JsonSerializer.Serialize(writer, values, options);
            }

            writer.WriteEndObject();
        }
    }
}
