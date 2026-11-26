using System.Text.Json;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Represents module-owned design metadata attached to an activity definition version.
/// </summary>
public sealed record ActivityDesignFacet
{
    public ActivityDesignFacet(string kind, string schemaVersion, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("A design facet kind is required.", nameof(kind));
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new ArgumentException("A design facet schema version is required.", nameof(schemaVersion));
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new ArgumentException("A design facet payload is required.", nameof(payload));

        Kind = kind;
        SchemaVersion = schemaVersion;
        Payload = payload.Clone();
    }

    public string Kind { get; }

    public string SchemaVersion { get; }

    public JsonElement Payload { get; }
}
