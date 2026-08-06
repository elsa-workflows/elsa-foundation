namespace Elsa.Workflows.Runtime.Core.Models;

public enum RuntimeCheckpointRecoveryStatus
{
    Absent,
    Exact,
    Missing,
    FingerprintMismatch,
    UnsupportedVersionOrKind,
    Ambiguous
}

public enum RuntimeCheckpointRecoveryRoute
{
    SourceFree,
    SourceBound
}

public sealed record RuntimeCheckpointRecoveryResolution(
    RuntimeCheckpointRecoveryStatus Status,
    RuntimeCheckpointRecoveryAuthority? Authority = null,
    RuntimeSchedulerWorkItem? WorkItem = null,
    RuntimeCheckpointRecoveryRoute Route = RuntimeCheckpointRecoveryRoute.SourceBound);
