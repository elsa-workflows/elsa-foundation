using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Core.Manifests;
using Groundwork.DiagnosticRecords;

namespace Elsa.Diagnostics.Persistence.Groundwork;

/// <summary>
/// Aggregates the document and diagnostic-record declarations in the first-party Diagnostics deployment.
/// </summary>
public static class DiagnosticsGroundworkStorageManifest
{
    private static readonly IReadOnlyList<IGroundworkDiagnosticRecordManifestSource> DiagnosticRecordSources =
    [
        new GroundworkOpenTelemetryPersistenceFeature(),
        new GroundworkStructuredLogsPersistenceFeature()
    ];

    /// <summary>
    /// Returns the complete document portion of Diagnostics: the OpenTelemetry resource and instrument
    /// catalogs plus its capture-operation ledger.
    /// </summary>
    public static StorageManifest CreateDocumentManifest() =>
        OpenTelemetryGroundworkStorageSchema.CreateDocumentManifest();

    /// <summary>
    /// Returns the five immutable history streams deployed beside the document manifest. The default
    /// OpenTelemetry binding is part of this parameterless first-party deployment contract; a customized
    /// binding requires a matching host-owned deployment source.
    /// </summary>
    public static IReadOnlyList<DiagnosticRecordStreamDefinition> CreateDiagnosticRecordStreams() =>
        DiagnosticRecordSources
            .OrderBy(source => source.FeatureIdentity, StringComparer.Ordinal)
            .SelectMany(source => source.CreateDiagnosticRecordStreams())
            .ToArray();

    /// <summary>Returns the one combined declaration consumed by Groundwork.Tool preview.81.</summary>
    public static DiagnosticRecordDeploymentManifest CreateDeploymentManifest() =>
        new(CreateDocumentManifest(), CreateDiagnosticRecordStreams());
}
