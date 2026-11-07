using Elsa.Activities.Primitives.Activities;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives.Services;

/// <summary>Checks the shared observed schema contract before a publish checkpoint accepts protocol data.</summary>
internal sealed class PublishStimulusSchemaGuard(ISchemaDormancyCheck? dormancy = null, IServiceProvider? services = null) : IRuntimeCheckpointCommitEnricher
{
    private static readonly SchemaVersionRequirement[] Requirements = [new("RuntimePostCommitOutbox", "2.0.0")];
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        if (services is null) return commit;
        var contributor = services.GetService<IRuntimeHistorySourceContributor>();
        if (contributor is null) return commit;
        RuntimeHistoryCapturePolicy? pin = null;
        var enriched = await contributor.AddFactsAsync(commit, policy =>
        {
            pin = policy;
            return policy.SupportedKinds.Contains(nameof(RuntimeHistorySourceFactKind.StimulusPublicationCommitted), StringComparer.Ordinal)
                ? PublicationFacts(commit) : [];
        }, cancellationToken);
        if (pin is null || !pin.SupportedKinds.Contains(nameof(RuntimeHistorySourceFactKind.StimulusPublicationCommitted), StringComparer.Ordinal) ||
            !pin.SupportedKinds.Contains(PublishStimulusHistoryObservations.AttemptKind, StringComparer.Ordinal) ||
            !pin.SupportedKinds.Contains(PublishStimulusHistoryObservations.DispositionKind, StringComparer.Ordinal) || enriched.HistorySourceFacts is null) return enriched;
        var owned = enriched.HistorySourceFacts.Where(fact => fact.Kind == RuntimeHistorySourceFactKind.StimulusPublicationCommitted)
            .Select(fact => fact.SubjectId).ToHashSet(StringComparer.Ordinal);
        if (owned.Count == 0) return enriched;
        if (services.GetService<IRuntimeHistoryObservationWriter>() is not { } writer ||
            services.GetService<IRuntimeHistoryStore>() is not { } reader ||
            services.GetService<IRuntimePostCommitIntentDispatcher>() is not { } dispatcher ||
            !dispatcher.CanRecordHistory(PublishStimulusConstants.PublishStimulusIntentKind, writer, reader))
            throw new RuntimeSchedulerWorkUnavailableException("runtime.history.capture-unavailable", "The originally captured publication history owner is unavailable.");
        return RuntimeHistorySourceFacts.WithPostCommitCausation(enriched, intent =>
            intent.Kind == PublishStimulusConstants.PublishStimulusIntentKind && owned.Contains(intent.IntentId)
                ? new(RuntimeHistoryIdentity.CreateId(commit.Checkpoint.CheckpointId, nameof(RuntimeHistorySourceFactKind.StimulusPublicationCommitted), intent.IntentId),
                    commit.WorkflowExecutionId, RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId)) : null,
            services.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope());
    }

    private static IEnumerable<RuntimeHistorySourceFact> PublicationFacts(RuntimeCheckpointCommit commit)
    {
        var sources = new Dictionary<string, RuntimePostCommitIntent>(StringComparer.Ordinal);
        foreach (var intent in commit.PostCommitIntents.Concat(commit.StateChanges.PostCommitOutbox
                     .Where(change => change.Operation == RuntimeStateChangeOperation.Upsert).Select(change => change.State.Intent))
                     .Where(intent => intent.Kind == PublishStimulusConstants.PublishStimulusIntentKind))
        {
            if (intent.WorkflowExecutionId != commit.WorkflowExecutionId || string.IsNullOrWhiteSpace(intent.ActivityExecutionId))
                throw new InvalidOperationException("Publication history requires the exact publishing activity's owned intent.");
            _ = new PublishStimulusIntentAdapter().CreateRequest(intent);
            if (sources.TryGetValue(intent.IntentId, out var existing) && !existing.IsEquivalentTo(intent))
                throw new InvalidOperationException("Publication history contains conflicting representations of its source intent.");
            sources[intent.IntentId] = intent;
        }
        return sources.Values.OrderBy(intent => intent.IntentId, StringComparer.Ordinal).Select(intent =>
            new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.StimulusPublicationCommitted, intent.IntentId, intent.RecordedAt,
                activityExecutionId: intent.ActivityExecutionId,
                causation: new(WorkflowExecutionId: commit.WorkflowExecutionId, DeliveryId: commit.CommitId + ":" + intent.IntentId)));
    }
    public async ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit); cancellationToken.ThrowIfCancellationRequested();
        if (!commit.PostCommitIntents.Any(intent => intent.Kind == PublishStimulusConstants.PublishStimulusIntentKind) &&
            !commit.StateChanges.PostCommitOutbox.Any(change => change.Operation == RuntimeStateChangeOperation.Upsert && change.State?.Intent.Kind == PublishStimulusConstants.PublishStimulusIntentKind))
            return;
        if (dormancy is null)
            throw new SchemaDormancyRefusedException("RuntimePostCommitOutbox", "(none)", "2.0.0", PublishStimulusConstants.PublishStimulusIntentKind,
                "Named-event publication requires an observed finalized outbox schema; this host has no shared dormancy check.");
        await dormancy.EnsureAvailableAsync(Requirements, PublishStimulusConstants.PublishStimulusIntentKind, cancellationToken);
    }
}
