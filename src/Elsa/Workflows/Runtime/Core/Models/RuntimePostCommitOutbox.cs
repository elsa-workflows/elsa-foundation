using System.Text.Json.Serialization;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Creates deterministic post-commit identities within the provider-portable projection boundary,
/// preserving the readable legacy form whenever it already fits.
/// </summary>
public static class RuntimePostCommitOutboxIdentity
{
    public const int MaximumLength = 450;
    private const string DigestPrefix = "outbox:sha256:v1:";

    public static string Create(string commitId, string intentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);

        var candidate = $"{commitId}:{intentId}";
        if (candidate.Length <= MaximumLength &&
            !candidate.StartsWith(DigestPrefix, StringComparison.Ordinal))
            return candidate;

        candidate = $"{DigestPrefix}{Digest(commitId)}:{intentId}";
        return candidate.Length <= MaximumLength
            ? candidate
            : $"{DigestPrefix}{Digest($"{commitId}:{intentId}")}";
    }

    private static string Digest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

/// <summary>
/// Durable delivery state for a post-commit intent. Intent payloads are delivered only after checkpoint commit.
/// </summary>
public sealed class RuntimePostCommitOutboxItem
{
    public RuntimePostCommitOutboxItem(
        string outboxItemId,
        RuntimePostCommitIntent intent,
        RuntimePostCommitOutboxStatus status,
        DateTimeOffset recordedAt,
        DateTimeOffset? availableAt,
        RuntimePostCommitRetryPolicy? retryPolicy = null,
        int deliveryAttemptCount = 0,
        string? deliveringOwnerId = null,
        DateTimeOffset? deliveryStartedAt = null,
        DateTimeOffset? deliveredAt = null,
        string? lastFailureMessage = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        long deliveryFencingToken = 0,
        DateTimeOffset? deliveryVisibleAfter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);
        ArgumentNullException.ThrowIfNull(intent);

        if (deliveryAttemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(deliveryAttemptCount), "Delivery attempt count cannot be negative.");
        if (deliveryFencingToken < 0)
            throw new ArgumentOutOfRangeException(nameof(deliveryFencingToken), "Delivery fencing token cannot be negative.");

        Validate(status, deliveringOwnerId, deliveryStartedAt, deliveredAt, deliveryFencingToken, deliveryVisibleAfter);

