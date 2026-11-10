namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// A durable store that can tell in one cheap read whether it holds any of its rows for a workflow execution. The
/// coalescing overlay probes it once for a run that has no durable record (see
/// <see cref="RuntimeCoalescingEmptyBaseline"/>).
/// </summary>
/// <remarks>
/// A store implements this only when the probe costs less than the point reads it replaces. The in-memory stores do
/// not: their reads are dictionary lookups, and a probe would scan every execution's rows. A store that does not
/// implement it has every row read from it, as before, so it loses this saving and nothing else. That is why the
/// interface is internal (ADR 0080 D3) and implemented only by the first-party EF stores. Making it an extension
/// point is a separate decision with its own catalog entry.
/// </remarks>
internal interface IRunRowPresenceProbe
{
    /// <summary>
    /// Whether the store holds any of its rows for the run. It may answer <see langword="true"/> for a run it holds
    /// nothing of, which only costs the saving. It must never answer <see langword="false"/> for a run it holds a
    /// row of.
    /// </summary>
    ValueTask<bool> HasAnyAsync(string workflowExecutionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the persistence scope a point read of this row runs under, after throwing what the store's own read
    /// throws before it queries: an invalid id, a cancelled token, an access context the store refuses. The overlay
    /// calls it before every read it may answer itself, so a read that skips the query never skips a refusal, and
    /// what a probe found under one scope is never used for a read under another.
    /// </summary>
    string ResolveReadScope(string workflowExecutionId, string rowId, CancellationToken cancellationToken = default);
}
