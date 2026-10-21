namespace Elsa.Samples.Compensation.Ledger;

/// <summary>One resource's ledger file: what was reserved, what was released, and which late requests to refuse.</summary>
public sealed class ResourceLedger
{
    public required string Resource { get; init; }
    public List<ReservationRecord> Reservations { get; init; } = [];
    public List<ReleaseRecord> Releases { get; init; } = [];
    public List<TombstoneRecord> Tombstones { get; init; } = [];

    /// <summary>When each failed release attempt came, by the release's operation key.</summary>
    public Dictionary<string, List<DateTimeOffset>> FailedReleaseAttempts { get; init; } = new(StringComparer.Ordinal);
}

public sealed class ReservationRecord
{
    public required string ReservationId { get; init; }
    public required string OperationKey { get; init; }
    public required string WorkflowExecutionId { get; init; }
    public ReservationStatus Status { get; set; }
    public DateTimeOffset ReservedAt { get; init; }
    public DateTimeOffset? ReleasedAt { get; set; }

    /// <summary>The operation key of the release that undid it.</summary>
    public string? ReleasedBy { get; set; }
}

public sealed class ReleaseRecord
{
    public required string OperationKey { get; init; }
    public string? TargetOperationKey { get; init; }
    public string? ReservationId { get; init; }
    public required string WorkflowExecutionId { get; init; }
    public ReleaseStatus Result { get; init; }
    public DateTimeOffset At { get; init; }
}

/// <summary>A target operation key released before its reservation arrived; a reservation with it is refused.</summary>
public sealed class TombstoneRecord
{
    public required string OperationKey { get; init; }
    public required string ReleasedBy { get; init; }
    public DateTimeOffset At { get; init; }
}

public enum ReservationStatus
{
    Reserved,
    Released
}

public enum ReserveStatus
{
    /// <summary>A new reservation.</summary>
    Reserved,

    /// <summary>The operation key had reserved already; the original reservation is returned.</summary>
    Repeated,

    /// <summary>The operation key was released before it arrived (a tombstone); nothing is reserved.</summary>
    Refused,

    /// <summary>The participant refused the request for business reasons; nothing is reserved.</summary>
    Rejected
}

public enum ReleaseStatus
{
    /// <summary>The reservation was undone.</summary>
    Released,

    /// <summary>Another release had undone it already.</summary>
    AlreadyReleased,

    /// <summary>No reservation had been made with the target key; a tombstone refuses it later.</summary>
    Tombstoned,

    /// <summary>This release's operation key was done already; nothing changed.</summary>
    Repeated,

    /// <summary>The call failed before any effect (<c>FailTimes</c>).</summary>
    Unavailable,

    /// <summary>The target made a different reservation than the one given, or none in this resource; nothing changed.</summary>
    Mismatch,

    /// <summary>Neither a target operation key nor a reservation was given; nothing changed.</summary>
    NoTarget
}

public sealed record ReserveResult(ReserveStatus Status, string? ReservationId);

public sealed record ReleaseResult(ReleaseStatus Status, string? ReservationId, int FailedAttempts);
