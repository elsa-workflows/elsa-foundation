using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned compiled structure for one executable node.
/// </summary>
/// <remarks>
/// Runtime core stores and round-trips the generic shape. The owning activity module
/// owns the kind name, schema version, payload, and consistency rules against child
/// projections.
/// </remarks>
public sealed record ExecutableActivityStructure
{
    public ExecutableActivityStructure(string kind, string schemaVersion, JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(kind))
            throw new ArgumentException("An executable activity structure kind is required.", nameof(kind));
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new ArgumentException("An executable activity structure schema version is required.", nameof(schemaVersion));
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new ArgumentException("An executable activity structure payload is required.", nameof(payload));

        Kind = kind;
        SchemaVersion = schemaVersion;
        Payload = payload.Clone();
    }

    public string Kind { get; }
    public string SchemaVersion { get; }
    public JsonElement Payload { get; }
}
