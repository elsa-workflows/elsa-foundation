namespace Elsa.Persistence.Core;

/// <summary>
/// Runs background work once for every host-supplied persistence partition using a fresh ordinary operation scope.
/// </summary>
public interface IPersistenceScopeRunner
{
    /// <summary>
    /// Visits the finite scope snapshot sequentially. A non-fatal failure in one partition does not prevent later
    /// partitions from being visited; failures are surfaced after the snapshot has been exhausted.
    /// </summary>
    ValueTask RunAsync(
        Func<PersistenceScope, PersistenceOperationScope, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default);
}
