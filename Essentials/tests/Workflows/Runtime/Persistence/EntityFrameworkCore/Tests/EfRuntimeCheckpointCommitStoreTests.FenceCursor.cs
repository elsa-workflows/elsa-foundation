using System.Data.Common;
using System.Reflection;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// #2564: a fenced commit whose lease has a usable fence cursor does not read the ownership row. Its save writes the
/// revision bump it would have written after reading. Every other case reads the row, validates it and bumps it at
/// save, as before. Each test counts the commands the commit ran against the ownership table: a read is a SELECT, a
/// write is an UPDATE.
/// </summary>
public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    private const string CursorScope = "tenant-a";
    private const string CursorRun = "workflow-a";

    [Fact]
    public async Task Fenced_commit_with_a_usable_cursor_does_not_read_the_ownership_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var acquired = await OwnershipRevisionAsync(database);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var store = CursorStore(context, cursors, clock);

        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-2", lease)));

        Assert.Equal(acquired + 2, await OwnershipRevisionAsync(database));
        Assert.Equal(new ExecutionFenceCursor(acquired + 2, lease.ExpiresAt, RuntimeOperationalStateEfModule.SchemaVersion), Cursor(cursors, lease));
        // The bump was staged from the cursor, not from the row, and is detached after the save.
        Assert.Empty(context.ChangeTracker.Entries<ExecutionLivenessStateEntity>());
        await using var read = database.Open(CursorScope);
        Assert.Equal(2, await read.RuntimeCheckpointCommits.CountAsync());
        // Only the revision changed, so the owner's next heartbeat still finds its lease.
        Assert.True((await OwnershipNode(read, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
    }

    [Theory]
    [InlineData("store-has-no-cursors")]
    [InlineData("lease-has-no-cursor")]
    public async Task Fenced_commit_without_a_cursor_reads_the_row_and_creates_no_cursor(string because)
    {
        // A store built without cursors, or a lease no liveness store of this process wrote: one written by another
        // node, or by a store that is not the EF store. The commit never makes a cursor of its own for such a lease.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        RuntimeExecutionLease lease;
        await using (var seed = database.Open(CursorScope))
            lease = await OwnershipNode(seed, clock, "node-a").AcquireAsync(CursorRun);
        var acquired = await OwnershipRevisionAsync(database);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var store = CursorStore(context, because == "store-has-no-cursors" ? null : cursors, clock);

        Assert.Equal((1, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));
        Assert.Equal((1, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-2", lease)));

        Assert.Equal(0, cursors.Count);
        Assert.Equal(acquired + 2, await OwnershipRevisionAsync(database));
    }

    [Fact]
    public async Task Fenced_commit_for_a_run_that_was_never_owned_is_refused_after_reading_no_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var never = new RuntimeExecutionLease("lease-never", CursorRun, "node-a", OccurredAt, OccurredAt.AddMinutes(1), 1);

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => CursorStore(context, cursors, clock).CommitAsync(FencedCommit("commit-refused", never), Decision()).AsTask());

        Assert.Equal(RuntimeFencingRejectionReason.NoActiveLease, refusal.Reason);
        Assert.Equal((1, 0), OwnershipCommands(capture, 0));
        Assert.Equal(0, cursors.Count);
    }

    [Fact]
    public async Task Fenced_commit_after_a_heartbeat_it_did_not_hear_of_reads_the_row_once_and_not_again()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        clock.Advance(TimeSpan.FromSeconds(20));
        await using (var other = database.Open(CursorScope))
            Assert.True((await OwnershipNode(other, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
        var renewed = await OwnershipRevisionAsync(database);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var store = CursorStore(context, cursors, clock);

        // The bump staged from the cursor matches no row. The commit runs again: it reads and validates the row,
        // and its save bumps it.
        Assert.Equal((1, 2), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));
        Assert.Equal(new ExecutionFenceCursor(renewed + 1, lease.ExpiresAt.AddSeconds(20), RuntimeOperationalStateEfModule.SchemaVersion), Cursor(cursors, lease));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-2", lease)));

        Assert.Equal(renewed + 2, await OwnershipRevisionAsync(database));
    }

    [Theory]
    [InlineData("taken-over", RuntimeFencingRejectionReason.StaleToken)]
    [InlineData("taken-over-by-its-own-owner-at-the-same-instant", RuntimeFencingRejectionReason.StaleToken)]
    [InlineData("borrowed", RuntimeFencingRejectionReason.StaleToken)]
    [InlineData("released", RuntimeFencingRejectionReason.NoActiveLease)]
    [InlineData("revoked", RuntimeFencingRejectionReason.NoActiveLease)]
    [InlineData("expired", RuntimeFencingRejectionReason.ExpiredLease)]
    public async Task Fenced_commit_whose_lease_is_gone_is_refused_with_the_reason_the_row_gives_and_writes_nothing(
        string because, RuntimeFencingRejectionReason expectedReason)
    {
        // The cursor still says the lease is held: nothing told this process otherwise, as when another node
        // takes the run. The refusal and its reason come from reading the row.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        if (because == "revoked")
            await SeedWorkflowRowAsync(database, clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var cursor = Cursor(cursors, lease);
        await using (var other = database.Open(CursorScope))
        {
            switch (because)
            {
                case "taken-over":
                    await OwnershipNode(other, clock, "node-b").AcquireAsync(CursorRun);
                    break;
                case "taken-over-by-its-own-owner-at-the-same-instant":
                    // The new lease has the owner and the lease times of the old one. The revision is what tells
                    // the two leases apart.
                    await OwnershipNode(other, clock, "node-a").AcquireAsync(CursorRun);
                    break;
                case "borrowed":
                    await BorrowAsync(OwnershipNode(other, clock, "node-b"));
                    break;
                case "released":
                    Assert.True((await OwnershipNode(other, clock, "node-a").ReleaseAsync(lease)).Succeeded);
                    break;
                case "revoked":
                    Assert.True(await new EfWorkflowExecutionStateStore(other, new FixedAccessor(CursorScope), new NoopContinuationCodec()).DeleteAsync(CursorRun));
                    break;
                default:
                    clock.Advance(TimeSpan.FromMinutes(2));
                    break;
            }
        }
        var before = await OwnershipRevisionAsync(database);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);

        var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
            () => CursorStore(context, cursors, clock).CommitAsync(FencedCommit("commit-refused", lease), Decision()).AsTask());

        Assert.Equal(expectedReason, refusal.Reason);
        // A cursor past its lease time is not tried, so an expired lease that nobody touched is not admitted.
        Assert.Equal(because == "expired" ? (1, 0) : (1, 1), OwnershipCommands(capture, 0));
        // A cursor the row had left is marked unknown. It is never moved to a revision the lease did not hold.
        Assert.Equal(because == "expired" ? cursor : cursor with { Revision = 0 }, Cursor(cursors, lease));
        Assert.Equal(before, await OwnershipRevisionAsync(database));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        await using var read = database.Open(CursorScope);
        Assert.Empty(await read.RuntimeCheckpointCommits.Where(row => row.CommitId == EfRelationalIdentity.Encode("commit-refused")).ToArrayAsync());
    }

    [Fact]
    public async Task Fenced_commit_with_a_cursor_past_its_lease_time_reads_the_row_and_is_admitted_when_the_lease_was_renewed()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        clock.Advance(TimeSpan.FromSeconds(40));
        await using (var other = database.Open(CursorScope))
            Assert.True((await OwnershipNode(other, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
        var renewed = await OwnershipRevisionAsync(database);
        // Past the lease time the cursor knows, inside the one the row has.
        clock.Advance(TimeSpan.FromSeconds(30));
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var store = CursorStore(context, cursors, clock);

        Assert.Equal((1, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));

        Assert.Equal(new ExecutionFenceCursor(renewed + 1, lease.ExpiresAt.AddSeconds(40), RuntimeOperationalStateEfModule.SchemaVersion), Cursor(cursors, lease));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-2", lease)));
    }

    [Fact]
    public async Task A_commit_that_fails_before_its_transaction_commits_does_not_move_the_cursor()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var cursor = Cursor(cursors, lease);
        var capture = new CommandCaptureInterceptor();
        var failure = new FailMarkerInsertInterceptor();
        await using var context = database.Open(CursorScope, capture, failure);
        var store = CursorStore(context, cursors, clock);
        failure.Arm();

        await Assert.ThrowsAsync<DbUpdateException>(() => store.CommitAsync(FencedCommit("commit-failed", lease), Decision()).AsTask());

        // The bump was rolled back with the rest, so the cursor still names the row.
        Assert.Equal(cursor, Cursor(cursors, lease));
        Assert.Equal(cursor.Revision, await OwnershipRevisionAsync(database));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-failed", lease)));
        Assert.Equal(cursor with { Revision = cursor.Revision + 1 }, Cursor(cursors, lease));
    }

    [Fact]
    public async Task A_commit_whose_outcome_is_not_known_distrusts_the_cursor_and_the_next_commit_reads_the_row()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var cursor = Cursor(cursors, lease);
        var capture = new CommandCaptureInterceptor();
        var acknowledgementLoss = new CommitAcknowledgementLossInterceptor();
        await using var context = database.Open(CursorScope, capture, acknowledgementLoss);
        var store = CursorStore(context, cursors, clock);
        acknowledgementLoss.Arm();

        // The commit was applied and its acknowledgement was lost. The marker settles the commit, not the cursor.
        await store.CommitAsync(FencedCommit("commit-acknowledgement-lost", lease), Decision());

        Assert.Equal(cursor with { Revision = 0 }, Cursor(cursors, lease));
        Assert.Equal(cursor.Revision + 1, await OwnershipRevisionAsync(database));
        // No bump is staged from a revision that is not known.
        Assert.Equal((1, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-next", lease)));
        Assert.Equal(cursor with { Revision = cursor.Revision + 2 }, Cursor(cursors, lease));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-after", lease)));
    }

    [Theory]
    [InlineData("heartbeat-report-before-the-commit")]
    [InlineData("heartbeat-report-after-the-commit")]
    [InlineData("commit-report-after-the-heartbeat-report")]
    public async Task A_heartbeat_and_a_commit_whose_cursor_writes_cross_end_in_no_read_or_one_read(string order)
    {
        // The heartbeat writes the row and records its cursor from another service scope, so the two cursor writes
        // can arrive in either order around a commit. Each order is replayed here step by step.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var acquired = Cursor(cursors, lease);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var store = CursorStore(context, cursors, clock);
        clock.Advance(TimeSpan.FromSeconds(20));
        var renewedExpiry = lease.ExpiresAt.AddSeconds(20);

        async Task<ExecutionFenceCursor> HeartbeatWithoutReportAsync()
        {
            await using var other = database.Open(CursorScope);
            Assert.True((await OwnershipNode(other, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
            return new(await OwnershipRevisionAsync(database), renewedExpiry, RuntimeOperationalStateEfModule.SchemaVersion);
        }

        switch (order)
        {
            case "heartbeat-report-before-the-commit":
                cursors.Hold(CursorKey(lease), await HeartbeatWithoutReportAsync());
                Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));
                break;
            case "heartbeat-report-after-the-commit":
                var late = await HeartbeatWithoutReportAsync();
                Assert.Equal((1, 2), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));
                // The report names a revision the row has left. The commit's cursor is ahead and stays.
                cursors.Hold(CursorKey(lease), late);
                break;
            default:
                Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));
                cursors.Hold(CursorKey(lease), await HeartbeatWithoutReportAsync());
                // The commit's own report, arriving after the heartbeat's: it is behind and is dropped.
                cursors.Advance(CursorKey(lease), acquired with { Revision = acquired.Revision + 1 });
                break;
        }

        Assert.Equal(new ExecutionFenceCursor(acquired.Revision + 2, renewedExpiry, RuntimeOperationalStateEfModule.SchemaVersion), Cursor(cursors, lease));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-2", lease)));
        Assert.Equal(acquired.Revision + 3, await OwnershipRevisionAsync(database));
    }

    [Fact]
    public async Task Fenced_commit_that_admits_background_work_reads_the_ownership_row_even_with_a_current_cursor()
    {
        // Background admission checks the run's deletion tombstone on the ownership row the fence step tracked.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        RuntimeExecutionLease owner;
        await using (var seed = database.Open(CursorScope))
            owner = await BackgroundActivityWorkTestSupport.SeedLiveExecutionAsync(seed, new FixedAccessor(CursorScope), CursorRun, "tenant-a", clock);
        var acquired = new ExecutionFenceCursor(await OwnershipRevisionAsync(database), owner.ExpiresAt, RuntimeOperationalStateEfModule.SchemaVersion);
        cursors.Hold(CursorKey(owner), acquired);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor(CursorScope), clock) { FenceCursors = cursors };
        var admission = FencedCommit("commit-background-admission", owner);
        admission = admission with
        {
            StateChanges = admission.StateChanges.WithBackgroundActivityWork(
                new BackgroundActivityWorkCheckpointChanges(admissions: [BackgroundRequest("work-cursor")]))
        };

        Assert.Equal((1, 1), await CommitAndCountAsync(store, capture, admission));

        Assert.Single(await context.BackgroundActivityWorks.AsNoTracking().ToArrayAsync());
        Assert.Equal(acquired with { Revision = acquired.Revision + 1 }, Cursor(cursors, owner));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-after-admission", owner)));
    }

    [Fact]
    public async Task Fenced_commit_reads_the_ownership_row_when_the_context_holds_a_pending_change_to_it()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var acquired = Cursor(cursors, lease);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        // A change a caller staged on the context. The tracked save writes it together with the fence bump.
        context.Entry(await context.ExecutionLivenessStates.SingleAsync()).State = EntityState.Modified;

        Assert.Equal((1, 1), await CommitAndCountAsync(CursorStore(context, cursors, clock), capture, FencedCommit("commit-1", lease)));

        Assert.Equal(acquired.Revision + 1, await OwnershipRevisionAsync(database));
        Assert.Equal(acquired with { Revision = acquired.Revision + 1 }, Cursor(cursors, lease));
    }

    [Fact]
    public async Task A_poison_record_under_the_fence_costs_the_next_commit_one_read_of_the_ownership_row()
    {
        // The poison store reads, validates and bumps the ownership row and knows nothing of cursors. Its bump
        // leaves the cursor behind the row, which the next commit finds out when its save matches no row.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var acquired = Cursor(cursors, lease);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var accessor = new FixedAccessor(CursorScope);
        var liveness = new EfExecutionLivenessStateStore(context, accessor, new NoopContinuationCodec());
        var poison = new EfWorkflowSchedulerPoisonStore(context, accessor, () => liveness, clock);
        var store = CursorStore(context, cursors, clock);

        await poison.RecordFencedAsync(
            new RuntimeSchedulerPoisonRecord(CursorRun, "work-1", WorkflowExecutionCommandKind.RunSchedulerWork, "handler-1",
                new RuntimeFaultInfo("System.InvalidOperationException", "boom", "stack"), 1, RuntimeSchedulerPoisonDisposition.Poisoned,
                OccurredAt, OccurredAt, null, new Dictionary<string, string>(), null),
            lease.ToFence());

        Assert.Equal(acquired, Cursor(cursors, lease));
        Assert.Equal((1, 2), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));
        Assert.Equal(acquired with { Revision = acquired.Revision + 2 }, Cursor(cursors, lease));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-2", lease)));
    }

    [Fact]
    public async Task A_commit_with_a_usable_cursor_runs_the_commands_of_one_that_reads_the_row_minus_the_read()
    {
        // The save writes the bump in both cases, so it is the same statement at the same place among the save's
        // commands. That keeps the order in which a commit locks its rows as it is without a cursor.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        await SeedWorkflowRowAsync(database, clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);

        async Task<string[]> CommandsOfAsync(string commitId, EfExecutionFenceCursors? cursorsOfTheStore)
        {
            var capture = new CommandCaptureInterceptor();
            await using var context = database.Open(CursorScope, capture);
            await CursorStore(context, cursorsOfTheStore, clock).CommitAsync(WorkflowRowCommit(commitId, lease), Decision());
            return capture.Commands.ToArray();
        }

        // The first commit brings the workflow row to the shape the next two change in the same way.
        await CommandsOfAsync("commit-warm-up", cursors);
        var byCursor = await CommandsOfAsync("commit-by-cursor", cursors);
        var reading = await CommandsOfAsync("commit-reading", null);

        var read = Assert.Single(reading, IsOwnershipRead);
        Assert.DoesNotContain(byCursor, IsOwnershipRead);
        Assert.Single(byCursor, IsOwnershipWrite);
        Assert.Equal(reading.Where(command => !ReferenceEquals(command, read)), byCursor);
    }

    [Fact]
    public async Task The_staged_bump_does_not_stay_in_the_context_and_later_reads_return_the_real_row()
    {
        // The bump is staged as a stand-in that carries the row's key and revision only. Nobody may be handed it.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var acquired = await OwnershipRevisionAsync(database);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var store = CursorStore(context, cursors, clock);

        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));

        Assert.Empty(context.ChangeTracker.Entries<ExecutionLivenessStateEntity>());
        var untracked = await context.ExecutionLivenessStates.AsNoTracking().SingleAsync();
        var tracked = await context.ExecutionLivenessStates.SingleAsync();
        Assert.All(new[] { untracked, tracked }, row =>
        {
            Assert.Equal(acquired + 1, row.Revision);
            Assert.Equal(lease.LeaseId, EfExecutionLivenessStateStore.Read(row, CursorScope, CursorRun, RuntimeExecutionOwnershipStateId.For(CursorRun)).ExecutionLease!.LeaseId);
        });

        // And after a save in which the bump matched no row: what the context then tracks is the row that was read.
        context.ChangeTracker.Clear();
        await using (var other = database.Open(CursorScope))
            Assert.True((await OwnershipNode(other, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
        Assert.Equal((1, 2), await CommitAndCountAsync(store, capture, FencedCommit("commit-2", lease)));
        Assert.All(context.ChangeTracker.Entries<ExecutionLivenessStateEntity>(), entry => Assert.NotNull(entry.Entity.ContentJson));
    }

    [Fact]
    public async Task The_staged_bump_shows_a_save_interceptor_the_rows_schema_stamp_and_changes_only_the_revision()
    {
        // In a composed host the schema write gate looks at the stamp of every row a save changes. The staged bump
        // must show it the stamp the row has, as the row itself would, and must write nothing but the revision.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        string storedStamp;
        await using (var read = database.Open(CursorScope))
            storedStamp = (await read.ExecutionLivenessStates.AsNoTracking().SingleAsync()).SchemaVersion;
        var seen = new OwnershipWritesAtSave();
        await using var context = database.Open(CursorScope, seen);

        await CursorStore(context, cursors, clock).CommitAsync(FencedCommit("commit-1", lease), Decision());

        var write = Assert.Single(seen.Writes);
        Assert.Equal(storedStamp, write.Stamp);
        Assert.Equal(storedStamp, Cursor(cursors, lease).SchemaVersion);
        Assert.Equal([nameof(ExecutionLivenessStateEntity.Revision)], write.Modified);
    }

    [Fact]
    public async Task A_cursor_that_is_behind_does_not_take_one_of_the_renewal_attempts()
    {
        // A commit that reads the row gets three attempts when the owner's heartbeat lands under it. A cursor that
        // is behind costs one more run, which must not be one of those three.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        await using (var other = database.Open(CursorScope))
            Assert.True((await OwnershipNode(other, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
        var races = new OwnerHeartbeatRaces(EfRuntimeCheckpointParticipantStaging.MaxFenceRenewalAttempts - 1);
        await using var context = database.Open(CursorScope, races);

        await CursorStore(context, cursors, clock).CommitAsync(FencedCommit("commit-1", lease), Decision());

        Assert.Equal(EfRuntimeCheckpointParticipantStaging.MaxFenceRenewalAttempts - 1, races.Races);
        await using var read = database.Open(CursorScope);
        Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(await OwnershipRevisionAsync(database), Cursor(cursors, lease).Revision);
    }

    [Fact]
    public async Task The_run_after_a_cursor_that_was_behind_reads_the_row_even_when_a_late_cursor_write_arrives_first()
    {
        // The extra run must read the row whatever the cursors say by then. A record of an older heartbeat can
        // arrive between the two runs and make the cursor look usable again while it is still behind.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var behind = Cursor(cursors, lease);
        await using (var other = database.Open(CursorScope))
            Assert.True((await OwnershipNode(other, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
        var capture = new CommandCaptureInterceptor();
        // The first command outside a transaction after the failed save is the marker lookup that follows the rollback.
        var afterTheFailedRun = new OnCommand(command => command.Transaction is null && capture.Commands.Any(IsOwnershipWrite))
        {
            Then = () => cursors.Hold(CursorKey(lease), behind)
        };
        await using var context = database.Open(CursorScope, capture, afterTheFailedRun);

        Assert.Equal((1, 2), await CommitAndCountAsync(CursorStore(context, cursors, clock), capture, FencedCommit("commit-1", lease)));

        Assert.Equal(1, afterTheFailedRun.Hits);
        Assert.Equal(await OwnershipRevisionAsync(database), Cursor(cursors, lease).Revision);
    }

    [Fact]
    public async Task A_conflict_on_another_row_of_a_commit_with_a_cursor_leaves_as_it_does_without_one()
    {
        // Only a conflict on the staged bump means the cursor was behind. A conflict on any other row is the
        // commit's own, and it is not run again.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        await SeedWorkflowRowAsync(database, clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var cursor = Cursor(cursors, lease);
        var rootChanges = new ChangeRetainedRootBeforeSave(delete: false);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture, rootChanges);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => CursorStore(context, cursors, clock).CommitAsync(WorkflowRowCommit("commit-1", lease), Decision()).AsTask());

        Assert.True(rootChanges.Executed);
        Assert.Equal((0, 1), OwnershipCommands(capture, 0));
        Assert.Equal(cursor, Cursor(cursors, lease));
        Assert.Equal(cursor.Revision, await OwnershipRevisionAsync(database));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task A_commit_on_a_context_with_staged_changes_reads_the_row_so_a_cursor_that_is_behind_cannot_fail_it()
    {
        // Such a commit cannot run again: a rollback would lose what the caller staged. So it does not use its
        // cursor, and a cursor that is behind the row cannot turn it into a concurrency failure.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        await using (var other = database.Open(CursorScope))
            Assert.True((await OwnershipNode(other, clock, "node-a").HeartbeatAsync(lease)).Succeeded);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        context.SchedulerStates.Add(SchedulerRow(CursorScope, CursorRun));

        Assert.Equal((1, 1), await CommitAndCountAsync(CursorStore(context, cursors, clock), capture, FencedCommit("commit-1", lease)));

        await using var read = database.Open(CursorScope);
        Assert.Single(await read.SchedulerStates.ToArrayAsync());
        Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Theory]
    [InlineData("distrusted")]
    [InlineData("lease-time-ran-out")]
    public async Task A_cursor_that_stops_being_usable_while_the_commit_stages_makes_the_commit_read_the_row(string how)
    {
        // The cursor is chosen before staging and used at the save. It is looked up again there.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        await SeedWorkflowRowAsync(database, clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var capture = new CommandCaptureInterceptor();
        var duringStaging = new OnCommand(command => command.Transaction is not null && IsRead(command.CommandText, WorkflowExecutionTable))
        {
            Then = () =>
            {
                if (how == "distrusted")
                    cursors.Distrust(CursorKey(lease));
                else
                    clock.Advance(TimeSpan.FromMinutes(2));
            }
        };
        await using var context = database.Open(CursorScope, capture, duringStaging);
        var store = CursorStore(context, cursors, clock);

        if (how == "distrusted")
        {
            Assert.Equal((1, 1), await CommitAndCountAsync(store, capture, WorkflowRowCommit("commit-1", lease)));
        }
        else
        {
            var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(WorkflowRowCommit("commit-1", lease), Decision()).AsTask());
            Assert.Equal(RuntimeFencingRejectionReason.ExpiredLease, refusal.Reason);
            Assert.Equal((1, 0), OwnershipCommands(capture, 0));
        }

        Assert.Equal(1, duringStaging.Hits);
        // The read came after the staging read of the root row: it is the late read, not the one that comes first.
        var commands = capture.Commands.ToArray();
        Assert.True(Array.FindIndex(commands, command => IsRead(command, WorkflowExecutionTable)) < Array.FindIndex(commands, IsOwnershipRead));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_commit_whose_staging_fails_before_its_fence_is_checked_leaves_as_stale_when_the_run_was_lost(bool runWasLost)
    {
        // A commit with a cursor stages before its fence is checked. A commit that reads the row first never stages
        // for a drain that lost its run. Before a staging failure leaves, the row is read once, so such a drain is
        // still refused as stale and not with whatever staging made of its commit.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        if (runWasLost)
        {
            await using var other = database.Open(CursorScope);
            await OwnershipNode(other, clock, "node-b").AcquireAsync(CursorRun);
        }
        var cursor = Cursor(cursors, lease);
        var before = await OwnershipRevisionAsync(database);
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        Func<Task> act = () => CursorStore(context, cursors, clock).CommitAsync(ConsumingMissingWork("commit-staging-fails", lease), Decision()).AsTask();

        if (runWasLost)
            Assert.Equal(RuntimeFencingRejectionReason.StaleToken, (await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(act)).Reason);
        else
            await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(act);

        // No bump was staged. The one read is the look at the row after the rollback.
        Assert.Equal((1, 0), OwnershipCommands(capture, 0));
        Assert.Equal(cursor, Cursor(cursors, lease));
        Assert.Equal(before, await OwnershipRevisionAsync(database));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        await using var read = database.Open(CursorScope);
        Assert.Empty(await read.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Theory]
    [InlineData("provider-timeout", true)]
    [InlineData("provider-timeout", false)]
    [InlineData("call-cancelled", true)]
    public async Task A_staging_failure_reported_as_a_cancellation_leaves_as_stale_only_when_the_call_was_not_cancelled_and_the_run_was_lost(
        string cancellation, bool runWasLost)
    {
        // Some providers report a timeout as an OperationCanceledException. While the call's token is not cancelled
        // that is a failure like any other, and the row is looked at before it leaves. A cancellation of the call
        // itself leaves as it is, and the row is not read.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        if (runWasLost)
        {
            await using var other = database.Open(CursorScope);
            await OwnershipNode(other, clock, "node-b").AcquireAsync(CursorRun);
        }
        using var call = new CancellationTokenSource();
        var capture = new CommandCaptureInterceptor();
        var failure = new OnCommand(command => command.Transaction is not null && IsRead(command.CommandText, SchedulerWorkItemTable))
        {
            Then = () =>
            {
                if (cancellation == "call-cancelled")
                {
                    call.Cancel();
                    throw new OperationCanceledException(call.Token);
                }
                throw new TaskCanceledException("Simulated provider timeout.");
            }
        };
        await using var context = database.Open(CursorScope, capture, failure);
        Func<Task> act = () => CursorStore(context, cursors, clock)
            .CommitAsync(ConsumingMissingWork("commit-cancellation", lease), Decision(), call.Token).AsTask();

        if (cancellation == "provider-timeout" && runWasLost)
            Assert.Equal(RuntimeFencingRejectionReason.StaleToken, (await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(act)).Reason);
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(act);

        Assert.Equal(1, failure.Hits);
        Assert.Equal(cancellation == "provider-timeout" ? (1, 0) : (0, 0), OwnershipCommands(capture, 0));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task A_staging_failure_leaves_as_it_is_when_the_look_at_the_ownership_row_fails_too()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        await using (var other = database.Open(CursorScope))
            await OwnershipNode(other, clock, "node-b").AcquireAsync(CursorRun);
        // The look is the one read of the ownership row this commit makes outside its transaction.
        var lookFails = new OnCommand(command => command.Transaction is null && IsOwnershipRead(command.CommandText))
        {
            Then = () => throw new InvalidOperationException("Simulated failure of the ownership read.")
        };
        await using var context = database.Open(CursorScope, lookFails);

        await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(
            () => CursorStore(context, cursors, clock).CommitAsync(ConsumingMissingWork("commit-look-fails", lease), Decision()).AsTask());

        Assert.Equal(1, lookFails.Hits);
    }

    [Fact]
    public async Task Liveness_store_records_the_lease_it_saved_and_drops_the_lease_that_save_replaced()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        await using var context = database.Open(CursorScope);
        await using var heartbeatContext = database.Open(CursorScope);

        var first = await OwnershipNode(context, clock, "node-a", cursors).AcquireAsync(CursorRun);
        Assert.Equal(new ExecutionFenceCursor(1, first.ExpiresAt, RuntimeOperationalStateEfModule.SchemaVersion), Cursor(cursors, first));

        // A heartbeat through another context and another store, as the lease runner's own service scope has.
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.True((await OwnershipNode(heartbeatContext, clock, "node-a", cursors).HeartbeatAsync(first)).Succeeded);
        Assert.Equal(new ExecutionFenceCursor(2, first.ExpiresAt.AddSeconds(20), RuntimeOperationalStateEfModule.SchemaVersion), Cursor(cursors, first));

        // A takeover written by a store of this process: the lease it replaced has no cursor any more.
        var second = await OwnershipNode(context, clock, "node-b", cursors).AcquireAsync(CursorRun);
        Assert.False(cursors.TryGet(CursorKey(first), out _));
        Assert.Equal(new ExecutionFenceCursor(3, second.ExpiresAt, RuntimeOperationalStateEfModule.SchemaVersion), Cursor(cursors, second));

        Assert.True((await OwnershipNode(context, clock, "node-b", cursors).ReleaseAsync(second)).Succeeded);
        Assert.Equal(0, cursors.Count);

        // The unconditional save records as well: the row holds the lease it wrote at the revision it wrote.
        var saved = new RuntimeExecutionLease("lease-saved", CursorRun, "node-c", clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(1), 3);
        await new EfExecutionLivenessStateStore(context, new FixedAccessor(CursorScope), new NoopContinuationCodec()) { FenceCursors = cursors }
            .SaveAsync(new(RuntimeExecutionOwnershipStateId.For(CursorRun), CursorRun, saved, null, null, null));
        Assert.Equal(new ExecutionFenceCursor(await OwnershipRevisionAsync(database), saved.ExpiresAt, RuntimeOperationalStateEfModule.SchemaVersion), Cursor(cursors, saved));
    }

    [Theory]
    [InlineData("not-an-ownership-row")]
    [InlineData("save-refused")]
    [InlineData("save-inside-a-transaction")]
    public async Task Liveness_store_records_no_cursor_for_a_save_that_does_not_make_one_true(string because)
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        await using var context = database.Open(CursorScope);
        var store = new EfExecutionLivenessStateStore(context, new FixedAccessor(CursorScope), new NoopContinuationCodec()) { FenceCursors = cursors };
        var lease = new RuntimeExecutionLease("lease-a", CursorRun, "node-a", OccurredAt, OccurredAt.AddMinutes(1), 1);
        var ownership = new ExecutionLivenessState(RuntimeExecutionOwnershipStateId.For(CursorRun), CursorRun, lease, null, null, null);

        switch (because)
        {
            case "not-an-ownership-row":
                // Another liveness row of the run that happens to carry a lease.
                Assert.True((await store.TrySaveAsync(new ExecutionLivenessState("operational-1", CursorRun, lease, null, null, null), 0)).Succeeded);
                break;
            case "save-refused":
                Assert.False((await store.TrySaveAsync(ownership, 7)).Succeeded);
                break;
            default:
                // The save can still be rolled back, and then the row never held the lease at that revision.
                await using (var transaction = await context.Database.BeginTransactionAsync())
                {
                    Assert.True((await store.TrySaveAsync(ownership, 0)).Succeeded);
                    Assert.Equal(0, cursors.Count);
                    await transaction.RollbackAsync();
                }
                break;
        }

        Assert.Equal(0, cursors.Count);
    }

    [Fact]
    public void The_liveness_store_stays_a_type_registration_the_page_reuse_composition_guard_checks()
    {
        // The guard that decides whether durable-value pages may be reused checks every concrete store the EF
        // backend registered by its type: an unowned second registration of one makes the composition ineligible.
        // It finds those concretes by their type registration, so the liveness store must stay one.
        var services = CompleteEfServices();
        Assert.True(RuntimeCoalescingDurableValuePageReuseRegistration.CaptureBeforeDecoration(services).EligibleBeforeDecoration);

        services.AddScoped<EfExecutionLivenessStateStore>(_ => throw new InvalidOperationException("An override the backend does not own."));

        Assert.False(RuntimeCoalescingDurableValuePageReuseRegistration.CaptureBeforeDecoration(services).EligibleBeforeDecoration);
    }

    /// <summary>
    /// The runtime's ownership service over the EF store, as any node runs it. With <paramref name="cursors"/> its
    /// store records cursors, as a store of this process does. Without them what it writes is what another node
    /// writes: nothing in this process hears of it.
    /// </summary>
    private static RuntimeExecutionOwnershipService OwnershipNode(
        RuntimeDbContext context, TimeProvider clock, string ownerId, EfExecutionFenceCursors? cursors = null) =>
        new(new EfExecutionLivenessStateStore(context, new FixedAccessor(CursorScope), new NoopContinuationCodec()) { FenceCursors = cursors }, clock,
            new RuntimeExecutionOwnershipOptions { OwnerId = ownerId, LeaseDuration = TimeSpan.FromMinutes(1) });

    /// <summary>Acquires the run as node-a through a store of this process, which records the lease's cursor.</summary>
    private static async Task<RuntimeExecutionLease> AcquireWithCursorAsync(TestDatabase database, EfExecutionFenceCursors cursors, TimeProvider clock)
    {
        await using var context = database.Open(CursorScope);
        var lease = await OwnershipNode(context, clock, "node-a", cursors).AcquireAsync(CursorRun);
        Assert.True(cursors.TryGet(CursorKey(lease), out _));
        return lease;
    }

    // BorrowAsync is internal to the runtime. An alteration is what calls it.
    private static async Task BorrowAsync(RuntimeExecutionOwnershipService node)
    {
        var borrow = typeof(RuntimeExecutionOwnershipService).GetMethod("BorrowAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(node, [CursorRun, CancellationToken.None])!;
        await (Task)borrow.GetType().GetMethod("AsTask")!.Invoke(borrow, null)!;
    }

    private static async Task SeedWorkflowRowAsync(TestDatabase database, TimeProvider clock)
    {
        await using var seed = database.Open(CursorScope);
        var creation = Commit("workflow-row-seed") with
        {
            StateChanges = new(new(CursorRun, RuntimeStateChangeOperation.Upsert, Execution(CursorRun, "tenant-a"),
                new Dictionary<string, string>()), null, [], [], [], [], [])
        };
        await CursorStore(seed, null, clock).CommitAsync(creation, Decision());
    }

    private static RuntimeCheckpointCommit WorkflowRowCommit(string commitId, RuntimeExecutionLease lease) => FencedCommit(commitId, lease) with
    {
        StateChanges = new(new(CursorRun, RuntimeStateChangeOperation.Upsert,
            Execution(CursorRun, "tenant-a") with { SystemMetadata = new Dictionary<string, string> { ["business-value"] = commitId } },
            new Dictionary<string, string>()), null, [], [], [], [], [])
    };

    /// <summary>A fenced commit that consumes a work item that is not in the queue, which staging refuses.</summary>
    private static RuntimeCheckpointCommit ConsumingMissingWork(string commitId, RuntimeExecutionLease lease)
    {
        var commit = FencedCommit(commitId, lease);
        return commit with
        {
            StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems([new ConsumedSchedulerWorkItem(CursorRun, "work-missing", "worker-a", 1)])
        };
    }

    private const string WorkflowExecutionTable = "\"elsa_runtime_workflow_execution_state\"";
    private const string SchedulerWorkItemTable = "\"elsa_runtime_scheduler_work_item\"";

    private static bool IsRead(string command, string table) =>
        command.StartsWith("SELECT", StringComparison.Ordinal) && command.Contains(table, StringComparison.Ordinal);

    private static bool IsOwnershipRead(string command) => IsRead(command, RuntimeOperationalStateEfModule.ExecutionLivenessTableName);

    private static bool IsOwnershipWrite(string command) =>
        command.StartsWith("UPDATE", StringComparison.Ordinal) && command.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.Ordinal);

    /// <summary>What each pending change to the ownership table looks like to an interceptor when a save starts.</summary>
    private sealed class OwnershipWritesAtSave : SaveChangesInterceptor
    {
        public List<(string? Stamp, string[] Modified)> Writes { get; } = [];

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<ExecutionLivenessStateEntity>().Where(entry => entry.State == EntityState.Modified))
                Writes.Add((entry.Property(row => row.SchemaVersion).CurrentValue,
                    entry.Properties.Where(property => property.IsModified).Select(property => property.Metadata.Name).ToArray()));
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Runs <see cref="Then"/> once, before the first command that matches.</summary>
    private sealed class OnCommand(Func<DbCommand, bool> matches) : DbCommandInterceptor
    {
        public required Action Then { get; init; }
        public int Hits { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Hits == 0 && matches(command))
            {
                Hits++;
                Then();
            }
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>
    /// The owner's heartbeat lands under a commit that read the ownership row, as many times as asked: the save of
    /// that attempt sees a superseded revision, and the heartbeat's bump is applied by the first statement the
    /// context runs outside a transaction afterwards, as another connection's commit would be.
    /// </summary>
    private sealed class OwnerHeartbeatRaces(int count) : ISaveChangesInterceptor, IDbCommandInterceptor
    {
        private bool pending;
        public int Races { get; private set; }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            // Only a row that was read carries its content. The bump staged from a cursor does not.
            var fence = eventData.Context!.ChangeTracker.Entries<ExecutionLivenessStateEntity>()
                .SingleOrDefault(entry => entry.State == EntityState.Modified && entry.Entity.ContentJson is not null);
            if (Races < count && fence is not null)
            {
                Races++;
                pending = true;
                fence.Property(row => row.Revision).OriginalValue = fence.Property(row => row.Revision).OriginalValue - 1;
            }
            return ValueTask.FromResult(result);
        }

        public async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (pending && command.Transaction is null)
            {
                pending = false;
                await using var change = command.Connection!.CreateCommand();
                change.CommandText = $"UPDATE \"{RuntimeOperationalStateEfModule.ExecutionLivenessTableName}\" SET \"Revision\" = \"Revision\" + 1";
                Assert.Equal(1, await change.ExecuteNonQueryAsync(cancellationToken));
            }
            return result;
        }
    }

    private static EfExecutionFenceCursors.Key CursorKey(RuntimeExecutionLease lease) => EfExecutionFenceCursors.Key.For(CursorScope, lease);

    private static ExecutionFenceCursor Cursor(EfExecutionFenceCursors cursors, RuntimeExecutionLease lease)
    {
        Assert.True(cursors.TryGet(CursorKey(lease), out var cursor));
        return cursor;
    }

    private static async Task<long> OwnershipRevisionAsync(TestDatabase database)
    {
        await using var read = database.Open(CursorScope);
        return (await read.ExecutionLivenessStates.AsNoTracking().SingleAsync()).Revision;
    }

    private static EfRuntimeCheckpointCommitStore CursorStore(RuntimeDbContext context, EfExecutionFenceCursors? cursors, TimeProvider clock) =>
        new(context, new FixedAccessor(CursorScope), clock, new PassThroughRootWriteLeaseManager()) { FenceCursors = cursors };

    private static RuntimeCheckpointCommit FencedCommit(string commitId, RuntimeExecutionLease lease) =>
        Commit(commitId) with { ExpectedFence = lease.ToFence() };

    private static async Task<(int Reads, int Writes)> CommitAndCountAsync(
        EfRuntimeCheckpointCommitStore store, CommandCaptureInterceptor capture, RuntimeCheckpointCommit commit)
    {
        var before = capture.Commands.Count;
        await store.CommitAsync(commit, Decision());
        return OwnershipCommands(capture, before);
    }

    /// <summary>The reads and writes of the ownership table among the commands captured from <paramref name="from"/> on.</summary>
    private static (int Reads, int Writes) OwnershipCommands(CommandCaptureInterceptor capture, int from)
    {
        var commands = capture.Commands.Skip(from).ToArray();
        return (commands.Count(IsOwnershipRead), commands.Count(IsOwnershipWrite));
    }
}
