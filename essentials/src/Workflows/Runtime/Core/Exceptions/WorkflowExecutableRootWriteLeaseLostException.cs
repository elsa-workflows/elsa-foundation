namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Raised when a retention-root writer loses its provider-backed lease before its write completes.</summary>
public sealed class WorkflowExecutableRootWriteLeaseLostException(string artifactId, string leaseId)
    : InvalidOperationException($"Executable artifact retention root '{leaseId}' lost its write lease for artifact '{artifactId}'.")
{
    public string ArtifactId { get; } = artifactId;
    public string LeaseId { get; } = leaseId;
}
