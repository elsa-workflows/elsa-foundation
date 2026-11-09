namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// A durable store that can tell in one cheap read whether it holds any of its rows for a workflow execution. The
/// coalescing overlay probes it once for a run that has no durable record (see
/// <see cref="RuntimeCoalescingEmptyBaseline"/>).
/// </summary>
/// <remarks>
/// A store implements this only when the probe costs less than the point reads it replaces. The in-memory stores do
/// not: their reads are dictionary lookups, and a probe would scan every execution's rows. A store that does not
/// implement it has every row read from it, as before. A probe may answer <see langword="true"/> for a run it holds
/// nothing of, which only costs the saving; it must never answer <see langword="false"/> for one it holds a row of.
/// </remarks>
internal interface IWorkflowExecutionRowPresenceProbe
{
    ValueTask<bool> HasAnyAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}
