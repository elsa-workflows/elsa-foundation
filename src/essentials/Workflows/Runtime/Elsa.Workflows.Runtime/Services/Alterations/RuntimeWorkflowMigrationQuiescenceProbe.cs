using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Conservative actor-side liveness proof. A single positive proof means the actor saw no retained lane capable of
/// executing, delivering, or resuming the target before it stages the migration checkpoint. Unknown store support is
/// a rejection rather than an unsafe success.
/// </summary>
public sealed class RuntimeWorkflowMigrationQuiescenceProbe(
    IActivityExecutionStateStore activityStore,
    ISchedulerStateStore schedulerStore,
    IExecutionLivenessStateStore livenessStore,
    IDurableTimerStore timerStore,
    IRuntimePostCommitOutboxStore outboxStore,
    IWorkflowDispatchQueryStore dispatchStore,
    IWorkflowSchedulerWorkQueue schedulerWorkQueue,
    TimeProvider? timeProvider = null) : IWorkflowMigrationQuiescenceProbe
{
    private readonly IActivityExecutionStateStore _activityStore = activityStore ?? throw new ArgumentNullException(nameof(activityStore));
    private readonly ISchedulerStateStore _schedulerStore = schedulerStore ?? throw new ArgumentNullException(nameof(schedulerStore));
    private readonly IExecutionLivenessStateStore _livenessStore = livenessStore ?? throw new ArgumentNullException(nameof(livenessStore));
    private readonly IDurableTimerStore _timerStore = timerStore ?? throw new ArgumentNullException(nameof(timerStore));
    private readonly IRuntimePostCommitOutboxStore _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
    private readonly IWorkflowDispatchQueryStore _dispatchStore = dispatchStore ?? throw new ArgumentNullException(nameof(dispatchStore));
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue = schedulerWorkQueue ?? throw new ArgumentNullException(nameof(schedulerWorkQueue));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<WorkflowMigrationQuiescenceReport> ProbeAsync(
        string workflowExecutionId,
        RuntimeExecutionFence? currentActorFence,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        var findings = new List<WorkflowMigrationCompatibilityFinding>();
        var now = _timeProvider.GetUtcNow();

        if (await HasRunningActivityAsync(workflowExecutionId, cancellationToken))
            findings.Add(new("ActiveActivityExecution"));

        var scheduler = await _schedulerStore.FindAsync(workflowExecutionId, cancellationToken);
        if (scheduler is not null && HasPendingSchedulerLane(scheduler))
            findings.Add(new("PendingSchedulerWork"));
        if (await HasQueuedSchedulerWorkAsync(workflowExecutionId, cancellationToken))
            findings.Add(new("PendingDurableSchedulerWork"));
        if (_schedulerWorkQueue is IWorkflowSchedulerWorkClaimInspection claimInspection &&
            (await claimInspection.ListActiveClaimsAsync(workflowExecutionId, now, cancellationToken)).Count != 0)
            findings.Add(new("ActiveSchedulerWorkClaim"));

        var liveness = await _livenessStore.ListAsync(workflowExecutionId, cancellationToken);
        if (liveness.Any(state => HasActiveLiveness(state, now, currentActorFence)))
            findings.Add(new("ActiveExecutionFence"));

        var timers = await _timerStore.ListAsync(workflowExecutionId, cancellationToken);
        if (timers.Count != 0)
            findings.Add(new("PendingDurableTimer"));

        var outbox = await _outboxStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(DateTimeOffset.MaxValue, 1, workflowExecutionId), cancellationToken);
        if (outbox.Any(item => !item.IsTerminal))
            findings.Add(new("PendingOrClaimedOutboxDelivery"));

        if (await HasActiveDispatchAsync(workflowExecutionId, isParent: true, cancellationToken) ||
            await HasActiveDispatchAsync(workflowExecutionId, isParent: false, cancellationToken))
            findings.Add(new("ActiveWorkflowDispatch"));

        return findings.Count == 0
            ? WorkflowMigrationQuiescenceReport.Quiescent()
            : WorkflowMigrationQuiescenceReport.Busy(findings);
    }

    private static bool HasPendingSchedulerLane(SchedulerState state) =>
        state.PendingWork.Count != 0 ||
        state.PendingCompletionWork.Count != 0 ||
        state.PendingContinuations.Count != 0 ||
        state.VolatileWaits.Count != 0 ||
        state.ActiveGenerators.Count != 0 ||
        state.PendingGeneratedEvents.Count != 0;

    private async ValueTask<bool> HasRunningActivityAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        string? continuation = null;
        do
        {
            var page = await _activityStore.ListPageAsync(
                new ActivityExecutionStatePageQuery(workflowExecutionId, continuationToken: continuation), cancellationToken);
            if (page.Items.Any(activity => activity.Status == ActivityExecutionStatus.Running))
                return true;
            continuation = page.NextContinuationToken;
        } while (continuation is not null);
        return false;
    }

    private async ValueTask<bool> HasQueuedSchedulerWorkAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        string? continuation = null;
        do
        {
            var page = await _schedulerWorkQueue.ListAsync(
                new RuntimeSchedulerWorkQuery(workflowExecutionId, continuationToken: continuation), cancellationToken);
            if (page.Items.Count != 0)
                return true;
            continuation = page.NextContinuationToken;
        } while (continuation is not null);
        return false;
    }

    private async ValueTask<bool> HasActiveDispatchAsync(string workflowExecutionId, bool isParent, CancellationToken cancellationToken)
    {
        foreach (var status in new[] { WorkflowDispatchStatus.Pending, WorkflowDispatchStatus.Started })
        {
            var page = await _dispatchStore.QueryAsync(isParent
                    ? new WorkflowDispatchQuery(parentWorkflowExecutionId: workflowExecutionId, status: status, take: 1)
                    : new WorkflowDispatchQuery(childWorkflowExecutionId: workflowExecutionId, status: status, take: 1),
                cancellationToken);
            if (page.Count != 0)
                return true;
        }
        return false;
    }

    private static bool HasActiveLiveness(ExecutionLivenessState state, DateTimeOffset now, RuntimeExecutionFence? currentActorFence) =>
        state.ExecutionLease is { } lease && !lease.IsExpired(now) && !Matches(lease, currentActorFence) ||
        state.Heartbeat is { } heartbeat && (currentActorFence is null || !StringComparer.Ordinal.Equals(heartbeat.LeaseId, currentActorFence.LeaseId) || !StringComparer.Ordinal.Equals(heartbeat.OwnerId, currentActorFence.OwnerId)) ||
        state.Drain is { StopsNewWork: true } ||
        state.InterruptedExecution is not null ||
        state.PendingPostCommitIntentIds.Count != 0;

    private static bool Matches(RuntimeExecutionLease lease, RuntimeExecutionFence? fence) =>
        fence is not null &&
        StringComparer.Ordinal.Equals(lease.LeaseId, fence.LeaseId) &&
        StringComparer.Ordinal.Equals(lease.OwnerId, fence.OwnerId) &&
        lease.FencingToken == fence.FencingToken;
}
