using Elsa.Activities.Primitives.Activities;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Services;

/// <summary>Checks the shared observed schema contract before a publish checkpoint accepts protocol data.</summary>
internal sealed class PublishStimulusSchemaGuard(ISchemaDormancyCheck? dormancy = null) : IRuntimeCheckpointCommitEnricher
{
    private static readonly SchemaVersionRequirement[] Requirements = [new("RuntimePostCommitOutbox", "2.0.0")];
    public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default) => ValueTask.FromResult(commit);
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
