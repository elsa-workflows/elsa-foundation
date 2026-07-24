using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.DiagnosticRecords;

namespace Elsa.Diagnostics.Persistence.Groundwork;

/// <summary>
/// Public, parameterless Groundwork.Tool source for the deployable Diagnostics document schema.
/// A host may select this source through its provider registration to use the same manifest and naming
/// policy for CLI validation/application and runtime schema admission.
/// </summary>
public sealed class DiagnosticsGroundworkDeploymentSchema :
    GroundworkDeploymentSchemaManifestSource,
    IDiagnosticRecordDeploymentManifestSource
{
    protected override IReadOnlyCollection<Type> ManifestSourceTypes { get; } =
        Array.AsReadOnly<Type>([typeof(DiagnosticsGroundworkStorageManifestSource)]);

    /// <summary>
    /// Creates the combined document and diagnostic-record deployment from the same document definition
    /// exposed by <see cref="GroundworkDeploymentSchemaManifestSource.CreateManifest"/>.
    /// </summary>
    public DiagnosticRecordDeploymentManifest CreateDeploymentManifest() =>
        new(CreateManifest(), DiagnosticsGroundworkStorageManifest.CreateDiagnosticRecordStreams());
}
