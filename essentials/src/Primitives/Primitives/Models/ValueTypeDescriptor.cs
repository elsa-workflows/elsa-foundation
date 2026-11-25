using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Primitives.Models;

/// <summary>
/// Portable, assembly-independent type and optional schema identity for a workflow value.
/// </summary>
public sealed record ValueTypeDescriptor
{
    public ValueTypeDescriptor(
        string alias,
        CollectionKind collectionKind = CollectionKind.Single,
        JsonElement? schema = null,
        int? schemaVersion = null,
        TypeReference? element = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (alias.Contains(',', StringComparison.Ordinal))
            throw new ArgumentException("A value type alias cannot contain assembly-qualified type metadata.", nameof(alias));

        if (schemaVersion is <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "A schema version must be positive.");

        if (element is not null && collectionKind == CollectionKind.Single)
            throw new ArgumentException("A single value has no nested element type.", nameof(element));

        // Object, System.Object and Elsa.Any read as Any, the dynamic kind (spec 1228 FR-018).
        Alias = TypeAliasConvention.ReadAlias(alias);
        CollectionKind = collectionKind;
        Schema = schema?.Clone();
        SchemaVersion = schemaVersion;
        Element = element;
    }

    public string Alias { get; }
    public CollectionKind CollectionKind { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Schema { get; }
    public int? SchemaVersion { get; }

    /// <summary>
    /// The nested element type of a collection of collections (spec 1228 FR-022); null otherwise, and then left out of
    /// the serialized form. See <see cref="TypeReference.Element"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TypeReference? Element { get; }

    public TypeReference ToTypeReference() => new(Alias, CollectionKind, Element);

    /// <summary>The descriptor of <paramref name="type"/>, nested element included, with no schema.</summary>
    public static ValueTypeDescriptor FromTypeReference(TypeReference type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new(type.Alias, type.CollectionKind, element: type.Element);
    }

    /// <summary>The full shape as one readable name for diagnostics. See <see cref="TypeReference.ToDisplayString"/>.</summary>
    public string ToDisplayString() => ToTypeReference().ToDisplayString();
}
