using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Primitives.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>What one unit of the backfill's work runs with: a fresh service scope of the shell, and a fresh context of the module.</summary>
public sealed record EfSchemaBackfillScope(IServiceProvider Services, DbContext Context);

/// <summary>
/// The post-finalization backfill of one EF module in one shell, and so in one database (spec 186): once a family's newer
/// version is finalized and this host has adopted it, it upgrades every row below it through the family's rewriter, waits
/// until no counted member can still be writing below it, proves that none remains, and records the family complete in
/// its finish record. Afterwards it audits the family, and withdraws the completion when a row below it turns up.
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
/// target, and the settle margin has passed since this worker first saw that hold.</item>
/// <item><b>Completion is recorded only from a verification pass that found nothing</b> (FR-013, FR-014): a complete
/// pass over every table of the family, after the settle condition, that found no row below the target, no row it could
/// not read and no content-addressed row below it, written by compare-and-set.</item>
/// <item><b>The audit keeps looking</b> (FR-018). Any pass that finds a row below the standing completion rewrites it,
/// reports it, and withdraws the completion by compare-and-set until a new verification succeeds.</item>
/// </list>
/// <para>
/// It is not an <see cref="IEfPostMigrationAction"/>: an action audits at Prepare and refuses the module while it is
/// required, but the backfill may run only after finalization, which needs the module active (spec 186, "Why this is not
/// a post-migration action").
/// </para>
/// <para>
/// Several workers may run it for one family and database at once, on several hosts or in several shells of one host.
/// Each row write and the completion are compare-and-sets, so they never both write one row version or the completion.
/// A worker claims the run in the finish record to spare the others its reads, and nothing correct depends on the claim
/// (FR-008): a crashed worker's claim expires, and a run with no finish record to claim in runs unclaimed.
/// </para>
/// </remarks>
public sealed class EfSchemaBackfill
{
    private const int RewriteAttempts = 3;
    private const int RecordAttempts = 3;
    private const int ReportedRowsPerTable = 5;

