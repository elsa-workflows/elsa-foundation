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
        // is a String dictionary whose element is a String array. It is structured, as it was when its alias was the
        // element's array name (System.String[], System.Byte[]); a type without an element is inferred as before.
        if (type.Element is not null)
            return ValueRepresentation.StructuredValue;
        if (StringComparer.OrdinalIgnoreCase.Equals(alias, "String"))
            return ValueRepresentation.TextValue;
        if (StringComparer.OrdinalIgnoreCase.Equals(alias, "Byte") && type.CollectionKind is CollectionKind.Array or CollectionKind.List)
            return ValueRepresentation.BinaryContent;
        if (StringComparer.OrdinalIgnoreCase.Equals(alias, "Stream"))
            return ValueRepresentation.TransientResource;
        if (type.CollectionKind != CollectionKind.Single)
            return ValueRepresentation.StructuredValue;
        if (StructuredAliases.Contains(alias))
            return ValueRepresentation.StructuredValue;

        return ValueRepresentation.TypedValue;
    }

    // Compared after the leading "System." is stripped, so "System.Text.Json.JsonElement?" matches
    // "Text.Json.JsonElement?". JsonElement is a struct, so its nullable aliases carry a trailing "?".
    // The namespaced JsonObject, JsonArray and JsonValue are left out on purpose: their JSON kind is known at publish,
    // so they may only infer StructuredValue once publish refuses a kind that can never fit the target (#4844).
    private static readonly HashSet<string> StructuredAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "Any",
        "Elsa.Any",
        "JsonNode",
        "JsonObject",
        "JsonElement",
        "JsonElement?",
        "Text.Json.JsonElement",
        "Text.Json.JsonElement?",
        "Text.Json.Nodes.JsonNode"
    };
}
