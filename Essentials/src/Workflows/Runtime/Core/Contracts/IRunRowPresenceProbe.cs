namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional capability of an <see cref="IActivityExecutionStateStore"/> or an
/// <see cref="IActivityExecutionInspectionStore"/>: the store can tell in one cheap read whether it holds any of its
/// rows for a workflow execution. The drain of a run that has no durable record asks once per store, and from then
/// on does not read rows the store cannot hold.
/// </summary>
/// <remarks>
/// Implement it on the store object itself, and only when the probe costs less than the point reads it replaces. A
/// store that does not implement it has every row read from it, as before, and loses nothing else. A decorator
/// around a store that implements it must forward both members, or that composition loses the saving. The in-memory
/// stores do not implement it: their reads are dictionary lookups, and a probe would scan every execution's rows.
/// </remarks>
public interface IRunRowPresenceProbe
{
    /// <summary>
    /// Returns whether the store holds any of its rows for the workflow execution, in the persistence scope the
    /// call runs under. A wrong <see langword="true"/> only costs the saving. A wrong <see langword="false"/> makes
    /// the runtime treat rows that exist as absent, so the read must see every committed row of the run, and it
    /// must throw when it cannot tell.
    /// </summary>
    ValueTask<bool> HasAnyAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a key for the persistence scope a point read of this row runs under, after throwing what the store's
    /// own <c>FindAsync</c> throws before it queries: an invalid id, a cancelled token, an access context the store
    /// refuses. The runtime calls it before every read it may answer itself, so a read that skips the query never
    /// skips a refusal, and what <see cref="HasAnyAsync"/> found under one scope is never used for a read under
    /// another. A store that has no scopes returns the same string every time.
    /// </summary>
    string ResolveReadScope(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default);
}
