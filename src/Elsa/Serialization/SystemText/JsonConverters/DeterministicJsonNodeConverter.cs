using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Serialization.SystemText.JsonConverters;

/// <summary>
/// Canonicalizes embedded JSON (<see cref="JsonElement"/> and <see cref="JsonNode"/>) on write by recursively
/// sorting object keys ordinally (array order preserved). System.Text.Json writes these verbatim in parse
/// order, so without this an embedded payload's key order leaks into the bytes — defeating the deterministic
/// serializer wherever state embeds raw JSON (spec 086 gap #555; e.g. <c>ActivityNode.Structure.Payload</c>
/// and the opaque designer bags in ADR 0035 D3). ADR 0034 D3/D7: the content hash must be order-invariant.
/// </summary>
/// <remarks>
/// Applied via <see cref="Modifier"/>, which attaches the canonicalizing converter to each
/// <see cref="JsonElement"/>/<see cref="JsonNode"/>-typed <em>property</em> — NOT as a global converter for
/// those types. A global converter for <see cref="JsonElement"/>/<see cref="JsonNode"/> collides with
/// <see cref="PolymorphicObjectConverter"/>, which leans on the default handling of both as internal buffer
/// types (it stack-overflows the object-graph read path). Property-scoped attachment leaves that untouched and
/// only rewrites explicit embedded-JSON members — exactly where StateSource carries raw JSON. The rewrite only
/// reorders object members, so reads stay lossless.
/// </remarks>
public static class DeterministicJsonNodeConverter
{
    private static readonly ElementConverter ForElement = new();
    private static readonly NodeConverter ForNode = new();

    /// <summary>
    /// A <see cref="DefaultJsonTypeInfoResolver"/> modifier that attaches the canonicalizing converter to every
    /// <see cref="JsonElement"/>/<see cref="JsonNode"/>-typed member of an object contract.
    /// </summary>
    public static void Modifier(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
        {
            if (property.PropertyType == typeof(JsonElement))
                property.CustomConverter = ForElement;
            else if (typeof(JsonNode).IsAssignableFrom(property.PropertyType))
                property.CustomConverter = ForNode;
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private sealed class ElementConverter : JsonConverter<JsonElement>
    {
        public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonElement.ParseValue(ref reader);

        public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
            => WriteCanonical(writer, value);
    }

    private sealed class NodeConverter : JsonConverter<JsonNode>
    {
        // The concrete DOM types (JsonObject/JsonArray/JsonValue) all route here via the base type.
        public override bool CanConvert(Type typeToConvert) => typeof(JsonNode).IsAssignableFrom(typeToConvert);

        public override JsonNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => JsonNode.Parse(ref reader);

        public override void Write(Utf8JsonWriter writer, JsonNode? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            // Render the node to JSON verbatim (its keys are opaque data — no naming policy), then canonicalize
            // that in one recursion. ToJsonString avoids re-entering this converter and needs no options clone.
            using var document = JsonDocument.Parse(value.ToJsonString());
            WriteCanonical(writer, document.RootElement);
        }
    }
}
