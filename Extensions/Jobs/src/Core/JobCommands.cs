namespace Elsa.Jobs.Core;

/// <summary>Replacement contract: one authoritative mutation provider per configured context; atomic commands return confirmation tokens only.</summary>
public interface IJobCommands
{
    /// <summary>Expires acceptance-configured terminal payloads only after exact owed delivery proof has settled; metadata and digests remain.</summary>
    ValueTask<JobPayloadCleanupResult> ExpirePayloadsAsync(JobPayloadCleanupRequest request, CancellationToken cancellationToken = default);

    /// <summary>Validates and atomically recognizes a producer key or protected durable origin before accepting new work.</summary>
    ValueTask<JobAdmissionResult> AdmitAsync(JobAdmission admission, CancellationToken cancellationToken = default);

    /// <summary>Atomically grants/replays one per-pull permission identity. New grants check all matching holds/capacity; replay never selects a second job.</summary>
    ValueTask<JobClaimResult> ClaimAsync(JobClaimRequest request, CancellationToken cancellationToken = default);

    /// <summary>Renews only current unexpired ownership; returns its new ownership revision and stop signals.</summary>
    ValueTask<JobRenewal> RenewAsync(JobAttemptLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default);

    /// <summary>Records physical entry under a valid permission without changing its ownership revision.</summary>
    ValueTask<JobTransition> RecordPhysicalStartAsync(JobAttemptLease lease, CancellationToken cancellationToken = default);

    /// <summary>Atomically selects a result or eligible retry and releases only this current attempt's permits.</summary>
    ValueTask<JobTransition> CompleteAsync(JobAttemptLease lease, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default);

    /// <summary>Appends exact attempt evidence, including authenticated late receipts; never changes ownership or result selection.</summary>
    ValueTask<JobTransition> RecordPhysicalReceiptAsync(JobScope scope, string jobId, JobPhysicalReceipt receipt, CancellationToken cancellationToken = default);

    /// <summary>Records bounded progress under current ownership without changing its ownership revision.</summary>
    ValueTask<JobTransition> RecordProgressAsync(JobAttemptLease lease, JobProgress progress, CancellationToken cancellationToken = default);

    /// <summary>Records cancellation independently from cooperative acknowledgement and physical exit.</summary>
    ValueTask<JobTransition> CancelAsync(JobCancelRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cancels an existing exact origin or atomically prevents its future admission with a durable tombstone.</summary>
    ValueTask<JobTransition> CancelOriginAsync(JobCancelOriginRequest request, CancellationToken cancellationToken = default);

    /// <summary>Checks terminal revision, records operation recognition and creates one linked independent invocation atomically.</summary>
    ValueTask<JobAdmissionResult> RetryAsNewRunAsync(JobRetryAsNewRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>Adds an independent hold ordered against attempt, occurrence and model-delivery permission decisions.</summary>
    ValueTask<JobTransition> PauseAsync(JobPauseRequest request, CancellationToken cancellationToken = default);

    /// <summary>Releases only the addressed hold at its expected revision.</summary>
    ValueTask<JobTransition> ReleaseHoldAsync(JobReleaseHoldRequest request, CancellationToken cancellationToken = default);

    /// <summary>Revision-checks a live-authorized-lease limit; the limit does not bound stale physical processes.</summary>
    ValueTask<JobTransition> SetCapacityAsync(JobSetCapacityRequest request, CancellationToken cancellationToken = default);

    /// <summary>Revision-checks a schedule without silently discarding unmaterialized due responsibility.</summary>
    ValueTask<JobTransition> SaveScheduleAsync(JobSaveScheduleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Changes schedule eligibility, preserving its cursor and every accepted job.</summary>
    ValueTask<JobTransition> SetScheduleEnabledAsync(JobSetScheduleEnabledRequest request, CancellationToken cancellationToken = default);

    /// <summary>Atomically accepts bounded due occurrences and advances their cursor under the selected missed-occurrence policy.</summary>
    ValueTask<JobMaterializationResult> MaterializeDueAsync(JobMaterializationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Grants/replays permission for the exact retained result/model input; workflow holds prevent only new grants.</summary>
    ValueTask<JobContinuationResult> GrantContinuationAsync(JobContinuationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Retires exact owed delivery only after trusted model-owned consumer or terminal-parent proof.</summary>
    ValueTask<JobTransition> AcknowledgeDeliveryAsync(JobAcknowledgeDeliveryRequest request, CancellationToken cancellationToken = default);
}
