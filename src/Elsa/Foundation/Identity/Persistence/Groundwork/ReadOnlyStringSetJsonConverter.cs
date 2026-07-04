using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.Persistence.Groundwork;

/// <summary>
/// Serializes <see cref="IReadOnlySet{T}"/> string sets as JSON arrays and materializes them back into a
/// concrete <see cref="HashSet{T}"/> on read. The identity records expose their collections as
/// <c>IReadOnlySet&lt;string&gt;</c>, which System.Text.Json cannot instantiate directly; without this
/// converter the durable stores could write documents they could never read back.
/// </summary>
internal sealed class ReadOnlyStringSetJsonConverter : JsonConverter<IReadOnlySet<string>>
{
    public override IReadOnlySet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return new HashSet<string>();

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected a JSON array for '{typeToConvert}' but found {reader.TokenType}.");

        var set = new HashSet<string>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                return set;

            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Expected string array elements but found {reader.TokenType}.");

            set.Add(reader.GetString()!);
        }

        throw new JsonException("Unterminated JSON array while reading a string set.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlySet<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        // Sort ordinally so the serialized shape is deterministic regardless of set enumeration order,
        // which keeps golden fixtures stable across runs.
        foreach (var item in value.OrderBy(x => x, StringComparer.Ordinal))
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
