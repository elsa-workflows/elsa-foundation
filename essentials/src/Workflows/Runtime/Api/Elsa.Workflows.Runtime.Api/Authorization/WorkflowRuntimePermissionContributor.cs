using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Api.UserTasks;

namespace Elsa.Workflows.Runtime.Api.Authorization;

public sealed class WorkflowRuntimePermissionContributor : IPermissionContributor
{
    public string OwnerId => WorkflowsRuntimeApi.OwnerId;

    public IEnumerable<Permission> Contribute() =>
    [
        new(UserTaskPermissions.Read, "Read my tasks", "User Tasks", "Read assigned tasks and eligible group work."),
        new(UserTaskPermissions.Act, "Act on my tasks", "User Tasks", "Claim, release and complete assigned or eligible tasks."),
        new(UserTaskPermissions.Manage, "Manage user tasks", "User Tasks", "Inspect and reassign tenant work; completion still requires assignment."),
        new(UserTaskPermissions.FormsRead, "Read task forms", "User Tasks", "Use published reusable task form versions."),
        new(UserTaskPermissions.FormsManage, "Manage task forms", "User Tasks", "Create immutable reusable task form versions."),
        .. RetainedWorkflowRuntimePermissionContributor.RuntimePermissions()
    ];
}
