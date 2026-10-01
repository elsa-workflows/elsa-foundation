namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// What the resumption sweep keeps between passes, per persistence scope, to share discovery fairly (#2188).
/// </summary>
/// <param name="BacklogAfterWorkflowExecutionId">
/// The last backlog execution the previous pass visited; the next pass lists after it. <see langword="null"/> starts the
/// backlog walk from the first execution.
/// </param>
/// <param name="RecoveryHasSingleSlotTurn">
/// Under a cap of one execution per sweep, whether the recovery scanner gets the slot when both sides want it. The side
/// that used the slot hands the turn to the other, so neither can starve the other.
/// </param>
public sealed record RuntimeResumptionDiscoveryState(
    string? BacklogAfterWorkflowExecutionId = null,
    bool RecoveryHasSingleSlotTurn = true);
