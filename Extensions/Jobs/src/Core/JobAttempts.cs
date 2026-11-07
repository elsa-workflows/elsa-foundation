namespace Elsa.Jobs.Core;

/// <summary>Claims one compatible due invocation under a stable per-pull request identity. Providers require a nonblank RequestId and replay its original grant; workers renew before physical entry.</summary>
public sealed record JobClaimRequest(
    JobScope Scope,
    string Owner,
    IReadOnlyList<string> Queues,
    IReadOnlyList<JobHandlerSupport> Handlers,
    TimeSpan LeaseDuration,
    string? RequestId = null);

/// <summary>One durable start permission and ownership revision. Only claim/renewal changes that revision; observational writes do not invalidate it. Expiry does not prove physical exit.</summary>
public sealed record JobAttemptLease(
    JobScope Scope,
    string JobId,
    string AttemptId,
    int AttemptNumber,
    string Owner,
    long Fence,
    long Revision,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset Deadline,
    string ImplementationIdentity);

/// <summary>Atomic claim confirmation; a returned lease is permission, not proof the handler entered.</summary>
public sealed record JobClaimResult(
    JobCommandStatus Status,
    JobAttemptLease? Lease);

/// <summary>Renewal confirmation retains cancellation/deadline signals without pretending the physical task exited.</summary>
public sealed record JobRenewal(
    JobCommandStatus Status,
    JobAttemptLease? Lease,
    bool CancellationRequested,
    bool DeadlineReached);

/// <summary>Safe bounded domain diagnostic; raw exception bodies, credentials and input values are prohibited.</summary>
public sealed record JobDiagnostic(
    string Code,
    string Summary);

/// <summary>Immutable physical completion report; a stale receipt cannot select or replace an authoritative result.</summary>
/// <remarks>Successful completion requires its declared schema envelope; its JSON value may be null. Failure can omit the envelope.</remarks>
public sealed record JobPhysicalReceipt(
    string ReceiptId,
    string AttemptId,
    string Owner,
    long Fence,
    JobAttemptOutcome Outcome,
    JobPayload? Payload,
    string PayloadSha256,
    JobDiagnostic? Diagnostic,
    bool Retryable,
    DateTimeOffset ReportedAt);

/// <summary>Immutable selected terminal outcome; retains its identity through all consumer redeliveries.</summary>
public sealed record JobResult(
    string ResultId,
    string JobId,
    string? AttemptId,
    JobOutcome Outcome,
    JobPayload? Payload,
    JobDiagnostic? Diagnostic,
    string PayloadSha256,
    DateTimeOffset CompletedAt);

/// <summary>Inspection separates durable authorization from reported physical entry and exit.</summary>
public sealed record JobAttemptSnapshot(
    JobAttemptLease Lease,
    JobAttemptState State,
    DateTimeOffset? PhysicalStartedAt,
    DateTimeOffset? PhysicalExitedAt,
    JobDiagnostic? Diagnostic);

/// <summary>Durable attempt facts; timed-out, cancelled and lost states do not prove physical exit.</summary>
public enum JobAttemptState
{
    Permitted = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    TimedOut = 4,
    Cancelled = 5,
    OwnershipLost = 6
}

/// <summary>Reported physical outcome, distinct from authoritative job settlement.</summary>
public enum JobAttemptOutcome
{
    Succeeded = 0,
    Failed = 1,
    TimedOut = 2,
    Cancelled = 3,
    Abandoned = 4
}

/// <summary>Authoritative terminal result selected by the job authority.</summary>
public enum JobOutcome
{
    Succeeded = 0,
    Failed = 1,
    TimedOut = 2,
    Cancelled = 3
}
