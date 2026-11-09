using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Etl.Workflows.Runtime.Services;

/// <summary>Captures ETL responsibility in the ordinary native checkpoint/outbox transaction.</summary>
internal sealed class EtlCheckpointEnricher(IActivityExecutionStateStore activities) : IRuntimeCheckpointCommitEnricher
{
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        var intents = commit.PostCommitIntents.ToList();
        var changed = commit.StateChanges.ActivityExecutions
            .Where(change => change.Operation == RuntimeStateChangeOperation.Upsert)
            .ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);

        if (commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkCreated)
        {
            foreach (var change in commit.StateChanges.Bookmarks.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert))
            {
                if (!changed.TryGetValue(change.State.ActivityExecutionId, out var activity) || activity.Execution.ActivityType != RunEtl.ActivityType)
                    continue;
                if (activity.Status != ActivityExecutionStatus.Suspended)
                    throw EtlNativeBoundary.Invalid();
                var invocation = EtlNativeBoundary.Invocation(activity);
                if (invocation.ResultAddress.WorkflowExecutionId != commit.WorkflowExecutionId)
                    throw EtlNativeBoundary.Invalid();
                EtlNativeBoundary.EnsureBookmark(invocation.ResultAddress, change.State);
                Add(EtlNativeBoundary.Intent(invocation, false, activity.PrivateState!.CommittedAt));
            }
        }

        var wholeParentCancelled = commit.StateChanges.WorkflowExecution is
            { Operation: RuntimeStateChangeOperation.Upsert, State.Status: WorkflowExecutionStatus.Cancelled };
        var candidates = changed.Values.Where(state => state.Status == ActivityExecutionStatus.Cancelled).ToList();
        if (wholeParentCancelled)
        {
            string? continuation = null;
            do
            {
                var page = await activities.ListPageAsync(new ActivityExecutionStatePageQuery(commit.WorkflowExecutionId,
                    continuationToken: continuation), cancellationToken);
                candidates.AddRange(page.Items.Where(state => !changed.ContainsKey(state.InvocationId) &&
                    state.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled)));
                if (page.NextContinuationToken is not null && page.NextContinuationToken == continuation)
                    throw new InvalidDataException("The native activity state page did not advance.");
                continuation = page.NextContinuationToken;
            } while (continuation is not null);
        }

        foreach (var activity in candidates.Where(state => state.Execution.ActivityType == RunEtl.ActivityType && state.PrivateState is not null))
        {
            var invocation = EtlNativeBoundary.Invocation(activity);
            if (invocation.ResultAddress.WorkflowExecutionId != commit.WorkflowExecutionId)
                throw EtlNativeBoundary.Invalid();
            Add(EtlNativeBoundary.Intent(invocation, true, activity.PrivateState!.CommittedAt));
        }
        return commit with { PostCommitIntents = intents };

        void Add(RuntimePostCommitIntent intent)
        {
            var existing = intents.SingleOrDefault(candidate => candidate.IntentId == intent.IntentId);
            if (existing is null)
                intents.Add(intent);
            else if (!existing.IsEquivalentTo(intent))
                throw new InvalidDataException("The ETL native intent conflicts with the checkpoint.");
        }
    }
}
