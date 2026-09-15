using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeCheckpointCommitStoreTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Marker_is_create_only_replay_safe_scope_isolated_and_restartable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = Commit("commit-1");

        await using (var tenantA = database.Open("tenant-a"))
        {
            var store = new EfRuntimeCheckpointCommitStore(tenantA, new FixedAccessor("tenant-a"));
            var first = await store.CommitAsync(commit, Decision());
            var replay = await store.CommitAsync(commit, Decision());

            Assert.Empty(first.PendingPostCommitWorkIds);
            Assert.Empty(replay.PendingPostCommitWorkIds);
            await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
                store.CommitAsync(commit with { Checkpoint = commit.Checkpoint with { Name = "different" } }, Decision()).AsTask());
        }

        await using (var tenantB = database.Open("tenant-b"))
        {
            var store = new EfRuntimeCheckpointCommitStore(tenantB, new FixedAccessor("tenant-b"));
            await store.CommitAsync(commit, Decision());
            Assert.Single(await tenantB.RuntimeCheckpointCommits.Where(row => row.ScopeKey == EfRelationalIdentity.Encode("tenant-b")).ToArrayAsync());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Single(await restarted.RuntimeCheckpointCommits.Where(row => row.ScopeKey == EfRelationalIdentity.Encode("tenant-a")).ToArrayAsync());
        var row = await restarted.RuntimeCheckpointCommits.SingleAsync(row => row.ScopeKey == EfRelationalIdentity.Encode("tenant-a"));
        Assert.Equal(EfRelationalIdentity.Encode("tenant-a"), row.ScopeKey);
        Assert.Equal(EfRelationalIdentity.Encode(commit.CommitId), row.CommitId);
    }

    [Fact]
    public async Task Marker_commit_flushes_a_pre_staged_sibling_in_the_same_transaction()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new ParticipantOrderAndFailureInterceptor();
        await using var context = database.Open("tenant-a", interceptor);
        context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));

        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        await store.CommitAsync(Commit("commit-sibling"), Decision());

        await using var reopened = database.Open("tenant-a");
        Assert.Single(await reopened.SchedulerStates.ToArrayAsync());
        Assert.Single(await reopened.RuntimeCheckpointCommits.ToArrayAsync());
        var schedulerIndex = interceptor.Tables.IndexOf("elsa_runtime_scheduler_state");
        var markerIndex = interceptor.Tables.IndexOf("elsa_runtime_checkpoint_commit");
        Assert.True(schedulerIndex >= 0 && markerIndex > schedulerIndex, "The immutable checkpoint marker must be inserted after staged participant rows.");
    }

    [Fact]
    public async Task Damaged_marker_scope_projection_fails_closed_instead_of_becoming_a_false_new_commit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = WithBookmark("commit-marker-drift", CheckpointBookmark("bookmark-marker-drift", "original"),
            RuntimeStateChangeOperation.Upsert);
        await using (var seed = database.Open("tenant-a"))
        {
            await new EfRuntimeCheckpointCommitStore(seed, new FixedAccessor("tenant-a"))
                .CommitAsync(commit, Decision());
            await seed.RuntimeCheckpointCommits.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.ScopeKey, EfRelationalIdentity.Encode("tenant-b")));
        }

        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", capture);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(commit, Decision()).AsTask());
        Assert.DoesNotContain(capture.Commands, command => command.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase));
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Single(await context.Bookmarks.ToArrayAsync());
    }

    [Fact]
    public async Task Nonempty_execution_scheduler_and_fence_commit_as_one_replayable_unit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var now = OccurredAt;
        var scope = "tenant-a";
        await using (var context = database.Open(scope))
        {
            var accessor = new FixedAccessor(scope);
            var liveness = new EfExecutionLivenessStateStore(context, accessor, new NoopContinuationCodec());
            var lease = new RuntimeExecutionLease("lease-a", "workflow-a", "owner-a", now, now.AddMinutes(5), 1);
            await liveness.SaveAsync(new ExecutionLivenessState("ownership:workflow-a", "workflow-a", lease, null, null, null));

            var commit = Commit("commit-nonempty") with
            {
                ExpectedFence = lease.ToFence(),
                StateChanges = new RuntimeCheckpointStateChangeSet(
                    new RuntimeStateChange<WorkflowExecutionState>("workflow-a", RuntimeStateChangeOperation.Upsert, Execution("workflow-a", scope), new Dictionary<string, string>()),
                    new RuntimeStateChange<SchedulerState>("workflow-a", RuntimeStateChangeOperation.Upsert, new SchedulerState("workflow-a", 7), new Dictionary<string, string>()),
                    [], [], [], [], [])
            };

            var manager = new PassThroughRootWriteLeaseManager();
            var store = new EfRuntimeCheckpointCommitStore(context, accessor, new FixedTimeProvider(now), manager);
            var first = await store.CommitAsync(commit, Decision());
            var replay = await store.CommitAsync(commit, Decision());

            Assert.Empty(first.PendingPostCommitWorkIds);
            Assert.Empty(replay.PendingPostCommitWorkIds);
            Assert.Equal("artifact-workflow-a", manager.ArtifactId);
            Assert.Equal("checkpoint:commit-nonempty", manager.LeaseId);
            Assert.Equal(1, (await context.WorkflowExecutionStates.SingleAsync()).Revision);
            Assert.Equal(0, (await context.WorkflowRunHealthStates.SingleAsync()).IncidentCount);
            Assert.Equal(1, (await context.SchedulerStates.SingleAsync()).Revision);
            Assert.Equal(2, (await context.ExecutionLivenessStates.SingleAsync()).Revision);
            Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
        }
    }

    [Fact]
    public async Task Folded_pending_outbox_is_committed_and_replayed_with_the_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = WithPendingIntent("commit-outbox", "intent-outbox");
        var id = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, "intent-outbox");

        await using (var context = database.Open("tenant-a"))
        {
            var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
            Assert.Equal([id], (await store.CommitAsync(commit, Decision())).PendingPostCommitWorkIds);
            Assert.Equal([id], (await store.CommitAsync(commit, Decision())).PendingPostCommitWorkIds);
            var outbox = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));
            Assert.Equal(id, (await outbox.FindAsync(id))!.OutboxItemId);
            Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Equal([id], (await new EfRuntimeCheckpointCommitStore(restarted, new FixedAccessor("tenant-a"))
            .CommitAsync(commit, Decision())).PendingPostCommitWorkIds);
        Assert.Equal(id, (await new EfRuntimePostCommitOutboxStore(restarted, new FixedAccessor("tenant-a"))
            .FindAsync(id))!.OutboxItemId);
    }

    [Fact]
    public async Task Ordinary_dispatch_and_pending_outbox_commit_atomically_before_the_replay_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        var dispatch = PendingDispatch("workflow-a", "activity-checkpoint", "tenant-a");
        var commit = WithPendingDispatch("commit-dispatch-outbox", "start-child", dispatch);
        var outboxId = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, "start-child");

        await using (var context = database.Open("tenant-a"))
        {
            var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
            Assert.Equal([outboxId], (await store.CommitAsync(commit, Decision())).PendingPostCommitWorkIds);
            Assert.Equal([outboxId], (await store.CommitAsync(commit, Decision())).PendingPostCommitWorkIds);
            Assert.Equal(WorkflowDispatchStatus.Pending,
                (await new EfWorkflowDispatchStore(context, new FixedAccessor("tenant-a"))
                    .FindAsync(dispatch.DispatchId))!.Status);
            Assert.Equal(outboxId,
                (await new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"))
                    .FindAsync(outboxId))!.OutboxItemId);
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Single(await restarted.WorkflowDispatches.ToArrayAsync());
        Assert.Single(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
    }

    [Fact]
    public async Task Marker_failure_rolls_back_dispatch_and_outbox_and_allows_clean_retry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var dispatch = PendingDispatch("workflow-a", "activity-rolled-back", "tenant-a");
        var commit = WithPendingDispatch("commit-dispatch-rollback", "intent-rolled-back", dispatch);
        var interceptor = new FailMarkerInsertInterceptor();

        await using (var context = database.Open("tenant-a", interceptor))
        {
            interceptor.Arm();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                    .CommitAsync(commit, Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var retry = database.Open("tenant-a");
        Assert.Empty(await retry.WorkflowDispatches.ToArrayAsync());
        Assert.Empty(await retry.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Empty(await retry.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Single((await new EfRuntimeCheckpointCommitStore(retry, new FixedAccessor("tenant-a"))
            .CommitAsync(commit, Decision())).PendingPostCommitWorkIds);
        Assert.Single(await retry.WorkflowDispatches.ToArrayAsync());
    }

    [Fact]
    public async Task Damaged_dispatch_projection_is_rejected_as_corrupt_not_missing_before_the_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var dispatch = PendingDispatch("workflow-a", "activity-projection", "tenant-a");
        await new EfWorkflowDispatchStore(context, access).SaveAsync(dispatch);
        var row = await context.WorkflowDispatches.SingleAsync();
        row.ScopeKeyHash = "damaged-scope-hash";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EfRuntimeCheckpointCommitStore(context, access)
                .CommitAsync(WithPendingDispatch("commit-damaged-dispatch", "intent-damaged-dispatch", dispatch),
                    Decision()).AsTask());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Empty(await context.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Single(await context.WorkflowDispatches.ToArrayAsync());
    }

    [Fact]
    public async Task Damaged_outbox_projection_is_rejected_as_corrupt_not_missing_before_the_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var commit = WithPendingIntent("commit-damaged-outbox", "intent-damaged-outbox");
        await new EfRuntimePostCommitOutboxStore(context, access)
            .SavePendingAsync(commit.StateChanges.PostCommitOutbox.Single().State);
        var row = await context.RuntimePostCommitOutbox.SingleAsync();
        row.ScopeKeyHash = "damaged-scope-hash";
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Single(await context.RuntimePostCommitOutbox.ToArrayAsync());
    }

    [Fact]
    public async Task Parent_cancellation_is_recorded_with_the_checkpoint_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var dispatch = PendingDispatch("workflow-a", "activity-cancel", "tenant-a", WorkflowDispatchMode.WaitForCompletion);
        var dispatchStore = new EfWorkflowDispatchStore(context, access);
        await dispatchStore.SaveAsync(dispatch);
        var request = new WorkflowDispatchCancellationRequest(
            dispatch.DispatchId, dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId, dispatch.ChildWorkflowExecutionId, OccurredAt.AddMinutes(1));
        var commit = Commit("commit-parent-cancel");
        commit = commit with { StateChanges = commit.StateChanges.WithWorkflowDispatchCancellations([request]) };

        await new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(commit, Decision());
        Assert.Equal(WorkflowDispatchStatus.Cancelled, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Claimed_scheduler_work_dispatch_and_outbox_share_the_marker_transaction_and_replay()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var queue = new EfSchedulerWorkQueueStore(context, access, new NoopContinuationCodec());
        await queue.EnqueueAsync(SchedulerWork("work-checkpoint"));
        var claim = (await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            "workflow-a", "worker-a", OccurredAt, TimeSpan.FromMinutes(1))))!;
        var dispatch = PendingDispatch("workflow-a", "activity-queue", "tenant-a");
        var commit = WithPendingDispatch("commit-queue", "intent-queue", dispatch);
        commit = commit with
        {
            StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems(
                [ConsumedSchedulerWorkItem.FromClaim(claim)])
        };

        var store = new EfRuntimeCheckpointCommitStore(context, access);
        var first = await store.CommitAsync(commit, Decision());
        Assert.Equal(["work-checkpoint"], first.ConsumedSchedulerWorkItemIds);
        Assert.Equal(first.ConsumedSchedulerWorkItemIds, (await store.CommitAsync(commit, Decision())).ConsumedSchedulerWorkItemIds);
        Assert.Empty(await context.SchedulerWorkItems.ToArrayAsync());
        Assert.Single(await context.WorkflowDispatches.ToArrayAsync());
        Assert.Single(await context.RuntimePostCommitOutbox.ToArrayAsync());

        await using var restarted = database.Open("tenant-a");
        Assert.Equal(["work-checkpoint"],
            (await new EfRuntimeCheckpointCommitStore(restarted, access).CommitAsync(commit, Decision()))
            .ConsumedSchedulerWorkItemIds);
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Marker_failure_restores_claimed_scheduler_work_and_sibling_writes_before_retry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new FailMarkerInsertInterceptor();
        RuntimeCheckpointCommit commit;
        await using (var context = database.Open("tenant-a", interceptor))
        {
            var access = new FixedAccessor("tenant-a");
            var queue = new EfSchedulerWorkQueueStore(context, access, new NoopContinuationCodec());
            await queue.EnqueueAsync(SchedulerWork("work-rollback"));
            var claim = (await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                "workflow-a", "worker-a", OccurredAt, TimeSpan.FromMinutes(1))))!;
            commit = WithPendingDispatch("commit-queue-rollback", "intent-queue-rollback",
                PendingDispatch("workflow-a", "activity-queue-rollback", "tenant-a"));
            commit = commit with
            {
                StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems(
                    [ConsumedSchedulerWorkItem.FromClaim(claim)])
            };

            interceptor.Arm();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(commit, Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Single(await restarted.SchedulerWorkItems.ToArrayAsync());
        Assert.Empty(await restarted.WorkflowDispatches.ToArrayAsync());
        Assert.Empty(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(["work-rollback"],
            (await new EfRuntimeCheckpointCommitStore(restarted, new FixedAccessor("tenant-a"))
                .CommitAsync(commit, Decision())).ConsumedSchedulerWorkItemIds);
        Assert.Empty(await restarted.SchedulerWorkItems.ToArrayAsync());
    }

    [Fact]
    public async Task Lost_scheduler_claim_rejects_the_entire_checkpoint_and_mismatched_workflow_fails_before_io()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var queue = new EfSchedulerWorkQueueStore(context, access, new NoopContinuationCodec());
        await queue.EnqueueAsync(SchedulerWork("work-fenced"));
        var original = (await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            "workflow-a", "worker-a", OccurredAt, TimeSpan.FromMinutes(1))))!;
        var successor = await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
            "workflow-a", "worker-b", OccurredAt.AddMinutes(2), TimeSpan.FromMinutes(1)));
        Assert.NotNull(successor);
        Assert.True(successor!.FencingToken > original.FencingToken);
        var commit = WithPendingDispatch("commit-lost-claim", "intent-lost-claim",
            PendingDispatch("workflow-a", "activity-lost-claim", "tenant-a"));
        commit = commit with
        {
            StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems(
                [ConsumedSchedulerWorkItem.FromClaim(original)])
        };

        await Assert.ThrowsAsync<RuntimeSchedulerWorkConsumeConflictException>(() =>
            new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(await context.WorkflowDispatches.ToArrayAsync());
        Assert.Empty(await context.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(successor.FencingToken, (await context.SchedulerWorkItems.SingleAsync()).ClaimToken);

        var wrongWorkflow = commit with
        {
            StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems(
                [ConsumedSchedulerWorkItem.FromClaim(original) with { WorkflowExecutionId = "wrong-workflow" }])
        };
        var commands = new CommandCaptureInterceptor();
        await using var beforeIo = database.Open("tenant-a", commands);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfRuntimeCheckpointCommitStore(beforeIo, access).CommitAsync(wrongWorkflow, Decision()).AsTask());
        Assert.Empty(commands.Commands);
    }

    [Fact]
    public async Task New_test_dispatch_and_test_execution_admit_the_same_open_scope_once_with_marker_replay()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var testScope = TestScope("scope-checkpoint");
        await new EfWorkflowTestScopeStore(context, access, new NoopContinuationCodec())
            .CreateAsync(testScope, OccurredAt);
        var dispatch = PendingDispatch("workflow-a", "activity-test", "tenant-a", testScope: testScope);
        var commit = Commit("commit-test-scope") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    "workflow-a", RuntimeStateChangeOperation.Upsert,
                    Execution("workflow-a", "tenant-a") with { TestScope = testScope, RunKind = WorkflowRunKind.TestRun },
                    new Dictionary<string, string>()),
                null, [], [], [], [], [],
                [new RuntimeStateChange<WorkflowDispatchRecord>(dispatch.DispatchId,
                    RuntimeStateChangeOperation.Upsert, dispatch, new Dictionary<string, string>())],
                null, null, null, null)
        };
        var manager = new PassThroughRootWriteLeaseManager();
        var store = new EfRuntimeCheckpointCommitStore(context, access, rootWriteLeaseManager: manager);
        await store.CommitAsync(commit, Decision());
        await store.CommitAsync(commit, Decision());

        Assert.Equal(1, (await context.WorkflowTestScopes.SingleAsync()).Revision);
        Assert.Single(await context.WorkflowExecutionStates.ToArrayAsync());
        Assert.Single(await context.WorkflowDispatches.ToArrayAsync());
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
        await using var restarted = database.Open("tenant-a");
        await new EfRuntimeCheckpointCommitStore(restarted, access).CommitAsync(commit, Decision());
        Assert.Equal(1, (await restarted.WorkflowTestScopes.SingleAsync()).Revision);
    }

    [Fact]
    public async Task Closed_test_scope_rejects_new_dispatch_and_rolls_back_outbox_and_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var scope = TestScope("scope-closed-checkpoint");
        var scopes = new EfWorkflowTestScopeStore(context, access, new NoopContinuationCodec());
        await scopes.CreateAsync(scope, OccurredAt);
        await scopes.CloseAsync(new WorkflowTestScopeCloseRequest(
            scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, OccurredAt.AddMinutes(1)));
        var commit = WithPendingDispatch("commit-closed-scope", "intent-closed-scope",
            PendingDispatch("workflow-a", "activity-closed", "tenant-a", testScope: scope));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(await context.WorkflowDispatches.ToArrayAsync());
        Assert.Empty(await context.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(WorkflowTestScopeState.Closing,
            (await scopes.FindAsync(scope.ScopeId))!.State);
    }

    [Fact]
    public async Task Marker_failure_rolls_back_test_scope_touch_and_test_dispatch_with_outbox()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new FailMarkerInsertInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            var access = new FixedAccessor("tenant-a");
            var scope = TestScope("scope-test-rollback");
            await new EfWorkflowTestScopeStore(context, access, new NoopContinuationCodec())
                .CreateAsync(scope, OccurredAt);
            var commit = WithPendingDispatch("commit-test-rollback", "intent-test-rollback",
                PendingDispatch("workflow-a", "activity-test-rollback", "tenant-a", testScope: scope));

            interceptor.Arm();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(commit, Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Equal(0, (await restarted.WorkflowTestScopes.SingleAsync()).Revision);
        Assert.Empty(await restarted.WorkflowDispatches.ToArrayAsync());
        Assert.Empty(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Durable_value_update_and_delete_join_the_checkpoint_marker_and_replay()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var values = new EfDurableValueStateStore(context, access, new NoopContinuationCodec());
        await values.SaveAsync(DurableValue("value-update", "before"));
        await values.SaveAsync(DurableValue("value-delete", "before"));
        var commit = Commit("commit-durable-value") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [],
                [new RuntimeStateChange<DurableValueState>(
                    "value-update", RuntimeStateChangeOperation.Upsert,
                    DurableValue("value-update", "after"), new Dictionary<string, string>()),
                 new RuntimeStateChange<DurableValueState>(
                    "value-delete", RuntimeStateChangeOperation.Delete,
                    DurableValue("value-delete", "before"), new Dictionary<string, string>())],
                [], [])
        };

        var store = new EfRuntimeCheckpointCommitStore(context, access);
        await store.CommitAsync(commit, Decision());
        await store.CommitAsync(commit, Decision());
        Assert.Equal("after", (await values.FindAsync("workflow-a", "value-update"))!.InlineValue!.Value.GetString());
        Assert.Null(await values.FindAsync("workflow-a", "value-delete"));

        await using var restarted = database.Open("tenant-a");
        await new EfRuntimeCheckpointCommitStore(restarted, access).CommitAsync(commit, Decision());
        Assert.Equal(2, (await restarted.DurableValueStates.SingleAsync()).Revision);
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Marker_failure_rolls_back_durable_value_mutation_without_a_hidden_retry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new FailMarkerInsertInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            var access = new FixedAccessor("tenant-a");
            await new EfDurableValueStateStore(context, access, new NoopContinuationCodec())
                .SaveAsync(DurableValue("value-rollback", "before"));
            var commit = Commit("commit-durable-rollback") with
            {
                StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [],
                    [new RuntimeStateChange<DurableValueState>(
                        "value-rollback", RuntimeStateChangeOperation.Upsert,
                        DurableValue("value-rollback", "after"), new Dictionary<string, string>())],
                    [], [])
            };
            interceptor.Arm();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(context, access).CommitAsync(commit, Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Equal("before", (await new EfDurableValueStateStore(restarted, new FixedAccessor("tenant-a"),
            new NoopContinuationCodec()).FindAsync("workflow-a", "value-rollback"))!.InlineValue!.Value.GetString());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Operational_upserts_are_marker_atomic_restartable_and_replay_safe()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var state = new ExecutionLivenessState("operational-checkpoint", "workflow-a", null, null, null, null);
        var initial = WithOperational("commit-operational-initial", state, RuntimeStateChangeOperation.Upsert);
        var update = WithOperational("commit-operational-update", state, RuntimeStateChangeOperation.Upsert);
        var store = new EfRuntimeCheckpointCommitStore(context, access);

        await store.CommitAsync(initial, Decision());
        await store.CommitAsync(initial, Decision());
        Assert.Equal(1, (await context.ExecutionLivenessStates.SingleAsync()).Revision);
        await store.CommitAsync(update, Decision());
        await store.CommitAsync(update, Decision());
        Assert.Equal(2, (await context.ExecutionLivenessStates.SingleAsync()).Revision);

        await using var restarted = database.Open("tenant-a");
        await new EfRuntimeCheckpointCommitStore(restarted, access).CommitAsync(initial, Decision());
        await new EfRuntimeCheckpointCommitStore(restarted, access).CommitAsync(update, Decision());
        Assert.Equal(2, await restarted.RuntimeCheckpointCommits.CountAsync());
        Assert.Equal(2, (await restarted.ExecutionLivenessStates.SingleAsync()).Revision);
    }

    [Fact]
    public async Task Operational_update_joins_fence_validation_and_marker_without_overwriting_ownership()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var now = OccurredAt;
        var access = new FixedAccessor("tenant-a");
        var lease = new RuntimeExecutionLease("lease-operational", "workflow-a", "owner-a",
            now, now.AddMinutes(5), 1);
        var ownership = new ExecutionLivenessState("ownership:workflow-a", "workflow-a", lease, null, null, null);
        await new EfExecutionLivenessStateStore(context, access, new NoopContinuationCodec()).SaveAsync(ownership);
        var state = new ExecutionLivenessState("operational-fenced", "workflow-a", null, null, null, null);
        var commit = WithOperational("commit-operational-fenced", state, RuntimeStateChangeOperation.Upsert) with
        {
            ExpectedFence = lease.ToFence()
        };

        await new EfRuntimeCheckpointCommitStore(context, access, new FixedTimeProvider(now))
            .CommitAsync(commit, Decision());
        Assert.Equal(2, (await context.ExecutionLivenessStates.SingleAsync(row => row.OperationalStateId ==
            EfRelationalIdentity.Encode("ownership:workflow-a"))).Revision);
        Assert.Equal(1, (await context.ExecutionLivenessStates.SingleAsync(row => row.OperationalStateId ==
            EfRelationalIdentity.Encode("operational-fenced"))).Revision);
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Operational_append_delete_and_reserved_ownership_fail_before_provider_io()
    {
        await using var database = await TestDatabase.CreateAsync();
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", capture);
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        var state = new ExecutionLivenessState("operational-a", "workflow-a", null, null, null, null);
        var reserved = new ExecutionLivenessState("ownership:workflow-a", "workflow-a", null, null, null, null);
        foreach (var unsupported in new[]
                 {
                     WithOperational("commit-operational-append", state, RuntimeStateChangeOperation.Append),
                     WithOperational("commit-operational-delete", state, RuntimeStateChangeOperation.Delete),
                     WithOperational("commit-reserved-ownership", reserved, RuntimeStateChangeOperation.Upsert)
                 })
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(unsupported, Decision()).AsTask());
        Assert.Empty(capture.Commands);
    }

    [Fact]
    public async Task Marker_failure_restores_operational_revision_and_detaches_failed_write()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new FailMarkerInsertInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            var access = new FixedAccessor("tenant-a");
            var state = new ExecutionLivenessState("operational-rollback", "workflow-a", null, null, null, null);
            await new EfExecutionLivenessStateStore(context, access, new NoopContinuationCodec()).SaveAsync(state);
            interceptor.Arm();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(context, access)
                    .CommitAsync(WithOperational("commit-operational-rollback", state,
                        RuntimeStateChangeOperation.Upsert), Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Equal(1, (await restarted.ExecutionLivenessStates.SingleAsync()).Revision);
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Bookmark_upsert_delete_and_stimulus_index_join_replayable_checkpoint_markers()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        var bookmarks = new EfBookmarkStateStore(context, access);
        var bookmark = CheckpointBookmark("bookmark-1", "before");
        await bookmarks.SaveAsync(bookmark);
        var update = WithBookmark("commit-bookmark-update", CheckpointBookmark("bookmark-1", "after"),
            RuntimeStateChangeOperation.Upsert);
        var delete = WithBookmark("commit-bookmark-delete", CheckpointBookmark("bookmark-1", "after"),
            RuntimeStateChangeOperation.Delete);
        var store = new EfRuntimeCheckpointCommitStore(context, access);

        await store.CommitAsync(update, Decision());
        await store.CommitAsync(update, Decision());
        Assert.Equal("after", (await bookmarks.FindAsync("workflow-a", "bookmark-1"))!.StimulusHash);
        await store.CommitAsync(delete, Decision());
        await store.CommitAsync(delete, Decision());
        Assert.Null(await bookmarks.FindAsync("workflow-a", "bookmark-1"));

        await using var restarted = database.Open("tenant-a");
        await new EfRuntimeCheckpointCommitStore(restarted, access).CommitAsync(update, Decision());
        await new EfRuntimeCheckpointCommitStore(restarted, access).CommitAsync(delete, Decision());
        Assert.Empty(await restarted.Bookmarks.ToArrayAsync());
        Assert.Equal(2, await restarted.RuntimeCheckpointCommits.CountAsync());
    }

    [Fact]
    public async Task Marker_failure_rolls_back_bookmark_update_and_avoids_sibling_flush()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new FailMarkerInsertInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            var access = new FixedAccessor("tenant-a");
            await new EfBookmarkStateStore(context, access).SaveAsync(CheckpointBookmark("bookmark-rollback", "before"));
            interceptor.Arm();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(context, access)
                    .CommitAsync(WithBookmark("commit-bookmark-rollback",
                        CheckpointBookmark("bookmark-rollback", "after"),
                        RuntimeStateChangeOperation.Upsert), Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Equal("before", (await new EfBookmarkStateStore(restarted, new FixedAccessor("tenant-a"))
            .FindAsync("workflow-a", "bookmark-rollback"))!.StimulusHash);
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Scope_cleanup_removes_exact_bookmark_timer_and_queue_resources_with_a_replayable_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var access = new FixedAccessor("tenant-a");
        await SeedCleanupResourcesAsync(context, access);
        var commit = WithScopeCleanup("commit-scope-cleanup");
        var store = new EfRuntimeCheckpointCommitStore(context, access);

        await store.CommitAsync(commit, Decision());
        await store.CommitAsync(commit, Decision());
        Assert.Empty(await context.Bookmarks.ToArrayAsync());
        Assert.Empty(await context.DurableTimers.ToArrayAsync());
        Assert.Empty(await context.SchedulerWorkItems.ToArrayAsync());
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());

        await using var restarted = database.Open("tenant-a");
        await new EfRuntimeCheckpointCommitStore(restarted, access).CommitAsync(commit, Decision());
        Assert.Empty(await restarted.Bookmarks.ToArrayAsync());
    }

    [Fact]
    public async Task Activity_execution_upsert_and_bookmark_commit_once_with_the_checkpoint_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = WithActivityExecution("commit-activity", CheckpointActivity("activity-a"),
            CheckpointBookmark("bookmark-activity", "activity"));
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
        await store.CommitAsync(commit, Decision());
        await store.CommitAsync(commit, Decision());
        Assert.Single(await context.ActivityExecutionStates.ToArrayAsync());
        Assert.Single(await context.Bookmarks.ToArrayAsync());
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.True((await context.ActivityExecutionStates.SingleAsync()).Revision > 0);
    }

    [Fact]
    public async Task Workflow_activity_inspection_incident_and_run_health_share_one_replayable_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var commit = WithWorkflowIncidentAndInspection("commit-full-projection");
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"),
            new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager());

        await store.CommitAsync(commit, Decision());
        await store.CommitAsync(commit, Decision());
        Assert.Single(await context.WorkflowExecutionStates.ToArrayAsync());
        Assert.Single(await context.ActivityExecutionStates.ToArrayAsync());
        Assert.Single(await context.ActivityExecutionInspections.ToArrayAsync());
        Assert.Single(await context.ActivityExecutionHierarchies.ToArrayAsync());
        Assert.Single(await context.IncidentStates.ToArrayAsync());
        Assert.Equal(1, (await context.WorkflowRunHealthStates.SingleAsync()).IncidentCount);
        Assert.Equal(1, (await context.WorkflowRunHealthStates.SingleAsync()).IncidentBearingCount);
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    /// <summary>
    /// One execution commits from more than one scope: a nested drain commits in its own scope between two commits of
    /// the outer one. The outer context must stage against the row as it now is, not the snapshot its own earlier
    /// commit left tracked, or the revision check refuses a legitimate commit.
    /// </summary>
    [Fact]
    public async Task A_commit_stages_against_current_rows_after_another_context_committed_the_same_execution()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var outer = database.Open("tenant-a");
        await using var nested = database.Open("tenant-a");
        var outerStore = new EfRuntimeCheckpointCommitStore(outer, new FixedAccessor("tenant-a"));
        var nestedStore = new EfRuntimeCheckpointCommitStore(nested, new FixedAccessor("tenant-a"));

        await outerStore.CommitAsync(WithInspection("commit-scheduled", ActivityExecutionStatus.Scheduled), Decision());
        await outerStore.CommitAsync(WithInspection("commit-running", ActivityExecutionStatus.Running), Decision());
        await nestedStore.CommitAsync(WithInspection("commit-suspended", ActivityExecutionStatus.Suspended), Decision());
        await outerStore.CommitAsync(WithInspection("commit-completed", ActivityExecutionStatus.Completed), Decision());

        await using var verification = database.Open("tenant-a");
        var inspection = await verification.ActivityExecutionInspections.AsNoTracking().SingleAsync();
        Assert.Equal(nameof(ActivityExecutionStatus.Completed), inspection.Status);
        Assert.Equal(4, inspection.Revision);
        Assert.Equal(4, await verification.RuntimeCheckpointCommits.CountAsync());
    }

    [Fact]
    public async Task Inspection_without_its_own_scope_uses_provenance_for_checkpoint_hierarchy()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"),
                new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager())
            .CommitAsync(WithWorkflowIncidentAndInspection("commit-provenance-scope", true), Decision());

        Assert.Equal(EfRelationalIdentity.Encode("scope-inspected"),
            (await context.ActivityExecutionHierarchies.SingleAsync()).ExecutionScopeId);
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Marker_failure_rolls_back_run_health_incident_inspection_and_workflow_together()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = WithWorkflowIncidentAndInspection("commit-full-projection-failure");
        var interceptor = new FailMarkerInsertInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            interceptor.Arm();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"),
                    new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager())
                    .CommitAsync(commit, Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Empty(await restarted.WorkflowRunHealthStates.ToArrayAsync());
        Assert.Empty(await restarted.IncidentStates.ToArrayAsync());
        Assert.Empty(await restarted.ActivityExecutionInspections.ToArrayAsync());
        Assert.Empty(await restarted.ActivityExecutionHierarchies.ToArrayAsync());
        Assert.Empty(await restarted.WorkflowExecutionStates.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        await new EfRuntimeCheckpointCommitStore(restarted, new FixedAccessor("tenant-a"),
                new FixedTimeProvider(OccurredAt), new PassThroughRootWriteLeaseManager())
            .CommitAsync(commit, Decision());
        Assert.Equal(1, (await restarted.WorkflowRunHealthStates.SingleAsync()).IncidentCount);
    }

    [Fact]
    public async Task Marker_failure_rolls_back_activity_execution_and_allows_a_clean_retry()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = WithActivityExecution("commit-activity-rollback", CheckpointActivity("activity-rollback"));
        var interceptor = new FailMarkerInsertInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            interceptor.Arm();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                    .CommitAsync(commit, Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Empty(await restarted.ActivityExecutionStates.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        await new EfRuntimeCheckpointCommitStore(restarted, new FixedAccessor("tenant-a"))
            .CommitAsync(commit, Decision());
        Assert.Single(await restarted.ActivityExecutionStates.ToArrayAsync());
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Unsupported_activity_append_and_incomplete_scope_cleanup_fail_before_provider_io()
    {
        await using var database = await TestDatabase.CreateAsync();
        var capture = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", capture);
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CommitAsync(WithActivityExecution("commit-activity-append",
                CheckpointActivity("activity-append"), operation: RuntimeStateChangeOperation.Append),
                Decision()).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.CommitAsync(WithScopeCleanup("commit-incomplete-cleanup", []), Decision()).AsTask());
        Assert.Empty(capture.Commands);
    }

    [Fact]
    public async Task Marker_failure_restores_all_scope_cleanup_resources_and_excludes_a_hidden_second_attempt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new FailMarkerInsertInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            var access = new FixedAccessor("tenant-a");
            await SeedCleanupResourcesAsync(context, access);
            interceptor.Arm();
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                new EfRuntimeCheckpointCommitStore(context, access)
                    .CommitAsync(WithScopeCleanup("commit-cleanup-failure"), Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }

        await using var restarted = database.Open("tenant-a");
        Assert.Single(await restarted.Bookmarks.ToArrayAsync());
        Assert.Single(await restarted.DurableTimers.ToArrayAsync());
        Assert.Single(await restarted.SchedulerWorkItems.ToArrayAsync());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        await new EfRuntimeCheckpointCommitStore(restarted, new FixedAccessor("tenant-a"))
            .CommitAsync(WithScopeCleanup("commit-cleanup-failure"), Decision());
        Assert.Empty(await restarted.Bookmarks.ToArrayAsync());
        Assert.Empty(await restarted.DurableTimers.ToArrayAsync());
        Assert.Empty(await restarted.SchedulerWorkItems.ToArrayAsync());
    }

    [Fact]
    public async Task Marker_failure_rolls_back_previously_saved_outbox_and_does_not_leak_tracker_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        var commit = WithPendingIntent("commit-outbox-rollback", "intent-outbox-rollback");
        var interceptor = new FailMarkerInsertInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            interceptor.Arm();
            var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
            await Assert.ThrowsAsync<DbUpdateException>(() => store.CommitAsync(commit, Decision()).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
            await context.SaveChangesAsync();
        }

        await using var reopened = database.Open("tenant-a");
        Assert.Empty(await reopened.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Empty(await reopened.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Single((await new EfRuntimeCheckpointCommitStore(reopened, new FixedAccessor("tenant-a"))
            .CommitAsync(commit, Decision())).PendingPostCommitWorkIds);
    }

    [Fact]
    public async Task Conflicting_existing_outbox_refuses_marker_and_preserves_original_pending_intent()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var commit = WithPendingIntent("commit-outbox-conflict", "intent-conflict");
        var id = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, "intent-conflict");
        var different = new RuntimePostCommitIntent("intent-conflict", "workflow-a", "other.kind", OccurredAt, null, null, null);
        var outbox = new EfRuntimePostCommitOutboxStore(context, new FixedAccessor("tenant-a"));
        await outbox.SavePendingAsync(new RuntimePostCommitOutboxItem(id, different, RuntimePostCommitOutboxStatus.Pending, OccurredAt, OccurredAt));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(commit, Decision()).AsTask());
        Assert.Equal("other.kind", (await outbox.FindAsync(id))!.Intent.Kind);
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Intent_without_folded_outbox_change_fails_closed_before_provider_io()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", interceptor);
        var intent = new RuntimePostCommitIntent("unfolded", "workflow-a", "test.intent", OccurredAt, null, null, null);
        var commit = Commit("commit-unfolded") with { PostCommitIntents = [intent] };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(interceptor.Commands);
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Folded_outbox_with_a_different_intent_fails_closed_before_provider_io()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", interceptor);
        var commit = WithPendingIntent("commit-mismatched-intent", "intent-mismatch");
        var original = Assert.Single(commit.StateChanges.PostCommitOutbox);
        var alteredIntent = new RuntimePostCommitIntent(
            "intent-mismatch", "workflow-a", "other.kind", OccurredAt, null, null, null);
        var altered = new RuntimePostCommitOutboxItem(
            original.StateId, alteredIntent, RuntimePostCommitOutboxStatus.Pending, OccurredAt, OccurredAt);
        commit = commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox([
                original with { State = altered }])
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(interceptor.Commands);
        Assert.Empty(await context.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Staged_sibling_concurrency_failure_rolls_back_execution_and_marker_and_clears_retryable_state()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var scheduler = SchedulerRow("tenant-a", "workflow-a");
        context.SchedulerStates.Add(scheduler);
        await context.SaveChangesAsync();

        await using (var competing = database.Open("tenant-a"))
        {
            var competingRow = await competing.SchedulerStates.SingleAsync();
            competingRow.Revision++;
            await competing.SaveChangesAsync();
        }

        // This is the sibling mutation already staged by an R15 caller. The checkpoint must not clear it before
        // the atomic save, but a failed unit must clear the stale tracker after rollback so a later SaveChanges
        // cannot silently retry the sibling mutation.
        scheduler.ContentJson = scheduler.ContentJson.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal);
        scheduler.Revision++;
        var commit = Commit("commit-sibling-cas") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>("workflow-a", RuntimeStateChangeOperation.Upsert, Execution("workflow-a", "tenant-a"), new Dictionary<string, string>()),
                null,
                [], [], [], [], [])
        };

        var store = new EfRuntimeCheckpointCommitStore(
            context,
            new FixedAccessor("tenant-a"),
            rootWriteLeaseManager: new PassThroughRootWriteLeaseManager());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(context.ChangeTracker.Entries());

        await using var verification = database.Open("tenant-a");
        Assert.Null(await verification.WorkflowExecutionStates.SingleOrDefaultAsync());
        Assert.Empty(await verification.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(2, (await verification.SchedulerStates.SingleAsync()).Revision);
    }

    [Fact]
    public async Task Workflow_identity_mismatch_is_rejected_before_provider_io()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", interceptor);
        var commit = Commit("commit-identity") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    "workflow-other",
                    RuntimeStateChangeOperation.Upsert,
                    Execution("workflow-other", "tenant-a"),
                    new Dictionary<string, string>()),
                null,
                [], [], [], [], [])
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(interceptor.Commands);
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Scheduler_identity_mismatch_is_rejected_before_provider_io()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new CommandCaptureInterceptor();
        await using var context = database.Open("tenant-a", interceptor);
        var commit = Commit("commit-scheduler-identity") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                null,
                new RuntimeStateChange<SchedulerState>(
                    "workflow-other",
                    RuntimeStateChangeOperation.Upsert,
                    new SchedulerState("workflow-other", 1),
                    new Dictionary<string, string>()),
                [], [], [], [], [])
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(interceptor.Commands);
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task New_workflow_execution_fails_closed_without_root_write_lease_manager()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var commit = Commit("commit-no-root-lease") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    "workflow-a",
                    RuntimeStateChangeOperation.Upsert,
                    Execution("workflow-a", "tenant-a"),
                    new Dictionary<string, string>()),
                null,
                [], [], [], [], [])
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"))
                .CommitAsync(commit, Decision()).AsTask());
        Assert.Empty(await context.WorkflowExecutionStates.ToArrayAsync());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Marker_failure_rolls_back_a_pre_staged_sibling_and_leaves_marker_reusable()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new ParticipantOrderAndFailureInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
            interceptor.Arm();
            var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));

            await Assert.ThrowsAsync<DbUpdateException>(() =>
                store.CommitAsync(Commit("commit-rollback"), Decision()).AsTask());
            Assert.Contains("elsa_runtime_scheduler_state", interceptor.Tables);
            Assert.DoesNotContain("elsa_runtime_checkpoint_commit", interceptor.Tables);
        }

        await using (var verification = database.Open("tenant-a"))
        {
            Assert.Empty(await verification.SchedulerStates.ToArrayAsync());
            Assert.Empty(await verification.RuntimeCheckpointCommits.ToArrayAsync());
        }

        await using var retryContext = database.Open("tenant-a");
        var retry = new EfRuntimeCheckpointCommitStore(retryContext, new FixedAccessor("tenant-a"));
        await retry.CommitAsync(Commit("commit-rollback"), Decision());
        Assert.Single(await retryContext.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Ambiguous_transaction_acknowledgement_reconciles_through_the_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        var interceptor = new CommitAcknowledgementLossInterceptor();
        await using (var context = database.Open("tenant-a", interceptor))
        {
            var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));
            interceptor.Arm();
            var result = await store.CommitAsync(Commit("commit-ambiguous"), Decision());
            Assert.Empty(result.PendingPostCommitWorkIds);
        }

        await using var reopened = database.Open("tenant-a");
        Assert.Single(await reopened.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public void Registration_rejects_a_partial_ef_composition_without_changing_contracts()
    {
        var services = new ServiceCollection();
        services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });
        var before = services.ToArray();
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeCheckpointCommitEntityFrameworkCore());
        Assert.Equal(before, services);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
    }

    [Fact]
    public void Registration_owns_the_public_checkpoint_contract_only_after_every_participant_is_ef_owned()
    {
        var services = CompleteEfServices();
        services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        services.AddRuntimeCheckpointCommitEntityFrameworkCore();

        var marker = RuntimeCheckpointCommitStoreBackend.Find(services)!;
        Assert.Equal(RuntimeCheckpointCommitStoreBackend.EntityFramework, marker.Name);
        marker.EnsureOwnsRegisteredContract(services);
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeCheckpointCommitStore));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfRuntimeCheckpointCommitStore>(scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>());
    }

    [Fact]
    public async Task Registered_checkpoint_contract_commits_a_real_sqlite_marker_without_groundwork()
    {
        var connection = $"Data Source=file:ef-checkpoint-di-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connection);
        await keeper.OpenAsync();
        var services = CompleteEfServices(connection);
        services.AddRuntimeCheckpointCommitEntityFrameworkCore();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BookmarkStateDbContext>();
        await context.Database.EnsureCreatedAsync();

        var writer = scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>();
        Assert.IsType<EfRuntimeCheckpointCommitStore>(writer);
        await writer.CommitAsync(Commit("di-checkpoint"), Decision());
        Assert.Single(await context.RuntimeCheckpointCommits.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public void Registration_rejects_a_tampered_dispatch_backend_without_any_mutation()
    {
        var services = CompleteEfServices();
        services.Remove(services.Single(descriptor => descriptor.ServiceType == typeof(IWorkflowDispatchStore)));
        services.AddScoped<IWorkflowDispatchStore>(_ => throw new InvalidOperationException("foreign"));
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeCheckpointCommitEntityFrameworkCore());
        Assert.Equal(before, services);
        Assert.Null(RuntimeCheckpointCommitStoreBackend.Find(services));
    }

    private static ServiceCollection CompleteEfServices(string connection = "Data Source=:memory:")
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddPersistenceCore("tenant-a");
        services.AddRuntimeOperationalStateEntityFrameworkCore(new() { ConnectionString = connection });
        services.AddRuntimeArtifactsEntityFrameworkCore(new() { ConnectionString = connection });
        services.AddRuntimeActivityExecutionEntityFrameworkCore(new() { ConnectionString = connection });
        services.AddRuntimeBookmarksEntityFrameworkCore(new() { ConnectionString = connection });
        services.AddRuntimeWorkflowExecutionEntityFrameworkCore(new() { ConnectionString = connection });
        services.AddRuntimeWorkflowTestScopeEntityFrameworkCore(new() { ConnectionString = connection });
        services.AddRuntimeWorkflowAlterationEntityFrameworkCore(new() { ConnectionString = connection });
        services.AddRuntimeSchedulerWorkQueueEntityFrameworkCore(new() { ConnectionString = connection });
        services.AddRuntimeDurableTimerEntityFrameworkCore(new() { ConnectionString = connection });
        services.AddRuntimeWorkflowDispatchEntityFrameworkCore();
        services.AddRuntimePostCommitOutboxEntityFrameworkCore();
        return services;
    }

    [Fact]
    public async Task Checkpoint_slice_rejects_unsupported_incident_delete_without_writing_a_marker()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.Open("tenant-a");
        var store = new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a"));

        var incident = new RuntimeStateChange<IncidentState>(
            "incident-a",
            RuntimeStateChangeOperation.Delete,
            new IncidentState("incident-a", "workflow-a", null, null,
                IncidentSeverity.Error, IncidentStatus.Open, null, "test-failure", "delete unsupported",
                OccurredAt, null),
            new Dictionary<string, string>());
        var nonempty = Commit("commit-nonempty") with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                null,
                null,
                [],
                [],
                [],
                [incident],
                [])
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(nonempty, Decision()).AsTask());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());

        var replayable = Commit("commit-replay-fence");
        await store.CommitAsync(replayable, Decision());
        var replay = await store.CommitAsync(replayable with { ExpectedFence = new RuntimeExecutionFence("lease", "owner", 1) }, Decision());
        Assert.Empty(replay.PendingPostCommitWorkIds);
    }

    private static RuntimeCheckpointCommit Commit(string commitId) => new(
        commitId,
        new RuntimeCheckpoint(
            $"checkpoint-{commitId}",
            "EmptyCheckpoint",
            "workflow-a",
            OccurredAt,
            [],
            new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], []),
        [],
        new Dictionary<string, string>());

    private static RuntimeCheckpointCommit WithPendingIntent(string commitId, string intentId)
    {
        var commit = Commit(commitId);
        var intent = new RuntimePostCommitIntent(intentId, "workflow-a", "test.intent", OccurredAt, null, null, null);
        var id = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commitId, intentId);
        var item = new RuntimePostCommitOutboxItem(id, intent, RuntimePostCommitOutboxStatus.Pending, OccurredAt, OccurredAt);
        return commit with
        {
            PostCommitIntents = [intent],
            StateChanges = commit.StateChanges.WithPostCommitOutbox([
                new RuntimeStateChange<RuntimePostCommitOutboxItem>(id, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())])
        };
    }

    private static RuntimeCheckpointCommit WithPendingDispatch(
        string commitId,
        string intentId,
        WorkflowDispatchRecord dispatch)
    {
        var commit = WithPendingIntent(commitId, intentId);
        return commit with
        {
            StateChanges = commit.StateChanges.WithWorkflowDispatches([
                new RuntimeStateChange<WorkflowDispatchRecord>(
                    dispatch.DispatchId, RuntimeStateChangeOperation.Upsert,
                    dispatch, new Dictionary<string, string>())])
        };
    }

    private static WorkflowDispatchRecord PendingDispatch(
        string parent,
        string activity,
        string tenant,
        WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget,
        WorkflowTestScope? testScope = null)
    {
        var identity = new WorkflowDispatchIdentity(parent, activity);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parent,
            activity,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity($"artifact-{activity}", "definition-child", "version-child", "1", $"hash-{activity}"),
            new WorkflowExecutableSourceProvenance($"source-{activity}", "WorkflowDefinitionVersion", "version-child", "1", "definition-child", "version-child", "1", "publication-child", "slot-child"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            tenant,
            testScope?.Partition ?? new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            testScope is null ? WorkflowRunKind.PublishedRun : WorkflowRunKind.TestRun,
            new WorkflowExecutionAuthoritySnapshot(parent, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            OccurredAt,
            OccurredAt,
            new Dictionary<string, string> { ["safe-code"] = "dispatch" },
            testScope: testScope);
    }

    private static RuntimeCheckpointPersistenceDecision Decision() => new(RuntimeCheckpointPersistenceMode.Immediate);

    private static RuntimeSchedulerWorkItem SchedulerWork(string id) => new(
        id, "workflow-a", "command", WorkflowExecutionCommandKind.ScheduleActivity,
        "envelope", $"enqueue-{id}", OccurredAt, OccurredAt, 1);

    private static WorkflowTestScope TestScope(string id) => new(
        id, OccurredAt.AddHours(1), "tenant-a",
        new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue));

    private static DurableValueState DurableValue(string id, string value) => new(
        id, "workflow-a", id, new RuntimeValueTypeDescriptor("json", null, null),
        DurableValueLifecycle.Result, DurableValueStorage.Inline,
        JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement,
        null, null, OccurredAt, new Dictionary<string, string>());

    private static RuntimeCheckpointCommit WithOperational(
        string commitId, ExecutionLivenessState state, RuntimeStateChangeOperation operation)
    {
        var commit = Commit(commitId);
        return commit with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [],
                [new RuntimeStateChange<ExecutionLivenessState>(
                    state.OperationalStateId, operation, state, new Dictionary<string, string>())])
        };
    }

    private static BookmarkState CheckpointBookmark(string id, string payload) => new(
        id, "workflow-a", "activity-a", "node-a", "resume-a", "stimulus", payload,
        JsonDocument.Parse(JsonSerializer.Serialize(payload)).RootElement,
        new Dictionary<string, string> { ["kind"] = "checkpoint" }, OccurredAt, OccurredAt.AddHours(1));

    private static RuntimeCheckpointCommit WithBookmark(
        string commitId, BookmarkState bookmark, RuntimeStateChangeOperation operation)
    {
        var commit = Commit(commitId);
        return commit with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [],
                [new RuntimeStateChange<BookmarkState>(bookmark.BookmarkId, operation,
                    bookmark, new Dictionary<string, string>())], [], [], [])
        };
    }

    private static RuntimeCheckpointCommit WithScopeCleanup(string commitId, string[]? executionIds = null)
    {
        var commit = Commit(commitId);
        return commit with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [],
                null, null, null,
                [new ActivityScopeCleanupRequest("workflow-a", "scope-a", executionIds ?? ["scope-a"],
                    ["bookmark-cleanup"], ["timer-cleanup"], ["work-cleanup"])], null)
        };
    }

    private static RuntimeCheckpointCommit WithActivityExecution(
        string commitId, ActivityExecutionState state, BookmarkState? bookmark = null,
        RuntimeStateChangeOperation operation = RuntimeStateChangeOperation.Upsert)
    {
        var commit = Commit(commitId);
        return commit with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(null, null,
                [new RuntimeStateChange<ActivityExecutionState>(state.Execution.ActivityExecutionId,
                    operation, state, new Dictionary<string, string>())],
                bookmark is null ? [] : [new RuntimeStateChange<BookmarkState>(bookmark.BookmarkId,
                    RuntimeStateChangeOperation.Upsert, bookmark, new Dictionary<string, string>())],
                [], [], [])
        };
    }

    private static RuntimeCheckpointCommit WithWorkflowIncidentAndInspection(
        string commitId, bool inspectionUsesProvenanceOnly = false)
    {
        var commit = Commit(commitId);
        var workflow = Execution("workflow-a", "tenant-a");
        var activity = CheckpointActivity("activity-inspected");
        var inspection = CheckpointInspection("activity-inspected");
        if (inspectionUsesProvenanceOnly)
            inspection = inspection with { ExecutionScopeId = null };
        var incident = new IncidentState("incident-inspected", "workflow-a", null, null,
            IncidentSeverity.Error, IncidentStatus.Open, null, "test-failure", "checkpoint projection",
            OccurredAt, null);
        return commit with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>("workflow-a", RuntimeStateChangeOperation.Upsert,
                    workflow, new Dictionary<string, string>()),
                null,
                [new RuntimeStateChange<ActivityExecutionState>(activity.Execution.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert, activity, new Dictionary<string, string>())],
                [], [],
                [new RuntimeStateChange<IncidentState>(incident.IncidentId,
                    RuntimeStateChangeOperation.Append, incident, new Dictionary<string, string>())],
                [], null,
                [new RuntimeStateChange<ActivityExecutionInspectionProjection>(inspection.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert, inspection, new Dictionary<string, string>())],
                null, null, null)
        };
    }

    private static RuntimeCheckpointCommit WithInspection(string commitId, ActivityExecutionStatus status)
    {
        var inspection = CheckpointInspection("activity-inspected") with { Status = status };
        return Commit(commitId) with
        {
            StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], null,
                [new RuntimeStateChange<ActivityExecutionInspectionProjection>(inspection.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert, inspection, new Dictionary<string, string>())],
                null, null, null)
        };
    }

    private static ActivityExecutionInspectionProjection CheckpointInspection(string id) => new(
        id, "workflow-a", $"node-{id}", $"authored-{id}", "Test.Activity", "1",
        ActivityExecutionStatus.Completed, null, 1, OccurredAt, OccurredAt, OccurredAt,
        "checkpoint-1", "checkpoint-1", OccurredAt,
        ActivitySchedulingProvenance.From("workflow-a", null, null, null, null, null, "scope-inspected", "checkpoint"),
        ["Done"], [], [], [], new Dictionary<string, string>(), "scope-inspected");

    private static ActivityExecutionState CheckpointActivity(string id) => new(
        new ActivityExecution(id, "workflow-a", $"node-{id}", $"authored-{id}", "Test.Activity", "1"),
        ActivityExecutionStatus.Completed, null, 1,
        OccurredAt, OccurredAt, OccurredAt, null, null, null, null,
        ActivitySchedulingProvenance.From("workflow-a", null, null, null, null, null, null, "checkpoint"),
        null, [], [], 0, 0, new Dictionary<string, string>());

    private static async Task SeedCleanupResourcesAsync(
        BookmarkStateDbContext context, FixedAccessor access)
    {
        await new EfBookmarkStateStore(context, access).SaveAsync(
            CheckpointBookmark("bookmark-cleanup", "scope-cleanup"));
        await new EfDurableTimerStore(context, access, new NoopContinuationCodec()).SaveAsync(
            new DurableTimer("timer-cleanup", "workflow-a", "Delay", "stimulus-cleanup",
                OccurredAt.AddMinutes(10), OccurredAt));
        await new EfSchedulerWorkQueueStore(context, access, new NoopContinuationCodec()).EnqueueAsync(
            SchedulerWork("work-cleanup"));
    }

    private static WorkflowExecutionState Execution(string id, string tenantId) => new(
        id,
        new WorkflowExecutableIdentity($"artifact-{id}", $"definition-{id}", "version-1", "1.0.0", "hash-1"),
        WorkflowExecutionStatus.Running,
        null,
        OccurredAt,
        OccurredAt,
        OccurredAt,
        null,
        null,
        null,
        tenantId,
        new Dictionary<string, string>());

    private static SchedulerStateEntity SchedulerRow(string scope, string workflowExecutionId) => new()
    {
        Id = "pre-staged-scheduler-row",
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        WorkflowExecutionId = EfRelationalIdentity.Encode(workflowExecutionId),
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(workflowExecutionId),
        WorkflowExecutionIdOrderKey = Order(workflowExecutionId),
        Collection = "schedulerState",
        ContentJson = $$"""{"workflowExecutionId":"{{workflowExecutionId}}","version":1,"pendingWork":[],"pendingContinuations":[],"volatileWaits":[],"pendingCompletionWork":[],"activeGenerators":[],"pendingGeneratedEvents":[]}""",
        SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion,
        Revision = 1
    };

    private static string Order(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.IdentityMaximumLength));

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public string? ArtifactId { get; private set; }
        public string? LeaseId { get; private set; }

        public ValueTask ExecuteAsync(
            string artifactId,
            string leaseId,
            Func<CancellationToken, ValueTask> write,
            CancellationToken cancellationToken = default)
        {
            ArtifactId = artifactId;
            LeaseId = leaseId;
            return write(cancellationToken);
        }
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailMarkerInsertInterceptor : DbCommandInterceptor
    {
        private int armed;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed(command);
            return ValueTask.FromResult(result);
        }

        private void ThrowIfArmed(DbCommand command)
        {
            if (command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref armed, 0) == 1)
                throw new DbUpdateException("Simulated checkpoint marker insert failure.");
        }
    }

    private sealed class NoopContinuationCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }

    private sealed class TestDatabase(SqliteConnection connection) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var context = new BookmarkStateSqliteDbContext(
                new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection);
        }

        public BookmarkStateSqliteDbContext Open(string scope, params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<BookmarkStateSqliteDbContext>().UseSqlite(Connection);
            if (interceptors.Length > 0)
                builder.AddInterceptors(interceptors);
            return new BookmarkStateSqliteDbContext(builder.Options);
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private sealed class ParticipantOrderAndFailureInterceptor : DbCommandInterceptor
    {
        private int armed;

        public List<string> Tables { get; } = [];

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ObserveAndFail(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ObserveAndFail(command);
            return ValueTask.FromResult(result);
        }

        private void ObserveAndFail(DbCommand command)
        {
            var isInsert = command.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase);
            if (isInsert && command.CommandText.Contains("elsa_runtime_checkpoint_commit", StringComparison.OrdinalIgnoreCase))
                Tables.Add("elsa_runtime_checkpoint_commit");
            if (isInsert && command.CommandText.Contains("elsa_runtime_scheduler_state", StringComparison.OrdinalIgnoreCase))
                Tables.Add("elsa_runtime_scheduler_state");
            if ((isInsert || command.CommandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)) &&
                command.CommandText.Contains("elsa_runtime_scheduler_state", StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Exchange(ref armed, 0) == 1)
                throw new InvalidOperationException("Simulated later sibling participant failure.");
        }
    }

    private sealed class CommitAcknowledgementLossInterceptor : DbTransactionInterceptor
    {
        private int armed;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref armed, 0) == 1)
                throw new InvalidOperationException("Simulated acknowledgement loss after commit.");
            return Task.CompletedTask;
        }
    }
}
