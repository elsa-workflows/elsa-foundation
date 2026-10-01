using System.Runtime.ExceptionServices;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// The post-finalization backfill of one EF module in one shell, and so in one database (spec 186): once a family's newer
/// version is finalized and this host has adopted it, it upgrades every row below it through the family's rewriter, waits
/// until no counted member can still be writing below it, proves that none remains, and records the family complete in
/// its finish record. While a completion stands it audits the family, and withdraws the completion when a row below it
/// turns up.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant.</b> While a finish record names completion version C, no row of the family below C exists that the
/// runtime has not reported. It holds through these mechanisms, each tested on its own:
/// </para>
/// <list type="number">
/// <item><b>Nothing runs before finalization</b> (FR-002). The target of a run is this host's write version, which the
/// finalization gate only ever takes from a finalized version this host has adopted, and every row it writes is held to
/// the write version by the gate's write check.</item>
/// <item><b>The backfill is an ordinary writer</b> (FR-004). It asks the family's rewriter to read each row through the
/// family's read path and write it back through its write path, a compare-and-set on the row's revision; a lost one is
/// asked again, and a row is written whole or not at all.</item>
/// <item><b>Content-addressed rows are never rewritten</b> (FR-010a), and a family holding one below the target is never
/// recorded complete at it (FR-011a).</item>
/// <item><b>The settle condition comes before verification</b> (FR-012): every counted member reports observing the
/// target, and the settle margin has passed since this worker first saw that hold with no withdrawal in the finish
/// history since, whichever worker wrote it.</item>
/// <item><b>Completion is recorded only from a verification pass that found nothing</b> (FR-013, FR-014): a complete
/// pass over every table of the family, after the settle condition, that found no row below the target, no row it could
/// not read and no content-addressed row below it, with no completion withdrawn since its settle margin began, written
/// by compare-and-set.</item>
/// <item><b>A straggler is reported before it is rewritten</b> (FR-018). While a completion stands the family is audited
/// on its interval, whatever this host's target; and the audit, the upgrade pass and the verification pass each withdraw
/// the completion, by compare-and-set against evidence read after the record they withdraw, before they rewrite the first
/// row they find below it. A pass reads what stands afresh before each row, so a completion another worker records while
/// it runs is withdrawn too. A worker that dies midway leaves no known straggler under a standing completion.</item>
/// </list>
/// <para>
/// It is not an <see cref="IEfPostMigrationAction"/>: an action audits at Prepare and refuses the module while it is
/// required, but the backfill may run only after finalization, which needs the module active (spec 186, "Why this is not
/// a post-migration action").
/// </para>
/// <para>
/// Several workers may run it for one family and database at once, on several hosts or in several shells of one host.
/// Each row write, the completion and its withdrawal are compare-and-sets, so they never both write one row version or
/// the completion. To spare the others its reads, a worker claims the family in its finish record before any pass reads
/// a row: the survey, the upgrade pass, the settle condition, the verification passes and the audit. The claim names this
/// host's write target and is held on the completion that stands or, while none stands, on the withdrawal that ended it,
/// which keeps a claim that still holds, so after a withdrawal one worker goes on and the others wait. A worker that finds
/// the family claimed elsewhere reads nothing of it until the claim expires, and defers its audit by an interval. The
/// claimant keeps the claim only while its run goes on next round, settling or verifying again, and releases it on every
/// other way out. Nothing correct depends on the claim (FR-008): a crashed worker's claim expires, and a claim lost to
/// compare-and-set again and again leaves the run unclaimed. The claim only ever narrows who works: a worker renews it, or
/// stops, between two batches, between two tables a selection counts and before each phase; it rewrites a row only after
/// a read that shows no other worker's live claim; it withdraws and records a completion only while no other worker's
/// live claim stands; and a worker whose member has lapsed from the fleet claims nothing, stops, and releases what it
/// holds.
/// </para>
/// <para>
/// Its parts: <see cref="EfSchemaBackfillPasses"/> selects and rewrites rows, <see cref="EfSchemaBackfillFinish"/> writes
/// the finish record, <see cref="EfSchemaBackfillSettle"/> answers the settle condition, and
/// <see cref="EfSchemaBackfillStatusBoard"/> holds what the gate's status carries. This class decides, per family and
/// round, which of them runs.
/// </para>
/// </remarks>
public sealed class EfSchemaBackfill
{
    private readonly EfSchemaModuleGate _gate;
    private readonly EfSchemaBackfillOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly EfSchemaBackfillStatusBoard _status;
    private readonly EfSchemaBackfillSettle _settle;
    private readonly EfSchemaBackfillFinish _finish;
    private readonly EfSchemaBackfillPasses _passes;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _nextAudit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Target, DateTimeOffset At)> _nextSurvey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<EfSchemaStampedTable>> _tables = new(StringComparer.Ordinal);

    /// <summary>
    /// Builds the backfill of <paramref name="gate"/>'s module, whose status the gate then carries (FR-021): one backfill
    /// per gate.
    /// </summary>
    /// <param name="gate">The module's finalization gate, whose write versions and observed records the backfill reads.</param>
    /// <param name="fleet">This host's view of the fleet, or null when the host composes none: then no completion is ever recorded.</param>
    public EfSchemaBackfill(
        EfSchemaModuleGate gate,
        IEfSchemaFleet? fleet,
        EfSchemaBackfillOptions options,
        TimeProvider? time = null,
        ILogger? logger = null)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _status = new EfSchemaBackfillStatusBoard(gate.Module, _logger);
        _finish = new EfSchemaBackfillFinish(gate, fleet, _status, _options, _time, _logger);
        _settle = new EfSchemaBackfillSettle(fleet, _finish, _options, _time, _logger, _status);
        _passes = new EfSchemaBackfillPasses(_finish, _options, _time, _logger, _status);
        gate.ReportBackfillFrom(_status);
    }

    /// <summary>The EF module whose families this backfill upgrades.</summary>
    public string Module => _gate.Module;

    /// <summary>What this worker last saw of <paramref name="family"/>'s backfill (FR-021).</summary>
    public EfSchemaBackfillStatus StatusOf(string family) => _status.StatusOf(family);

    /// <summary>
    /// Runs a round every <see cref="EfSchemaBackfillOptions.CheckInterval"/> until <paramref name="stopping"/> fires,
    /// starting one interval after it is called, so the shell is running by then (spec 186, User Story 7). A failed round
    /// is logged and the next one follows on schedule; whatever it had not written is found again.
    /// </summary>
    /// <param name="withScope">Runs its argument in a fresh service scope of the shell, with a fresh context of the module.</param>
    public async Task RunAsync(EfSchemaBackfillScopeRunner withScope, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(withScope);
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CheckInterval, _time, stopping);
                await RunOnceAsync(withScope, stopping);
            }
            catch (Exception) when (stopping.IsCancellationRequested)
            {
                // Stopping, as the shell or host is: the round is abandoned, not failed, and its rows are found again.
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Whatever failed, a cancellation that is not this loop's own included, must not end the loop: a backfill
                // that stopped for good would leave its families incomplete with nothing saying so.
                _logger.LogWarning(exception, "The post-finalization backfill of EF module {Module} failed a round; it tries again on schedule.", Module);
            }
        }
    }

    /// <summary>
    /// One round over every family of the module: a standing completion is audited when its audit is due, and a family with
    /// work is backfilled, unless it was blocked at the same target less than its re-survey interval ago, or another
    /// worker's claim on it still holds. A member that has lapsed from the fleet does neither. A family whose run fails is
    /// logged and the round goes on to the module's other families; the round then fails with that failure, or with every
    /// one of them, so its caller learns of it. Only the round's own cancellation stops it at once. Rounds of one worker
    /// never overlap.
    /// </summary>
    public async Task RunOnceAsync(EfSchemaBackfillScopeRunner withScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(withScope);
        await _running.WaitAsync(cancellationToken);
        var failures = new List<ExceptionDispatchInfo>();
        try
        {
            foreach (var chain in _gate.Families.Chains)
            {
                try
                {
                    await RunFamilyAsync(withScope, chain, cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(exception,
                        "The post-finalization backfill of schema family {Family} of EF module {Module} failed this round; the module's other " +
                        "families go on, and it tries again next round.", chain.Family, Module);
                    failures.Add(ExceptionDispatchInfo.Capture(exception));
                }
            }
        }
        finally
        {
            _running.Release();
        }

        if (failures.Count == 1)
            failures[0].Throw();
        if (failures.Count > 1)
            throw new AggregateException($"The post-finalization backfill of EF module {Module} failed for {failures.Count} families this round.",
                failures.Select(failure => failure.SourceException));
    }

    private async Task RunFamilyAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaChain chain, CancellationToken cancellationToken)
    {
        var family = chain.Family;
        if (_finish.HasLapsed)
        {
            // Before anything the gate observed decides otherwise: others may count this member expired and take its work
            // over (spec 183, FR-007), so it takes none, and lets go of what it holds rather than keep the family from them.
            await LetGoAsLapsedAsync(scopes, family, EfSchemaBackfillFinish.Lapsed);
            return;
        }

        // Every way out before the claim leaves the family until a later round, so a claim this worker kept from the last
        // round, while its run settled or verified, is let go of here too, by the same bounded release; it holds none in a
        // round that kept nothing, and then this costs nothing.
        if (Plan(chain) is not { } round)
        {
            await _finish.ReleaseAsync(scopes, family);
            return;
        }

        // The claim is kept only while the family's run goes on next round: settling, or verifying again. Every other way
        // out releases it, so it never holds the other workers off until it expires: an audit that found nothing, a run that
        // recorded completion, was blocked or had nothing left to do, a run that stopped, and a round that failed or was
        // cancelled, which releases on a token of its own.
        bool goesOn;
        try
        {
            goesOn = await ClaimAndRunAsync(scopes, chain, round.Target, round.Identity, round.AuditDue, round.RunDue, round.Sole, round.Now, cancellationToken);
        }
        catch (EfSchemaBackfillStoppedException stopped) when (stopped.Reason == EfSchemaBackfillStop.Lapsed)
        {
            await LetGoAsLapsedAsync(scopes, family, stopped.Message);
            return;
        }
        catch (EfSchemaBackfillStoppedException stopped)
        {
            _status.Update(family, status => status with
            {
                State = stopped.Reason == EfSchemaBackfillStop.TakenOver ? EfSchemaBackfillState.ClaimedElsewhere : EfSchemaBackfillState.Idle,
                Detail = stopped.Message
            });
            goesOn = false;
        }
        catch (Exception)
        {
            await ReleaseAfterFailureAsync(scopes, family);
            throw;
        }

        if (!goesOn)
            await _finish.ReleaseAsync(scopes, family);
    }

    /// <summary>
    /// What this round does with <paramref name="chain"/>'s family, decided from what the gate last observed with no I/O,
    /// or null when it leaves the family this round, saying why on its status where there is a reason to give: the gate
    /// has not admitted the module, the host refuses every write to the family, the completion is one this host cannot
    /// place or is ahead of what it writes, or neither an audit nor a run is due.
    /// </summary>
    private FamilyRound? Plan(EfSchemaChain chain)
    {
        var family = chain.Family;
        var state = _gate.StateOf(family);
        if (state is null || _gate.ObservedRecordOf(family)?.Record is not { } observed || _gate.DatabaseIdentity is not { } identity)
            return null;
        if (state.WritesRefused)
        {
            _status.Update(family, status => status with { State = EfSchemaBackfillState.Idle, Detail = "This host refuses every write to the family, so it rewrites nothing." });
            return null;
        }

        var readable = chain.ReadableVersions;
        var target = state.WriteVersion;
        var targetAt = SchemaVersionChain.PositionOf(readable, target);
        var completion = observed.Finish?.CompletionVersion;
        var completionAt = completion is null ? -1 : SchemaVersionChain.PositionOf(readable, completion);
        if (completion is not null && completionAt < 0)
        {
            // Activation refuses this (FR-020); a completion recorded since by a newer host lies after this host's chain.
            _status.Update(family, status => status with
            {
                State = EfSchemaBackfillState.Idle,
                Detail = $"The finish record names '{completion}', which this host cannot place along [{string.Join(", ", readable)}]."
            });
            return null;
        }

        if (completionAt > targetAt)
        {
            // This host has not adopted the finalized version the completion names, so a row it rewrote would still be
            // below it: it leaves the family to hosts that write the completion version.
            _status.Update(family, status => status with
            {
                State = EfSchemaBackfillState.Idle,
                Detail = $"This host writes '{target}', behind the completion version '{completion}', so it leaves the family to hosts that write it."
            });
            return null;
        }

        // FR-018: a standing completion is audited on its interval whatever this host's target, so a family whose run
        // towards a newer version is blocked still has its stragglers found.
        var now = _time.GetUtcNow();
        var auditDue = completion is not null && now >= NextAuditOf(family);
        var runDue = completionAt < targetAt && !SurveyDeferred(family, target);
        if (!auditDue && !runDue)
        {
            if (completionAt == targetAt)
                _status.Update(family, status => status with { State = EfSchemaBackfillState.Complete, TargetVersion = null, Blockers = [], SettleWaitingFor = [], ClaimedBy = null, Detail = null });
            return null;
        }

        return new FamilyRound(target, identity, auditDue, runDue, Sole: completionAt == targetAt, now);
    }

    /// <summary>What one round does with a family: the target, the database, whether an audit and a run are due, and when it decided.</summary>
    private readonly record struct FamilyRound(string Target, string Identity, bool AuditDue, bool RunDue, bool Sole, DateTimeOffset Now);

    /// <summary>
    /// FR-008: the claim comes before any read of the family's rows, so a worker that finds it held elsewhere surveys,
    /// settles, verifies and audits nothing. It waits out the claim, and leaves the audit due now to the holder, which
    /// audits on its own interval while it holds the family. The claim names this host's write target, whether it covers
    /// an audit, a run, or both. Returns whether the family's run goes on next round, and so keeps its claim.
    /// </summary>
    private async Task<bool> ClaimAndRunAsync(
        EfSchemaBackfillScopeRunner scopes,
        EfSchemaChain chain,
        string target,
        string identity,
        bool auditDue,
        bool runDue,
        bool sole,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var family = chain.Family;
        var claim = await _finish.ClaimAsync(scopes, chain, target, evenIfComplete: auditDue, cancellationToken);
        switch (claim.Outcome)
        {
            case EfSchemaBackfillClaimOutcome.Elsewhere:
                if (auditDue)
                    ScheduleAudit(family, now);
                if (runDue)
                    DeferSurvey(family, target, claim.Holder is { } holder ? holder.ExpiresAt - now : _options.CheckInterval);
                return false;
            case EfSchemaBackfillClaimOutcome.Lapsed:
                throw new EfSchemaBackfillStoppedException(EfSchemaBackfillStop.Lapsed, EfSchemaBackfillFinish.Lapsed);
            case EfSchemaBackfillClaimOutcome.NothingToDo or EfSchemaBackfillClaimOutcome.Unclaimable:
                return false;
        }

        var goesOn = auditDue && await AuditAsync(scopes, chain, sole, cancellationToken);
        if (runDue)
            goesOn = await BackfillAsync(scopes, chain, target, identity, cancellationToken);
        return goesOn;
    }

    /// <summary>
    /// What a worker whose member has lapsed from the fleet does with a family, however it found the lapse (spec 183,
    /// FR-007): it lets go of its claim at once, so another member takes the family over rather than wait for the claim to
    /// expire, and says so.
    /// </summary>
    private async Task LetGoAsLapsedAsync(EfSchemaBackfillScopeRunner scopes, string family, string detail)
    {
        if (await _finish.ReleaseAsync(scopes, family))
            _logger.LogInformation("The backfill of schema family {Family} of EF module {Module} released its claim: its member has lapsed from the fleet.", family, Module);
        _status.Update(family, status => status with { State = EfSchemaBackfillState.Idle, ClaimedBy = null, Detail = detail });
    }

    /// <summary>
    /// A round that failed, or was cancelled, lets go of the family's claim before the failure goes on, so a worker whose
    /// rounds keep failing never holds the others off. A release that fails too is logged, not thrown, so the round's own
    /// failure is the one reported; the claim then expires on its own.
    /// </summary>
    private async Task ReleaseAfterFailureAsync(EfSchemaBackfillScopeRunner scopes, string family)
    {
        try
        {
            await _finish.ReleaseAsync(scopes, family);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _logger.LogWarning(exception,
                "The backfill of schema family {Family} of EF module {Module} could not release its claim after its round failed; the claim expires on its own.",
                family, Module);
        }
    }

    /// <summary>
    /// One run for <paramref name="chain"/>'s family to <paramref name="target"/> (FR-005 to FR-014), under this worker's
    /// claim: a survey of what blocks completion, the upgrade pass, the settle condition, and verification passes until one
    /// finds nothing, which records completion. The claim is renewed between the tables a selection counts, between
    /// batches, before the settle condition and before each verification pass, so a worker that no longer owns the run
    /// stops there. Returns whether the run goes on next round, settling or verifying again, and so keeps its claim.
    /// </summary>
    private async Task<bool> BackfillAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaChain chain, string target, string identity, CancellationToken cancellationToken)
    {
        var family = chain.Family;
        // The record as it stands now, not as the gate last observed it: a completion another worker recorded since is the
        // one a row below it straggles behind (FR-018), and one at the target already leaves nothing to do.
        var completion = (await _finish.ReadAsync(scopes, family, cancellationToken))?.Finish;
        if (completion is not null && SchemaVersionChain.PositionOf(chain.ReadableVersions, completion.CompletionVersion) >= SchemaVersionChain.PositionOf(chain.ReadableVersions, target))
        {
            await scopes.WithScopeAsync(scope => _gate.RefreshAsync(scope.Context, cancellationToken), cancellationToken);
            return false;
        }

        var run = new EfSchemaBackfillRun(chain, target, completion, _gate.Families.DeclarationOf(family), await TablesAsync(scopes, chain, cancellationToken));
        var startedAt = _time.GetUtcNow();
        _status.Update(family, status => status with
        {
            State = EfSchemaBackfillState.Upgrading,
            TargetVersion = target,
            RowsRewritten = status.TargetVersion == target ? status.RowsRewritten : 0,
            RunStartedAt = status.TargetVersion == target ? status.RunStartedAt ?? startedAt : startedAt,
            ClaimedBy = null,
            Detail = null
        });

        var (blockers, toUpgrade) = await _passes.SurveyAsync(scopes, run, cancellationToken);
        if (toUpgrade > 0)
        {
            if (run.Declaration.Rewriter is null)
                blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.NoRewriter, null, toUpgrade,
                    $"{toUpgrade} row(s) of schema family '{family}' are below '{target}', and the family names no rewriter to upgrade them " +
                    "(spec 186, FR-004): declare one with [EfSchemaFamily(..., Rewriter = typeof(...))]."));
            else
                await _passes.UpgradeAsync(scopes, run, blockers, cancellationToken);
        }

        if (blockers.Count > 0)
        {
            Blocked(family, target, blockers);
            return false;
        }

        await _finish.RenewClaimAsync(scopes, run, cancellationToken);
        var (settled, historyMark) = await _settle.CheckAsync(scopes, chain, target, identity, cancellationToken);
        switch (settled)
        {
            case EfSchemaBackfillSettle.Outcome.Blocked:
                DeferSurvey(family, target, _options.AuditInterval);
                return false;
            case EfSchemaBackfillSettle.Outcome.Waiting:
                return true;
        }

        for (var pass = 1; pass <= _options.VerificationPasses; pass++)
        {
            await _finish.RenewClaimAsync(scopes, run, cancellationToken);
            var verification = await _passes.VerifyAsync(scopes, run, historyMark, cancellationToken);
            if (verification.Blockers.Count > 0)
            {
                Blocked(family, target, verification.Blockers);
                return false;
            }

            if (verification.Found == 0)
                return await RecordAsync(scopes, run, verification, cancellationToken);

            _logger.LogInformation(
                "The verification pass of schema family {Family} found {Found} row(s) below {Target} and rewrote them; it starts again (spec 186, FR-013).",
                family, verification.Found, target);
        }

        _status.Update(family, status => status with
        {
            State = EfSchemaBackfillState.Verifying,
            Detail = $"Each of {_options.VerificationPasses} verification passes found rows below '{target}' that the one before it had not; the next round tries again."
        });
        return true;
    }

    /// <summary>Records the completion a verification pass proved; returns whether the run goes on next round, having recorded nothing.</summary>
    private async Task<bool> RecordAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, EfSchemaBackfillVerification verification, CancellationToken cancellationToken)
    {
        if (await _finish.RecordAsync(scopes, run, verification, cancellationToken) is { } unrecorded)
        {
            _status.Update(run.Family, status => status with { State = EfSchemaBackfillState.Verifying, Detail = unrecorded });
            return true;
        }

        ScheduleAudit(run.Family, _time.GetUtcNow());
        _status.Update(run.Family, status => status with { State = EfSchemaBackfillState.Complete, Blockers = [], SettleWaitingFor = [], ClaimedBy = null, Detail = null });
        return false;
    }

    private void ScheduleAudit(string family, DateTimeOffset from)
    {
        lock (_lock)
            _nextAudit[family] = from + _options.AuditInterval;
    }

    /// <summary>
    /// The audit (FR-018): a selection by stamp of every table of the family for rows below the completion that stands. Any
    /// such row, rewritable or not, withdraws the completion first; then the rewritable ones are rewritten. With
    /// <paramref name="sole"/> the completion is at this host's target, so the audit is all the family has to do and the
    /// status says the family is complete; otherwise the run that follows says what it is doing. An audit that fails is
    /// due again the next round. Returns whether the family's run goes on next round: with <paramref name="sole"/>, when the
    /// audit withdrew the completion, so the run towards this host's target starts again.
    /// </summary>
    private async Task<bool> AuditAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaChain chain, bool sole, CancellationToken cancellationToken)
    {
        var family = chain.Family;
        var auditedAt = _time.GetUtcNow();
        var examined = (await _finish.ReadAsync(scopes, family, cancellationToken))?.Finish;
        if (examined is null || SchemaVersionChain.PositionOf(chain.ReadableVersions, examined.CompletionVersion) < 0)
        {
            ScheduleAudit(family, auditedAt);
            return false;
        }

        var run = new EfSchemaBackfillRun(chain, examined.CompletionVersion, examined, _gate.Families.DeclarationOf(family), await TablesAsync(scopes, chain, cancellationToken));
        string? found = null;
        run.Completion = await _finish.WithdrawAsync(scopes, run, async (scope, standing) =>
            found = await _passes.StragglersAsync(scopes, scope.Context, run, standing, audit: true, cancellationToken), cancellationToken);
        var unrewritable = new List<EfSchemaBackfillBlocker>();
        if (found is not null)
        {
            foreach (var table in run.Tables.Where(run.Rewrites))
                await _passes.RewriteAsync(scopes, run, table, unrewritable, cancellationToken);
        }

        ScheduleAudit(family, auditedAt);
        var detail = found is null ? null : found + (unrewritable.Count == 0 ? "" : " Not all could be rewritten: " + string.Join(" ", unrewritable.Select(blocker => blocker.Detail)));
        _status.Update(family, status => sole
            ? status with { State = EfSchemaBackfillState.Complete, LastAuditAt = auditedAt, TargetVersion = null, Blockers = [], SettleWaitingFor = [], ClaimedBy = null, Detail = detail }
            : status with { LastAuditAt = auditedAt, Detail = detail ?? status.Detail });
        return sole && found is not null;
    }

    /// <summary>
    /// Records what blocks the family at <paramref name="target"/>, and leaves it until it is due to be surveyed again unless
    /// the target moves (FR-023): a blocker persists until someone resolves it, and surveying the family every round would
    /// cost a selection by stamp of every table every check interval. Rows an operator repairs in place, with a stamp this
    /// host cannot read or that fail to upcast, are surveyed again at <see cref="EfSchemaBackfillOptions.RepairableBlockerInterval"/>,
    /// so a repair is seen soon; any other blocker, content-addressed rows among them, at the audit interval.
    /// </summary>
    private void Blocked(string family, string target, IReadOnlyList<EfSchemaBackfillBlocker> blockers)
    {
        _status.Blocked(family, blockers);
        var repairable = blockers.Any(blocker => blocker.Kind is EfSchemaBackfillBlockerKind.Skew or EfSchemaBackfillBlockerKind.Corruption);
        DeferSurvey(family, target, repairable ? _options.RepairableBlockerInterval : _options.AuditInterval);
    }

    private void DeferSurvey(string family, string target, TimeSpan interval)
    {
        lock (_lock)
            _nextSurvey[family] = (target, _time.GetUtcNow() + interval);
    }

    private bool SurveyDeferred(string family, string target)
    {
        lock (_lock)
            return _nextSurvey.TryGetValue(family, out var next) && next.Target == target && _time.GetUtcNow() < next.At;
    }

    private DateTimeOffset NextAuditOf(string family)
    {
        lock (_lock)
            return _nextAudit.GetValueOrDefault(family, DateTimeOffset.MinValue);
    }

    private async Task<IReadOnlyList<EfSchemaStampedTable>> TablesAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaChain chain, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_tables.TryGetValue(chain.Family, out var known))
                return known;
        }

        var tables = await scopes.WithScopeAsync(scope =>
            Task.FromResult(EfSchemaStampedTable.Of(scope.Context.Model, _gate.Families, _gate.Families.DeclarationOf(chain.Family))), cancellationToken);
        lock (_lock)
            _tables[chain.Family] = tables;
        return tables;
    }
}
