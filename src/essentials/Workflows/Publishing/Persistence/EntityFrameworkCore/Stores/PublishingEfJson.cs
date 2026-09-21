using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Lossless JSON material for the receipt kinds that carry nested diagnostics. Every string is written as its
/// framed UTF-16 projection, so an accepted lone surrogate or NUL survives the provider unchanged instead of
/// being replaced by an encoder.
/// </summary>
internal static class PublishingEfJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string content, string subject)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(content, Options)
                   ?? throw new InvalidOperationException($"Malformed persisted publication state: the {subject} material is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"Malformed persisted publication state: the {subject} material is unreadable.", exception);
        }
    }

    /// <summary>Whether two values serialize to the same material, regardless of member order.</summary>
    public static bool SameMaterial<T>(T left, T right) =>
        JsonNode.DeepEquals(JsonNode.Parse(Serialize(left)), JsonNode.Parse(Serialize(right)));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.Converters.Add(new LosslessUtf16StringConverter());
        return options;
    }

    private sealed class LosslessUtf16StringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Decode(ref reader);

        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Decode(ref reader);

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(EfRelationalIdentity.Encode(value));

        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WritePropertyName(EfRelationalIdentity.Encode(value));

        private static string Decode(ref Utf8JsonReader reader) =>
            EfRelationalIdentity.Decode(reader.GetString() ?? throw new JsonException("A publication string value was null."));
    }
}
