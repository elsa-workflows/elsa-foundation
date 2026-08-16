using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Workflows.Dashboard;

public static class WorkflowsDashboardPermissions
{
    public const string Read = "workflows.dashboard.read";
}

public sealed class WorkflowsDashboardPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Dashboard";

    public IEnumerable<Permission> Contribute() =>
    [
        new(WorkflowsDashboardPermissions.Read, "Read workflow dashboard", "Workflows", "Read tenant-scoped workflow dashboard statistics.")
    ];
}
