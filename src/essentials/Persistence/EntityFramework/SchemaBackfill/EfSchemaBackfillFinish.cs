using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// The backfill's writes to a family's finish record (spec 186, FR-008, FR-014 and FR-018): the run's claim, the
/// completion a verification pass proved, and its withdrawal when a row below it turns up. Each is a compare-and-set on
/// the record, read afresh and decided again when it is lost, through <see cref="CompareAndSetAsync{T}"/>.
/// </summary>
internal sealed class EfSchemaBackfillFinish(
    EfSchemaModuleGate gate,
    EfSchemaBackfillSettle settle,
    EfSchemaBackfillStatusBoard status,
    EfSchemaBackfillOptions options,
    TimeProvider time,
    ILogger logger)
{
    private const int RecordAttempts = 3;

    private readonly string _worker = Guid.NewGuid().ToString("N");

    /// <summary>The family's finalization record as it stands now, not as the gate last observed it.</summary>
    public Task<SchemaFinalizationRecord?> ReadAsync(EfSchemaBackfillScopeRunner scopes, string family, CancellationToken cancellationToken) =>
        scopes.WithScopeAsync(scope => new EfSchemaFinalizationStore(scope.Context, time).FindAsync(family, cancellationToken), cancellationToken);

    /// <summary>
    /// Records the family complete at the run's target (FR-014), unless a completion at or after it already stands, or the
    /// completion was withdrawn after the verification pass began, which only a new pass can answer. Then the gate reads
    /// the record again, so this host's dormancy check answers from it at once (FR-017). Returns why nothing was recorded,
    /// or null when the completion stands.
    /// </summary>
    public Task<string?> RecordAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, EfSchemaBackfillVerification verification, CancellationToken cancellationToken)
    {
        var family = run.Family;
        var readable = run.Chain.ReadableVersions;
        var member = gate.LocalMember();
        return CompareAndSetAsync<string?>(scopes, family, refreshGate: true, async (store, record, _) =>
        {
            if (record is null)
                throw new InvalidOperationException($"The finalization record of '{family}' vanished while its backfill ran.");
            if (record.Finish is { } standing && SchemaVersionChain.PositionOf(readable, standing.CompletionVersion) >= run.TargetAt)
                return Attempt<string?>.Done(null);
            if (record.FinishHistory.Skip(verification.HistoryMark).Any(entry => entry.Transition == SchemaFinishTransition.Withdrawn))
            {
                // A row below the target turned up while the pass ran, so it proved nothing; the margin starts again (FR-012).
                settle.Reset(family);
                return Attempt<string?>.Done("The completion was withdrawn while the verification pass ran, so only a new pass can prove it.");
            }

            try
            {
                if (!(await store.RecordCompletionAsync(family, record.Revision, run.Target, verification.StartedAt, verification.EndedAt, readable, member, cancellationToken)).Applied)
                    return Attempt<string?>.Again;
                logger.LogInformation("Schema family {Family} of EF module {Module} is complete at {Version}: no row below it remains (spec 186, FR-014).", family, gate.Module, run.Target);
                return Attempt<string?>.Done(null);
            }
            catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.NotForward)
            {
                // Another worker recorded it, or a later version, since the read.
                return Attempt<string?>.Done(null);
            }
        }, () => $"Each of {RecordAttempts} attempts to record the completion lost its compare-and-set; the next round tries again.", cancellationToken);
    }

    /// <summary>
    /// Withdraws the completion that stands when <paramref name="evidence"/>, read after it, finds rows below it (FR-018),
    /// by compare-and-set with a history entry giving that evidence, leaving the finalized version where it is (FR-019), and
    /// reports it as critical. The evidence is read against the record it withdraws, so a completion recorded since the
    /// caller last looked is withdrawn only when rows below it remain, and never on stale evidence. Every host's Attention
    /// reads the withdrawal from the record; the gate reads the record again, so features that need completeness go dormant
    /// on this host at once; and the settle margin starts again. Returns the finish record that stands afterwards: null
    /// when it was withdrawn or none stood, and the standing one when nothing below it remains.
    /// </summary>
    /// <param name="evidence">Why the standing completion does not hold, read in the given scope, or null when nothing below it remains.</param>
    /// <exception cref="InvalidOperationException">
    /// Every attempt lost its compare-and-set. The caller stops rather than rewrite a straggler under a completion that
    /// still stands, so the next round finds it again and nothing goes unreported.
    /// </exception>
    public Task<SchemaFinishRecord?> WithdrawAsync(
        EfSchemaBackfillScopeRunner scopes,
        string family,
        Func<EfSchemaBackfillScope, SchemaFinishRecord, Task<string?>> evidence,
        CancellationToken cancellationToken)
    {
        var member = gate.LocalMember();
        return CompareAndSetAsync(scopes, family, refreshGate: true, async (store, record, scope) =>
        {
            if (record?.Finish is not { } standing)
                return Attempt<SchemaFinishRecord?>.Done(null);
            if (await evidence(scope, standing) is not { } reason)
                return Attempt<SchemaFinishRecord?>.Done(standing);
            try
            {
                if (!(await store.WithdrawCompletionAsync(family, record.Revision, member, reason, cancellationToken)).Applied)
                    return Attempt<SchemaFinishRecord?>.Again;
            }
            catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.NoCompletion)
            {
                return Attempt<SchemaFinishRecord?>.Done(null);
            }

            settle.Reset(family);
            logger.LogCritical(
                "Schema family {Family} of EF module {Module} is no longer complete at {Version}: {Reason} The completion is withdrawn until a new " +
                "verification pass succeeds, and features that need it are dormant meanwhile (spec 186, FR-018).",
                family, gate.Module, standing.CompletionVersion, reason);
            return Attempt<SchemaFinishRecord?>.Done(null);
        }, () => throw new InvalidOperationException(
            $"Each of {RecordAttempts} attempts to withdraw the completion of schema family '{family}' lost its compare-and-set; the round stops " +
            "rather than rewrite a row below a completion that still stands."), cancellationToken);
    }

    /// <summary>Where the family's finish history stands now: a verification pass's mark (FR-013).</summary>
    public async Task<int> HistoryMarkAsync(EfSchemaBackfillScopeRunner scopes, string family, CancellationToken cancellationToken) =>
        (await ReadAsync(scopes, family, cancellationToken))?.FinishHistory.Count ?? 0;

    /// <summary>
    /// Claims the run in the finish record (FR-008), or returns false when another worker's claim holds. A record with no
    /// finish record, or one whose claim is lost to compare-and-set again and again, runs unclaimed: nothing correct
    /// depends on the claim.
    /// </summary>
    public Task<bool> ClaimAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, CancellationToken cancellationToken)
    {
        if (options.ClaimDuration <= TimeSpan.Zero)
            return Task.FromResult(true);
        var family = run.Family;
        var member = gate.LocalMember();
        return CompareAndSetAsync(scopes, family, refreshGate: false, async (store, record, scope) =>
        {
            if (record is not { Finish: not null })
                return Attempt<bool>.Done(true);
            if (record.Finish.Run is { } own && own.Worker == _worker && own.TargetVersion == run.Target &&
                own.ExpiresAt - time.GetUtcNow() > options.ClaimDuration * 2 / 3)
            {
                run.ClaimRenewedAt = own.ExpiresAt - options.ClaimDuration;
                return Attempt<bool>.Done(true);
            }

            try
            {
                if (!(await store.ClaimBackfillAsync(family, record.Revision, run.Target, run.Chain.ReadableVersions, member, _worker, options.ClaimDuration, cancellationToken)).Applied)
                    return Attempt<bool>.Again;
                run.ClaimRenewedAt = time.GetUtcNow();
                return Attempt<bool>.Done(true);
            }
            catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.BackfillClaimed)
            {
                var holder = record.Finish.Run;
                status.Update(family, current => current with
                {
                    State = EfSchemaBackfillState.ClaimedElsewhere,
                    ClaimedBy = holder,
                    Detail = holder is null ? null : $"The run is claimed by {holder.Member} until {holder.ExpiresAt:u}."
                });
                return Attempt<bool>.Done(false);
            }
            catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal is SchemaFinalizationRefusal.NotForward or SchemaFinalizationRefusal.CompletionBeyondFinalized)
            {
                // The completion moved to the target or past it since this host last observed it: nothing to claim.
                await gate.RefreshAsync(scope.Context, cancellationToken);
                return Attempt<bool>.Done(false);
            }
        }, () => true, cancellationToken);
    }

    /// <summary>Renews the run's claim once a third of its period has passed, and stops the run if another worker has taken it over.</summary>
    public async Task RenewClaimAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, CancellationToken cancellationToken)
    {
        if (run.ClaimRenewedAt is not { } renewedAt || time.GetUtcNow() - renewedAt < options.ClaimDuration / 3)
            return;
        if (!await ClaimAsync(scopes, run, cancellationToken))
            throw new EfSchemaBackfillClaimLostException($"The run of schema family '{run.Family}' was taken over by another worker after this one's claim lapsed.");
    }

    /// <summary>
    /// The one compare-and-set loop over a family's finalization record: <paramref name="attempt"/> is given the record as it
    /// stands, read afresh in one scope, and says whether it is done or lost its compare-and-set and must decide again; after
    /// <see cref="RecordAttempts"/> lost attempts, <paramref name="exhausted"/> answers, or throws. With
    /// <paramref name="refreshGate"/> the gate then reads the record again in the same scope.
    /// </summary>
    private Task<T> CompareAndSetAsync<T>(
        EfSchemaBackfillScopeRunner scopes,
        string family,
        bool refreshGate,
        Func<EfSchemaFinalizationStore, SchemaFinalizationRecord?, EfSchemaBackfillScope, Task<Attempt<T>>> attempt,
        Func<T> exhausted,
        CancellationToken cancellationToken) =>
        scopes.WithScopeAsync(async scope =>
        {
            var store = new EfSchemaFinalizationStore(scope.Context, time);
            var outcome = Attempt<T>.Again;
            for (var tried = 1; tried <= RecordAttempts && !outcome.Finished; tried++)
                outcome = await attempt(store, await store.FindAsync(family, cancellationToken), scope);
            var result = outcome.Finished ? outcome.Value : exhausted();
            if (refreshGate)
                await gate.RefreshAsync(scope.Context, cancellationToken);
            return result;
        }, cancellationToken);

    /// <summary>One attempt's outcome: done with a value, or lost its compare-and-set.</summary>
    private readonly record struct Attempt<T>(bool Finished, T Value)
    {
        public static Attempt<T> Again => default;

        public static Attempt<T> Done(T value) => new(true, value);
    }
}
