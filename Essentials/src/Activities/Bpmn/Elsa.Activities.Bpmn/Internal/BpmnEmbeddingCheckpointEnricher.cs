using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Freezes the actual typed registration and its start/cancellation responsibility with the native checkpoint.</summary>
internal sealed class BpmnEmbeddingCheckpointEnricher(IActivityExecutionStateStore activities, BpmnNativeWorkBoundary native) : IRuntimeCheckpointCommitEnricher
{
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        var changes = commit.StateChanges.ActivityExecutions.ToDictionary(item => item.StateId, StringComparer.Ordinal);
        var intents = commit.PostCommitIntents.ToList();
        if (commit.Checkpoint.Name == RuntimeCheckpointNames.BookmarkCreated)
        {
            foreach (var bookmark in commit.StateChanges.Bookmarks.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert))
            {
                if (!changes.TryGetValue(bookmark.State.ActivityExecutionId, out var change) || change.State.Execution.ActivityType != RunBpmn.ActivityType) continue;
                var activity = change.State;
                var invocation = BpmnEmbeddingBoundary.Read(activity);
                var registration = activity.TriggerRegistrations?.SingleOrDefault() ?? throw BpmnEmbeddingJson.Invalid();
                if (activity.Status != ActivityExecutionStatus.Suspended || registration.ResumeTargetKey != invocation.ResumeTargetId ||
                    registration.InvocationId != invocation.NativeInvocationId || registration.PayloadType != BpmnEmbeddingBoundary.CompletionType ||
                    registration.StimulusType != BpmnEmbeddingBoundary.StimulusType || registration.StimulusHash != invocation.ExecutionId ||
                    registration.DeduplicationPolicy != ActivityTriggerDeduplicationPolicy.Once) throw BpmnEmbeddingJson.Invalid();
                invocation = invocation with { RegistrationId = registration.RegistrationId, NativeAttemptId = activity.PrivateState!.ProducedByAttemptId };
                BpmnEmbeddingBoundary.EnsureBookmark(invocation, bookmark.State);
                var previous = activity.PrivateState!;
                var frozen = new RunBpmnState(1, BpmnEmbeddingJson.Write(invocation));
                activity = activity with { PrivateState = new(previous.InvocationId, previous.StateVersion,
                    ValueEnvelope.Inline(previous.Value.Type, JsonSerializer.SerializeToElement(frozen), previous.Value.Policy),
                    previous.ProducedByAttemptId, previous.CommittedAt) };
                changes[change.StateId] = change with { State = activity };
                Add(Intent(invocation, false, previous.CommittedAt));
            }
        }
        var cancelled = changes.Values.Select(change => change.State).Where(state => state.Status == ActivityExecutionStatus.Cancelled).ToList();
        if (commit.StateChanges.WorkflowExecution?.State.Status == WorkflowExecutionStatus.Cancelled)
        {
            string? continuation = null; var seen = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var page = await activities.ListPageAsync(new(commit.WorkflowExecutionId, continuationToken: continuation), cancellationToken);
                cancelled.AddRange(page.Items.Where(state => !changes.ContainsKey(state.InvocationId) &&
                    state.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled)));
                continuation = page.NextContinuationToken;
                if (continuation is not null && !seen.Add(continuation)) throw BpmnEmbeddingJson.Invalid();
            } while (continuation is not null);
        }
        foreach (var activity in cancelled.Where(state => state.Execution.ActivityType == RunBpmn.ActivityType))
        {
            // Cancellation retires the private value in its proposed state. Its committed predecessor still
            // contains the actual registration; retain the cancellation intent before that writer removes it.
            var retained = activity.PrivateState is not null ? activity :
                await native.PhysicalActivities.FindAsync(activity.Execution.WorkflowExecutionId, activity.InvocationId, cancellationToken);
            if (retained?.PrivateState is null) continue; // No committed RunBpmn wait was accepted.
            var invocation = BpmnEmbeddingBoundary.Read(retained);
            if (invocation.RegistrationId is not null) Add(Intent(invocation, true, retained.PrivateState.CommittedAt));
        }
        return commit with { StateChanges = commit.StateChanges.WithActivityExecutions(changes.Values.ToArray()), PostCommitIntents = intents };

        void Add(RuntimePostCommitIntent intent)
        {
            var existing = intents.SingleOrDefault(item => item.IntentId == intent.IntentId);
            if (existing is null) intents.Add(intent);
            else if (!existing.IsEquivalentTo(intent)) throw BpmnEmbeddingJson.Invalid();
        }
    }

    internal static RuntimePostCommitIntent Intent(BpmnEmbeddedInvocation invocation, bool cancel, DateTimeOffset at)
    {
        var id = "bpmn-embedded-" + (cancel ? "cancel:" : "start:") + BpmnModelIdentity.Create(invocation.ExecutionId);
        return new(id, invocation.NativeExecutionId, cancel ? BpmnEmbeddingBoundary.CancelKind : BpmnEmbeddingBoundary.StartKind,
            at, invocation.NativeInvocationId, id, BpmnEmbeddingJson.Write(invocation));
    }
}
