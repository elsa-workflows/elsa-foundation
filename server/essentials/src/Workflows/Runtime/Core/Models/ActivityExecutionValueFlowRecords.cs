using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Portable contract identity pinned to one logical invocation.
/// </summary>
public sealed record ActivityInvocationContractIdentity
{
    [JsonConstructor]
    public ActivityInvocationContractIdentity(string activityTypeKey, string contractVersion, string schemaFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityTypeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaFingerprint);

        ActivityTypeKey = activityTypeKey;
        ContractVersion = contractVersion;
        SchemaFingerprint = schemaFingerprint;
    }

    public string ActivityTypeKey { get; }
    public string ContractVersion { get; }
    public string SchemaFingerprint { get; }
}

public static class ActivityExecutionValueFlowDocumentVersions
{
    public const int Legacy = 1;
    public const int Current = 2;
}

/// <summary>
/// Complete materialized inputs pinned to one logical activity invocation before activation.
/// </summary>
public sealed record ActivityInputSnapshot
{
    [JsonConstructor]
    public ActivityInputSnapshot(
        string invocationId,
        string contractFingerprint,
        string bindingFingerprint,
        IReadOnlyDictionary<string, ValueEnvelope> values,
        DateTimeOffset materializedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingFingerprint);
        ArgumentNullException.ThrowIfNull(values);

        var snapshot = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(values));
            ArgumentNullException.ThrowIfNull(value);

            if (!snapshot.TryAdd(key, value))
                throw new ArgumentException($"Pinned activity input key '{key}' occurs more than once.", nameof(values));
        }

        InvocationId = invocationId;
        ContractFingerprint = contractFingerprint;
        BindingFingerprint = bindingFingerprint;
        Values = new ReadOnlyDictionary<string, ValueEnvelope>(snapshot);
        MaterializedAt = materializedAt;
    }

    public string InvocationId { get; }
    public string ContractFingerprint { get; }
    public string BindingFingerprint { get; }
    public IReadOnlyDictionary<string, ValueEnvelope> Values { get; }
    public DateTimeOffset MaterializedAt { get; }
}

/// <summary>
/// One transient activation attempt belonging to a stable logical activity invocation.
/// </summary>
public sealed record ActivityAttempt
{
    [JsonConstructor]
    public ActivityAttempt(
        string attemptId,
        string invocationId,
        int ordinal,
        ActivityAttemptReason reason,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt = null,
        string? triggerDeliveryId = null,
        ActivityTransitionKind? transitionKind = null,
        string? incidentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);

        if (ordinal <= 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "An activity attempt ordinal must be positive.");

        if (triggerDeliveryId is not null && string.IsNullOrWhiteSpace(triggerDeliveryId))
            throw new ArgumentException("A trigger delivery ID cannot be blank.", nameof(triggerDeliveryId));

        if (reason != ActivityAttemptReason.Resume && triggerDeliveryId is not null)
            throw new ArgumentException("Only a resume attempt can carry a trigger delivery ID.", nameof(triggerDeliveryId));

        if (endedAt.HasValue != transitionKind.HasValue)
            throw new ArgumentException("An ended activity attempt must declare a transition, and an active attempt cannot declare one.");

        if (endedAt is { } completedAt && completedAt < startedAt)
            throw new ArgumentOutOfRangeException(nameof(endedAt), endedAt, "An activity attempt cannot end before it starts.");

        if (incidentId is not null && string.IsNullOrWhiteSpace(incidentId))
            throw new ArgumentException("An incident ID cannot be blank.", nameof(incidentId));

        if (incidentId is not null && transitionKind != ActivityTransitionKind.Fault)
            throw new ArgumentException("Only a fault transition can carry an incident ID.", nameof(incidentId));

        AttemptId = attemptId;
        InvocationId = invocationId;
        Ordinal = ordinal;
        Reason = reason;
        StartedAt = startedAt;
        EndedAt = endedAt;
        TriggerDeliveryId = triggerDeliveryId;
        TransitionKind = transitionKind;
        IncidentId = incidentId;
    }

    public string AttemptId { get; }
    public string InvocationId { get; }
    public int Ordinal { get; }
    public ActivityAttemptReason Reason { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? EndedAt { get; }
    public string? TriggerDeliveryId { get; }
    public ActivityTransitionKind? TransitionKind { get; }
    public string? IncidentId { get; }
}

public enum ActivityAttemptReason
{
    Initial,
    Retry,
    Resume
}

public enum ActivityTransitionKind
{
    Complete,
    Suspend,
    Fault,
    Cancel
}

