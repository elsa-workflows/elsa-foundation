using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// The backfill's writes to a family's finish record (spec 186, FR-008, FR-014 and FR-018): the claim on the family, the
/// completion a verification pass proved, and its withdrawal when a row below it turns up. Each is a compare-and-set on
/// the record, read afresh and decided again when it is lost, through <see cref="CompareAndSetAsync{T}"/>.
/// </summary>
internal sealed class EfSchemaBackfillFinish(
    EfSchemaModuleGate gate,
    IEfSchemaFleet? fleet,
    EfSchemaBackfillStatusBoard status,
    EfSchemaBackfillOptions options,
    TimeProvider time,
    ILogger logger)
{
    private const int RecordAttempts = 3;

    /// <summary>Why a worker whose member has lapsed does nothing for a family.</summary>
    public const string Lapsed = "This member has lapsed from the fleet, so it claims and rewrites nothing, and leaves the family to a member that has not.";

    private readonly string _worker = Guid.NewGuid().ToString("N");
    private readonly object _lock = new();
    private readonly Dictionary<string, (string Target, DateTimeOffset RenewedAt)> _held = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether this worker's member has concluded that it lapsed from the fleet (spec 183, FR-007): others may count it
    /// expired and take its work over, so it takes none. A host that composes no fleet never lapses.
    /// </summary>
    public bool HasLapsed => fleet?.GetLocalStanding().HasLapsed == true;

    /// <summary>The family's finalization record as it stands now, not as the gate last observed it.</summary>
    public Task<SchemaFinalizationRecord?> ReadAsync(EfSchemaBackfillScopeRunner scopes, string family, CancellationToken cancellationToken) =>
        scopes.WithScopeAsync(scope => new EfSchemaFinalizationStore(scope.Context, time).FindAsync(family, cancellationToken), cancellationToken);

    /// <summary>
    /// Records the family complete at the run's target (FR-014), unless a completion at or after it already stands, or a
    /// completion was withdrawn, by this worker or another, after the settle margin the verification pass followed began,
    /// which only a pass after a new margin can answer (FR-012). Nor does a worker record whose member has lapsed, or whose
    /// claim lapsed and another worker took over: the run is that worker's now (FR-008). Then the gate reads the record
    /// again, so this host's dormancy check answers from it at once (FR-017). Returns why nothing was recorded, or null when
    /// the completion stands.
    /// </summary>
    public async Task<string?> RecordAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, EfSchemaBackfillVerification verification, CancellationToken cancellationToken)
    {
        var family = run.Family;
        var readable = run.Chain.ReadableVersions;
        var member = gate.LocalMember();
        var unrecorded = await CompareAndSetAsync<string?>(scopes, family, refreshGate: true, async (store, record, _) =>
        {
            if (record is null)
                throw new InvalidOperationException($"The finalization record of '{family}' vanished while its backfill ran.");
            if (record.Finish is { } standing && SchemaVersionChain.PositionOf(readable, standing.CompletionVersion) >= run.TargetAt)
                return Attempt<string?>.Done(null);
            if (EfSchemaBackfillSettle.WithdrawnSince(record.FinishHistory, verification.HistoryMark))
            {
                // A row below a completion turned up after the margin began, so the pass proved nothing. The withdrawal in the
                // history starts the margin again on every worker's next settle check (FR-012).
                return Attempt<string?>.Done("A completion was withdrawn after the settle margin began, so only a verification pass after a new margin can prove it.");
            }

            if (HasLapsed)
                return Attempt<string?>.Done(Lapsed);
            if (record.BackfillRun is { } held && held.HoldsAt(time.GetUtcNow()) && held.Worker != _worker)
                return Attempt<string?>.Done($"The run is claimed by {held.Member} until {held.ExpiresAt:u}, after this worker's claim lapsed, so this worker records nothing; the claimant verifies again.");

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

        // A recorded completion dropped the claim.
        if (unrecorded is null)
            Forget(family);
        return unrecorded;
    }

    /// <summary>
    /// Withdraws the completion that stands when <paramref name="evidence"/>, read after it, finds rows below it (FR-018),
    /// by compare-and-set with a history entry giving that evidence, leaving the finalized version where it is (FR-019), and
    /// reports it as critical. The evidence is read against the record it withdraws, so a completion recorded since the
    /// caller last looked is withdrawn only when rows below it remain, and never on stale evidence. Every host's Attention
    /// reads the withdrawal from the record; the gate reads the record again, so features that need completeness go dormant
    /// on this host at once; and every worker's settle margin starts again when it next finds the withdrawal in the
    /// history. A claim that still holds moves onto the withdrawal, so its holder goes on with the family. Returns the
    /// finish record that stands afterwards: null when it was withdrawn or none stood, and the standing one when nothing
    /// below it remains.
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

            logger.LogCritical(
                "Schema family {Family} of EF module {Module} is no longer complete at {Version}: {Reason} The completion is withdrawn until a new " +
                "verification pass succeeds, and features that need it are dormant meanwhile (spec 186, FR-018).",
                family, gate.Module, standing.CompletionVersion, reason);
            return Attempt<SchemaFinishRecord?>.Done(null);
        }, () => throw new InvalidOperationException(
            $"Each of {RecordAttempts} attempts to withdraw the completion of schema family '{family}' lost its compare-and-set; the round stops " +
            "rather than rewrite a row below a completion that still stands."), cancellationToken);
    }

    /// <summary>
    /// Claims the family for this worker before any pass reads its rows (FR-008): the survey, the upgrade pass, the settle
    /// condition, the verification passes and, with <paramref name="audit"/>, the audit of the standing completion. The
    /// claim is held on the completion that stands or, while none stands, on the withdrawal that ended it, so it can be
    /// taken after a withdrawal too. A claim this worker renewed less than a third of its period ago is kept without a
    /// write. Another worker's live claim answers <see cref="EfSchemaBackfillClaimOutcome.Elsewhere"/> with its holder,
    /// and this worker reads nothing more of the family. A member that has lapsed claims nothing. Nothing correct depends
    /// on the claim: with no claim duration, or when the claim loses its compare-and-set again and again, the worker runs
    /// unclaimed.
    /// </summary>
    /// <param name="audit">
    /// Whether the claim covers an audit of the standing completion, so a completion at the target still leaves work; a
    /// renewal passes true, since it keeps the claim for whatever the run is doing.
    /// </param>
    public Task<(EfSchemaBackfillClaimOutcome Outcome, SchemaBackfillClaim? Holder)> ClaimAsync(
        EfSchemaBackfillScopeRunner scopes,
        EfSchemaChain chain,
        string target,
        bool audit,
        CancellationToken cancellationToken)
    {
        if (HasLapsed)
            return Task.FromResult<(EfSchemaBackfillClaimOutcome, SchemaBackfillClaim?)>((EfSchemaBackfillClaimOutcome.Lapsed, null));
        if (options.ClaimDuration <= TimeSpan.Zero)
            return Task.FromResult<(EfSchemaBackfillClaimOutcome, SchemaBackfillClaim?)>((EfSchemaBackfillClaimOutcome.Held, null));
        var family = chain.Family;
        var readable = chain.ReadableVersions;
        var targetAt = SchemaVersionChain.PositionOf(readable, target);
        var member = gate.LocalMember();
        return CompareAndSetAsync<(EfSchemaBackfillClaimOutcome, SchemaBackfillClaim?)>(scopes, family, refreshGate: false, async (store, record, scope) =>
        {
            if (record is null)
                return Done(EfSchemaBackfillClaimOutcome.Held);
            if (!audit && record.Finish is { } standing && SchemaVersionChain.PositionOf(readable, standing.CompletionVersion) >= targetAt)
                return await NothingToDoAsync(scope);

            var now = time.GetUtcNow();
            if (record.BackfillRun is { } own && own.Worker == _worker && own.TargetVersion == target && own.ExpiresAt - now > options.ClaimDuration * 2 / 3)
            {
                Hold(family, target, own.ExpiresAt - options.ClaimDuration);
                return Done(EfSchemaBackfillClaimOutcome.Held);
            }

            try
            {
                if (!(await store.ClaimBackfillAsync(family, record.Revision, target, readable, member, _worker, options.ClaimDuration, cancellationToken)).Applied)
                    return Attempt<(EfSchemaBackfillClaimOutcome, SchemaBackfillClaim?)>.Again;
                Hold(family, target, now);
                return Done(EfSchemaBackfillClaimOutcome.Held);
            }
            catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.BackfillClaimed)
            {
                var holder = record.BackfillRun;
                Forget(family);
                status.Update(family, current => current with
                {
                    State = EfSchemaBackfillState.ClaimedElsewhere,
                    ClaimedBy = holder,
                    Detail = holder is null ? null : $"The run is claimed by {holder.Member} until {holder.ExpiresAt:u}."
                });
                return Done(EfSchemaBackfillClaimOutcome.Elsewhere, holder);
            }
            catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal is SchemaFinalizationRefusal.NotForward or SchemaFinalizationRefusal.CompletionBeyondFinalized)
            {
                return await NothingToDoAsync(scope);
            }
            catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.NoCompletion)
            {
                // Neither a completion nor a withdrawal of one stands, which no record the store writes reaches: the run
                // goes unclaimed rather than not at all, since nothing correct depends on the claim.
                return Done(EfSchemaBackfillClaimOutcome.Held);
            }
        }, () => (EfSchemaBackfillClaimOutcome.Held, null), cancellationToken);

        static Attempt<(EfSchemaBackfillClaimOutcome, SchemaBackfillClaim?)> Done(EfSchemaBackfillClaimOutcome outcome, SchemaBackfillClaim? holder = null) =>
            Attempt<(EfSchemaBackfillClaimOutcome, SchemaBackfillClaim?)>.Done((outcome, holder));

        // The completion moved to the target or past it since this host last observed it: nothing to claim.
        async Task<Attempt<(EfSchemaBackfillClaimOutcome, SchemaBackfillClaim?)>> NothingToDoAsync(EfSchemaBackfillScope scope)
        {
            await gate.RefreshAsync(scope.Context, cancellationToken);
            return Done(EfSchemaBackfillClaimOutcome.NothingToDo);
        }
    }

    /// <summary>
    /// Between two batches of a pass and before each phase of a run (FR-008): stops the run, releasing its claim, when this
    /// member has lapsed from the fleet; renews the claim once a third of its period has passed; and stops the run when
    /// another worker has taken it over since this one's claim lapsed, so this worker finishes nothing it no longer owns.
    /// </summary>
    /// <exception cref="EfSchemaBackfillClaimLostException">The run stops, for one of those reasons.</exception>
    public async Task RenewClaimAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, CancellationToken cancellationToken)
    {
        var family = run.Family;
        EfSchemaBackfillClaimOutcome outcome;
        if (HasLapsed)
            outcome = EfSchemaBackfillClaimOutcome.Lapsed;
        else if (HeldOf(family) is { } held && time.GetUtcNow() - held.RenewedAt >= options.ClaimDuration / 3)
            outcome = (await ClaimAsync(scopes, run.Chain, held.Target, audit: true, cancellationToken)).Outcome;
        else
            return;

        switch (outcome)
        {
            case EfSchemaBackfillClaimOutcome.Held:
                return;
            case EfSchemaBackfillClaimOutcome.Lapsed:
                await ReleaseAsync(scopes, family, cancellationToken);
                throw new EfSchemaBackfillClaimLostException(Lapsed);
            case EfSchemaBackfillClaimOutcome.NothingToDo:
                Forget(family);
                throw new EfSchemaBackfillClaimLostException($"The completion of schema family '{family}' moved past this worker's target while its run went on, so the run stops.");
            default:
                throw new EfSchemaBackfillClaimLostException($"The run of schema family '{family}' was taken over by another worker after this one's claim lapsed.");
        }
    }

    /// <summary>
    /// Releases this worker's claim on the family, held or lapsed, so another member's worker may take the family over at
    /// once rather than when the claim expires: what a worker does once its member has lapsed from the fleet (spec 183,
    /// FR-007). A claim another worker has taken since is left alone. A release that loses its compare-and-set on every
    /// attempt fails the round, and the next round tries again.
    /// </summary>
    public async Task ReleaseAsync(EfSchemaBackfillScopeRunner scopes, string family, CancellationToken cancellationToken)
    {
        if (HeldOf(family) is null)
            return;
        await CompareAndSetAsync(scopes, family, refreshGate: true, async (store, record, _) =>
        {
            if (record?.BackfillRun is not { } held || held.Worker != _worker)
                return Attempt<bool>.Done(false);
            if (!(await store.ReleaseBackfillAsync(family, record.Revision, _worker, cancellationToken)).Applied)
                return Attempt<bool>.Again;
            logger.LogInformation(
                "The backfill of schema family {Family} of EF module {Module} released its claim: its member {Member} has lapsed from the fleet.",
                family, gate.Module, held.Member);
            return Attempt<bool>.Done(true);
        }, () => throw new InvalidOperationException(
            $"Each of {RecordAttempts} attempts to release the backfill claim on schema family '{family}' lost its compare-and-set; the next round tries again."), cancellationToken);
        Forget(family);
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

    /// <summary>The claim this worker holds on <paramref name="family"/>, as far as it knows: its target, and when it last took or renewed it.</summary>
    private (string Target, DateTimeOffset RenewedAt)? HeldOf(string family)
    {
        lock (_lock)
            return _held.TryGetValue(family, out var held) ? held : null;
    }

    private void Hold(string family, string target, DateTimeOffset renewedAt)
    {
        lock (_lock)
            _held[family] = (target, renewedAt);
    }

    private void Forget(string family)
    {
        lock (_lock)
            _held.Remove(family);
    }

    /// <summary>One attempt's outcome: done with a value, or lost its compare-and-set.</summary>
    private readonly record struct Attempt<T>(bool Finished, T Value)
    {
        public static Attempt<T> Again => default;

        public static Attempt<T> Done(T value) => new(true, value);
    }
}

/// <summary>What a worker's attempt to claim a family found (spec 186, FR-008).</summary>
internal enum EfSchemaBackfillClaimOutcome
{
    /// <summary>This worker holds the claim, or goes on unclaimed, since nothing correct depends on it.</summary>
    Held,

    /// <summary>Another worker's claim holds: this one reads nothing of the family until it expires.</summary>
    Elsewhere,

    /// <summary>The completion stands at the target or past it, so there is nothing to claim the family for.</summary>
    NothingToDo,

    /// <summary>This worker's member has lapsed from the fleet, so it claims nothing.</summary>
    Lapsed
}
