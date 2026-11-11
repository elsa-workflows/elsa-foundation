using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// #2564: a plain commit on a clean context does not read its marker before it runs. A commit whose marker is
/// durable fails at its marker insert at the latest, and the marker is read after a failure, so a replay still gets
/// the stored result or the replay conflict. A commit that carries an admission condition or a model decision, and
/// a commit on a context with pending changes or an open transaction, read the marker first. Each test counts the
/// reads of the marker table: a read is a SELECT.
/// </summary>
public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    private const string MarkerTable = $"\"{RuntimeOperationalStateEfModule.CheckpointCommitTableName}\"";
    private const string SchedulerStateTable = $"\"{RuntimeOperationalStateEfModule.SchedulerTableName}\"";
    private const string DurableValueTable = $"\"{RuntimeOperationalStateEfModule.DurableValueTableName}\"";

    [Fact]
    public async Task A_plain_commit_runs_the_commands_of_one_that_reads_its_marker_first_minus_that_read()
    {
        // A context with a pending change makes a commit read its marker first. That commit also saves the pending
        // row, so its commands are those of the plain commit plus the read and that one insert.
        await using var database = await TestDatabase.CreateAsync();

        async Task<string[]> CommandsOfAsync(string commitId, bool pendingSibling)
        {
            var capture = new CommandCaptureInterceptor();
            await using var context = database.Open("tenant-a", capture);
            if (pendingSibling)
                context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(ValueCommit(commitId), Decision());
            return capture.Commands.ToArray();
        }

        // The first commit inserts the value the next two update in the same way.
        await CommandsOfAsync("commit-warm-up", pendingSibling: false);
        var plain = await CommandsOfAsync("commit-plain", pendingSibling: false);
        var reading = await CommandsOfAsync("commit-reading", pendingSibling: true);

        Assert.DoesNotContain(plain, IsMarkerRead);
        Assert.Single(plain, IsMarkerInsert);
        var read = Assert.Single(reading, IsMarkerRead);
        Assert.Same(read, reading[0]);
        var sibling = Assert.Single(reading, command => command.StartsWith("INSERT", StringComparison.Ordinal) &&
            command.Contains(SchedulerStateTable, StringComparison.Ordinal));
        Assert.Equal(reading.Where(command => !ReferenceEquals(command, read) && !ReferenceEquals(command, sibling)), plain);
    }

    [Fact]
    public async Task A_replay_of_a_plain_commit_gets_the_stored_result_from_the_marker_read_after_its_attempt_fails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = WithPendingIntent("commit-replayed", "intent-1");
        RuntimeCheckpointCommitStoreResult first;
        await using (var seed = database.Open("tenant-a"))
            first = await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", capture);

        var replay = await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());

        Assert.Single(first.PendingPostCommitWorkIds);
        Assert.Equal(first.PendingPostCommitWorkIds, replay.PendingPostCommitWorkIds);
        Assert.Equal(first.ConsumedSchedulerWorkItemIds, replay.ConsumedSchedulerWorkItemIds);
        // The attempt ran first. Its marker insert failed on the marker's key, it rolled back, and the one marker
        // read came after that.
        var commands = capture.Commands.ToArray();
        Assert.Single(commands, IsMarkerRead);
        Assert.True(Array.FindIndex(commands, IsMarkerInsert) >= 0);
        Assert.True(Array.FindIndex(commands, IsMarkerInsert) < Array.FindIndex(commands, IsMarkerRead));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Null(context.Database.CurrentTransaction);
        await using var read = database.Open("tenant-a");
        Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(1, (await read.RuntimePostCommitOutbox.AsNoTracking().SingleAsync()).Revision);
    }

    [Fact]
    public async Task A_replay_of_a_plain_commit_with_other_content_is_refused_as_a_replay_conflict_and_changes_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var seed = database.Open("tenant-a"))
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a")).CommitAsync(ValueCommit("commit-conflict", "one"), Decision());
        await using var context = database.Open("tenant-a");

        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(ValueCommit("commit-conflict", "two"), Decision()).AsTask());

        Assert.Empty(context.ChangeTracker.Entries());
        await using var read = database.Open("tenant-a");
        Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(1, (await read.DurableValueStates.AsNoTracking().SingleAsync()).Revision);
    }

    [Theory]
    [InlineData("exact-bookmark-resume")]
    [InlineData("model-decision")]
    [InlineData("native-model-admission")]
    public async Task A_commit_that_carries_an_admission_condition_or_a_model_decision_reads_its_marker_before_anything_else(string shape)
    {
        // The commit id was committed with other content, so the marker read refuses this commit as a replay
        // conflict. Read first, that read is the only command: no transaction begins and nothing is staged.
        await using var database = await TestDatabase.CreateAsync();
        await using (var seed = database.Open("tenant-a"))
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a")).CommitAsync(Commit("commit-shaped"), Decision());
        var fence = new RuntimeExecutionFence("lease", "owner", 1);
        var commit = shape switch
        {
            "exact-bookmark-resume" => Commit("commit-shaped") with
            {
                Checkpoint = Commit("commit-shaped").Checkpoint with { Name = "ExactBookmarkClaim" },
                ExpectedBookmarkResume = StimulusResumeRecipient.From(CheckpointBookmark("bookmark-a", "payload"))
            },
            "model-decision" => Commit("commit-shaped") with
            {
                Checkpoint = Commit("commit-shaped").Checkpoint with { Name = "ModelDecision" },
                ExpectedFence = fence,
                ModelState = ModelDecision("workflow-a")
            },
            _ => Commit("commit-shaped") with
            {
                Checkpoint = Commit("commit-shaped").Checkpoint with { Name = RuntimeCheckpointNames.WorkflowStarted },
                ExpectedFence = fence,
                ExpectedModelExecution = ModelExecution("model-execution"),
                StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert,
                    Execution("workflow-a", "tenant-a") with { Partition = new("tenant-a"), Authority = ModelExecution("model-execution").Authority },
                    new Dictionary<string, string>()), null, [], [], [], [], [])
            }
        };
        var capture = new CommandCaptureInterceptor();
        var manager = new PassThroughRootWriteLeaseManager();
        await using var context = database.Open("tenant-a", capture);

        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
                .CommitAsync(commit, Decision()).AsTask());

        Assert.True(IsMarkerRead(Assert.Single(capture.Commands)));
        Assert.Empty(manager.LeaseIds);
    }

    [Fact]
    public async Task A_replay_on_a_context_with_pending_changes_reads_its_marker_first_and_keeps_those_changes()
    {
        // A failed attempt clears the tracker. Reading the marker first answers the replay without an attempt, so
        // what the caller staged on the context is still pending afterwards.
        await using var database = await TestDatabase.CreateAsync();
        var commit = WithPendingIntent("commit-pending", "intent-1");
        await using (var seed = database.Open("tenant-a"))
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", capture);
        var pending = SchedulerRow("tenant-a", "workflow-a");
        context.SchedulerStates.Add(pending);

        var replay = await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());

        Assert.Single(replay.PendingPostCommitWorkIds);
        Assert.True(IsMarkerRead(Assert.Single(capture.Commands)));
        Assert.Equal(EntityState.Added, context.Entry(pending).State);
    }

    [Fact]
    public async Task A_replay_inside_a_transaction_of_the_context_reads_its_marker_first_and_asks_for_no_root_write_lease()
    {
        // The commit changes a workflow row, so an attempt would ask the root write lease manager before it tries
        // to begin its transaction. Read first, the replay is answered without either.
        await using var database = await TestDatabase.CreateAsync();
        var commit = WorkflowRootCommit("commit-in-transaction");
        await using (var seed = database.Open("tenant-a"))
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager())
                .CommitAsync(commit, Decision());
        var capture = new CommandCaptureInterceptor();
        var manager = new PassThroughRootWriteLeaseManager();
        await using var context = database.Open("tenant-a", capture);
        await using var transaction = await context.Database.BeginTransactionAsync();

        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
            .CommitAsync(commit, Decision());

        Assert.Empty(manager.LeaseIds);
        Assert.True(IsMarkerRead(Assert.Single(capture.Commands)));
        Assert.Same(transaction, context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("same-content")]
    [InlineData("other-content")]
    [InlineData("not-committed")]
    public async Task A_refusal_of_the_root_write_lease_manager_is_followed_by_one_marker_read_that_decides_what_leaves(string committed)
    {
        // The manager refuses before the commit's callback runs, so no attempt ran and no marker read followed.
        // The marker is read once before the refusal leaves: a commit that is durable gets its stored result, a
        // commit id that is durable with other content is refused as a replay conflict, and otherwise the refusal
        // leaves as it is.
        await using var database = await TestDatabase.CreateAsync();
        if (committed != "not-committed")
        {
            await using var seed = database.Open("tenant-a");
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager())
                .CommitAsync(WorkflowRootCommit("commit-refused", committed == "same-content" ? "one" : "two"), Decision());
        }
        var capture = new CommandCaptureInterceptor();
        var manager = new PassThroughRootWriteLeaseManager { FailFirstAttempt = true };
        await using var context = database.Open("tenant-a", capture);
        Func<Task> act = () => new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
            .CommitAsync(WorkflowRootCommit("commit-refused", "one"), Decision()).AsTask();

        if (committed == "same-content")
            await act();
        else if (committed == "other-content")
            await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(act);
        else
            await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(act);

        Assert.Single(manager.LeaseIds);
        Assert.True(IsMarkerRead(Assert.Single(capture.Commands)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Initial_native_model_work_without_its_condition_is_refused_only_after_one_marker_read_finds_no_other_answer(bool commitIdIsDurable)
    {
        // The refusal is thrown before any attempt. A commit whose marker is durable gets its replay answer first,
        // here the replay conflict of a commit id that was committed with other content.
        await using var database = await TestDatabase.CreateAsync();
        if (commitIdIsDurable)
        {
            await using var seed = database.Open("tenant-a");
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a")).CommitAsync(Commit("commit-model-work"), Decision());
        }
        var model = ModelExecution("model-execution");
        var native = Execution("workflow-a", "tenant-a");
        var origin = new RuntimeModelNativeWorkOrigin(1, model.Scope, model.ExecutionId, model.Protocol, model.Executable,
            model.MaterialIncarnationId, "service", "invocation", "attempt", native.PinnedExecutable, native.WorkflowExecutionId,
            "source-commit:start", "start", "test.native-result", "result-input");
        var commit = Commit("commit-model-work") with
        {
            Checkpoint = Commit("commit-model-work").Checkpoint with { Name = RuntimeCheckpointNames.WorkflowStarted },
            StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert, native with { ModelWorkOrigin = origin },
                new Dictionary<string, string>()), null, [], [], [], [], [])
        };
        var capture = new CommandCaptureInterceptor();
        var manager = new PassThroughRootWriteLeaseManager();
        await using var context = database.Open("tenant-a", capture);
        Func<Task> act = () => new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
            .CommitAsync(commit, Decision()).AsTask();

        if (commitIdIsDurable)
            await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(act);
        else
            await Assert.ThrowsAsync<NotSupportedException>(act);

        Assert.Empty(manager.LeaseIds);
        Assert.True(IsMarkerRead(Assert.Single(capture.Commands)));
    }

    [Fact]
    public async Task A_failed_attempt_whose_marker_read_finds_nothing_is_not_followed_by_a_second_marker_read()
    {
        await using var database = await TestDatabase.CreateAsync();
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", capture);
        var commit = Commit("commit-staging-fails");
        commit = commit with
        {
            StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems([new ConsumedSchedulerWorkItem("workflow-a", "work-missing", "worker-a", 1)])
        };

        await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision()).AsTask());

        Assert.Single(capture.Commands, IsMarkerRead);
        Assert.False(IsMarkerRead(capture.Commands.First()));
    }

    [Fact]
    public async Task A_unique_key_conflict_of_the_first_save_whose_marker_read_finds_nothing_is_not_followed_by_a_second_marker_read()
    {
        // Another writer inserted the row after this commit's staging read found none, so the save's insert fails
        // on the row's key. The staging read is made to return no rows to stand in for that order of events.
        await using var database = await TestDatabase.CreateAsync();
        await using (var seed = database.Open("tenant-a"))
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a")).CommitAsync(ValueCommit("commit-other-writer"), Decision());
        var capture = new CommandCaptureInterceptor();
        var stagingFindsNoRow = new ReadFindsNoRows(command => command.Transaction is not null && IsRead(command.CommandText, DurableValueTable));
        await using var context = database.Open("tenant-a", capture, stagingFindsNoRow);

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(ValueCommit("commit-loses-the-race"), Decision()).AsTask());

        Assert.True(EfRelationalExceptionClassifier.IsSaveConflict(failure, EfWriteConflict.UniqueKey));
        Assert.Equal(1, stagingFindsNoRow.Hits);
        Assert.Single(capture.Commands, IsMarkerRead);
        Assert.DoesNotContain(capture.Commands, IsMarkerInsert);
    }

    [Fact]
    public async Task A_marker_read_follows_the_failure_of_a_later_attempt_when_the_read_after_an_earlier_attempt_found_nothing()
    {
        // The first attempt conflicts on the fence row with its owner's heartbeat. The marker read after it finds
        // nothing and the commit runs again. The second attempt fails as a provider's timeout, which no marker read
        // follows inside the attempt. What the first read found says nothing about this failure, so the marker is
        // read once more before it leaves.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        RuntimeExecutionLease lease;
        await using (var seed = database.Open(CursorScope))
            lease = await OwnershipNode(seed, clock, "node-a").AcquireAsync(CursorRun);
        var capture = new CommandCaptureInterceptor();
        var heartbeat = new OwnerHeartbeatRaces(1);
        var secondAttemptTimesOut = new OnCommand(command => heartbeat.Races == 1 && command.Transaction is not null &&
            IsRead(command.CommandText, DurableValueTable))
        {
            Then = () => throw new TaskCanceledException("Simulated provider timeout.")
        };
        await using var context = database.Open(CursorScope, capture, heartbeat, secondAttemptTimesOut);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CursorStore(context, null, clock).CommitAsync(ValueCommit("commit-two-attempts") with { ExpectedFence = lease.ToFence() }, Decision()).AsTask());

        Assert.Equal(1, heartbeat.Races);
        Assert.Equal(1, secondAttemptTimesOut.Hits);
        Assert.Equal(2, capture.Commands.Count(IsMarkerRead));
    }

    [Fact]
    public async Task A_marker_read_follows_the_failure_of_a_later_attempt_when_an_earlier_attempt_had_asked_its_transaction_to_commit()
    {
        // The built-in root write lease manager runs the commit's callback once. A replacement may run it again.
        // Here the first run fails at its transaction's commit and the second fails while staging, both as a
        // provider's timeout. That the first run had asked for its commit says nothing about the second, so the
        // marker is read once before the second failure leaves.
        await using var database = await TestDatabase.CreateAsync();
        var capture = new CommandCaptureInterceptor();
        var commitTimesOut = new CommitReportedAsCancelled();
        var secondRunTimesOut = new OnCommand(command => commitTimesOut.Hits == 1 && command.Transaction is not null &&
            IsRead(command.CommandText, WorkflowExecutionTable))
        {
            Then = () => throw new TaskCanceledException("Simulated provider timeout.")
        };
        var manager = new RunsItsCallbackAgainAfterACancellation();
        await using var context = database.Open("tenant-a", capture, commitTimesOut, secondRunTimesOut);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
                .CommitAsync(WorkflowRootCommit("commit-run-twice"), Decision()).AsTask());

        Assert.Equal(2, manager.Runs);
        Assert.Equal(1, commitTimesOut.Hits);
        Assert.Equal(1, secondRunTimesOut.Hits);
        Assert.Single(capture.Commands, IsMarkerRead);
        await using var read = database.Open("tenant-a");
        Assert.Empty(await read.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task A_commit_that_fails_after_asking_its_transaction_to_commit_is_not_followed_by_a_marker_read()
    {
        // The marker insert had succeeded, so no marker was durable before this attempt and there is no replay to
        // answer. The provider reports its timeout as a cancellation, which is the failure no marker read follows
        // inside the attempt either.
        await using var database = await TestDatabase.CreateAsync();
        var capture = new CommandCaptureInterceptor();
        var commitTimesOut = new CommitReportedAsCancelled();
        await using var context = database.Open("tenant-a", capture, commitTimesOut);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(ValueCommit("commit-times-out"), Decision()).AsTask());

        Assert.Equal(1, commitTimesOut.Hits);
        Assert.Single(capture.Commands, IsMarkerInsert);
        Assert.DoesNotContain(capture.Commands, IsMarkerRead);
        await using var read = database.Open("tenant-a");
        Assert.Empty(await read.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task A_cancelled_call_leaves_with_its_own_cancellation_and_no_marker_read()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var call = new CancellationTokenSource();
        var capture = new CommandCaptureInterceptor();
        var manager = new CancellingRootWriteLeaseManager(call);
        await using var context = database.Open("tenant-a", capture);

        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
                .CommitAsync(WorkflowRootCommit("commit-cancelled"), Decision(), call.Token).AsTask());

        Assert.Same(manager.Thrown, cancellation);
        Assert.Empty(capture.Commands);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_attempt_that_fails_as_a_provider_timeout_reported_as_a_cancellation_is_followed_by_one_marker_read(bool committedBefore)
    {
        // Inside the attempt no marker read follows a cancellation. While the call's token is not cancelled it is a
        // provider's timeout, and the marker is read once before it leaves: a commit that is durable gets its
        // stored result.
        await using var database = await TestDatabase.CreateAsync();
        var commit = ValueCommit("commit-timeout");
        if (committedBefore)
        {
            await using var seed = database.Open("tenant-a");
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision());
        }
        var capture = new CommandCaptureInterceptor();
        var stagingTimesOut = new OnCommand(command => command.Transaction is not null && IsRead(command.CommandText, DurableValueTable))
        {
            Then = () => throw new TaskCanceledException("Simulated provider timeout.")
        };
        await using var context = database.Open("tenant-a", capture, stagingTimesOut);
        Func<Task> act = () => new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(commit, Decision()).AsTask();

        if (committedBefore)
            await act();
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(act);

        Assert.Equal(1, stagingTimesOut.Hits);
        Assert.Single(capture.Commands, IsMarkerRead);
        Assert.DoesNotContain(capture.Commands, IsMarkerInsert);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("reads-the-row")]
    [InlineData("uses-its-cursor")]
    public async Task A_drain_that_lost_its_run_and_replays_a_committed_commit_gets_the_stored_result(string fence)
    {
        // The fence is not part of a commit's identity: a commit that is durable stays a replay after its drain
        // lost the run. It is not refused as stale.
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        var lease = await AcquireWithCursorAsync(database, cursors, clock);
        var commit = WithPendingIntent("commit-lost-drain", "intent-1") with { ExpectedFence = lease.ToFence() };
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var store = CursorStore(context, fence == "uses-its-cursor" ? cursors : null, clock);
        var first = await store.CommitAsync(commit, Decision());
        await using (var other = database.Open(CursorScope))
            await OwnershipNode(other, clock, "node-b").AcquireAsync(CursorRun);
        var takenOver = await OwnershipRevisionAsync(database);
        var before = capture.Commands.Count;

        var replay = await store.CommitAsync(commit, Decision());

        Assert.Single(first.PendingPostCommitWorkIds);
        Assert.Equal(first.PendingPostCommitWorkIds, replay.PendingPostCommitWorkIds);
        // Reading the row refuses the fence before anything is written. With a cursor the save's bump matches no row.
        Assert.Equal(fence == "uses-its-cursor" ? (0, 1) : (1, 0), OwnershipCommands(capture, before));
        Assert.Single(capture.Commands.Skip(before), IsMarkerRead);
        Assert.Equal(takenOver, await OwnershipRevisionAsync(database));
        await using var read = database.Open(CursorScope);
        Assert.Single(await read.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task A_lost_drain_whose_replay_finds_it_needs_a_root_lease_gets_the_stored_result_and_the_root_lease_manager_is_not_asked()
    {
        // The replay hopes to update its root without a lease and finds the stored root pinned otherwise. The look
        // at the ownership row then refuses the drain as stale, because another node took the run. The commit is
        // durable, so the marker read that follows answers it as a replay, and the lease manager is not asked.
        await using var database = await TestDatabase.CreateAsync();
        var lease = await SeedRetainedRootAsync(database);
        var clock = new FixedTimeProvider(OccurredAt);
        var cursors = new EfExecutionFenceCursors(clock);
        string stamp;
        await using (var read = database.Open(CursorScope))
            stamp = (await read.ExecutionLivenessStates.AsNoTracking().SingleAsync()).SchemaVersion;
        cursors.Hold(CursorKey(lease), new(await OwnershipRevisionAsync(database), lease.ExpiresAt, stamp));
        var original = RetainedRootCommit(lease).StateChanges.WorkflowExecution!.State;
        RuntimeCheckpointCommit Pinning(string commitId, string hash) => Commit(commitId) with
        {
            ExpectedFence = lease.ToFence(),
            StateChanges = new(new(CursorRun, RuntimeStateChangeOperation.Upsert,
                original with { PinnedExecutable = original.PinnedExecutable with { ArtifactHash = hash } }, new Dictionary<string, string>()),
                null, [], [], [], [], [])
        };
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open(CursorScope, capture);
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var hint = sessions.Push(RootHint());
        var manager = new PassThroughRootWriteLeaseManager();
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor(CursorScope), clock, manager)
        { CoalescingSessions = sessions, FenceCursors = cursors };
        // Each of these two commits changes the root's pin, so each takes a root write lease.
        var repinned = Pinning("commit-repinned", "another-hash");
        await store.CommitAsync(repinned, Decision());
        await store.CommitAsync(Pinning("commit-pinned-back", original.PinnedExecutable.ArtifactHash), Decision());
        Assert.Equal(2, manager.LeaseIds.Count);
        await using (var other = database.Open(CursorScope))
            await OwnershipNode(other, clock, "node-b").AcquireAsync(CursorRun);
        var before = capture.Commands.Count;

        await store.CommitAsync(repinned, Decision());

        Assert.Equal(2, manager.LeaseIds.Count);
        // The one read of the ownership row is the look. No bump was staged.
        Assert.Equal((1, 0), OwnershipCommands(capture, before));
        Assert.Single(capture.Commands.Skip(before), IsMarkerRead);
        await using var verification = database.Open(CursorScope);
        Assert.Equal(original.PinnedExecutable.ArtifactHash,
            (await new EfWorkflowExecutionStateStore(verification, new FixedAccessor(CursorScope), new NoopContinuationCodec()).FindAsync(CursorRun))!.PinnedExecutable.ArtifactHash);
    }

    [Fact]
    public async Task A_marker_read_that_fails_after_a_failure_outside_an_attempt_leaves_in_place_of_that_failure()
    {
        // The read did not complete, so it cannot say the commit is not durable. Its failure leaves, as the failure
        // of a marker read after a failed attempt does.
        await using var database = await TestDatabase.CreateAsync();
        var readFails = new OnCommand(command => IsMarkerRead(command.CommandText))
        {
            Then = () => throw new TimeoutException("Simulated failure of the marker read.")
        };
        var manager = new PassThroughRootWriteLeaseManager { FailFirstAttempt = true };
        await using var context = database.Open("tenant-a", readFails);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"), new FixedTimeProvider(OccurredAt), manager)
                .CommitAsync(WorkflowRootCommit("commit-read-fails"), Decision()).AsTask());

        Assert.Single(manager.LeaseIds);
        Assert.Equal(1, readFails.Hits);
    }

    private static bool IsMarkerRead(string command) => IsRead(command, MarkerTable);

    private static bool IsMarkerInsert(string command) =>
        command.StartsWith("INSERT", StringComparison.Ordinal) && command.Contains(MarkerTable, StringComparison.Ordinal);

    /// <summary>A plain commit that writes one durable value. The value is part of the commit's fingerprint.</summary>
    private static RuntimeCheckpointCommit ValueCommit(string commitId, string? value = null) => Commit(commitId) with
    {
        StateChanges = new(null, null, [], [],
            [new("value-a", RuntimeStateChangeOperation.Upsert, DurableValue("value-a", value ?? commitId), new Dictionary<string, string>())], [], [])
    };

    /// <summary>A plain commit that writes the workflow row, which pins an executable and so needs a root write lease.</summary>
    private static RuntimeCheckpointCommit WorkflowRootCommit(string commitId, string value = "one") => Commit(commitId) with
    {
        StateChanges = new(new("workflow-a", RuntimeStateChangeOperation.Upsert,
            Execution("workflow-a", "tenant-a") with { SystemMetadata = new Dictionary<string, string> { ["business-value"] = value } },
            new Dictionary<string, string>()), null, [], [], [], [], [])
    };

    private static RuntimeModelExecutionState ModelExecution(string executionId)
    {
        using var payload = JsonDocument.Parse("{}");
        return new(1, new("tenant-a"), executionId, "tenant-a", new("test.model", 1),
            new WorkflowExecutableIdentity("model-artifact", "process", "process-v1", "1.0.0", "model-hash"), "incarnation-1", 1,
            RuntimeModelLifecycle.Running, new("system", "initiator"), OccurredAt, OccurredAt, payload.RootElement, null);
    }

    private static RuntimeModelStateChange ModelDecision(string executionId)
    {
        using var result = JsonDocument.Parse("{\"outcome\":\"completed\"}");
        return new(null, ModelExecution(executionId),
            new(new("tenant-a"), executionId, "input-1", "test.input", "content-1", 1, OccurredAt, null, null, result.RootElement));
    }

    /// <summary>Fails the commit of every transaction of the context the way a provider reports a timeout.</summary>
    private sealed class CommitReportedAsCancelled : DbTransactionInterceptor
    {
        public int Hits { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Hits++;
            throw new TaskCanceledException("Simulated provider timeout at commit.");
        }
    }

    /// <summary>Makes the first read that matches return no rows.</summary>
    private sealed class ReadFindsNoRows(Func<DbCommand, bool> matches) : DbCommandInterceptor
    {
        public int Hits { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Hits == 0 && matches(command))
            {
                Hits++;
                command.CommandText = $"SELECT * FROM ({command.CommandText}) WHERE 0";
            }
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Runs the commit's callback a second time when the first run leaves as a cancellation.</summary>
    private sealed class RunsItsCallbackAgainAfterACancellation : IWorkflowExecutableRootWriteLeaseManager
    {
        public int Runs { get; private set; }

        public async ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        {
            try
            {
                Runs++;
                await write(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Runs++;
                await write(cancellationToken);
            }
        }
    }

    /// <summary>Cancels the call and leaves with that cancellation, before the commit's callback runs.</summary>
    private sealed class CancellingRootWriteLeaseManager(CancellationTokenSource call) : IWorkflowExecutableRootWriteLeaseManager
    {
        public OperationCanceledException? Thrown { get; private set; }

        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
        {
            call.Cancel();
            throw Thrown = new OperationCanceledException(call.Token);
        }
    }
}