        OutboxItemId = outboxItemId;
        Intent = intent;
        Status = status;
        RecordedAt = recordedAt;
        AvailableAt = availableAt;
        RetryPolicy = retryPolicy ?? RuntimePostCommitRetryPolicy.None;
        DeliveryAttemptCount = deliveryAttemptCount;
        DeliveringOwnerId = deliveringOwnerId;
        DeliveryStartedAt = deliveryStartedAt;
        DeliveredAt = deliveredAt;
        LastFailureMessage = lastFailureMessage;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        DeliveryFencingToken = deliveryFencingToken;
        DeliveryVisibleAfter = deliveryVisibleAfter;
    }

    public string OutboxItemId { get; }
    public RuntimePostCommitIntent Intent { get; }
    public RuntimePostCommitOutboxStatus Status { get; }
    public DateTimeOffset RecordedAt { get; }
    public DateTimeOffset? AvailableAt { get; }
    public RuntimePostCommitRetryPolicy RetryPolicy { get; }
    public int DeliveryAttemptCount { get; }
    public string? DeliveringOwnerId { get; }
    public DateTimeOffset? DeliveryStartedAt { get; }
    public DateTimeOffset? DeliveredAt { get; }
    public string? LastFailureMessage { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public long DeliveryFencingToken { get; }
    public DateTimeOffset? DeliveryVisibleAfter { get; }

    public bool IsTerminal => Status is RuntimePostCommitOutboxStatus.Delivered
        or RuntimePostCommitOutboxStatus.FailedFinal
        or RuntimePostCommitOutboxStatus.Cancelled;

    private static void Validate(
        RuntimePostCommitOutboxStatus status,
        string? deliveringOwnerId,
        DateTimeOffset? deliveryStartedAt,
        DateTimeOffset? deliveredAt,
        long deliveryFencingToken,
        DateTimeOffset? deliveryVisibleAfter)
    {
        if (status == RuntimePostCommitOutboxStatus.Delivering)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(deliveringOwnerId);

            if (deliveryStartedAt is null)
                throw new ArgumentException("A delivering outbox item requires a delivery start time.", nameof(deliveryStartedAt));

            // Token zero with no visibility is accepted only as the legacy v1 Delivering representation. Every v2
            // claim created through the claim contract carries both a positive token and visibility deadline.
            if ((deliveryFencingToken > 0) != (deliveryVisibleAfter is not null))
                throw new ArgumentException("A claimed outbox item requires both a positive fencing token and visibility deadline.", nameof(deliveryFencingToken));
            if (deliveryVisibleAfter is { } visibleAfter && visibleAfter <= deliveryStartedAt)
                throw new ArgumentOutOfRangeException(nameof(deliveryVisibleAfter), "Delivery visibility must follow the claim time.");
        }
        else
        {
            if (deliveringOwnerId is not null)
                throw new ArgumentException("Only delivering outbox items can carry active delivery ownership.", nameof(deliveringOwnerId));

            if (deliveryStartedAt is not null)
                throw new ArgumentException("Only delivering outbox items can carry a delivery start time.", nameof(deliveryStartedAt));

            if (deliveryVisibleAfter is not null)
                throw new ArgumentException("Only delivering outbox items can carry a delivery visibility deadline.", nameof(deliveryVisibleAfter));
        }

        if (status == RuntimePostCommitOutboxStatus.Delivered && deliveredAt is null)
            throw new ArgumentException("A delivered outbox item requires a delivered time.", nameof(deliveredAt));

        if (status != RuntimePostCommitOutboxStatus.Delivered && deliveredAt is not null)
            throw new ArgumentException("Only delivered outbox items can carry a delivered time.", nameof(deliveredAt));
    }
}

public sealed class RuntimePostCommitOutboxClaimRequest
{
    public RuntimePostCommitOutboxClaimRequest(
        string ownerId,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        int limit,
        string? workflowExecutionId = null,
        string? intentKind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "Outbox claim visibility timeout must be greater than zero.");
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Outbox claim limit must be greater than zero.");
        ValidateOptional(workflowExecutionId, nameof(workflowExecutionId));
        if (intentKind is not null)
            RuntimePostCommitIntent.ValidateKind(intentKind, nameof(intentKind));

        OwnerId = ownerId;
        Now = now;
        VisibilityTimeout = visibilityTimeout;
        Limit = limit;
        WorkflowExecutionId = workflowExecutionId;
        IntentKind = intentKind;
    }

    public string OwnerId { get; }
    public DateTimeOffset Now { get; }
    public TimeSpan VisibilityTimeout { get; }
    public int Limit { get; }
    public string? WorkflowExecutionId { get; }
    public string? IntentKind { get; }

    private static void ValidateOptional(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Outbox claim filters cannot be blank.", parameterName);
    }
}

public sealed class RuntimePostCommitOutboxClaim
{
    public RuntimePostCommitOutboxClaim(
        RuntimePostCommitOutboxItem item,
        string ownerId,
        long fencingToken,
        DateTimeOffset claimedAt,
        DateTimeOffset visibleAfter)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (fencingToken <= 0)
            throw new ArgumentOutOfRangeException(nameof(fencingToken), "Outbox claim fencing token must be positive.");
        if (visibleAfter <= claimedAt)
            throw new ArgumentOutOfRangeException(nameof(visibleAfter), "Outbox claim visibility must follow the claim time.");

