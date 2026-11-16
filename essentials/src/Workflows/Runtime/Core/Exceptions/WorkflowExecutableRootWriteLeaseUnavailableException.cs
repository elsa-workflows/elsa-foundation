namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Raised when a durable retention root cannot safely be established for an executable artifact.</summary>
public sealed class WorkflowExecutableRootWriteLeaseUnavailableException(string artifactId, string leaseId)
    : InvalidOperationException($"Executable artifact '{artifactId}' cannot accept retention root '{leaseId}' because it is missing or reserved for deletion.")
{
    public string ArtifactId { get; } = artifactId;
    public string LeaseId { get; } = leaseId;
}
