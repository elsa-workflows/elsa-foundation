namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.RestartFixture;

internal sealed record RestartRequest(string Directory, string Nonce, string Mode, string Operation, string? SequenceToken = null);
internal sealed record CheckpointGroup(string Logical, string Physical);
internal sealed record HistorySnapshot(string WorkflowStatus, string ActivityStatus, int BookmarkCount,
    string Availability, string? SequenceToken, long Upper, long? Total, long? Position, string? ActivityId,
    string? FirstEventId, long? FirstSequence, string? FirstPhysical, DateTimeOffset? FirstRecordedAt, string? FirstEvidenceHash,
    long LastSequence, long PurgedThrough, long NativeMarkers, string NativeEvidenceHash, CheckpointGroup[] Groups);
internal sealed record RestartReply(string Status, int ProcessId, long ProcessStartTicks, string ArtifactHash,
    string Version, string Mode, string[] AppliedMigrations, HistorySnapshot? Before, HistorySnapshot After,
    HistorySnapshot? Replayed = null, string? Error = null);
