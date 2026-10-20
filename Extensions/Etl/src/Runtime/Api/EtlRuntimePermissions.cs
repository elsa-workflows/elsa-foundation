using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Etl.Runtime.Api;

/// <summary>Permissions required by the finite ETL runtime API.</summary>
public static class EtlRuntimePermissions
{
    public const string ReadRuns = "etl/runs:read";
    public const string CancelRuns = "etl/runs:cancel";
}

internal sealed class EtlRuntimePermissionContributor : IPermissionContributor
{
    public string OwnerId => EtlRuntimeApi.OwnerId;

    public IEnumerable<Permission> Contribute() =>
    [
        new(EtlRuntimePermissions.ReadRuns, "Read ETL runs", "ETL", "Read bounded ETL run and partition summaries."),
        new(EtlRuntimePermissions.CancelRuns, "Cancel ETL runs", "ETL", "Request cancellation of an ETL run.")
    ];
}
