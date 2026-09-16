using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// The provider-neutral structural rules for a <see cref="RuntimeCheckpointCommit"/>: the rules that can be decided from
/// the commit alone. The application layer applies them before a commit reaches any
/// <see cref="IRuntimeCheckpointCommitStore"/>, so every store receives an already-validated commit and no store keeps a
/// copy of these rules that could drift from another store's copy.
/// </summary>
/// <remarks>
/// <para>
/// Exactly two call sites exist: <see cref="RuntimeCheckpointCommitter"/> validates every commit it hands to a store, and
/// <see cref="CoalescingRuntimeCheckpointCommitStore"/> validates the folded commit it assembles itself. Everything else
/// that reaches a store is a commit the committer already validated.
/// </para>
/// <para>
/// What deliberately does not live here: <c>StateId</c> identity for the change-set collections, which
/// <see cref="RuntimeCheckpointStateChangeSet"/> enforces on construction; rules that read current persisted state
/// (write-once incident outcomes, dispatch transitions, cancellation resolution, test-scope admission, alteration claim
/// fences), which a store enforces inside its own atomic boundary; and provider storage limits or capability checks.
/// </para>
/// </remarks>
public static class RuntimeCheckpointCommitValidator
{
    private static readonly RuntimeStateChangeOperation[] UpsertOnly = [RuntimeStateChangeOperation.Upsert];
    private static readonly RuntimeStateChangeOperation[] UpsertOrDelete = [RuntimeStateChangeOperation.Upsert, RuntimeStateChangeOperation.Delete];
    private static readonly RuntimeStateChangeOperation[] AppendOrUpsert = [RuntimeStateChangeOperation.Append, RuntimeStateChangeOperation.Upsert];

    public static void Validate(RuntimeCheckpointCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.CommitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit.WorkflowExecutionId);

        var changes = commit.StateChanges;

        if (changes.WorkflowExecution is { } workflow)
        {
            ValidateChanges(commit, [workflow], "Workflow execution", state => state.WorkflowExecutionId, UpsertOnly);
            RequireStateId(workflow.StateId, workflow.State.WorkflowExecutionId, "Workflow execution", "WorkflowExecutionState.WorkflowExecutionId");
        }

        if (changes.Scheduler is { } scheduler)
        {
            ValidateChanges(commit, [scheduler], "Scheduler", state => state.WorkflowExecutionId, UpsertOnly);
            RequireStateId(scheduler.StateId, scheduler.State.WorkflowExecutionId, "Scheduler", "SchedulerState.WorkflowExecutionId");
        }

        ValidateChanges(commit, changes.ActivityExecutions, "Activity execution", state => state.Execution.WorkflowExecutionId, UpsertOnly);
        foreach (var change in changes.ActivityExecutions)
        {
            change.State.EnsureValueFlowCompatible();
            change.State.EnsureSupersessionCompatible();
            RequireMatchingProvenance(change.State.ExecutionScopeId, change.State.Attempt, change.State.Provenance, "Activity execution");
        }

        // Inspection evidence is only ever upserted: no producer retracts it, so a delete is a malformed commit.
        ValidateChanges(commit, changes.ActivityExecutionInspections, "Activity execution inspection", state => state.WorkflowExecutionId, UpsertOnly);
        foreach (var change in changes.ActivityExecutionInspections)
            RequireMatchingProvenance(change.State.ExecutionScopeId, change.State.Attempt, change.State.Provenance, "Activity execution inspection");

        ValidateChanges(commit, changes.Incidents, "Incident", state => state.WorkflowExecutionId, AppendOrUpsert);
        RequireUnique(changes.Incidents.Select(change => change.StateId), id => $"Incident '{id}' occurs more than once in one checkpoint commit.");

        ValidateChanges(commit, changes.DurableValues, "Durable value", state => state.WorkflowExecutionId, UpsertOrDelete);
        ValidateChanges(commit, changes.Bookmarks, "Bookmark", state => state.WorkflowExecutionId, UpsertOrDelete);