/// <summary>
/// Last complete immutable private state document committed by a suspended invocation.
/// </summary>
public sealed record ActivityPrivateState
{
    [JsonConstructor]
    public ActivityPrivateState(
        string invocationId,
        int stateVersion,
        ValueEnvelope value,
        string producedByAttemptId,
        DateTimeOffset committedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(producedByAttemptId);
        ArgumentNullException.ThrowIfNull(value);

        if (stateVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(stateVersion), stateVersion, "An activity state version must be positive.");

        RequireMaterialized(value, nameof(value), "Activity private state");
        RequirePersistable(value, nameof(value), "Activity private state");

        InvocationId = invocationId;
        StateVersion = stateVersion;
        Value = value;
        ProducedByAttemptId = producedByAttemptId;
        CommittedAt = committedAt;
    }

    public string InvocationId { get; }
    public int StateVersion { get; }
    public ValueEnvelope Value { get; }
    public string ProducedByAttemptId { get; }
    public DateTimeOffset CommittedAt { get; }

    private static void RequireMaterialized(ValueEnvelope value, string parameterName, string subject)
    {
        if (value.Presence == ValuePresence.Absent)
            throw new ArgumentException($"{subject} cannot be absent.", parameterName);
    }

    private static void RequirePersistable(ValueEnvelope value, string parameterName, string subject)
    {
        if (value.Policy.Lifecycle == DurableValueLifecycle.None)
            throw new ArgumentException($"{subject} must be persistable.", parameterName);
    }
}

/// <summary>
/// One atomic successful activity result and authored control outcome.
/// </summary>
public sealed record ActivityCompletion
{
    [JsonConstructor]
    public ActivityCompletion(
        string invocationId,
        string attemptId,
        ValueEnvelope result,
        string outcomeKey,
        DateTimeOffset completedAt,
        string contractFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcomeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractFingerprint);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Presence == ValuePresence.Absent)
            throw new ArgumentException("An activity completion result cannot be absent.", nameof(result));

        InvocationId = invocationId;
        AttemptId = attemptId;
        Result = result;
        OutcomeKey = outcomeKey;
        CompletedAt = completedAt;
        ContractFingerprint = contractFingerprint;
    }

    public string InvocationId { get; }
    public string AttemptId { get; }
    public ValueEnvelope Result { get; }
    public ValueTypeDescriptor ResultType => Result.Type;
    public string OutcomeKey { get; }
    public DateTimeOffset CompletedAt { get; }
    public string ContractFingerprint { get; }
}

