using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.SystemText.JsonConverters;

/// <summary>
/// The generic worker behind <see cref="DeterministicDictionaryConverterFactory"/> for a
/// <see cref="string"/>-keyed dictionary of <typeparamref name="TValue"/>. Writes entries sorted by ordinal
/// key; reads them back into a <see cref="Dictionary{TKey,TValue}"/> (assignable to the concrete and
/// interface shapes the factory claims). Values are (de)serialized through the full options, so nested
/// determinism — sorted nested dictionaries, fixed member order — is preserved.
/// </summary>
public sealed class DeterministicDictionaryConverter<TValue> : JsonConverter<IReadOnlyDictionary<string, TValue>>
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        // The factory has already vetted the shape; claim every dictionary interface/concrete type it routes here.
        return true;
    }

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object.");

        var dictionary = new Dictionary<string, TValue>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return dictionary;

            var key = reader.GetString()!;
            reader.Read();
            dictionary[key] = JsonSerializer.Deserialize<TValue>(ref reader, options)!;
        }

        throw new JsonException("Expected end of object.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, TValue> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Ordinal by key. Keys are written verbatim (DictionaryKeyPolicy is unset), so the key is the
        // serialized property name and sorting by key equals sorting by serialized name (FR-001).
        foreach (var entry in value.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(entry.Key);
            JsonSerializer.Serialize(writer, entry.Value, options);
        }

        writer.WriteEndObject();
    }
}
