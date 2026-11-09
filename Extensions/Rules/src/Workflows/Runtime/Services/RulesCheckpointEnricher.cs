using System.Text;
using Elsa.Rules.Core;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Rules.Workflows.Runtime.Services;

/// <summary>Persists Rules dispatch responsibility with the native bookmark checkpoint.</summary>
internal sealed class RulesCheckpointEnricher(IActivityExecutionStateStore activities) : IRuntimeCheckpointCommitEnricher
{
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        var intents = commit.PostCommitIntents.ToList();
        var changed = commit.StateChanges.ActivityExecutions
            .Where(change => change.Operation == RuntimeStateChangeOperation.Upsert)
            .ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        var replacements = new Dictionary<string, ActivityExecutionState>(StringComparer.Ordinal);

        if (commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkCreated)
        {
            foreach (var change in commit.StateChanges.Bookmarks.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert))
            {
                if (!changed.TryGetValue(change.State.ActivityExecutionId, out var activity) ||
                    activity.Execution.ActivityType != RunRules.ActivityType)
                    continue;

                if (activity.Status != ActivityExecutionStatus.Suspended)
                    throw RulesNativeBoundary.Invalid();

                var request = RulesNativeBoundary.ReadState(activity);
                if (!StringComparer.Ordinal.Equals(request.WorkflowExecutionId, commit.WorkflowExecutionId))
                    throw RulesNativeBoundary.Invalid();

                var address = RulesNativeBoundary.CaptureAddress(activity, change.State);
                var frozenActivity = FreezeAddress(activity, address);
                changed[activity.InvocationId] = frozenActivity;
                if (!ReferenceEquals(activity, frozenActivity))
                    replacements[activity.InvocationId] = frozenActivity;

                Add(RulesNativeBoundary.Intent(request, address, cancel: false, activity.PrivateState!.CommittedAt));
            }
        }

        var wholeParentCancelled = commit.StateChanges.WorkflowExecution is
            { Operation: RuntimeStateChangeOperation.Upsert, State.Status: WorkflowExecutionStatus.Cancelled };
        var cancellationCandidates = changed.Values
            .Where(state => wholeParentCancelled
                ? state.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Superseded)
                : state.Status == ActivityExecutionStatus.Cancelled)
            .ToList();

        if (wholeParentCancelled)
        {
            string? continuation = null;
            do
            {
                var page = await activities.ListPageAsync(
                    new ActivityExecutionStatePageQuery(commit.WorkflowExecutionId, continuationToken: continuation),
                    cancellationToken);
                cancellationCandidates.AddRange(page.Items.Where(state =>
                    !changed.ContainsKey(state.InvocationId) &&
                    state.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Superseded)));

                if (page.NextContinuationToken is not null && page.NextContinuationToken == continuation)
                    throw new InvalidDataException("The native activity state page did not advance.");

                continuation = page.NextContinuationToken;
            } while (continuation is not null);
        }

        foreach (var activity in cancellationCandidates.Where(state =>
                     state.Execution.ActivityType == RunRules.ActivityType && state.PrivateState is not null))
        {
            if (!StringComparer.Ordinal.Equals(activity.Execution.WorkflowExecutionId, commit.WorkflowExecutionId))
                throw RulesNativeBoundary.Invalid();

            // The address was committed alongside Start. Never reconstruct a bookmark/registration from
            // invocation fields: the native trigger can already be consumed and its payload retired.
            if (!activity.Metadata.ContainsKey(RulesNativeBoundary.AddressMetadataKey))
                throw RulesNativeBoundary.Invalid();

            var request = RulesNativeBoundary.ReadState(activity);
            var address = RulesNativeBoundary.ReadAddress(activity);
            if (!StringComparer.Ordinal.Equals(request.WorkflowExecutionId, commit.WorkflowExecutionId))
                throw RulesNativeBoundary.Invalid();

            Add(RulesNativeBoundary.Intent(request, address, cancel: true, activity.PrivateState!.CommittedAt));
        }

        var stateChanges = replacements.Count == 0
            ? commit.StateChanges
            : WithActivityExecutions(commit.StateChanges, commit.StateChanges.ActivityExecutions
                .Select(change => replacements.TryGetValue(change.StateId, out var replacement)
                    ? change with { State = replacement }
                    : change)
                .ToArray());

        return commit with { StateChanges = stateChanges, PostCommitIntents = intents };

        void Add(RuntimePostCommitIntent intent)
        {
            var existing = intents.SingleOrDefault(candidate => candidate.IntentId == intent.IntentId);
            if (existing is null)
                intents.Add(intent);
            else if (!existing.IsEquivalentTo(intent))
                throw new InvalidDataException("The Rules native intent conflicts with the checkpoint.");
        }
    }

    private static ActivityExecutionState FreezeAddress(ActivityExecutionState activity, RuleNativeResultAddress address)
    {
        if (activity.Metadata.TryGetValue(RulesNativeBoundary.AddressMetadataKey, out _))
        {
            if (RulesNativeBoundary.ReadAddress(activity) != address)
                throw RulesNativeBoundary.Invalid();
            return activity;
        }

        var serialized = RulesNativeBoundary.Write(address).GetRawText();
        if (Encoding.UTF8.GetByteCount(serialized) > RuleLimits.MaxStateMetadataBytes)
            throw RulesNativeBoundary.Invalid();

        var metadata = activity.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata.Add(RulesNativeBoundary.AddressMetadataKey, serialized);
        return activity with { Metadata = metadata };
    }

    private static RuntimeCheckpointStateChangeSet WithActivityExecutions(
        RuntimeCheckpointStateChangeSet source,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activityExecutions) =>
        new(
            source.WorkflowExecution,
            source.Scheduler,
            activityExecutions,
            source.Bookmarks,
            source.DurableValues,
            source.Incidents,
            source.Operational,
            source.WorkflowDispatches,
            source.ActivityExecutionInspections,
            source.PostCommitOutbox,
            source.ActivityScopeCleanups,
            source.WorkflowDispatchCancellations,
            source.ConsumedSchedulerWorkItems,
            source.AlterationJobTerminalChange,
            source.BackgroundActivityWork);
}
