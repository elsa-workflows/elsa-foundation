using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePauseDecisionProviderTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 14, 30, 0, TimeSpan.Zero);
    private readonly InMemoryControlPlaneStateStore _store = new();
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
        Assert.Equal(nameof(ControlPlaneHoldScope.WorkflowExecution), decision.Metadata["runtime.pause.holdScope"]);
    }

    [Fact]
    public async Task DecideAsync_PrefersOldestMatchingHoldThenHoldId()
    {
        await _store.SaveAsync(new ControlPlaneState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds:
            [
                ControlPlaneHold.ForWorkflowExecution("pause-b", "wfexec-1", _now.AddMinutes(1), "operator", "later"),
                ControlPlaneHold.ForWorkflowExecution("pause-c", "wfexec-1", _now, "operator", "same-time-c"),
                ControlPlaneHold.ForWorkflowExecution("pause-a", "wfexec-1", _now, "operator", "same-time-a")
            ]));

        var decision = await _provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1"));

        Assert.False(decision.CanAdvance);
        Assert.Equal("pause-a", decision.HoldId);
        Assert.Equal("same-time-a", decision.Reason);
    }

    [Fact]
    public async Task DecideAsync_IgnoresReleasedHolds()
    {
        await _store.SaveAsync(new ControlPlaneState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            releasedHolds:
            [
                new ControlPlaneHold(
                    holdId: "pause-1",
                    scope: ControlPlaneHoldScope.WorkflowExecution,
                    status: ControlPlaneHoldStatus.Released,
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
        var store = new TrackingControlPlaneStateStore([NewWorkflowState("control-1", "wfexec-1", "pause-1")]);
        var provider = new RuntimePauseDecisionProvider(store);

        await provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1", activityExecutionId: "actexec-1"));

        Assert.Equal(1, store.WorkflowScopedQueryCount);
        Assert.Equal(0, store.ListAllQueryCount);
    }

    [Fact]
    public async Task DecideAsync_UsesAllStateQueryWhenRequestIncludesGlobalTargets()
    {
        var store = new TrackingControlPlaneStateStore([]);
        var provider = new RuntimePauseDecisionProvider(store);

        await provider.DecideAsync(NewRequest(workflowExecutionId: "wfexec-1", hostId: "host-1"));

        Assert.Equal(0, store.WorkflowScopedQueryCount);
        Assert.Equal(1, store.ListAllQueryCount);
    }

    [Fact]
    public async Task DecideAsync_MatchesActivityGeneratorIngressWorkerAndHostScopes()
    {
        await _store.SaveAsync(new ControlPlaneState(
            controlPlaneStateId: "activity-control",
            workflowExecutionId: "wfexec-1",
            activeHolds:
            [
                new ControlPlaneHold(
                    holdId: "activity-pause",
                    scope: ControlPlaneHoldScope.ActivityExecution,
                    status: ControlPlaneHoldStatus.Requested,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "activity maintenance",
                    workflowExecutionId: "wfexec-1",
                    activityExecutionId: "actexec-1")
            ]));
        await _store.SaveAsync(new ControlPlaneState(
            controlPlaneStateId: "generator-control",
            workflowExecutionId: "wfexec-1",
            activeHolds:
            [
                new ControlPlaneHold(
                    holdId: "generator-pause",
                    scope: ControlPlaneHoldScope.Generator,
                    status: ControlPlaneHoldStatus.Requested,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "generator maintenance",
                    workflowExecutionId: "wfexec-1",
                    generatorId: "generator-1")
            ]));
        await _store.SaveAsync(new ControlPlaneState(
            controlPlaneStateId: "global-control",
            activeHolds:
            [
                new ControlPlaneHold(
                    holdId: "ingress-pause",
                    scope: ControlPlaneHoldScope.Ingress,
                    status: ControlPlaneHoldStatus.Requested,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "ingress maintenance",
                    ingressSourceId: "http-1",
                    ingressPolicy: IngressPausePolicy.DefaultFor(RuntimeIngressSourceKind.HttpEndpoint)),
                new ControlPlaneHold(
                    holdId: "worker-pause",
                    scope: ControlPlaneHoldScope.WorkerDispatcher,
                    status: ControlPlaneHoldStatus.Requested,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "worker maintenance",
                    workerId: "worker-1"),
                ControlPlaneHold.ForHostDrain("host-drain", "host-1", _now, "operator", "shutdown")
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

    private ControlPlaneState NewWorkflowState(string controlPlaneStateId, string workflowExecutionId, string holdId) =>
        new(
            controlPlaneStateId: controlPlaneStateId,
            workflowExecutionId: workflowExecutionId,
            activeHolds:
            [
                ControlPlaneHold.ForWorkflowExecution(
                    holdId: holdId,
                    workflowExecutionId: workflowExecutionId,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "maintenance")
            ]);

    private sealed class TrackingControlPlaneStateStore : IControlPlaneStateStore
    {
        private readonly InMemoryControlPlaneStateStore _inner = new();

        public TrackingControlPlaneStateStore(IEnumerable<ControlPlaneState> states)
        {
            foreach (var state in states)
                _inner.SaveAsync(state).AsTask().GetAwaiter().GetResult();
        }

        public int WorkflowScopedQueryCount { get; private set; }
        public int ListAllQueryCount { get; private set; }

        public ValueTask<ControlPlaneState> SaveAsync(ControlPlaneState state, CancellationToken cancellationToken = default) =>
            _inner.SaveAsync(state, cancellationToken);

        public ValueTask<ControlPlaneState?> FindAsync(string controlPlaneStateId, CancellationToken cancellationToken = default) =>
            _inner.FindAsync(controlPlaneStateId, cancellationToken);

        public ValueTask<IReadOnlyCollection<ControlPlaneState>> ListForWorkflowExecutionAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            WorkflowScopedQueryCount++;
            return _inner.ListForWorkflowExecutionAsync(workflowExecutionId, cancellationToken);
        }

        public ValueTask<IReadOnlyCollection<ControlPlaneState>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            ListAllQueryCount++;
            return _inner.ListAllAsync(cancellationToken);
        }
    }
}
