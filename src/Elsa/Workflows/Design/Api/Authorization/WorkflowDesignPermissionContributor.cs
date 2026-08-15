using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Workflows.Design.Api.Authorization;

public sealed class WorkflowDesignPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Design.Api";

    public IEnumerable<Permission> Contribute() =>
    [
        new(PermissionNames.WorkflowDesignRead, "Read workflow designs", "Workflow design", "Read workflow definitions and their design metadata."),
        new(PermissionNames.WorkflowDesignManage, "Manage workflow designs", "Workflow design", "Create and change workflow definitions.")
    ];
}
