using System.Text.Json;

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
        int? schemaVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        if (alias.Contains(',', StringComparison.Ordinal))
            throw new ArgumentException("A value type alias cannot contain assembly-qualified type metadata.", nameof(alias));

        if (schemaVersion is <= 0)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "A schema version must be positive.");

        Alias = alias;
        CollectionKind = collectionKind;
        Schema = schema?.Clone();
        SchemaVersion = schemaVersion;
    }

    public string Alias { get; }
    public CollectionKind CollectionKind { get; }
    public JsonElement? Schema { get; }
    public int? SchemaVersion { get; }

    public TypeReference ToTypeReference() => new(Alias, CollectionKind);
}