        Item = item;
        OwnerId = ownerId;
        FencingToken = fencingToken;
        ClaimedAt = claimedAt;
        VisibleAfter = visibleAfter;
    }

    public string OutboxItemId => Item.OutboxItemId;
    public RuntimePostCommitOutboxItem Item { get; }
    public string OwnerId { get; }
    public long FencingToken { get; }
    public DateTimeOffset ClaimedAt { get; }
    public DateTimeOffset VisibleAfter { get; }
}

/// <summary>One fenced outbox completion plus its optional atomic dispatch lifecycle projection.</summary>
public sealed class RuntimePostCommitOutboxClaimCompletion
{
    public RuntimePostCommitOutboxClaimCompletion(
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult deliveryResult,
        WorkflowDispatchRecord? workflowDispatch = null,
        RuntimePostCommitOutboxItem? followUpOutboxItem = null)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(deliveryResult);
        if (!StringComparer.Ordinal.Equals(claim.OutboxItemId, deliveryResult.OutboxItemId))
            throw new ArgumentException("The outbox delivery result must identify the claimed item.", nameof(deliveryResult));
        if (workflowDispatch is not null)
        {
            var becomesFinal = deliveryResult.Status == RuntimePostCommitOutboxStatus.FailedFinal ||
                deliveryResult.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
                claim.Item.RetryPolicy.IsExhaustedAfterAttempt(
                    RuntimePostCommitRetryPolicy.SaturatingIncrement(claim.Item.DeliveryAttemptCount));
            if (!becomesFinal ||
                workflowDispatch.Status != WorkflowDispatchStatus.DispatchFailed)
            {
                throw new ArgumentException("An outbox claim failure projection requires a failed delivery result and DispatchFailed status.", nameof(workflowDispatch));
            }

            var identity = new WorkflowDispatchIdentity(
                workflowDispatch.ParentWorkflowExecutionId,
                workflowDispatch.ParentActivityExecutionId);
            var authorized = StringComparer.Ordinal.Equals(claim.Item.Intent.Kind, WorkflowDispatchLifecycle.StartChildIntentKind) &&
                StringComparer.Ordinal.Equals(claim.Item.Intent.WorkflowExecutionId, workflowDispatch.ParentWorkflowExecutionId) &&
                StringComparer.Ordinal.Equals(claim.Item.Intent.ActivityExecutionId, workflowDispatch.ParentActivityExecutionId) &&
                StringComparer.Ordinal.Equals(claim.Item.Intent.IntentId, identity.StartIntentId) &&
                StringComparer.Ordinal.Equals(claim.Item.Intent.IdempotencyKey, identity.StartIdempotencyKey) &&
                claim.Item.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId) &&
                StringComparer.Ordinal.Equals(dispatchId, workflowDispatch.DispatchId) &&
                StringComparer.Ordinal.Equals(workflowDispatch.DispatchId, identity.DispatchId) &&
                WorkflowDispatchLifecycle.ReadSafeDiagnosticCode(workflowDispatch) is not null &&
                WorkflowDispatchLifecycle.ReadSafeDiagnosticCategory(workflowDispatch) is not null;
            if (!authorized)
                throw new ArgumentException("The claimed outbox intent does not authorize this workflow-dispatch failure projection.", nameof(workflowDispatch));
        }
        if (followUpOutboxItem is not null)
        {
            if (workflowDispatch is null || workflowDispatch.Mode != WorkflowDispatchMode.WaitForCompletion)
                throw new ArgumentException("A failure follow-up is valid only for a waited workflow dispatch.", nameof(followUpOutboxItem));
            var identity = new WorkflowDispatchIdentity(
                workflowDispatch.ParentWorkflowExecutionId,
                workflowDispatch.ParentActivityExecutionId);
            var generation = WorkflowDispatchLifecycle.ReadDeliveryGeneration(workflowDispatch);
            var authorized = followUpOutboxItem.Status == RuntimePostCommitOutboxStatus.Pending &&
                StringComparer.Ordinal.Equals(followUpOutboxItem.Intent.Kind, WorkflowDispatchLifecycle.ResumeParentIntentKind) &&
                followUpOutboxItem.DeliveryAttemptCount == 0 &&
                followUpOutboxItem.DeliveringOwnerId is null &&
                followUpOutboxItem.DeliveryStartedAt is null &&
                followUpOutboxItem.DeliveredAt is null &&
                followUpOutboxItem.LastFailureMessage is null &&
                followUpOutboxItem.RetryPolicy.RetryUntilAcknowledged &&
                StringComparer.Ordinal.Equals(followUpOutboxItem.OutboxItemId, identity.WaitFailureResumeOutboxItemId(generation)) &&
                StringComparer.Ordinal.Equals(followUpOutboxItem.Intent.IntentId, identity.ParentResumeIntentId) &&
                StringComparer.Ordinal.Equals(followUpOutboxItem.Intent.IdempotencyKey, identity.ParentResumeIdempotencyKey) &&
                StringComparer.Ordinal.Equals(followUpOutboxItem.Intent.WorkflowExecutionId, workflowDispatch.ParentWorkflowExecutionId) &&
                StringComparer.Ordinal.Equals(followUpOutboxItem.Intent.ActivityExecutionId, workflowDispatch.ParentActivityExecutionId) &&
                followUpOutboxItem.Intent.RecordedAt == deliveryResult.RecordedAt &&
                followUpOutboxItem.RecordedAt == deliveryResult.RecordedAt &&
                followUpOutboxItem.AvailableAt == deliveryResult.RecordedAt &&
                followUpOutboxItem.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var followUpDispatchId) &&
                StringComparer.Ordinal.Equals(followUpDispatchId, workflowDispatch.DispatchId) &&
                followUpOutboxItem.Intent.Metadata.TryGetValue(RuntimeMetadataKeys.ChildWorkflowExecutionId, out var childWorkflowExecutionId) &&
                StringComparer.Ordinal.Equals(childWorkflowExecutionId, workflowDispatch.ChildWorkflowExecutionId);
            if (!authorized)
                throw new ArgumentException("The failure follow-up does not match the waited dispatch and deterministic parent-resume identity.", nameof(followUpOutboxItem));
        }

        Claim = claim;
        DeliveryResult = deliveryResult;
        WorkflowDispatch = workflowDispatch;
        FollowUpOutboxItem = followUpOutboxItem;
    }

    public RuntimePostCommitOutboxClaim Claim { get; }
    public RuntimePostCommitOutboxDeliveryResult DeliveryResult { get; }
    public WorkflowDispatchRecord? WorkflowDispatch { get; }
    public RuntimePostCommitOutboxItem? FollowUpOutboxItem { get; }
}

