using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Diagnostics.OpenTelemetry.Permissions;

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
        },
        new Permission(
            OpenTelemetryPermissions.LegacyPolicy,
            "Legacy OpenTelemetry diagnostics access",
            "Diagnostics",
            "Compatibility alias for existing grants; use Diagnostics:OpenTelemetry.Read for new assignments.",
            new HashSet<string>(StringComparer.Ordinal) { OpenTelemetryPermissions.Read })
        {
            OwnerId = OwnerId,
            ContributorType = ContributorType
        }
    ];
}
