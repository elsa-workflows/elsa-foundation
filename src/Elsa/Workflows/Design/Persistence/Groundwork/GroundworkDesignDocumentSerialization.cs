using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>JSON options for the public-v2 workflow-design payload rows.</summary>
public static class GroundworkDesignDocumentSerialization
{
    private static readonly string[] ExcludedMembers =
    [
        "RowNumber",
        "StateSource",
        "Definition",
        "WorkflowDefinition",
        "WorkflowDefinitionVersion",
        "WorkflowDefinitionDraft"
    ];

    public static JsonSerializerOptions Create(IPayloadSerializer payloadSerializer)
    {
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        var options = CreateOptions();
        options.Converters.Add(new PayloadDelegatingConverterFactory(payloadSerializer));
        return options;
    }

    private static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new GroundworkDesignTypeInfoResolver()
    };

    private sealed class GroundworkDesignTypeInfoResolver : IJsonTypeInfoResolver
    {
        private readonly DefaultJsonTypeInfoResolver inner = new();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var typeInfo = inner.GetTypeInfo(type, options);
            if (typeInfo?.Kind != JsonTypeInfoKind.Object)
                return typeInfo;
            foreach (var property in typeInfo.Properties)
            {
                if (ExcludedMembers.Contains(property.Name, StringComparer.OrdinalIgnoreCase) ||
                    property.AttributeProvider is PropertyInfo member &&
                    ExcludedMembers.Contains(member.Name, StringComparer.OrdinalIgnoreCase))
                {
                    property.ShouldSerialize = static (_, _) => false;
                }
            }
            return typeInfo;
        }
    }

    private sealed class PayloadDelegatingConverterFactory(IPayloadSerializer payloadSerializer) : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(WorkflowDefinitionState);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            new PayloadDelegatingConverter(payloadSerializer);
    }

    private sealed class PayloadDelegatingConverter(IPayloadSerializer payloadSerializer)
        : JsonConverter<WorkflowDefinitionState>
    {
        public override WorkflowDefinitionState Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            return payloadSerializer.Deserialize<WorkflowDefinitionState>(document.RootElement);
        }

        public override void Write(
            Utf8JsonWriter writer,
            WorkflowDefinitionState value,
            JsonSerializerOptions options) =>
            payloadSerializer.SerializeToElement(value).WriteTo(writer);
    }
}