/// <summary>Provider-neutral DispatchWorkflow final-failure projection produced before fenced completion.</summary>
public sealed class PostCommitFailureProjection
{
    public PostCommitFailureProjection(
        WorkflowDispatchRecord workflowDispatch,
        RuntimePostCommitOutboxItem? followUpOutboxItem = null)
    {
        ArgumentNullException.ThrowIfNull(workflowDispatch);
        if (workflowDispatch.Status != WorkflowDispatchStatus.DispatchFailed)
            throw new ArgumentException("A delivery-failure projection requires DispatchFailed lifecycle state.", nameof(workflowDispatch));
        if (followUpOutboxItem is not null && workflowDispatch.Mode != WorkflowDispatchMode.WaitForCompletion)
            throw new ArgumentException("Only a waited dispatch can carry a delivery-failure follow-up.", nameof(followUpOutboxItem));

        WorkflowDispatch = workflowDispatch;
        FollowUpOutboxItem = followUpOutboxItem;
    }

    public WorkflowDispatchRecord WorkflowDispatch { get; }
    public RuntimePostCommitOutboxItem? FollowUpOutboxItem { get; }
}

/// <summary>Shared claim and completion transitions for in-memory and durable outbox stores.</summary>
public static class RuntimePostCommitOutboxClaimTransitions
{
    public const string FirstDeliveryAttemptedAtMetadataKey = "runtime.delivery.firstAttemptedAt";

