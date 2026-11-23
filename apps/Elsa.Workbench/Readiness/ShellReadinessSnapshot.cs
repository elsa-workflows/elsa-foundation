namespace Elsa.Workbench.Readiness;

public sealed record ShellReadinessSnapshot(
    ShellReadinessStatus Status,
    string Code,
    string? ShellName = null,
    int Attempt = 0,
    int? Generation = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    TimeSpan? Duration = null);
