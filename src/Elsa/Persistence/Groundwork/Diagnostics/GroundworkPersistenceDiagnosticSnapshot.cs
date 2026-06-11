namespace Elsa.Persistence.Groundwork.Diagnostics;

public sealed record GroundworkPersistenceDiagnosticSnapshot(
    IReadOnlyList<GroundworkManifestDiagnostic> Manifests,
    IReadOnlyList<GroundworkProviderDiagnostic> Providers,
    IReadOnlyList<GroundworkMaterializationDiagnostic> Materializations);

public sealed record GroundworkManifestDiagnostic(string Identity, string Version, string Owner);

public sealed record GroundworkProviderDiagnostic(string Name, string Version);

public sealed record GroundworkMaterializationDiagnostic(
    string ManifestIdentity,
    string ManifestVersion,
    string? ProviderName,
    string? ProviderVersion,
    GroundworkMaterializationStatus Status,
    string? Message,
    DateTimeOffset RecordedAt);

public enum GroundworkMaterializationStatus
{
    Succeeded,
    Skipped,
    ProviderUnavailable,
    Failed
}
