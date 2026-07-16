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

    /// <summary>
    /// Executes a durable retention-root write while holding renewable leases for the exact root artifact and its
    /// complete dependency closure. The default preserves compatibility for custom managers compiled against the
    /// original single-artifact contract; the built-in manager provides closure-wide behavior.
    /// </summary>
    ValueTask ExecuteAsync(
        Models.WorkflowExecutableIdentity root,
        string leaseId,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(root.ArtifactId, leaseId, write, cancellationToken);
}
