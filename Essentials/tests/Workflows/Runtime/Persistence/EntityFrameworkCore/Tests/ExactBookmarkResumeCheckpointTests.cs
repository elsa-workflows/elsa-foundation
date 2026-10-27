using System.Text.Json;
using System.Data.Common;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Actual provider checkpoints; these do not qualify the later source-owned router or real callback path.</summary>
public sealed class ExactBookmarkResumeCheckpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 2, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("tenant");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");
    private static WorkflowExecutionState Workflow => new("target", Pin, WorkflowExecutionStatus.Suspended, null, Now, Now, Now,
        null, null, null, Scope.Value, new Dictionary<string, string>()) { RunKind = WorkflowRunKind.PublishedRun, Partition = new("partition") };
    private static BookmarkState Bookmark => new("wait", "target", "activity", "node", "resume", "event", "named",
        JsonSerializer.SerializeToElement(new { retained = "not copied into the capture" }),
        new Dictionary<string, string> { ["activation"] = "A", ["slot"] = "S" }, Now, Now.AddMinutes(5));
    private static ActivityExecutionState Suspended => new(new("activity", "target", "node", "authored", "Test.Activity", "1"),
        ActivityExecutionStatus.Suspended, null, 1, Now, Now, null, null, null, null, null,
        ActivitySchedulingProvenance.From("target", null, null, null, null, null, null, "fixture"), null,
        ["wait"], [], 0, 0, new Dictionary<string, string>());
    private static ActivityExecutionState Claim => Suspended with { Status = ActivityExecutionStatus.Running,
        Attempts = [new("claim", "activity", 1, ActivityAttemptReason.Resume, Now)],
        Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ActivityAttemptActivationClaim] = "claim",
            [RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId] = "resume-work" } };

    private static RuntimeCheckpointCommit Commit(StimulusResumeRecipient expected, bool compound = false) => new(
        "exact-claim", new("checkpoint", "ExactBookmarkClaim", "target", Now, ["activity"], new Dictionary<string, string>()),
        new(compound ? new("target", RuntimeStateChangeOperation.Upsert, Workflow with { UpdatedAt = Now.AddSeconds(1) }, new Dictionary<string, string>()) : null,
            null, [new("activity", RuntimeStateChangeOperation.Upsert, Claim, new Dictionary<string, string>())],
            [new("wait", RuntimeStateChangeOperation.Delete, Bookmark, new Dictionary<string, string>())], [], [], [],
            postCommitOutbox: compound ? [new("sentinel:send", RuntimeStateChangeOperation.Upsert,
                new("sentinel:send", new("send", "target", "sentinel", Now, null, null, null), RuntimePostCommitOutboxStatus.Pending, Now, Now),
                new Dictionary<string, string>())] : []), [], new Dictionary<string, string>()) { ExpectedBookmarkResume = expected, RetainSchedulerWork = true };

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Exact_checkpoint_consumes_one_generation_and_replays_before_retired_predicates(bool ef, bool compound)
    {
        await using var harness = await Harness.Create(ef); await harness.Seed();
        var expected = await harness.Capture(); var commit = Commit(expected, compound);
        Assert.True(harness.Checkpoints.SupportsExactBookmarkResume);
        Assert.True(harness.Checkpoints.CanCommitExactBookmarkResume(harness.Bookmarks, harness.Executions, harness.Activities));
        await harness.Checkpoints.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Null(await harness.Bookmarks.FindAsync("target", "wait"));
        Assert.Equal(ActivityExecutionStatus.Running, (await harness.Activities.FindAsync("target", "activity"))!.Status);
        Assert.Single((await harness.Activities.FindAsync("target", "activity"))!.Attempts!); Assert.Equal(1, await harness.Markers());
        Assert.Equal(compound ? 1 : 0, await harness.OutboxCount());
        await harness.Bookmarks.SaveAsync(Bookmark); // Same identity, data and CreatedAt: a new physical generation.
        var replacement = (await harness.Bookmarks.ObserveAsync("target", "wait"))!;
        Assert.NotEqual(expected.RegistrationCapture!.Generation, replacement.Generation);
        await harness.Checkpoints.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Equal(replacement.Generation, (await harness.Bookmarks.ObserveAsync("target", "wait"))!.Generation);
        var changed = commit with { ExpectedBookmarkResume = StimulusResumeRecipient.From(replacement, Workflow, Scope) };
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => harness.Checkpoints.CommitAsync(changed, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Equal(1, await harness.Markers());
    }

    [Theory]
    [InlineData(false, "recreate")] [InlineData(true, "recreate")]
    [InlineData(false, "resave")] [InlineData(true, "resave")]
    [InlineData(false, "metadata")] [InlineData(true, "metadata")]
    [InlineData(false, "payload")] [InlineData(true, "payload")]
    [InlineData(false, "expiry")] [InlineData(true, "expiry")]
    [InlineData(false, "activity-cancel")] [InlineData(true, "activity-cancel")]
    [InlineData(false, "activity-superseded")] [InlineData(true, "activity-superseded")]
    [InlineData(false, "workflow-terminal")] [InlineData(true, "workflow-terminal")]
    [InlineData(false, "partition")] [InlineData(true, "partition")]
    [InlineData(false, "run-kind")] [InlineData(true, "run-kind")]
    [InlineData(false, "missing")] [InlineData(true, "missing")]
    [InlineData(false, "activity-bookmarks")] [InlineData(true, "activity-bookmarks")]
    [InlineData(false, "pin")] [InlineData(true, "pin")]
    [InlineData(false, "pinned-source")] [InlineData(true, "pinned-source")]
    public async Task Changed_wait_or_owner_refuses_the_entire_compound_checkpoint(bool ef, string change)
    {
        await using var harness = await Harness.Create(ef); await harness.Seed(); var expected = await harness.Capture();
        if (change == "recreate") { Assert.True(await harness.Bookmarks.DeleteAsync("target", "wait")); await harness.Bookmarks.SaveAsync(Bookmark); }
        if (change == "resave") await harness.Bookmarks.SaveAsync(Bookmark);
        if (change == "metadata") await harness.Bookmarks.SaveAsync(Bookmark with { Metadata = new Dictionary<string, string> { ["activation"] = "replacement" } });
        if (change == "payload") await harness.Bookmarks.SaveAsync(Bookmark with { Payload = JsonSerializer.SerializeToElement("replacement") });
        if (change == "expiry") harness.Clock.Advance(TimeSpan.FromMinutes(5));
        if (change == "activity-cancel") await harness.Activities.SaveAsync(Suspended with { Status = ActivityExecutionStatus.Cancelled });
        if (change == "activity-superseded") await harness.Activities.SaveAsync(Suspended with { Status = ActivityExecutionStatus.Superseded, SupersededByActivityExecutionId = "successor", SupersededAt = Now });
        if (change == "workflow-terminal") await harness.Executions.SaveAsync(Workflow with { Status = WorkflowExecutionStatus.Cancelled });
        if (change == "partition") await harness.Executions.SaveAsync(Workflow with { Partition = new("other") });
        if (change == "run-kind") await harness.Executions.SaveAsync(Workflow with { RunKind = WorkflowRunKind.Unknown });
        if (change == "missing") Assert.True(await harness.Bookmarks.DeleteAsync("target", "wait"));
        if (change == "activity-bookmarks") await harness.Activities.SaveAsync(Suspended with { BookmarkIds = [] });
        if (change == "pin") await harness.Executions.SaveAsync(Workflow with { PinnedExecutable = Pin with { ArtifactHash = "replacement" } });
        if (change == "pinned-source") await harness.Executions.SaveAsync(Workflow with { PinnedSource = new("reference", "published", "source", null, "definition", "version", "1.0.0", "publication", "slot") });
        var before = await harness.Bookmarks.ObserveAsync("target", "wait");
        var stateBefore = (await harness.Activities.FindAsync("target", "activity"))!;
        await Assert.ThrowsAsync<BookmarkResumeSupersededException>(() => harness.Checkpoints.CommitAsync(Commit(expected, true), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Equal(before?.Generation, (await harness.Bookmarks.ObserveAsync("target", "wait"))?.Generation);
        Assert.Equal(stateBefore.Status, (await harness.Activities.FindAsync("target", "activity"))!.Status);
        Assert.Empty((await harness.Activities.FindAsync("target", "activity"))!.Attempts ?? []);
        Assert.Equal(0, await harness.Markers()); Assert.Equal(0, await harness.OutboxCount());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Capability_refuses_foreign_store_tuples_and_uncaptured_commit(bool ef)
    {
        await using var harness = await Harness.Create(ef); await harness.Seed();
        await using var foreign = await Harness.Create(ef); await foreign.Seed();
        Assert.False(harness.Checkpoints.CanCommitExactBookmarkResume(foreign.Bookmarks, harness.Executions, harness.Activities));
        Assert.False(harness.Checkpoints.CanCommitExactBookmarkResume(harness.Bookmarks, foreign.Executions, harness.Activities));
        Assert.False(harness.Checkpoints.CanCommitExactBookmarkResume(harness.Bookmarks, harness.Executions, foreign.Activities));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Checkpoints.CommitAsync(
            Commit(StimulusResumeRecipient.From(Bookmark), true), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.NotNull(await harness.Bookmarks.FindAsync("target", "wait")); Assert.Equal(0, await harness.Markers());
    }

    [Fact]
    public async Task Native_generations_and_owned_metadata_survive_alias_mutation_and_store_replacement()
    {
        var metadata = new Dictionary<string, string>(Bookmark.Metadata); var original = Bookmark with { Metadata = metadata };
        var store = new InMemoryBookmarkStateStore(); await store.SaveAsync(original);
        var observed = (await store.ObserveAsync("target", "wait"))!;
        metadata["activation"] = "changed-after-save";
        Assert.Equal("A", (await store.FindAsync("target", "wait"))!.Metadata["activation"]);
        Assert.Equal("A", observed.State.Metadata["activation"]);
        var replacement = new InMemoryBookmarkStateStore(); await replacement.SaveAsync(Bookmark);
        Assert.NotEqual(observed.Generation, (await replacement.ObserveAsync("target", "wait"))!.Generation);
    }

    [Fact]
    public async Task Ef_dirty_observation_and_admission_refuse_without_flushing_or_clearing_sibling()
    {
        await using var harness = await Harness.Create(true); await harness.Seed(); var expected = await harness.Capture();
        var context = harness.Context!; var row = await context.WorkflowExecutionStates.SingleAsync();
        row.Revision++; var entry = context.Entry(row); context.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Modified, entry.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Bookmarks.ObserveAsync("target", "wait").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Checkpoints.CommitAsync(Commit(expected, true), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Equal(EntityState.Modified, entry.State); Assert.Equal(0, await harness.Markers());
        await using var verification = harness.FreshContext();
        Assert.NotEqual(row.Revision, (await verification.WorkflowExecutionStates.SingleAsync()).Revision);
        Assert.Single(await verification.Bookmarks.ToListAsync()); Assert.Empty(await verification.RuntimePostCommitOutbox.ToListAsync());
        entry.State = EntityState.Detached;
        await harness.Checkpoints.CommitAsync(Commit(expected, true), new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Null(await harness.Bookmarks.FindAsync("target", "wait")); Assert.Equal(1, await harness.Markers());
    }

    [Fact]
    public async Task Ef_local_transaction_refuses_without_consuming_wait_or_ending_callers_transaction()
    {
        await using var harness = await Harness.Create(true); await harness.Seed(); var expected = await harness.Capture();
        await using var transaction = await harness.Context!.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Bookmarks.ObserveAsync("target", "wait").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Checkpoints.CommitAsync(Commit(expected), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Same(transaction, harness.Context.Database.CurrentTransaction); Assert.Equal(0, await harness.Markers());
        Assert.NotNull(await harness.Bookmarks.FindAsync("target", "wait"));
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Ef_before_save_failure_and_after_sql_expiry_roll_back_all_participants_then_retry(bool expireAfterSql)
    {
        var cut = new ParticipantSaveCut(expireAfterSql);
        await using var harness = await Harness.Create(true, cut); await harness.Seed(); var expected = await harness.Capture();
        cut.Expire = () => harness.Clock.Advance(TimeSpan.FromMinutes(5)); cut.Armed = true;
        var commit = Commit(expected, true);
        if (expireAfterSql)
            await Assert.ThrowsAsync<BookmarkResumeSupersededException>(() => harness.Checkpoints.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        else
        {
            var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => harness.Checkpoints.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
            Assert.Same(cut.Cause, failure.InnerException);
        }
        Assert.Equal(1, cut.Cuts);
        await using (var verification = harness.FreshContext())
        {
            Assert.Single(await verification.RuntimeCheckpointCommits.ToListAsync()); // Seed only.
            Assert.Single(await verification.Bookmarks.ToListAsync()); Assert.Empty(await verification.RuntimePostCommitOutbox.ToListAsync());
            Assert.Equal(ActivityExecutionStatus.Suspended, (await new EfActivityExecutionStateStore(verification, new Access(), new Codec()).FindAsync("target", "activity"))!.Status);
            Assert.Equal(Now, (await new EfWorkflowExecutionStateStore(verification, new Access(), new Codec(), harness.Clock).FindAsync("target"))!.UpdatedAt);
        }
        if (expireAfterSql)
        {
            // A later valid wait is a different event recipient; the old captured request stays refused.
            await harness.Bookmarks.SaveAsync(Bookmark with { ExpiresAt = Now.AddMinutes(10) });
            expected = await harness.Capture();
            commit = Commit(expected, true) with { StateChanges = ReplaceDeleteState(Commit(expected, true).StateChanges, Bookmark with { ExpiresAt = Now.AddMinutes(10) }) };
        }
        await harness.Checkpoints.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Equal(1, await harness.Markers()); Assert.Null(await harness.Bookmarks.FindAsync("target", "wait")); Assert.Equal(1, await harness.OutboxCount());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Ef_actual_commit_ack_loss_reconciles_or_retries_fresh_without_second_admission(bool loseReconciliation)
    {
        var loss = new CommitAckLoss(); var read = new ReconciliationReadFailure(loss, loseReconciliation);
        await using var harness = await Harness.Create(true, loss, read); await harness.Seed(); var expected = await harness.Capture();
        loss.Armed = true; var commit = Commit(expected);
        if (loseReconciliation)
        {
            var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => harness.Checkpoints.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
            var causes = Assert.IsType<AggregateException>(failure.InnerException).InnerExceptions;
            Assert.Contains(loss.Cause, causes); Assert.Contains(read.Cause, causes);
        }
        else await harness.Checkpoints.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Equal(1, loss.Losses);
        await using var fresh = harness.FreshContext();
        var bookmarks = new EfBookmarkStateStore(fresh, new Access()); var activities = new EfActivityExecutionStateStore(fresh, new Access(), new Codec());
        Assert.Null(await bookmarks.FindAsync("target", "wait")); Assert.Single((await activities.FindAsync("target", "activity"))!.Attempts!);
        await bookmarks.SaveAsync(Bookmark); var replacement = (await bookmarks.ObserveAsync("target", "wait"))!;
        await new EfRuntimeCheckpointCommitStore(fresh, new Access(), harness.Clock).CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Equal(replacement.Generation, (await bookmarks.ObserveAsync("target", "wait"))!.Generation);
        Assert.Equal(2, await fresh.RuntimeCheckpointCommits.CountAsync()); // Seed + one actual admission.
    }

    private static RuntimeCheckpointStateChangeSet ReplaceDeleteState(RuntimeCheckpointStateChangeSet changes, BookmarkState bookmark) =>
        new(changes.WorkflowExecution, changes.Scheduler, changes.ActivityExecutions,
            [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Delete, bookmark, new Dictionary<string, string>())],
            changes.DurableValues, changes.Incidents, changes.Operational, postCommitOutbox: changes.PostCommitOutbox);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Final_deadline_after_actual_root_lease_refuses_all_checkpoint_writes(bool ef)
    {
        await using var harness = await Harness.Create(ef); await harness.Seed();
        var wait = Bookmark with { ExpiresAt = Now.AddSeconds(1) }; await harness.Bookmarks.SaveAsync(wait);
        var expected = await harness.Capture(); var commit = Commit(expected, true);
        commit = commit with { StateChanges = ReplaceDeleteState(commit.StateChanges, wait) };
        harness.BeforeRootWrite = () => { harness.Clock.Advance(TimeSpan.FromSeconds(1)); return Task.CompletedTask; };
        await Assert.ThrowsAsync<BookmarkResumeSupersededException>(() => harness.Checkpoints.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Equal(1, harness.RootCuts); Assert.Equal(0, await harness.Markers()); Assert.Equal(0, await harness.OutboxCount());
        Assert.Equal(expected.RegistrationCapture!.Generation, (await harness.Bookmarks.ObserveAsync("target", "wait"))!.Generation);
        Assert.Equal(ActivityExecutionStatus.Suspended, (await harness.Activities.FindAsync("target", "activity"))!.Status);
    }

    [Theory]
    [InlineData("bookmark")] [InlineData("activity")]
    public async Task Native_admission_gate_serializes_peer_raw_store_writes_after_the_initial_claim(string peer)
    {
        await using var harness = await Harness.Create(false); await harness.Seed(); var expected = await harness.Capture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.BeforeRootWrite = async () => { entered.SetResult(); await release.Task; };
        var admission = harness.Checkpoints.CommitAsync(Commit(expected, true), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var write = peer == "bookmark" ? harness.Bookmarks.SaveAsync(Bookmark).AsTask() as Task : harness.Activities.SaveAsync(Suspended with { Status = ActivityExecutionStatus.Cancelled }).AsTask();
        Assert.False(write.IsCompleted); // Each call has already reached the held provider write gate.
        release.SetResult(); await admission; await write;
        Assert.Equal(1, await harness.Markers()); Assert.Equal(1, await harness.OutboxCount());
        if (peer == "bookmark") Assert.NotEqual(expected.RegistrationCapture!.Generation, (await harness.Bookmarks.ObserveAsync("target", "wait"))!.Generation);
        else { Assert.Null(await harness.Bookmarks.FindAsync("target", "wait")); Assert.Equal(ActivityExecutionStatus.Cancelled, (await harness.Activities.FindAsync("target", "activity"))!.Status); }
    }

    [Fact]
    public async Task Native_concurrent_different_checkpoints_admit_a_captured_wait_only_once()
    {
        await using var harness = await Harness.Create(false); await harness.Seed(); var expected = await harness.Capture();
        async Task<bool> Attempt(string id)
        {
            try { await harness.Checkpoints.CommitAsync(Commit(expected) with { CommitId = id }, new(RuntimeCheckpointPersistenceMode.Immediate)); return true; }
            catch (BookmarkResumeSupersededException) { return false; }
        }
        var result = await Task.WhenAll(Task.Run(() => Attempt("one")), Task.Run(() => Attempt("two")));
        Assert.Equal(1, result.Count(value => value)); Assert.Equal(1, await harness.Markers());
        Assert.Single((await harness.Activities.FindAsync("target", "activity"))!.Attempts!);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Active_coalescing_flush_keeps_exact_condition_and_persists_buffer_only_with_matching_wait(bool rearm)
    {
        await using var harness = await Harness.Create(false); await harness.Seed(); var expected = await harness.Capture();
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var decorator = new CoalescingRuntimeCheckpointCommitStore(new(harness.Checkpoints), accessor);
        var session = new RuntimeCoalescingSession("target", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        using var active = accessor.Push(session);
        var other = Suspended with { Execution = Suspended.Execution with { ActivityExecutionId = "other" }, BookmarkIds = [] };
        await decorator.CommitAsync(new("buffered", new("buffered", "Buffered", "target", Now, ["other"], new Dictionary<string, string>()),
            new(null, null, [new("other", RuntimeStateChangeOperation.Upsert, other, new Dictionary<string, string>())], [], [], [], []), [], new Dictionary<string, string>()),
            new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.True(session.HasBufferedChanges); Assert.Null(await harness.Activities.FindAsync("target", "other"));
        if (rearm) await harness.Bookmarks.SaveAsync(Bookmark);
        if (rearm)
        {
            await Assert.ThrowsAsync<BookmarkResumeSupersededException>(() => decorator.CommitAsync(Commit(expected), new(RuntimeCheckpointPersistenceMode.Deferred)).AsTask());
            Assert.Null(await harness.Activities.FindAsync("target", "other")); Assert.Equal(0, await harness.Markers());
        }
        else
        {
            await decorator.CommitAsync(Commit(expected), new(RuntimeCheckpointPersistenceMode.Deferred));
            Assert.NotNull(await harness.Activities.FindAsync("target", "other")); Assert.Equal(1, await harness.Markers());
            var durable = ((InMemoryRuntimeCheckpointCommitStore)harness.Checkpoints).ListCommits().Single(record => record.Commit.CommitId == "exact-claim");
            Assert.NotNull(durable.Commit.ExpectedBookmarkResume); Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, durable.Decision.Mode);
            await harness.Checkpoints.CommitAsync(durable.Commit, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal(1, await harness.Markers());
        }
    }

    private sealed class ObservedLease(IWorkflowExecutableRootWriteLeaseManager inner, Harness owner) : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => inner.ExecuteAsync(artifactId, leaseId, write, cancellationToken);
        public ValueTask ExecuteAsync(WorkflowExecutableIdentity root, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(root, leaseId, async token => { if (owner.BeforeRootWrite is { } cut) { owner.BeforeRootWrite = null; owner.RootCuts++; await cut(); } await write(token); }, cancellationToken);
    }

    private sealed class ParticipantSaveCut(bool afterSql) : SaveChangesInterceptor
    {
        public bool Armed; public Action? Expire; public int Cuts;
        public IOException Cause { get; } = new("Constructed pre-save failure of the actual exact-wait checkpoint.");
        private static bool ConsumesWait(DbContext? context) => context!.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Deleted);
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && !afterSql && ConsumesWait(eventData.Context)) { Armed = false; Cuts++; throw Cause; }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && afterSql && eventData.Context!.ChangeTracker.Entries().Any(entry => entry.Entity is Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities.ActivityExecutionStateEntity))
            { Armed = false; Cuts++; Expire!(); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CommitAckLoss : DbTransactionInterceptor
    {
        public bool Armed; public int Losses;
        public IOException Cause { get; } = new("Constructed lost acknowledgment after actual exact-wait transaction commit.");
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (Armed) { Armed = false; Losses++; throw Cause; } return Task.CompletedTask; }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Ef_actual_observation_or_initial_marker_read_outage_preserves_wait_and_cancellation_cause(bool marker, bool unrequestedCancellation)
    {
        var cut = new InitialReadCut(marker, unrequestedCancellation);
        await using var harness = await Harness.Create(true, cut); await harness.Seed(); var expected = await harness.Capture();
        cut.Armed = true;
        if (marker)
        {
            var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => harness.Checkpoints.CommitAsync(Commit(expected), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
            Assert.Same(cut.Cause, failure.InnerException);
        }
        else
        {
            var failure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => harness.Bookmarks.ObserveAsync("target", "wait").AsTask());
            Assert.Same(cut.Cause, failure.InnerException);
        }
        Assert.Equal(1, cut.Cuts); Assert.Equal(0, await harness.Markers());
        Assert.Equal(expected.RegistrationCapture!.Generation, (await harness.Bookmarks.ObserveAsync("target", "wait"))!.Generation);
        Assert.Equal(ActivityExecutionStatus.Suspended, (await harness.Activities.FindAsync("target", "activity"))!.Status);
        await harness.Checkpoints.CommitAsync(Commit(expected), new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Equal(1, await harness.Markers()); Assert.Null(await harness.Bookmarks.FindAsync("target", "wait"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Requested_cancellation_propagates_before_observation_or_checkpoint_without_consumption(bool ef)
    {
        await using var harness = await Harness.Create(ef); await harness.Seed(); var expected = await harness.Capture();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Bookmarks.ObserveAsync("target", "wait", stop.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Checkpoints.CommitAsync(Commit(expected), new(RuntimeCheckpointPersistenceMode.Immediate), stop.Token).AsTask());
        Assert.Equal(0, await harness.Markers()); Assert.NotNull(await harness.Bookmarks.FindAsync("target", "wait"));
    }

    private sealed class InitialReadCut(bool marker, bool unrequestedCancellation) : DbCommandInterceptor
    {
        public bool Armed; public int Cuts;
        public Exception Cause { get; } = unrequestedCancellation ? new OperationCanceledException("Constructed unrequested provider cancellation.") : new IOException("Constructed physical authority read outage.");
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var table = marker ? RuntimeOperationalStateEfModule.CheckpointCommitTableName : BookmarkStateEfModule.TableName;
            if (Armed && command.CommandText.Contains(table, StringComparison.Ordinal)) { Armed = false; Cuts++; throw Cause; }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class ReconciliationReadFailure(CommitAckLoss ack, bool enabled) : DbCommandInterceptor
    {
        private bool failed;
        public IOException Cause { get; } = new("Constructed unavailable marker reconciliation after lost commit acknowledgment.");
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (enabled && ack.Losses > 0 && !failed && command.CommandText.Contains(RuntimeOperationalStateEfModule.CheckpointCommitTableName, StringComparison.Ordinal))
            { failed = true; throw Cause; }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection? connection;
        public RuntimeSqliteDbContext? Context { get; }
        public FakeTimeProvider Clock { get; } = new(Now);
        public IBookmarkStateStore Bookmarks { get; }
        public IActivityExecutionStateStore Activities { get; }
        public IWorkflowExecutionStateStore Executions { get; }
        public IRuntimeCheckpointCommitStore Checkpoints { get; }
        public IWorkflowExecutableStore Material { get; }
        public Func<Task>? BeforeRootWrite; public int RootCuts;
        private Harness(SqliteConnection? connection, IInterceptor[] interceptors)
        {
            this.connection = connection;
            if (connection is null)
            {
                var state = new InMemoryRuntimeCheckpointStoreState(); var ownership = new InMemoryExecutionLivenessStateStore();
                Material = new InMemoryWorkflowExecutableStore(); var references = new InMemoryWorkflowExecutableSourceReferenceStore((InMemoryWorkflowExecutableStore)Material, Clock);
                Bookmarks = new InMemoryBookmarkStateStore(); Activities = new InMemoryActivityExecutionStateStore();
                Executions = new InMemoryWorkflowExecutionStateStore(state, ownership);
                Checkpoints = new InMemoryRuntimeCheckpointCommitStore(Material, references, Executions, Activities, Bookmarks,
                    operationalStateStore: ownership, state: state, timeProvider: Clock, persistenceAccessContextAccessor: new Access(),
                    rootWriteLeaseManager: new ObservedLease(new WorkflowExecutableRootWriteLeaseManager(Material, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock), this));
            }
            else
            {
                var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
                options.AddInterceptors(interceptors);
                Context = new(options.Options); Material = new EfWorkflowExecutableStore(Context, new Access());
                Bookmarks = new EfBookmarkStateStore(Context, new Access()); Activities = new EfActivityExecutionStateStore(Context, new Access(), new Codec());
                Executions = new EfWorkflowExecutionStateStore(Context, new Access(), new Codec(), Clock);
                Checkpoints = new EfRuntimeCheckpointCommitStore(Context, new Access(), Clock,
                new ObservedLease(new WorkflowExecutableRootWriteLeaseManager(Material, Options.Create(new WorkflowExecutableGarbageCollectionOptions { RootWriteLeaseDuration = TimeSpan.FromMinutes(30) }), Clock), this));
            }
        }
        public RuntimeSqliteDbContext FreshContext() => new(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection!).Options);
        public static async Task<Harness> Create(bool ef, params IInterceptor[] interceptors)
        {
            SqliteConnection? connection = null;
            if (ef) { connection = new("Data Source=:memory:"); await connection.OpenAsync(); }
            var harness = new Harness(connection, interceptors); if (ef) await harness.Context!.Database.EnsureCreatedAsync(); return harness;
        }
        public async Task Seed()
        {
            await Material.SaveAsync(new(Pin, new ExecutableNode("node", "authored", "test/activity", "1.0.0",
                new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
            await Checkpoints.CommitAsync(new("seed", new("seed-checkpoint", "Seed", "target", Now, [], new Dictionary<string, string>()),
                new(new("target", RuntimeStateChangeOperation.Upsert, Workflow, new Dictionary<string, string>()), null, [], [], [], [], []),
                [], new Dictionary<string, string>()), new(RuntimeCheckpointPersistenceMode.Immediate));
            await Activities.SaveAsync(Suspended); await Bookmarks.SaveAsync(Bookmark);
        }
        public async Task<StimulusResumeRecipient> Capture() => StimulusResumeRecipient.From((await Bookmarks.ObserveAsync("target", "wait"))!,
            (await Executions.FindAsync("target"))!, Scope);
        public async Task<int> Markers() => (Context is null ? ((InMemoryRuntimeCheckpointCommitStore)Checkpoints).ListCommits().Count : await Context.RuntimeCheckpointCommits.CountAsync()) - 1;
        public async Task<int> OutboxCount() => Context is null ? (await ((IRuntimePostCommitOutboxStore)Checkpoints).GetDeliverableAsync(new(Now, 10))).Count : await Context.RuntimePostCommitOutbox.CountAsync();
        public async ValueTask DisposeAsync() { if (Context is not null) await Context.DisposeAsync(); if (connection is not null) await connection.DisposeAsync(); }
    }
}
