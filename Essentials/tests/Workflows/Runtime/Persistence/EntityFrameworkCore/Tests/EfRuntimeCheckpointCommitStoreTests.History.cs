using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    private static readonly RuntimeHistoryCapturePolicy NativeHistoryPolicy = new("native-history", 1, true,
        ["ActivityStarted", "BookmarkCreated"]);

    [Fact]
    public async Task Native_checkpoint_commits_bookmark_journal_membership_and_marker_then_replays_original_positions()
    {
        await using var database = await TestDatabase.CreateAsync(); await SeedNativeHistoryAsync(database);
        var commit = NativeHistoryCommit("captured"); var recordedAt = OccurredAt.AddMinutes(1);
        await using (var context = database.Open("tenant-a"))
        {
            var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(recordedAt));
            await store.CommitAsync(commit, Decision()); await store.CommitAsync(commit, Decision());
            // The same logical sidecar under another physical boundary cannot spend its positions again.
            await store.CommitAsync(Commit("later-physical") with { HistoryBatches = commit.HistoryBatches }, Decision());
        }
        await using var read = database.Open("tenant-a"); var mapping = new EfRuntimeHistoryProjectionMapping(RuntimeHistoryEfModule.Chain);
        var head = mapping.Read(await read.Set<RuntimeHistoryHeadEntity>().SingleAsync(), "tenant-a", "workflow-a");
        Assert.Equal(2, head.LastSequence); Assert.Equal("later-physical", head.LastPhysicalCommitId);
        var rows = await read.Set<RuntimeHistoryEventEntity>().OrderBy(row => row.Sequence).ToArrayAsync();
        Assert.Equal(new long[] { 1, 2 }, rows.Select(row => row.Sequence));
        Assert.All(rows, row => { var item = mapping.Read(row, "tenant-a", "workflow-a", head);
            Assert.Equal("captured", item.PhysicalCommitId); Assert.Equal(recordedAt, item.RecordedAt); Assert.False(item.OccurredAtKnown); });
        Assert.Single(await read.Bookmarks.ToArrayAsync()); Assert.Single(await read.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync());
        Assert.Single(await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.Equal(2, await read.RuntimeCheckpointCommits.CountAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Native_writer_refuses_missing_required_history_with_or_without_a_prior_captured_head(bool priorHead)
    {
        await using var database = await TestDatabase.CreateAsync(); await SeedNativeHistoryAsync(database);
        if (priorHead)
        {
            await using var first = database.Open("tenant-a");
            await new EfRuntimeCheckpointCommitStore(first, new FixedAccessor("tenant-a")).CommitAsync(NativeHistoryCommit("first"), Decision());
        }
        await using (var context = database.Open("tenant-a"))
        {
            var uncaptured = WithBookmark("missing", CheckpointBookmark("uncaptured", "payload"), RuntimeStateChangeOperation.Upsert);
            var error = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
                new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(uncaptured, Decision()).AsTask());
            Assert.Contains("required history", error.Message, StringComparison.OrdinalIgnoreCase); Assert.Empty(context.ChangeTracker.Entries());
        }
        await using var read = database.Open("tenant-a");
        Assert.Equal(priorHead ? 1 : 0, await read.Bookmarks.CountAsync()); Assert.Equal(priorHead ? 1 : 0, await read.RuntimeCheckpointCommits.CountAsync());
        Assert.Equal(priorHead ? 2 : 0, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
    }

    [Fact]
    public async Task Legacy_uncaptured_checkpoint_and_original_marker_replay_do_not_create_history()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var seed = database.Open("tenant-a"))
        {
            seed.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(Execution("workflow-a", "tenant-a"),
                "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "workflow-a"), 1)); await seed.SaveChangesAsync();
            var store = new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a"));
            await store.CommitAsync(Commit("legacy"), Decision()); await store.CommitAsync(Commit("legacy"), Decision());
        }
        await using var read = database.Open("tenant-a"); Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Empty(await read.Set<RuntimeHistoryHeadEntity>().ToArrayAsync()); Assert.Empty(await read.Set<RuntimeHistoryEventEntity>().ToArrayAsync());
    }

    [Fact]
    public async Task Optional_missing_contribution_commits_an_explicit_gap_with_the_native_marker()
    {
        var optional = new RuntimeHistoryCapturePolicy(NativeHistoryPolicy.PolicyId, 1, false, NativeHistoryPolicy.SupportedKinds);
        await using var database = await TestDatabase.CreateAsync(); await SeedNativeHistoryAsync(database, optional);
        await using (var context = database.Open("tenant-a"))
            await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(Commit("optional"), Decision());
        await using var read = database.Open("tenant-a");
        var head = new EfRuntimeHistoryProjectionMapping(RuntimeHistoryEfModule.Chain).Read(await read.Set<RuntimeHistoryHeadEntity>().SingleAsync(), "tenant-a", "workflow-a");
        Assert.True(head.HasGaps); Assert.False(head.CapturedFromStart); Assert.Equal(0, head.LastSequence);
        Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Native_marker_insert_failure_rolls_back_history_head_bookmark_and_occurrence_then_allows_clean_retry()
    {
        await using var database = await TestDatabase.CreateAsync(); await SeedNativeHistoryAsync(database);
        var failure = new FailMarkerInsertInterceptor(); var commit = NativeHistoryCommit("marker-failure");
        await using (var context = database.Open("tenant-a", failure))
        {
            failure.Arm(); var error = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() =>
                new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision()).AsTask());
            Assert.IsType<DbUpdateException>(error.InnerException); Assert.Equal("workflow-a", error.WorkflowExecutionId);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        await AssertNoNativeHistoryAsync(database);
        await using (var retry = database.Open("tenant-a"))
            await new EfRuntimeCheckpointCommitStore(retry, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());
        await using var read = database.Open("tenant-a"); Assert.Equal(2, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
        Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync()); Assert.Single(await read.Bookmarks.ToArrayAsync());
    }

    [Fact]
    public async Task Native_derived_projection_failure_cannot_leave_journal_bookmark_or_marker_committed()
    {
        await using var database = await TestDatabase.CreateAsync(); await SeedNativeHistoryAsync(database);
        await using (var context = database.Open("tenant-a"))
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_native_history_member BEFORE INSERT ON elsa_runtime_history_occurrence_member
                BEGIN SELECT RAISE(ABORT, 'controlled native history membership failure'); END;
                """);
            var error = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(NativeHistoryCommit("projection-failure"), Decision()).AsTask());
            Assert.IsType<DbUpdateException>(error.InnerException); Assert.Equal("workflow-a", error.WorkflowExecutionId);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        await AssertNoNativeHistoryAsync(database);
    }

    [Fact]
    public async Task Lost_native_commit_acknowledgement_reconciles_original_history_through_the_marker()
    {
        await using var database = await TestDatabase.CreateAsync(); await SeedNativeHistoryAsync(database);
        var failure = new CommitAcknowledgementLossInterceptor(); var commit = NativeHistoryCommit("ambiguous");
        await using (var context = database.Open("tenant-a", failure))
        {
            failure.Arm(); var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
            await store.CommitAsync(commit, Decision()); await store.CommitAsync(commit, Decision());
        }
        await using var read = database.Open("tenant-a"); Assert.Equal(2, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
        Assert.Equal(2, (await read.Set<RuntimeHistoryHeadEntity>().SingleAsync()).LastSequence);
        Assert.Single(await read.Bookmarks.ToArrayAsync()); Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Single(await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
    }

    [Fact]
    public async Task Head_reservation_contention_is_unavailable_with_its_cause_after_marker_reconciliation()
    {
        await using var database = await TestDatabase.CreateAsync(); await SeedNativeHistoryAsync(database); var first = NativeHistoryCommit("first");
        await using (var context = database.Open("tenant-a"))
        {
            var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")); await store.CommitAsync(first, Decision());
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER ignore_history_reservation BEFORE UPDATE ON elsa_runtime_history_head
                BEGIN SELECT RAISE(IGNORE); END;
                """);
            var next = Commit("contended") with { HistoryBatches = first.HistoryBatches };
            var error = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => store.CommitAsync(next, Decision()).AsTask());
            Assert.Equal("workflow-a", error.WorkflowExecutionId); Assert.Contains("allocating", error.Operation, StringComparison.Ordinal);
            Assert.IsType<EfRuntimeHistoryCheckpoint.HeadAllocationConflictException>(error.InnerException); Assert.Empty(context.ChangeTracker.Entries());
        }
        await using var read = database.Open("tenant-a"); Assert.Equal(2, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
        Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync()); Assert.Single(await read.Bookmarks.ToArrayAsync());
        Assert.Equal("first", new EfRuntimeHistoryProjectionMapping(RuntimeHistoryEfModule.Chain)
            .Read(await read.Set<RuntimeHistoryHeadEntity>().SingleAsync(), "tenant-a", "workflow-a").LastPhysicalCommitId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task History_never_admits_a_stale_fence_but_an_existing_marker_still_wins(bool committedFirst)
    {
        await using var database = await TestDatabase.CreateAsync(); var clock = new FixedTimeProvider(OccurredAt);
        await using (var context = database.Open("tenant-a"))
        {
            var access = new FixedAccessor("tenant-a");
            var lease = await BackgroundActivityWorkTestSupport.SeedLiveExecutionAsync(context, access, "workflow-a", "tenant-a", clock);
            // A controlled admission fixture, before the first history checkpoint; no running-policy
            // mutation or whole-host capture qualification is exercised by this direct-store control.
            var row = await context.WorkflowExecutionStates.SingleAsync();
            var state = EfWorkflowExecutionStateStore.ReadChecked(row, "tenant-a", "workflow-a") with
            { HistoryCapturePolicy = NativeHistoryPolicy, HistoryCaptureEnabledAtAdmission = true };
            row.ContentJson = RuntimeArtifactJson.Serialize(state); row.Revision++; await context.SaveChangesAsync();
            var store = new EfRuntimeCheckpointCommitStore(context, access, clock);
            var commit = NativeHistoryCommit("fenced") with { ExpectedFence = lease.ToFence() };
            if (committedFirst) await store.CommitAsync(commit, Decision());
            var stale = commit with { ExpectedFence = new("wrong-lease", "wrong-owner", 1) };
            if (committedFirst) await store.CommitAsync(stale, Decision());
            else await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(stale, Decision()).AsTask());
        }
        if (!committedFirst) await AssertNoNativeHistoryAsync(database);
        else
        {
            await using var read = database.Open("tenant-a"); Assert.Equal(2, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
            Assert.Single(await read.Bookmarks.ToArrayAsync()); Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
        }
    }

    [Fact]
    public async Task Confirmed_uncaptured_native_work_commits_when_only_history_head_storage_is_unavailable()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var context = database.Open("tenant-a"))
        {
            var state = Execution("workflow-a", "tenant-a") with { HistoryCaptureEnabledAtAdmission = false };
            context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "workflow-a"), 1));
            await context.SaveChangesAsync(); await context.Database.ExecuteSqlRawAsync("DROP TABLE elsa_runtime_history_head;");
            var ordinary = WithBookmark("uncaptured-available", CheckpointBookmark("ordinary", "payload"), RuntimeStateChangeOperation.Upsert);
            await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(ordinary, Decision());
        }
        await using var read = database.Open("tenant-a"); Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Single(await read.Bookmarks.ToArrayAsync()); Assert.Empty(await read.Set<RuntimeHistoryEventEntity>().ToArrayAsync());
    }

    [Fact]
    public async Task Optional_history_head_failure_retains_its_typed_provider_cause_without_a_batch()
    {
        var optional = new RuntimeHistoryCapturePolicy(NativeHistoryPolicy.PolicyId, 1, false, NativeHistoryPolicy.SupportedKinds);
        await using var database = await TestDatabase.CreateAsync(); await SeedNativeHistoryAsync(database, optional);
        await using (var context = database.Open("tenant-a"))
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE elsa_runtime_history_head;");
            var error = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() =>
                new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(Commit("optional-unavailable"), Decision()).AsTask());
            Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(error.InnerException); Assert.Equal("workflow-a", error.WorkflowExecutionId);
        }
        await using var read = database.Open("tenant-a"); Assert.Empty(await read.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Empty(await read.Bookmarks.ToArrayAsync()); Assert.Empty(await read.Set<RuntimeHistoryEventEntity>().ToArrayAsync());
    }

    [Fact]
    public async Task Captured_write_and_reconciliation_failures_keep_both_causes_at_the_public_boundary()
    {
        await using var database = await TestDatabase.CreateAsync(); await SeedNativeHistoryAsync(database);
        var insert = new FailMarkerInsertInterceptor();
        await using (var context = database.Open("tenant-a", insert, new FailHistoryMarkerLookup()))
        {
            insert.Arm(); var error = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() =>
                new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(NativeHistoryCommit("lookup-failure"), Decision()).AsTask());
            var causes = Causes(error).ToArray(); Assert.Contains(causes, cause => cause is DbUpdateException);
            Assert.Contains(causes, cause => cause is TimeoutException); Assert.Equal("workflow-a", error.WorkflowExecutionId);
        }
        await AssertNoNativeHistoryAsync(database);

        static IEnumerable<Exception> Causes(Exception error)
        {
            yield return error;
            if (error is AggregateException group)
            {
                foreach (var inner in group.InnerExceptions)
                foreach (var cause in Causes(inner)) yield return cause;
            }
            else if (error.InnerException is { } inner)
                foreach (var cause in Causes(inner)) yield return cause;
        }
    }

    private sealed class FailHistoryMarkerLookup : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.Ordinal))
                throw new TimeoutException("Controlled history marker reconciliation failure.");
            return ValueTask.FromResult(result);
        }
    }

    private static async Task SeedNativeHistoryAsync(TestDatabase database, RuntimeHistoryCapturePolicy? policy = null)
    {
        await using var seed = database.Open("tenant-a");
        var state = Execution("workflow-a", "tenant-a") with { HistoryCapturePolicy = policy ?? NativeHistoryPolicy, HistoryCaptureEnabledAtAdmission = true };
        seed.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "workflow-a"), 1));
        await seed.SaveChangesAsync();
    }
    private static RuntimeCheckpointCommit NativeHistoryCommit(string id)
    {
        var bookmark = CheckpointBookmark("history-bookmark", "payload"); var commit = WithBookmark(id, bookmark, RuntimeStateChangeOperation.Upsert);
        var started = new RuntimeHistoryCandidate(new("started", "workflow-a", 0, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", OccurredAt,
            "activity-a", checkpointId: commit.Checkpoint.CheckpointId) { Authority = RuntimeHistoryAuthority.CommittedTransition,
            OccurredAtKnown = false, ActivityAncestryKnown = true }, 0);
        var created = new RuntimeHistoryCandidate(new("created", "workflow-a", 0, RuntimeHistoryEventCategory.BookmarkLifecycle, "BookmarkCreated", OccurredAt,
            "activity-a", checkpointId: commit.Checkpoint.CheckpointId) { Authority = RuntimeHistoryAuthority.CommittedTransition,
            OccurredAtKnown = false, BookmarkId = bookmark.BookmarkId }, 1, new(ActivityExecutionBookmarkSummary.From(bookmark), RuntimeHistoryBookmarkStatus.Active));
        return commit with { HistoryBatches = [new(NativeHistoryPolicy, commit.Checkpoint.CheckpointId, OccurredAt, true, [started, created])] };
    }
    private static async Task AssertNoNativeHistoryAsync(TestDatabase database)
    {
        await using var read = database.Open("tenant-a"); Assert.Empty(await read.RuntimeCheckpointCommits.ToArrayAsync()); Assert.Empty(await read.Bookmarks.ToArrayAsync());
        Assert.Empty(await read.Set<RuntimeHistoryHeadEntity>().ToArrayAsync()); Assert.Empty(await read.Set<RuntimeHistoryEventEntity>().ToArrayAsync());
        Assert.Empty(await read.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync()); Assert.Empty(await read.Set<RuntimeHistoryBookmarkCountEntity>().ToArrayAsync());
        Assert.Empty(await read.Set<RuntimeHistoryActivityEntity>().ToArrayAsync()); Assert.Empty(await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.Empty(await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().ToArrayAsync()); Assert.Empty(await read.Set<RuntimeHistoryOccurrenceCounterEntity>().ToArrayAsync());
        Assert.Single(await read.WorkflowExecutionStates.ToArrayAsync());
    }
}
