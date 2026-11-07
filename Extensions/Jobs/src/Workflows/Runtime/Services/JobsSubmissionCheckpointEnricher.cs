using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>Commits submission-only admission and continuing cancellation responsibility with the actual completion.</summary>
internal sealed class JobsSubmissionCheckpointEnricher(IActivityExecutionStateStore activities, IExternalPayloadStore? external = null)
    : IRuntimeCheckpointCommitEnricher
{
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        var intents = commit.PostCommitIntents.ToList();
        var changed = commit.StateChanges.ActivityExecutions.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert)
            .ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        var replacements = new Dictionary<string, ActivityExecutionState>(StringComparer.Ordinal);
        foreach (var activity in changed.Values.Where(activity => activity.Execution.ActivityType == JobsNativeSubmissionBoundary.ActivityType &&
                     activity.Status == ActivityExecutionStatus.Completed).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (_, admission) = await JobsNativeSubmissionBoundary.ReadCompletionAsync(activity, external, cancellationToken);
            if (activity.Metadata.ContainsKey(JobsNativeSubmissionBoundary.MetadataKey))
            {
                if (!JobsNativeBoundary.Equivalent(JobsNativeSubmissionBoundary.ReadAdmission(activity), admission)) throw JobsNativeBoundary.Invalid();
                continue;
            }
            var metadata = activity.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            metadata[JobsNativeSubmissionBoundary.MetadataKey] = JobsNativeBoundary.Write(admission).GetRawText();
            var saved = activity with { Metadata = metadata };
            changed[activity.InvocationId] = saved; replacements[activity.InvocationId] = saved;
            Add(JobsNativeSubmissionBoundary.Intent(admission, JobsNativeSubmissionBoundary.StartKind));
            // This required source remains owed while the owning parent is active, even after
            // admission is Delivered. The existing deletion fence therefore retains the exact
            // completed child needed for a later explicit parent cancellation.
            if (admission.Submission.HandlerPin.AssociationMode == JobAssociationMode.Independent)
                Add(JobsNativeSubmissionBoundary.Intent(admission, JobsNativeSubmissionBoundary.OwnershipKind));
        }

        if (commit.StateChanges.WorkflowExecution is { Operation: RuntimeStateChangeOperation.Upsert, State.Status: WorkflowExecutionStatus.Cancelled } parentChange)
        {
            var all = changed.Values.ToDictionary(activity => activity.InvocationId, StringComparer.Ordinal);
            string? after = null; var seen = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await activities.ListPageAsync(new ActivityExecutionStatePageQuery(commit.WorkflowExecutionId, continuationToken: after), cancellationToken);
                foreach (var activity in page.Items) all.TryAdd(activity.InvocationId, activity);
                after = page.NextContinuationToken;
                if (after is not null && !seen.Add(after)) throw JobsNativeBoundary.Invalid();
            } while (after is not null);
            foreach (var activity in all.Values.Where(activity => activity.Execution.ActivityType == JobsNativeSubmissionBoundary.ActivityType))
            {
                if (!activity.Metadata.ContainsKey(JobsNativeSubmissionBoundary.MetadataKey))
                {
                    if (activity.Status == ActivityExecutionStatus.Cancelled && activity.Completion is null && activity.PrivateState is null &&
                        activity.BookmarkIds.Count == 0 && activity.TriggerRegistrations?.Count is not > 0 && activity.TriggerDeliveries?.Count is not > 0)
                    {
                        var marker = JobsNativeSubmissionBoundary.CancellationMarker(parentChange.State, activity);
                        if (activity.Metadata.TryGetValue(JobsNativeSubmissionBoundary.CancellationMetadataKey, out var prior) && prior != marker)
                            throw JobsNativeBoundary.Invalid();
                        var metadata = activity.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                        metadata[JobsNativeSubmissionBoundary.CancellationMetadataKey] = marker;
                        replacements[activity.InvocationId] = activity with { Metadata = metadata };
                    }
                    continue;
                }
                var admission = JobsNativeSubmissionBoundary.ReadAdmission(activity);
                if (admission.Submission.Owner.Origin is null && admission.Submission.HandlerPin.AssociationMode == JobAssociationMode.Independent)
                    Add(JobsNativeSubmissionBoundary.Intent(admission, JobsNativeSubmissionBoundary.CancelKind));
            }
        }
        var activityChanges = commit.StateChanges.ActivityExecutions.Select(change => replacements.TryGetValue(change.StateId, out var replacement)
            ? change with { State = replacement } : change).ToList();
        foreach (var (id, activity) in replacements.Where(item => !activityChanges.Any(change => change.StateId == item.Key)))
            activityChanges.Add(new(id, RuntimeStateChangeOperation.Upsert, activity, commit.Metadata));
        var changes = replacements.Count == 0 ? commit.StateChanges : WithActivities(commit.StateChanges, activityChanges);
        return commit with { StateChanges = changes, PostCommitIntents = intents };

        void Add(RuntimePostCommitIntent intent)
        {
            var existing = intents.SingleOrDefault(candidate => candidate.IntentId == intent.IntentId);
            if (existing is null) intents.Add(intent); else if (!existing.IsEquivalentTo(intent)) throw JobsNativeBoundary.Invalid();
        }
    }

    public async ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        foreach (var change in commit.StateChanges.ActivityExecutions.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert &&
                     change.State.Execution.ActivityType == JobsNativeSubmissionBoundary.ActivityType &&
                     change.State.Metadata.ContainsKey(JobsNativeSubmissionBoundary.MetadataKey)))
        {
            var retained = JobsNativeSubmissionBoundary.ReadAdmission(change.State);
            var (_, actual) = await JobsNativeSubmissionBoundary.ReadCompletionAsync(change.State, external, cancellationToken);
            if (!JobsNativeBoundary.Equivalent(retained, actual)) throw JobsNativeBoundary.Invalid();
        }
        foreach (var intent in commit.PostCommitIntents.Where(intent => intent.Kind is JobsNativeSubmissionBoundary.StartKind or
                     JobsNativeSubmissionBoundary.OwnershipKind or JobsNativeSubmissionBoundary.CancelKind))
        {
            cancellationToken.ThrowIfCancellationRequested(); _ = JobsNativeSubmissionIntentHandler.Read(intent);
        }
    }

    private static RuntimeCheckpointStateChangeSet WithActivities(RuntimeCheckpointStateChangeSet source,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activities) => new(source.WorkflowExecution, source.Scheduler,
        activities, source.Bookmarks, source.DurableValues, source.Incidents, source.Operational, source.WorkflowDispatches,
        source.ActivityExecutionInspections, source.PostCommitOutbox, source.ActivityScopeCleanups, source.WorkflowDispatchCancellations,
        source.ConsumedSchedulerWorkItems, source.AlterationJobTerminalChange, source.BackgroundActivityWork);
}
