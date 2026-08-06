using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Rehydrates one durable checkpoint preparation through the same deterministic enrichment, post-commit folding,
/// and final-policy path used by a live commit.
/// </summary>
public sealed class RuntimeCheckpointPreparationReplayer : IRuntimeCheckpointPreparationReplayer
{
    private readonly IRuntimeCheckpointPersistencePolicy _persistencePolicy;
    private readonly IReadOnlyCollection<IRuntimeCheckpointCommitEnricher> _enrichers;
    private readonly IReadOnlyCollection<RuntimePostCommitIntentHandlerContribution> _intentHandlerContributions;
    private readonly RuntimeCheckpointPreparationPayloadCodec _payloadCodec = new();

    /// <summary>Creates the shared live/recovery deterministic preparation replayer.</summary>
    /// <exception cref="ArgumentNullException">A required collaborator or contribution sequence is null.</exception>
    public RuntimeCheckpointPreparationReplayer(
        IRuntimeCheckpointPersistencePolicy persistencePolicy,
        IEnumerable<IRuntimeCheckpointCommitEnricher> enrichers,
        IEnumerable<RuntimePostCommitIntentHandlerContribution> intentHandlerContributions)
    {
        ArgumentNullException.ThrowIfNull(persistencePolicy);
        ArgumentNullException.ThrowIfNull(enrichers);
        ArgumentNullException.ThrowIfNull(intentHandlerContributions);

        _persistencePolicy = persistencePolicy;
        _enrichers = enrichers
            .Select((enricher, index) => new { Enricher = enricher, Index = index })
            .OrderBy(item => item.Enricher.Order)
            .ThenBy(item => item.Index)
            .Select(item => item.Enricher)
            .ToArray();
        _intentHandlerContributions = intentHandlerContributions.ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<RuntimeCheckpointPreparedCommit> RehydrateAsync(
        RuntimeCheckpointPreparedReservation reservation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(reservation.Token);
        ArgumentNullException.ThrowIfNull(reservation.CanonicalEnvelope);
        reservation.Token.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        if (reservation.Status != RuntimeLogicalCheckpointLedgerStatus.Prepared)
            throw new InvalidOperationException($"Checkpoint '{reservation.CommitId}' is not a durable Prepared reservation.");

        if (!StringComparer.Ordinal.Equals(reservation.InputFingerprint, reservation.CanonicalEnvelope.PayloadSha256))
            throw new InvalidOperationException($"Prepared checkpoint '{reservation.CommitId}' has a canonical-input digest that does not match its reservation token.");

        var request = _payloadCodec.Decode(reservation.CanonicalEnvelope);
        if (!StringComparer.Ordinal.Equals(request.Commit.CommitId, reservation.CommitId) ||
            request.InitialPersistenceMode != reservation.CandidatePersistenceMode)
        {
            throw new InvalidOperationException($"Prepared checkpoint '{reservation.CommitId}' canonical input does not match its durable reservation authority.");
        }

        var commit = request.Commit with
        {
            Checkpoint = request.Commit.Checkpoint with { Provenance = reservation.Provenance },
            ExpectedFence = reservation.ExpectedFence
        };

        foreach (var enricher in _enrichers)
            commit = await enricher.EnrichAsync(commit, cancellationToken);

        var postCommitOutbox = RuntimePostCommitOutboxItems.CreatePendingChanges(commit, _intentHandlerContributions);
        if (postCommitOutbox.Count > 0)
            commit = commit with { StateChanges = commit.StateChanges.WithPostCommitOutbox(postCommitOutbox) };

        var decision = await _persistencePolicy.DecideAsync(commit.Checkpoint, cancellationToken);
        if (decision.Mode == RuntimeCheckpointPersistenceMode.Deferred &&
            (!(reservation.Provenance.ExecutionContext ?? throw new InvalidOperationException("Prepared provenance has no execution context.")).IsEmpty || commit.StateChanges.PostCommitOutbox.Count > 0))
        {
            decision = new RuntimeCheckpointPersistenceDecision(
                RuntimeCheckpointPersistenceMode.Immediate,
                "Generic checkpoint context or post-commit work requires an immediate atomic commit.");
        }

        return new RuntimeCheckpointPreparedCommit(reservation.Token, commit, decision);
    }
}
