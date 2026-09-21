namespace Elsa.Workflows.Publishing.Api.Models;

// These intermediate service models are intentionally implementation-owned. They never appear in
// accepts/produces metadata and therefore must remain in the replaceable API feature assembly.
internal sealed record RuntimeArtifactPreflight(
    string ArtifactId,
    bool IsRetained,
    bool IsAvailable,
    IReadOnlyList<RuntimeCapabilityPreflight> Capabilities);

internal sealed record RuntimeCapabilityPreflight(
    RuntimeCapabilityKind Kind,
    string Key,
    string? SchemaVersion,
    RuntimeCapabilityStatus Status,
    IReadOnlyList<string> SupportedSchemaVersions);

internal enum RuntimeCapabilityKind
{
    ActivityConsumer,
    DurableValueStorageDriver
}

internal enum RuntimeCapabilityStatus
{
    Available,
    Missing,
    UnsupportedSchema
}
