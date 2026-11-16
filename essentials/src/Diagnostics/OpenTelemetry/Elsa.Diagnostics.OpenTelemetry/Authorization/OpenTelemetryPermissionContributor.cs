using Elsa.Diagnostics.OpenTelemetry.Permissions;
using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Diagnostics.OpenTelemetry.Authorization;

/// <summary>Contributes the query and live-stream permission owned by OpenTelemetry diagnostics.</summary>
public sealed class OpenTelemetryPermissionContributor : IPermissionContributor
{
    public string OwnerId => OpenTelemetryPermissions.OwnerId;

    public string ContributorType => typeof(OpenTelemetryPermissionContributor).FullName!;

    public IEnumerable<Permission> Contribute() =>
    [
        new Permission(
            OpenTelemetryPermissions.Read,
            "Read OpenTelemetry diagnostics",
            "Diagnostics",
            "Search OpenTelemetry data and subscribe to the live diagnostics stream.")
        {
            OwnerId = OwnerId,
            ContributorType = ContributorType
        }
    ];
}
