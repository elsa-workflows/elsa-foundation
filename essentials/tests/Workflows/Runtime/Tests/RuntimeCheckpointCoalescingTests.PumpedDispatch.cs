using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeCheckpointCoalescingTests
{
    [Theory]
    [InlineData("accepted", false)]
    [InlineData("retaining-boundary", true)]
    [InlineData("attempt-claim", true)]
    [InlineData("other-execution", true)]
    [InlineData("other-source", true)]
    [InlineData("other-command", true)]
    [InlineData("unfenced", true)]
    public async Task Only_the_pumped_decision_that_committed_can_leave_the_durable_frontier(string outcome, bool remains)
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var session = new RuntimeCoalescingSession("pumped-parent", queue, new());
        var item = PumpedParentWork("parent-evaluation");
        await session.EnqueueOverlayAsync(item, default);
        using var dispatch = session.BeginPumpedDispatch(item);
        var commit = PumpedDecision(item);
        if (outcome == "retaining-boundary") commit = commit with { RetainSchedulerWork = true };
        if (outcome == "attempt-claim")
            commit = commit with { Checkpoint = commit.Checkpoint with { Name = RuntimeCheckpointNames.ActivityAttemptClaimed } };
        if (outcome == "other-execution")
            commit = commit with { Checkpoint = commit.Checkpoint with { WorkflowExecutionId = "another-execution" } };
        if (outcome == "other-source")
            commit = commit with { Metadata = new Dictionary<string, string>(commit.Metadata) { [RuntimeMetadataKeys.SchedulerWorkItemId] = "another-evaluation" } };
        if (outcome == "other-command")
            commit = commit with { Metadata = new Dictionary<string, string>(commit.Metadata) { [RuntimeMetadataKeys.CommandId] = "another-command" } };
        if (outcome == "unfenced") commit = commit with { ExpectedFence = null };
        session.RecordCommittedPumpedEffect(commit);
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, default);
        Assert.Equal(remains ? [item.WorkItemId] : [], (await queue.ListAllAsync("pumped-parent")).Select(work => work.WorkItemId));
    }

    [Fact]
    public async Task A_failed_physical_checkpoint_cannot_acknowledge_the_pumped_decision()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var session = new RuntimeCoalescingSession("pumped-parent", queue, new());
        var item = PumpedParentWork("parent-evaluation");
        await session.EnqueueOverlayAsync(item, default);
        using var dispatch = session.BeginPumpedDispatch(item);
        var store = new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(new RejectPumpedCheckpoint()),
            new FixedCoalescingSessionAccessor(session));
        await Assert.ThrowsAsync<IOException>(() => store.CommitAsync(PumpedDecision(item),
            new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, default);
        Assert.Equal(item.WorkItemId, Assert.Single(await queue.ListAllAsync("pumped-parent")).WorkItemId);
    }

    [Fact]
    public async Task A_nested_dispatch_receipt_does_not_acknowledge_the_unfinished_outer_decision()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var session = new RuntimeCoalescingSession("pumped-parent", queue, new());
        var outer = PumpedParentWork("outer-evaluation");
        var nested = PumpedParentWork("nested-evaluation");
        await session.EnqueueOverlayAsync(outer, default);
        await session.EnqueueOverlayAsync(nested, default);
        using var outerScope = session.BeginPumpedDispatch(outer);
        using (session.BeginPumpedDispatch(nested))
        {
            session.RecordCommittedPumpedEffect(PumpedDecision(nested));
            await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, default);
        }
        await session.ConsumePumpedOverlayItemAsync(nested.WorkItemId, default);
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: true, default);
        Assert.Equal(outer.WorkItemId, Assert.Single(await queue.ListAllAsync("pumped-parent")).WorkItemId);
    }

    private static RuntimeSchedulerWorkItem PumpedParentWork(string id) => new(id, "pumped-parent", "command-" + id,
        WorkflowExecutionCommandKind.CompleteActivity, "envelope-" + id, "idempotency-" + id, Now, Now, 1);

    private static RuntimeCheckpointCommit PumpedDecision(RuntimeSchedulerWorkItem item) =>
        NewEmptyCommit("pumped-parent", 1, RuntimeCheckpointNames.ActivityInspectionCaptured) with
        {
            ExpectedFence = new("lease-parent", "owner-parent", 1),
            Metadata = new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = item.WorkItemId,
                [RuntimeMetadataKeys.CommandId] = item.CommandId
            }
        };

    private sealed class RejectPumpedCheckpoint : IRuntimeCheckpointCommitStore
    {
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<RuntimeCheckpointCommitStoreResult>(new IOException("Injected physical write failure."));
    }
}