    public static DateTimeOffset ClaimableAt(RuntimePostCommitOutboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Status == RuntimePostCommitOutboxStatus.Delivering
            ? item.DeliveryVisibleAfter ?? DateTimeOffset.MaxValue
            : item.AvailableAt ?? DateTimeOffset.MinValue;
    }

    public static DateTimeOffset? ReadFirstDeliveryAttemptedAt(RuntimePostCommitOutboxItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Metadata.TryGetValue(FirstDeliveryAttemptedAtMetadataKey, out var value) &&
               DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var attemptedAt)
            ? attemptedAt
            : null;
    }

    public static bool CanClaim(RuntimePostCommitOutboxItem item, RuntimePostCommitOutboxClaimRequest request)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(request);

        if (request.WorkflowExecutionId is not null &&
            !StringComparer.Ordinal.Equals(item.Intent.WorkflowExecutionId, request.WorkflowExecutionId))
            return false;
        if (request.IntentKind is not null && !StringComparer.Ordinal.Equals(item.Intent.Kind, request.IntentKind))
            return false;
        if (item.Status == RuntimePostCommitOutboxStatus.Delivering)
            return item.DeliveryVisibleAfter is { } visibleAfter && visibleAfter <= request.Now;
        if (item.AvailableAt is { } availableAt && availableAt > request.Now)
            return false;
        if (item.Status == RuntimePostCommitOutboxStatus.Pending)
            return true;
        return item.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
               !item.RetryPolicy.IsExhaustedAfterAttempt(item.DeliveryAttemptCount);
    }

    public static RuntimePostCommitOutboxClaim Claim(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxClaimRequest request)
    {
        if (!CanClaim(item, request))
            throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' is not claimable.");

        var token = checked(item.DeliveryFencingToken + 1);
        var visibleAfter = request.Now.Add(request.VisibilityTimeout);
        var metadata = item.Metadata.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        metadata.TryAdd(FirstDeliveryAttemptedAtMetadataKey, request.Now.ToString("O", CultureInfo.InvariantCulture));
        var claimedItem = Copy(
            item,
            RuntimePostCommitOutboxStatus.Delivering,
            item.AvailableAt,
            item.DeliveryAttemptCount,
            request.OwnerId,
            request.Now,
            deliveredAt: null,
            item.LastFailureMessage,
            token,
            visibleAfter,
            metadata);
        return new RuntimePostCommitOutboxClaim(claimedItem, request.OwnerId, token, request.Now, visibleAfter);
    }

    public static RuntimePostCommitOutboxItem Complete(
        RuntimePostCommitOutboxItem current,
        RuntimePostCommitOutboxClaim claim,
        RuntimePostCommitOutboxDeliveryResult result)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);

        if (!StringComparer.Ordinal.Equals(current.OutboxItemId, claim.OutboxItemId) ||
            !StringComparer.Ordinal.Equals(result.OutboxItemId, claim.OutboxItemId) ||
            current.Status != RuntimePostCommitOutboxStatus.Delivering ||
            !StringComparer.Ordinal.Equals(current.DeliveringOwnerId, claim.OwnerId) ||
            current.DeliveryFencingToken != claim.FencingToken)
        {
            throw new InvalidOperationException($"Post-commit outbox claim for '{claim.OutboxItemId}' is stale or not owned by this claimant.");
        }

        var attemptCount = RuntimePostCommitRetryPolicy.SaturatingIncrement(current.DeliveryAttemptCount);
        var status = result.Status == RuntimePostCommitOutboxStatus.FailedRetryable &&
                     current.RetryPolicy.IsExhaustedAfterAttempt(attemptCount)
            ? RuntimePostCommitOutboxStatus.FailedFinal
            : result.Status;
        DateTimeOffset? availableAt = status == RuntimePostCommitOutboxStatus.FailedRetryable
            ? result.RecordedAt.Add(current.RetryPolicy.Delay ?? TimeSpan.Zero)
            : null;
        return Copy(
            current,
            status,
            availableAt,
            attemptCount,
            deliveringOwnerId: null,
            deliveryStartedAt: null,
            deliveredAt: status == RuntimePostCommitOutboxStatus.Delivered ? result.RecordedAt : null,
            result.FailureMessage,
            current.DeliveryFencingToken,
            deliveryVisibleAfter: null,
            current.Metadata);
    }

    /// <summary>Reopens the same terminal delivery responsibility while advancing its fence.</summary>
    public static RuntimePostCommitOutboxItem RedriveFailedFinal(
        RuntimePostCommitOutboxItem item,
        DateTimeOffset availableAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (availableAt == default)
            throw new ArgumentOutOfRangeException(nameof(availableAt), "A post-commit redrive requires a recorded availability time.");
        if (item.Status != RuntimePostCommitOutboxStatus.FailedFinal)
            throw new InvalidOperationException($"Post-commit outbox item '{item.OutboxItemId}' is not a failed-final dead letter.");

        var metadata = item.Metadata
            .Where(entry => !StringComparer.Ordinal.Equals(entry.Key, FirstDeliveryAttemptedAtMetadataKey))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        return Copy(
            item,
            RuntimePostCommitOutboxStatus.Pending,
            availableAt,
            deliveryAttemptCount: 0,
            deliveringOwnerId: null,
            deliveryStartedAt: null,
            deliveredAt: null,
            lastFailureMessage: null,
            deliveryFencingToken: checked(item.DeliveryFencingToken + 1),
            deliveryVisibleAfter: null,
            metadata);
    }

    private static RuntimePostCommitOutboxItem Copy(
        RuntimePostCommitOutboxItem item,
        RuntimePostCommitOutboxStatus status,
        DateTimeOffset? availableAt,
        int deliveryAttemptCount,
        string? deliveringOwnerId,
        DateTimeOffset? deliveryStartedAt,
        DateTimeOffset? deliveredAt,
        string? lastFailureMessage,
        long deliveryFencingToken,
        DateTimeOffset? deliveryVisibleAfter,
        IReadOnlyDictionary<string, string> metadata) =>
        new(
            item.OutboxItemId,
            item.Intent,
            status,
            item.RecordedAt,
            availableAt,
            item.RetryPolicy,
            deliveryAttemptCount,
            deliveringOwnerId,
            deliveryStartedAt,
            deliveredAt,
            lastFailureMessage,
            metadata,
            deliveryFencingToken,
            deliveryVisibleAfter);
}

