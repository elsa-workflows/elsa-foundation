namespace Elsa.Etl.Core.Runtime;

public enum EtlHttpDeliveryPhase { InFlight, Waiting, Acknowledged, Failed }
public enum EtlHttpDeliveryFailure { None, AttemptsExhausted, DeadlineExceeded, ResponseRejected }

/// <summary>A durably allocated physical request attempt, bound to the original effect and current fence.</summary>
/// <remarks>RequestId is a caller-generated lowercase 32-digit GUID, reused only for an identical begin call.
/// ExpiresAt bounds the request; it does not prove physical termination or release local capacity.</remarks>
public sealed record EtlHttpDeliveryAttempt(string RequestId, int Number, long Fence, string EffectId,
    DateTimeOffset StartedAt, DateTimeOffset ExpiresAt);
/// <summary>Transport outcome without a response body, URL or exception. Null status means uncertain delivery.</summary>
/// <remarks>RetryAfter is an already parsed absolute UTC instant; transport parsing remains the caller's responsibility.
/// ResponseReceivedAtUtc records completion of bounded response handling, separate from the durable time at which
/// the store reconciles this outcome. Acknowledgement uses status; draining a response body is optional and bounded.
/// The timestamp is optional for compatibility with callers that cannot provide it.</remarks>
public sealed record EtlHttpDeliveryResponse(int? StatusCode, DateTimeOffset? RetryAfter)
{
    public DateTimeOffset? ResponseReceivedAtUtc { get; init; }
}
/// <summary>An open attempt has no completion time; a closed attempt with no response records uncertainty.</summary>
public sealed record EtlHttpDeliveryAttemptRecord(EtlHttpDeliveryAttempt Attempt, EtlHttpDeliveryResponse? Response,
    DateTimeOffset? CompletedAt);
/// <summary>Bounded durable attempt history for one retained route body.</summary>
public sealed record EtlConnectorHttpDelivery(DateTimeOffset Deadline, IReadOnlyList<EtlHttpDeliveryAttemptRecord> Attempts,
    EtlHttpDeliveryPhase Phase, DateTimeOffset? NotBefore, EtlHttpDeliveryFailure Failure, DateTimeOffset UpdatedAt);
public sealed record EtlConnectorHttpDeliveries(EtlConnectorHttpDelivery? Main, EtlConnectorHttpDelivery? Errors);
/// <summary>Only a non-null Permit authorizes a transport attempt. A refusal can carry existing retry state.</summary>
public sealed record EtlConnectorHttpDeliveryResult(EtlStoreStatus Status, EtlConnectorHttpDelivery? Delivery,
    EtlHttpDeliveryAttempt? Permit);
