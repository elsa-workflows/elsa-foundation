using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryCheckpointTests
{
    [Fact]
    public async Task Prefix_retirement_keeps_replay_membership_bookmark_and_native_coverage_evidence()
    {
        await using var database = await BookmarkHistoryAsync();
        Assert.Equal(2, await PurgeAsync(database, 2));
        await using var read = database.Open(); var mapping = new Mapping(Chain);
        var head = mapping.Read(await read.Set<RuntimeHistoryHeadEntity>().SingleAsync(), Scope, Run);
        Assert.Equal(2, head.PurgedThroughSequence); Assert.Equal(3, head.FirstSequence); Assert.Equal(4, head.LastSequence);
        Assert.Equal("native", head.LastPhysicalCommitId); Assert.Equal(Now.AddDays(1), head.LastCommittedAt);
        Assert.True(head.CapturedFromStart); Assert.False(head.HasGaps); Assert.True(Policy.IsEquivalentTo(head.Policy));
        var rows = await read.Set<RuntimeHistoryEventEntity>().OrderBy(row => row.Sequence).ToArrayAsync();
        Assert.All(rows.Take(2), row => { Assert.True(row.IsPurged); Assert.Empty(row.ContentJson); Assert.Equal(64, row.CandidateFingerprint.Length); });
        Assert.All(rows.Skip(2), row => { Assert.False(row.IsPurged); Assert.NotEmpty(row.ContentJson); });
        Assert.All(rows, row => Assert.Equal(row.Sequence, mapping.ReadReceipt(row, Scope, Run, head).Sequence));
        var bookmark = mapping.Read(await read.Set<RuntimeHistoryBookmarkEntity>().SingleAsync(), Scope, Run, head);
        Assert.Equal(RuntimeHistoryBookmarkStatus.Consumed, bookmark.Status); Assert.Null(bookmark.CreatedHistoryEventId);
        Assert.Equal("consumed", bookmark.EndedHistoryEventId); Assert.Equal(RuntimeHistoryCreationEvidence.CreationMetadata, bookmark.PriorState);
        Assert.Equal(Now, bookmark.EndedAt); Assert.Equal(2, await read.Set<RuntimeHistoryBookmarkCountEntity>().CountAsync());
        var global = await read.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null);
        Assert.Equal(1, global.NextOrdinal); Assert.Equal(4, global.CoveredThroughSequence);
        Assert.Single(await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.Equal(2, await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().CountAsync());
        Assert.Single(await read.WorkflowExecutionStates.ToArrayAsync());
    }

    [Fact]
    public async Task Native_and_independent_observation_replay_cannot_resurrect_purged_content()
    {
        await using var database = await CapturedDatabaseAsync(); var observation = Observation("delivery");
        await PersistObservationAsync(database, observation); Assert.Equal(2, await PurgeAsync(database, long.MaxValue));
        var replay = await PersistObservationAsync(database, observation);
        Assert.True(replay.Recorded); Assert.Null(replay.Event);
        Assert.Empty(await PersistAsync(database, Commit("replay", [Batch("native", Candidate("native", "native-event", 0))])));
        var next = await PersistAsync(database, Commit("next", [Batch("next", Candidate("next", "next-event", 0))]));
        Assert.Equal(3, Assert.Single(next).Sequence);
        await using var read = database.Open(); var rows = await read.Set<RuntimeHistoryEventEntity>().OrderBy(row => row.Sequence).ToArrayAsync();
        Assert.Equal(3, rows.Length); Assert.All(rows.Take(2), row => { Assert.True(row.IsPurged); Assert.Empty(row.ContentJson); });
        Assert.Single(await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.Equal(3, (await read.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null)).CoveredThroughSequence);
    }

    [Fact]
    public async Task Prefix_is_clamped_monotonic_and_idempotent_without_spending_allocation()
    {
        await using var database = await CapturedDatabaseAsync();
        Assert.Equal(0, await PurgeAsync(database, 0)); Assert.Equal(1, await PurgeAsync(database, long.MaxValue));
        long revision;
        await using (var read = database.Open()) revision = (await read.Set<RuntimeHistoryHeadEntity>().SingleAsync()).Revision;
        Assert.Equal(1, await PurgeAsync(database, 0)); Assert.Equal(1, await PurgeAsync(database, long.MaxValue));
        await using var after = database.Open(); var head = await after.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        Assert.Equal(revision, head.Revision); Assert.Equal(1, head.LastSequence); Assert.Equal(2, head.FirstSequence);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_native_or_capture_history_cannot_be_created_by_retention(bool missingNative)
    {
        await using var database = await Database.CreateAsync(explicitLegacy: true);
        if (missingNative) { await using var remove = database.Open(); await remove.WorkflowExecutionStates.ExecuteDeleteAsync(); }
        Assert.Equal(0, await PurgeAsync(database, long.MaxValue)); await AssertRowsAsync(database, 0);
    }

    [Fact]
    public async Task Purge_failure_rolls_back_the_earlier_head_reservation_and_retains_content()
    {
        await using var database = await CapturedDatabaseAsync();
        await using (var context = database.Open())
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_history_purge BEFORE UPDATE ON elsa_runtime_history_event
                BEGIN SELECT RAISE(ABORT, 'controlled content retirement failure'); END;
                """);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<SqliteException>(() => StagePurge(context, 1)); await transaction.RollbackAsync();
        }
        await using var read = database.Open(); var head = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        Assert.Equal(0, head.PurgedThroughSequence); Assert.Equal(1, head.FirstSequence);
        var item = await read.Set<RuntimeHistoryEventEntity>().SingleAsync(); Assert.False(item.IsPurged); Assert.NotEmpty(item.ContentJson);
    }

    [Fact]
    public async Task Retention_refuses_policy_mismatch_and_access_retirement()
    {
        await using var database = await CapturedDatabaseAsync(); var access = new Accessor();
        await using (var context = database.Open(new RetireAccess(access)))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => StagePurge(context, 1, access)); await transaction.RollbackAsync();
        }
        await using (var change = database.Open())
        {
            var row = await change.WorkflowExecutionStates.SingleAsync();
            row.ContentJson = RuntimeArtifactJson.Serialize(Workflow(Policy) with { HistoryCaptureEnabledAtAdmission = false }); await change.SaveChangesAsync();
        }
        await using var mismatch = database.Open(); await using var boundary = await mismatch.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => StagePurge(mismatch, 1)); await boundary.RollbackAsync();
        await using var read = database.Open(); Assert.Equal(0, (await read.Set<RuntimeHistoryHeadEntity>().SingleAsync()).PurgedThroughSequence);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Neither_retirement_participant_can_run_without_its_owners_transaction(bool fullDelete)
    {
        await using var database = await CapturedDatabaseAsync(); await using var context = database.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fullDelete ? StageDelete(context) : StagePurge(context, 1));
        await AssertRowsAsync(database, 1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Pending_history_changes_cannot_be_saved_over_set_based_retirement(bool fullDelete)
    {
        await using var database = await CapturedDatabaseAsync(); await using var context = database.Open();
        (await context.Set<RuntimeHistoryEventEntity>().SingleAsync()).ContentJson = "pending overwrite";
        await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fullDelete ? StageDelete(context) : StagePurge(context, 1));
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 1);
    }

    [Fact]
    public async Task Full_retirement_removes_all_owned_history_and_preserves_other_scope_and_run()
    {
        await using var database = await BookmarkHistoryAsync();
        await SeedOtherAsync(database, "other-scope", Run); await SeedOtherAsync(database, Scope, "other-run");
        await using (var context = database.Open())
        {
            // Clean tracked projections must not remain stale after set-based deletion. A native
            // sibling remains tracked and is saved by its own caller in this same transaction.
            await context.Set<RuntimeHistoryEventEntity>().Where(row => row.WorkflowExecutionId == EfRelationalIdentity.Encode(Run)).LoadAsync();
            var native = await context.WorkflowExecutionStates.SingleAsync(row => row.Id == EfWorkflowExecutionStateStore.CreateId(Scope, Run));
            await using var transaction = await context.Database.BeginTransactionAsync();
            await StageDelete(context); context.WorkflowExecutionStates.Remove(native);
            await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        await using var read = database.Open();
        Assert.Equal(2, await read.Set<RuntimeHistoryHeadEntity>().CountAsync()); Assert.Equal(2, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
        Assert.Equal(2, await read.Set<RuntimeHistoryOccurrenceEntity>().CountAsync()); Assert.Equal(4, await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().CountAsync());
        Assert.Equal(4, await read.Set<RuntimeHistoryOccurrenceCounterEntity>().CountAsync()); Assert.Equal(2, await read.Set<RuntimeHistoryActivityEntity>().CountAsync());
        Assert.Empty(await read.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync()); Assert.Empty(await read.Set<RuntimeHistoryBookmarkCountEntity>().ToArrayAsync());
        Assert.Equal(2, await read.WorkflowExecutionStates.CountAsync());
        Assert.False((await PersistObservationAsync(database, Observation("late"))).Recorded);
    }

    [Fact]
    public async Task Later_delete_failure_rolls_back_every_already_retired_projection()
    {
        await using var database = await BookmarkHistoryAsync();
        await using (var context = database.Open())
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_history_delete BEFORE DELETE ON elsa_runtime_history_event
                BEGIN SELECT RAISE(ABORT, 'controlled run retirement failure'); END;
                """);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<SqliteException>(() => StageDelete(context)); await transaction.RollbackAsync();
        }
        await using var read = database.Open(); Assert.Single(await read.Set<RuntimeHistoryHeadEntity>().ToArrayAsync());
        Assert.Equal(4, await read.Set<RuntimeHistoryEventEntity>().CountAsync()); Assert.Single(await read.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync());
        Assert.Equal(2, await read.Set<RuntimeHistoryBookmarkCountEntity>().CountAsync()); Assert.Single(await read.Set<RuntimeHistoryActivityEntity>().ToArrayAsync());
        Assert.Single(await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync()); Assert.Equal(2, await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().CountAsync());
        Assert.Equal(2, await read.Set<RuntimeHistoryOccurrenceCounterEntity>().CountAsync()); Assert.Single(await read.WorkflowExecutionStates.ToArrayAsync());
    }

    [Fact]
    public async Task Access_retirement_after_first_bulk_delete_refuses_and_rolls_back_the_pending_result()
    {
        await using var database = await BookmarkHistoryAsync(); var access = new Accessor();
        await using (var context = database.Open(new RetireDeleteAccess(access)))
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => StageDelete(context, access)); await transaction.RollbackAsync();
        }
        await using var read = database.Open(); Assert.Equal(2, await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().CountAsync());
        Assert.Single(await read.Set<RuntimeHistoryHeadEntity>().ToArrayAsync()); Assert.Equal(4, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
    }

    [Fact]
    public async Task Full_retirement_uses_constant_set_based_work_without_loading_journal_rows()
    {
        await using var database = await CapturedDatabaseAsync();
        var candidates = Enumerable.Range(0, 700).Select(index => Candidate("long", "event-" + index, index)).ToArray();
        await PersistAsync(database, Commit("long", [Batch("long", candidates)]));
        var commands = new RetirementCommands(); await using var context = database.Open(commands);
        await using var transaction = await context.Database.BeginTransactionAsync(); await StageDelete(context);
        Assert.Equal(8, commands.NonQueries); Assert.Equal(0, commands.Reads); Assert.Empty(context.ChangeTracker.Entries());
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 701);
    }

    private static Task<long> StagePurge(HistoryContext context, long through, Accessor? access = null) =>
        EfRuntimeHistoryRetention.StagePurgeAsync(context, access ?? new(), Scope, Run, through, Chain, CancellationToken.None).AsTask();
    private static Task StageDelete(HistoryContext context, Accessor? access = null) =>
        EfRuntimeHistoryRetention.StageDeleteAsync(context, access ?? new(), Scope, Run, Chain, CancellationToken.None).AsTask();
    private static async Task<long> PurgeAsync(Database database, long through)
    {
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        var staged = await StagePurge(context, through); await context.SaveChangesAsync(); await transaction.CommitAsync(); return staged;
    }
    private static async Task<Database> BookmarkHistoryAsync()
    {
        var database = await Database.CreateAsync(); var bookmark = Bookmark();
        await PersistAsync(database, Commit("native", [Batch("logical", Candidate("logical", "started", 0),
            Candidate("logical", "created", 1, new(bookmark, RuntimeHistoryBookmarkStatus.Active)),
            Candidate("logical", "consumed", 2, new(bookmark, RuntimeHistoryBookmarkStatus.Consumed), knownTime: true),
            Candidate("logical", "completed", 3, name: "ActivityCompleted"))]));
        return database;
    }
    private static async Task SeedOtherAsync(Database database, string scope, string run)
    {
        await using var context = database.Open(); var state = Workflow(Policy) with { WorkflowExecutionId = run, TenantId = scope };
        context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, scope, EfWorkflowExecutionStateStore.CreateId(scope, run), 1));
        await context.SaveChangesAsync(); await using var transaction = await context.Database.BeginTransactionAsync();
        var item = new RuntimeHistoryCandidate(new("event", run, 0, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", Now,
            "activity", checkpointId: "logical") { Authority = RuntimeHistoryAuthority.CommittedTransition, ActivityAncestryKnown = true }, 0);
        var commit = new RuntimeCheckpointCommit("native", new("logical", "control", run, Now, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()) { HistoryBatches = [Batch("logical", item)] };
        var access = new Accessor { Current = PersistenceAccessContext.Scoped(new(scope)) };
        await EfRuntimeHistoryCheckpoint.StageAsync(context, access, commit, scope, Now, Chain, CancellationToken.None);
        await context.SaveChangesAsync(); await transaction.CommitAsync();
    }
    private sealed class RetirementCommands : DbCommandInterceptor
    {
        internal int Reads; internal int NonQueries;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Reads++; return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { NonQueries++; return ValueTask.FromResult(result); }
    }

    private sealed class RetireDeleteAccess(Accessor access) : DbCommandInterceptor
    {
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE", StringComparison.Ordinal)) access.Current = PersistenceAccessContext.Scoped(new("other"));
            return ValueTask.FromResult(result);
        }
    }
}
