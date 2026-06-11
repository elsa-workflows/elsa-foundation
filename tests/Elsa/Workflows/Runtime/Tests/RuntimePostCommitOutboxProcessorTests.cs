using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePostCommitOutboxProcessorTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Processor_DispatchesDeliverableItemsAndRecordsDelivered()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var dispatcher = new RecordingDispatcher();
        var processor = NewProcessor(store, dispatcher, _now.AddSeconds(5));

        await store.SavePendingAsync(NewOutboxItem("outbox-2", "intent-2", "wfexec-1", availableAt: _now.AddSeconds(-1)));
        await store.SavePendingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-2)));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Equal(2, result.AttemptedCount);
        Assert.Equal(2, result.DeliveredCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(["intent-1", "intent-2"], dispatcher.Intents.Select(intent => intent.IntentId));
        Assert.Equal([RuntimePostCommitOutboxStatus.Delivered, RuntimePostCommitOutboxStatus.Delivered], result.Items.Select(item => item.RequestedDeliveryResultStatus));
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddMinutes(1), limit: 10)));
    }

    [Fact]
    public async Task Processor_RecordsRetryableFailureWhenDispatchFails()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: new InvalidOperationException("Dispatch failed."));
        var processor = NewProcessor(store, dispatcher, _now);

        await store.SavePendingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1"));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, item.RequestedDeliveryResultStatus);
        Assert.Equal("Dispatch failed.", item.FailureMessage);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(5), limit: 10)));

        var retryable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(11), limit: 10));
        var retryableItem = Assert.Single(retryable);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, retryableItem.Status);
        Assert.Equal(1, retryableItem.DeliveryAttemptCount);
        Assert.Equal("Dispatch failed.", retryableItem.LastFailureMessage);
    }

    [Fact]
    public async Task Processor_ResultReportsRequestedFailedStatusWhenStoreNormalizesToFinal()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: new InvalidOperationException("Dispatch failed."));
        var processor = NewProcessor(store, dispatcher, _now);

        await store.SavePendingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", retryPolicy: new RuntimePostCommitRetryPolicy(1, TimeSpan.FromSeconds(10))));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        var processed = Assert.Single(result.Items);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, processed.RequestedDeliveryResultStatus);
        Assert.Equal(1, result.FailedCount);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(11), limit: 10)));
    }

    [Fact]
    public async Task Processor_UsesWorkflowExecutionFilterAndLimit()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var dispatcher = new RecordingDispatcher();
        var processor = NewProcessor(store, dispatcher, _now);

        await store.SavePendingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-3)));
        await store.SavePendingAsync(NewOutboxItem("outbox-2", "intent-2", "wfexec-1", availableAt: _now.AddSeconds(-2)));
        await store.SavePendingAsync(NewOutboxItem("outbox-3", "intent-3", "wfexec-2", availableAt: _now.AddSeconds(-1)));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 1, workflowExecutionId: "wfexec-1"));

        var processed = Assert.Single(result.Items);
        Assert.Equal("outbox-1", processed.OutboxItemId);
        Assert.Equal(["intent-1"], dispatcher.Intents.Select(intent => intent.IntentId));

        var remainingWorkflowItems = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 10, workflowExecutionId: "wfexec-1"));
        Assert.Equal(["outbox-2"], remainingWorkflowItems.Select(item => item.OutboxItemId));
    }

    [Fact]
    public async Task Processor_ReturnsEmptyResultWhenNoItemsAreDeliverable()
    {
        var store = new InMemoryRuntimePostCommitOutboxStore();
        var dispatcher = new RecordingDispatcher();
        var processor = NewProcessor(store, dispatcher, _now);

        await store.SavePendingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(1)));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.AttemptedCount);
        Assert.Empty(dispatcher.Intents);

        var futureItems = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(1), limit: 10));
        Assert.Equal(["outbox-1"], futureItems.Select(item => item.OutboxItemId));
    }

    [Fact]
    public async Task Processor_PreservesDispatchFailureWhenFailedResultRecordingFails()
    {
        var dispatchFailure = new InvalidOperationException("Dispatch failed.");
        var resultRecordingFailure = new InvalidOperationException("Result recording failed.");
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");
        var store = new ThrowingResultStore(item, resultRecordingFailure);
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: dispatchFailure);
        var processor = NewProcessor(store, dispatcher, _now);

        var exception = await Assert.ThrowsAsync<RuntimePostCommitOutboxProcessingException>(async () =>
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10)));

        Assert.Equal("outbox-1", exception.OutboxItemId);
        Assert.Equal("intent-1", exception.IntentId);
        Assert.Same(dispatchFailure, exception.InnerException);
        Assert.Same(resultRecordingFailure, exception.DeliveryResultRecordingException);
    }

    [Fact]
    public async Task Processor_PropagatesCancellationWhenFailedResultRecordingIsCanceled()
    {
        var dispatchFailure = new InvalidOperationException("Dispatch failed.");
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");
        var store = new ThrowingResultStore(item, new OperationCanceledException());
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: dispatchFailure);
        var processor = NewProcessor(store, dispatcher, _now);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10)));
    }

    [Fact]
    public async Task Processor_SurfacesDeliveredResultRecordingFailureAfterSuccessfulDispatch()
    {
        var resultRecordingFailure = new InvalidOperationException("Result recording failed.");
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");
        var store = new ThrowingResultStore(item, resultRecordingFailure);
        var dispatcher = new RecordingDispatcher();
        var processor = NewProcessor(store, dispatcher, _now);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10)));

        Assert.Same(resultRecordingFailure, exception);
        Assert.Equal(["intent-1"], dispatcher.Intents.Select(intent => intent.IntentId));
    }

    [Fact]
    public void ProcessRequest_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimePostCommitOutboxProcessRequest(limit: 0));
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitOutboxProcessRequest(limit: 10, workflowExecutionId: " "));
    }

    private static RuntimePostCommitOutboxProcessor NewProcessor(
        IRuntimePostCommitOutboxStore store,
        RecordingDispatcher dispatcher,
        DateTimeOffset now) =>
        new(store, dispatcher, new FixedTimeProvider(now));

    private RuntimePostCommitOutboxItem NewOutboxItem(
        string outboxItemId,
        string intentId,
        string workflowExecutionId,
        DateTimeOffset? availableAt = null,
        RuntimePostCommitRetryPolicy? retryPolicy = null) =>
        new(
            outboxItemId: outboxItemId,
            intent: NewIntent(intentId, workflowExecutionId),
            status: RuntimePostCommitOutboxStatus.Pending,
            recordedAt: _now,
            availableAt: availableAt ?? _now,
            retryPolicy: retryPolicy ?? new RuntimePostCommitRetryPolicy(3, TimeSpan.FromSeconds(10)));

    private RuntimePostCommitIntent NewIntent(string intentId, string workflowExecutionId)
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
            metadata: new Dictionary<string, string>());
    }

    private sealed class RecordingDispatcher(
        string? failOnIntentId = null,
        Exception? failure = null) : IRuntimePostCommitIntentDispatcher
    {
        public List<RuntimePostCommitIntent> Intents { get; } = [];

        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            if (intent.IntentId == failOnIntentId)
                throw failure ?? new InvalidOperationException($"Intent {intent.IntentId} failed.");

            Intents.Add(intent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingResultStore(
        RuntimePostCommitOutboxItem item,
        Exception exception) : IRuntimePostCommitOutboxStore
    {
        public ValueTask SavePendingAsync(RuntimePostCommitOutboxItem item, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxItem>>([item]);

        public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
