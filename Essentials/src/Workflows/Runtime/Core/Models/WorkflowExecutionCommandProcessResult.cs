namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The outcome of <see cref="Contracts.IWorkflowExecutionCommandExecutor.ProcessAsync(WorkflowExecutionCommandEnvelope, WorkflowExecutionCommandDispatchOptions, System.Threading.CancellationToken)"/>.
///
/// <para>The processor used to return <c>void</c>, so the <see cref="RuntimeSchedulerDrainResult"/> produced by the
/// drain coordinator was discarded and dispatch callers always observed success even when the drain stopped on a fault or
/// the outbox failed to deliver. This result carries the drain verdict back to the agent so it can surface a non-success
/// dispatch outcome.</para>
/// <para>For model executions, <c>WorkflowTerminated</c> also reports a fully settled model after its permitted
/// drain so the in-process mailbox can be evicted. It does not authorize the native terminal stop/purge policy.</para>
/// </summary>
public sealed record WorkflowExecutionCommandProcessResult(
    bool DrainPerformed,
    RuntimeSchedulerDrainStopReason? StopReason,
    bool Faulted,
    bool OutboxDeliveryFailed,
    string? FaultReason,
    bool WorkflowTerminated = false,
    bool Shed = false,
    TimeSpan? RetryAfter = null,
    bool ShedWorkQueued = false,
    bool CheckpointRuleViolation = false)
{
    /// <summary>
    /// The command was enqueued but no drain was performed (the drain policy returned no request). There is no fault verdict.
    /// </summary>
    public static WorkflowExecutionCommandProcessResult NoDrain { get; } = new(false, null, false, false, null);

    /// <summary>
    /// True when the drain ended the turn in a faulted state — either a handler faulted or the post-commit outbox failed to
    /// deliver. Dispatch callers should treat this as a non-success (but accepted) outcome.
    /// </summary>
    public bool IsFaulted => Faulted || OutboxDeliveryFailed;

    /// <summary>
    /// Projects a completed drain into a process result, extracting the first faulted work item's error (or an outbox
    /// failure message) as the human-readable fault reason. A deferred drain carries its safe explanation in the same
    /// reason field without setting a fault flag; <see cref="StopReason"/> distinguishes it from quiescence.
    /// This is a drain diagnostic. The command remains accepted with durable retry work, unlike admission refusal.
    /// </summary>
    public static WorkflowExecutionCommandProcessResult FromDrain(RuntimeSchedulerDrainResult drainResult)
    {
        ArgumentNullException.ThrowIfNull(drainResult);

        var faulted = drainResult.StoppedOnFault || drainResult.StopReason == RuntimeSchedulerDrainStopReason.Faulted;
        var outboxDeliveryFailed = drainResult.OutboxFailedCount > 0 || drainResult.StopReason == RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed;

        string? faultReason = null;
        if (faulted)
        {
            var faultedItem = drainResult.Items.FirstOrDefault(item => item.Status == RuntimeSchedulerWorkItemResultStatus.Faulted);
            faultReason = faultedItem?.Error ?? "A scheduler work item faulted during drain.";
        }
        else if (outboxDeliveryFailed)
        {
            faultReason = "One or more post-commit outbox intents failed to deliver during drain.";
        }
        else if (drainResult.StoppedOnDeferral)
        {
            // Durable retry deferral carries an explanation without becoming a fault. The work item's
            // error is the handler's safe summary, never an exception dump or dependency details.
            faultReason = drainResult.Items.FirstOrDefault(item => item.Status == RuntimeSchedulerWorkItemResultStatus.Deferred)?.Error
                ?? "A scheduler work item is waiting for a temporarily unavailable dependency.";
        }

        return new WorkflowExecutionCommandProcessResult(
            DrainPerformed: true,
            StopReason: drainResult.StopReason,
            Faulted: faulted,
            OutboxDeliveryFailed: outboxDeliveryFailed,
            FaultReason: faultReason,
            WorkflowTerminated: drainResult.StoppedOnTerminalStatus,
            CheckpointRuleViolation: drainResult.Items.Any(item => item.CheckpointRuleViolation));
    }

    /// <summary>
    /// The command was refused by live dispatch admission control (RB1, #1235) and no drain ran. This is deliberately
    /// not a fault: nothing went wrong with the command, the host declined to take it now. It is also deliberately not
    /// success, so a caller pattern-matching on acceptance cannot read a refusal as work done.
    /// </summary>
    /// <param name="workQueued">
    /// Whether the refusal still left the work item durably queued. It separates the two refusals: a start writes
    /// nothing and is genuinely retryable, while any other kind is parked for the resumption sweep and has therefore
    /// consumed its idempotency key. Callers that dedup on that key need to tell them apart.
    /// </param>
    public static WorkflowExecutionCommandProcessResult FromShed(string reason, TimeSpan? retryAfter, bool workQueued) =>
        new(
            DrainPerformed: false,
            StopReason: null,
            Faulted: false,
            OutboxDeliveryFailed: false,
            FaultReason: reason,
            Shed: true,
            RetryAfter: retryAfter,
            ShedWorkQueued: workQueued);

    /// <summary>
    /// A completed drain that ended by committing a terminal workflow status without faulting. Test/support factory for
    /// the terminal-eviction path (#542); production always projects the real drain through <see cref="FromDrain"/>.
    /// </summary>
    public static WorkflowExecutionCommandProcessResult Terminal { get; } =
        new(true, RuntimeSchedulerDrainStopReason.WorkflowTerminated, false, false, null, WorkflowTerminated: true);
}
