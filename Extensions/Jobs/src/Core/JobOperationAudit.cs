namespace Elsa.Jobs.Core;

/// <summary>The finite administrative commands whose existing idempotent receipts retain operation audit metadata.</summary>
public enum JobOperationKind
{
    Cancel = 0,
    CancelOrigin = 1,
    RetryAsNewRun = 2,
    Pause = 3,
    ReleaseHold = 4,
    SetCapacity = 5,
    SaveSchedule = 6,
    SetScheduleEnabled = 7
}

/// <summary>Bounded command targets only. Fields are selected by command kind; no input, template, result or continuation payload is retained.</summary>
/// <remarks>For SetCapacity, an explicitly retained null CapacityLimit means unlimited; a missing retained field must not be interpreted as that decision.</remarks>
public sealed record JobOperationTarget(
    string? JobId = null,
    JobOrigin? Origin = null,
    JobGateKey? Gate = null,
    string? HoldId = null,
    string? ScheduleId = null,
    long? ExpectedRevision = null,
    int? CapacityLimit = null,
    bool? Enabled = null);

/// <summary>Original operation and first confirmation, recorded atomically using provider transaction time. Replay does not restamp this record.</summary>
/// <remarks>Retry targets name the source job; Confirmation.JobId names the actual accepted new job, never a proposed identifier. CommandSha256 retains the existing operation fingerprint.</remarks>
public sealed record JobOperationAudit(
    int FormatVersion,
    JobScope Scope,
    JobOperation Operation,
    JobOperationKind Kind,
    DateTimeOffset RecordedAt,
    string CommandSha256,
    JobOperationTarget Target,
    JobTransition Confirmation);

/// <summary>Validated receipt identity with available audit metadata or an explicit safe legacy/corruption diagnostic. Missing actor/reason/time is never invented.</summary>
public sealed record JobOperationAuditSnapshot(
    string OperationId,
    JobOperationAudit? Audit,
    JobDiagnostic? UnavailableDiagnostic = null);
