using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Primitives.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EntityFramework.SchemaBackfill;

/// <summary>
/// The backfill's passes over a family's rows (spec 186, FR-005 to FR-013 and FR-018): the survey of what blocks
/// completion, the upgrade pass, the verification pass, and the audit's selection of rows below a standing completion,
/// each a selection by stamp in bounded batches, and every rewrite asked of the family's rewriter in a fresh scope.
/// </summary>
/// <remarks>
/// A pass that meets a row below the completion standing withdraws that completion before it rewrites the row (FR-018),
/// so a pass that dies midway never leaves a known straggler under a standing completion. What stands is read afresh
/// before each row, and so whenever a batch starts, unless the run already knows of a completion at or after its target:
/// another worker may record one while the run goes on.
/// </remarks>
internal sealed class EfSchemaBackfillPasses(
    EfSchemaBackfillFinish finish,
    EfSchemaBackfillOptions options,
    TimeProvider time,
    ILogger logger,
    EfSchemaBackfillStatusBoard status)
{
    private const int RewriteAttempts = 3;
    private const int ReportedRowsPerTable = 5;

    /// <summary>
    /// The survey: what blocks completion whatever the upgrade pass does (FR-006, FR-011a), and how many rows of the
    /// family's rewritable tables are below the target.
    /// </summary>
    public Task<(List<EfSchemaBackfillBlocker> Blockers, long ToUpgrade)> SurveyAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, CancellationToken cancellationToken) =>
        scopes.WithScopeAsync(async scope =>
        {
            var blockers = new List<EfSchemaBackfillBlocker>();
            long toUpgrade = 0;
            foreach (var table in run.Tables)
            {
                blockers.AddRange(await BlockersAsync(scope.Context, run, table, cancellationToken));
                if (!table.ContentAddressed)
                    toUpgrade += await table.CountAsync(scope.Context, EfSchemaStampFilter.In(run.Below), cancellationToken);
            }

            return (blockers, toUpgrade);
        }, cancellationToken);

    /// <summary>The upgrade pass (FR-005): every row of every table the run rewrites, below its target.</summary>
    public async Task UpgradeAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, List<EfSchemaBackfillBlocker> blockers, CancellationToken cancellationToken)
    {
        foreach (var table in run.Tables.Where(run.Rewrites))
            await RewriteAsync(scopes, run, table, blockers, cancellationToken);
    }

    /// <summary>
    /// The verification pass (FR-013): after the settle condition, a complete pass over every table of the family. A
    /// rewritable row below the target is rewritten and counted as found, so the pass must run again; a row with an
    /// unreadable stamp, a row that fails to upcast and a content-addressed row below the target each block completion.
    /// </summary>
    /// <param name="historyMark">Where the finish history stood when the settle margin the pass follows began.</param>
    public async Task<EfSchemaBackfillVerification> VerifyAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, int historyMark, CancellationToken cancellationToken)
    {
        var startedAt = time.GetUtcNow();
        status.Update(run.Family, current => current with { State = EfSchemaBackfillState.Verifying, SettleWaitingFor = [], Detail = null });
        var blockers = new List<EfSchemaBackfillBlocker>();
        long found = 0;
        foreach (var table in run.Tables)
        {
            blockers.AddRange(await scopes.WithScopeAsync(scope => BlockersAsync(scope.Context, run, table, cancellationToken), cancellationToken));
            if (table.ContentAddressed)
                continue;
            if (run.Rewrites(table))
            {
                found += await RewriteAsync(scopes, run, table, blockers, cancellationToken);
                continue;
            }

            var below = await scopes.WithScopeAsync(scope => table.CountAsync(scope.Context, EfSchemaStampFilter.In(run.Below), cancellationToken), cancellationToken);
            if (below > 0)
                blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.NoRewriter, table.Name, below,
                    $"{below} row(s) of table '{table.Name}' are below '{run.Target}', and schema family '{run.Family}' names no rewriter to upgrade them (spec 186, FR-004)."));
        }

        return new EfSchemaBackfillVerification(startedAt, time.GetUtcNow(), historyMark, found, blockers);
    }

    /// <summary>
    /// Why <paramref name="standing"/> does not hold, read now in <paramref name="context"/> (FR-018), or null when no row
    /// below it remains: per table, the rows below its completion version, and for an audit every such row and every row
    /// with a stamp this host cannot read, whatever the table; for a pass only the rows it rewrites. It names the table,
    /// the count, and what becomes of them.
    /// </summary>
    public async Task<string?> StragglersAsync(DbContext context, EfSchemaBackfillRun run, SchemaFinishRecord standing, bool audit, CancellationToken cancellationToken)
    {
        var readable = run.Chain.ReadableVersions;
        if (SchemaVersionChain.PositionOf(readable, standing.CompletionVersion) < 0)
            return null;
        var below = EfSchemaBackfillRun.BelowOf(run.Chain, standing.CompletionVersion);
        var found = new List<string>();
        foreach (var table in run.Tables.Where(table => audit || run.Rewrites(table)))
        {
            if (audit && await table.CountAsync(context, EfSchemaStampFilter.NotIn(readable), cancellationToken) is > 0 and var unreadable)
                found.Add($"table '{table.Name}': {unreadable} (with a stamp this host cannot read)");
            if (await table.CountAsync(context, EfSchemaStampFilter.In(below), cancellationToken) is > 0 and var count)
                found.Add($"table '{table.Name}': {count} ({(run.Rewrites(table) ? "to be rewritten" : table.ContentAddressed ? "content-addressed, which nothing may upgrade" : "with no rewriter to upgrade them")})");
        }

        return found.Count == 0
            ? null
            : $"{(audit ? "The audit" : "The backfill")} found rows below the completion version '{standing.CompletionVersion}': {string.Join("; ", found)}.";
    }

    /// <summary>
    /// Rewrites every row of <paramref name="table"/> below the run's target, in batches ordered by key, each batch
    /// resuming after the last key of the one before it; returns how many rows it found below the target that still
    /// existed. A row below the completion that stands withdraws it before it is rewritten (FR-018).
    /// </summary>
    public async Task<long> RewriteAsync(
        EfSchemaBackfillScopeRunner scopes,
        EfSchemaBackfillRun run,
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
            var batch = await scopes.WithScopeAsync(scope => table.PageAsync(scope.Context, EfSchemaStampFilter.In(run.Below), after, options.BatchSize, cancellationToken), cancellationToken);
            if (batch.Count == 0)
                break;

            foreach (var row in batch)
            {
                await WithdrawOverAsync(scopes, run, row, cancellationToken);
                switch (await RewriteRowAsync(scopes, run, table, row, cancellationToken))
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
                        status.Update(run.Family, current => current with { RowsRewritten = current.RowsRewritten + 1 });
                        break;
                }

                found++;
            }

            after = batch[^1].Key;
            await finish.RenewClaimAsync(scopes, run, cancellationToken);
            if (batch.Count < options.BatchSize)
                break;
            await Task.Delay(options.BatchPause, time, cancellationToken);
        }

        if (corrupt.Count > 0)
            blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.Corruption, table.Name, corrupt.Count,
                $"{corrupt.Count} row(s) of table '{table.Name}' of schema family '{run.Family}' could not be upcast, so they are corrupt: " +
                $"{Sample(corrupt)}. They are never rewritten or skipped silently, and the family cannot be recorded complete until they are repaired."));
        if (skew.Count > 0)
            blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.Skew, table.Name, skew.Count,
                $"{skew.Count} row(s) of table '{table.Name}' of schema family '{run.Family}' were read at a stamp this host cannot read: {Sample(skew)}."));
        if (unrewritten > 0)
            blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.NoRewriter, table.Name, unrewritten,
                $"{unrewritten} row(s) of table '{table.Name}' are below '{run.Target}', and the rewriter of schema family '{run.Family}' " +
                $"cannot be constructed in this shell: {run.RewriterFault}"));
        return found;
    }

    /// <summary>
    /// Withdraws the completion that stands over <paramref name="row"/> before the row is rewritten (FR-018). While the run
    /// knows of no completion at or after its target, it reads the finish record afresh first: a completion another worker
    /// recorded since the run began, or since this batch was selected, covers the row, and a row rewritten under it
    /// unreported would hide a straggler. A completion at or after the target covers every row a pass selects, and the
    /// withdrawal reads the record afresh itself.
    /// </summary>
    private async Task WithdrawOverAsync(EfSchemaBackfillScopeRunner scopes, EfSchemaBackfillRun run, EfSchemaStampedRow row, CancellationToken cancellationToken)
    {
        if (!run.CompletionCoversTarget)
            run.Completion = (await finish.ReadAsync(scopes, run.Family, cancellationToken))?.Finish;
        if (run.IsBelowCompletion(row.Stamp))
            run.Completion = await finish.WithdrawAsync(scopes, run.Family, (scope, standing) => StragglersAsync(scope.Context, run, standing, audit: false, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// What blocks completion in <paramref name="table"/> whatever a rewrite does (FR-006, FR-011a): rows whose stamp is
    /// missing or outside this host's readable set, and, in a content-addressed table, rows below the target.
    /// </summary>
    private static async Task<IReadOnlyList<EfSchemaBackfillBlocker>> BlockersAsync(DbContext context, EfSchemaBackfillRun run, EfSchemaStampedTable table, CancellationToken cancellationToken)
    {
        var blockers = new List<EfSchemaBackfillBlocker>();
        var skew = await table.CountAsync(context, EfSchemaStampFilter.NotIn(run.Chain.ReadableVersions), cancellationToken);
        if (skew > 0)
            blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.Skew, table.Name, skew,
                $"{skew} row(s) of table '{table.Name}' carry a stamp that is missing or outside this host's readable set " +
                $"[{string.Join(", ", run.Chain.ReadableVersions)}]. They are never rewritten, and schema family '{run.Family}' cannot be " +
                "recorded complete until they are resolved (spec 180, FR-007)."));
        if (table.ContentAddressed && await table.CountAsync(context, EfSchemaStampFilter.In(run.Below), cancellationToken) is > 0 and var below)
            blockers.Add(new EfSchemaBackfillBlocker(EfSchemaBackfillBlockerKind.ContentAddressed, table.Name, below,
                $"{below} content-addressed row(s) of table '{table.Name}' are below '{run.Target}', and nothing may upgrade them " +
                $"(spec 186, FR-010a), so schema family '{run.Family}' can never be recorded complete at '{run.Target}' while they remain."));
        return blockers;
    }

    /// <summary>
    /// Asks the family's rewriter to rewrite one row, in a fresh scope each time (FR-007): a lost compare-and-set is asked
    /// again, so the rewriter reads the row afresh; a row still contended after a few attempts is left for the verification
    /// pass. Skew and corruption are reported and never retried.
    /// </summary>
    private async Task<RowOutcome> RewriteRowAsync(
        EfSchemaBackfillScopeRunner scopes,
        EfSchemaBackfillRun run,
        EfSchemaStampedTable table,
        EfSchemaStampedRow row,
        CancellationToken cancellationToken)
    {
        var request = new EfSchemaRowToRewrite(table.Entity, row.Key, row.Stamp, run.Target);
        for (var attempt = 1; attempt <= RewriteAttempts; attempt++)
        {
            try
            {
                switch (await scopes.WithScopeAsync(scope => Rewriter(scope.Services, run).RewriteAsync(request, cancellationToken).AsTask(), cancellationToken))
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
                logger.LogDebug(exception, "The backfill of schema family {Family} met a row of table {Table} ({Row}) at a stamp this host cannot read; it is not rewritten.",
                    run.Family, table.Name, table.Describe(row.Key));
                return RowOutcome.Skew;
            }
            catch (InvalidDataException exception)
            {
                // Reported with its table's blocker, which names the row and is logged as an error when it first appears.
                logger.LogDebug(exception, "The backfill of schema family {Family} found row {Row} of table {Table} corrupt: it could not be read through the chain, and is not rewritten.",
                    run.Family, table.Describe(row.Key), table.Name);
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
                logger.LogInformation(exception, "The backfill of schema family {Family} left row {Row} of table {Table} for a later pass: its write was refused.",
                    run.Family, table.Describe(row.Key), table.Name);
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
    private static IEfSchemaRowRewriter Rewriter(IServiceProvider services, EfSchemaBackfillRun run)
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

    private static string Sample(IReadOnlyList<string> rows) =>
        $"{string.Join("; ", rows.Take(ReportedRowsPerTable))}{(rows.Count > ReportedRowsPerTable ? "; ..." : "")}";

    /// <summary>The family's rewriter cannot be constructed in this shell.</summary>
    private sealed class RewriterUnavailableException(string message, Exception? inner) : Exception(message, inner);

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
