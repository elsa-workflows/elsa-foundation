using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Services;

/// <summary>Actual publication owner inputs, qualified against the selected source before profile admission.</summary>
internal sealed class PublishStimulusHistoryEnricher(IPersistenceAccessContextAccessor access,
    IRuntimeHistorySourceExtension? source = null) : IRuntimeCheckpointCommitEnricher
{
    public int Order => 1900;
    public bool CanSupplyHistorySource(string intentKind, IRuntimeHistorySourceExtension selectedSource) =>
        intentKind == PublishStimulusConstants.PublishStimulusIntentKind && source is not null && ReferenceEquals(source, selectedSource);
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit); cancellationToken.ThrowIfCancellationRequested();
        if (source is null || commit.HistoryBatches is { Count: > 0 } ||
            commit.Checkpoint.Metadata.ContainsKey(RuntimeMetadataKeys.CoalescedFlush) || !commit.PostCommitIntents.Any(IsPublication) &&
            !commit.StateChanges.PostCommitOutbox.Any(change => change.Operation == RuntimeStateChangeOperation.Upsert && change.State is { } item && IsPublication(item.Intent)))
            return commit;
        var context = access.Current;
        PersistenceScope? scope = null;
        var enriched = await source.AddFactsAsync(commit, policy =>
        {
            if (!SupportsPublication(policy)) return [];
            if (access.Current != context) throw new InvalidOperationException("Persistence access changed during publication source capture.");
            scope = context.RequireScope();
            var originals = commit.PostCommitIntents.Concat(commit.StateChanges.PostCommitOutbox
                .Where(change => change.Operation == RuntimeStateChangeOperation.Upsert && change.State is not null).Select(change => change.State.Intent))
                .Where(IsPublication).GroupBy(intent => intent.IntentId, StringComparer.Ordinal).ToArray();
            if (originals.Any(group => group.Any(intent => !group.First().IsEquivalentTo(intent))))
                throw new InvalidOperationException("One publication has conflicting native source inputs.");
            return originals.Select(group => group.First()).Select(intent => Fact(commit, intent));
        }, cancellationToken);
        if (scope is null) return enriched;
        if (access.Current != context) throw new InvalidOperationException("Persistence access changed during publication source capture.");
        return RuntimeHistorySourceFacts.WithPostCommitCausation(enriched, intent => IsPublication(intent) ? Origin(commit, intent) : null, scope);
    }

    public ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit); cancellationToken.ThrowIfCancellationRequested();
        var batches = (commit.HistoryBatches ?? []).Where(batch => SupportsPublication(batch.Policy)).ToArray();
        if (batches.Length == 0) return ValueTask.CompletedTask;
        var pending = commit.StateChanges.PostCommitOutbox.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert &&
            change.State is { } item && IsPublication(item.Intent)).Select(change => change.State).ToArray();
        var scope = pending.Length == 0 ? null : access.Current.RequireScope();
        var originals = commit.PostCommitIntents.Concat(pending.Select(item => item.Intent)).Where(IsPublication)
            .GroupBy(intent => intent.IntentId, StringComparer.Ordinal).ToArray();
        var facts = (commit.HistorySourceFacts ?? []).Where(fact => fact.Kind == RuntimeHistorySourceFactKind.StimulusPublicationCommitted).ToArray();
        if (commit.Checkpoint.Metadata.ContainsKey(RuntimeMetadataKeys.CoalescedFlush))
        {
            // A physical flush carries original raw sources and their already captured logical groups, never a new send.
            var events = batches.SelectMany(batch => batch.Candidates).Select(candidate => candidate.Event)
                .Where(item => item.Name == "StimulusPublicationCommitted").ToArray();
            if (commit.PostCommitIntents.Any(IsPublication) || pending.Any(item => item.Intent.HistoryCausation is not { } cause ||
                cause.WorkflowExecutionId != commit.WorkflowExecutionId || cause.DeliveryId != item.OutboxItemId ||
                item.StimulusDeliverySourceScope != scope ||
                !events.Any(itemEvent => itemEvent.HistoryEventId == cause.HistoryEventId && itemEvent.WorkflowExecutionId == commit.WorkflowExecutionId)))
                throw Changed();
            return ValueTask.CompletedTask;
        }
        if (facts.Length != originals.Length || originals.Any(group => group.Any(intent => !group.First().IsEquivalentTo(intent)) ||
            group.Any(intent => intent.HistoryCausation != Origin(commit, intent)) ||
            facts.Count(fact => fact.SubjectId == group.Key && fact.IsEquivalentTo(Fact(commit, group.First()))) != 1) ||
            pending.Any(item => item.StimulusDeliverySourceScope != scope))
            throw Changed();
        return ValueTask.CompletedTask;
    }

    private static RuntimeCheckpointCommitValidationException Changed() => new("Publication inputs changed after their required logical history was captured.");
    private static bool SupportsPublication(RuntimeHistoryCapturePolicy policy) => RuntimeHistorySourceFacts.IsFinalProfile(policy) &&
        RuntimeHistorySourceFacts.PublicationObservationKinds.Prepend("StimulusPublicationCommitted")
            .All(kind => policy.SupportedKinds.Contains(kind, StringComparer.Ordinal));
    private static RuntimeHistorySourceFact Fact(RuntimeCheckpointCommit commit, RuntimePostCommitIntent intent) =>
        new(RuntimeHistorySourceFactKind.StimulusPublicationCommitted, intent.IntentId, commit.Checkpoint.OccurredAt,
            intent.ActivityExecutionId, causation: commit.Checkpoint.HistoryCausation);
    private static RuntimeHistoryCausation Origin(RuntimeCheckpointCommit commit, RuntimePostCommitIntent intent)
    {
        string? Read(string key) => commit.Checkpoint.Metadata.TryGetValue(key, out var value) ? value : commit.Metadata.GetValueOrDefault(key);
        return new(
            HistoryEventId: RuntimeHistoryIdentity.CreateId(commit.Checkpoint.CheckpointId, "StimulusPublicationCommitted", intent.IntentId),
            WorkflowExecutionId: commit.WorkflowExecutionId,
            DeliveryId: RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId),
            CommandId: Read(RuntimeMetadataKeys.CommandId), SchedulerWorkItemId: Read(RuntimeMetadataKeys.SchedulerWorkItemId));
    }
    private static bool IsPublication(RuntimePostCommitIntent intent) => intent.Kind == PublishStimulusConstants.PublishStimulusIntentKind;
}
