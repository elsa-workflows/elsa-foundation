using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Testing.Core.Serialization;

// A missing optional property is C# null; a present JSON null is a real JsonElement.
// Attach this to the model so every serializer, including retained receipts, preserves that distinction.
internal sealed class OptionalJsonElementConverter : JsonConverter<JsonElement?>
{
    public override bool HandleNull => true;

    public override JsonElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
    {
        if (value is { } element) element.WriteTo(writer);
        else writer.WriteNullValue();
    }
}
