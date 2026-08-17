using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Workflows.Publishing.Api.Authorization;

public sealed class WorkflowPublishingPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Publishing.Api";

    public IEnumerable<Permission> Contribute() =>
    [
        new(PermissionNames.WorkflowPublishingRead, "Read workflow publications", "Workflow publishing", "Read workflow publication state and validation results."),
        new(PermissionNames.WorkflowPublishingManage, "Manage workflow publications", "Workflow publishing", "Publish, retract, and test workflow versions.")
    ];
}
