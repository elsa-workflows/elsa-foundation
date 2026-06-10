using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeVolatileWaitContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void VolatileWaitRegistration_IsActivityExecutionScopedAndNotDurableResumeState()
    {
        var registration = NewVolatileWait();

        Assert.Equal(RuntimeWaitMode.VolatileWait, registration.WaitMode);
        Assert.Equal("wfexec-1", registration.WorkflowExecutionId);
        Assert.Equal("actexec-1", registration.ActivityExecutionId);
        Assert.Equal("branch-a", registration.BranchId);
        Assert.Equal("timer", registration.AwaitableKind);
        Assert.Equal(VolatileWaitStatus.Registered, registration.Status);
    }

    [Fact]
    public void SchedulerState_CarriesVolatileContinuationWorkSeparatelyFromActivityScheduling()
    {
        var scheduler = new SchedulerState(
            WorkflowExecutionId: "wfexec-1",
            Version: 4,
            PendingWork: [],
            PendingContinuations:
            [
                new SchedulerContinuationWorkItem(
                    workItemId: "continuation-1",
                    workflowExecutionId: "wfexec-1",
                    activityExecutionId: "actexec-1",
                    branchId: "branch-a",
                    kind: SchedulerContinuationKind.VolatileWaitCompleted,
                    volatileWaitId: "wait-1",
                    enqueuedAt: _now.AddSeconds(5),
                    reason: "VolatileWaitCompleted")
            ],
            VolatileWaits: [NewVolatileWait()]);

        Assert.Empty(scheduler.PendingWork);
        var continuation = Assert.Single(scheduler.PendingContinuations);
        Assert.Equal(SchedulerContinuationKind.VolatileWaitCompleted, continuation.Kind);
        Assert.Equal("wait-1", continuation.VolatileWaitId);
        Assert.Equal("actexec-1", continuation.ActivityExecutionId);
        Assert.Equal("branch-a", continuation.BranchId);
    }

    [Theory]
    [InlineData(SchedulerContinuationKind.VolatileWaitCompleted)]
    [InlineData(SchedulerContinuationKind.VolatileWaitCancelled)]
    [InlineData(SchedulerContinuationKind.VolatileWaitExpired)]
    [InlineData(SchedulerContinuationKind.VolatileWaitFaulted)]
    public void VolatileWaitContinuationWork_RequiresVolatileWaitId(SchedulerContinuationKind kind)
    {
        var exception = Assert.Throws<ArgumentException>(() => new SchedulerContinuationWorkItem(
            workItemId: "continuation-1",
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            branchId: "branch-a",
            kind: kind,
            volatileWaitId: null,
            enqueuedAt: _now,
            reason: "wait finished"));

        Assert.Contains("volatile wait ID", exception.Message);
    }

    [Fact]
    public void VolatileWaitRegistration_RejectsInvalidExpiration()
    {
        Assert.Throws<ArgumentException>(() => new VolatileWaitRegistration(
            waitId: "wait-1",
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            branchId: "branch-a",
            registeredAt: _now,
            expiresAt: _now,
            awaitableKind: "timer",
            status: VolatileWaitStatus.Registered,
            hostShutdownBehavior: VolatileWaitHostShutdownBehavior.CancelWait,
            cancellationBehavior: VolatileWaitCancellationBehavior.CancelWait));
    }

    [Fact]
    public void VolatileWaitPolicyDecision_RequiresReasonWhenDenied()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeVolatileWaitPolicyDecision(
            isAllowed: false,
            hostShutdownBehavior: VolatileWaitHostShutdownBehavior.CancelWait,
            cancellationBehavior: VolatileWaitCancellationBehavior.CancelWait,
            durableFallbackPolicy: VolatileWaitDurableFallbackPolicy.NotAllowed,
            maximumDuration: TimeSpan.FromSeconds(30),
            reason: null));
    }

    [Fact]
    public void VolatileWaitPolicyRequest_ExposesHostGuardrails()
    {
        var request = new RuntimeVolatileWaitPolicyRequest(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            branchId: "branch-a",
            awaitableKind: "timer",
            requestedDuration: TimeSpan.FromSeconds(5),
            hostSupportsInMemoryContinuation: true,
            requestedHostShutdownBehavior: VolatileWaitHostShutdownBehavior.CancelWait,
            requestedCancellationBehavior: VolatileWaitCancellationBehavior.CompleteWithCancellationResult,
            durableFallbackPolicy: VolatileWaitDurableFallbackPolicy.AllowedWhenBookmarkDeclared,
            metadata: new Dictionary<string, string> { ["Host"] = "http" });

        Assert.True(request.HostSupportsInMemoryContinuation);
        Assert.Equal(TimeSpan.FromSeconds(5), request.RequestedDuration);
        Assert.Equal(VolatileWaitHostShutdownBehavior.CancelWait, request.RequestedHostShutdownBehavior);
        Assert.Equal(VolatileWaitCancellationBehavior.CompleteWithCancellationResult, request.RequestedCancellationBehavior);
        Assert.Equal(VolatileWaitDurableFallbackPolicy.AllowedWhenBookmarkDeclared, request.DurableFallbackPolicy);
        Assert.Equal("http", request.Metadata["Host"]);
    }

    private VolatileWaitRegistration NewVolatileWait() => new(
        waitId: "wait-1",
        workflowExecutionId: "wfexec-1",
        activityExecutionId: "actexec-1",
        branchId: "branch-a",
        registeredAt: _now,
        expiresAt: _now.AddSeconds(5),
        awaitableKind: "timer",
        status: VolatileWaitStatus.Registered,
        hostShutdownBehavior: VolatileWaitHostShutdownBehavior.CancelWait,
        cancellationBehavior: VolatileWaitCancellationBehavior.CancelWait,
        metadata: new Dictionary<string, string> { ["Source"] = "unit-test" });
}
