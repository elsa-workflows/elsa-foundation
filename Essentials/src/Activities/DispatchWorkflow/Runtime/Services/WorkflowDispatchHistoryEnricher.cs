using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Records only this module's validated raw start responsibility before semantic capture.</summary>
internal sealed class WorkflowDispatchHistoryEnricher(IServiceProvider services) : IRuntimeCheckpointCommitEnricher
{
    public int Order => 25;
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (commit.Checkpoint.Name is not (RuntimeCheckpointNames.ActivityCompleted or RuntimeCheckpointNames.ActivitySuspended or RuntimeCheckpointNames.BookmarkCreated)) return commit;
        var contributor = services.GetService<IRuntimeHistorySourceContributor>();
        if (contributor is null) return commit;
        RuntimeHistoryCapturePolicy? pin = null;
        var enriched = await contributor.AddFactsAsync(commit, policy =>
        {
            pin = policy;
            return policy.SupportedKinds.Contains(nameof(RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested), StringComparer.Ordinal)
                ? RequestedFacts(commit) : [];
        }, cancellationToken);
        if (pin is null || !pin.SupportedKinds.Contains(nameof(RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested), StringComparer.Ordinal) ||
            !pin.SupportedKinds.Contains(ChildStartHistoryObservations.AttemptKind, StringComparer.Ordinal) ||
            !pin.SupportedKinds.Contains(ChildStartHistoryObservations.DispositionKind, StringComparer.Ordinal) || enriched.HistorySourceFacts is null) return enriched;
        var owned = enriched.HistorySourceFacts.Where(fact => fact.Kind == RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested)
            .Select(fact => new WorkflowDispatchIdentity(commit.WorkflowExecutionId, fact.ActivityExecutionId!).StartIntentId)
            .ToHashSet(StringComparer.Ordinal);
        if (owned.Count == 0) return enriched;
        if (services.GetService<IRuntimeHistoryObservationWriter>() is not { } writer ||
            services.GetService<IRuntimeHistoryStore>() is not { } reader ||
            services.GetService<IRuntimePostCommitIntentDispatcher>() is not { } dispatcher ||
            !dispatcher.CanRecordHistory(DispatchWorkflowConstants.StartChildIntentKind, writer, reader))
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The originally captured child-start history owner is unavailable.");
        return RuntimeHistorySourceFacts.WithPostCommitCausation(enriched, intent =>
            intent.Kind == DispatchWorkflowConstants.StartChildIntentKind && owned.Contains(intent.IntentId)
                ? new(RuntimeHistoryIdentity.CreateId(commit.Checkpoint.CheckpointId, nameof(RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested),
                        new WorkflowDispatchIdentity(commit.WorkflowExecutionId, intent.ActivityExecutionId!).DispatchId),
                    commit.WorkflowExecutionId, RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId)) : null,
            services.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope());
    }

    private static IEnumerable<RuntimeHistorySourceFact> RequestedFacts(RuntimeCheckpointCommit commit)
    {
        var intents = new Dictionary<string, RuntimePostCommitIntent>(StringComparer.Ordinal);
        foreach (var intent in commit.PostCommitIntents.Concat(commit.StateChanges.PostCommitOutbox
                     .Where(change => change.Operation == RuntimeStateChangeOperation.Upsert).Select(change => change.State.Intent))
                     .Where(intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind))
        {
            if (intents.TryGetValue(intent.IntentId, out var old) && !old.IsEquivalentTo(intent))
                throw new InvalidOperationException("Child dispatch history has conflicting source intent representations.");
            intents[intent.IntentId] = intent;
        }
        foreach (var change in commit.StateChanges.WorkflowDispatches.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert && change.State.Status == WorkflowDispatchStatus.Pending)
                     .OrderBy(change => change.StateId, StringComparer.Ordinal))
        {
            var dispatch = change.State; var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
            if (!intents.TryGetValue(identity.StartIntentId, out var intent)) continue;
            if (dispatch.ParentWorkflowExecutionId != commit.WorkflowExecutionId || intent.WorkflowExecutionId != commit.WorkflowExecutionId ||
                intent.ActivityExecutionId != dispatch.ParentActivityExecutionId || intent.IdempotencyKey != identity.StartIdempotencyKey ||
                !intent.RecordedAt.EqualsExact(dispatch.CreatedAt) || intent.Payload is null)
                throw new InvalidOperationException("Child dispatch history requires its exact committed parent start responsibility.");
            var payload = intent.Payload.Value.Deserialize<WorkflowDispatchStartPayload>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Child dispatch history requires its typed start source.");
            ChildStartExecutor.ValidatePayloadAgainstDispatch(intent.IntentId, payload, dispatch);
            var activity = commit.StateChanges.ActivityExecutions.SingleOrDefault(item => item.StateId == dispatch.ParentActivityExecutionId)?.State;
            var attempt = activity?.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ActivityAttemptActivationClaim);
            yield return new(RuntimeHistorySourceFactKind.ChildWorkflowDispatchRequested, dispatch.DispatchId, dispatch.CreatedAt,
                dispatch.ParentActivityExecutionId, attempt,
                new(WorkflowExecutionId: commit.WorkflowExecutionId, DeliveryId: RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId)),
                relatedWorkflowExecutionId: dispatch.ChildWorkflowExecutionId);
        }
    }
}
