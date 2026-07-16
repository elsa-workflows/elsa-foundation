using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimePostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
{
    private readonly IRuntimePostCommitOutboxStore _outboxStore;
    private readonly IRuntimePostCommitIntentDispatcher _intentDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly IRuntimeFaultCapturePolicy _faultCapturePolicy;
    private readonly IRuntimePostCommitOutboxClaimStore? _claimStore;
    private readonly IRuntimePostCommitOutboxClaimCompletionStore? _claimCompletionStore;
    private readonly IWorkflowDispatchStore? _workflowDispatchStore;
    private readonly string _claimOwnerId = $"runtime-outbox-{Guid.NewGuid():N}";
    private static readonly TimeSpan ClaimVisibilityTimeout = TimeSpan.FromMinutes(1);

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
        : this(outboxStore, intentDispatcher, timeProvider, DefaultRuntimeFaultCapturePolicy.CreateDefault())
    {
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider,
        IRuntimeFaultCapturePolicy faultCapturePolicy)
        : this(outboxStore, intentDispatcher, timeProvider, faultCapturePolicy, workflowDispatchStore: null)
    {
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider,
        IRuntimeFaultCapturePolicy faultCapturePolicy,
        IWorkflowDispatchStore? workflowDispatchStore)
    {
        ArgumentNullException.ThrowIfNull(outboxStore);
        ArgumentNullException.ThrowIfNull(intentDispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(faultCapturePolicy);

        _outboxStore = outboxStore;
        _intentDispatcher = intentDispatcher;
        _timeProvider = timeProvider;
        _faultCapturePolicy = faultCapturePolicy;
        _claimStore = outboxStore as IRuntimePostCommitOutboxClaimStore;
        _claimCompletionStore = outboxStore as IRuntimePostCommitOutboxClaimCompletionStore;
        _workflowDispatchStore = workflowDispatchStore;
    }

    public async ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
        RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var processedItems = new List<RuntimePostCommitOutboxProcessedItem>();

        if (_claimStore is not null)
        {
            var claims = await _claimStore.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                ownerId: _claimOwnerId,
                now: _timeProvider.GetUtcNow(),
                visibilityTimeout: ClaimVisibilityTimeout,
                limit: request.Limit,
                workflowExecutionId: request.WorkflowExecutionId,
                intentKind: request.IntentKind), cancellationToken);
            foreach (var claim in claims)
                processedItems.Add(await ProcessItemAsync(claim.Item, claim, cancellationToken));
        }
        else
        {
            // Compatibility path for third-party v1 stores. Durable compositions advertise the additive claim
            // capability and therefore always take the fenced path above.
            var items = await _outboxStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
                now: _timeProvider.GetUtcNow(),
                limit: request.Limit,
                workflowExecutionId: request.WorkflowExecutionId,
                intentKind: request.IntentKind),
                cancellationToken);
            foreach (var item in items)
                processedItems.Add(await ProcessItemAsync(item, claim: null, cancellationToken));
        }

        return new RuntimePostCommitOutboxProcessResult(processedItems);
    }

    private async ValueTask<RuntimePostCommitOutboxProcessedItem> ProcessItemAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxClaim? claim,
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
        catch (WorkflowDispatchAdmissionProjectionException)
        {
            // The child already exists. Leaving the fenced item Delivering lets claim expiry redeliver the exact
            // deterministic start and repair Started without misclassifying a live child as DispatchFailed.
            throw;
        }
        catch (Exception exception)
        {
            var failureMessage = _faultCapturePolicy.Capture(exception).ToSummaryString();
            var recordingException = await TryRecordDeliveryResultAsync(
                item,
                claim,
                RuntimePostCommitOutboxStatus.FailedRetryable,
                failureMessage,
                cancellationToken);

            if (recordingException is not null)
                throw new OutboxProcessingException(item.OutboxItemId, item.Intent.IntentId, exception, recordingException);

            return new RuntimePostCommitOutboxProcessedItem(
                item.OutboxItemId,
                item.Intent.IntentId,
                RuntimePostCommitOutboxStatus.FailedRetryable,
                failureMessage);
        }

        await RecordDeliveryResultAsync(item, claim, RuntimePostCommitOutboxStatus.Delivered, null, cancellationToken);

        return new RuntimePostCommitOutboxProcessedItem(
            item.OutboxItemId,
            item.Intent.IntentId,
            RuntimePostCommitOutboxStatus.Delivered,
            FailureMessage: null);
    }

    private async ValueTask<Exception?> TryRecordDeliveryResultAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxClaim? claim,
        RuntimePostCommitOutboxStatus status,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await RecordDeliveryResultAsync(item, claim, status, failureMessage, cancellationToken);
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
        RuntimePostCommitOutboxClaim? claim,
        RuntimePostCommitOutboxStatus status,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        var result = new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: item.OutboxItemId,
            status: status,
            recordedAt: _timeProvider.GetUtcNow(),
            failureMessage: failureMessage);
        if (claim is not null)
        {
            var dispatchFailure = await CreateDispatchFailureProjectionAsync(item, result, cancellationToken);
            if (_claimCompletionStore is not null)
            {
                await _claimCompletionStore.CompleteClaimAsync(
                    new RuntimePostCommitOutboxClaimCompletion(claim, result, dispatchFailure),
                    cancellationToken);
            }
            else if (dispatchFailure is not null)
            {
                throw new InvalidOperationException(
                    "A final workflow-dispatch start failure requires atomic outbox/dispatch claim completion support.");
            }
            else
                await _claimStore!.RecordDeliveryResultAsync(claim, result, cancellationToken);
        }
        else
            await _outboxStore.RecordDeliveryResultAsync(result, cancellationToken);
    }

    private async ValueTask<WorkflowDispatchRecord?> CreateDispatchFailureProjectionAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken)
    {
        if (!WillBecomeFinalFailure(item, result) ||
            !item.Intent.Metadata.TryGetValue("runtime.dispatchId", out var dispatchId))
            return null;
        if (_workflowDispatchStore is null)
            throw new InvalidOperationException("A workflow dispatch store is required to project final child-start delivery failure.");

        var record = await _workflowDispatchStore.FindAsync(dispatchId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for final child-start delivery failure.");
        if (record.Status == WorkflowDispatchStatus.DispatchFailed)
            return record;
        return record.TransitionToDispatchFailed(result.RecordedAt);
    }

    private static bool WillBecomeFinalFailure(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxDeliveryResult result) =>
        result.Status == RuntimePostCommitOutboxStatus.FailedFinal ||
        result.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
        item.DeliveryAttemptCount + 1 >= item.RetryPolicy.MaxAttempts;
}