        ValidateChanges(commit, changes.Operational, "Operational", state => state.WorkflowExecutionId, UpsertOnly);
        var ownershipStateId = RuntimeExecutionOwnershipStateId.For(commit.WorkflowExecutionId);
        if (changes.Operational.Any(change => StringComparer.Ordinal.Equals(change.StateId, ownershipStateId)))
            throw new InvalidOperationException("Checkpoint operational changes cannot overwrite the reserved execution-ownership state.");

        ValidatePostCommitOutbox(commit);
        ValidateActivityScopeCleanups(commit);
        ValidateWorkflowDispatches(commit);

        foreach (var request in changes.WorkflowDispatchCancellations)
        {
            if (!StringComparer.Ordinal.Equals(request.ParentWorkflowExecutionId, commit.WorkflowExecutionId))
                throw new InvalidOperationException($"Workflow dispatch cancellation request '{request.DispatchId}' must be committed by its parent workflow execution.");
        }

        foreach (var consumed in changes.ConsumedSchedulerWorkItems)
        {
            RequireWorkflow(consumed.WorkflowExecutionId, commit, "Consumed scheduler work item");
            if (consumed.FencingToken <= 0)
                throw new InvalidOperationException($"Consumed scheduler work item '{consumed.WorkItemId}' requires a positive fencing token.");
        }

