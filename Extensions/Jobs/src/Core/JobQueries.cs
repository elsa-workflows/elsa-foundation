namespace Elsa.Jobs.Core;

/// <summary>Bounded inspection of accepted state. Unreadable/unsupported requests retain validated metadata and a safe unavailable diagnostic; reads never fabricate executable content.</summary>
public sealed record JobSnapshot(
    JobScope Scope,
    string JobId,
    string RequestSha256,
    JobRequest? Request,
    JobState State,
    long Revision,
    DateTimeOffset AcceptedAt,
    DateTimeOffset EligibleAt,
    JobAttemptLease? ActiveAttempt,
    JobResult? Result,
    JobProgress? Progress,
    bool CancellationRequested,
    string? RetriedFromJobId,
    JobDiagnostic? UnavailableDiagnostic = null)
{
    public JobRequestMetadata? RequestMetadata { get; init; }
    public JobResultMetadata? ResultMetadata { get; init; }
    public JobPayloadState InputPayloadState { get; init; } = JobPayloadState.Available;
    public JobPayloadState ResultPayloadState { get; init; } = JobPayloadState.NotProduced;
    public JobPayloadRetention? PayloadRetention { get; init; }
}

/// <summary>Bounded cursor request; providers bind opaque cursors to exact scope and filters.</summary>
public sealed record JobPageRequest(
    int Limit,
    string? ContinuationToken);

/// <summary>One bounded read-only page; a null continuation ends the current traversal.</summary>
public sealed record JobPage<T>(
    IReadOnlyList<T> Items,
    string? ContinuationToken);

/// <summary>Bounded job inspection filter.</summary>
public sealed record JobListQuery(
    JobPageRequest Page,
    string? Queue,
    string? JobType,
    JobState? State);

/// <summary>Operational queue counts; authorized live leases and held work remain separate.</summary>
public sealed record JobQueueSnapshot(
    string Name,
    long AcceptedCount,
    long ActiveLeaseCount,
    long HeldCount);

/// <summary>Primary lifecycle; delay, holds and delivery responsibility are separate inspection dimensions.</summary>
public enum JobState
{
    Accepted = 0,
    AttemptActive = 1,
    Terminal = 2
}

/// <summary>Replacement contract: scoped CQS reads; reads never create claims, permissions, recovery state or retention changes.</summary>
public interface IJobQueries
{
    ValueTask<JobSnapshot?> FindAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default);
    ValueTask<JobPage<JobSnapshot>> ListJobsAsync(JobScope scope, JobListQuery query, CancellationToken cancellationToken = default);
    ValueTask<JobPage<JobAttemptSnapshot>> ListAttemptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default);
    ValueTask<JobPage<JobPhysicalReceipt>> ListPhysicalReceiptsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default);
    /// <summary>Reads original report metadata with explicit payload expiry, without inventing a null-valued successful receipt.</summary>
    ValueTask<JobPage<JobPhysicalReceiptInspection>> ListPhysicalReceiptInspectionsAsync(JobScope scope, string jobId, JobPageRequest page, CancellationToken cancellationToken = default);
    ValueTask<JobPage<JobHold>> ListHoldsAsync(JobScope scope, JobGateKey? gate, JobPageRequest page, CancellationToken cancellationToken = default);
    ValueTask<JobPage<JobCapacity>> ListCapacityAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default);
    ValueTask<JobScheduleSnapshot?> FindScheduleAsync(JobScope scope, string scheduleId, CancellationToken cancellationToken = default);
    ValueTask<JobPage<JobScheduleSnapshot>> ListSchedulesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default);
    ValueTask<JobPage<JobDeliverySnapshot>> ListPendingDeliveriesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default);
    /// <summary>Reads one exact retained grant, including after delivery acknowledgement. Null is absence of a grant, never model-consumption proof.</summary>
    ValueTask<JobContinuationPermission?> FindContinuationAsync(JobScope scope, string jobId, string continuationId, CancellationToken cancellationToken = default);
    ValueTask<JobDeliveryStatusSnapshot?> FindDeliveryAsync(JobScope scope, string jobId, CancellationToken cancellationToken = default);
    ValueTask<JobOriginSnapshot?> FindOriginAsync(JobScope scope, JobOrigin origin, CancellationToken cancellationToken = default);
    /// <summary>Reads one exact retained operation. Null means no receipt; legacy or unreadable audit metadata has an explicit unavailable projection.</summary>
    ValueTask<JobOperationAuditSnapshot?> FindOperationAuditAsync(JobScope scope, string operationId, CancellationToken cancellationToken = default);
    /// <summary>Pages scoped operation receipts in stable key order, including unavailable metadata. This is not a chronological or complete per-job history query.</summary>
    ValueTask<JobPage<JobOperationAuditSnapshot>> ListOperationAuditsAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default);
    ValueTask<JobPage<JobQueueSnapshot>> ListQueuesAsync(JobScope scope, JobPageRequest page, CancellationToken cancellationToken = default);
}
