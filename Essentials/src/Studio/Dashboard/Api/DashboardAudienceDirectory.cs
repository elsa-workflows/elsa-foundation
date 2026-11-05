using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Studio.Dashboard.Core.Resources;

namespace Elsa.Studio.Dashboard.Api;

internal sealed class DashboardAudienceDirectory(IUserStore? users = null, IRoleStore? roles = null,
    IPagedUserStore? pagedUsers = null) : IDashboardAudienceDirectory
{
    public async ValueTask<bool> IsValidAsync(DashboardActor actor, DashboardAudienceGrant grant, CancellationToken cancellationToken = default)
    {
        if (grant.Kind == "user" && Allowed(actor, "identity/users:read") && users is not null)
            return await users.FindAsync(actor.TenantId, grant.Id, cancellationToken) is { Status: UserStatus.Active };
        if (grant.Kind == "role" && Allowed(actor, "identity/roles:read") && roles is not null)
            return await roles.FindAsync(actor.TenantId, grant.Id, cancellationToken) is not null;
        return false;
    }

    public async ValueTask<DashboardAudienceCandidatePage> SearchAsync(DashboardActor actor, string kind, string? search,
        CancellationToken cancellationToken = default)
    {
        if (search?.Length > 200) throw new DashboardResourceException("validation", "Audience search exceeds 200 characters.");
        if (kind == "user")
        {
            if (!Allowed(actor, "identity/users:read")) throw Denied();
            if (pagedUsers is null) throw Unavailable();
            var page = await pagedUsers.ListPageAsync(actor.TenantId, new(0, 100), search, cancellationToken);
            return new(page.Items.Where(user => user.Status == UserStatus.Active).Select(user =>
                new DashboardAudienceCandidate("user", user.Id, user.DisplayName ?? user.UserName)).ToArray());
        }
        if (kind == "role")
        {
            if (!Allowed(actor, "identity/roles:read")) throw Denied();
            if (roles is null) throw Unavailable();
            var found = await roles.ListAsync(actor.TenantId, cancellationToken);
            return new(found.Where(role => string.IsNullOrWhiteSpace(search) || role.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase).ThenBy(role => role.Id, StringComparer.Ordinal).Take(100)
                .Select(role => new DashboardAudienceCandidate("role", role.Id, role.Name)).ToArray());
        }
        throw new DashboardResourceException("validation", "Choose user or role audience discovery.");
    }

    private static bool Allowed(DashboardActor actor, string permission) => actor.Permissions.Contains(permission) || actor.Permissions.Contains("*");
    private static DashboardResourceException Denied() => new("denied", "Audience discovery requires the corresponding identity directory read permission.");
    private static DashboardResourceException Unavailable() => new("unavailable", "This identity provider does not support the requested audience discovery.");
}
