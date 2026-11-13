using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Services;

/// <summary>Actual publication owner inputs; supported recording/handler qualification is composed separately.</summary>
internal sealed class PublishStimulusHistoryEnricher(IPersistenceAccessContextAccessor access,
    IRuntimeHistorySourceExtension? source = null) : IRuntimeCheckpointCommitEnricher
{
    public int Order => 1900;
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
            if (!RuntimeHistorySourceFacts.IsFinalProfile(policy) ||
                !new[] { "StimulusPublicationCommitted", "StimulusRoutingAttemptObserved", "StimulusRoutingDispositionObserved" }
                    .All(kind => policy.SupportedKinds.Contains(kind, StringComparer.Ordinal))) return [];
            if (access.Current != context) throw new InvalidOperationException("Persistence access changed during publication source capture.");
            scope = context.RequireScope();
            var originals = commit.PostCommitIntents.Concat(commit.StateChanges.PostCommitOutbox
                .Where(change => change.Operation == RuntimeStateChangeOperation.Upsert && change.State is not null).Select(change => change.State.Intent))
                .Where(IsPublication).GroupBy(intent => intent.IntentId, StringComparer.Ordinal).ToArray();
            if (originals.Any(group => group.Any(intent => !group.First().IsEquivalentTo(intent))))
                throw new InvalidOperationException("One publication has conflicting native source inputs.");
            return originals.Select(group => group.First()).Select(intent => new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.StimulusPublicationCommitted,
                intent.IntentId, commit.Checkpoint.OccurredAt, intent.ActivityExecutionId, causation: commit.Checkpoint.HistoryCausation));
        }, cancellationToken);
        if (scope is null) return enriched;
        if (access.Current != context) throw new InvalidOperationException("Persistence access changed during publication source capture.");
        string? Read(string key) => commit.Checkpoint.Metadata.TryGetValue(key, out var value) ? value : commit.Metadata.GetValueOrDefault(key);
        RuntimeHistoryCausation? Origin(RuntimePostCommitIntent intent) => !IsPublication(intent) ? null : new(
            HistoryEventId: RuntimeHistoryIdentity.CreateId(commit.Checkpoint.CheckpointId, "StimulusPublicationCommitted", intent.IntentId),
            WorkflowExecutionId: commit.WorkflowExecutionId,
            DeliveryId: RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId),
            CommandId: Read(RuntimeMetadataKeys.CommandId), SchedulerWorkItemId: Read(RuntimeMetadataKeys.SchedulerWorkItemId));
        return RuntimeHistorySourceFacts.WithPostCommitCausation(enriched, Origin, scope);
    }
    private static bool IsPublication(RuntimePostCommitIntent intent) => intent.Kind == PublishStimulusConstants.PublishStimulusIntentKind;
}
