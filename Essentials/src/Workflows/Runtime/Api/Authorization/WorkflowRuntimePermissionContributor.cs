using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.UserTasks;
using Elsa.Workflows.Runtime.Api.Services;

namespace Elsa.Workflows.Runtime.Api.Authorization;

public sealed class WorkflowRuntimePermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Workflows.Runtime.Api";

    public IEnumerable<Permission> Contribute() =>
    [
        new(UserTaskPermissions.Read, "Read my tasks", "User Tasks", "Read assigned tasks and eligible group work."),
        new(UserTaskPermissions.Act, "Act on my tasks", "User Tasks", "Claim, release and complete assigned or eligible tasks."),
        new(UserTaskPermissions.Manage, "Manage user tasks", "User Tasks", "Inspect and reassign tenant work; completion still requires assignment."),
        new(UserTaskPermissions.FormsRead, "Read task forms", "User Tasks", "Use published reusable task form versions."),
        new(UserTaskPermissions.FormsManage, "Manage task forms", "User Tasks", "Create immutable reusable task form versions."),
        new(WorkflowRuntimePermissions.WorkflowRuntimeRead, "Read workflow runtime", "Workflow runtime", "Read executable and workflow-instance runtime state."),
        new(WorkflowRuntimePermissions.WorkflowRuntimeExecute, "Execute workflows", "Workflow runtime", "Start and interact with workflow executions."),
        new(WorkflowRuntimePermissions.WorkflowRuntimeManage, "Manage workflow runtime", "Workflow runtime", "Alter, cancel, and administer workflow executions."),
        new(HttpContextActivityExecutionInspectionAuthorizationContext.StructurePermission, "Inspect activity executions", "Workflow runtime", "Inspect the structure of recorded activity executions."),
        new(HttpContextActivityExecutionInspectionAuthorizationContext.SensitiveValuesPermission, "Inspect activity execution values", "Workflow runtime", "Read the captured input and output values of activity executions."),
        new(HttpContextActivityExecutionInspectionAuthorizationContext.ResolveValuePayloadsPermission, "Resolve activity execution value payloads", "Workflow runtime", "Resolve the stored payloads behind captured activity execution values.")
    ];
}
