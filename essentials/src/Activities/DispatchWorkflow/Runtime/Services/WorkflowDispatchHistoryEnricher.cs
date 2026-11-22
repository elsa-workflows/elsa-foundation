using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>The native dispatch owner contributes its exact edge before logical history capture.</summary>
internal sealed class WorkflowDispatchHistoryEnricher(IPersistenceAccessContextAccessor? access = null,
    IRuntimeHistorySourceExtension? source = null) : IRuntimeCheckpointCommitEnricher
{
    private PersistenceAccessContext AccessContext => access?.Current ??
        throw new NotSupportedException("Captured native dispatch requires explicit persistence access.");
    public int Order => 1900;
    public bool CanSupplyHistorySource(string kind, IRuntimeHistorySourceExtension selected) =>
        kind is WorkflowDispatchLifecycle.StartChildIntentKind or WorkflowDispatchLifecycle.ResumeParentIntentKind &&
        source is not null && access is not null && ReferenceEquals(source, selected);

    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (source is null || commit.HistoryBatches is { Count: > 0 } ||
            commit.Checkpoint.Metadata.ContainsKey(RuntimeMetadataKeys.CoalescedFlush) || commit.StateChanges.WorkflowDispatches.Count == 0)
            return commit;
        var context = access?.Current;
        PersistenceScope? scope = null;
        var owned = new Dictionary<string, RuntimeHistoryCausation>(StringComparer.Ordinal);
        var enriched = await source.AddFactsAsync(commit, policy =>
        {
            if (policy is not { PolicyId: "runtime-history-v5", Version: 5 }) return [];
            var facts = Facts(commit, policy).ToArray();
            if (facts.Length == 0) return [];
            if (AccessContext != context) throw new InvalidOperationException("Dispatch history access changed.");
            scope = AccessContext.RequireScope();
            foreach (var fact in facts)
            {
                var dispatch = commit.StateChanges.WorkflowDispatches.Single(change => change.StateId == fact.SubjectId).State;
                var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
                var intentId = fact.Kind == RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested ? identity.StartIntentId : identity.ParentResumeIntentId;
                if (Intents(commit).Any(intent => intent.IntentId == intentId))
                    owned.Add(intentId, Origin(commit, fact));
            }
            return facts;
        }, cancellationToken);
        if (scope is null) return enriched;
        if (AccessContext != context) throw new InvalidOperationException("Dispatch history access changed.");
        return RuntimeHistorySourceFacts.WithPostCommitCausation(enriched,
            intent => owned.GetValueOrDefault(intent.IntentId), scope);
    }

    public ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batches = (commit.HistoryBatches ?? []).Where(batch => batch.Policy is { PolicyId: "runtime-history-v5", Version: 5 }).ToArray();
        if (batches.Length == 0) return ValueTask.CompletedTask;
        var scope = AccessContext.RequireScope();
        if (commit.Checkpoint.Metadata.ContainsKey(RuntimeMetadataKeys.CoalescedFlush))
        {
            var events = batches.SelectMany(batch => batch.Candidates).Select(candidate => candidate.Event).ToArray();
            foreach (var change in commit.StateChanges.PostCommitOutbox.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert &&
                         change.State.Intent.Kind is WorkflowDispatchLifecycle.StartChildIntentKind or WorkflowDispatchLifecycle.ResumeParentIntentKind))
            {
                var item = change.State;
                if (item.Intent.HistoryCausation is not { } cause || item.StimulusDeliverySourceScope != scope ||
                    cause.DeliveryId != item.OutboxItemId || !events.Any(e => e.HistoryEventId == cause.HistoryEventId && e.WorkflowExecutionId == cause.WorkflowExecutionId))
                    throw Changed();
            }
            return ValueTask.CompletedTask;
        }
        var expected = Facts(commit, batches[0].Policy).ToArray();
        var actual = (commit.HistorySourceFacts ?? []).Where(fact => IsChild(fact.Kind)).ToArray();
        if (expected.Length != actual.Length || expected.Any(fact => actual.Count(item => item.IsEquivalentTo(fact)) != 1)) throw Changed();
        foreach (var fact in expected)
        {
            var dispatch = commit.StateChanges.WorkflowDispatches.Single(change => change.StateId == fact.SubjectId).State;
            var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
            var id = fact.Kind == RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested ? identity.StartIntentId : identity.ParentResumeIntentId;
            if (Intents(commit).Where(intent => intent.IntentId == id).Any(intent => intent.HistoryCausation != Origin(commit, fact)) ||
                commit.StateChanges.PostCommitOutbox.Any(change => change.Operation == RuntimeStateChangeOperation.Upsert &&
                    change.State.Intent.IntentId == id && change.State.StimulusDeliverySourceScope != scope)) throw Changed();
        }
        return ValueTask.CompletedTask;
    }

    private static IEnumerable<RuntimeHistorySourceFact> Facts(RuntimeCheckpointCommit commit, RuntimeHistoryCapturePolicy policy)
    {
        var intents = Intents(commit).ToArray();
        foreach (var change in commit.StateChanges.WorkflowDispatches.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert))
        {
            var dispatch = change.State;
            var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
            if (dispatch.ParentWorkflowExecutionId == commit.WorkflowExecutionId && intents.Any(intent => intent.IntentId == identity.StartIntentId &&
                    intent.Kind == WorkflowDispatchLifecycle.StartChildIntentKind && intent.WorkflowExecutionId == commit.WorkflowExecutionId && intent.ActivityExecutionId == dispatch.ParentActivityExecutionId) &&
                policy.SupportedKinds.Contains(nameof(RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested), StringComparer.Ordinal))
                yield return new(RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested, dispatch.DispatchId, commit.Checkpoint.OccurredAt,
                    dispatch.ParentActivityExecutionId, relatedWorkflowExecutionId: dispatch.ChildWorkflowExecutionId, reasonCode: "child-start-committed");
            if (dispatch.ChildWorkflowExecutionId == commit.WorkflowExecutionId && WorkflowDispatchLifecycle.IsTerminal(dispatch.Status) &&
                commit.StateChanges.WorkflowExecution?.State is { } child && child.WorkflowExecutionId == dispatch.ChildWorkflowExecutionId && child.Status.IsTerminal() &&
                policy.SupportedKinds.Contains(nameof(RuntimeHistorySourceFactKind.ChildWorkflowSettled), StringComparer.Ordinal))
                yield return new(RuntimeHistorySourceFactKind.ChildWorkflowSettled, dispatch.DispatchId, commit.Checkpoint.OccurredAt,
                    relatedWorkflowExecutionId: dispatch.ParentWorkflowExecutionId, relatedActivityExecutionId: dispatch.ParentActivityExecutionId,
                    reasonCode: dispatch.Mode == WorkflowDispatchMode.FireAndForget ? "detached-child-settled" : "child-settled");
        }
    }

    private static IEnumerable<RuntimePostCommitIntent> Intents(RuntimeCheckpointCommit commit) => commit.PostCommitIntents.Concat(
        commit.StateChanges.PostCommitOutbox.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert && change.State.Intent is not null)
            .Select(change => change.State.Intent)).DistinctBy(intent => intent.IntentId, StringComparer.Ordinal);
    private static RuntimeHistoryCausation Origin(RuntimeCheckpointCommit commit, RuntimeHistorySourceFact fact)
    {
        var dispatch = commit.StateChanges.WorkflowDispatches.Single(change => change.StateId == fact.SubjectId).State;
        var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
        var intentId = fact.Kind == RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested ? identity.StartIntentId : identity.ParentResumeIntentId;
        return new(HistoryEventId: RuntimeHistoryIdentity.CreateId(commit.Checkpoint.CheckpointId, fact.Kind.ToString(), fact.SubjectId),
            WorkflowExecutionId: commit.WorkflowExecutionId, DeliveryId: RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intentId),
            CommandId: commit.Checkpoint.Metadata.GetValueOrDefault(RuntimeMetadataKeys.CommandId),
            SchedulerWorkItemId: commit.Checkpoint.Metadata.GetValueOrDefault(RuntimeMetadataKeys.SchedulerWorkItemId));
    }
    private static bool IsChild(RuntimeHistorySourceFactKind kind) => kind is RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested or RuntimeHistorySourceFactKind.ChildWorkflowSettled;
    private static RuntimeCheckpointCommitValidationException Changed() => new("Native child dispatch changed after its logical history capture.");
}
