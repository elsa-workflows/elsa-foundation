using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePostCommitOutboxStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InMemoryRuntimePostCommitOutboxStore_SavesAndQueriesDeliverablePendingItems()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var available = NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-1));
        var duplicateAvailable = NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-1));
        var unavailable = NewOutboxItem("outbox-2", "intent-2", "wfexec-1", availableAt: _now.AddMinutes(1));
        var otherWorkflow = NewOutboxItem("outbox-3", "intent-3", "wfexec-2", availableAt: _now.AddSeconds(-1));

        await store.SavePendingAsync(unavailable);
        await store.SavePendingAsync(otherWorkflow);
        await store.SavePendingAsync(available);
        await store.SavePendingAsync(duplicateAvailable);

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 10, workflowExecutionId: "wfexec-1"));

        var item = Assert.Single(deliverable);
        Assert.NotSame(available, duplicateAvailable);
        Assert.Equal(available.OutboxItemId, item.OutboxItemId);
        Assert.Equal("intent-1", item.Intent.IntentId);
    }

    [Fact]
    public async Task InMemoryRuntimePostCommitOutboxStore_RejectsOwnerFilteredQueriesBecauseClaimingIsOutOfScope()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        await store.SavePendingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1"));

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            now: _now,
            limit: 10,
            ownerId: "dispatcher-1")).AsTask());

        Assert.Contains("ownership filtering", exception.Message);
    }

    [Fact]
    public async Task InMemoryRuntimePostCommitOutboxStore_ReturnsDeliverableItemsInDeterministicOrderWithLimit()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var second = NewOutboxItem("outbox-2", "intent-2", "wfexec-1", availableAt: _now.AddSeconds(-3));
        var first = NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-5));
        var third = NewOutboxItem("outbox-3", "intent-3", "wfexec-1", availableAt: _now.AddSeconds(-1));

        await store.SavePendingAsync(third);
        await store.SavePendingAsync(second);
        await store.SavePendingAsync(first);

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 2));

        Assert.Equal(["outbox-1", "outbox-2"], deliverable.Select(item => item.OutboxItemId));
    }

    [Fact]
    public async Task InMemoryRuntimePostCommitOutboxStore_RejectsNonPendingSaveAndConflictingDuplicate()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var pending = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");
        var conflicting = NewOutboxItem("outbox-1", "intent-2", "wfexec-1");
        var conflictingWaitDependency = NewOutboxItem(
            "outbox-1",
            "intent-1",
            "wfexec-1",
            dependsOnWaitRegistrationId: "wait-1",
            failurePolicy: RuntimeWaitDependentIntentFailurePolicy.FaultWorkflow);
        var delivered = NewOutboxItem("outbox-2", "intent-3", "wfexec-1", RuntimePostCommitOutboxStatus.Delivered, deliveredAt: _now);

        await store.SavePendingAsync(pending);

        var duplicateException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SavePendingAsync(conflicting).AsTask());
        var waitDependencyException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SavePendingAsync(conflictingWaitDependency).AsTask());
        var statusException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.SavePendingAsync(delivered).AsTask());

        Assert.Contains("already exists", duplicateException.Message);
        Assert.Contains("already exists", waitDependencyException.Message);
        Assert.Contains("pending", statusException.Message);
    }

    [Fact]
    public async Task InMemoryRuntimePostCommitOutboxStore_RecordDeliveredResultRemovesItemFromDeliverableQuery()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");

        await store.SavePendingAsync(item);
        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "outbox-1",
            status: RuntimePostCommitOutboxStatus.Delivered,
            recordedAt: _now.AddSeconds(1)));

        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddMinutes(1), limit: 10)));
    }

    [Fact]
    public async Task InMemoryRuntimePostCommitOutboxStore_RetryableFailureRespectsRetryDelay()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");

        await store.SavePendingAsync(item);
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
    public async Task InMemoryRuntimePostCommitOutboxStore_RetryableFailureWithoutRemainingAttemptsBecomesTerminal()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var item = NewOutboxItem(
            "outbox-1",
            "intent-1",
            "wfexec-1",
            retryPolicy: RuntimePostCommitRetryPolicy.None);

        await store.SavePendingAsync(item);
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
    public async Task InMemoryRuntimePostCommitOutboxStore_FailedFinalResultIsTerminalAndNotDeliverable()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");

        await store.SavePendingAsync(item);
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
    public async Task InMemoryRuntimePostCommitOutboxStore_RejectsDeliveryResultForMissingOrTerminalItem()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");

        var missingException = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: "missing",
            status: RuntimePostCommitOutboxStatus.Cancelled,
            recordedAt: _now)).AsTask());

        await store.SavePendingAsync(item);
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
        RuntimeWaitDependentIntentFailurePolicy? failurePolicy = null) =>
        new(
            outboxItemId: outboxItemId,
            intent: NewIntent(intentId, workflowExecutionId, dependsOnWaitRegistrationId, failurePolicy),
            status: status,
            recordedAt: _now,
            availableAt: availableAt ?? _now,
            retryPolicy: retryPolicy ?? new RuntimePostCommitRetryPolicy(3, TimeSpan.FromSeconds(10)),
            deliveredAt: deliveredAt);

    private RuntimePostCommitIntent NewIntent(
        string intentId,
        string workflowExecutionId,
        string? dependsOnWaitRegistrationId = null,
        RuntimeWaitDependentIntentFailurePolicy? failurePolicy = null)
    {
        using var document = JsonDocument.Parse("""{"signal":"sent"}""");

        return new RuntimePostCommitIntent(
            intentId: intentId,
            workflowExecutionId: workflowExecutionId,
            kind: "DispatchSignal",
            recordedAt: _now,
            activityExecutionId: "actexec-1",
            idempotencyKey: $"checkpoint-1:{intentId}",
            payload: document.RootElement.Clone(),
            metadata: new Dictionary<string, string>(),
            dependsOnWaitRegistrationId: dependsOnWaitRegistrationId,
            waitFailurePolicy: failurePolicy);
    }
}
