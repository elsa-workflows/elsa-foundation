using Elsa.Persistence.Groundwork.Composition;

namespace Elsa.Diagnostics.Persistence.Groundwork;

/// <summary>
/// Contributes the diagnostics document manifest to a host-selected Groundwork deployment schema.
/// Diagnostic record streams are intentionally excluded; see
/// <see cref="DiagnosticsGroundworkStorageManifest.StreamDeploymentLimitation"/>.
/// </summary>
public sealed class DiagnosticsGroundworkStorageManifestSource : IGroundworkStorageManifestSource
{
    public const string FeatureName = "elsa-diagnostics";

    public string FeatureIdentity => FeatureName;

    public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
            FeatureIdentity,
            DiagnosticsGroundworkStorageManifest.CreateDocumentManifest(),
            [],
            [],
            [],
            [
                "diagnostics-open-telemetry-resource-catalog",
                "diagnostics-open-telemetry-instrument-catalog",
                "diagnostics-open-telemetry-capture-operation-ledger"
            ]));
    }
}