        if (changes.AlterationJobTerminalChange is { } alteration &&
            !StringComparer.Ordinal.Equals(alteration.CheckpointCommitId, commit.CommitId))
            throw new InvalidOperationException("Workflow alteration terminal evidence must reference its checkpoint commit ID.");
    }

    private static void ValidatePostCommitOutbox(RuntimeCheckpointCommit commit)
    {
        var outbox = commit.StateChanges.PostCommitOutbox;

        // Unlike state, an outbox item is not required to belong to the checkpoint's workflow execution: its intent names
        // the execution the work is delivered to. A waited child's terminal checkpoint carries the parent-resume intent
        // for its parent (WorkflowDispatchCompletionEnricher), so membership cannot be a structural rule here.
        ValidateOperations(outbox, "Post-commit outbox", UpsertOnly);

        var seen = new Dictionary<string, RuntimePostCommitOutboxItem>(StringComparer.Ordinal);
        foreach (var change in outbox)
        {
            if (change.State.Status != RuntimePostCommitOutboxStatus.Pending)
                throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");
            if (seen.TryGetValue(change.StateId, out var duplicate) && !PendingItemsEquivalent(duplicate, change.State))
                throw new InvalidOperationException($"Post-commit outbox item '{change.StateId}' occurs more than once with conflicting content.");
            seen[change.StateId] = change.State;
        }

        // A commit that carries intents carries exactly their pending outbox items, so a provider cannot persist the
        // checkpoint while dropping or substituting continuation work. A folded coalescing flush carries no intents: its
        // outbox is the segment's still-undelivered overlay, not a projection of this commit's own intents.
        if (commit.PostCommitIntents.Count == 0)
            return;

        var intents = new Dictionary<string, RuntimePostCommitIntent>(StringComparer.Ordinal);
        foreach (var intent in commit.PostCommitIntents)
        {
            var id = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId);
            if (intents.TryGetValue(id, out var duplicate) && !IntentsEquivalent(duplicate, intent))
                throw new InvalidOperationException($"Post-commit intent '{id}' occurs more than once with conflicting content.");
            intents[id] = intent;
        }

        if (!seen.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(intents.Keys))
            throw new InvalidOperationException("A checkpoint with post-commit intents must include their pending outbox state changes in the same atomic unit.");

        foreach (var change in outbox)
        {
            if (!IntentsEquivalent(intents[change.StateId], change.State.Intent))
                throw new InvalidOperationException($"Post-commit outbox item '{change.StateId}' does not match its checkpoint intent.");
        }
    }

    private static void ValidateActivityScopeCleanups(RuntimeCheckpointCommit commit)
    {
        var changes = commit.StateChanges;
        foreach (var cleanup in changes.ActivityScopeCleanups)
        {
            RequireWorkflow(cleanup.WorkflowExecutionId, commit, "Activity-scope cleanup");
            ArgumentException.ThrowIfNullOrWhiteSpace(cleanup.ExecutionScopeId);
            if (!cleanup.ActivityExecutionIds.Contains(cleanup.ExecutionScopeId, StringComparer.Ordinal))
                throw new InvalidOperationException("Activity scope cleanup must include its outer execution scope.");

            foreach (var bookmarkId in cleanup.BookmarkIds)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
                if (changes.Bookmarks.Any(change => StringComparer.Ordinal.Equals(change.StateId, bookmarkId)))
                    throw new InvalidOperationException($"Bookmark '{bookmarkId}' cannot be both changed and deleted by activity-scope cleanup in one checkpoint commit.");
            }

            foreach (var timerId in cleanup.TimerIds)
                ArgumentException.ThrowIfNullOrWhiteSpace(timerId);

            foreach (var workItemId in cleanup.SchedulerWorkItemIds)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
                if (changes.ConsumedSchedulerWorkItems.Any(item => StringComparer.Ordinal.Equals(item.WorkItemId, workItemId)))
                    throw new InvalidOperationException($"Scheduler work item '{workItemId}' cannot be both consumed and deleted by activity-scope cleanup in one checkpoint commit.");
            }
        }
    }

    private static void ValidateWorkflowDispatches(RuntimeCheckpointCommit commit)
    {
        var changes = commit.StateChanges.WorkflowDispatches;
        ValidateOperations(changes, "Workflow dispatch", UpsertOnly);

        var seen = new Dictionary<string, WorkflowDispatchRecord>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            WorkflowDispatchLifecycle.ValidateCheckpointOwnership(commit.WorkflowExecutionId, change.State);
            if (seen.TryGetValue(change.StateId, out var duplicate) && !WorkflowDispatchLifecycle.RecordsEqual(duplicate, change.State))
                throw new InvalidOperationException($"Workflow dispatch '{change.StateId}' occurs more than once with conflicting state.");
            seen[change.StateId] = change.State;
        }
    }

    /// <summary>
    /// The rules every state kind shares: an allowed operation, and membership of the checkpoint's workflow execution.
    /// <paramref name="kind"/> is the sentence-case label the messages are built from, e.g. <c>"Durable value"</c>.
    /// </summary>
    private static void ValidateChanges<TState>(
        RuntimeCheckpointCommit commit,
        IReadOnlyCollection<RuntimeStateChange<TState>> changes,
        string kind,
        Func<TState, string> workflowExecutionId,
        RuntimeStateChangeOperation[] allowedOperations)
    {
        ValidateOperations(changes, kind, allowedOperations);
        foreach (var change in changes)
            RequireWorkflow(workflowExecutionId(change.State), commit, $"{kind} state change");
    }

    private static void ValidateOperations<TState>(
        IReadOnlyCollection<RuntimeStateChange<TState>> changes,
        string kind,
        RuntimeStateChangeOperation[] allowedOperations)
    {
        foreach (var change in changes)
        {
            if (allowedOperations.Contains(change.Operation))
                continue;

            var allowed = string.Join(" or ", allowedOperations.Select(operation => $"'{operation}'"));
            throw new InvalidOperationException($"A checkpoint commit can only carry {kind.ToLowerInvariant()} {allowed} changes, not '{change.Operation}'.");
        }
    }

    private static void RequireStateId(string stateId, string modelId, string kind, string modelMember)
    {
        if (!StringComparer.Ordinal.Equals(stateId, modelId))
            throw new InvalidOperationException($"{kind} state change StateId must match {modelMember}.");
    }

    private static void RequireWorkflow(string workflowExecutionId, RuntimeCheckpointCommit commit, string subject)
    {
        if (!StringComparer.Ordinal.Equals(workflowExecutionId, commit.WorkflowExecutionId))
            throw new InvalidOperationException($"{subject} WorkflowExecutionId '{workflowExecutionId}' must match the checkpoint workflow execution ID '{commit.WorkflowExecutionId}'.");
    }

    private static void RequireUnique(IEnumerable<string> ids, Func<string, string> message)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                throw new InvalidOperationException(message(id));
        }
    }

    private static void RequireMatchingProvenance(
        string? executionScopeId,
        ActivityExecutionAttemptLineage? attempt,
        ActivitySchedulingProvenance provenance,
        string kind)
    {
        if (executionScopeId is not null && provenance.ExecutionScopeId is not null &&
            !StringComparer.Ordinal.Equals(executionScopeId, provenance.ExecutionScopeId))
            throw new InvalidOperationException($"{kind} ExecutionScopeId must match its scheduling provenance when both are present.");
        if (attempt is not null && provenance.Attempt is not null && attempt != provenance.Attempt)
            throw new InvalidOperationException($"{kind} Attempt must match its scheduling provenance when both are present.");
    }

    private static bool PendingItemsEquivalent(RuntimePostCommitOutboxItem left, RuntimePostCommitOutboxItem right) =>
        StringComparer.Ordinal.Equals(left.OutboxItemId, right.OutboxItemId) &&
        IntentsEquivalent(left.Intent, right.Intent) &&
        left.Status == right.Status &&
        left.RecordedAt == right.RecordedAt &&
        left.AvailableAt == right.AvailableAt &&
        left.RetryPolicy.IsEquivalentTo(right.RetryPolicy) &&
        left.DeliveryAttemptCount == right.DeliveryAttemptCount &&
        StringComparer.Ordinal.Equals(left.DeliveringOwnerId, right.DeliveringOwnerId) &&
        left.DeliveryStartedAt == right.DeliveryStartedAt &&
        left.DeliveredAt == right.DeliveredAt &&
        StringComparer.Ordinal.Equals(left.LastFailureMessage, right.LastFailureMessage) &&
        MetadataEquals(left.Metadata, right.Metadata) &&
        left.DeliveryFencingToken == right.DeliveryFencingToken &&
        left.DeliveryVisibleAfter == right.DeliveryVisibleAfter;

    private static bool IntentsEquivalent(RuntimePostCommitIntent left, RuntimePostCommitIntent right) =>
        StringComparer.Ordinal.Equals(left.IntentId, right.IntentId) &&
        StringComparer.Ordinal.Equals(left.WorkflowExecutionId, right.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(left.Kind, right.Kind) &&
        left.RecordedAt == right.RecordedAt &&
        StringComparer.Ordinal.Equals(left.ActivityExecutionId, right.ActivityExecutionId) &&
        StringComparer.Ordinal.Equals(left.IdempotencyKey, right.IdempotencyKey) &&
        StringComparer.Ordinal.Equals(left.DependsOnWaitRegistrationId, right.DependsOnWaitRegistrationId) &&
        left.WaitFailurePolicy == right.WaitFailurePolicy &&
        PayloadEquals(left.Payload, right.Payload) &&
        MetadataEquals(left.Metadata, right.Metadata);

    private static bool PayloadEquals(JsonElement? left, JsonElement? right) =>
        left.HasValue == right.HasValue &&
        (!left.HasValue || StringComparer.Ordinal.Equals(left.Value.GetRawText(), right!.Value.GetRawText()));

    private static bool MetadataEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(entry => right.TryGetValue(entry.Key, out var value) && StringComparer.Ordinal.Equals(entry.Value, value));
}
