using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record RuntimeRequirementPreflightView(
    int CheckedArtifactCount,
    bool IsReady,
    IReadOnlyList<RuntimeRequirementPreflightItemView> Requirements,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public sealed record RuntimeRequirementPreflightItemView(
    string ConsumerKey,
    string SchemaVersion,
    string Status,
    int AffectedArtifactCount);

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
