namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The outcome of <see cref="Contracts.IWorkflowExecutionCommandProcessor.ProcessAsync(WorkflowExecutionCommandEnvelope, WorkflowExecutionCommandDispatchOptions, System.Threading.CancellationToken)"/>.
///
/// <para>Before RT-14 the processor returned <c>void</c>, so the <see cref="RuntimeSchedulerDrainResult"/> produced by the
/// drain coordinator was discarded and dispatch callers always observed success even when the drain stopped on a fault or
/// the outbox failed to deliver. This result carries the drain verdict back to the agent so it can surface a non-success
/// dispatch outcome.</para>
/// </summary>
public sealed record WorkflowExecutionCommandProcessResult(
    bool DrainPerformed,
    RuntimeSchedulerDrainStopReason? StopReason,
    bool Faulted,
    bool OutboxDeliveryFailed,
    string? FaultReason)
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
    /// failure message) as the human-readable fault reason.
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

        return new WorkflowExecutionCommandProcessResult(
            DrainPerformed: true,
            StopReason: drainResult.StopReason,
            Faulted: faulted,
            OutboxDeliveryFailed: outboxDeliveryFailed,
            FaultReason: faultReason);
    }
}
