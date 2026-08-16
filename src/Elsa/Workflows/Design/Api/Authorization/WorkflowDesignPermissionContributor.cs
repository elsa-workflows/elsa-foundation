using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Workflows.Design.Api.Authorization;

public sealed class WorkflowDesignPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Design.Api";

    public string ContributorType => typeof(WorkflowDesignPermissionContributor).FullName!;

    public IEnumerable<Permission> Contribute() =>
    [
        new(WorkflowDesignPermissions.Read, "Read workflow designs", "Workflow design", "Read workflow definitions and their design metadata."),
        new(WorkflowDesignPermissions.Manage, "Manage workflow designs", "Workflow design", "Create and change workflow definitions.")
    ];
}
