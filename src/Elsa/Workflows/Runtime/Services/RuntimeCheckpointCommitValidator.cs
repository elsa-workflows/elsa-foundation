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
/// <see cref="RuntimeCheckpointStateChangeSet"/> enforces on construction; and rules that read current persisted state.
/// Those stay inside each store's atomic boundary, but each is one shared function every store calls on the state it read
/// there: <see cref="IncidentStateTransitionValidator"/>, <see cref="WorkflowDispatchLifecycle"/>,
/// <see cref="RuntimeExecutionFenceValidator"/>, <see cref="ConsumedSchedulerWorkItem.IsFencedBy"/>,
/// <see cref="RuntimePostCommitOutboxItem.IsEquivalentPendingItem"/>, <see cref="WorkflowTestScopeAdmission"/>, and
/// <c>WorkflowAlterationTerminalEvidence</c>. Provider storage limits and capability checks stay in the provider.
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
        RuntimeExecutionOwnershipStateId.EnsureNotWritten(changes.Operational);

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
        ValidateOperations(outbox, "Post-commit outbox", UpsertOnly);

        var seen = new Dictionary<string, RuntimePostCommitOutboxItem>(StringComparer.Ordinal);
        foreach (var change in outbox)
        {
            if (change.State.Status != RuntimePostCommitOutboxStatus.Pending)
                throw new InvalidOperationException("Only pending post-commit outbox items can be saved as pending.");
            if (seen.TryGetValue(change.StateId, out var duplicate) && !duplicate.IsEquivalentPendingItem(change.State))
                throw new InvalidOperationException($"Post-commit outbox item '{change.StateId}' occurs more than once with conflicting content.");
            RequireDeliverableFromThisCheckpoint(commit, change.State);
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
            if (intents.TryGetValue(id, out var duplicate) && !duplicate.IsEquivalentTo(intent))
                throw new InvalidOperationException($"Post-commit intent '{id}' occurs more than once with conflicting content.");
            intents[id] = intent;
        }

        if (!seen.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(intents.Keys))
            throw new InvalidOperationException("A checkpoint with post-commit intents must include their pending outbox state changes in the same atomic unit.");

        foreach (var change in outbox)
        {
            if (!intents[change.StateId].IsEquivalentTo(change.State.Intent))
                throw new InvalidOperationException($"Post-commit outbox item '{change.StateId}' does not match its checkpoint intent.");
        }
    }

    /// <summary>
    /// An outbox item's intent names the execution its work is delivered to. That is the checkpoint's own execution, with
    /// one exception: a child's terminal checkpoint may carry work for its parent, but only when the same commit carries
    /// the terminal dispatch record that links that parent to this child. The rule keys on that linkage, not on an intent
    /// kind, so the runtime stays independent of the activities that emit such intents.
    /// </summary>
    private static void RequireDeliverableFromThisCheckpoint(RuntimeCheckpointCommit commit, RuntimePostCommitOutboxItem item)
    {
        var target = item.Intent.WorkflowExecutionId;
        if (StringComparer.Ordinal.Equals(target, commit.WorkflowExecutionId))
            return;

        var linked = commit.StateChanges.WorkflowDispatches.Any(change =>
            WorkflowDispatchLifecycle.IsTerminal(change.State.Status) &&
            StringComparer.Ordinal.Equals(change.State.ParentWorkflowExecutionId, target) &&
            StringComparer.Ordinal.Equals(change.State.ChildWorkflowExecutionId, commit.WorkflowExecutionId));
        if (!linked)
            throw new InvalidOperationException(
                $"Post-commit outbox item '{item.OutboxItemId}' targets workflow execution '{target}', which is neither the checkpoint workflow execution '{commit.WorkflowExecutionId}' nor the parent of a terminal workflow dispatch for it in this commit.");
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
}
