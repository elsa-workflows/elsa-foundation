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

        // A nested collection names its element under the innermost alias (spec 1228 US5): a dictionary of string arrays
        // is a String dictionary whose element is a String array. It is structured, as it was when its alias was
        // System.String[]; a String type without an element is inferred as before.
        if (StringComparer.OrdinalIgnoreCase.Equals(alias, "String") && type.Element is null)
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
