using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Workflows.Runtime.Core.UserTasks;
using MinimalEndpoints;
using Elsa.Workflows.Runtime.Api.Endpoints;

namespace Elsa.Workflows.Runtime.Api.UserTasks;

internal sealed record TargetRequest(int Skip = 0, int Take = 25, string? Search = null);
internal sealed record AssignmentTarget(string Id, string Name);
internal sealed record AssignmentTargets(IReadOnlyList<AssignmentTarget> Users, IReadOnlyList<AssignmentTarget> Groups, bool MoreUsers, bool MoreGroups);

[Get("runtime/user-task-targets")]
[RequireAnyPermission(UserTaskPermissions.Manage, UserTaskPermissions.FormsManage)]
[RuntimeProblems("processing user tasks")]
internal sealed class ReadAssignmentTargets(IUserTaskAccess access, IPagedUserStore users, IPagedRoleStore groups) : ApiEndpoint<TargetRequest, AssignmentTargets>
{
    public override void Configure(ApiEndpointOptions options) => options.Operation = "UserTaskAssignmentTargets";
    public override async Task<AssignmentTargets> HandleAsync(TargetRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.GetActorAsync(cancellationToken);
        if (!actor.CanManage && !actor.CanManageForms) throw new UserTaskException("forbidden", "Task authoring permission is required.");
        if (request.Take is < 1 or > 100 || request.Skip < 0) throw new UserTaskException("validation", "Choose a valid target page.");
        var page = new IamPageRequest(request.Skip, request.Take);
        var userPage = await users.ListPageAsync(actor.TenantId, page, request.Search, cancellationToken);
        var groupPage = await groups.ListPageAsync(actor.TenantId, page, cancellationToken);
        return new(userPage.Items.Where(x => x.TenantId == actor.TenantId && x.Status == UserStatus.Active).Select(x => new AssignmentTarget(x.Id, x.DisplayName ?? x.UserName)).ToArray(),
            groupPage.Items.Where(x => x.TenantId == actor.TenantId).Select(x => new AssignmentTarget(x.Id, x.Name)).ToArray(),
            request.Skip + request.Take < userPage.TotalCount, request.Skip + request.Take < groupPage.TotalCount);
    }
}
