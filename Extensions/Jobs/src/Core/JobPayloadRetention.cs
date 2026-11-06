namespace Elsa.Jobs.Core;

/// <summary>Payload availability is distinct from a missing job, pending result and unreadable retained data.</summary>
public enum JobPayloadState
{
    Available = 0,
    NotProduced = 1,
    Expired = 2
}

/// <summary>Immutable request identity without its input bytes. Metadata and original digests remain indefinitely.</summary>
public sealed record JobRequestMetadata(JobHandlerIdentity Handler, JobSchema InputSchema, string InputSha256,
    JobSchema ResultSchema, string Queue, JobExecutionPolicy Policy, DateTimeOffset? NotBefore, JobAssociation? Association)
{
    public static JobRequestMetadata From(JobRequest request) => new(request.Handler, request.Input.Schema,
        JobPayloadDigest.Compute(request.Input), request.ResultSchema, request.Queue, request.Policy, request.NotBefore, request.Association);
}

/// <summary>Selected result identity without its payload bytes; a successful expired result is not a fabricated null-valued result.</summary>
public sealed record JobResultMetadata(string ResultId, string JobId, string? AttemptId, JobOutcome Outcome,
    JobSchema? PayloadSchema, string PayloadSha256, DateTimeOffset CompletedAt, JobDiagnostic? Diagnostic)
{
    public static JobResultMetadata From(JobResult result) => new(result.ResultId, result.JobId, result.AttemptId,
        result.Outcome, result.Payload?.Schema, result.PayloadSha256, result.CompletedAt, result.Diagnostic);
}

/// <summary>Acceptance-captured terminal payload promises; null durations retain bytes indefinitely. Metadata has no expiry.</summary>
public sealed record JobPayloadRetention(TimeSpan? TerminalInputRetention, TimeSpan? TerminalResultRetention,
    DateTimeOffset? InputExpiresAt, DateTimeOffset? ResultExpiresAt, DateTimeOffset? InputExpiredAt, DateTimeOffset? ResultExpiredAt);

/// <summary>Original physical-report identity and digest, retained after report payload expiry.</summary>
public sealed record JobPhysicalReceiptMetadata(string ReceiptId, string AttemptId, string Owner, long Fence,
    JobAttemptOutcome Outcome, JobSchema? PayloadSchema, string PayloadSha256, JobDiagnostic? Diagnostic,
    bool Retryable, DateTimeOffset ReportedAt, string ReceiptSha256);

/// <summary>Inspection retains metadata independently from the optional original physical report.</summary>
public sealed record JobPhysicalReceiptInspection(JobPhysicalReceiptMetadata Metadata, JobPhysicalReceipt? Receipt,
    JobPayloadState PayloadState, DateTimeOffset? ExpiredAt);

/// <summary>Trusted scope-bound maintenance request; providers use their own time and a bounded page.</summary>
public sealed record JobPayloadCleanupRequest(JobScope Scope, int MaximumJobs = 100, string? ContinuationToken = null);

/// <summary>Confirmed cleanup counts. Protected or unavailable records keep all their bytes.</summary>
public sealed record JobPayloadCleanupResult(int ExaminedJobs, int ExpiredInputs, int ExpiredResults, string? ContinuationToken);
