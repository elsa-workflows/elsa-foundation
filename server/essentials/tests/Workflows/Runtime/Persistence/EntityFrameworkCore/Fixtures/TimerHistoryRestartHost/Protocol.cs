namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.TimerRestartFixture;

internal sealed record TimerRestartRequest(string Directory, string Nonce, string Mode, string Phase);
internal sealed record TimerRestartReply(string Status, int ProcessId, long ProcessStartTicks, string ArtifactHash,
    string Version, string Mode, string[] AppliedMigrations, string TimerWriteVersion, TimerRestartSnapshot? Before, TimerRestartSnapshot After);
internal sealed record TimerRestartSnapshot(string WorkflowStatus, string ActivityStatus, string ActivityId,
    int BookmarkCount, bool ActiveTimer, bool Pending, string? ScheduledId, DateTimeOffset? ScheduledAt,
    DateTimeOffset? ScheduleRecordedAt, string? DispatchedId, string? DispatchCause, long LastSequence,
    string[] EventNames, string[] EventIds, bool CapturedFromStart, bool HasGaps);
