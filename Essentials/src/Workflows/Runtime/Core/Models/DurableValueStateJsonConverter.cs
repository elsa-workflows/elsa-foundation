using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

// Nullable<JsonElement> normally reads JSON null as no value. Only Inline storage requires that null
// to remain a present payload. Delegate the ordinary shape to STJ so host options/converters still apply.
internal sealed class DurableValueStateJsonConverter : JsonConverter<DurableValueState>
{
    public DurableValueStateJsonConverter() { }

    public override DurableValueState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var shape = document.RootElement.Deserialize<SerializationShape>(options)
            ?? throw new JsonException("Durable value state must be an object.");
        var inline = shape.InlineValue;
        if (inline is null && shape.Storage == DurableValueStorage.Inline)
        {
            var name = options.PropertyNamingPolicy?.ConvertName(nameof(DurableValueState.InlineValue)) ?? nameof(DurableValueState.InlineValue);
            JsonElement? raw = null;
            foreach (var property in document.RootElement.EnumerateObject())
                if (string.Equals(property.Name, name, options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    raw = property.Value;
            if (raw is { ValueKind: JsonValueKind.Null } value) inline = value.Clone();
        }
        return new DurableValueState(shape.DurableValueId, shape.WorkflowExecutionId, shape.ValueId, shape.Type,
            shape.Lifecycle, shape.Storage, inline, shape.ExternalReference, shape.SourceActivityExecutionId, shape.CapturedAt, shape.Metadata);
    }

    public override void Write(Utf8JsonWriter writer, DurableValueState value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new SerializationShape(value), options);

    // Match the model's constructor nullability/required parameters and get-only properties. The serializer
    // enforces required/null/unknown-member rules and its scalar/collection ignore behavior on this shape.
    private sealed class SerializationShape
    {
        [JsonConstructor]
        public SerializationShape(string durableValueId, string workflowExecutionId, string valueId, RuntimeValueTypeDescriptor type,
            DurableValueLifecycle lifecycle, DurableValueStorage storage, JsonElement? inlineValue,
            DurableValueExternalReference? externalReference, string? sourceActivityExecutionId, DateTimeOffset capturedAt,
            IReadOnlyDictionary<string, string> metadata)
        {
            DurableValueId = durableValueId; WorkflowExecutionId = workflowExecutionId; ValueId = valueId; Type = type;
            Lifecycle = lifecycle; Storage = storage; InlineValue = inlineValue; ExternalReference = externalReference;
            SourceActivityExecutionId = sourceActivityExecutionId; CapturedAt = capturedAt; Metadata = metadata;
        }

        public SerializationShape(DurableValueState value) : this(value.DurableValueId, value.WorkflowExecutionId, value.ValueId,
            value.Type, value.Lifecycle, value.Storage, value.InlineValue, value.ExternalReference, value.SourceActivityExecutionId,
            value.CapturedAt, value.Metadata) { }

        public string DurableValueId { get; }
        public string WorkflowExecutionId { get; }
        public string ValueId { get; }
        public RuntimeValueTypeDescriptor Type { get; }
        public DurableValueLifecycle Lifecycle { get; }
        public DurableValueStorage Storage { get; }
        public JsonElement? InlineValue { get; }
        public DurableValueExternalReference? ExternalReference { get; }
        public string? SourceActivityExecutionId { get; }
        public DateTimeOffset CapturedAt { get; }
        public IReadOnlyDictionary<string, string> Metadata { get; }
    }
}
