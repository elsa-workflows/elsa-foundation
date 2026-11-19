using System.Text.Json.Serialization;

namespace Elsa.Primitives.Models;

/// <summary>
/// A rename-proof reference to an authored argument's type: a stable element-type <see cref="Alias"/>, a
/// <see cref="CollectionKind"/> shape and, for a nested collection, the <see cref="Element"/> type the collection
/// holds. This is the sole way an authored Variable/Input/Output type is stored or transferred — no namespace,
/// assembly name, or assembly version. Serializes natively (camelCase) via the configured payload serializer.
/// </summary>
/// <remarks>
/// A collection of collections names its element: <c>IDictionary&lt;string, string[]&gt;</c> is kind
/// <see cref="CollectionKind.Dictionary"/> with element <c>{ String, Array }</c>. The alias sits on the innermost
/// element, and every enclosing reference repeats it, so a reader that only looks at <see cref="Alias"/> still sees
/// the innermost element type. <see cref="Element"/> is null for a single value and a flat collection, and is then
/// left out of the serialized form (spec 1228 FR-022).
/// </remarks>
public sealed record TypeReference(
    string Alias,
    CollectionKind CollectionKind = CollectionKind.Single,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TypeReference? Element = null)
{
    private readonly string _alias = TypeAliasConvention.ReadAlias(Alias);

    /// <summary>
    /// The element type's alias. <c>Object</c>, <c>System.Object</c> and <c>Elsa.Any</c> read as <c>Any</c>, the
    /// dynamic kind, so a definition naming them loads as <c>Any</c> (spec 1228 FR-018).
    /// </summary>
    /// <remarks>Declared in the body, so it is ordered first explicitly to keep the serialized member order.</remarks>
    [JsonPropertyOrder(-1)]
    public string Alias
    {
        get => _alias;
        init => _alias = TypeAliasConvention.ReadAlias(value);
    }

    /// <summary>
    /// The full shape as one readable name for diagnostics, for example <c>Dictionary&lt;String, String[]&gt;</c>.
    /// </summary>
    public string ToDisplayString()
    {
        var element = Element?.ToDisplayString() ?? Alias;
        return CollectionKind switch
        {
            CollectionKind.Array => $"{element}[]",
            CollectionKind.List => $"List<{element}>",
            CollectionKind.HashSet => $"HashSet<{element}>",
            CollectionKind.Dictionary => $"Dictionary<String, {element}>",
            _ => element
        };
    }
}
