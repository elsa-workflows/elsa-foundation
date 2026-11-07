namespace Elsa.Jobs.Core;

/// <summary>Accepted automatic retry budget; includes the first authorized attempt.</summary>
public sealed record JobRetryPolicy(
    int MaximumAttempts,
    TimeSpan InitialBackoff,
    TimeSpan MaximumBackoff,
    double BackoffMultiplier,
    double JitterFraction,
    bool RetryTimeouts);

/// <summary>Immutable execution and producer-key recognition policy; stores validate bounds at acceptance.</summary>
public sealed record JobExecutionPolicy(
    JobRetryPolicy Retry,
    TimeSpan AttemptTimeout,
    TimeSpan MinimumKeyRecognition);

/// <summary>Immutable invocation content. Durable acceptance must snapshot and validate the complete request.</summary>
public sealed record JobRequest(
    JobHandlerIdentity Handler,
    JobPayload Input,
    JobSchema ResultSchema,
    string Queue,
    JobExecutionPolicy Policy,
    DateTimeOffset? NotBefore,
    JobAssociation? Association);

/// <summary>Ordinary producer request; trusted durable origins enter through provider admission instead.</summary>
public sealed record JobSubmission(
    string IdempotencyKey,
    JobRequest Request);

/// <summary>Durable admission confirmation; recognition may be extended by unsettled obligations.</summary>
public sealed record JobAcceptance(
    string JobId,
    DateTimeOffset AcceptedAt,
    DateTimeOffset RecognizeUntil,
    long Revision);

/// <summary>Authorized idempotent administrative command identity; providers bind it to the complete command digest.</summary>
public sealed record JobOperation(
    string OperationId,
    string Actor,
    string Reason);

/// <summary>Trusted validated admission. Proposed JobId is excluded from the request fingerprint so matching replay resolves the winner.</summary>
public sealed record JobAdmission(
    JobScope Scope,
    string ProposedJobId,
    string IdempotencyKey,
    string RequestSha256,
    JobRequest Request,
    JobOrigin? Origin);

/// <summary>Atomic admission confirmation; cancelled or retired durable origins never become fresh submissions.</summary>
public sealed record JobAdmissionResult(
    JobCommandStatus Status,
    JobAcceptance? Acceptance);

/// <summary>Small mutation confirmation, not a read model.</summary>
public sealed record JobTransition(
    JobCommandStatus Status,
    long? Revision,
    string? JobId,
    string? ResultId);

/// <summary>Explicit command confirmation; absence and unavailable storage are distinct.</summary>
public enum JobCommandStatus
{
    Applied = 0,
    Replayed = 1,
    Conflict = 2,
    NotFound = 3,
    Unavailable = 4,
    LeaseLost = 5,
    NoEligibleWork = 6,
    Held = 7,
    CapacityUnavailable = 8,
    Terminal = 9,
    OriginCancelled = 10,
    OriginRetired = 11,
    PayloadExpired = 12
}
