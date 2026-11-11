using System.Reflection;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// #2564: a fenced commit whose lease has a fence cursor checks the fence with one conditional statement. Every
/// other case reads the ownership row, validates it and bumps it at save, as before. Each test counts the commands
/// the commit ran against the ownership table: a read is a SELECT, a write is an UPDATE.
/// </summary>
public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    private const string CursorScope = "tenant-a";
    private const string CursorRun = "workflow-a";

    [Fact]
    public async Task Fenced_commit_with_a_current_cursor_checks_the_fence_with_one_statement_and_does_not_read_the_row()
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
        Assert.Equal(new ExecutionFenceCursor(acquired + 2, lease.AcquiredAt, lease.ExpiresAt), Cursor(cursors, lease));
        // The statement goes around the change tracker, so the commit leaves no copy of the row in it.
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
        // A store built without cursors, or a lease nothing reported: one that another ownership service or a
        // decorated liveness store manages. The commit never makes a cursor of its own for such a lease.
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
    public async Task Fenced_commit_after_a_heartbeat_it_did_not_hear_of_reads_the_row_once_and_is_one_statement_again()
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

        // The statement matches no row. The row is then read and validated, and the tracked save bumps it.
        Assert.Equal((1, 2), await CommitAndCountAsync(store, capture, FencedCommit("commit-1", lease)));
        Assert.Equal(new ExecutionFenceCursor(renewed + 1, lease.AcquiredAt, lease.ExpiresAt.AddSeconds(20)), Cursor(cursors, lease));
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
                    // The new lease has the owner and the lease times of the old one, so every lease column of
                    // the row is as the cursor has it. Only the revision tells the two leases apart.
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
        Assert.Equal(cursor, Cursor(cursors, lease));
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

        Assert.Equal(new ExecutionFenceCursor(renewed + 1, lease.AcquiredAt, lease.ExpiresAt.AddSeconds(40)), Cursor(cursors, lease));
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

        // The statement's bump was rolled back with the rest, so the cursor still names the row.
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
        // No statement is tried against a revision that is not known.
        Assert.Equal((1, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-next", lease)));
        Assert.Equal(cursor with { Revision = cursor.Revision + 2 }, Cursor(cursors, lease));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-after", lease)));
    }

    [Theory]
    [InlineData("heartbeat-report-before-the-commit")]
    [InlineData("heartbeat-report-after-the-commit")]
    [InlineData("commit-report-after-the-heartbeat-report")]
    public async Task A_heartbeat_and_a_commit_whose_cursor_writes_cross_end_in_one_statement_or_one_read(string order)
    {
        // The heartbeat writes the row and reports its cursor from another service scope, so the two cursor writes
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
            return new(await OwnershipRevisionAsync(database), lease.AcquiredAt, renewedExpiry);
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

        Assert.Equal(new ExecutionFenceCursor(acquired.Revision + 2, lease.AcquiredAt, renewedExpiry), Cursor(cursors, lease));
        Assert.Equal((0, 1), await CommitAndCountAsync(store, capture, FencedCommit("commit-2", lease)));
        Assert.Equal(acquired.Revision + 3, await OwnershipRevisionAsync(database));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("acquired-at")]
    [InlineData("expires-at")]
    public async Task A_cursor_whose_revision_the_row_has_under_other_lease_columns_matches_no_row(string differs)
    {
        // No writer in the runtime uses a revision of an ownership row twice. A row removed and written again
        // outside the runtime could reach a revision again, holding another lease. The lease columns in the
        // statement keep it from matching then, and the row is read.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var held = new RuntimeExecutionLease("lease-a", CursorRun, "node-a", OccurredAt, OccurredAt.AddMinutes(1), 1);
        var remembered = differs switch
        {
            "owner" => new RuntimeExecutionLease("lease-a", CursorRun, "node-b", held.AcquiredAt, held.ExpiresAt, 1),
            "acquired-at" => new RuntimeExecutionLease("lease-a", CursorRun, "node-a", held.AcquiredAt.AddSeconds(-1), held.ExpiresAt, 1),
            _ => new RuntimeExecutionLease("lease-a", CursorRun, "node-a", held.AcquiredAt, held.ExpiresAt.AddSeconds(1), 1)
        };
        await using (var seed = database.Open(CursorScope))
            await new EfExecutionLivenessStateStore(seed, new FixedAccessor(CursorScope), new NoopContinuationCodec())
                .SaveAsync(new(RuntimeExecutionOwnershipStateId.For(CursorRun), CursorRun, held, null, null, null));
        cursors.Hold(CursorKey(remembered), new(await OwnershipRevisionAsync(database), remembered.AcquiredAt, remembered.ExpiresAt));
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var store = CursorStore(context, cursors, clock);
        var commit = FencedCommit("commit-1", remembered);

        if (differs == "owner")
        {
            var refusal = await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(commit, Decision()).AsTask());
            Assert.Equal(RuntimeFencingRejectionReason.StaleToken, refusal.Reason);
            Assert.Equal((1, 1), OwnershipCommands(capture, 0));
        }
        else
        {
            // The validator does not compare lease times, so the read admits the commit. The statement did not.
            Assert.Equal((1, 2), await CommitAndCountAsync(store, capture, commit));
        }
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
        var acquired = new ExecutionFenceCursor(await OwnershipRevisionAsync(database), owner.AcquiredAt, owner.ExpiresAt);
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
        // leaves the cursor behind the row, which the next commit finds out from its statement.
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

    /// <summary>
    /// The runtime's ownership service over the EF store, as any node runs it. Built by hand it reports to no
    /// cursors, so what it writes is what another node writes, or a report that never arrived.
    /// </summary>
    private static RuntimeExecutionOwnershipService OwnershipNode(RuntimeDbContext context, TimeProvider clock, string ownerId) =>
        new(new EfExecutionLivenessStateStore(context, new FixedAccessor(CursorScope), new NoopContinuationCodec()), clock,
            new RuntimeExecutionOwnershipOptions { OwnerId = ownerId, LeaseDuration = TimeSpan.FromMinutes(1) });

    /// <summary>Acquires the run as node-a and gives the lease the cursor the ownership service reports in a composed host.</summary>
    private static async Task<RuntimeExecutionLease> AcquireWithCursorAsync(TestDatabase database, EfExecutionFenceCursors cursors, TimeProvider clock)
    {
        RuntimeExecutionLease lease;
        await using (var context = database.Open(CursorScope))
            lease = await OwnershipNode(context, clock, "node-a").AcquireAsync(CursorRun);
        cursors.Hold(CursorKey(lease), new(await OwnershipRevisionAsync(database), lease.AcquiredAt, lease.ExpiresAt));
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
        var commands = capture.Commands.Skip(from)
            .Where(command => command.Contains(RuntimeOperationalStateEfModule.ExecutionLivenessTableName, StringComparison.Ordinal))
            .ToArray();
        return (commands.Count(command => command.StartsWith("SELECT", StringComparison.Ordinal)),
            commands.Count(command => command.StartsWith("UPDATE", StringComparison.Ordinal)));
    }
}
