using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

/// <summary>Commits the real native wait and admission/cancellation responsibility together.</summary>
internal sealed class JobsCheckpointEnricher(IActivityExecutionStateStore activities) : IRuntimeCheckpointCommitEnricher
{
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        var intents = commit.PostCommitIntents.ToList();
        var changed = commit.StateChanges.ActivityExecutions.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert)
            .ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        var replacements = new Dictionary<string, ActivityExecutionState>(StringComparer.Ordinal);
        if (commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkCreated)
        {
            foreach (var change in commit.StateChanges.Bookmarks.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!changed.TryGetValue(change.State.ActivityExecutionId, out var activity) || activity.Execution.ActivityType != JobsNativeBoundary.ActivityType) continue;
                if (activity.Status != ActivityExecutionStatus.Suspended || activity.Execution.WorkflowExecutionId != commit.WorkflowExecutionId) throw JobsNativeBoundary.Invalid();
                var admission = JobsNativeBoundary.Admission(JobsNativeBoundary.ReadState(activity),
                    JobsNativeBoundary.CaptureAddress(activity, change.State), activity.PrivateState!.CommittedAt);
                activity = Freeze(activity, JobsNativeBoundary.AdmissionMetadataKey, JobsNativeBoundary.Write(admission).GetRawText(), immutable: true);
                changed[activity.InvocationId] = activity; replacements[activity.InvocationId] = activity;
                Add(JobsNativeBoundary.Intent(admission, cancel: false));
            }
        }

        foreach (var activity in changed.Values.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (activity.Execution.ActivityType != JobsNativeBoundary.ActivityType || activity.Status != ActivityExecutionStatus.Running ||
                !activity.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityAttemptActivationClaim, out var attemptId)) continue;
            var attempt = activity.Attempts?.SingleOrDefault(candidate => candidate.AttemptId == attemptId);
            if (attempt is not { Reason: ActivityAttemptReason.Resume, EndedAt: null, TriggerDeliveryId: { } deliveryId }) continue;
            var admission = JobsNativeBoundary.ReadAdmission(activity);
            var receipt = activity.TriggerDeliveries?.SingleOrDefault(candidate => candidate.DeliveryId == deliveryId);
            if (receipt is not { Status: ActivityTriggerDeliveryStatus.Consumed } || receipt.RegistrationId != admission.Address.RegistrationId ||
                receipt.PayloadType != JobsNativeBoundary.CompletionType || receipt.ProviderId != JobsNativeBoundary.ProviderId ||
                receipt.Payload.InlineValue is not { } payload) throw JobsNativeBoundary.Invalid();
            var completion = JobsNativeBoundary.Read<RunJobCompletion>(payload);
            var expectedKey = JobsNativeBoundary.DeduplicationKey(admission.Address, JobsNativeBoundary.DeliveryId(admission.Address, completion));
            if (completion.FormatVersion != 1 || completion.RequestSha256 != admission.RequestSha256 || receipt.DeduplicationKey != expectedKey ||
                activity.ContractIdentity is not { } contract) throw JobsNativeBoundary.Invalid();
            var binding = new JobNativeDeliveryBinding(1, completion, receipt.RegistrationId, receipt.DeliveryId,
                receipt.DeduplicationKey, attemptId, contract);
            if (activity.Metadata.TryGetValue(JobsNativeBoundary.DeliveryMetadataKey, out var previous))
            {
                var prior = JobsNativeBoundary.ReadString<JobNativeDeliveryBinding>(previous);
                if (!JobsNativeBoundary.Equivalent(prior with { AttemptId = attemptId }, binding)) throw JobsNativeBoundary.Invalid();
            }
            var frozen = Freeze(activity, JobsNativeBoundary.DeliveryMetadataKey, JobsNativeBoundary.Write(binding).GetRawText(), immutable: false);
            changed[activity.InvocationId] = frozen; replacements[activity.InvocationId] = frozen;
        }

        var cancelledParent = commit.StateChanges.WorkflowExecution is
            { Operation: RuntimeStateChangeOperation.Upsert, State.Status: WorkflowExecutionStatus.Cancelled };
        var cancellations = changed.Values.Where(state => cancelledParent
            ? state.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Superseded)
            : state.Status == ActivityExecutionStatus.Cancelled).ToList();
        if (cancelledParent)
        {
            string? after = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await activities.ListPageAsync(new ActivityExecutionStatePageQuery(commit.WorkflowExecutionId, continuationToken: after), cancellationToken);
                cancellations.AddRange(page.Items.Where(state => !changed.ContainsKey(state.InvocationId) &&
                    state.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Superseded)));
                if (page.NextContinuationToken is not null && page.NextContinuationToken == after) throw JobsNativeBoundary.Invalid();
                after = page.NextContinuationToken;
            } while (after is not null);
        }
        foreach (var activity in cancellations.Where(state => state.Execution.ActivityType == JobsNativeBoundary.ActivityType &&
                     state.Metadata.ContainsKey(JobsNativeBoundary.AdmissionMetadataKey)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (activity.Execution.WorkflowExecutionId != commit.WorkflowExecutionId) throw JobsNativeBoundary.Invalid();
            var admission = JobsNativeBoundary.ReadAdmission(activity);
            if (activity.Status == ActivityExecutionStatus.Cancelled && changed.ContainsKey(activity.InvocationId) &&
                !activity.Metadata.ContainsKey(JobsNativeBoundary.DeliveryMetadataKey) &&
                activity.TriggerDeliveries?.Any(receipt => receipt.Status == ActivityTriggerDeliveryStatus.Consumed) != true &&
                activity.Attempts?.Any(attempt => attempt.Reason == ActivityAttemptReason.Resume || attempt.TriggerDeliveryId is not null) != true)
            {
                var marked = Freeze(activity, JobsNativeBoundary.OwnerCancellationMetadataKey,
                    JobsNativeBoundary.OwnerCancellationMarker(admission), immutable: true);
                changed[activity.InvocationId] = marked; replacements[activity.InvocationId] = marked;
            }
            // Cancellation confirms the tombstone/request immediately. A separate required source
            // retains direct-native history until the eventual selected result is acknowledged.
            Add(JobsNativeBoundary.Intent(admission, cancel: true));
            if (admission.Address.Owner.Origin is null) Add(JobsNativeBoundary.SettlementIntent(admission));
        }

        foreach (var activity in changed.Values.Where(state => state.Execution.ActivityType == JobsNativeBoundary.ActivityType &&
                     state.Status is (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled) &&
                     state.Metadata.ContainsKey(JobsNativeBoundary.DeliveryMetadataKey)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var admission = JobsNativeBoundary.ReadAdmission(activity);
            // ModelWorkOrigin uses the owning model's existing result source. A second source
            // waiting for that model result would create a cancellation-settlement cycle.
            if (admission.Address.Owner.Origin is null) Add(JobsNativeBoundary.SettlementIntent(admission));
        }

        var changes = replacements.Count == 0 ? commit.StateChanges : WithActivities(commit.StateChanges,
            commit.StateChanges.ActivityExecutions.Select(change => replacements.TryGetValue(change.StateId, out var replacement)
                ? change with { State = replacement } : change).ToArray());
        return commit with { StateChanges = changes, PostCommitIntents = intents };

        void Add(RuntimePostCommitIntent intent)
        {
            var existing = intents.SingleOrDefault(candidate => candidate.IntentId == intent.IntentId);
            if (existing is null) intents.Add(intent); else if (!existing.IsEquivalentTo(intent)) throw JobsNativeBoundary.Invalid();
        }
    }

    public ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        foreach (var change in commit.StateChanges.ActivityExecutions.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert &&
                     change.State.Execution.ActivityType == JobsNativeBoundary.ActivityType && change.State.Metadata.ContainsKey(JobsNativeBoundary.AdmissionMetadataKey)))
        {
            cancellationToken.ThrowIfCancellationRequested(); _ = JobsNativeBoundary.ReadAdmission(change.State);
        }
        foreach (var intent in commit.PostCommitIntents.Where(intent => intent.Kind is JobsNativeBoundary.StartKind or JobsNativeBoundary.CancelKind))
        {
            cancellationToken.ThrowIfCancellationRequested(); _ = JobsNativeIntentHandler.Read(intent);
        }
        foreach (var intent in commit.PostCommitIntents.Where(intent => intent.Kind == JobsNativeBoundary.SettlementKind))
        {
            cancellationToken.ThrowIfCancellationRequested(); _ = JobsNativeSettlementIntentHandler.Read(intent);
        }
        return ValueTask.CompletedTask;
    }

    private static ActivityExecutionState Freeze(ActivityExecutionState activity, string key, string value, bool immutable)
    {
        if (immutable && activity.Metadata.TryGetValue(key, out var prior) && prior != value) throw JobsNativeBoundary.Invalid();
        var metadata = activity.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[key] = value; return activity with { Metadata = metadata };
    }

    private static RuntimeCheckpointStateChangeSet WithActivities(RuntimeCheckpointStateChangeSet source,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activities) => new(source.WorkflowExecution, source.Scheduler,
        activities, source.Bookmarks, source.DurableValues, source.Incidents, source.Operational, source.WorkflowDispatches,
        source.ActivityExecutionInspections, source.PostCommitOutbox, source.ActivityScopeCleanups, source.WorkflowDispatchCancellations,
        source.ConsumedSchedulerWorkItems, source.AlterationJobTerminalChange, source.BackgroundActivityWork);
}
