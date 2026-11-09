using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Etl.Runtime.Api;

/// <summary>Permissions required by the ETL runtime API.</summary>
public static class EtlRuntimePermissions
{
    public const string ReadRuns = "etl/runs:read";
    public const string CancelRuns = "etl/runs:cancel";
    public const string CompleteRuns = "etl/runs:complete";
}

internal sealed class EtlRuntimePermissionContributor : IPermissionContributor
{
    public string OwnerId => EtlRuntimeApi.OwnerId;

    public IEnumerable<Permission> Contribute() =>
    [
        new(EtlRuntimePermissions.ReadRuns, "Read ETL runs", "ETL", "Read bounded ETL run and partition summaries."),
        new(EtlRuntimePermissions.CancelRuns, "Cancel ETL runs", "ETL", "Request cancellation of an ETL run."),
        new(EtlRuntimePermissions.CompleteRuns, "Complete ETL runs", "ETL", "Request completion of a continuous ETL run.")
    ];
}
