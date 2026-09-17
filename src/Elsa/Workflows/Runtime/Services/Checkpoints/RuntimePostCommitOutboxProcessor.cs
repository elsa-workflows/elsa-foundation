using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

public sealed class RuntimePostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
{
    private const string GenericFailureCode = "runtime-post-commit-delivery-failed";
    private const string RetryUntilAcknowledgedFailureMessage =
        "Runtime post-commit intent delivery deferred pending acknowledgement.";

    private readonly IRuntimePostCommitOutboxStore _outboxStore;
    private readonly IRuntimePostCommitIntentDispatcher _intentDispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly IRuntimeFaultCapturePolicy _faultCapturePolicy;
    private readonly IRuntimePostCommitOutboxClaimStore? _claimStore;
    private readonly IRuntimePostCommitOutboxClaimCompletionStore? _claimCompletionStore;
    private readonly IWorkflowDispatchStore? _workflowDispatchStore;
    private readonly IPostCommitFailureProjector? _deliveryFailureProjector;
    private readonly IRuntimeLiveDrainDeliveryAccessor? _liveDrainDeliveryAccessor;
    private readonly IRuntimeCoalescingSessionAccessor? _coalescingSessionAccessor;
    private readonly ILogger<RuntimePostCommitOutboxProcessor> _logger;
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
        : this(outboxStore, intentDispatcher, timeProvider, faultCapturePolicy, workflowDispatchStore, logger: null)
    {
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider,
        IRuntimeFaultCapturePolicy faultCapturePolicy,
        IWorkflowDispatchStore? workflowDispatchStore,
        ILogger<RuntimePostCommitOutboxProcessor>? logger,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor = null)
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
        _liveDrainDeliveryAccessor = liveDrainDeliveryAccessor;
        _coalescingSessionAccessor = coalescingSessionAccessor;
        _logger = logger ?? NullLogger<RuntimePostCommitOutboxProcessor>.Instance;
    }

    public RuntimePostCommitOutboxProcessor(
        IRuntimePostCommitOutboxStore outboxStore,
        IRuntimePostCommitIntentDispatcher intentDispatcher,
        TimeProvider timeProvider,
        IRuntimeFaultCapturePolicy faultCapturePolicy,
        IWorkflowDispatchStore? workflowDispatchStore,
        IEnumerable<IPostCommitFailureProjector> deliveryFailureProjectors,
        ILogger<RuntimePostCommitOutboxProcessor>? logger,
        IRuntimeLiveDrainDeliveryAccessor? liveDrainDeliveryAccessor = null,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor = null)
        : this(outboxStore, intentDispatcher, timeProvider, faultCapturePolicy, workflowDispatchStore, logger, liveDrainDeliveryAccessor, coalescingSessionAccessor)
    {
        ArgumentNullException.ThrowIfNull(deliveryFailureProjectors);
        var projectors = deliveryFailureProjectors.ToArray();
        if (projectors.Length > 1)
        {
            throw new InvalidOperationException(
                "Exactly one owning feature may register the post-commit failure projector replacement contract.");
        }

        _deliveryFailureProjector = projectors.SingleOrDefault();
    }

    public async ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
        RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var processedItems = new List<RuntimePostCommitOutboxProcessedItem>();

        if (_claimStore is not null && !DeliversInMemory(request))
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
            // In-memory live-drain delivery and the compatibility path for third-party v1 stores share the same
            // claim-free mechanics: read the deliverable items, enqueue the continuation through the queue's idempotent
            // EnqueueAsync, and mark the durable outbox item Delivered directly (Pending -> Delivered) via the existing
            // recording contract. DeliversInMemory forces this branch even when the store advertises the claim capability
            // so a live drain skips the durable claim round-trip while the durable outbox item stays a crash backstop.
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

    // The live-drain fast path engages only when: a live drain owns this exact execution's delivery, the request
    // targets EnqueueSchedulerWork intents (every other kind stays on the durable claim path, condition (e)), and no
    // coalescing session is active (the coalescing overlay is authoritative, condition (c) — the drain orchestrator
    // never pushes a live-drain scope on the coalescing path, and this guard defends against direct callers).
    private bool DeliversInMemory(RuntimePostCommitOutboxProcessRequest request) =>
        _liveDrainDeliveryAccessor?.Current is { } scope &&
        scope.AppliesTo(request.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(request.IntentKind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork) &&
        _coalescingSessionAccessor?.Current is not { IsActive: true };

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
            var classification = exception as RuntimePostCommitDeliveryException;
            var requestedStatus = classification?.Kind == PostCommitFailureKind.Permanent
                ? RuntimePostCommitOutboxStatus.FailedFinal
                : RuntimePostCommitOutboxStatus.FailedRetryable;
            var effectiveStatus = EffectiveFailureStatus(item, requestedStatus);
            var failureMessage = classification?.SafeSummary ?? (item.RetryPolicy.RetryUntilAcknowledged
                ? RetryUntilAcknowledgedFailureMessage
                : _faultCapturePolicy.Capture(exception).ToSummaryString());
            var recordedAt = _timeProvider.GetUtcNow();
            var (outcome, recordingException) = await TryRecordDeliveryResultAsync(
                item,
                claim,
                effectiveStatus,
                failureMessage,
                recordedAt,
                exception,
                cancellationToken);

            if (recordingException is not null)
                throw new OutboxProcessingException(item.OutboxItemId, item.Intent.IntentId, exception, recordingException);

            var persistedStatus = outcome == RuntimePostCommitOutboxClaimCompletionOutcome.DeliveredOnChildEvidence
                ? RuntimePostCommitOutboxStatus.Delivered
                : effectiveStatus;
            LogDeliveryFailure(item, exception, classification, persistedStatus, recordedAt);

            return new RuntimePostCommitOutboxProcessedItem(
                item.OutboxItemId,
                item.Intent.IntentId,
                effectiveStatus,
                failureMessage);
        }

        await RecordDeliveryResultAsync(item, claim, RuntimePostCommitOutboxStatus.Delivered, null, recordedAt: null, deliveryFailure: null, cancellationToken);
        LogDelivered(item);

        return new RuntimePostCommitOutboxProcessedItem(
            item.OutboxItemId,
            item.Intent.IntentId,
            RuntimePostCommitOutboxStatus.Delivered,
            FailureMessage: null);
    }

    private async ValueTask<(RuntimePostCommitOutboxClaimCompletionOutcome Outcome, Exception? Failure)> TryRecordDeliveryResultAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxClaim? claim,
        RuntimePostCommitOutboxStatus status,
        string? failureMessage,
        DateTimeOffset recordedAt,
        Exception deliveryFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await RecordDeliveryResultAsync(item, claim, status, failureMessage, recordedAt, deliveryFailure, cancellationToken), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return (RuntimePostCommitOutboxClaimCompletionOutcome.Persisted, exception);
        }
    }

    private async ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxClaim? claim,
        RuntimePostCommitOutboxStatus status,
        string? failureMessage,
        DateTimeOffset? recordedAt,
        Exception? deliveryFailure,
        CancellationToken cancellationToken)
    {
        var result = new RuntimePostCommitOutboxDeliveryResult(
            outboxItemId: item.OutboxItemId,
            status: status,
            recordedAt: recordedAt ?? _timeProvider.GetUtcNow(),
            failureMessage: failureMessage);
        if (claim is not null)
        {
            var dispatchFailure = await CreateDeliveryFailureProjectionAsync(item, result, cancellationToken);
            if (_claimCompletionStore is not null)
            {
                var outcome = await _claimCompletionStore.CompleteClaimAsync(
                    new RuntimePostCommitOutboxClaimCompletion(
                        claim,
                        result,
                        dispatchFailure?.WorkflowDispatch,
                        dispatchFailure?.FollowUpOutboxItem),
                    cancellationToken);
                // A store that found the child already started persisted the start as delivered and discarded the
                // projection, so there is no delivery incident or failure resume to report.
                if (dispatchFailure is not null && outcome == RuntimePostCommitOutboxClaimCompletionOutcome.Persisted)
                    LogDeliveryFailureProjection(item, dispatchFailure, result.RecordedAt, deliveryFailure);
                return outcome;
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

        return RuntimePostCommitOutboxClaimCompletionOutcome.Persisted;
    }

    private async ValueTask<PostCommitFailureProjection?> CreateDeliveryFailureProjectionAsync(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxDeliveryResult result,
        CancellationToken cancellationToken)
    {
        if (!WillBecomeFinalFailure(item, result))
            return null;
        if (_deliveryFailureProjector is not null)
            return await _deliveryFailureProjector.ProjectAsync(item, result, cancellationToken);
        if (!StringComparer.Ordinal.Equals(item.Intent.Kind, WorkflowDispatchLifecycle.StartChildIntentKind) ||
            !item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId))
            return null;
        if (_workflowDispatchStore is null)
            throw new InvalidOperationException("A workflow dispatch store is required to project final child-start delivery failure.");

        var record = await _workflowDispatchStore.FindAsync(dispatchId, cancellationToken)
            ?? throw new InvalidOperationException($"Workflow dispatch '{dispatchId}' was not found for final child-start delivery failure.");
        var identity = new WorkflowDispatchIdentity(
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(record.DispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(item.Intent.IntentId, identity.StartIntentId) ||
            !StringComparer.Ordinal.Equals(item.Intent.IdempotencyKey, identity.StartIdempotencyKey) ||
            !StringComparer.Ordinal.Equals(item.Intent.WorkflowExecutionId, record.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(item.Intent.ActivityExecutionId, record.ParentActivityExecutionId))
        {
            throw new InvalidOperationException(
                $"Child-start intent '{item.Intent.IntentId}' conflicts with workflow dispatch '{record.DispatchId}'.");
        }
        if (record.Status == WorkflowDispatchStatus.DispatchFailed)
            return new PostCommitFailureProjection(record);
        var generation = WorkflowDispatchLifecycle.ReadDeliveryGeneration(record);
        var attemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(item.DeliveryAttemptCount);
        var firstAttemptAt = RuntimePostCommitOutboxClaimTransitions.ReadFirstDeliveryAttemptedAt(item) ??
            item.DeliveryStartedAt ??
            result.RecordedAt;
        var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(
            record,
            item.OutboxItemId,
            generation,
            attemptCount,
            firstAttemptAt,
            result.RecordedAt);
        return new PostCommitFailureProjection(failed);
    }

    private static bool WillBecomeFinalFailure(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxDeliveryResult result) =>
        result.Status == RuntimePostCommitOutboxStatus.FailedFinal ||
        result.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
        item.RetryPolicy.IsExhaustedAfterAttempt(
            RuntimePostCommitRetryPolicy.SaturatingIncrement(item.DeliveryAttemptCount));

    private static RuntimePostCommitOutboxStatus EffectiveFailureStatus(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxStatus requestedStatus)
    {
        if (requestedStatus == RuntimePostCommitOutboxStatus.FailedFinal)
            return requestedStatus;

        var attemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(item.DeliveryAttemptCount);
        return item.RetryPolicy.IsExhaustedAfterAttempt(attemptCount)
            ? RuntimePostCommitOutboxStatus.FailedFinal
            : RuntimePostCommitOutboxStatus.FailedRetryable;
    }

    /// <remarks>
    /// Every failure event carries the delivery exception, so the cause and its stack trace reach the log; the structured
    /// fields stay identifiers and classifications. An expected deferral (<see cref="IRuntimePostCommitDeferral"/>) is a
    /// routine wait rather than a failure and is logged without its exception. The persisted <c>LastFailureMessage</c> and every durable dispatch
    /// and incident projection stay free of exception text. <paramref name="effectiveStatus"/> is the status the store
    /// persisted, which is <see cref="RuntimePostCommitOutboxStatus.Delivered"/> when it found the child of a failed start
    /// already started: the attempt still failed, but nothing became final and no retry was scheduled.
    /// </remarks>
    private void LogDeliveryFailure(
        RuntimePostCommitOutboxItem item,
        Exception exception,
        RuntimePostCommitDeliveryException? classification,
        RuntimePostCommitOutboxStatus effectiveStatus,
        DateTimeOffset recordedAt)
    {
        // Under retry-until-acknowledged a retryable failure is deferred, not scheduled. A permanent failure still ends
        // delivery, so it is logged as final like any other.
        if (item.RetryPolicy.RetryUntilAcknowledged && effectiveStatus == RuntimePostCommitOutboxStatus.FailedRetryable)
        {
            LogRetryUntilAcknowledged(item, exception, recordedAt);
            return;
        }

        item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId);
        var attemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(item.DeliveryAttemptCount);
        var failureCode = classification?.Code ?? GenericFailureCode;
        var failureKind = classification?.Kind.ToString() ?? PostCommitFailureKind.Transient.ToString();
        _logger.LogWarning(
            new EventId(68101, "RuntimePostCommitDeliveryAttemptFailed"),
            exception,
            "Runtime post-commit delivery attempt failed. OutboxItemId={OutboxItemId} IntentId={IntentId} IntentKind={IntentKind} DispatchId={DispatchId} DeliveryAttemptCount={DeliveryAttemptCount} FailureCode={FailureCode} FailureKind={FailureKind} EffectiveStatus={EffectiveStatus} RecordedAt={RecordedAt}",
            item.OutboxItemId,
            item.Intent.IntentId,
            item.Intent.Kind,
            dispatchId,
            attemptCount,
            failureCode,
            failureKind,
            effectiveStatus,
            recordedAt);

        if (effectiveStatus == RuntimePostCommitOutboxStatus.FailedFinal)
        {
            _logger.LogWarning(
                new EventId(68103, "RuntimePostCommitDeliveryFailedFinal"),
                exception,
                "Runtime post-commit delivery became final. OutboxItemId={OutboxItemId} IntentId={IntentId} IntentKind={IntentKind} DispatchId={DispatchId} DeliveryAttemptCount={DeliveryAttemptCount} FailureCode={FailureCode} FailureKind={FailureKind} EffectiveStatus={EffectiveStatus} RecordedAt={RecordedAt}",
                item.OutboxItemId,
                item.Intent.IntentId,
                item.Intent.Kind,
                dispatchId,
                attemptCount,
                failureCode,
                failureKind,
                effectiveStatus,
                recordedAt);
            return;
        }

        if (effectiveStatus != RuntimePostCommitOutboxStatus.FailedRetryable)
            return;

        var nextAvailableAt = recordedAt.Add(item.RetryPolicy.Delay!.Value);
        _logger.LogWarning(
            new EventId(68102, "RuntimePostCommitRetryScheduled"),
            exception,
            "Runtime post-commit delivery retry scheduled. OutboxItemId={OutboxItemId} IntentId={IntentId} IntentKind={IntentKind} DispatchId={DispatchId} DeliveryAttemptCount={DeliveryAttemptCount} FailureCode={FailureCode} FailureKind={FailureKind} EffectiveStatus={EffectiveStatus} NextAvailableAt={NextAvailableAt}",
            item.OutboxItemId,
            item.Intent.IntentId,
            item.Intent.Kind,
            dispatchId,
            attemptCount,
            failureCode,
            failureKind,
            effectiveStatus,
            nextAvailableAt);
    }

    private void LogDelivered(RuntimePostCommitOutboxItem item)
    {
        item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId);
        _logger.LogInformation(
            new EventId(68104, "RuntimePostCommitDeliverySucceeded"),
            "Runtime post-commit delivery succeeded. OutboxItemId={OutboxItemId} IntentId={IntentId} IntentKind={IntentKind} DispatchId={DispatchId} DeliveryAttemptCount={DeliveryAttemptCount} EffectiveStatus={EffectiveStatus}",
            item.OutboxItemId,
            item.Intent.IntentId,
            item.Intent.Kind,
            dispatchId,
            RuntimePostCommitRetryPolicy.SaturatingIncrement(item.DeliveryAttemptCount),
            RuntimePostCommitOutboxStatus.Delivered);
    }

    private void LogDeliveryFailureProjection(
        RuntimePostCommitOutboxItem item,
        PostCommitFailureProjection projection,
        DateTimeOffset recordedAt,
        Exception? deliveryFailure)
    {
        var dispatch = projection.WorkflowDispatch;
        _logger.LogWarning(
            new EventId(68105, "WorkflowDispatchDeliveryIncidentRecorded"),
            deliveryFailure,
            "Workflow dispatch delivery incident recorded. OutboxItemId={OutboxItemId} IntentId={IntentId} IntentKind={IntentKind} DispatchId={DispatchId} DeliveryGeneration={DeliveryGeneration} DeliveryAttemptCount={DeliveryAttemptCount} DeliveryIncidentId={DeliveryIncidentId} DeliveryDeadLetterId={DeliveryDeadLetterId} EffectiveStatus={EffectiveStatus} RecordedAt={RecordedAt}",
            item.OutboxItemId,
            item.Intent.IntentId,
            item.Intent.Kind,
            dispatch.DispatchId,
            WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch),
            WorkflowDispatchLifecycle.ReadDeliveryAttemptCount(dispatch),
            WorkflowDispatchLifecycle.ReadDeliveryIncidentId(dispatch),
            WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(dispatch),
            RuntimePostCommitOutboxStatus.FailedFinal,
            recordedAt);

        if (projection.FollowUpOutboxItem is { } followUp)
        {
            _logger.LogInformation(
                new EventId(68106, "WorkflowDispatchFailureResumeQueued"),
                "Workflow dispatch failure resume queued. OutboxItemId={OutboxItemId} IntentId={IntentId} IntentKind={IntentKind} DispatchId={DispatchId} DeliveryGeneration={DeliveryGeneration} DeliveryIncidentId={DeliveryIncidentId} EffectiveStatus={EffectiveStatus} RecordedAt={RecordedAt}",
                followUp.OutboxItemId,
                followUp.Intent.IntentId,
                followUp.Intent.Kind,
                dispatch.DispatchId,
                WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch),
                WorkflowDispatchLifecycle.ReadDeliveryIncidentId(dispatch),
                followUp.Status,
                recordedAt);
        }
    }

    private void LogRetryUntilAcknowledged(RuntimePostCommitOutboxItem item, Exception exception, DateTimeOffset recordedAt)
    {
        if (!item.RetryPolicy.RetryUntilAcknowledged)
            return;

        item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId);
        var attemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(item.DeliveryAttemptCount);
        var nextAvailableAt = recordedAt.Add(item.RetryPolicy.Delay!.Value);
        _logger.LogWarning(
            new EventId(67901, "RuntimePostCommitRetryDeferred"),
            exception is IRuntimePostCommitDeferral ? null : exception,
            "Runtime post-commit intent retry deferred. OutboxItemId={OutboxItemId} IntentId={IntentId} IntentKind={IntentKind} DispatchId={DispatchId} DeliveryAttemptCount={DeliveryAttemptCount} NextAvailableAt={NextAvailableAt}",
            item.OutboxItemId,
            item.Intent.IntentId,
            item.Intent.Kind,
            dispatchId,
            attemptCount,
            nextAvailableAt);
    }
}
