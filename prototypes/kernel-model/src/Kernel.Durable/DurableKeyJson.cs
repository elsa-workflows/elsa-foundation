using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kernel.Durable;

/// <summary>Opaque kernel keys use ordinary JSON strings whenever representable. For an isolated UTF-16
/// surrogate, a one-element binary array preserves each original code unit without reserving any string value.</summary>
public sealed class DurableKeyJson : JsonConverter<string>
{
    private static readonly JsonSerializerOptions Keys = new() { Converters = { new DurableKeyJson() } };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Keys);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Keys)!;
    public static string WriteKeys(IEnumerable<string> keys) => Write(keys);
    public static List<string> ReadKeys(string json) => Read<List<string>>(json);

    // JSON property names cannot be arrays. Use a reversible tag only for a tagged name or
    // a value needing binary storage. Both branches are injective, including arbitrary '~' names.
    public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.StartsWith('~') || HasUnpairedSurrogate(value) ? "~" + Convert.ToBase64String(CodeUnits(value)) : value);

    public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        var property = reader.GetString()!;
        return property.StartsWith('~') ? FromCodeUnits(Convert.FromBase64String(property[1..])) : property;
    }

    public override string Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return reader.GetString()!;
        if (reader.TokenType != JsonTokenType.StartArray || !reader.Read() || reader.TokenType != JsonTokenType.String ||
            !reader.TryGetBytesFromBase64(out var bytes) || (bytes.Length & 1) != 0 ||
            !reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Invalid durable key.");
        return FromCodeUnits(bytes);
    }

    public static string FromCodeUnits(byte[] bytes)
    {
        if ((bytes.Length & 1) != 0) throw new JsonException("An encoded key must contain complete UTF-16 units.");
        return string.Create(bytes.Length / 2, bytes, (chars, data) =>
        {
            for (int i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i * 2));
        });
    }

    public static bool HasUnpairedSurrogate(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i])) continue;
            if (i + 1 < value.Length && char.IsSurrogatePair(value[i], value[i + 1])) { i++; continue; }
            return true;
        }
        return false;
    }

    public static byte[] CodeUnits(string value)
    {
        var bytes = new byte[checked(value.Length * 2)];
        for (int i = 0; i < value.Length; i++) BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), value[i]);
        return bytes;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        if (HasUnpairedSurrogate(value))
        {
            writer.WriteStartArray();
            writer.WriteBase64StringValue(CodeUnits(value));
            writer.WriteEndArray();
            return;
        }
        writer.WriteStringValue(value);
    }
}