    private readonly EfSchemaModuleGate _gate;
    private readonly IEfSchemaFleet? _fleet;
    private readonly EfSchemaBackfillOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly string _worker = Guid.NewGuid().ToString("N");
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly object _lock = new();
    private readonly Dictionary<string, EfSchemaBackfillStatus> _statuses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Target, DateTimeOffset Since)> _settled = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _nextAudit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<EfSchemaStampedTable>> _tables = new(StringComparer.Ordinal);

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
        _fleet = fleet;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The EF module whose families this backfill upgrades.</summary>
    public string Module => _gate.Module;

    /// <summary>What this worker last saw of <paramref name="family"/>'s backfill (FR-021).</summary>
    public EfSchemaBackfillStatus StatusOf(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        lock (_lock)
            return _statuses.GetValueOrDefault(family) ?? EfSchemaBackfillStatus.Idle(family);
    }

    /// <summary>
    /// Runs a round every <see cref="EfSchemaBackfillOptions.CheckInterval"/> until <paramref name="stopping"/> fires,
    /// starting one interval after it is called, so the shell is running by then (spec 186, User Story 7). A failed round
    /// is logged and the next one follows on schedule; whatever it had not written is found again.
    /// </summary>
    /// <param name="withScope">Runs its argument in a fresh service scope of the shell, with a fresh context of the module.</param>
    public async Task RunAsync(Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope, CancellationToken stopping)
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
    /// One round over every family of the module: a family with work is backfilled, one whose completion stands is
    /// audited when its audit is due. Rounds of one worker never overlap.
    /// </summary>
    public async Task RunOnceAsync(Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(withScope);
        await _running.WaitAsync(cancellationToken);
        try
        {
            foreach (var chain in _gate.Families.Chains)
                await RunFamilyAsync(withScope, chain, cancellationToken);
        }
        finally
        {
            _running.Release();
        }
    }

    private async Task RunFamilyAsync(Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope, EfSchemaChain chain, CancellationToken cancellationToken)
    {
        var family = chain.Family;
        var state = _gate.StateOf(family);
        if (state is null || _gate.ObservedRecordOf(family)?.Record is not { } observed || _gate.DatabaseIdentity is not { } identity)
            return;
        if (state.WritesRefused)
        {
            Update(family, status => status with { State = EfSchemaBackfillState.Idle, Detail = "This host refuses every write to the family, so it rewrites nothing." });
            return;
        }

        var readable = chain.ReadableVersions;
        var target = state.WriteVersion;
        var targetAt = SchemaVersionChain.PositionOf(readable, target);
        var completion = observed.Finish?.CompletionVersion;
        var completionAt = completion is null ? -1 : SchemaVersionChain.PositionOf(readable, completion);
        if (completion is not null && completionAt < 0)
        {
            // Activation refuses this (FR-020); a completion recorded since by a newer host lies after this host's chain.
            Update(family, status => status with
            {
                State = EfSchemaBackfillState.Idle,
                Detail = $"The finish record names '{completion}', which this host cannot place along [{string.Join(", ", readable)}]."
            });
            return;
        }

        if (completionAt > targetAt)
        {
            // This host has not adopted the finalized version the completion names, so a row it rewrote would still be
            // below it: it leaves the family to hosts that write the completion version.
            Update(family, status => status with
            {
                State = EfSchemaBackfillState.Idle,
                Detail = $"This host writes '{target}', behind the completion version '{completion}', so it leaves the family to hosts that write it."
            });
            return;
        }

        if (completionAt == targetAt)
        {
            if (_time.GetUtcNow() >= NextAuditOf(family))
                await AuditAsync(withScope, chain, completion!, cancellationToken);
            else
                Update(family, status => status with { State = EfSchemaBackfillState.Complete, TargetVersion = null, Blockers = [], SettleWaitingFor = [], ClaimedBy = null, Detail = null });
            return;
        }

        try
        {
            await BackfillAsync(withScope, chain, target, identity, cancellationToken);
        }
        catch (ClaimLostException lost)
        {
            Update(family, status => status with { State = EfSchemaBackfillState.ClaimedElsewhere, Detail = lost.Message });
        }
    }

    /// <summary>
    /// One run for <paramref name="chain"/>'s family to <paramref name="target"/> (FR-005 to FR-014): a survey of what
    /// blocks completion, the upgrade pass under a claim, the settle condition, and verification passes until one finds
    /// nothing, which records completion.
    /// </summary>
    private async Task BackfillAsync(
        Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope,
        EfSchemaChain chain,
        string target,
        string identity,
        CancellationToken cancellationToken)
    {
        var family = chain.Family;
        // The record as it stands now, not as the gate last observed it: a completion another worker recorded since is the
        // one a row below it straggles behind (FR-018), and one at the target already leaves nothing to do.
        SchemaFinalizationRecord? record = null;
        await withScope(async scope => record = await new EfSchemaFinalizationStore(scope.Context, _time).FindAsync(family, cancellationToken), cancellationToken);
        var completion = record?.Finish?.CompletionVersion;
        if (completion is not null && SchemaVersionChain.PositionOf(chain.ReadableVersions, completion) >= SchemaVersionChain.PositionOf(chain.ReadableVersions, target))
        {
            await withScope(scope => _gate.RefreshAsync(scope.Context, cancellationToken), cancellationToken);
            return;
        }

        var run = new Run(chain, target, completion, _gate.Families.DeclarationOf(family), await TablesAsync(withScope, chain, cancellationToken));
        var startedAt = _time.GetUtcNow();
        Update(family, status => status with
        {
            State = EfSchemaBackfillState.Upgrading,
            TargetVersion = target,
            RowsRewritten = status.TargetVersion == target ? status.RowsRewritten : 0,
            RunStartedAt = status.TargetVersion == target ? status.RunStartedAt ?? startedAt : startedAt,
            ClaimedBy = null,
            Detail = null
        });

        // The survey: what blocks completion whatever the upgrade pass does, and whether there is anything to upgrade.
        var blockers = new List<EfSchemaBackfillBlocker>();
        long toUpgrade = 0;
        await withScope(async scope =>
        {
            foreach (var table in run.Tables)
            {
                blockers.AddRange(await SurveyAsync(scope.Context, run, table, cancellationToken));
                if (!table.ContentAddressed)
                    toUpgrade += await table.CountAsync(scope.Context, EfSchemaStampFilter.In(run.Below), cancellationToken);
            }
        }, cancellationToken);

        if (toUpgrade > 0)
        {
            if (run.Declaration.Rewriter is null)
                blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.NoRewriter, null, toUpgrade,
                    $"{toUpgrade} row(s) of schema family '{family}' are below '{target}', and the family names no rewriter to upgrade them " +
                    "(spec 186, FR-004): declare one with [EfSchemaFamily(..., Rewriter = typeof(...))]."));
            else if (await ClaimAsync(withScope, run, cancellationToken))
            {
                foreach (var table in run.Tables.Where(table => !table.ContentAddressed))
                    await RewriteAsync(withScope, run, table, blockers, cancellationToken);
            }
            else
                return;
        }

        await WithdrawForStragglersAsync(withScope, run, cancellationToken);
        if (blockers.Count > 0)
        {
            Blocked(family, blockers);
            return;
        }

        if (!await SettledAsync(chain, target, identity, cancellationToken))
            return;

        for (var pass = 1; pass <= _options.VerificationPasses; pass++)
        {
            var verification = await VerifyAsync(withScope, run, cancellationToken);
            await WithdrawForStragglersAsync(withScope, run, cancellationToken);
            if (verification.Blockers.Count > 0)
            {
                Blocked(family, verification.Blockers);
                return;
            }

            if (verification.Found == 0)
            {
                await RecordAsync(withScope, run, verification, cancellationToken);
                return;
            }

            _logger.LogInformation(
                "The verification pass of schema family {Family} found {Found} row(s) below {Target} and rewrote them; it starts again (spec 186, FR-013).",
                family, verification.Found, target);
        }

        Update(family, status => status with
        {
            State = EfSchemaBackfillState.Verifying,
            Detail = $"Each of {_options.VerificationPasses} verification passes found rows below '{target}' that the one before it had not; the next round tries again."
        });
    }

    /// <summary>
    /// The verification pass (FR-013): after the settle condition, a complete pass over every table of the family. A
    /// rewritable row below the target is rewritten and counted as found, so the pass must run again; a row with an
    /// unreadable stamp, a row that fails to upcast and a content-addressed row below the target each block completion.
    /// </summary>
    private async Task<Verification> VerifyAsync(Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope, Run run, CancellationToken cancellationToken)
    {
        var startedAt = _time.GetUtcNow();
        Update(run.Chain.Family, status => status with { State = EfSchemaBackfillState.Verifying, SettleWaitingFor = [], Detail = null });
        // Where the finish history stands as the pass begins, so a withdrawal during it is told apart by position, not by
        // clock: two hosts' clocks, or one that has not moved, cannot order them.
        var historyMark = 0;
        await withScope(async scope => historyMark = (await new EfSchemaFinalizationStore(scope.Context, _time).FindAsync(run.Chain.Family, cancellationToken))?.FinishHistory.Count ?? 0, cancellationToken);
        var blockers = new List<EfSchemaBackfillBlocker>();
        long found = 0;
        foreach (var table in run.Tables)
        {
            await withScope(async scope => blockers.AddRange(await SurveyAsync(scope.Context, run, table, cancellationToken)), cancellationToken);
            if (table.ContentAddressed)
                continue;
            if (run.Declaration.Rewriter is null)
            {
                long below = 0;
                await withScope(async scope => below = await table.CountAsync(scope.Context, EfSchemaStampFilter.In(run.Below), cancellationToken), cancellationToken);
                if (below > 0)
                    blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.NoRewriter, table.Name, below,
                        $"{below} row(s) of table '{table.Name}' are below '{run.Target}', and schema family '{run.Chain.Family}' names no rewriter to upgrade them (spec 186, FR-004)."));
                continue;
            }

            found += await RewriteAsync(withScope, run, table, blockers, cancellationToken);
        }

        return new Verification(startedAt, _time.GetUtcNow(), historyMark, found, blockers);
    }

    /// <summary>
    /// What blocks completion in <paramref name="table"/> whatever a rewrite does (FR-006, FR-011a): rows whose stamp is
    /// missing or outside this host's readable set, and, in a content-addressed table, rows below the target.
    /// </summary>
    private static async Task<IReadOnlyList<EfSchemaBackfillBlocker>> SurveyAsync(DbContext context, Run run, EfSchemaStampedTable table, CancellationToken cancellationToken)
    {
        var blockers = new List<EfSchemaBackfillBlocker>();
        var skew = await table.CountAsync(context, EfSchemaStampFilter.NotIn(run.Chain.ReadableVersions), cancellationToken);
        if (skew > 0)
            blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.Skew, table.Name, skew,
                $"{skew} row(s) of table '{table.Name}' carry a stamp that is missing or outside this host's readable set " +
                $"[{string.Join(", ", run.Chain.ReadableVersions)}]. They are never rewritten, and schema family '{run.Chain.Family}' cannot be " +
                "recorded complete until they are resolved (spec 180, FR-007)."));
        if (table.ContentAddressed)
        {
            var below = await table.CountAsync(context, EfSchemaStampFilter.In(run.Below), cancellationToken);
            if (below > 0)
                blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.ContentAddressed, table.Name, below,
                    $"{below} content-addressed row(s) of table '{table.Name}' are below '{run.Target}', and nothing may upgrade them " +
                    $"(spec 186, FR-010a), so schema family '{run.Chain.Family}' can never be recorded complete at '{run.Target}' while they remain."));
        }

        return blockers;
    }

    /// <summary>
    /// Rewrites every row of <paramref name="table"/> below the run's target, in batches ordered by key, each batch
    /// resuming after the last key of the one before it; returns how many rows it found below the target that still
    /// existed. A row found below the standing completion is a straggler (FR-018).
    /// </summary>
    private async Task<long> RewriteAsync(
        Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope,
        Run run,
        EfSchemaStampedTable table,
        List<EfSchemaBackfillBlocker> blockers,
        CancellationToken cancellationToken)
    {
        object?[]? after = null;
        long found = 0;
        var corrupt = new List<string>();
        var skew = new List<string>();
        long unrewritten = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<EfSchemaStampedRow> batch = [];
            await withScope(async scope => batch = await table.PageAsync(scope.Context, EfSchemaStampFilter.In(run.Below), after, _options.BatchSize, cancellationToken), cancellationToken);
            if (batch.Count == 0)
                break;

            foreach (var row in batch)
            {
                var outcome = await RewriteRowAsync(withScope, run, table, row, cancellationToken);
                switch (outcome)
                {
                    case RowOutcome.Missing:
                        continue;
                    case RowOutcome.Corrupt:
                        corrupt.Add(table.Describe(row.Key));
                        break;
                    case RowOutcome.Skew:
                        skew.Add(table.Describe(row.Key));
                        break;
                    case RowOutcome.NoRewriter:
                        unrewritten++;
                        break;
                    case RowOutcome.Rewritten:
                        Update(run.Chain.Family, status => status with { RowsRewritten = status.RowsRewritten + 1 });
                        break;
                }

                found++;
                if (run.IsBelowCompletion(row.Stamp))
                    run.Stragglers[table.Name] = run.Stragglers.GetValueOrDefault(table.Name) + 1;
            }

            after = batch[^1].Key;
            await RenewClaimAsync(withScope, run, cancellationToken);
            if (batch.Count < _options.BatchSize)
                break;
            await Task.Delay(_options.BatchPause, _time, cancellationToken);
        }

        if (corrupt.Count > 0)
            blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.Corruption, table.Name, corrupt.Count,
                $"{corrupt.Count} row(s) of table '{table.Name}' of schema family '{run.Chain.Family}' could not be upcast, so they are corrupt: " +
                $"{string.Join("; ", corrupt.Take(ReportedRowsPerTable))}{(corrupt.Count > ReportedRowsPerTable ? "; ..." : "")}. " +
                "They are never rewritten or skipped silently, and the family cannot be recorded complete until they are repaired."));
        if (skew.Count > 0)
            blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.Skew, table.Name, skew.Count,
                $"{skew.Count} row(s) of table '{table.Name}' of schema family '{run.Chain.Family}' were read at a stamp this host cannot read: " +
                $"{string.Join("; ", skew.Take(ReportedRowsPerTable))}{(skew.Count > ReportedRowsPerTable ? "; ..." : "")}."));
        if (unrewritten > 0)
            blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.NoRewriter, table.Name, unrewritten,
                $"{unrewritten} row(s) of table '{table.Name}' are below '{run.Target}', and the rewriter of schema family '{run.Chain.Family}' " +
                $"cannot be constructed in this shell: {run.RewriterFault}"));
        return found;
    }

    /// <summary>
    /// Asks the family's rewriter to rewrite one row, in a fresh scope each time (FR-007): a lost compare-and-set is asked
    /// again, so the rewriter reads the row afresh; a row still contended after a few attempts is left for the verification
    /// pass. Skew and corruption are reported and never retried.
    /// </summary>
    private async Task<RowOutcome> RewriteRowAsync(
        Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope,
        Run run,
        EfSchemaStampedTable table,
        EfSchemaStampedRow row,
        CancellationToken cancellationToken)
    {
        var request = new EfSchemaRowToRewrite(table.Entity, row.Key, row.Stamp, run.Target);
        for (var attempt = 1; attempt <= RewriteAttempts; attempt++)
        {
            try
            {
                var outcome = EfSchemaRewriteOutcome.Conflict;
                await withScope(async scope => outcome = await Rewriter(scope.Services, run).RewriteAsync(request, cancellationToken), cancellationToken);
                switch (outcome)
                {
                    case EfSchemaRewriteOutcome.Rewritten:
                        return RowOutcome.Rewritten;
                    case EfSchemaRewriteOutcome.AlreadyCurrent:
                        return RowOutcome.AlreadyCurrent;
                    case EfSchemaRewriteOutcome.Missing:
                        return RowOutcome.Missing;
                }
            }
            catch (EfSchemaVersionSkewException exception)
            {
                // Reported with its table's blocker, which is logged as an error when it first appears (FR-006).
                _logger.LogDebug(exception, "The backfill of schema family {Family} met a row of table {Table} ({Row}) at a stamp this host cannot read; it is not rewritten.",
                    run.Chain.Family, table.Name, table.Describe(row.Key));
                return RowOutcome.Skew;
            }
            catch (InvalidDataException exception)
            {
                // Reported with its table's blocker, which names the row and is logged as an error when it first appears.
                _logger.LogDebug(exception, "The backfill of schema family {Family} found row {Row} of table {Table} corrupt: it could not be read through the chain, and is not rewritten.",
                    run.Chain.Family, table.Describe(row.Key), table.Name);
                return RowOutcome.Corrupt;
            }
            catch (RewriterUnavailableException exception)
            {
                run.RewriterFault = exception.Message;
                return RowOutcome.NoRewriter;
            }
            catch (EfSchemaFamilyWritesRefusedException)
            {
                // This host can no longer write the family at all (spec 181, FR-012): the run stops.
                throw;
            }
            catch (SchemaWriteRefusedException exception)
            {
                // The write version moved under the rewrite; the row is left for the next pass, at the new version.
                _logger.LogInformation(exception, "The backfill of schema family {Family} left row {Row} of table {Table} for a later pass: its write was refused.",
                    run.Chain.Family, table.Describe(row.Key), table.Name);
                return RowOutcome.Contended;
            }
            catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Concurrency))
            {
                // A lost compare-and-set: the row changed between the rewriter's read and its write. Ask again.
            }
        }

        return RowOutcome.Contended;
    }

    /// <summary>
    /// The family's rewriter, constructed in <paramref name="services"/>, the shell's scope for this row. One that cannot be
    /// constructed, because it is not a rewriter or a service it takes is not composed in this shell, blocks completion with
    /// the reason rather than failing the round, so the status says why.
    /// </summary>
    private static IEfSchemaRowRewriter Rewriter(IServiceProvider services, Run run)
    {
        var type = run.Declaration.Rewriter!;
        object instance;
        try
        {
            instance = ActivatorUtilities.CreateInstance(services, type);
        }
        catch (Exception exception) when (exception is InvalidOperationException or MissingMethodException or ArgumentException)
        {
            throw new RewriterUnavailableException($"'{type.FullName}' cannot be constructed: {exception.Message}", exception);
        }

        return instance as IEfSchemaRowRewriter
               ?? throw new RewriterUnavailableException($"'{type.FullName}' does not implement {nameof(IEfSchemaRowRewriter)}.", null);
    }

    /// <summary>
    /// The settle condition (FR-012): every counted member reports observing the target or later, and the settle margin
    /// has passed since this worker first saw that hold, which is no earlier than when the last of them began to report it.
    /// </summary>
    private async Task<bool> SettledAsync(EfSchemaChain chain, string target, string identity, CancellationToken cancellationToken)
    {
        var family = chain.Family;
        if (_fleet is null)
        {
            Blocked(family, [new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.NoFleet, null, 0,
                "This host composes no fleet, so it cannot tell whether every counted member has observed the finalized version, " +
                "and it never records completion (spec 186, FR-012).")]);
            return false;
        }

        Update(family, status => status with { State = EfSchemaBackfillState.Settling, Detail = null });
        var versions = chain.ReadableVersions.Skip(SchemaVersionChain.PositionOf(chain.ReadableVersions, target)).ToArray();
        EfSchemaFleetAnswer answer;
        try
        {
            answer = await _fleet.CountObservingAsync(family, versions, identity, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No answer is no settle condition: verification waits for a round in which the fleet can be read.
            _logger.LogWarning(exception, "The backfill of schema family {Family} could not read the fleet for its settle condition; it tries again next round.", family);
            Update(family, status => status with { Detail = $"The fleet could not be read: {exception.GetType().Name}." });
            return false;
        }

        var now = _time.GetUtcNow();
        DateTimeOffset since;
        lock (_lock)
        {
            if (!answer.EveryCountedMemberReads)
            {
                _settled.Remove(family);
                since = DateTimeOffset.MaxValue;
            }
            else if (_settled.TryGetValue(family, out var settled) && settled.Target == target)
                since = settled.Since;
            else
                _settled[family] = (target, since = now);
        }

        if (!answer.EveryCountedMemberReads)
        {
            Update(family, status => status with { SettleWaitingFor = answer.Blockers, Detail = $"Waiting for every counted member to observe '{target}' as finalized." });
            return false;
        }

        var margin = _options.SettleMargin ?? _fleet.SettleMargin;
        if (now - since < margin)
        {
            Update(family, status => status with { SettleWaitingFor = [], Detail = $"Every counted member has observed '{target}'; verification starts at {since + margin:u}." });
            return false;
        }

        return true;
    }

    /// <summary>
    /// Records the family complete at the run's target by compare-and-set (FR-014), unless a completion at or after it
    /// already stands, or the completion was withdrawn after the verification pass began, which only a new pass can
    /// answer. Then the gate reads the record again, so this host's dormancy check answers from it at once (FR-017).
    /// </summary>
    private async Task RecordAsync(
        Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope,
        Run run,
        Verification verification,
        CancellationToken cancellationToken)
    {
        var family = run.Chain.Family;
        var readable = run.Chain.ReadableVersions;
        var member = _gate.LocalMember();
        string? unrecorded = $"Each of {RecordAttempts} attempts to record the completion lost its compare-and-set; the next round tries again.";
        await withScope(async scope =>
        {
            var store = new EfSchemaFinalizationStore(scope.Context, _time);
            for (var attempt = 1; attempt <= RecordAttempts; attempt++)
            {
                var record = await store.FindAsync(family, cancellationToken)
                             ?? throw new InvalidOperationException($"The finalization record of '{family}' vanished while its backfill ran.");
                if (record.Finish is { } standing && SchemaVersionChain.PositionOf(readable, standing.CompletionVersion) >= run.TargetAt)
                {
                    unrecorded = null;
                    break;
                }

                if (record.FinishHistory.Skip(verification.HistoryMark).Any(entry => entry.Transition == SchemaFinishTransition.Withdrawn))
                {
                    unrecorded = "The completion was withdrawn while the verification pass ran, so only a new pass can prove it.";
                    break;
                }

                try
                {
                    if (!(await store.RecordCompletionAsync(family, record.Revision, run.Target, verification.StartedAt, verification.EndedAt, readable, member, cancellationToken)).Applied)
                        continue;
                    _logger.LogInformation("Schema family {Family} of EF module {Module} is complete at {Version}: no row below it remains (spec 186, FR-014).", family, Module, run.Target);
                    unrecorded = null;
                    break;
                }
                catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.NotForward)
                {
                    // Another worker recorded it, or a later version, since the read.
                    unrecorded = null;
                    break;
                }
            }

            await _gate.RefreshAsync(scope.Context, cancellationToken);
        }, cancellationToken);

        if (unrecorded is not null)
        {
            Update(family, status => status with { State = EfSchemaBackfillState.Verifying, Detail = unrecorded });
            return;
        }

        lock (_lock)
            _nextAudit[family] = _time.GetUtcNow() + _options.AuditInterval;
        Update(family, status => status with { State = EfSchemaBackfillState.Complete, Blockers = [], SettleWaitingFor = [], ClaimedBy = null, Detail = null });
    }

    /// <summary>
    /// The audit (FR-018): while the completion stands, a selection by stamp of every table of the family for a row below
    /// it. A rewritable straggler is rewritten; any row found below it, rewritable or not, withdraws the completion.
    /// </summary>
    private async Task AuditAsync(Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope, EfSchemaChain chain, string completion, CancellationToken cancellationToken)
    {
        var family = chain.Family;
        var run = new Run(chain, completion, completion, _gate.Families.DeclarationOf(family), await TablesAsync(withScope, chain, cancellationToken));
        var auditedAt = _time.GetUtcNow();
        var found = new Dictionary<string, (long Count, string Kind)>(StringComparer.Ordinal);
        foreach (var table in run.Tables)
        {
            long unreadable = 0;
            long below = 0;
            await withScope(async scope =>
            {
                unreadable = await table.CountAsync(scope.Context, EfSchemaStampFilter.NotIn(chain.ReadableVersions), cancellationToken);
                below = await table.CountAsync(scope.Context, EfSchemaStampFilter.In(run.Below), cancellationToken);
            }, cancellationToken);
            if (unreadable > 0)
                found[$"{table.Name} (unreadable stamps)"] = (unreadable, "with a stamp this host cannot read");
            if (below == 0)
                continue;
            if (table.ContentAddressed || run.Declaration.Rewriter is null)
                found[table.Name] = (below, table.ContentAddressed ? "content-addressed, which nothing may upgrade" : "with no rewriter to upgrade them");
            else
            {
                var unrewritable = new List<EfSchemaBackfillBlocker>();
                await RewriteAsync(withScope, run, table, unrewritable, cancellationToken);
                found[table.Name] = (below, unrewritable.Count == 0 ? "rewritten" : "found, and not all could be rewritten: " + string.Join(" ", unrewritable.Select(blocker => blocker.Detail)));
            }
        }

        lock (_lock)
            _nextAudit[family] = auditedAt + _options.AuditInterval;
        run.Stragglers.Clear();
        var reason = found.Count == 0
            ? null
            : $"The audit found rows below '{completion}': " + string.Join("; ", found.Select(entry => $"table '{entry.Key}': {entry.Value.Count} ({entry.Value.Kind})")) + ".";
        Update(family, status => status with { State = EfSchemaBackfillState.Complete, LastAuditAt = auditedAt, TargetVersion = null, Blockers = [], Detail = reason });
        if (reason is not null)
            await WithdrawAsync(withScope, run, reason, cancellationToken);
    }

    /// <summary>Withdraws the standing completion when a pass of this run rewrote a row below it (FR-018).</summary>
    private Task WithdrawForStragglersAsync(Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope, Run run, CancellationToken cancellationToken)
    {
        if (run.Stragglers.Count == 0)
            return Task.CompletedTask;
        var reason = $"The backfill found rows below the completion version '{run.Completion}': " +
                     string.Join("; ", run.Stragglers.Select(entry => $"table '{entry.Key}': {entry.Value} (rewritten)")) + ".";
        run.Stragglers.Clear();
        return WithdrawAsync(withScope, run, reason, cancellationToken);
    }

    /// <summary>
    /// Withdraws the completion that stands, by compare-and-set with a history entry giving <paramref name="reason"/>
    /// (FR-018), leaving the finalized version where it is (FR-019), and reports it as critical. Every host's Attention
    /// reads the withdrawal from the record. Then the gate reads the record again, so features that need completeness go
    /// dormant on this host at once.
    /// </summary>
    private async Task WithdrawAsync(Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope, Run run, string reason, CancellationToken cancellationToken)
    {
        var family = run.Chain.Family;
        var member = _gate.LocalMember();
        await withScope(async scope =>
        {
            var store = new EfSchemaFinalizationStore(scope.Context, _time);
            for (var attempt = 1; attempt <= RecordAttempts; attempt++)
            {
                if (await store.FindAsync(family, cancellationToken) is not { Finish: not null } record)
                    break;
                try
                {
                    if ((await store.WithdrawCompletionAsync(family, record.Revision, member, reason, cancellationToken)).Applied)
                    {
                        _logger.LogCritical(
                            "Schema family {Family} of EF module {Module} is no longer complete at {Version}: {Reason} The completion is withdrawn until a new " +
                            "verification pass succeeds, and features that need it are dormant meanwhile (spec 186, FR-018).",
                            family, Module, record.Finish.CompletionVersion, reason);
                        break;
                    }
                }
                catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.NoCompletion)
                {
                    break;
                }
            }

            await _gate.RefreshAsync(scope.Context, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Claims the run in the finish record (FR-008), or returns false when another worker's claim holds. A record with no
    /// finish record, or one whose claim is lost to compare-and-set again and again, runs unclaimed: nothing correct
    /// depends on the claim.
    /// </summary>
    private async Task<bool> ClaimAsync(Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope, Run run, CancellationToken cancellationToken)
    {
        if (_options.ClaimDuration <= TimeSpan.Zero)
            return true;
        var family = run.Chain.Family;
        var member = _gate.LocalMember();
        var claimed = true;
        await withScope(async scope =>
        {
            var store = new EfSchemaFinalizationStore(scope.Context, _time);
            for (var attempt = 1; attempt <= RecordAttempts; attempt++)
            {
                if (await store.FindAsync(family, cancellationToken) is not { Finish: not null } record)
                    return;
                if (record.Finish.Run is { } own && own.Worker == _worker && own.TargetVersion == run.Target &&
                    own.ExpiresAt - _time.GetUtcNow() > _options.ClaimDuration * 2 / 3)
                {
                    run.ClaimRenewedAt = own.ExpiresAt - _options.ClaimDuration;
                    return;
                }

                try
                {
                    if ((await store.ClaimBackfillAsync(family, record.Revision, run.Target, run.Chain.ReadableVersions, member, _worker, _options.ClaimDuration, cancellationToken)).Applied)
                    {
                        run.ClaimRenewedAt = _time.GetUtcNow();
                        return;
                    }
                }
                catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.BackfillClaimed)
                {
                    claimed = false;
                    var holder = record.Finish.Run;
                    Update(family, status => status with
                    {
                        State = EfSchemaBackfillState.ClaimedElsewhere,
                        ClaimedBy = holder,
                        Detail = holder is null ? null : $"The run is claimed by {holder.Member} until {holder.ExpiresAt:u}."
                    });
                    return;
                }
                catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal is SchemaFinalizationRefusal.NotForward or SchemaFinalizationRefusal.CompletionBeyondFinalized)
                {
                    // The completion moved to the target or past it since this host last observed it: nothing to claim.
                    claimed = false;
                    await _gate.RefreshAsync(scope.Context, cancellationToken);
                    return;
                }
            }
        }, cancellationToken);
        return claimed;
    }

    /// <summary>Renews the run's claim once a third of its period has passed, and stops the run if another worker has taken it over.</summary>
    private async Task RenewClaimAsync(Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope, Run run, CancellationToken cancellationToken)
    {
        if (run.ClaimRenewedAt is not { } renewedAt || _time.GetUtcNow() - renewedAt < _options.ClaimDuration / 3)
            return;
        if (!await ClaimAsync(withScope, run, cancellationToken))
            throw new ClaimLostException($"The run of schema family '{run.Chain.Family}' was taken over by another worker after this one's claim lapsed.");
    }

    private async Task<IReadOnlyList<EfSchemaStampedTable>> TablesAsync(
        Func<Func<EfSchemaBackfillScope, Task>, CancellationToken, Task> withScope,
        EfSchemaChain chain,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_tables.TryGetValue(chain.Family, out var known))
                return known;
        }

        IReadOnlyList<EfSchemaStampedTable> tables = [];
        await withScope(scope =>
        {
            tables = EfSchemaStampedTable.Of(scope.Context.Model, _gate.Families, _gate.Families.DeclarationOf(chain.Family));
            return Task.CompletedTask;
        }, cancellationToken);
        lock (_lock)
            _tables[chain.Family] = tables;
        return tables;
    }

    private DateTimeOffset NextAuditOf(string family)
    {
        lock (_lock)
            return _nextAudit.GetValueOrDefault(family, DateTimeOffset.MinValue);
    }

    private void Blocked(string family, IReadOnlyList<EfSchemaBackfillBlocker> blockers)
    {
        // Every round meets a persistent blocker again, so it is logged when it first appears or changes, not each round.
        var reported = StatusOf(family).Blockers.Select(blocker => blocker.Detail).ToHashSet(StringComparer.Ordinal);
        foreach (var blocker in blockers.Where(blocker => !reported.Contains(blocker.Detail)))
            _logger.Log(
                blocker.Kind == EfSchemaBackfillBlockerKind.ContentAddressed ? LogLevel.Information : LogLevel.Error,
                "Schema family {Family} of EF module {Module} cannot be recorded complete: {Blocker}", family, Module, blocker.Detail);
        Update(family, status => status with { State = EfSchemaBackfillState.Blocked, Blockers = blockers, SettleWaitingFor = [], Detail = null });
    }

    private void Update(string family, Func<EfSchemaBackfillStatus, EfSchemaBackfillStatus> change)
    {
        lock (_lock)
            _statuses[family] = change(_statuses.GetValueOrDefault(family) ?? EfSchemaBackfillStatus.Idle(family));
    }

    /// <summary>One run's fixed facts: the family, its target, the completion that stood when it began, and its tables.</summary>
    private sealed class Run(
        EfSchemaChain chain,
        string target,
        string? completion,
        EfSchemaFamilyDescriptor declaration,
        IReadOnlyList<EfSchemaStampedTable> tables)
    {
        public EfSchemaChain Chain { get; } = chain;

        public string Target { get; } = target;

        public int TargetAt { get; } = SchemaVersionChain.PositionOf(chain.ReadableVersions, target);

        public string? Completion { get; } = completion;

        public EfSchemaFamilyDescriptor Declaration { get; } = declaration;

        public IReadOnlyList<EfSchemaStampedTable> Tables { get; } = tables;

        /// <summary>The versions this host reads before the target: the stamps a pass selects.</summary>
        public IReadOnlyList<string> Below { get; } = chain.ReadableVersions.Take(SchemaVersionChain.PositionOf(chain.ReadableVersions, target)).ToArray();

        /// <summary>Rows found below the completion that stood when the run began, per table (FR-018).</summary>
        public Dictionary<string, long> Stragglers { get; } = new(StringComparer.Ordinal);

        public DateTimeOffset? ClaimRenewedAt { get; set; }

        /// <summary>Why the family's rewriter cannot be constructed, once a row has found out.</summary>
        public string? RewriterFault { get; set; }

        public bool IsBelowCompletion(string? stamp)
        {
            if (Completion is null)
                return false;
            var position = SchemaVersionChain.PositionOf(Chain.ReadableVersions, stamp ?? "");
            return position >= 0 && position < SchemaVersionChain.PositionOf(Chain.ReadableVersions, Completion);
        }
    }

    /// <summary>This worker's claim lapsed and another worker took the run over: this one stops, which spares the other its reads.</summary>
    private sealed class ClaimLostException(string message) : Exception(message);

    /// <summary>The family's rewriter cannot be constructed in this shell.</summary>
    private sealed class RewriterUnavailableException(string message, Exception? inner) : Exception(message, inner);

    /// <param name="HistoryMark">How many finish history entries stood when the pass began.</param>
    private sealed record Verification(DateTimeOffset StartedAt, DateTimeOffset EndedAt, int HistoryMark, long Found, IReadOnlyList<EfSchemaBackfillBlocker> Blockers);

    private enum RowOutcome
    {
        Rewritten,
        AlreadyCurrent,
        Missing,
        Contended,
        Skew,
        Corrupt,
        NoRewriter
    }
}
