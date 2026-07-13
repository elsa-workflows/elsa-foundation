namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Coordinates the provider-backed lease required before a durable executable retention root is written.
/// </summary>
public interface IWorkflowExecutableRootWriteLeaseManager
{
    /// <summary>
    /// Executes a durable retention-root write while holding and renewing a lease for <paramref name="artifactId"/>.
    /// </summary>
    /// <exception cref="Exceptions.WorkflowExecutableRootWriteLeaseUnavailableException">
    /// The artifact does not exist or is currently reserved for deletion.
    /// </exception>
    ValueTask ExecuteAsync(
        string artifactId,
        string leaseId,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default);
}
