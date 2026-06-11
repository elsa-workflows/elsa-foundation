using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimePostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
{
    private readonly IRuntimePostCommitOutboxStore _outboxStore;
    private readonly IRuntimePostCommitIntentDispatcher _intentDispatcher;
    private readonly TimeProvider _timeProvider;

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher)
        : this(outboxStore, intentDispatcher, TimeProvider.System)
    {
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(outboxStore);
        ArgumentNullException.ThrowIfNull(intentDispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _outboxStore = outboxStore;
        _intentDispatcher = intentDispatcher;
        _timeProvider = timeProvider;
    }

    public async ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
        RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var items = await _outboxStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            now: _timeProvider.GetUtcNow(),
            limit: request.Limit,
            workflowExecutionId: request.WorkflowExecutionId),
            cancellationToken);
        var processedItems = new List<RuntimePostCommitOutboxProcessedItem>();

        foreach (var item in items)
        {
            processedItems.Add(await ProcessItemAsync(item, cancellationToken));
        }

        return new RuntimePostCommitOutboxProcessResult(processedItems);
    }

    private async ValueTask<RuntimePostCommitOutboxProcessedItem> ProcessItemAsync(
        RuntimePostCommitOutboxItem item,
        CancellationToken cancellationToken)
    {
        try
        {
            await _intentDispatcher.DispatchAsync(item.Intent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failureMessage = RuntimeFailureMessages.For(exception);
            var recordingException = await TryRecordDeliveryResultAsync(
                item,
                RuntimePostCommitOutboxStatus.FailedRetryable,
                failureMessage,
                cancellationToken);

            if (recordingException is not null)
                throw new RuntimePostCommitOutboxProcessingException(item.OutboxItemId, item.Intent.IntentId, exception, recordingException);

            return new RuntimePostCommitOutboxProcessedItem(
                item.OutboxItemId,
                item.Intent.IntentId,
                RuntimePostCommitOutboxStatus.FailedRetryable,
                failureMessage);
        }

        await RecordDeliveryResultAsync(item, RuntimePostCommitOutboxStatus.Delivered, null, cancellationToken);

        return new RuntimePostCommitOutboxProcessedItem(
            item.OutboxItemId,
            item.Intent.IntentId,
            RuntimePostCommitOutboxStatus.Delivered,
            FailureMessage: null);
    }

    private async ValueTask<Exception?> TryRecordDeliveryResultAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxStatus status,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await RecordDeliveryResultAsync(item, status, failureMessage, cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async ValueTask RecordDeliveryResultAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxStatus status,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        await _outboxStore.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: item.OutboxItemId,
            status: status,
            recordedAt: _timeProvider.GetUtcNow(),
            failureMessage: failureMessage),
            cancellationToken);
    }
}
