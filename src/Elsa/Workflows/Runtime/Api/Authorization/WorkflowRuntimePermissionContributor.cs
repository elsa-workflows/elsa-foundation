using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Workflows.Runtime.Api.Authorization;

public sealed class WorkflowRuntimePermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Runtime.Api";

    public IEnumerable<Permission> Contribute() =>
    [
        new(WorkflowRuntimePermissions.WorkflowRuntimeRead, "Read workflow runtime", "Workflow runtime", "Read executable and workflow-instance runtime state."),
        new(WorkflowRuntimePermissions.WorkflowRuntimeExecute, "Execute workflows", "Workflow runtime", "Start and interact with workflow executions."),
        new(WorkflowRuntimePermissions.WorkflowRuntimeManage, "Manage workflow runtime", "Workflow runtime", "Alter, cancel, and administer workflow executions.")
    ];
}
