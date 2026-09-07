using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;
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

    [Fact]
    public async Task ScanPageAsync_ReturnsBoundedPagesWithAContinuation()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState("operational-1", "wfexec-1", "worker-1", leaseExpiresAt: _now.AddSeconds(-3)));
        await store.SaveAsync(NewExecutionLivenessState("operational-2", "wfexec-2", "worker-1", leaseExpiresAt: _now.AddSeconds(-2)));
        await store.SaveAsync(NewExecutionLivenessState("operational-3", "wfexec-3", "worker-1", leaseExpiresAt: _now.AddSeconds(-1)));

        var first = await scanner.ScanPageAsync(NewRequest(limit: 2));
        var second = await scanner.ScanPageAsync(NewRequest(limit: 2, continuationToken: first.NextContinuationToken));

        Assert.Equal(["wfexec-1", "wfexec-2"], first.Items.Select(candidate => candidate.WorkflowExecutionId));
        Assert.Equal(["wfexec-3"], second.Items.Select(candidate => candidate.WorkflowExecutionId));
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task ScanPageAsync_RejectsAContinuationBoundToDifferentScanOptions()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        await store.SaveAsync(NewExecutionLivenessState("operational-1", "wfexec-1", "worker-1", leaseExpiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(NewExecutionLivenessState("operational-2", "wfexec-2", "worker-1", leaseExpiresAt: _now.AddSeconds(-2)));

        var first = await scanner.ScanPageAsync(NewRequest(limit: 1));

        await Assert.ThrowsAsync<ArgumentException>(() => scanner.ScanPageAsync(
            NewRequest(limit: 1, continuationToken: first.NextContinuationToken, ownerId: "worker-1")).AsTask());
    }

    [Fact]
    public async Task ScanPageAsync_ContinuesAcrossScannerInstancesWithTheSameSigningKey()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        await store.SaveAsync(NewExecutionLivenessState("operational-1", "wfexec-1", "worker-1", leaseExpiresAt: _now.AddSeconds(-2)));
        await store.SaveAsync(NewExecutionLivenessState("operational-2", "wfexec-2", "worker-1", leaseExpiresAt: _now.AddSeconds(-1)));

        var firstScanner = new InMemoryRuntimeRecoveryScanner(store, RecoveryCodec("shared-recovery-signing-key-32-bytes"));
        var first = await firstScanner.ScanPageAsync(NewRequest(limit: 1));
        var secondScanner = new InMemoryRuntimeRecoveryScanner(store, RecoveryCodec("shared-recovery-signing-key-32-bytes"));

        var second = await secondScanner.ScanPageAsync(NewRequest(
            limit: 1,
            continuationToken: first.NextContinuationToken));

        Assert.Equal("wfexec-1", Assert.Single(first.Items).WorkflowExecutionId);
        Assert.Equal("wfexec-2", Assert.Single(second.Items).WorkflowExecutionId);
    }

    [Fact]
    public async Task ScanPageAsync_RejectsAContinuationFromADifferentSigningKey()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        await store.SaveAsync(NewExecutionLivenessState("operational-1", "wfexec-1", "worker-1", leaseExpiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(NewExecutionLivenessState("operational-2", "wfexec-2", "worker-1", leaseExpiresAt: _now.AddSeconds(-2)));
        var first = await new InMemoryRuntimeRecoveryScanner(
            store,
            RecoveryCodec("shared-recovery-signing-key-32-bytes"))
            .ScanPageAsync(NewRequest(limit: 1));

        var wrongKeyScanner = new InMemoryRuntimeRecoveryScanner(
            store,
            RecoveryCodec("different-recovery-signing-key-32-bytes"));

        await Assert.ThrowsAsync<ArgumentException>(() => wrongKeyScanner.ScanPageAsync(NewRequest(
            limit: 1,
            continuationToken: first.NextContinuationToken)).AsTask());
    }

    [Fact]
    public void RecoveryContinuationCodec_BindsThePurposeToItsSignature()
    {
        var codec = RecoveryCodec("shared-recovery-signing-key-32-bytes");
        var token = codec.Encode("recovery-page", [1, 2, 3]);

        Assert.Throws<ArgumentException>(() => codec.Decode("different-page", token));
    }

    [Fact]
    public void RecoveryContinuationCodec_RefusesAnEphemeralKeyWhenDurablePagingIsRequired()
    {
        Assert.Throws<InvalidOperationException>(() => new HmacRuntimeRecoveryContinuationCodec(
            Options.Create(new RuntimeRecoveryContinuationOptions
            {
                AllowEphemeralDevelopmentKey = false
            })));
    }

    [Fact]
    public void RecoverySweepCursorStore_DoesNotLeakOrEvictAReinsertedGeneration()
    {
        var store = new InMemoryRuntimeRecoverySweepCursorStore();
        var cursor = new RuntimeRecoverySweepCursor(
            _now,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(1),
            10,
            "cursor");

        for (var cycle = 0; cycle < 2_000; cycle++)
        {
            store.Set("tenant-a", "scanner", cursor);
            store.Clear("tenant-a", "scanner");
        }

        for (var index = 0; index < 1_024; index++)
            store.Set("tenant-a", $"scanner-{index}", cursor);

        store.Clear("tenant-a", "scanner");
        store.Set("tenant-a", "scanner", cursor);
        Assert.Equal(cursor, store.Get("tenant-a", "scanner"));
        Assert.Null(store.Get("tenant-a", "scanner-0"));
        Assert.Equal(cursor, store.Get("tenant-a", "scanner-1023"));
    }

    [Fact]
    public void RecoveryScanRequest_RejectsAnUnboundedPageLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewRequest(limit: RuntimeStorePageRequest.MaximumLimit + 1));
    }

    [Fact]
    public async Task ScanPageAsync_DoesNotDropCandidatesAfterTheProviderPageBoundary()
    {
        var store = new InMemoryExecutionLivenessStateStore();
        var scanner = new InMemoryRuntimeRecoveryScanner(store);
        for (var index = 0; index <= RuntimeStorePageRequest.MaximumLimit; index++)
        {
            await store.SaveAsync(NewExecutionLivenessState(
                $"operational-{index:D3}",
                $"wfexec-{index:D3}",
                "worker-1",
                leaseAcquiredAt: _now.AddMinutes(-index - 2),
                leaseExpiresAt: _now.AddMinutes(-index - 1)));
        }

        var first = await scanner.ScanPageAsync(NewRequest(limit: RuntimeStorePageRequest.MaximumLimit));
        var second = await scanner.ScanPageAsync(NewRequest(
            limit: RuntimeStorePageRequest.MaximumLimit,
            continuationToken: first.NextContinuationToken));

        Assert.Equal(RuntimeStorePageRequest.MaximumLimit, first.Items.Count);
        Assert.InRange(
            first.NextContinuationToken?.Length ?? 0,
            1,
            RuntimeStorePageRequest.MaximumContinuationTokenLength);
        Assert.Single(second.Items);
        Assert.Equal("wfexec-000", second.Items[0].WorkflowExecutionId);
        Assert.Null(second.NextContinuationToken);
    }

    [Fact]
    public async Task ScanPageAsync_RefusesAnIdOrderedCustomStoreInsteadOfStarvingDueOrderedRecovery()
    {
        var customStore = new IdOrderedCustomLivenessStore(
        [
            NewExecutionLivenessState(
                "operational-z",
                "wfexec-z",
                "worker-1",
                leaseAcquiredAt: _now.AddMinutes(-10),
                leaseExpiresAt: _now.AddMinutes(-2)),
            NewExecutionLivenessState(
                "operational-a",
                "wfexec-a",
                "worker-1",
                leaseAcquiredAt: _now.AddMinutes(-6),
                leaseExpiresAt: _now.AddMinutes(-1))
        ]);
        var scanner = new InMemoryRuntimeRecoveryScanner(customStore);

        await Assert.ThrowsAsync<NotSupportedException>(() => scanner.ScanPageAsync(NewRequest(limit: 1)).AsTask());

        // The legacy collection surface remains compatible and can inspect the complete custom-store view. A page
        // caller must opt into the explicit due-ordered capability rather than silently paging by workflow ID.
        var legacy = await scanner.ScanAsync(NewRequest(limit: 1));
        Assert.Equal("wfexec-z", Assert.Single(legacy).WorkflowExecutionId);
    }

    private RuntimeRecoveryScanRequest NewRequest(string? ownerId = null, int limit = 10, string? continuationToken = null) =>
        new(
            now: _now,
            leaseTimeout: TimeSpan.FromMinutes(5),
            heartbeatTimeout: TimeSpan.FromMinutes(1),
            limit: limit,
            ownerId: ownerId,
            continuationToken: continuationToken);

    private static IRuntimeRecoveryContinuationCodec RecoveryCodec(string signingKey) =>
        new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        {
            SigningKey = signingKey,
            AllowEphemeralDevelopmentKey = false
        }));

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

    private sealed class IdOrderedCustomLivenessStore : IExecutionLivenessStateStore
    {
        private readonly InMemoryExecutionLivenessStateStore inner;

        public IdOrderedCustomLivenessStore(IReadOnlyCollection<ExecutionLivenessState> states)
        {
            inner = new InMemoryExecutionLivenessStateStore();
            foreach (var state in states)
                inner.SaveAsync(state).GetAwaiter().GetResult();
        }

        public ValueTask<ExecutionLivenessState> SaveAsync(ExecutionLivenessState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public ValueTask<ExecutionLivenessStateWriteResult> TrySaveAsync(ExecutionLivenessState state, long expectedRevision, CancellationToken cancellationToken = default) =>
            inner.TrySaveAsync(state, expectedRevision, cancellationToken);

        public ValueTask<ExecutionLivenessState?> FindAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, operationalStateId, cancellationToken);

        public ValueTask<VersionedExecutionLivenessState?> FindVersionedAsync(string workflowExecutionId, string operationalStateId, CancellationToken cancellationToken = default) =>
            inner.FindVersionedAsync(workflowExecutionId, operationalStateId, cancellationToken);

        public ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListAllPageAsync(RuntimeStorePageRequest query, CancellationToken cancellationToken = default) =>
            inner.ListAllPageAsync(query, cancellationToken);
    }
}