public sealed class RuntimePostCommitRetryPolicy
{
    public RuntimePostCommitRetryPolicy(
        int maxAttempts,
        TimeSpan? delay,
        IReadOnlyDictionary<string, string>? metadata = null)
        : this(maxAttempts, delay, retryUntilAcknowledged: false, metadata)
    {
    }

    [JsonConstructor]
    public RuntimePostCommitRetryPolicy(
        int maxAttempts,
        TimeSpan? delay,
        bool retryUntilAcknowledged,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (maxAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Maximum delivery attempts cannot be negative.");

        if (retryUntilAcknowledged && maxAttempts != 0)
            throw new ArgumentException("A retry-until-acknowledged policy cannot carry a finite maximum attempt count.", nameof(maxAttempts));

        if (!retryUntilAcknowledged && maxAttempts == 0 && delay is not null)
            throw new ArgumentException("A retry policy with no attempts cannot carry a retry delay.", nameof(delay));

        if ((maxAttempts > 0 || retryUntilAcknowledged) && delay is null)
            throw new ArgumentException("A retry policy with attempts requires a retry delay.", nameof(delay));

        if (delay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay), "Retry delay must be greater than zero.");

        MaxAttempts = maxAttempts;
        Delay = delay;
        RetryUntilAcknowledged = retryUntilAcknowledged;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public static RuntimePostCommitRetryPolicy None { get; } = new(0, null);

    public static RuntimePostCommitRetryPolicy UntilAcknowledged(
        TimeSpan delay,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(0, delay, retryUntilAcknowledged: true, metadata: metadata);

    public int MaxAttempts { get; }
    public TimeSpan? Delay { get; }
    public bool RetryUntilAcknowledged { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public bool IsExhaustedAfterAttempt(int attemptCount)
    {
        if (attemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(attemptCount));

        return !RetryUntilAcknowledged && attemptCount >= MaxAttempts;
    }

    public bool IsEquivalentTo(RuntimePostCommitRetryPolicy? other) =>
        other is not null &&
        MaxAttempts == other.MaxAttempts &&
        Delay == other.Delay &&
        RetryUntilAcknowledged == other.RetryUntilAcknowledged &&
        Metadata.Count == other.Metadata.Count &&
        Metadata.All(pair => other.Metadata.TryGetValue(pair.Key, out var value) && StringComparer.Ordinal.Equals(pair.Value, value));

    public static int SaturatingIncrement(int attemptCount)
    {
        if (attemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(attemptCount));

        return attemptCount == int.MaxValue ? int.MaxValue : attemptCount + 1;
    }
}

public sealed class RuntimePostCommitOutboxQuery
{
    public RuntimePostCommitOutboxQuery(
        DateTimeOffset now,
        int limit,
        string? workflowExecutionId = null,
        string? ownerId = null,
        string? intentKind = null)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Outbox query limit must be greater than zero.");

        if (workflowExecutionId is not null && string.IsNullOrWhiteSpace(workflowExecutionId))
            throw new ArgumentException("Outbox workflow execution filter cannot be blank.", nameof(workflowExecutionId));

        if (ownerId is not null && string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Outbox owner filter cannot be blank.", nameof(ownerId));

        if (intentKind is not null)
            RuntimePostCommitIntent.ValidateKind(intentKind, nameof(intentKind));

        Now = now;
        Limit = limit;
        WorkflowExecutionId = workflowExecutionId;
        OwnerId = ownerId;
        IntentKind = intentKind;
    }

    public DateTimeOffset Now { get; }
    public int Limit { get; }
    public string? WorkflowExecutionId { get; }
    public string? OwnerId { get; }
    public string? IntentKind { get; }
}

public sealed class RuntimePostCommitOutboxDeliveryResult
{
    public RuntimePostCommitOutboxDeliveryResult(
        string outboxItemId,
        RuntimePostCommitOutboxStatus status,
        DateTimeOffset recordedAt,
        string? failureMessage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxItemId);

        if (status is RuntimePostCommitOutboxStatus.Pending or RuntimePostCommitOutboxStatus.Delivering)
            throw new ArgumentException("Outbox delivery results must record a completed delivery outcome.", nameof(status));

        if (status is RuntimePostCommitOutboxStatus.FailedRetryable or RuntimePostCommitOutboxStatus.FailedFinal)
            ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        if (status is not (RuntimePostCommitOutboxStatus.FailedRetryable or RuntimePostCommitOutboxStatus.FailedFinal) && failureMessage is not null)
            throw new ArgumentException("Only failed outbox delivery results can carry a failure message.", nameof(failureMessage));

        OutboxItemId = outboxItemId;
        Status = status;
        RecordedAt = recordedAt;
        FailureMessage = failureMessage;
    }

    public string OutboxItemId { get; }
    public RuntimePostCommitOutboxStatus Status { get; }
    public DateTimeOffset RecordedAt { get; }
    public string? FailureMessage { get; }
}

public enum RuntimePostCommitOutboxStatus
{
    Pending,
    Delivering,
    Delivered,
    FailedRetryable,
    FailedFinal,
    Cancelled
}
