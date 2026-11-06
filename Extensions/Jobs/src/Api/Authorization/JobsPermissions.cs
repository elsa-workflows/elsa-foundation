namespace Elsa.Jobs.Api.Authorization;

/// <summary>Permissions owned by the optional Jobs operations API.</summary>
public static class JobsPermissions
{
    public const string Read = "jobs:read";
    public const string AuditRead = "jobs/audit:read";
    public const string Submit = "jobs:submit";
    public const string InputsRead = "jobs/inputs:read";
    public const string ResultsRead = "jobs/results:read";
    public const string Pause = "jobs:pause";
    public const string Cancel = "jobs:cancel";
    public const string Retry = "jobs:retry";
    public const string SchedulesManage = "jobs/schedules:manage";
    public const string CapacityManage = "jobs/capacity:manage";

    internal static IReadOnlyList<string> All { get; } =
        [Read, AuditRead, Submit, InputsRead, ResultsRead, Pause, Cancel, Retry, SchedulesManage, CapacityManage];
}
