using System.Text.Json.Serialization;

namespace Elsa.Primitives.Models;

/// <summary>Describes the durable form of a value at a workflow value-flow seam.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ValueRepresentation>))]
public enum ValueRepresentation
{
    TypedValue,
    StructuredValue,
    TextValue,
    FormattedContent,
    BinaryContent,
    DurableReference,
    TransientResource
}

/// <summary>Infers conservative defaults for value-flow contracts without inspecting runtime payloads.</summary>
public static class ValueRepresentationDefaults
{
    public static ValueRepresentation Infer(ValueTypeDescriptor type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var alias = type.Alias.StartsWith("System.", StringComparison.Ordinal)
            ? type.Alias["System.".Length..]
            : type.Alias;

        if (StringComparer.OrdinalIgnoreCase.Equals(alias, "String"))
            return ValueRepresentation.TextValue;
        if (StringComparer.OrdinalIgnoreCase.Equals(alias, "Byte") && type.CollectionKind is CollectionKind.Array or CollectionKind.List)
            return ValueRepresentation.BinaryContent;
        if (StringComparer.OrdinalIgnoreCase.Equals(alias, "Stream"))
            return ValueRepresentation.TransientResource;
        if (type.CollectionKind != CollectionKind.Single)
            return ValueRepresentation.StructuredValue;
        if (StringComparer.OrdinalIgnoreCase.Equals(alias, "Any") ||
            StringComparer.OrdinalIgnoreCase.Equals(alias, "Elsa.Any") ||
            StringComparer.OrdinalIgnoreCase.Equals(alias, "JsonNode") ||
            StringComparer.OrdinalIgnoreCase.Equals(alias, "JsonObject") ||
            StringComparer.OrdinalIgnoreCase.Equals(alias, "JsonElement"))
            return ValueRepresentation.StructuredValue;

        return ValueRepresentation.TypedValue;
    }
}
