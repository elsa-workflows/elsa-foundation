using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Expressions.Core.Helpers;

/// <summary>
/// Materializes a dynamic (<c>Any</c>) value into its canonical runtime representation — a mutable
/// <see cref="JsonNode"/> (ADR 0036).
/// </summary>
/// <remarks>
/// A schemaless <c>Any</c> value is stored as opaque JSON and round-trips through the durable store as a
/// read-only <see cref="JsonElement"/>. Expression engines bind <see cref="JsonNode"/> — Jint natively, and
/// Fluid/Liquid <em>only</em> for <see cref="JsonNode"/>, not <see cref="JsonElement"/> — so the stored
/// element is lifted to a <see cref="JsonNode"/> once at the engine binding seam. A value that is already a
/// <see cref="JsonNode"/> (for example a freshly parsed HTTP payload) or a concrete CLR type passes through
/// unchanged. The lift is a structural copy, never a re-serialization of the author's bytes, so it does not
/// disturb the stored opaque JSON (ADR 0035 D3).
/// </remarks>
public static class DynamicValueMaterializer
{
    /// <summary>
    /// Returns <paramref name="value"/> as its canonical dynamic representation: a stored
    /// <see cref="JsonElement"/> becomes a <see cref="JsonNode"/>; everything else is returned unchanged.
    /// </summary>
    public static object? Materialize(object? value) => value switch
    {
        JsonElement element => MaterializeElement(element),
        _ => value
    };

    private static JsonNode? MaterializeElement(JsonElement element) =>
        element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : element.Deserialize<JsonNode>();
}
