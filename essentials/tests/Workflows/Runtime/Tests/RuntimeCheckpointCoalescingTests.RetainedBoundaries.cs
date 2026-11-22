using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeCheckpointCoalescingTests
{
    public static IEnumerable<object[]> RetainedBoundaryCases()
    {
        foreach (var mode in Enum.GetValues<PersistenceMode>())
        foreach (var boundary in new[] { RuntimeCheckpointNames.ActivitySuspended, RuntimeCheckpointNames.BookmarkCreated,
                     RuntimeCheckpointNames.WorkflowSuspended, "ChildDispatch" })
        foreach (var buffered in new[] { false, true })
            yield return [mode, boundary, buffered];
    }

    [Theory]
    [MemberData(nameof(RetainedBoundaryCases))]
    public async Task A_qualified_boundary_and_following_external_attempt_keep_the_next_segment_folding(
        PersistenceMode mode, string boundary, bool buffered)
    {
        var workflowId = boundary == "ChildDispatch" ? "parent-dispatch" : "retained-boundary";
        var state = new InMemoryRuntimeCheckpointStoreState();
        var inner = new InMemoryRuntimeCheckpointCommitStore(workflowDispatchStore: new InMemoryWorkflowDispatchStore(state), state: state);
        var session = new RuntimeCoalescingSession(workflowId, new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions(), inner, new(mode, true));
        var store = new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(inner),
            new FixedCoalescingSessionAccessor(session));
        var deferred = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred);
        var immediate = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        if (buffered)
            await store.CommitAsync(NewEmptyCommit(workflowId, 1, "BeforeBoundary"), deferred);
        var boundaryCommit = boundary == "ChildDispatch" ? NewDispatchBoundaryCommit() : NewEmptyCommit(workflowId, 2, boundary);
        await store.CommitAsync(boundaryCommit, immediate);
        Assert.Single(inner.ListCommits());
        Assert.True(session.IsActive);

        var attempt = NewEmptyCommit(workflowId, 3, RuntimeCheckpointNames.ActivityAttemptClaimed);
        await store.CommitAsync(attempt, immediate);
        Assert.Equal(2, inner.ListCommits().Count);
        Assert.True(session.IsActive);
        var after = NewEmptyCommit(workflowId, 4, RuntimeCheckpointNames.ActivityCompleted) with
        {
            Checkpoint = NewEmptyCommit(workflowId, 4, RuntimeCheckpointNames.ActivityCompleted).Checkpoint with { ActivityExecutionIds = ["after-boundary"] },
            StateChanges = new(null, new(workflowId, RuntimeStateChangeOperation.Upsert, new SchedulerState(workflowId, 3),
                new Dictionary<string, string>()), [], [], [], [], [])
        };
        await store.CommitAsync(after, deferred);
        Assert.Equal(2, inner.ListCommits().Count);
        await store.CommitAsync(NewEmptyCommit(workflowId, 5, RuntimeCheckpointNames.WorkflowCompleted), immediate);
        Assert.Equal(3, inner.ListCommits().Count);
        Assert.False(session.IsActive);
        Assert.Equal(3, inner.ListCommits().Last().Commit.StateChanges.Scheduler!.State.Version);
        if (boundary == "ChildDispatch")
        {
            var id = Assert.Single(boundaryCommit.StateChanges.PostCommitOutbox).StateId;
            Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await inner.FindAsync(id))!.Status);
            Assert.Single(inner.ListCommits(), commit => commit.Commit.StateChanges.PostCommitOutbox.Any(change => change.StateId == id));
        }
    }

    [Fact]
    public async Task A_new_segment_does_not_reapply_a_committed_dispatch_over_a_later_durable_lifecycle()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var dispatches = new InMemoryWorkflowDispatchStore(state);
        var inner = new InMemoryRuntimeCheckpointCommitStore(workflowDispatchStore: dispatches, state: state);
        var initial = NewDispatchBoundaryCommit();
        var immediate = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate);
        await inner.CommitAsync(initial, immediate);
        var session = new RuntimeCoalescingSession(initial.WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new());
        session.RecordCapFlushState(initial.StateChanges);
        session.ClearBuffer(); // The preceding checkpoint has committed.
        var pending = Assert.Single(initial.StateChanges.WorkflowDispatches).State;
        var started = pending.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        await dispatches.SaveAsync(started); // A separately scoped delivery advanced the durable record.

        session.BufferDeferred(NewEmptyCommit(initial.WorkflowExecutionId, 10, "NextSegment"));
        var next = NewEmptyCommit(initial.WorkflowExecutionId, 11, "NextFlush") with
        {
            StateChanges = session.FoldBufferedStateChanges()
        };
        await inner.CommitAsync(next, immediate);
        Assert.Equal(WorkflowDispatchStatus.Started, (await dispatches.FindAsync(pending.DispatchId))!.Status);
        Assert.DoesNotContain(Assert.Single(inner.ListCommits(), item => item.Commit.CommitId == next.CommitId)
            .Commit.StateChanges.WorkflowDispatches, change => change.StateId == pending.DispatchId);
    }

    [Fact]
    public void An_explicit_trailing_dispatch_change_takes_precedence_over_the_earlier_overlay()
    {
        var initial = NewDispatchBoundaryCommit();
        var pending = Assert.Single(initial.StateChanges.WorkflowDispatches);
        var started = pending.State.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        var session = new RuntimeCoalescingSession(initial.WorkflowExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new());
        session.BufferDeferred(initial);
        var trailing = NewEmptyCommit(initial.WorkflowExecutionId, 2, "Trailing").StateChanges.WithWorkflowDispatches([
            pending with { State = started }]);
        Assert.Equal(WorkflowDispatchStatus.Started, Assert.Single(session.FoldBufferedStateChangesWith(trailing).WorkflowDispatches).State.Status);
    }

    [Fact]
    public async Task A_later_cap_fold_does_not_write_a_delivered_external_outbox_item_back_as_pending()
    {
        const string workflowId = "wfexec-cap";
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        var session = new RuntimeCoalescingSession(workflowId, new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions { MaxActivitiesPerCommit = 1 }, inner);
        var store = new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(inner),
            new FixedCoalescingSessionAccessor(session));
        var deferred = new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred);
        await store.CommitAsync(NewEmptyDeferredCommit(1), deferred);
        var boundary = NewEmptyDeferredCommit(2) with
        {
            PostCommitIntents = [new("external", workflowId, "probe.external", Now, null, null, null)]
        };
        boundary = boundary with { StateChanges = boundary.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(boundary)), PostCommitIntents = [] };
        var outboxId = Assert.Single(boundary.StateChanges.PostCommitOutbox).StateId;
        await store.CommitAsync(boundary, deferred);
        Assert.True(session.IsActive);
        Assert.True(session.IsOutboxDurablyPersisted(outboxId));
        await inner.RecordDeliveryResultAsync(new(outboxId, RuntimePostCommitOutboxStatus.Delivered, Now.AddSeconds(1)));
        await store.CommitAsync(NewEmptyDeferredCommit(3), deferred);
        await store.CommitAsync(NewEmptyDeferredCommit(4), deferred);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await inner.FindAsync(outboxId))!.Status);
        Assert.DoesNotContain(inner.ListCommits().Skip(1), commit => commit.Commit.StateChanges.PostCommitOutbox.Any(change => change.StateId == outboxId));
    }
}
