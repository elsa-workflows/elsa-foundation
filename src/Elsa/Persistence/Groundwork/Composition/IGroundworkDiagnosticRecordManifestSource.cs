using Groundwork.DiagnosticRecords;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Contributes one feature-owned set of diagnostic-record stream declarations to a host-selected
/// Groundwork deployment. The combined deployment remains the sole CLI and runtime authority.
/// </summary>
public interface IGroundworkDiagnosticRecordManifestSource
{
    /// <summary>Gets the stable identity used to order and diagnose this contribution.</summary>
    string FeatureIdentity { get; }

    /// <summary>Creates an immutable snapshot of the diagnostic-record streams owned by the feature.</summary>
    IReadOnlyList<DiagnosticRecordStreamDefinition> CreateDiagnosticRecordStreams();
}
