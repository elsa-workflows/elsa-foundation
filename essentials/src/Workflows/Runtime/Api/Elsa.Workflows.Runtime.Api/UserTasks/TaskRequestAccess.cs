using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.UserTasks;

internal static class UserTaskPermissions
{
    internal const string Read = "user-tasks:read";
    internal const string Act = "user-tasks:act";
    internal const string Manage = "user-tasks:manage";
    internal const string FormsRead = "user-task-forms:read";
    internal const string FormsManage = "user-task-forms:manage";
}

internal sealed class TaskRequestAccess(IHttpContextAccessor http, NormalizedPrincipalValidator? validator = null,
    IUserStore? users = null, IRoleStore? roles = null, ITenantMembershipStore? memberships = null, ICurrentSubjectPermissionAuthorizer? permissions = null) : IUserTaskAccess
{
    public async ValueTask<UserTaskActor> GetActorAsync(CancellationToken cancellationToken = default)
    {
        if (validator is null || users is null || roles is null || memberships is null || permissions is null)
            throw new UserTaskException("unavailable", "This host has no complete trusted task identity directory.");
        if (http.HttpContext is not { } context || !validator.TryGetNormalizedPrincipal(context.User, out var principal))
            throw new UserTaskException("unauthorized", "Sign in to view your tasks.");
        var subjects = principal.FindAll(IdentityClaimTypes.UserId).Select(x => x.Value).ToArray();
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).Select(x => x.Value).ToArray();
        if (subjects.Length != 1 || tenants.Length != 1 || string.IsNullOrWhiteSpace(subjects[0]) || string.IsNullOrWhiteSpace(tenants[0]))
            throw new UserTaskException("forbidden", "A canonical tenant user is required.");
        var tenant = tenants[0]; var subject = subjects[0];
        var user = await users.FindAsync(tenant, subject, cancellationToken);
        if (user is null || user.Id != subject || user.TenantId != tenant || user.Status != UserStatus.Active)
            throw new UserTaskException("forbidden", "The current user is unavailable.");
        var membership = await memberships.FindAsync(tenant, subject, cancellationToken);
        var candidates = user.RoleIds.Concat(membership?.Status == TenantMembershipStatus.Active ? membership.RoleIds : []);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in candidates.Distinct(StringComparer.Ordinal))
        {
            var role = await roles.FindAsync(tenant, id, cancellationToken);
            if (role is not null && role.Id == id && role.TenantId == tenant) groups.Add(id);
        }
        async ValueTask<bool> Has(string permission) =>
            (await permissions.AuthorizeAsync(new(tenant, subject, permission), cancellationToken)).Status == CurrentSubjectPermissionStatus.Granted;
        return new(tenant, subject, groups, await Has(UserTaskPermissions.Read), await Has(UserTaskPermissions.Act),
            await Has(UserTaskPermissions.Manage), await Has(UserTaskPermissions.FormsManage), await Has(UserTaskPermissions.FormsRead));
    }

    public async ValueTask AuthorizeAsync(UserTaskRecord task, string operation, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor.TenantId != task.TenantId) throw new UserTaskException("missing", "Task not found.");
        var permission = operation switch { "read" => UserTaskPermissions.Read, "act" => UserTaskPermissions.Act, "manage" => UserTaskPermissions.Manage, _ => throw new ArgumentException("Unknown task operation.") };
        var decision = await permissions!.AuthorizeAsync(new(actor.TenantId, actor.UserId, permission, task), cancellationToken);
        if (decision.Status != CurrentSubjectPermissionStatus.Granted)
            throw new UserTaskException(operation == "read" ? "missing" : "forbidden", operation == "read" ? "Task not found." : "This task operation is not permitted.");
    }

    public async ValueTask ValidateAssignmentAsync(string tenantId, string? assigneeId, string? groupId, CancellationToken cancellationToken = default)
    {
        if (users is null || roles is null) throw new UserTaskException("unavailable", "This host has no trusted task assignment directory.");
        if (string.IsNullOrWhiteSpace(assigneeId) == string.IsNullOrWhiteSpace(groupId))
            throw new UserTaskException("validation", "Choose exactly one active user or group.");
        if (assigneeId is not null)
        {
            var user = await users.FindAsync(tenantId, assigneeId, cancellationToken);
            if (user is null || user.TenantId != tenantId || user.Id != assigneeId || user.Status != UserStatus.Active)
                throw new UserTaskException("validation", "The selected user is unavailable.");
        }
        else
        {
            var role = await roles.FindAsync(tenantId, groupId!, cancellationToken);
            if (role is null || role.TenantId != tenantId || role.Id != groupId) throw new UserTaskException("validation", "The selected group is unavailable.");
        }
    }
}
