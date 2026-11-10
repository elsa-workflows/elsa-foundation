namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional capability of an <see cref="IActivityExecutionStateStore"/> or an
/// <see cref="IActivityExecutionInspectionStore"/>: the store can tell in one cheap read whether it holds any of its
/// rows for a workflow execution. A drain of a run that has no durable record asks each store once per coalescing
/// session, and from then on does not read rows the store cannot hold.
/// </summary>
/// <remarks>
/// <para>
/// Implement it on the store object itself, and only when the probe costs less than the point reads it replaces. A
/// store that does not implement it has every row read from it, as before, and loses nothing else. The in-memory
/// stores do not implement it: their reads are dictionary lookups, and a probe would scan every execution's rows.
/// </para>
/// <para>
/// The probe must agree with the store's own <c>FindAsync</c>. Every row a <c>FindAsync</c> for the run could
/// return in a scope, <see cref="HasAnyAsync"/> must count in that scope, including a write the store has
/// accepted but a read might not see yet. A decorator forwards both members only if its own <c>FindAsync</c>
/// returns nothing that its inner store's probe does not count. A decorator that also reads from somewhere else,
/// such as an archive or a cache, must not forward them.
/// </para>
/// <para>
/// After an empty probe the runtime assumes that rows of the run reach the store in two ways only: through the
/// store's own <c>SaveAsync</c>, and through a checkpoint commit, as exactly the activity executions and inspection
/// projections that commit lists. A provider whose commit store, triggers or background projection add other rows
/// for a run must not implement the probe. An object that implements both store contracts has one probe for both,
/// so it must count the rows of both.
/// </para>
/// </remarks>
public interface IRunRowPresenceProbe
{
    /// <summary>
    /// Returns whether the store holds any of its rows for the workflow execution, in the persistence scope the
    /// call runs under, which is the scope <see cref="ResolveReadScope"/> returns in the same context. A wrong <see langword="true"/> only costs the saving. A wrong <see langword="false"/> makes
    /// the runtime treat rows that exist as absent, so the read must see every row of the run that a
    /// <c>FindAsync</c> could return, and it must throw when it cannot tell.
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
