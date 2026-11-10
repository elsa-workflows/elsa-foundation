using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class WorkflowMigrationQuiescenceTests
{
    [Fact]
    public async Task ProbeAsync_AllowsOnlyTheCurrentActorLease()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        var lease = new RuntimeExecutionLease("lease", "workflow", "owner", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), 7);
        await liveness.SaveAsync(new ExecutionLivenessState(
            "ownership:workflow", "workflow", lease,
            new RuntimeHeartbeat("heartbeat", "workflow", "owner", "lease", DateTimeOffset.UnixEpoch), null, null));
        var probe = NewProbe(liveness);

        var own = await probe.ProbeAsync("workflow", lease.ToFence());
        var foreign = await probe.ProbeAsync("workflow", new RuntimeExecutionFence("other", "other-owner", 8));

        Assert.True(own.IsQuiescent);
        Assert.False(foreign.IsQuiescent);
        Assert.Contains(foreign.Findings, finding => finding.Code == "ActiveExecutionFence");
    }

    [Fact]
    public async Task ProbeAsync_WithoutAFence_RejectsALiveLease()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        await new RuntimeExecutionOwnershipService(liveness).AcquireAsync("workflow");

        var result = await NewProbe(liveness).ProbeAsync("workflow", null);

        Assert.Contains(result.Findings, finding => finding.Code == "ActiveExecutionFence");
    }

    // #2667: an alteration borrows the run's lease. Its own lease is exempt like any caller's, unless it took the run
    // from an owner: then the lease stands in for that owner, and the run is not quiescent.
    [Fact]
    public async Task ProbeAsync_ExemptsTheCallersLease_UnlessItWasBorrowedFromAnOwner()
    {
        var liveness = new InMemoryExecutionLivenessStateStore();
        var probe = NewProbe(liveness);
        var alteration = new RuntimeExecutionOwnershipService(liveness);

        var fromNoOwner = await alteration.BorrowAsync("workflow");
        var withNoOwner = await probe.ProbeAsync("workflow", fromNoOwner.Lease.ToFence());
        await alteration.ReturnAsync(fromNoOwner);
        await new RuntimeExecutionOwnershipService(liveness).AcquireAsync("workflow");
        var fromAnOwner = await alteration.BorrowAsync("workflow");
        var withAnOwner = await probe.ProbeAsync("workflow", fromAnOwner.Lease.ToFence());

        Assert.True(withNoOwner.IsQuiescent);
        Assert.Contains(withAnOwner.Findings, finding => finding.Code == "ActiveExecutionFence");
    }

    [Fact]
    public async Task ProbeAsync_RejectsRunningActivitiesAndSchedulerWork()
    {
        var activities = new InMemoryActivityExecutionStateStore();
        await activities.SaveAsync(new ActivityExecutionState(
            new ActivityExecution("activity", "workflow", "node", "node", "test", "1"),
            ActivityExecutionStatus.Running, null, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null,
            null, null, null, null, ActivitySchedulingProvenance.From("workflow", null, null, null, null, null, null, null),
            null, [], [], 0, 0, new Dictionary<string, string>()));
        var scheduler = new InMemorySchedulerStateStore();
        await scheduler.SaveAsync(new SchedulerState("workflow", 1, pendingWork: [new ScheduledActivityWorkItem("work", "workflow", "node", null, null, null, null, DateTimeOffset.UnixEpoch, "test")]));
        var probe = NewProbe(new InMemoryExecutionLivenessStateStore(), activities, scheduler);

        var result = await probe.ProbeAsync("workflow", null);

        Assert.False(result.IsQuiescent);
        Assert.Contains(result.Findings, finding => finding.Code == "ActiveActivityExecution");
        Assert.Contains(result.Findings, finding => finding.Code == "PendingSchedulerWork");
    }

    [Fact]
    public async Task ProbeAsync_RejectsAnAdmittedBackgroundInvocationWhileItIsRunning()
    {
        var activities = new InMemoryActivityExecutionStateStore();
        await activities.SaveAsync(Activity("background-activity", ActivityExecutionStatus.Running) with
        {
            SubStatus = "BackgroundPending"
        });
        var probe = NewProbe(new InMemoryExecutionLivenessStateStore(), activities);

        var result = await probe.ProbeAsync("workflow", null);

        Assert.False(result.IsQuiescent);
        Assert.Contains(result.Findings, finding => finding.Code == "ActiveActivityExecution");
    }

    [Fact]
    public async Task ProbeAsync_TraversesActivityPagesAndRejectsQueuedOrClaimedDurableWork()
    {
        var activities = new InMemoryActivityExecutionStateStore();
        for (var index = 0; index <= 100; index++)
        {
            await activities.SaveAsync(Activity($"activity-{index:D3}", index == 100 ? ActivityExecutionStatus.Running : ActivityExecutionStatus.Suspended));
        }
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        await queue.EnqueueAsync(WorkItem("queued"));
        var probe = NewProbe(new InMemoryExecutionLivenessStateStore(), activities, queue: queue);

        var queued = await probe.ProbeAsync("workflow", null);
        Assert.Contains(queued.Findings, finding => finding.Code == "ActiveActivityExecution");
        Assert.Contains(queued.Findings, finding => finding.Code == "PendingDurableSchedulerWork");

        await queue.ClaimAsync(new RuntimeSchedulerWorkClaimRequest("workflow", "worker", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        var claimed = await probe.ProbeAsync("workflow", null);
        Assert.Contains(claimed.Findings, finding => finding.Code == "ActiveSchedulerWorkClaim");
    }

    internal static RuntimeWorkflowMigrationQuiescenceProbe NewProbe(
        IExecutionLivenessStateStore liveness,
        IActivityExecutionStateStore? activities = null,
        ISchedulerStateStore? scheduler = null,
        IWorkflowSchedulerWorkQueue? queue = null,
        TimeProvider? timeProvider = null) => new(
        activities ?? new InMemoryActivityExecutionStateStore(),
        scheduler ?? new InMemorySchedulerStateStore(),
        liveness,
        new InMemoryDurableTimerStore(),
        new EmptyOutboxStore(),
        new EmptyDispatchStore(),
        queue ?? new InMemoryWorkflowSchedulerWorkQueue(),
        timeProvider ?? TimeProvider.System);

    private static ActivityExecutionState Activity(string activityId, ActivityExecutionStatus status) => new(
        new ActivityExecution(activityId, "workflow", "node", "node", "test", "1"),
        status, null, 0, DateTimeOffset.UnixEpoch, status == ActivityExecutionStatus.Running ? DateTimeOffset.UnixEpoch : null, null,
        null, null, null, null, ActivitySchedulingProvenance.From("workflow", null, null, null, null, null, null, null),
        null, [], [], 0, 0, new Dictionary<string, string>());

    private static RuntimeSchedulerWorkItem WorkItem(string id) => new(
        id, "workflow", $"command-{id}", WorkflowExecutionCommandKind.Checkpoint, $"envelope-{id}", $"idempotency-{id}",
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class EmptyOutboxStore : IRuntimePostCommitOutboxStore
    {
        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxItem>>([]);

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted);
    }

    private sealed class EmptyDispatchStore : IWorkflowDispatchQueryStore
    {
        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(WorkflowDispatchQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowDispatchRecord>>([]);
    }
}
