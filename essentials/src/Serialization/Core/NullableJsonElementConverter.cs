using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core;

/// <summary>
/// Preserves an explicit JSON null as a present element, distinct from an absent nullable value.
/// </summary>
public sealed class NullableJsonElementConverter : JsonConverter<JsonElement?>
{
    public override bool HandleNull => true;

    public override JsonElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
    {
        if (value is { } element)
            writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
        else
            writer.WriteNullValue();
    }
}
