using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// One exact call's inbox acceptance, applied only to the final consumer decision checkpoint. Merges the inbox
/// into any cancellation projection already in that checkpoint instead of restoring an obsolete call status.
/// </summary>
internal sealed class StructuralChildControlContribution(ActivityExecutionState acceptedChild, string? completionWorkItemId = null,
    RuntimeChildControlTransport? incoming = null, bool newlyAccepted = false, IReadOnlyList<RuntimeChildControlTransport>? ready = null)
{
    internal async ValueTask<RuntimeCheckpointCommit> ApplyAsync(RuntimeCheckpointCommitter committer,
        RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        commit = Apply(commit);
        return await committer.AddHistorySourceFactsAsync(commit, () => Facts(commit.Checkpoint.OccurredAt), cancellationToken);
    }

    private IEnumerable<RuntimeHistorySourceFact> Facts(DateTimeOffset at)
    {
        if (incoming is null) yield break;
        if (newlyAccepted)
            yield return new(RuntimeHistorySourceFactKind.ChildControlAccepted,
                RuntimeHistoryIdentity.CreateBinding([incoming.DispatchId, incoming.SourceOutboxItemId]), at,
                acceptedChild.InvocationId,
                causation: new(WorkflowExecutionId: incoming.Batch.ChildWorkflowExecutionId, DeliveryId: incoming.SourceOutboxItemId),
                relatedWorkflowExecutionId: incoming.Batch.ChildWorkflowExecutionId,
                relatedActivityExecutionId: incoming.ConsumerActivityExecutionId, reasonCode: "inbox-accepted");
        // One exact prefix decision can release an arbitrary number of already-buffered ordinals. This is a
        // bounded aggregate of actual inbox consumption, including a committed consumer fault, not callback success.
        if (ready is { Count: > 0 })
            yield return new(RuntimeHistorySourceFactKind.ChildControlApplied,
                RuntimeHistoryIdentity.CreateBinding([incoming.DispatchId, ready[0].SourceOutboxItemId, ready[^1].SourceOutboxItemId]), at,
                incoming.ConsumerActivityExecutionId,
                causation: new(WorkflowExecutionId: incoming.Batch.ChildWorkflowExecutionId, DeliveryId: incoming.SourceOutboxItemId),
                relatedWorkflowExecutionId: incoming.Batch.ChildWorkflowExecutionId,
                relatedActivityExecutionId: acceptedChild.InvocationId, reasonCode: "ordered-prefix-consumed");
    }

    internal RuntimeCheckpointCommit Apply(RuntimeCheckpointCommit commit)
    {
        if (acceptedChild.ChildControlInbox is not { } inbox ||
            acceptedChild.Execution.WorkflowExecutionId != commit.WorkflowExecutionId ||
            acceptedChild.InvocationId != inbox.Target.CallingActivityExecutionId ||
            acceptedChild.ParentActivityExecutionId != inbox.Target.ConsumerActivityExecutionId)
            throw new InvalidOperationException("Child-control acceptance does not belong to this checkpoint and exact invocation.");
        // Scope cleanup removes wait/timer/queue resources, not invocation rows. Preserve that cleanup and
        // merge acceptance into its cancelled invocation projection rather than reviving the old status.
        var existing = commit.StateChanges.ActivityExecutions.SingleOrDefault(change => change.StateId == acceptedChild.InvocationId);
        if (existing?.Operation == RuntimeStateChangeOperation.Delete)
            throw new InvalidOperationException("A child-control decision cannot delete its accepted invocation.");
        var changed = (existing?.State ?? acceptedChild) with { ChildControlInbox = inbox };
        if (completionWorkItemId is not null)
            changed = ActivityAttemptActivationClaimer.MarkParentCompletionProcessed(changed, completionWorkItemId);
        var changes = commit.StateChanges.ActivityExecutions.Where(change => change.StateId != acceptedChild.InvocationId)
            .Append(new RuntimeStateChange<ActivityExecutionState>(acceptedChild.InvocationId,
                RuntimeStateChangeOperation.Upsert, changed, existing?.Metadata ?? commit.Metadata)).ToArray();
        return commit with
        {
            StateChanges = commit.StateChanges.WithActivityExecutions(changes),
            Checkpoint = commit.Checkpoint with
            {
                ActivityExecutionIds = commit.Checkpoint.ActivityExecutionIds.Append(acceptedChild.InvocationId).Distinct(StringComparer.Ordinal).ToArray()
            }
        };
    }
}
