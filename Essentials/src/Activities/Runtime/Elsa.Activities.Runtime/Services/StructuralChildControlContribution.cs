using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// One exact call's inbox acceptance, applied only to the final consumer decision checkpoint. Merges the inbox
/// into any cancellation projection already in that checkpoint instead of restoring an obsolete call status.
/// </summary>
internal sealed class StructuralChildControlContribution(ActivityExecutionState acceptedChild, string? completionWorkItemId = null)
{
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
