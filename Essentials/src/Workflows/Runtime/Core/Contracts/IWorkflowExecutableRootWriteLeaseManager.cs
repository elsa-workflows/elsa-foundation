namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Coordinates the provider-backed lease required before a durable executable retention root is written.
/// </summary>
/// <remarks>
/// A lease id names one execution. The store hands an unexpired lease with the same id to every acquirer, and the first
/// to release it ends it for all of them, so writes that may run at once must pass distinct ids (#2274).
/// </remarks>
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

    /// <summary>
    /// Executes one durable root-set write while leasing the distinct complete dependency closures of every exact
    /// root. Legacy managers refuse this capability before the callback; sequential single-root writes cannot
    /// substitute for protecting the whole set through one atomic acceptance.
    /// </summary>
    /// <remarks>Provider and callback failures propagate with their original cause. Release failures are logged and
    /// do not replace the write result. Callbacks must honor cancellation and fence durable acceptance under current
    /// authority. The lease coordinator does not roll back a callback's durable effects.</remarks>
    /// <exception cref="InvalidOperationException">An exact pin or dependency graph cannot be validated; no write begins. The built-in manager reports WorkflowExecutableDependencyGraphException.</exception>
    /// <exception cref="Exceptions.WorkflowExecutableRootWriteLeaseUnavailableException">Material is missing or reserved for deletion; no write begins.</exception>
    /// <exception cref="Exceptions.WorkflowExecutableRootWriteLeaseLostException">A grant expires before the write, or renewal loses authority and cancels an active write.</exception>
    /// <exception cref="NotSupportedException">The selected legacy manager cannot protect the complete set; its old overload and the callback are not invoked.</exception>
    /// <exception cref="ArgumentException">The root set is empty/contains null, or the invocation lease identity is blank.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates.</exception>
    /// <exception cref="AggregateException">Concurrent lease expiry/renewal, callback or cancellation-registration failures retain their distinct causes.</exception>
    ValueTask ExecuteAsync(
        IReadOnlyCollection<Models.WorkflowExecutableIdentity> roots,
        string leaseId,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromException(new NotSupportedException("This lease manager cannot protect an atomic executable root set."));
    }
}
