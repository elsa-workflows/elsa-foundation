using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePostCommitOutboxStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InMemoryRuntimeCheckpointCommitStore_SavesAndQueriesDeliverablePendingItems()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var available = NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-1));
        var duplicateAvailable = NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-1));
        var unavailable = NewOutboxItem("outbox-2", "intent-2", "wfexec-1", availableAt: _now.AddMinutes(1));
        var otherWorkflow = NewOutboxItem("outbox-3", "intent-3", "wfexec-2", availableAt: _now.AddSeconds(-1));

        await store.AddPendingForTestingAsync(unavailable);
        await store.AddPendingForTestingAsync(otherWorkflow);
        await store.AddPendingForTestingAsync(available);
        await store.AddPendingForTestingAsync(duplicateAvailable);

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 10, workflowExecutionId: "wfexec-1"));

        var item = Assert.Single(deliverable);
        Assert.NotSame(available, duplicateAvailable);
        Assert.Equal(available.OutboxItemId, item.OutboxItemId);
        Assert.Equal("intent-1", item.Intent.IntentId);
    }

    [Fact]
    public async Task InMemoryRuntimeCheckpointCommitStore_RejectsOwnerFilteredQueriesBecauseClaimingIsOutOfScope()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1"));

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            now: _now,
            limit: 10,
            ownerId: "dispatcher-1")).AsTask());

        Assert.Contains("ownership filtering", exception.Message);
    }

    [Fact]
    public async Task InMemoryRuntimeCheckpointCommitStore_ReturnsDeliverableItemsInDeterministicOrderWithLimit()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var second = NewOutboxItem("outbox-2", "intent-2", "wfexec-1", availableAt: _now.AddSeconds(-3));
        var first = NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-5));
        var third = NewOutboxItem("outbox-3", "intent-3", "wfexec-1", availableAt: _now.AddSeconds(-1));

        await store.AddPendingForTestingAsync(third);
        await store.AddPendingForTestingAsync(second);
        await store.AddPendingForTestingAsync(first);

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 2));

        Assert.Equal(["outbox-1", "outbox-2"], deliverable.Select(item => item.OutboxItemId));
    }

    [Fact]
    public async Task ClaimAsync_AtomicallyAssignsOwnerTokenAndVisibility()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1"));

        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "worker-1", _now, TimeSpan.FromMinutes(1), 10)));

        Assert.Equal("worker-1", claim.OwnerId);
        Assert.Equal(1, claim.FencingToken);
        Assert.Equal(_now.AddMinutes(1), claim.VisibleAfter);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, claim.Item.Status);
        Assert.Equal(claim.FencingToken, claim.Item.DeliveryFencingToken);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, 10)));
    }

    [Fact]
    public async Task ExpiredClaim_IsReclaimedWithHigherFenceAndRejectsStaleAcknowledgement()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1"));
        var first = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "worker-1", _now, TimeSpan.FromSeconds(10), 10)));
        var second = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "worker-2", _now.AddSeconds(11), TimeSpan.FromSeconds(10), 10)));
        var delivered = new RuntimePostCommitOutboxDeliveryResult(
            "outbox-1", RuntimePostCommitOutboxStatus.Delivered, _now.AddSeconds(12));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RecordDeliveryResultAsync(first, delivered).AsTask());
        await store.RecordDeliveryResultAsync(second, delivered);

        Assert.Equal(2, second.FencingToken);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddMinutes(1), 10)));
    }

    [Fact]
    public async Task ClaimedItem_RejectsLegacyResultWithoutOwnerAndFence()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1"));
        await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "worker-1", _now, TimeSpan.FromMinutes(1), 10));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
                "outbox-1", RuntimePostCommitOutboxStatus.Delivered, _now.AddSeconds(1))).AsTask());

        Assert.Contains("owner and fencing token", exception.Message);
    }

    [Fact]
    public async Task InMemoryRuntimeCheckpointCommitStore_RejectsNonPendingSaveAndConflictingDuplicate()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var pending = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");
        var conflicting = NewOutboxItem("outbox-1", "intent-2", "wfexec-1");
        var conflictingWaitDependency = NewOutboxItem(
            "outbox-1",
            "intent-1",
            "wfexec-1",
            dependsOnWaitRegistrationId: "wait-1",
            failurePolicy: RuntimeWaitDependentIntentFailurePolicy.FaultWorkflow);
        var conflictingPayload = NewOutboxItem(
            "outbox-1",
            "intent-1",
            "wfexec-1",
            payloadJson: """{"signal":"different"}""");
        var conflictingMetadata = NewOutboxItem(
            "outbox-1",
            "intent-1",
            "wfexec-1",
            metadata: new Dictionary<string, string> { ["source"] = "different" });
        var delivered = NewOutboxItem("outbox-2", "intent-3", "wfexec-1", RuntimePostCommitOutboxStatus.Delivered, deliveredAt: _now);

        await store.AddPendingForTestingAsync(pending);

        var duplicateException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddPendingForTestingAsync(conflicting).AsTask());
        var waitDependencyException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddPendingForTestingAsync(conflictingWaitDependency).AsTask());
        var payloadException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddPendingForTestingAsync(conflictingPayload).AsTask());
        var metadataException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddPendingForTestingAsync(conflictingMetadata).AsTask());
        var statusException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddPendingForTestingAsync(delivered).AsTask());

        Assert.Contains("already exists", duplicateException.Message);
        Assert.Contains("already exists", waitDependencyException.Message);
        Assert.Contains("already exists", payloadException.Message);
        Assert.Contains("already exists", metadataException.Message);
        Assert.Contains("pending", statusException.Message);
    }

    [Fact]
    public async Task InMemoryRuntimeCheckpointCommitStore_RecordDeliveredResultRemovesItemFromDeliverableQuery()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");

        await store.AddPendingForTestingAsync(item);
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.Delivered,
            recordedAt: _now.AddSeconds(1)));

        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddMinutes(1), limit: 10)));
    }

    [Fact]
    public async Task InMemoryRuntimeCheckpointCommitStore_RetryableFailureRespectsRetryDelay()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");

        await store.AddPendingForTestingAsync(item);
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.FailedRetryable,
            recordedAt: _now.AddSeconds(1),
            failureMessage: "dispatch failed"));

        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(5), limit: 10)));

        var retryable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(11), limit: 10));
        var retryableItem = Assert.Single(retryable);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, retryableItem.Status);
        Assert.Equal(1, retryableItem.DeliveryAttemptCount);
        Assert.Equal("dispatch failed", retryableItem.LastFailureMessage);
    }

    [Fact]
    public async Task InMemoryRuntimeCheckpointCommitStore_RetryableFailureWithoutRemainingAttemptsBecomesTerminal()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var item = NewOutboxItem(
            "outbox-1",
            "intent-1",
            "wfexec-1",
            retryPolicy: RuntimePostCommitRetryPolicy.None);

        await store.AddPendingForTestingAsync(item);
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.FailedRetryable,
            recordedAt: _now.AddSeconds(1),
            failureMessage: "dispatch failed"));

        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddMinutes(1), limit: 10)));

        var terminalException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.Cancelled,
            recordedAt: _now.AddSeconds(2))).AsTask());
        Assert.Contains("terminal", terminalException.Message);
    }

    [Fact]
    public async Task InMemoryRuntimeCheckpointCommitStore_FailedFinalResultIsTerminalAndNotDeliverable()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");

        await store.AddPendingForTestingAsync(item);
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.FailedFinal,
            recordedAt: _now.AddSeconds(1),
            failureMessage: "dispatch failed"));

        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddMinutes(1), limit: 10)));

        var terminalException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.Cancelled,
            recordedAt: _now.AddSeconds(2))).AsTask());
        Assert.Contains("terminal", terminalException.Message);
    }

    [Fact]
    public async Task RetryUntilAcknowledged_RemainsRetryableAndSaturatesAttemptCount()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var policy = RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(10));
        var pending = NewOutboxItem("outbox-unbounded", "intent-unbounded", "wfexec-1", retryPolicy: policy);
        var saturated = new RuntimePostCommitOutboxItem(
            pending.OutboxItemId,
            pending.Intent,
            pending.Status,
            pending.RecordedAt,
            pending.AvailableAt,
            pending.RetryPolicy,
            deliveryAttemptCount: int.MaxValue,
            metadata: pending.Metadata);
        await store.AddPendingForTestingAsync(saturated);

        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            saturated.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable,
            _now,
            "still waiting"));

        var retry = Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(10), 10)));
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, retry.Status);
        Assert.Equal(int.MaxValue, retry.DeliveryAttemptCount);
        Assert.Equal(_now.AddSeconds(10), retry.AvailableAt);
    }

    [Fact]
    public async Task Lookup_ReturnsItemsInEveryStatusAndMissingAsNull()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-lookup", "intent-lookup", "wfexec-1"));
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            "outbox-lookup", RuntimePostCommitOutboxStatus.Delivered, _now));

        var delivered = await store.FindAsync("outbox-lookup");

        Assert.NotNull(delivered);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Null(await store.FindAsync("missing"));
    }

    [Fact]
    public async Task InMemoryRuntimeCheckpointCommitStore_RejectsDeliveryResultForMissingOrTerminalItem()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");

        var missingException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "missing",
            status: RuntimePostCommitOutboxStatus.Cancelled,
            recordedAt: _now)).AsTask());

        await store.AddPendingForTestingAsync(item);
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.Cancelled,
            recordedAt: _now));

        var terminalException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.Cancelled,
            recordedAt: _now)).AsTask());

        Assert.Contains("not found", missingException.Message);
        Assert.Contains("terminal", terminalException.Message);
    }

    private RuntimePostCommitOutboxItem NewOutboxItem(
        string outboxItemId,
        string intentId,
        string workflowExecutionId,
        RuntimePostCommitOutboxStatus status = RuntimePostCommitOutboxStatus.Pending,
        DateTimeOffset? availableAt = null,
        DateTimeOffset? deliveredAt = null,
        RuntimePostCommitRetryPolicy? retryPolicy = null,
        string? dependsOnWaitRegistrationId = null,
        RuntimeWaitDependentIntentFailurePolicy? failurePolicy = null,
        string payloadJson = """{"signal":"sent"}""",
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            outboxItemId: outboxItemId,
            intent: NewIntent(intentId, workflowExecutionId, dependsOnWaitRegistrationId, failurePolicy, payloadJson, metadata),
            status: status,
            recordedAt: _now,
            availableAt: availableAt ?? _now,
            retryPolicy: retryPolicy ?? new RuntimePostCommitRetryPolicy(3, TimeSpan.FromSeconds(10)),
            deliveredAt: deliveredAt);

    private RuntimePostCommitIntent NewIntent(
        string intentId,
        string workflowExecutionId,
        string? dependsOnWaitRegistrationId = null,
        RuntimeWaitDependentIntentFailurePolicy? failurePolicy = null,
        string payloadJson = """{"signal":"sent"}""",
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        using var document = JsonDocument.Parse(payloadJson);

        return new RuntimePostCommitIntent(
            intentId: intentId,
            workflowExecutionId: workflowExecutionId,
            kind: "DispatchSignal",
            recordedAt: _now,
            activityExecutionId: "actexec-1",
            idempotencyKey: $"checkpoint-1:{intentId}",
            payload: document.RootElement.Clone(),
            metadata: metadata ?? new Dictionary<string, string>(),
            dependsOnWaitRegistrationId: dependsOnWaitRegistrationId,
            waitFailurePolicy: failurePolicy);
    }
}
