using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Incidents;

/// <summary>
/// Reads back the execution a start command would have created, for a run whose first commit never landed. The handlers
/// that fault such a run into existence share it: a refused first commit (<see cref="CheckpointRuleViolationWorkflowFaulter"/>,
/// #1799) and a poisoned first checkpoint (<see cref="PoisonedSchedulerWorkIncidentObserver"/>, #2537).
/// </summary>
internal static class StartCommandExecution
{
    /// <summary>
    /// The execution <paramref name="envelope"/>'s start command carries, as <see cref="WorkflowExecutionStatus.Running"/> at
    /// <paramref name="occurredAt"/>, or <see langword="null"/> when the envelope is not this execution's start.
    /// </summary>
    /// <remarks>
    /// This runs on the way out of a failed drain, so nothing about failing to read the payload may escape: an exception
    /// here would be reported in place of the failure it was called to handle. A payload this runtime cannot read is
    /// handed to <paramref name="onUnreadable"/> and reads as no execution. The catch is deliberately broad, because the
    /// payload's constructor rejects more argument names than any whitelist would track.
    /// </remarks>
    public static WorkflowExecutionState? Read(
        WorkflowExecutionCommandEnvelope? envelope,
        string workflowExecutionId,
        DateTimeOffset occurredAt,
        Action<Exception> onUnreadable)
    {
        if (envelope?.Command is not { Kind: WorkflowExecutionCommandKind.Start, Payload: { } payloadElement } ||
            !StringComparer.Ordinal.Equals(envelope.WorkflowExecutionId, workflowExecutionId))
            return null;

        WorkflowExecutionStartCommandPayload? payload;
        try
        {
            payload = payloadElement.Deserialize<WorkflowExecutionStartCommandPayload>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            onUnreadable(exception);
            return null;
        }

        if (payload is null)
            return null;

        return new WorkflowExecutionState(
            workflowExecutionId,
            payload.PinnedExecutable,
            WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: occurredAt,
            StartedAt: occurredAt,
            UpdatedAt: occurredAt,
            CompletedAt: null,
            payload.CorrelationId,
            payload.ParentWorkflowExecutionId,
            payload.TenantId,
            PersistenceModeOf(envelope.Command))
        {
            RunKind = payload.RunKind,
            PinnedSource = payload.PinnedSource,
            Partition = payload.Partition,
            Authority = payload.Authority,
            DispatchNestingDepth = payload.DispatchNestingDepth,
            TestScope = payload.TestScope
        };
    }

    // The mode the start dispatcher decided for the run goes with it, as its first commit would have recorded it.
    private static Dictionary<string, string> PersistenceModeOf(WorkflowExecutionCommand command) =>
        command.Metadata.TryGetValue(RuntimeMetadataKeys.PersistenceMode, out var mode)
            ? new Dictionary<string, string> { [RuntimeMetadataKeys.PersistenceMode] = mode }
            : new Dictionary<string, string>();

    /// <summary>
    /// Whether a dispatched child may be faulted into existence. The ordinary enrichers project its dispatch and resume its
    /// parent only through the dispatch store's additive query capability. Without it a synthesized child would be a
    /// terminal execution nothing can act on, and the durable child-evidence rule would read it as a delivered start and
    /// discard the start failure that is the parent's only other way home.
    /// </summary>
    public static bool CanFaultDispatchedChild(IWorkflowDispatchStore? workflowDispatchStore) =>
        workflowDispatchStore is IWorkflowDispatchQueryStore;
}
