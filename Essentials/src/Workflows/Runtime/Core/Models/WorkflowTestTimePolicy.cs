namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Business-time policy frozen in the same trusted context as the selected test entry.</summary>
public sealed class WorkflowTestTimePolicy
{
    public WorkflowTestTimePolicy(WorkflowTestEntry entry, WorkflowTestTimeMode mode,
        string? clockId = null, DateTimeOffset? initialTime = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == WorkflowTestTimeMode.Controlled && (string.IsNullOrWhiteSpace(clockId) || initialTime is null))
            throw new ArgumentException("Controlled business time requires an exact clock and initial instant.");
        if (mode == WorkflowTestTimeMode.Wall && (clockId is not null || initialTime is not null))
            throw new ArgumentException("Wall business time cannot carry a controlled clock.");
        Entry = entry;
        Mode = mode;
        ClockId = clockId;
        InitialTime = initialTime;
    }

    public WorkflowTestEntry Entry { get; }
    public WorkflowTestTimeMode Mode { get; }
    public string? ClockId { get; }
    public DateTimeOffset? InitialTime { get; }

    public bool Matches(WorkflowTestTimePolicy? other) => other is not null && Entry.Matches(other.Entry) &&
        Mode == other.Mode && ClockId == other.ClockId && InitialTime == other.InitialTime;

    public bool MatchesClock(WorkflowBusinessTime time) => Mode == WorkflowTestTimeMode.Controlled &&
        time.ContextId == Entry.ContextId && time.ContextFingerprint == Entry.ContextFingerprint &&
        time.ClockId == ClockId && time.ClockRevision is > 0;
}

public enum WorkflowTestTimeMode
{
    Wall = 0,
    Controlled = 1
}

/// <summary>A business-clock read, including its exact admitted clock identity when controlled.</summary>
public sealed record WorkflowBusinessTime
{
    public WorkflowBusinessTime(DateTimeOffset instant, string? contextId = null,
        string? contextFingerprint = null, string? clockId = null, long? clockRevision = null)
    {
        var controlled = contextId is not null || contextFingerprint is not null || clockId is not null || clockRevision is not null;
        if (controlled && (string.IsNullOrWhiteSpace(contextId) || string.IsNullOrWhiteSpace(contextFingerprint) ||
                           string.IsNullOrWhiteSpace(clockId) || clockRevision is not > 0))
            throw new ArgumentException("A controlled time read requires complete context, clock and revision identity.");
        Instant = instant;
        ContextId = contextId;
        ContextFingerprint = contextFingerprint;
        ClockId = clockId;
        ClockRevision = clockRevision;
    }

    public DateTimeOffset Instant { get; }
    public string? ContextId { get; }
    public string? ContextFingerprint { get; }
    public string? ClockId { get; }
    public long? ClockRevision { get; }
    public bool IsControlled => ClockId is not null;
}
