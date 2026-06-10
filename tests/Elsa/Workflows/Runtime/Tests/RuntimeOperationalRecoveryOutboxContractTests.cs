using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeOperationalRecoveryOutboxContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OperationalState_SeparatesLeaseHeartbeatDrainAndInterruptionFromDomainRetry()
    {
        var state = new OperationalState(
            operationalStateId: "operational-1",
            workflowExecutionId: "wfexec-1",
            executionLease: new RuntimeExecutionLease(
                leaseId: "lease-1",
                workflowExecutionId: "wfexec-1",
                ownerId: "worker-1",
                acquiredAt: _now,
                expiresAt: _now.AddMinutes(5),
                fencingToken: 7),
            heartbeat: new RuntimeHeartbeat(
                heartbeatId: "heartbeat-1",
                workflowExecutionId: "wfexec-1",
                ownerId: "worker-1",
                leaseId: "lease-1",
                recordedAt: _now),
            drain: new RuntimeDrainState(
                drainId: "drain-1",
                mode: RuntimeDrainMode.Quiesce,
                status: RuntimeDrainStatus.Requested,
                requestedAt: _now,
                requestedBy: "host"),
            interruptedExecution: new InterruptedExecutionState(
                interruptionId: "interruption-1",
                workflowExecutionId: "wfexec-1",
                leaseId: "lease-1",
                lastCheckpointId: "checkpoint-1",
                reason: RuntimeInterruptionReason.LeaseLost,
                status: RuntimeInterruptionStatus.Detected,
                interruptedAt: _now,
                activityExecutionIds: ["actexec-1"]),
            pendingPostCommitIntentIds: ["intent-1"]);

        Assert.True(state.ExecutionLease!.IsExpired(_now.AddMinutes(6)));
        Assert.True(state.Drain!.StopsNewWork);
        Assert.Equal("checkpoint-1", state.InterruptedExecution!.LastCheckpointId);
        Assert.DoesNotContain(
            typeof(OperationalState).GetProperties().Select(property => property.Name),
            name => name.Contains("Retry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RecoveryCandidate_RequeuesFromLastCheckpointWithoutDomainRetry()
    {
        var scanRequest = new RuntimeRecoveryScanRequest(
            now: _now,
            leaseTimeout: TimeSpan.FromMinutes(5),
            heartbeatTimeout: TimeSpan.FromMinutes(1),
            limit: 10,
            ownerId: "worker-1");
        var candidate = new RuntimeRecoveryCandidate(
            workflowExecutionId: "wfexec-1",
            operationalStateId: "operational-1",
            lastCheckpointId: "checkpoint-1",
            reason: RuntimeInterruptionReason.LeaseLost,
            detectedAt: _now,
            requeueFromLastCheckpoint: true,
            metadata: new Dictionary<string, string>());
        var retryDecision = new RuntimeDomainRetryDecision(
            mode: RuntimeDomainRetryMode.DoNotRetry,
            delay: null,
            reason: "Operational recovery is not domain retry.",
            metadata: new Dictionary<string, string>());

        Assert.Equal(TimeSpan.FromMinutes(1), scanRequest.HeartbeatTimeout);
        Assert.True(candidate.RequeueFromLastCheckpoint);
        Assert.Equal(RuntimeDomainRetryMode.DoNotRetry, retryDecision.Mode);
        Assert.Contains("not domain retry", retryDecision.Reason);
    }

    [Fact]
    public void RecoveryCandidate_RequiresCheckpointIdWhenRequeueingFromLastCheckpoint()
    {
        var exception = Assert.Throws<ArgumentException>(() => new RuntimeRecoveryCandidate(
            workflowExecutionId: "wfexec-1",
            operationalStateId: "operational-1",
            lastCheckpointId: null,
            reason: RuntimeInterruptionReason.LeaseLost,
            detectedAt: _now,
            requeueFromLastCheckpoint: true));

        Assert.Contains("last checkpoint ID", exception.Message);
    }

    [Fact]
    public void PostCommitIntent_CanDeclareWaitDependencyWithoutGlobalBookmarkInbox()
    {
        var intent = NewIntent(
            dependsOnWaitRegistrationId: "wait-1",
            failurePolicy: RuntimeWaitDependentIntentFailurePolicy.FaultWorkflow);

        Assert.True(intent.IsWaitDependent);
        Assert.Equal("wait-1", intent.DependsOnWaitRegistrationId);
        Assert.Equal(RuntimeWaitDependentIntentFailurePolicy.FaultWorkflow, intent.WaitFailurePolicy);
    }

    [Fact]
    public void PostCommitIntent_RequiresFailurePolicyForWaitDependentIntent()
    {
        var exception = Assert.Throws<ArgumentException>(() => NewIntent(dependsOnWaitRegistrationId: "wait-1"));

        Assert.Contains("wait-dependent post-commit intent", exception.Message);
    }

    [Fact]
    public void PostCommitIntent_RejectsBlankWaitDependency()
    {
        var blankWithPolicy = Assert.Throws<ArgumentException>(() => NewIntent(
            dependsOnWaitRegistrationId: " ",
            failurePolicy: RuntimeWaitDependentIntentFailurePolicy.FaultWorkflow));
        var blankWithoutPolicy = Assert.Throws<ArgumentException>(() => NewIntent(dependsOnWaitRegistrationId: " "));

        Assert.Contains("wait registration dependency", blankWithPolicy.Message);
        Assert.Contains("wait registration dependency", blankWithoutPolicy.Message);
    }

    [Fact]
    public void OperationalState_RejectsChildStateFromAnotherWorkflowExecution()
    {
        var lease = new RuntimeExecutionLease(
            leaseId: "lease-1",
            workflowExecutionId: "other-wfexec",
            ownerId: "worker-1",
            acquiredAt: _now,
            expiresAt: _now.AddMinutes(5),
            fencingToken: 7);

        var exception = Assert.Throws<ArgumentException>(() => new OperationalState(
            operationalStateId: "operational-1",
            workflowExecutionId: "wfexec-1",
            executionLease: lease,
            heartbeat: null,
            drain: null,
            interruptedExecution: null));

        Assert.Contains("same workflow execution", exception.Message);
    }

    [Fact]
    public void PostCommitOutboxItem_RepresentsRecordCommitDeliverMarkDeliveredOrdering()
    {
        var pending = NewOutboxItem(RuntimePostCommitOutboxStatus.Pending);
        var delivering = NewOutboxItem(
            RuntimePostCommitOutboxStatus.Delivering,
            deliveringOwnerId: "dispatcher-1",
            deliveryStartedAt: _now.AddSeconds(1),
            deliveryAttemptCount: 1);
        var delivered = NewOutboxItem(
            RuntimePostCommitOutboxStatus.Delivered,
            deliveryAttemptCount: 1,
            deliveredAt: _now.AddSeconds(2));

        Assert.False(pending.IsTerminal);
        Assert.False(delivering.IsTerminal);
        Assert.True(delivered.IsTerminal);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
    }

    [Fact]
    public void PostCommitOutboxItem_RejectsDeliveredStateWithoutDeliveredTime()
    {
        var exception = Assert.Throws<ArgumentException>(() => NewOutboxItem(RuntimePostCommitOutboxStatus.Delivered));

        Assert.Contains("delivered time", exception.Message);
    }

    [Fact]
    public void PostCommitOutboxItem_RejectsActiveDeliveryOwnershipOutsideDeliveringState()
    {
        var ownerException = Assert.Throws<ArgumentException>(() => NewOutboxItem(
            RuntimePostCommitOutboxStatus.Pending,
            deliveringOwnerId: "dispatcher-1"));
        var startException = Assert.Throws<ArgumentException>(() => NewOutboxItem(
            RuntimePostCommitOutboxStatus.FailedRetryable,
            deliveryStartedAt: _now.AddSeconds(1)));

        Assert.Contains("Only delivering outbox items", ownerException.Message);
        Assert.Contains("delivery start time", startException.Message);
    }

    [Fact]
    public void RecoveryAndOutboxQueries_RejectInvalidLimitsAndRetrySettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeRecoveryScanRequest(_now, TimeSpan.Zero, TimeSpan.FromMinutes(1), 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeRecoveryScanRequest(_now, TimeSpan.FromMinutes(5), TimeSpan.Zero, 10));
        Assert.Throws<ArgumentException>(() => new RuntimeRecoveryScanRequest(_now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), 10, ownerId: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeDomainRetryDecision(
            RuntimeDomainRetryMode.RetryAfter,
            TimeSpan.Zero,
            "Back off."));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimePostCommitOutboxQuery(_now, 0));
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitOutboxQuery(_now, 10, workflowExecutionId: " "));
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitOutboxQuery(_now, 10, ownerId: " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimePostCommitRetryPolicy(-1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimePostCommitRetryPolicy(3, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitRetryPolicy(0, TimeSpan.FromSeconds(10)));
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitRetryPolicy(3, null));
    }

    [Fact]
    public void PostCommitOutboxDeliveryResult_RejectsIncompleteDeliveryOutcomes()
    {
        var incompleteException = Assert.Throws<ArgumentException>(() => new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.Pending,
            recordedAt: _now));
        var contradictoryException = Assert.Throws<ArgumentException>(() => new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.Delivered,
            recordedAt: _now,
            failureMessage: "No failure."));

        Assert.Contains("completed delivery outcome", incompleteException.Message);
        Assert.Contains("Only failed outbox delivery results", contradictoryException.Message);
    }

    [Fact]
    public void RuntimeRecoveryAndOutboxContracts_AreProviderBoundaries()
    {
        Assert.True(typeof(IRuntimeRecoveryScanner).IsInterface);
        Assert.True(typeof(IRuntimePostCommitOutboxStore).IsInterface);
        Assert.True(typeof(IRuntimePostCommitIntentDispatcher).IsInterface);
        Assert.True(typeof(IRuntimeDomainRetryPolicy).IsInterface);
    }

    private RuntimePostCommitOutboxItem NewOutboxItem(
        RuntimePostCommitOutboxStatus status,
        string? deliveringOwnerId = null,
        DateTimeOffset? deliveryStartedAt = null,
        DateTimeOffset? deliveredAt = null,
        int deliveryAttemptCount = 0) =>
        new(
            outboxItemId: "outbox-1",
            intent: NewIntent(),
            status: status,
            recordedAt: _now,
            availableAt: _now,
            retryPolicy: new RuntimePostCommitRetryPolicy(
                maxAttempts: 3,
                delay: TimeSpan.FromSeconds(10),
                metadata: new Dictionary<string, string>()),
            deliveryAttemptCount: deliveryAttemptCount,
            deliveringOwnerId: deliveringOwnerId,
            deliveryStartedAt: deliveryStartedAt,
            deliveredAt: deliveredAt);

    private RuntimePostCommitIntent NewIntent(
        string? dependsOnWaitRegistrationId = null,
        RuntimeWaitDependentIntentFailurePolicy? failurePolicy = null)
    {
        using var document = JsonDocument.Parse("""{"signal":"sent"}""");
        return new RuntimePostCommitIntent(
            intentId: "intent-1",
            workflowExecutionId: "wfexec-1",
            kind: "DispatchSignal",
            recordedAt: _now,
            activityExecutionId: "actexec-1",
            idempotencyKey: "checkpoint-1:intent-1",
            payload: document.RootElement.Clone(),
            metadata: new Dictionary<string, string>(),
            dependsOnWaitRegistrationId: dependsOnWaitRegistrationId,
            waitFailurePolicy: failurePolicy);
    }
}
