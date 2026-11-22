using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>Only this new mapping value distinguishes a missing member from a present JSON null.</summary>
internal sealed class BpmnModelInputValueJsonConverter : JsonConverter<JsonElement?>
{
    public BpmnModelInputValueJsonConverter() { }
    public override bool HandleNull => true;
    public override JsonElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.Clone();
    }
    public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
    {
        if (value.HasValue) value.Value.WriteTo(writer);
        else writer.WriteNullValue();
    }
}