/// <summary>
/// Safe persistable fault information; never contains a live CLR exception.
/// </summary>
public sealed record NormalizedActivityFault
{
    [JsonConstructor]
    public NormalizedActivityFault(
        string code,
        string exceptionType,
        string message,
        string? sanitizedStackTrace,
        bool isRetryable,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(exceptionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (sanitizedStackTrace is not null && string.IsNullOrWhiteSpace(sanitizedStackTrace))
            throw new ArgumentException("A sanitized stack trace cannot be blank.", nameof(sanitizedStackTrace));

        Code = code;
        ExceptionType = exceptionType;
        Message = message;
        SanitizedStackTrace = sanitizedStackTrace;
        IsRetryable = isRetryable;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string Code { get; }
    public string ExceptionType { get; }
    public string Message { get; }
    public string? SanitizedStackTrace { get; }
    public bool IsRetryable { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

/// <summary>
/// Typed durable trigger expectation committed with an activity suspension.
/// </summary>
public sealed record ActivityTriggerRegistration
{
    [JsonConstructor]
    public ActivityTriggerRegistration(
        string registrationId,
        string invocationId,
        string resumeTargetKey,
        ValueTypeDescriptor payloadType,
        string stimulusType,
        string stimulusHash,
        ActivityTriggerDeduplicationPolicy deduplicationPolicy,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resumeTargetKey);
        ArgumentNullException.ThrowIfNull(payloadType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);

        RegistrationId = registrationId;
        InvocationId = invocationId;
        ResumeTargetKey = resumeTargetKey;
        PayloadType = payloadType;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        DeduplicationPolicy = deduplicationPolicy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string RegistrationId { get; }
    public string InvocationId { get; }
    public string ResumeTargetKey { get; }
    public ValueTypeDescriptor PayloadType { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public ActivityTriggerDeduplicationPolicy DeduplicationPolicy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public enum ActivityTriggerDeduplicationPolicy
{
    Once,
    IdempotencyKey
}

/// <summary>
/// One identified typed payload delivered to a suspended activity invocation.
/// </summary>
public sealed record ActivityTriggerDelivery
{
    [JsonConstructor]
    public ActivityTriggerDelivery(
        string deliveryId,
        string registrationId,
        ValueTypeDescriptor payloadType,
        ValueEnvelope payload,
        string providerId,
        DateTimeOffset receivedAt,
        string deduplicationKey,
        ActivityTriggerDeliveryStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registrationId);
        ArgumentNullException.ThrowIfNull(payloadType);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deduplicationKey);

        if (payload.Presence == ValuePresence.Absent && status != ActivityTriggerDeliveryStatus.Consumed)
            throw new ArgumentException("Only a consumed historical trigger delivery may omit its retired payload.", nameof(payload));

        if (payload.Policy.Lifecycle == DurableValueLifecycle.None)
            throw new ArgumentException("A trigger delivery payload must be persistable.", nameof(payload));

        if (!SameType(payloadType, payload.Type))
            throw new ArgumentException("The trigger delivery payload does not match its declared portable type.", nameof(payload));

        DeliveryId = deliveryId;
        RegistrationId = registrationId;
        PayloadType = payloadType;
        Payload = payload;
        ProviderId = providerId;
        ReceivedAt = receivedAt;
        DeduplicationKey = deduplicationKey;
        Status = status;
    }

    public string DeliveryId { get; }
    public string RegistrationId { get; }
    public ValueTypeDescriptor PayloadType { get; }
    public ValueEnvelope Payload { get; }
    public string ProviderId { get; }
    public DateTimeOffset ReceivedAt { get; }
    public string DeduplicationKey { get; }
    public ActivityTriggerDeliveryStatus Status { get; }

    private static bool SameType(ValueTypeDescriptor left, ValueTypeDescriptor right) =>
        StringComparer.Ordinal.Equals(left.Alias, right.Alias) &&
        left.CollectionKind == right.CollectionKind &&
        left.SchemaVersion == right.SchemaVersion &&
        StringComparer.Ordinal.Equals(left.Schema?.GetRawText(), right.Schema?.GetRawText());
}

public enum ActivityTriggerDeliveryStatus
{
    Received,
    Consumed,
    Duplicate,
    Rejected
}

/// <summary>
/// Explicit compatibility decision used while moving an activity-execution document between versions.
/// </summary>
public sealed record ActivityExecutionValueFlowDocumentVersionGuard
{
    [JsonConstructor]
    public ActivityExecutionValueFlowDocumentVersionGuard(
        int sourceVersion,
        int targetVersion,
        bool isCompatible,
        string? incompatibilityCode,
        string? reason)
    {
        if (sourceVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceVersion), sourceVersion, "A source document version must be positive.");

        if (targetVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetVersion), targetVersion, "A target document version must be positive.");

        if (targetVersion < sourceVersion)
            throw new ArgumentOutOfRangeException(nameof(targetVersion), targetVersion, "A document version guard cannot move a document backwards.");

        if (isCompatible && (incompatibilityCode is not null || reason is not null))
            throw new ArgumentException("A compatible document version guard cannot carry incompatibility details.");

        if (!isCompatible && (string.IsNullOrWhiteSpace(incompatibilityCode) || string.IsNullOrWhiteSpace(reason)))
            throw new ArgumentException("An incompatible document version guard requires a code and reason.");

        SourceVersion = sourceVersion;
        TargetVersion = targetVersion;
        IsCompatible = isCompatible;
        IncompatibilityCode = incompatibilityCode;
        Reason = reason;
    }

    public int SourceVersion { get; }
    public int TargetVersion { get; }
    public bool IsCompatible { get; }
    public string? IncompatibilityCode { get; }
    public string? Reason { get; }

    public static ActivityExecutionValueFlowDocumentVersionGuard Compatible(int sourceVersion, int targetVersion) =>
        new(sourceVersion, targetVersion, true, null, null);

    public static ActivityExecutionValueFlowDocumentVersionGuard Incompatible(
        int sourceVersion,
        int targetVersion,
        string incompatibilityCode,
        string reason) =>
        new(sourceVersion, targetVersion, false, incompatibilityCode, reason);

    public void EnsureCompatible()
    {
        if (!IsCompatible)
            throw new InvalidOperationException(
                $"Activity-execution document version {SourceVersion} cannot be upgraded to {TargetVersion}: " +
                $"{IncompatibilityCode}: {Reason}");
    }
}
