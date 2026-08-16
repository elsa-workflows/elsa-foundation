using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Diagnostics.StructuredLogs.Authorization;

/// <summary>Contributes the stable permission vocabulary owned by structured-log diagnostics.</summary>
public sealed class StructuredLogsPermissionContributor : IPermissionContributor
{
    public string OwnerId => StructuredLogsPermissions.OwnerId;

    public string ContributorType => typeof(StructuredLogsPermissionContributor).FullName!;

    public IEnumerable<Permission> Contribute() =>
    [
        new Permission(
            StructuredLogsPermissions.Read,
            "Read structured logs",
            "Diagnostics",
            "Read recent structured logs and live structured-log streams.")
        {
            OwnerId = OwnerId,
            ContributorType = ContributorType
        }
    ];
}
