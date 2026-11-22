using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

// Nullable<JsonElement> normally reads JSON null as no value. Inline storage requires a present payload,
// including JSON null; external/none storage's legacy inlineValue:null means no inline payload.
internal sealed class DurableValueStateJsonConverter : JsonConverter<DurableValueState>
{
    public DurableValueStateJsonConverter() { }

    public override DurableValueState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Durable value state must be an object.");
        JsonElement? Property(string name)
        {
            var jsonName = options.PropertyNamingPolicy?.ConvertName(name) ?? name;
            JsonElement? result = null;
            foreach (var property in root.EnumerateObject())
                if (string.Equals(property.Name, jsonName, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    result = property.Value;
            return result;
        }
        T? ReadProperty<T>(string name) => Property(name) is { } property ? property.Deserialize<T>(options) : default;
        var storage = ReadProperty<DurableValueStorage>(nameof(DurableValueState.Storage));
        var payload = Property(nameof(DurableValueState.InlineValue));
        var inline = payload is { } value && (value.ValueKind != JsonValueKind.Null || storage == DurableValueStorage.Inline)
            ? value.Clone() : (JsonElement?)null;
        return new DurableValueState(
            ReadProperty<string>(nameof(DurableValueState.DurableValueId))!,
            ReadProperty<string>(nameof(DurableValueState.WorkflowExecutionId))!,
            ReadProperty<string>(nameof(DurableValueState.ValueId))!,
            ReadProperty<RuntimeValueTypeDescriptor>(nameof(DurableValueState.Type))!,
            ReadProperty<DurableValueLifecycle>(nameof(DurableValueState.Lifecycle)), storage, inline,
            ReadProperty<DurableValueExternalReference>(nameof(DurableValueState.ExternalReference)),
            ReadProperty<string>(nameof(DurableValueState.SourceActivityExecutionId)),
            ReadProperty<DateTimeOffset>(nameof(DurableValueState.CapturedAt)),
            ReadProperty<IReadOnlyDictionary<string, string>>(nameof(DurableValueState.Metadata))!);
    }

    public override void Write(Utf8JsonWriter writer, DurableValueState value, JsonSerializerOptions options)
    {
        void WriteProperty<T>(string name, T item)
        {
            if (options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull && item is null ||
                options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingDefault && EqualityComparer<T>.Default.Equals(item, default))
                return;
            writer.WritePropertyName(options.PropertyNamingPolicy?.ConvertName(name) ?? name);
            JsonSerializer.Serialize(writer, item, options);
        }
        writer.WriteStartObject();
        WriteProperty(nameof(value.DurableValueId), value.DurableValueId);
        WriteProperty(nameof(value.WorkflowExecutionId), value.WorkflowExecutionId);
        WriteProperty(nameof(value.ValueId), value.ValueId);
        WriteProperty(nameof(value.Type), value.Type);
        WriteProperty(nameof(value.Lifecycle), value.Lifecycle);
        WriteProperty(nameof(value.Storage), value.Storage);
        WriteProperty(nameof(value.InlineValue), value.InlineValue);
        WriteProperty(nameof(value.ExternalReference), value.ExternalReference);
        WriteProperty(nameof(value.SourceActivityExecutionId), value.SourceActivityExecutionId);
        WriteProperty(nameof(value.CapturedAt), value.CapturedAt);
        WriteProperty(nameof(value.Metadata), value.Metadata);
        writer.WriteEndObject();
    }
}
