using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeRecoveryScannerTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScanAsync_ReturnsLeaseCandidateWhenExpiresAtHasPassed()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState("operational-1", "wfexec-1", "worker-1", leaseExpiresAt: _now.AddSeconds(-1)));

        var candidate = Assert.Single(await scanner.ScanAsync(NewRequest()));

        Assert.Equal("wfexec-1", candidate.WorkflowExecutionId);
        Assert.Equal("operational-1", candidate.OperationalStateId);
        Assert.Equal(RuntimeInterruptionReason.LeaseLost, candidate.Reason);
        Assert.False(candidate.RequeueFromLastCheckpoint);
        Assert.Equal("ExecutionLease", candidate.Metadata["runtime.recovery.source"]);
    }

    [Fact]
    public async Task ScanAsync_ReturnsLeaseCandidateWhenLeaseTimeoutIsExceeded()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState(
            "operational-1",
            "wfexec-1",
            "worker-1",
            leaseAcquiredAt: _now.AddMinutes(-6),
            leaseExpiresAt: _now.AddMinutes(5)));

        var candidate = Assert.Single(await scanner.ScanAsync(NewRequest()));

        Assert.Equal("wfexec-1", candidate.WorkflowExecutionId);
        Assert.Equal("operational-1", candidate.OperationalStateId);
        Assert.Equal(RuntimeInterruptionReason.LeaseLost, candidate.Reason);
        Assert.False(candidate.RequeueFromLastCheckpoint);
        Assert.Equal("ExecutionLease", candidate.Metadata["runtime.recovery.source"]);
    }

    [Fact]
    public async Task ScanAsync_ReturnsStaleHeartbeatWhenLeaseIsLive()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState(
            "operational-1",
            "wfexec-1",
            "worker-1",
            leaseExpiresAt: _now.AddMinutes(5),
            heartbeatRecordedAt: _now.AddMinutes(-2)));

        var candidate = Assert.Single(await scanner.ScanAsync(NewRequest()));

        Assert.Equal(RuntimeInterruptionReason.HeartbeatExpired, candidate.Reason);
        Assert.Equal("Heartbeat", candidate.Metadata["runtime.recovery.source"]);
    }

    [Fact]
    public async Task ScanAsync_ReturnsDetectedInterruptedExecutionWithCheckpoint()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState(
            "operational-1",
            "wfexec-1",
            "worker-1",
            interruptedExecution: new InterruptedExecutionState(
                interruptionId: "interruption-1",
                workflowExecutionId: "wfexec-1",
                leaseId: "lease-worker-1",
                lastCheckpointId: "checkpoint-1",
                reason: RuntimeInterruptionReason.HostStopped,
                status: RuntimeInterruptionStatus.Detected,
                interruptedAt: _now.AddSeconds(-30))));

        var candidate = Assert.Single(await scanner.ScanAsync(NewRequest()));

        Assert.Equal(RuntimeInterruptionReason.HostStopped, candidate.Reason);
        Assert.Equal("checkpoint-1", candidate.LastCheckpointId);
        Assert.True(candidate.RequeueFromLastCheckpoint);
        Assert.Equal("InterruptedExecution", candidate.Metadata["runtime.recovery.source"]);
    }

    [Fact]
    public async Task ScanAsync_ReturnsOwnerFilteredDetectedInterruptedExecutionWhenOperationalOwnerIsUnknown()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState(
            "operational-1",
            "wfexec-1",
            "worker-1",
            includeLease: false,
            includeHeartbeat: false,
            interruptedExecution: new InterruptedExecutionState(
                interruptionId: "interruption-1",
                workflowExecutionId: "wfexec-1",
                leaseId: null,
                lastCheckpointId: "checkpoint-1",
                reason: RuntimeInterruptionReason.HostStopped,
                status: RuntimeInterruptionStatus.Detected,
                interruptedAt: _now.AddSeconds(-30))));

        var candidate = Assert.Single(await scanner.ScanAsync(NewRequest(ownerId: "worker-1")));

        Assert.Equal(RuntimeInterruptionReason.HostStopped, candidate.Reason);
        Assert.Equal("InterruptedExecution", candidate.Metadata["runtime.recovery.source"]);
    }

    [Fact]
    public async Task ScanAsync_FallsThroughToExpiredLeaseWhenInterruptedExecutionWasAlreadyHandled()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState(
            "operational-1",
            "wfexec-1",
            "worker-1",
            leaseExpiresAt: _now.AddSeconds(-1),
            interruptedExecution: new InterruptedExecutionState(
                interruptionId: "interruption-1",
                workflowExecutionId: "wfexec-1",
                leaseId: "lease-worker-1",
                lastCheckpointId: "checkpoint-1",
                reason: RuntimeInterruptionReason.HostStopped,
                status: RuntimeInterruptionStatus.Requeued,
                interruptedAt: _now.AddSeconds(-30))));

        var candidate = Assert.Single(await scanner.ScanAsync(NewRequest()));

        Assert.Equal(RuntimeInterruptionReason.LeaseLost, candidate.Reason);
        Assert.Equal("checkpoint-1", candidate.LastCheckpointId);
        Assert.Equal("ExecutionLease", candidate.Metadata["runtime.recovery.source"]);
    }

    [Fact]
    public async Task ScanAsync_HonorsOwnerFilterAndLimitDeterministically()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState("operational-2", "wfexec-2", "worker-1", leaseExpiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(NewExecutionLivenessState("operational-1", "wfexec-1", "worker-1", leaseExpiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(NewExecutionLivenessState("operational-3", "wfexec-3", "worker-2", leaseExpiresAt: _now.AddSeconds(-1)));

        var candidates = await scanner.ScanAsync(NewRequest(ownerId: "worker-1", limit: 1));

        var candidate = Assert.Single(candidates);
        Assert.Equal("wfexec-1", candidate.WorkflowExecutionId);
        Assert.Equal("operational-1", candidate.OperationalStateId);
    }

    [Fact]
    public async Task ScanAsync_SelectsTheOldestEligibleSignalBeforeApplyingTheLimit()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState(
            "operational-1",
            "wfexec-1",
            "worker-1",
            leaseExpiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(NewExecutionLivenessState(
            "operational-2",
            "wfexec-2",
            "worker-1",
            leaseAcquiredAt: _now.AddMinutes(-3),
            leaseExpiresAt: _now.AddMinutes(-2)));

        var candidate = Assert.Single(await scanner.ScanAsync(NewRequest(limit: 1)));

        Assert.Equal("wfexec-2", candidate.WorkflowExecutionId);
    }

    [Fact]
    public async Task ScanAsync_AppliesOwnerFilterToTheRecoverySource()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState(
            "operational-1",
            "wfexec-1",
            "worker-1",
            leaseOwnerId: "lease-owner",
            heartbeatOwnerId: "heartbeat-owner",
            leaseExpiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(NewExecutionLivenessState(
            "operational-2",
            "wfexec-2",
            "worker-2",
            leaseOwnerId: "lease-owner",
            heartbeatOwnerId: "heartbeat-owner",
            heartbeatRecordedAt: _now.AddMinutes(-2)));

        var heartbeatOwnedCandidates = await scanner.ScanAsync(NewRequest(ownerId: "heartbeat-owner"));

        var candidate = Assert.Single(heartbeatOwnedCandidates);
        Assert.Equal("wfexec-2", candidate.WorkflowExecutionId);
        Assert.Equal(RuntimeInterruptionReason.HeartbeatExpired, candidate.Reason);
    }

    [Fact]
    public async Task ScanAsync_IgnoresLiveOperationalState()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState("operational-1", "wfexec-1", "worker-1"));

        Assert.Empty(await scanner.ScanAsync(NewRequest()));
    }

    private RuntimeRecoveryScanRequest NewRequest(string? ownerId = null, int limit = 10) =>
        new(
            now: _now,
            leaseTimeout: TimeSpan.FromMinutes(5),
            heartbeatTimeout: TimeSpan.FromMinutes(1),
            limit: limit,
            ownerId: ownerId);

    private ExecutionLivenessState NewExecutionLivenessState(
        string operationalStateId,
        string workflowExecutionId,
        string ownerId,
        string? leaseOwnerId = null,
        string? heartbeatOwnerId = null,
        DateTimeOffset? leaseAcquiredAt = null,
        DateTimeOffset? leaseExpiresAt = null,
        DateTimeOffset? heartbeatRecordedAt = null,
        bool includeLease = true,
        bool includeHeartbeat = true,
        InterruptedExecutionState? interruptedExecution = null) =>
        new(
            operationalStateId: operationalStateId,
            workflowExecutionId: workflowExecutionId,
            executionLease: includeLease
                ? new RuntimeExecutionLease(
                leaseId: $"lease-{ownerId}",
                workflowExecutionId: workflowExecutionId,
                ownerId: leaseOwnerId ?? ownerId,
                acquiredAt: leaseAcquiredAt ?? _now.AddMinutes(-1),
                expiresAt: leaseExpiresAt ?? _now.AddMinutes(5),
                fencingToken: 1)
                : null,
            heartbeat: includeHeartbeat
                ? new RuntimeHeartbeat(
                heartbeatId: $"heartbeat-{ownerId}",
                workflowExecutionId: workflowExecutionId,
                ownerId: heartbeatOwnerId ?? ownerId,
                leaseId: $"lease-{ownerId}",
                recordedAt: heartbeatRecordedAt ?? _now)
                : null,
            drain: null,
            interruptedExecution: interruptedExecution);
}
