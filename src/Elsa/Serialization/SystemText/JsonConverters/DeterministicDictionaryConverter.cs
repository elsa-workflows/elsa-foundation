using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.SystemText.JsonConverters;

/// <summary>
/// The generic worker behind <see cref="DeterministicDictionaryConverterFactory"/> for a
/// <see cref="string"/>-keyed dictionary <typeparamref name="TDictionary"/> of <typeparamref name="TValue"/>.
/// Writes entries sorted by ordinal key; reads them back into a <see cref="Dictionary{TKey,TValue}"/>.
/// Values are (de)serialized through the full options, so nested determinism — sorted nested dictionaries,
/// fixed member order — is preserved.
/// </summary>
/// <remarks>
/// It is generic over the exact <typeparamref name="TDictionary"/> the factory routes here (the concrete
/// <see cref="Dictionary{TKey,TValue}"/> OR the <see cref="IDictionary{TKey,TValue}"/> /
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> interfaces), so the converter's declared type always matches
/// the requested type. Typing it to one fixed shape (e.g. <see cref="IReadOnlyDictionary{TKey,TValue}"/>) made
/// System.Text.Json reject it for the other shapes with a converter-incompatibility exception. Every shape is
/// materialized as a <see cref="Dictionary{TKey,TValue}"/> on read, which is assignable to all three.
/// </remarks>
public sealed class DeterministicDictionaryConverter<TDictionary, TValue> : JsonConverter<TDictionary>
    where TDictionary : IEnumerable<KeyValuePair<string, TValue>>
{
    /// <inheritdoc />
    public override TDictionary Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected start of object.");

        var dictionary = new Dictionary<string, TValue>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return (TDictionary)(object)dictionary;

            var key = reader.GetString()!;
            reader.Read();
            dictionary[key] = JsonSerializer.Deserialize<TValue>(ref reader, options)!;
        }

        throw new JsonException("Expected end of object.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TDictionary value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        // Ordinal by key. Keys are written verbatim (DictionaryKeyPolicy is unset), so the key is the
        // serialized property name and sorting by key equals sorting by serialized name (FR-001).
        foreach (var entry in value.InCanonicalOrder(entry => entry.Key))
        {
            writer.WritePropertyName(entry.Key);
            JsonSerializer.Serialize(writer, entry.Value, options);
        }

        writer.WriteEndObject();
    }
}
