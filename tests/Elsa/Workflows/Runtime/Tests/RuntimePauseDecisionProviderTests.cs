using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePauseDecisionProviderTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 14, 30, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowHoldStateStore _store = new();
    private readonly RuntimePauseDecisionProvider _provider;

    public RuntimePauseDecisionProviderTests()
    {
        _provider = new RuntimePauseDecisionProvider(_store);
    }

    [Fact]
    public async Task DecideAsync_AllowsSchedulerAdvanceWhenNoHoldMatches()
    {
        await _store.SaveAsync(NewWorkflowState("control-1", "wfexec-2", "pause-1"));

        var decision = await _provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1"));

        Assert.True(decision.CanAdvance);
        Assert.Equal(RuntimePauseBoundary.BeforeActivityExecutionStart, decision.Boundary);
        Assert.Equal(RuntimePauseContinuationPolicy.NotPaused, decision.ContinuationPolicy);
        Assert.Null(decision.HoldId);
        Assert.Equal(nameof(RuntimePauseDecisionProvider), decision.Metadata["runtime.pause.policy"]);
    }

    [Fact]
    public async Task DecideAsync_BlocksSchedulerAdvanceForMatchingWorkflowExecutionHold()
    {
        await _store.SaveAsync(NewWorkflowState("control-1", "wfexec-1", "pause-1"));

        var decision = await _provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1"));

        Assert.False(decision.CanAdvance);
        Assert.Equal("pause-1", decision.HoldId);
        Assert.Equal("maintenance", decision.Reason);
        Assert.Equal(RuntimePauseContinuationPolicy.StrictPause, decision.ContinuationPolicy);
        Assert.Equal(nameof(WorkflowHoldScope.WorkflowExecution), decision.Metadata["runtime.pause.holdScope"]);
    }

    [Fact]
    public async Task DecideAsync_PrefersOldestMatchingHoldThenHoldId()
    {
        await _store.SaveAsync(new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds:
            [
                WorkflowHold.ForWorkflowExecution("pause-b", "wfexec-1", _now.AddMinutes(1), "operator", "later"),
                WorkflowHold.ForWorkflowExecution("pause-c", "wfexec-1", _now, "operator", "same-time-c"),
                WorkflowHold.ForWorkflowExecution("pause-a", "wfexec-1", _now, "operator", "same-time-a")
            ]));

        var decision = await _provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1"));

        Assert.False(decision.CanAdvance);
        Assert.Equal("pause-a", decision.HoldId);
        Assert.Equal("same-time-a", decision.Reason);
    }

    [Fact]
    public async Task DecideAsync_IgnoresReleasedHolds()
    {
        await _store.SaveAsync(new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            releasedHolds:
            [
                new WorkflowHold(
                    holdId: "pause-1",
                    scope: WorkflowHoldScope.WorkflowExecution,
                    status: WorkflowHoldStatus.Released,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "maintenance",
                    workflowExecutionId: "wfexec-1",
                    releasedAt: _now.AddMinutes(5),
                    releasedBy: "operator")
            ]));

        var decision = await _provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1"));

        Assert.True(decision.CanAdvance);
        Assert.Equal(RuntimePauseContinuationPolicy.NotPaused, decision.ContinuationPolicy);
        Assert.Null(decision.HoldId);
    }

    [Fact]
    public async Task DecideAsync_UsesWorkflowScopedQueryForWorkflowOnlyTargets()
    {
        var store = new TrackingWorkflowHoldStateStore([NewWorkflowState("control-1", "wfexec-1", "pause-1")]);
        var provider = new RuntimePauseDecisionProvider(store);

        await provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1", activityExecutionId: "actexec-1"));

        Assert.Equal(1, store.WorkflowScopedQueryCount);
        Assert.Equal(0, store.ListAllQueryCount);
    }

    [Fact]
    public async Task DecideAsync_UsesAllStateQueryWhenRequestIncludesGlobalTargets()
    {
        var store = new TrackingWorkflowHoldStateStore([]);
        var provider = new RuntimePauseDecisionProvider(store);

        await provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1", hostId: "host-1"));

        Assert.Equal(0, store.WorkflowScopedQueryCount);
        Assert.Equal(1, store.ListAllQueryCount);
    }

    [Fact]
    public async Task DecideAsync_MatchesActivityGeneratorIngressWorkerAndHostScopes()
    {
        await _store.SaveAsync(new WorkflowHoldState(
            controlPlaneStateId: "activity-control",
            workflowExecutionId: "wfexec-1",
            activeHolds:
            [
                new WorkflowHold(
                    holdId: "activity-pause",
                    scope: WorkflowHoldScope.ActivityExecution,
                    status: WorkflowHoldStatus.Requested,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "activity maintenance",
                    workflowExecutionId: "wfexec-1",
                    activityExecutionId: "actexec-1")
            ]));
        await _store.SaveAsync(new WorkflowHoldState(
            controlPlaneStateId: "generator-control",
            workflowExecutionId: "wfexec-1",
            activeHolds:
            [
                new WorkflowHold(
                    holdId: "generator-pause",
                    scope: WorkflowHoldScope.Generator,
                    status: WorkflowHoldStatus.Requested,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "generator maintenance",
                    workflowExecutionId: "wfexec-1",
                    generatorId: "generator-1")
            ]));
        await _store.SaveAsync(new WorkflowHoldState(
            controlPlaneStateId: "global-control",
            activeHolds:
            [
                new WorkflowHold(
                    holdId: "ingress-pause",
                    scope: WorkflowHoldScope.Ingress,
                    status: WorkflowHoldStatus.Requested,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "ingress maintenance",
                    ingressSourceId: "http-1",
                    ingressPolicy: IngressPausePolicy.DefaultFor(RuntimeIngressSourceKind.HttpEndpoint)),
                new WorkflowHold(
                    holdId: "worker-pause",
                    scope: WorkflowHoldScope.WorkerDispatcher,
                    status: WorkflowHoldStatus.Requested,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "worker maintenance",
                    workerId: "worker-1"),
                WorkflowHold.ForHostDrain("host-drain", "host-1", _now, "operator", "shutdown")
            ]));

        Assert.Equal("activity-pause", (await _provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1", activityExecutionId: "actexec-1"))).HoldId);
        Assert.Equal("generator-pause", (await _provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1", generatorId: "generator-1"))).HoldId);
        Assert.Equal("ingress-pause", (await _provider.DecideAsync(NewRequest(ingressSourceId: "http-1"))).HoldId);
        Assert.Equal("worker-pause", (await _provider.DecideAsync(NewRequest(workerId: "worker-1"))).HoldId);
        Assert.Equal("host-drain", (await _provider.DecideAsync(NewRequest(hostId: "host-1"))).HoldId);
    }

    [Fact]
    public void RuntimePauseDecisionRequest_RequiresWorkflowExecutionForActivityAndGeneratorTargets()
    {
        Assert.Throws<ArgumentException>(() => NewRequest(activityExecutionId: "actexec-1"));
        Assert.Throws<ArgumentException>(() => NewRequest(generatorId: "generator-1"));
    }

    private RuntimePauseDecisionRequest NewRequest(
        string? workflowExecutionId = null,
        string? activityExecutionId = null,
        string? generatorId = null,
        string? ingressSourceId = null,
        string? workerId = null,
        string? hostId = null) =>
        new(
            boundary: RuntimePauseBoundary.BeforeActivityExecutionStart,
            evaluatedAt: _now,
            workflowExecutionId: workflowExecutionId,
            activityExecutionId: activityExecutionId,
            generatorId: generatorId,
            ingressSourceId: ingressSourceId,
            workerId: workerId,
            hostId: hostId);

    private WorkflowHoldState NewWorkflowState(string controlPlaneStateId, string workflowExecutionId, string holdId) =>
        new(
            controlPlaneStateId: controlPlaneStateId,
            workflowExecutionId: workflowExecutionId,
            activeHolds:
            [
                WorkflowHold.ForWorkflowExecution(
                    holdId: holdId,
                    workflowExecutionId: workflowExecutionId,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "maintenance")
            ]);

    private sealed class TrackingWorkflowHoldStateStore : IWorkflowHoldStateStore
    {
        private readonly InMemoryWorkflowHoldStateStore _inner = new();

        public TrackingWorkflowHoldStateStore(IEnumerable<WorkflowHoldState> states)
        {
            foreach (var state in states)
                _inner.SaveAsync(state).AsTask().GetAwaiter().GetResult();
        }

        public int WorkflowScopedQueryCount { get; private set; }
        public int ListAllQueryCount { get; private set; }

        public ValueTask<WorkflowHoldState> SaveAsync(WorkflowHoldState state, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(state, cancellationToken);

        public ValueTask<WorkflowHoldState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default) =>
            _inner.FindAsync(controlPlaneStateId, cancellationToken);

        public ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            WorkflowScopedQueryCount++;
            return _inner.ListForWorkflowExecutionAsync(workflowExecutionId, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<WorkflowHoldState>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            ListAllQueryCount++;
            return _inner.ListAllAsync(cancellationToken);
        }
    }
}
